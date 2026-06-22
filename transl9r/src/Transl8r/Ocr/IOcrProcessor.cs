using System.Drawing;

namespace Transl8r.Ocr;

/// <summary>
/// Unifies the two OCR paths for the pipeline: a direct backend (OCR+translate in
/// one call) or an OCR backend plus a separate translator. Returns ("","") when
/// there's no legible Japanese text.
/// </summary>
internal interface IOcrProcessor
{
    (string Ja, string En) Process(Bitmap image);
}
