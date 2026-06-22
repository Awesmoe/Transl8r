using System;
using System.IO;
using System.Text;
using Transl8r.Config;

namespace Transl8r.Core;

/// <summary>
/// Appends translated lines to a log file when OutputFile is enabled. Ports the
/// file sink of outputs.py: one line per translation, "[HH:MM:SS] {orig => }en".
/// Independent of the overlay/TTS sinks; safe to call from any thread.
/// </summary>
internal static class FileOutput
{
    private static readonly object Gate = new();

    public static void Write(AppConfig cfg, string original, string translated)
    {
        if (!cfg.OutputFile || string.IsNullOrWhiteSpace(translated))
        {
            return;
        }

        string path = string.IsNullOrWhiteSpace(cfg.OutputFilePath)
            ? "transl8r_log.txt"
            : cfg.OutputFilePath;
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        string prefix = string.IsNullOrEmpty(original) ? string.Empty : original + " => ";
        string line = $"[{stamp}] {prefix}{translated}{Environment.NewLine}";

        try
        {
            lock (Gate)
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // a bad path shouldn't kill a pipeline; just note it.
            System.Diagnostics.Debug.WriteLine($"[file] write error: {ex.Message}");
        }
    }
}
