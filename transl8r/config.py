"""Config: defaults + JSON persistence next to the package."""

import json
from pathlib import Path

CONFIG_PATH = Path(__file__).resolve().parent.parent / "config.json"

DEFAULTS = {
    # screen OCR pipeline
    "ocr_enabled": True,
    "regions": [],                 # list of {"left","top","width","height"} physical px
    "poll_interval": 0.5,          # seconds
    "frame_change_ratio": 0.01,    # fraction of pixels that must move (>24
                                   # levels) before a frame counts as changed;
                                   # raise to ignore animated textboxes, lower
                                   # to catch tiny text changes
    "ocr_backend": "manga-ocr",    # manga-ocr | paddle | vlm | vlm-direct
    "paddle_min_confidence": 0.75, # drop PaddleOCR lines below this score
    "vlm_url": "http://localhost:11434",   # OpenAI-compatible vision endpoint
    "vlm_model": "qwen3-vl:4b",    # 2b/4b fit comfortably next to a game

    # audio pipeline
    "audio_enabled": False,
    "whisper_model": "small",      # tiny/base/small/medium/large-v3
    "whisper_device": "auto",      # auto/cuda/cpu
    "audio_use_translator": False, # False = whisper task=translate,
                                   # True = transcribe ja, then text translator

    # translation backend
    "translator": "argos",         # argos | deepl | server
    "deepl_api_key": "",
    "server_url": "http://localhost:8080",  # OpenAI-compatible (llama.cpp server)
    "server_model": "hy-mt2",

    # outputs
    "output_overlay": True,
    "output_tts": False,
    "output_file": False,
    "output_file_path": "transl8r_log.txt",
    "show_original": False,

    # TTS (kokoro-onnx) — download model files yourself, see README
    "tts_model_path": "kokoro-v1.0.onnx",
    "tts_voices_path": "voices-v1.0.bin",
    "tts_voice": "af_sarah",

    # global hotkeys (Windows only; empty string disables)
    "hotkey_region": "ctrl+alt+r",
    "hotkey_overlay": "ctrl+alt+o",
    "hotkey_edit": "ctrl+alt+e",    # toggle draggable overlay edit mode

    # overlay appearance
    "overlay_font_size": 18,
    "overlay_opacity": 0.85,

    # overlay placement nudges (logical px), set by dragging in edit mode.
    # overlay_offsets is parallel to `regions` (offset from each region's
    # below-region base); audio_overlay_offset is from the bottom-center base.
    "overlay_offsets": [],
    "audio_overlay_offset": [0, 0],
}


def load() -> dict:
    cfg = dict(DEFAULTS)
    if CONFIG_PATH.exists():
        try:
            cfg.update(json.loads(CONFIG_PATH.read_text(encoding="utf-8")))
        except (json.JSONDecodeError, OSError):
            pass
    # migrate pre-multi-region configs
    old = cfg.pop("region", None)
    if old and not cfg.get("regions"):
        cfg["regions"] = [old]
    return cfg


def save(cfg: dict):
    CONFIG_PATH.write_text(json.dumps(cfg, indent=2, ensure_ascii=False),
                           encoding="utf-8")
