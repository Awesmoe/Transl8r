using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace Transl8r.Audio;

/// <summary>
/// Resolves the local ggml model file for a config model name, downloading it
/// once (from the whisper.cpp HF repo) if missing. Models land in a "models"
/// folder next to the exe — *.bin is gitignored so they never get committed.
/// </summary>
internal static class WhisperModelStore
{
    public static string ModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>Maps the config's whisper_model string to a Whisper.net GgmlType.</summary>
    public static GgmlType MapType(string? name) => (name ?? "").Trim().ToLowerInvariant() switch
    {
        "tiny" => GgmlType.Tiny,
        "base" => GgmlType.Base,
        "small" => GgmlType.Small,
        "medium" => GgmlType.Medium,
        "large-v1" => GgmlType.LargeV1,
        "large-v2" => GgmlType.LargeV2,
        "large" or "large-v3" => GgmlType.LargeV3,
        _ => GgmlType.Small,
    };

    /// <summary>
    /// Returns the path to the model file, downloading it first if needed.
    /// <paramref name="status"/> is invoked with human-readable progress notes
    /// (download can be hundreds of MB the first time).
    /// </summary>
    public static async Task<string> EnsureAsync(
        string? modelName, Action<string>? status, CancellationToken ct = default)
    {
        GgmlType type = MapType(modelName);
        Directory.CreateDirectory(ModelsDir);
        string path = Path.Combine(ModelsDir, $"ggml-{type}.bin".ToLowerInvariant());

        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        status?.Invoke($"Downloading Whisper model '{type}' (one-time, this can take a while)…");
        using Stream src = await WhisperGgmlDownloader.Default
            .GetGgmlModelAsync(type, QuantizationType.NoQuantization, ct);

        // download to a .part file then move, so an interrupted download doesn't
        // leave a truncated file that looks valid next time
        string tmp = path + ".part";
        using (FileStream dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst, ct);
        }
        File.Move(tmp, path, overwrite: true);
        status?.Invoke($"Whisper model '{type}' ready.");
        return path;
    }
}
