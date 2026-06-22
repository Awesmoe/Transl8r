using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Transl8r.Config;
using Transl8r.Imaging;

namespace Transl8r.Ocr;

/// <summary>
/// OCR + translation in a single VLM call (HANDOVER #3). Extends VlmOcrBackend
/// for the shared HTTP machinery; adds the JSON {ja,en} prompt and defensive
/// reply parsing (HANDOVER #6).
/// </summary>
internal sealed class VlmDirectBackend : VlmOcrBackend, IDirectOcrBackend
{
    private const string DirectPrompt =
        "Read the Japanese text visible in this image and translate it to natural " +
        "English. Respond with ONLY a JSON object: {\"ja\": \"<exact Japanese " +
        "transcription>\", \"en\": \"<English translation>\"}. If there is no " +
        "legible Japanese text, respond with {\"ja\": \"\", \"en\": \"\"}.";

    public VlmDirectBackend(AppConfig cfg) : base(cfg)
    {
    }

    public (string Ja, string En) RecognizeTranslate(Bitmap image)
    {
        string raw = Ask(ImageOps.ToPngBase64(image), DirectPrompt, 1000, forceJson: true);

        var match = JapaneseText.JsonRegex().Match(raw);
        if (!match.Success)
        {
            if (!string.IsNullOrEmpty(raw))
            {
                string hint = raw.Contains('{') ? " (truncated? raise max_tokens)" : "";
                Debug.WriteLine($"[vlm-direct] no JSON in reply{hint}: {Trunc(raw, 300)}");
            }
            return ("", "");
        }

        string ja, en;
        try
        {
            using var doc = JsonDocument.Parse(match.Value);
            JsonElement root = doc.RootElement;
            ja = (root.TryGetProperty("ja", out var je) ? je.GetString() : "") ?? "";
            en = (root.TryGetProperty("en", out var ee) ? ee.GetString() : "") ?? "";
            ja = ja.Trim();
            en = en.Trim();
        }
        catch (JsonException)
        {
            Debug.WriteLine($"[vlm-direct] unparseable JSON (truncated? raise max_tokens): {Trunc(raw, 300)}");
            return ("", "");
        }

        if (ja.Length > 0 && !JapaneseText.LooksJapanese(ja))
        {
            Debug.WriteLine($"[vlm-direct] dropped non-Japanese 'ja': {Trunc(ja, 200)}");
            return ("", "");
        }
        if (ja.Length == 0)
        {
            return ("", "");
        }
        return (ja, en);
    }
}
