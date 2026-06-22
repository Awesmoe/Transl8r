using System.Drawing;
using Transl8r.Config;
using Transl8r.Translation;

namespace Transl8r.Ocr;

internal static class OcrBackendFactory
{
    /// <summary>
    /// Build the OCR processor for the configured backend. Supported in C#:
    /// vlm-direct (OCR+translate in one call) and vlm (OCR + separate
    /// translator). manga-ocr / paddle aren't ported yet → fall back to
    /// vlm-direct with a note. May throw if a chosen translator isn't supported
    /// (e.g. argos) — surfaced to the user via the pipeline's error event.
    /// </summary>
    public static IOcrProcessor Build(AppConfig cfg, out string? note)
    {
        note = null;
        switch (cfg.OcrBackend)
        {
            case "vlm-direct":
                return new DirectProcessor(new VlmDirectBackend(cfg));
            case "vlm":
                return new TranslateProcessor(new VlmOcrBackend(cfg), TranslatorFactory.Build(cfg));
            default:
                note = $"OCR backend '{cfg.OcrBackend}' isn't ported to C# yet; using vlm-direct.";
                return new DirectProcessor(new VlmDirectBackend(cfg));
        }
    }
}

/// <summary>Direct backend: OCR + translate in one call.</summary>
internal sealed class DirectProcessor : IOcrProcessor
{
    private readonly IDirectOcrBackend _backend;

    public DirectProcessor(IDirectOcrBackend backend) => _backend = backend;

    public (string Ja, string En) Process(Bitmap image) => _backend.RecognizeTranslate(image);
}

/// <summary>OCR backend + separate translator.</summary>
internal sealed class TranslateProcessor : IOcrProcessor
{
    private readonly IOcrBackend _backend;
    private readonly ITranslator _translator;

    public TranslateProcessor(IOcrBackend backend, ITranslator translator)
    {
        _backend = backend;
        _translator = translator;
    }

    public (string Ja, string En) Process(Bitmap image)
    {
        string ja = _backend.Recognize(image);
        if (string.IsNullOrEmpty(ja))
        {
            return ("", "");
        }
        return (ja, _translator.Translate(ja));
    }
}
