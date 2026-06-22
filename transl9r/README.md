# transl9r

A real-time **Japanese → English translation overlay** for games and other apps
on Windows. Point it at a region of your screen; it OCRs the Japanese text with a
vision model and draws the English translation in a click-through overlay on top.

This is a native **C# / WPF (.NET 8)** rewrite of the original Python/PySide6
`transl8r`. (The product is still "transl8r"; `transl9r` is just the rewrite
folder — the Python version lives on the `main` branch.)

## Features

- **Screen OCR → overlay** — capture one or more screen regions; translucent,
  always-on-top, click-through overlays show the translation beneath each region,
  and clear when the text leaves.
- **Drag-to-select region picker** — multi-region (add or replace), with existing
  regions outlined while you draw.
- **Edit mode** — drag overlays to reposition; offsets are saved per region.
- **Pluggable backends** (OpenAI-compatible HTTP):
  - `vlm-direct` — OCR **and** translation in a single vision-model call (one
    model resident in VRAM; ideal with Ollama).
  - `vlm` — OCR only, paired with a translator.
  - Translators: **DeepL** (free or pro), or any **OpenAI-compatible server**
    (llama.cpp, Ollama, vLLM, …).
- **Optional original text** — show the source Japanese above the translation,
  with its own independent font size.
- **System-tray control**, **global hotkeys**, and a **tabbed settings dialog**.

Planned: audio pipeline (system audio → Whisper) and TTS output.

## Requirements

- **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>
- **Windows 10/11** — Windows-only by design (topmost click-through overlays,
  global hotkeys, system tray).
- A backend — e.g. **[Ollama](https://ollama.com)** running a vision model for OCR
  (`vlm-direct` needs only that one model). For separate translation: a DeepL API
  key, or an OpenAI-compatible server.

## Build & run

```sh
cd transl9r
dotnet build
dotnet run --project src/Transl8r
```

The app starts in the system tray (no main window).

## Usage

1. **Pick a region** — tray → *Select screen region…* (or `Ctrl+Alt+R`) and drag a
   box over the game's text area. *Add screen region* (`Ctrl+Alt+A`) adds more.
2. **Choose a backend** — tray → *Settings…* (Input + Translation tabs). Set the
   VLM URL/model (e.g. Ollama at `http://localhost:11434`); if using `vlm`, pick a
   translator.
3. **Read** — Japanese text in a region → the English overlay appears just below
   it.
4. **Reposition** — tray → *Edit overlay positions* (or `Ctrl+Alt+E`), drag, then
   toggle off. Toggle the overlay with `Ctrl+Alt+O`.

Default hotkeys (all rebindable in Settings; you're notified if a combo is already
taken by another app): pick region `Ctrl+Alt+R`, add region `Ctrl+Alt+A`, toggle
overlay `Ctrl+Alt+O`, edit positions `Ctrl+Alt+E`.

## Configuration

Settings live in `config.json` next to the executable (schema shared with the
Python version). Your real `config.json` is gitignored — keep API keys out of
source control.

## Status

The screen-OCR feature set is complete and validated on real Windows. Audio input
and TTS are not yet implemented.
