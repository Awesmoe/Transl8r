using System;
using System.Text.RegularExpressions;

namespace Transl8r.Audio;

/// <summary>
/// Post-filter for Whisper output. Even with VAD, a chunk that contains a little
/// real speech plus music/silence makes Whisper append one of its well-known
/// "filler" hallucinations (YouTube-caption training artifacts) or a sound-cue
/// tag. This strips sound tags (keeping any real text) and drops lines that are
/// purely a known hallucination phrase.
/// </summary>
internal static class HallucinationFilter
{
    // Phrases that are essentially never real game/anime dialogue. Matched
    // case-insensitively as substrings of the (tag-stripped) line.
    private static readonly string[] Phrases =
    {
        // English (Whisper translate task)
        "thank you for watching",
        "thanks for watching",
        "thank you for your watching",
        "please subscribe",
        "subscribe to my channel",
        "subscribe to the channel",
        "like and subscribe",
        "see you in the next video",
        // Japanese (transcribe task, before external translation)
        "ご視聴ありがとうございました",
        "ご視聴ありがとうございます",
        "ご清聴ありがとうございました",
        "チャンネル登録",
    };

    // [Music] (applause) 【...】 ♪ ♬ etc.
    private static readonly Regex SoundTags = new(
        @"[\[(（【][^\])）】]*[\])）】]|[♪♬♫]+", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Returns the cleaned line, or null if it should be dropped
    /// (empty, only a sound tag, or a known hallucination phrase).</summary>
    public static string? Filter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string cleaned = Whitespace.Replace(SoundTags.Replace(text, " "), " ").Trim();
        if (cleaned.Length == 0)
        {
            return null; // was only a sound cue like "[Music]"
        }

        string norm = cleaned.ToLowerInvariant();
        foreach (string p in Phrases)
        {
            if (norm.Contains(p, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        return cleaned;
    }
}
