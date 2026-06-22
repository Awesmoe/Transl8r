using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transl8r.Config;

/// <summary>A captured screen region, in PHYSICAL pixels (HANDOVER #8).</summary>
public sealed class Region
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>
/// Strongly-typed mirror of the Python <c>config.json</c> schema. Property names
/// are PascalCase here and mapped to the existing snake_case keys via
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/>, so an existing config.json is a
/// drop-in. Defaults mirror Python's <c>config.py</c> DEFAULTS.
/// </summary>
public sealed class AppConfig
{
    // --- screen OCR pipeline ------------------------------------------------
    public bool OcrEnabled { get; set; } = true;
    public List<Region> Regions { get; set; } = new();
    public double PollInterval { get; set; } = 0.5;
    public double FrameChangeRatio { get; set; } = 0.01;
    public string OcrBackend { get; set; } = "manga-ocr";
    public double PaddleMinConfidence { get; set; } = 0.75;
    public string VlmUrl { get; set; } = "http://localhost:11434";
    public string VlmModel { get; set; } = "qwen3-vl:4b";

    // --- audio pipeline -----------------------------------------------------
    public bool AudioEnabled { get; set; } = false;
    public string WhisperModel { get; set; } = "small";
    public string WhisperDevice { get; set; } = "auto";
    public bool AudioUseTranslator { get; set; } = false;
    // Silero VAD: skip chunks with no detected speech, so Whisper can't
    // hallucinate captions ("thank you for watching") on music/silence.
    public bool AudioVad { get; set; } = true;

    // --- translation backend ------------------------------------------------
    public string Translator { get; set; } = "argos";
    public string DeeplApiKey { get; set; } = "";
    public string ServerUrl { get; set; } = "http://localhost:8080";
    public string ServerModel { get; set; } = "hy-mt2";

    // --- outputs ------------------------------------------------------------
    public bool OutputOverlay { get; set; } = true;
    public bool OutputTts { get; set; } = false;
    public bool OutputFile { get; set; } = false;
    public string OutputFilePath { get; set; } = "transl8r_log.txt";
    public bool ShowOriginal { get; set; } = false;

    // --- TTS (kokoro-onnx; deferred in the C# rewrite) ----------------------
    public string TtsModelPath { get; set; } = "kokoro-v1.0.onnx";
    public string TtsVoicesPath { get; set; } = "voices-v1.0.bin";
    public string TtsVoice { get; set; } = "af_sarah";

    // --- global hotkeys (empty string disables) -----------------------------
    public string HotkeyRegion { get; set; } = "ctrl+alt+r";
    public string HotkeyAdd { get; set; } = "ctrl+alt+a";
    public string HotkeyOverlay { get; set; } = "ctrl+alt+o";
    public string HotkeyEdit { get; set; } = "ctrl+alt+e";

    // --- overlay appearance -------------------------------------------------
    public int OverlayFontSize { get; set; } = 18;        // translation (EN)
    public int OverlayOrigFontSize { get; set; } = 12;    // original (JA), when shown
    public double OverlayOpacity { get; set; } = 0.85;

    // Exclude the overlay windows from screen capture (SetWindowDisplayAffinity /
    // WDA_EXCLUDEFROMCAPTURE). Stops one region's OCR from reading another
    // overlay's on-screen text and re-translating it (the self-capture feedback
    // loop). Default on; turn off to make the overlays show up in screenshots /
    // screen recordings. Needs Windows 10 build 2004+ (Win11 has it).
    public bool ExcludeOverlayFromCapture { get; set; } = true;

    // Audio rolling-log overlay: how long a line stays before it expires (0 =
    // keep until it scrolls off the top), and how tall the box may grow before
    // oldest lines are dropped, as a percent of the work-area height.
    public double AudioMessageSeconds { get; set; } = 0;
    public int AudioOverlayMaxHeightPercent { get; set; } = 40;

    // Placement nudges (logical px) set by dragging in edit mode. OverlayOffsets
    // is PARALLEL to Regions; AudioOverlayOffset is from the bottom-center base.
    public List<int[]> OverlayOffsets { get; set; } = new();
    public int[] AudioOverlayOffset { get; set; } = new[] { 0, 0 };

    // Preserve any keys we don't model yet, so a save never drops unknown
    // fields written by a newer/older build (round-trip safety).
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Extra { get; set; } = new();

    // ------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        // Match Python's ensure_ascii=False so any non-ASCII stays readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>config.json lives next to the executable (dev convention; switch
    /// to %APPDATA% when we package — see HANDOVER's packaging notes).</summary>
    public static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg != null)
                {
                    cfg.Migrate();
                    return cfg;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Missing / unreadable / corrupt -> fall back to defaults.
        }
        return new AppConfig();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Deep copy via JSON round-trip (preserves regions, offsets, and
    /// any unmodeled Extra keys). Used so the settings dialog edits a copy.</summary>
    public AppConfig Clone()
    {
        string json = JsonSerializer.Serialize(this, JsonOpts);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts)!;
    }

    /// <summary>Migrate a pre-multi-region config: a single "region" object
    /// becomes the first entry of "regions" (mirrors Python's config.load()).</summary>
    private void Migrate()
    {
        if (Regions.Count == 0 &&
            Extra.TryGetValue("region", out var old) &&
            old.ValueKind == JsonValueKind.Object)
        {
            try
            {
                var r = old.Deserialize<Region>(JsonOpts);
                if (r != null)
                {
                    Regions.Add(r);
                }
            }
            catch (JsonException)
            {
                // ignore a malformed legacy region
            }
            Extra.Remove("region");
        }
    }
}
