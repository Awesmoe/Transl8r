using System.Drawing;

namespace Transl8r.Ocr;

/// <summary>
/// An OCR backend that returns OCR + translation in one call (HANDOVER #3).
/// Returns ("","") when there's no legible Japanese text.
/// </summary>
internal interface IDirectOcrBackend
{
    (string Ja, string En) RecognizeTranslate(Bitmap image);
}
