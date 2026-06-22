"""Translation backends. Each returns English text for Japanese input.

Backends:
  argos  — fully offline, downloads ja->en model (~100MB) on first use
  deepl  — DeepL API free tier, needs key in config
  server — any OpenAI-compatible /v1/chat/completions endpoint
           (llama.cpp server running Hy-MT2, Ollama with /v1, etc.)
"""

import requests


class TranslationError(Exception):
    pass


class ArgosTranslator:
    def __init__(self):
        import argostranslate.package
        import argostranslate.translate
        self._translate = argostranslate.translate

        installed = argostranslate.translate.get_installed_languages()
        codes = [lang.code for lang in installed]
        if "ja" not in codes or "en" not in codes:
            print("[translate] installing argos ja->en package (one-time, ~100MB)...")
            argostranslate.package.update_package_index()
            available = argostranslate.package.get_available_packages()
            pkg = next((p for p in available
                        if p.from_code == "ja" and p.to_code == "en"), None)
            if pkg is None:
                raise TranslationError("argos ja->en package not found in index")
            argostranslate.package.install_from_path(pkg.download())
            print("[translate] argos ja->en installed.")

    def translate(self, text: str) -> str:
        return self._translate.translate(text, "ja", "en")


class DeepLTranslator:
    URL = "https://api-free.deepl.com/v2/translate"

    def __init__(self, api_key: str):
        if not api_key:
            raise TranslationError("DeepL selected but no API key configured")
        self.api_key = api_key

    def translate(self, text: str) -> str:
        r = requests.post(
            self.URL,
            data={"auth_key": self.api_key, "text": text,
                  "source_lang": "JA", "target_lang": "EN"},
            timeout=15,
        )
        r.raise_for_status()
        return r.json()["translations"][0]["text"]


class ServerTranslator:
    """OpenAI-compatible chat endpoint (llama.cpp server, Ollama, vLLM...)."""

    SYSTEM = ("You are a translation engine. Translate the user's Japanese text "
              "into natural English. Output ONLY the translation, nothing else.")

    def __init__(self, base_url: str, model: str):
        self.url = base_url.rstrip("/") + "/v1/chat/completions"
        self.model = model

    def translate(self, text: str) -> str:
        r = requests.post(
            self.url,
            json={
                "model": self.model,
                "messages": [
                    {"role": "system", "content": self.SYSTEM},
                    {"role": "user", "content": text},
                ],
                "temperature": 0.1,
                "max_tokens": 512,
            },
            timeout=60,
        )
        r.raise_for_status()
        return r.json()["choices"][0]["message"]["content"].strip()


def build_translator(cfg: dict):
    kind = cfg.get("translator", "argos")
    if kind == "deepl":
        return DeepLTranslator(cfg.get("deepl_api_key", ""))
    if kind == "server":
        return ServerTranslator(cfg.get("server_url", "http://localhost:8080"),
                                cfg.get("server_model", "default"))
    return ArgosTranslator()
