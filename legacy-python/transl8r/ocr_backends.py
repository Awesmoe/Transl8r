"""Pluggable OCR backends. Each takes a PIL image, returns recognized JA text
("" when no text found — backends should prefer empty over guessing).

  manga-ocr — original backend; fast, but generative decoder hallucinates on
              empty frames and out-of-distribution fonts
  paddle    — PaddleOCR det+rec pipeline (lang='japan'); detector returns
              nothing on textless frames, per-line confidence is thresholded
  vlm       — any OpenAI-compatible vision endpoint (Ollama qwen2.5vl,
              llama.cpp multimodal, vLLM); best on stylized/decorative fonts
"""

import base64
import io
import re

import requests

# at least one hiragana/katakana/CJK char, else treat as garbage/no-text
_JA_RE = re.compile(r"[\u3040-\u30ff\u3400-\u9fff\uff66-\uff9f]")
# reasoning emitted by "thinking" model variants
_THINK_RE = re.compile(r"<think>.*?(?:</think>|$)", re.DOTALL)
# first {...} block anywhere in the reply, preamble-tolerant
_JSON_RE = re.compile(r"\{.*\}", re.DOTALL)


def looks_japanese(text: str) -> bool:
    return bool(_JA_RE.search(text))


class MangaOcrBackend:
    def __init__(self, cfg: dict):
        from manga_ocr import MangaOcr
        self.mocr = MangaOcr()

    def recognize(self, img) -> str:
        return self.mocr(img).strip()


class PaddleOcrBackend:
    def __init__(self, cfg: dict):
        from paddleocr import PaddleOCR
        self.min_conf = float(cfg.get("paddle_min_confidence", 0.75))
        try:  # PaddleOCR 3.x
            self.ocr = PaddleOCR(lang="japan",
                                 use_doc_orientation_classify=False,
                                 use_doc_unwarping=False,
                                 use_textline_orientation=False)
            self._v3 = True
        except TypeError:  # 2.x signature
            self.ocr = PaddleOCR(lang="japan", show_log=False)
            self._v3 = False

    def recognize(self, img) -> str:
        import numpy as np
        arr = np.array(img.convert("RGB"))
        lines: list[str] = []
        if self._v3:
            for res in self.ocr.predict(arr) or []:
                texts = res.get("rec_texts", []) if isinstance(res, dict) else \
                    getattr(res, "rec_texts", [])
                scores = res.get("rec_scores", []) if isinstance(res, dict) else \
                    getattr(res, "rec_scores", [])
                for t, s in zip(texts, scores):
                    if s >= self.min_conf:
                        lines.append(t)
        else:
            result = self.ocr.ocr(arr, cls=False)
            for page in result or []:
                for entry in page or []:
                    text, conf = entry[1]
                    if conf >= self.min_conf:
                        lines.append(text)
        return "".join(lines).strip()


def _shrink(img, max_side: int = 1024):
    """Cap longest side; vision encoder cost/VRAM scales with resolution and
    a textbox crop stays legible well below native res."""
    w, h = img.size
    if max(w, h) <= max_side:
        return img
    scale = max_side / max(w, h)
    return img.resize((int(w * scale), int(h * scale)))


