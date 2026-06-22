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
    public static Task<string> EnsureAsync(
        string? modelName, Action<string>? status, CancellationToken ct = default)
    {
        GgmlType type = MapType(modelName);
        string path = Path.Combine(ModelsDir, $"ggml-{type}.bin".ToLowerInvariant());
        return DownloadOnceAsync(
            path,
            c => WhisperGgmlDownloader.Default.GetGgmlModelAsync(type, QuantizationType.NoQuantization, c),
            $"Downloading Whisper model '{type}' (one-time, this can take a while)…",
            $"Whisper model '{type}' ready.",
            status, ct);
    }

    /// <summary>Returns the path to the Silero VAD model, downloading it once
    /// (~2 MB) if needed.</summary>
    public static Task<string> EnsureVadAsync(Action<string>? status, CancellationToken ct = default)
    {
        const SileroVadType type = SileroVadType.V5_1_2;
        string path = Path.Combine(ModelsDir, $"ggml-silero-{type}.bin".ToLowerInvariant());
        return DownloadOnceAsync(
            path,
            c => WhisperGgmlDownloader.Default.GetGgmlSileroVadModelAsync(type, c),
            "Downloading Silero VAD model (one-time)…",
            doneMsg: null,
            status, ct);
    }

    /// <summary>Shared download-if-missing: skip if the file already exists and is
    /// non-empty, otherwise fetch to a ".part" file and atomically move into place
    /// (so an interrupted download can't leave a truncated file that looks valid).</summary>
    private static async Task<string> DownloadOnceAsync(
        string path, Func<CancellationToken, Task<Stream>> fetch,
        string startMsg, string? doneMsg, Action<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(ModelsDir);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return path;
        }

        status?.Invoke(startMsg);
        using Stream src = await fetch(ct);
        string tmp = path + ".part";
        using (FileStream dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst, ct);
        }
        File.Move(tmp, path, overwrite: true);
        if (doneMsg != null)
        {
            status?.Invoke(doneMsg);
        }
        return path;
    }
}
