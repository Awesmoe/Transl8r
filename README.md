# transl8r

Real-time **Japanese → English translation overlay** for games and other apps on
Windows. Point it at a region of your screen (or let it listen to system audio);
it OCRs/transcribes the Japanese and draws the English in a translucent,
always-on-top, click-through overlay.

This repo holds two implementations:

| Folder | Stack | Status |
| --- | --- | --- |
| [`transl9r/`](transl9r) | **C# / WPF (.NET 8)** | **Active.** Screen OCR + system-audio pipelines, validated on Windows. |
| [`legacy-python/`](legacy-python) | Python / PySide6 | Deprecated prototype. Still runs; only build with TTS wired up. |

The product is "transl8r" throughout; `transl9r` is just the rewrite folder's
pun-y name.

## Quick start (C#)

```sh
cd transl9r
dotnet build
dotnet run --project src/Transl8r
```

It starts in the system tray (no main window). See
[`transl9r/README.md`](transl9r/README.md) for backends, hotkeys, configuration,
and troubleshooting.

## Features

- **Screen OCR → overlay** — one or more regions; overlays appear beneath each
  region and clear when the text leaves. Excluded from screen capture by default
  so they don't get OCR'd back in (toggle in Settings).
- **System audio → overlay** — WASAPI loopback → Whisper, as a rolling subtitle
  log. Either let Whisper translate straight to English, or transcribe Japanese
  and route it through a separate translation backend.
- **Pluggable backends** (OpenAI-compatible HTTP): `vlm-direct` (OCR +
  translation in one vision-model call), `vlm` + a translator, DeepL, or any
  OpenAI-compatible server (llama.cpp, Ollama, vLLM, …).
- **Edit mode** to drag overlays; offsets persist per region. **Global hotkeys**,
  **system-tray control**, **tabbed settings**, optional original-JA display, and
  file output.

Planned: TTS output (currently only in the Python prototype).

## Requirements

- **.NET 8 SDK** and **Windows 10 (build 2004+) / 11** — Windows-only by design
  (topmost click-through overlays, WASAPI loopback, global hotkeys, system tray).
- A backend — e.g. [Ollama](https://ollama.com) running a vision model, a DeepL
  API key, or an OpenAI-compatible server.
