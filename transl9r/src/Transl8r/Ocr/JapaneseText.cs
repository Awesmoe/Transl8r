using System.Text.RegularExpressions;

namespace Transl8r.Ocr;

/// <summary>JA detection + defensive VLM-reply cleanup (HANDOVER #6).</summary>
internal static partial class JapaneseText
{
    // at least one hiragana/katakana/CJK char, else treat as garbage/no-text
    [GeneratedRegex("[\\u3040-\\u30ff\\u3400-\\u9fff\\uff66-\\uff9f]")]
    private static partial Regex JaRegex();

    // reasoning emitted by "thinking" model variants (also matches an unclosed
    // <think> when max_tokens cut the reply short)
    [GeneratedRegex(@"<think>.*?(?:</think>|$)", RegexOptions.Singleline)]
    private static partial Regex ThinkRegex();

    // first {...} block anywhere in the reply, preamble/code-fence tolerant
    [GeneratedRegex(@"\{.*\}", RegexOptions.Singleline)]
    public static partial Regex JsonRegex();

    public static bool LooksJapanese(string text) => JaRegex().IsMatch(text);

    public static string StripThink(string text) => ThinkRegex().Replace(text, "").Trim();
}
