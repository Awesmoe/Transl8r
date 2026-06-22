# transl9r

A real-time **Japanese → English translation overlay** for games and other apps
on Windows. Point it at a region of your screen; it OCRs the Japanese text with a
vision model and draws the English translation in a click-through overlay on top.

This is a native **C# / WPF (.NET 8)** rewrite of the original Python/PySide6
`transl8r`. (The product is still "transl8r"; `transl9r` is just the rewrite
folder — the deprecated Python prototype now lives in [`../legacy-python`](../legacy-python).)

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

- **System audio → overlay** — WASAPI loopback → Whisper as a rolling subtitle
  log; either Whisper-direct English, or transcribe JA and route through a
  separate translator. Optional Silero VAD to drop non-speech.

Planned: TTS output.

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

## Troubleshooting

**The model outputs gibberish (random JSON, repeated tokens, ignores your text).**
If you pulled a model straight from a GGUF (`ollama run hf.co/<repo>:<quant>`),
Ollama may have corrupted the chat template while auto-converting it from the
GGUF's Jinja format to its own Go format — the model then never actually receives
your prompt. Check it:

```sh
ollama show --modelfile <model>
```

If the `TEMPLATE` is missing `{{ .Prompt }}` (or `{{ .Response }}` looks
truncated), that's the bug. The same model usually works fine under `llama.cpp`,
which uses the Jinja template directly — it's specifically Ollama's converter that
trips on non-trivial templates.

Fix it by rebuilding the model with a correct template (this reuses the existing
weights — no re-download). Create a `Modelfile`:

```
FROM <model>
TEMPLATE """<the model's correct template, with {{ .Prompt }} and {{ .Response }} restored>"""
```

then `ollama create <model> -f Modelfile`. The authoritative template is the
`tokenizer.chat_template` string embedded in the GGUF itself; translate its Jinja
to Ollama's Go syntax. (This was hit with Tencent's `Hy-MT2-1.8B-GGUF`.)

## Status

Screen-OCR and system-audio pipelines are complete and validated on real Windows.
TTS output is not yet implemented (still only in the Python prototype).
