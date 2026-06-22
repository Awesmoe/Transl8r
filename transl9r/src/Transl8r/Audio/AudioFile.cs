using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Transl8r.Audio;

/// <summary>
/// Decodes an audio file (wav/mp3/etc via NAudio) to the 16 kHz mono float
/// samples Whisper expects. Used by the Whisper smoke test; the live pipeline
/// resamples capture blocks the same way.
/// </summary>
internal static class AudioFile
{
    public static float[] Load16kMono(string path)
    {
        using var reader = new AudioFileReader(path);

        ISampleProvider mono = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => throw new NotSupportedException(
                $"Unsupported channel count ({reader.WaveFormat.Channels}); use a mono or stereo file."),
        };

        ISampleProvider src = mono.WaveFormat.SampleRate == WhisperTranscriber.SampleRate
            ? mono
            : new WdlResamplingSampleProvider(mono, WhisperTranscriber.SampleRate);

        var samples = new List<float>();
        var buf = new float[WhisperTranscriber.SampleRate]; // ~1s blocks
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                samples.Add(buf[i]);
            }
        }
        return samples.ToArray();
    }
}