class VlmOcrBackend:
    """OpenAI-compatible /v1/chat/completions with image input.

    Works with: Ollama (e.g. `ollama pull qwen3-vl:4b`, url
    http://localhost:11434), llama.cpp server with an mmproj, vLLM.
    """

    PROMPT = ("Transcribe the Japanese text visible in this image, exactly as "
              "written, preserving the original Japanese. Output ONLY the "
              "transcribed text. If there is no legible Japanese text, output "
              "an empty response.")

    def __init__(self, cfg: dict):
        self.url = cfg.get("vlm_url", "http://localhost:11434").rstrip("/") \
            + "/v1/chat/completions"
        self.model = cfg.get("vlm_model", "qwen3-vl:4b")
        self._json_mode_ok = True   # disabled on first server rejection
        self._extras_ok = True      # reasoning_effort support, ditto

    def _ask(self, img, prompt: str, max_tokens: int = 300,
             force_json: bool = False) -> str:
        buf = io.BytesIO()
        _shrink(img).save(buf, format="PNG")
        b64 = base64.b64encode(buf.getvalue()).decode("ascii")
        payload = {
            "model": self.model,
            "messages": [{
                "role": "user",
                "content": [
                    {"type": "image_url",
                     "image_url": {"url": f"data:image/png;base64,{b64}"}},
                    {"type": "text", "text": prompt},
                ],
            }],
            "temperature": 0.0,
            "max_tokens": max_tokens,
        }
        if self._extras_ok:
            # Ollama auto-enables thinking on capable models unless told not
            # to; over /v1 the control field is reasoning_effort, not think
            payload["reasoning_effort"] = "none"
        if force_json and self._json_mode_ok:
            payload["response_format"] = {"type": "json_object"}
        r = requests.post(self.url, json=payload, timeout=(5, 30))
        if r.status_code == 400 and (self._extras_ok or
                                     (force_json and self._json_mode_ok)):
            # server rejected an optional field — remember and retry plain
            self._extras_ok = False
            self._json_mode_ok = False
            payload.pop("reasoning_effort", None)
            payload.pop("response_format", None)
            r = requests.post(self.url, json=payload, timeout=(5, 30))
        r.raise_for_status()
        text = r.json()["choices"][0]["message"]["content"] or ""
        # thinking-variant models reason before answering; strip it (also
        # handles an unclosed <think> when max_tokens cut the reply short)
        return _THINK_RE.sub("", text).strip()

    def recognize(self, img) -> str:
        text = self._ask(img, self.PROMPT)
        # VLMs sometimes narrate instead of staying silent — if the reply
        # contains no Japanese at all, treat it as "no text found"
        if text and not looks_japanese(text):
            print(f"[vlm] dropped non-Japanese reply: {text[:200]!r}")
            return ""
        return text


class VlmDirectBackend(VlmOcrBackend):
    """OCR + translation in a single VLM call. One GPU-resident model instead
    of two; the model also sees the scene, which can help translation."""

    PROMPT = ('Read the Japanese text visible in this image and translate it '
              'to natural English. Respond with ONLY a JSON object: '
              '{"ja": "<exact Japanese transcription>", "en": "<English '
              'translation>"}. If there is no legible Japanese text, respond '
              'with {"ja": "", "en": ""}.')

    def recognize_translate(self, img) -> tuple[str, str]:
        import json
        raw = self._ask(img, self.PROMPT, max_tokens=1000, force_json=True)
        m = _JSON_RE.search(raw)  # tolerate preambles and code fences
        if not m:
            if raw:
                hint = " (truncated? raise max_tokens)" if "{" in raw else ""
                print(f"[vlm-direct] no JSON in reply{hint}: {raw[:300]!r}")
            return "", ""
        try:
            data = json.loads(m.group(0))
            ja = str(data.get("ja", "")).strip()
            en = str(data.get("en", "")).strip()
        except (json.JSONDecodeError, AttributeError):
            print(f"[vlm-direct] unparseable JSON "
                  f"(truncated reply? raise max_tokens): {raw[:300]!r}")
            return "", ""
        if ja and not looks_japanese(ja):
            print(f"[vlm-direct] dropped non-Japanese 'ja': {ja[:200]!r}")
            return "", ""
        if not ja:
            return "", ""
        return ja, en


BACKENDS = {
    "manga-ocr": MangaOcrBackend,
    "paddle": PaddleOcrBackend,
    "vlm": VlmOcrBackend,
    "vlm-direct": VlmDirectBackend,
}


def build_ocr_backend(cfg: dict):
    kind = cfg.get("ocr_backend", "manga-ocr")
    return BACKENDS.get(kind, MangaOcrBackend)(cfg)
