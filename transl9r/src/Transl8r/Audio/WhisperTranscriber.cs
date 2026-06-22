using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace Transl8r.Audio;

/// <summary>
/// Thin wrapper over a Whisper.net factory + processor. Holds one processor and
/// reuses it across chunks (the pipeline calls it sequentially — ProcessAsync is
/// not safe to call concurrently on the same instance).
///
/// translate=true uses Whisper's built-in translate task (source audio -> English
/// directly, mirroring faster-whisper's task="translate"). translate=false just
/// transcribes in the source language, for the "transcribe then external
/// translator" path (audio_use_translator).
/// </summary>
internal sealed class WhisperTranscriber : IDisposable
{
    public const int SampleRate = 16000;

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;

    public WhisperTranscriber(string modelPath, bool translate, string language = "ja")
    {
        _factory = WhisperFactory.FromPath(modelPath);
        WhisperProcessorBuilder builder = _factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext(); // independent chunks: don't let one bleed into the next
        if (translate)
        {
            builder = builder.WithTranslate();
        }
        _processor = builder.Build();
    }

    /// <summary>Transcribes 16 kHz mono float samples to a single text string.</summary>
    public async Task<string> TranscribeAsync(float[] samples16k, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (SegmentData seg in _processor.ProcessAsync(samples16k, ct))
        {
            sb.Append(seg.Text);
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
