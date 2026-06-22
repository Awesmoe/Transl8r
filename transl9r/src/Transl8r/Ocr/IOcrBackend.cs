using System.Drawing;

namespace Transl8r.Ocr;

/// <summary>OCR-only backend: returns recognized JA text ("" when none found).
/// Pair with an <see cref="Transl8r.Translation.ITranslator"/> for the English.</summary>
internal interface IOcrBackend
{
    string Recognize(Bitmap image);
}
