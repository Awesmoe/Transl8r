# transl9r — C# rewrite of transl8r

The C#/WPF rewrite. See `../CSHARP_REWRITE_PLAN.md` for the full plan, phases,
and rationale. (`transl8r` is the product name; `transl9r` is just this folder.)

## Status: Phase 0 ✅ done · Phase 1 ✅ done (validated on real Windows)

Phase 0 (tray + config) and Phase 1 (screen capture → vlm-direct → click-through
overlay) are both owner-validated on real Windows: JA text in the configured
region → English overlay below it, box clears when text leaves, clicks pass
through to the window behind. Phase 2 (region picker, edit mode, settings dialog,
the vlm/deepl/server backends) is next.

### Testing Phase 1

Prereqs: **Ollama running** with your VLM model, and a screen **region already in
config.json** (no picker until Phase 2 — hand-edit `regions` or reuse the
existing one). Then:

```sh
dotnet run --project src/Transl8r
```

- Tray → **Screen OCR** toggles the pipeline; **Show overlay** toggles the box;
  the `hotkey_overlay` combo (default ctrl+alt+o) toggles the overlay too.
- Put Japanese text inside the configured region → a translucent English overlay
  should appear just below the region and clear when the text leaves.

Caveats:
- Backend is **forced to vlm-direct** in Phase 1 (other backends are Phase 2).
- If the overlay stays empty, check the **num_ctx** of your model: vlm-direct
  needs room for an image + up to 1000 output tokens; `qwen3-vl-ocr`'s default
  `num_ctx 1024` may be too small → bump it (e.g. 4096) or wait for Phase 2's
  plain `vlm` + translator path. Run under a debugger to see `[vlm-direct]` drop
  reasons (they go to `Debug.WriteLine`).

## Prerequisites

- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
  (`winget install Microsoft.DotNet.SDK.8`)
- Windows 10/11 (the app is Windows-only by design).
- Optional: Visual Studio 2022 (17.8+) or JetBrains Rider — or just the CLI.

## Build & run

```sh
cd transl9r
dotnet build                 # restore + compile
dotnet run --project src/Transl8r
```

## What Phase 0 does

- Launches to a **system-tray icon** (placeholder icon), no main window.
- Enforces a **single instance** (second launch warns and exits).
- Loads `config.json` into a typed `AppConfig` (defaults if absent), and the tray
  **Settings…** item re-saves it and shows a few values — that's the round-trip
  smoke test. **Quit transl8r** exits cleanly.

### Testing the config round-trip against your real config

`config.json` is read from **next to the executable**
(`src/Transl8r/bin/Debug/net8.0-windows/`). To verify the existing schema loads:

1. Copy the repo-root `../config.json` into that output folder.
2. `dotnet run --project src/Transl8r`, then tray → **Settings…**.
3. It should report your real `ocr_backend`, region count, and `vlm_model`, and
   rewrite the file preserving every key (unknown keys are preserved too, via
   `[JsonExtensionData]`).

## Likely first-build snags (I couldn't compile to catch these)

- **WinForms/WPF type clashes** — mitigated by `ImplicitUsings=disable` + a
  `WinForms` alias, but watch for any `Application`/`MessageBox` ambiguity.
- **`JsonNamingPolicy.SnakeCaseLower`** requires the .NET 8 SDK (added in 8.0). On
  an older SDK it won't resolve.
- **App.xaml as ApplicationDefinition** — the WPF SDK should auto-detect it; if
  the generated `Main`/`InitializeComponent` isn't found, that's the cause.

## Mapping to the Python project

| C# (here) | Python (../transl8r) |
| --- | --- |
| `Config/AppConfig.cs` | `config.py` |
| `App.xaml(.cs)` | `app.py` (tray + lifecycle only, so far) |
| _next phases_ | `ocr_backends.py`, `overlay.py`, `workers.py`, … |
