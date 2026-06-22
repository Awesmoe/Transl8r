using System;
using System.Collections.Generic;
using Whisper.net;

namespace Transl8r.Audio;

/// <summary>
/// Silero VAD gate. Runs voice-activity detection on a 16 kHz mono chunk and
/// reports whether it contains any speech, so the pipeline can skip non-speech
/// audio (music, ambience, silence) before it reaches Whisper — which otherwise
/// hallucinates captions like "thank you for watching" on such input.
/// </summary>
internal sealed class VadGate : IDisposable
{
    private readonly WhisperVadFactory _factory;
    private readonly WhisperVadProcessor _processor;

    public VadGate(string modelPath)
    {
        _factory = WhisperVadFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder().Build();
    }

    /// <summary>True if any speech segment is detected in the 16 kHz mono samples.
    /// DetectSpeech resets VAD state per call, so chunks stay independent.</summary>
    public bool HasSpeech(float[] samples16k)
    {
        IReadOnlyList<VadSegmentData> segments = _processor.DetectSpeech(samples16k);
        return segments.Count > 0;
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
