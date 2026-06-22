# transl8r (Python prototype — deprecated)

> **Deprecated.** This Python/PySide6 version is the original prototype, kept
> here because it still runs and remains the only build with TTS wired up. Active
> development has moved to the C# / WPF rewrite in [`../transl9r`](../transl9r).
> Run the commands below from inside `legacy-python/`.

Desktop translation overlay for Japanese games and audio. Screen OCR
(pluggable backends, manga-ocr to vision LLMs) and system-audio speech
recognition (faster-whisper) feed a shared translation + output layer:
on-screen overlay, TTS, or text file.

## Setup

```
cd legacy-python
pip install -r requirements.txt
python -m transl8r
```

The app lives in the system tray (look for the 訳 icon). First launch with
OCR enabled asks you to drag-select the game's text box region.

Models download on first use: manga-ocr ~400MB, argos ja→en ~100MB,
whisper depends on size choice. All cached afterward.

## Pipelines

**Screen OCR** — polls the selected region (default 500ms), skips unchanged
frames via pixel-similarity change detection (a frame only counts as changed
if enough pixels actually moved, so a blinking "next" arrow doesn't
retrigger), debounces until the frame is stable (skips typewriter
animation), runs the selected OCR backend, drops results containing no
Japanese characters, translates, routes to outputs.

OCR backends (Settings → Input):
- **manga-ocr** — fast, but its generative decoder hallucinates on empty
  frames and unusual fonts
- **paddle** — PaddleOCR det+rec (`pip install paddleocr paddlepaddle-gpu`,
  or `paddlepaddle` for CPU); detector stays silent on textless frames,
  lines below the confidence threshold are dropped
- **vlm** — any OpenAI-compatible vision endpoint; best for stylized indie
  game fonts. e.g. `ollama pull qwen3-vl:4b-instruct` (use an `-instruct`
  tag — thinking editions burn the budget on reasoning and return nothing),
  URL `http://localhost:11434`
- **vlm-direct** — like vlm, but OCR + translation happen in one JSON call,
  so a single model stays in VRAM and no separate text translator is loaded.
  Best fit when GPU memory is contested (game + model at once)
Re-pick the region anytime from the tray menu (or ctrl+alt+r); OCR pauses
during selection so it doesn't read its own dimming overlay.

**Multiple regions**: "Add screen region" in the tray appends additional
capture areas — each gets its own overlay placed below it and its own
change detection, OCR'd round-robin in one worker. Useful for games with
several fixed text areas (speech bubbles, status lines); smaller crops are
also much faster for VLM backends than one big region. "Select screen
region..." resets back to a single region. Overlays auto-clear when their
region's text disappears.

**Audio** (Windows only) — captures system output via WASAPI loopback.
Tries sounddevice first (works if your bundled PortAudio lists devices
ending in `[Loopback]`); otherwise falls back to the `soundcard` library
automatically, which does loopback through its own bindings. Console shows
which path and device it's listening to. Chunks audio on trailing silence (min 2s, max 8s), runs
faster-whisper with `task=translate`. Optionally transcribe JA instead and
push through the text translator (Settings → "Audio translation") — useful
if you're running a dedicated MT model.

## Translation backends

- **argos** — offline, zero config, default
- **deepl** — free API tier, paste key in Settings
- **server** — any OpenAI-compatible endpoint. For Hy-MT2 via llama.cpp:
  `llama-server -m hy-mt2.gguf --port 8080`, set URL to
  `http://localhost:8080`. Ollama works too (`http://localhost:11434`).

## Outputs

- **Overlay** — frameless, always-on-top, click-through; sits just below
  the capture region (bottom-center if audio-only). Toggle original JA
  text display in Settings. Edit mode (`ctrl+alt+e` or tray menu) makes
  the boxes draggable; a dragged position is stored as an offset, persists
  across restarts, and follows its region if you re-pick it.
- **TTS** — optional, via kokoro-onnx. `pip install kokoro-onnx` and
  download `kokoro-v1.0.onnx` + `voices-v1.0.bin` from the kokoro-onnx
  releases page; set paths in Settings.
- **File** — appends timestamped lines.

Console always gets a copy.

## Files

```
transl8r/
  app.py           tray app, worker lifecycle, wiring
  workers.py       OcrWorker + AudioWorker (QThreads)
  ocr_backends.py  manga-ocr / paddle / vlm / vlm-direct backends
  translate.py     argos / deepl / server backends
  overlay.py       click-through overlay window
  region.py        drag-select region picker (DPI-aware)
  settings.py      settings dialog
  outputs.py       output router + TTS engine
  hotkeys.py       Win32 global hotkeys
  config.py        defaults + config.json persistence
```

## Hotkeys (Windows)

Global hotkeys, editable in Settings -> Input: `ctrl+alt+r` re-picks the
screen region, `ctrl+alt+o` toggles the overlay, `ctrl+alt+e` toggles
overlay edit mode. Format: modifiers
(ctrl/alt/shift/win) + a letter, digit, or F-key. Empty string disables.

## Known constraints

- Audio pipeline is Windows-only (WASAPI loopback).
- Fullscreen-exclusive games can hide the overlay — use borderless
  windowed mode.
- Region coordinates are physical pixels; if you change display scaling,
  re-pick the region.
