using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Transl8r.Audio;

/// <summary>
/// System-audio capture via WASAPI loopback on the default render device. The
/// whole sounddevice/soundcard fallback chain from the Python version collapses
/// to this. Raises <see cref="BlockReady"/> with mono float blocks at
/// <see cref="SampleRate"/> (the device mix rate, usually 48 kHz); callers
/// resample to 16 kHz for Whisper.
///
/// Note: WASAPI loopback delivers no buffers while nothing is playing, so silent
/// stretches simply produce no blocks.
/// </summary>
internal sealed class AudioCapture : IDisposable
{
    private readonly WasapiLoopbackCapture _cap;

    public int SampleRate { get; }
    public string DeviceName { get; }

    /// <summary>Human-readable capture mix format, for diagnostics.</summary>
    public string Format { get; }

    /// <summary>Mono float samples at <see cref="SampleRate"/>.</summary>
    public event Action<float[]>? BlockReady;

    public AudioCapture()
    {
        string name = "default output";
        try
        {
            using var en = new MMDeviceEnumerator();
            name = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
        }
        catch { /* keep placeholder name */ }
        DeviceName = name;

        _cap = new WasapiLoopbackCapture();
        WaveFormat wf = _cap.WaveFormat;
        SampleRate = wf.SampleRate;
        Format = $"{wf.Encoding}, {wf.Channels}ch, {wf.SampleRate} Hz, {wf.BitsPerSample}-bit";
        _cap.DataAvailable += OnData;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        WaveFormat wf = _cap.WaveFormat;
        int ch = wf.Channels;
        if (ch < 1)
        {
            return;
        }

        float[] mono;
        var wb = new WaveBuffer(e.Buffer);

        if (wf.Encoding == WaveFormatEncoding.IeeeFloat && wf.BitsPerSample == 32)
        {
            int frames = e.BytesRecorded / (4 * ch);
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++)
                {
                    sum += wb.FloatBuffer[f * ch + c];
                }
                mono[f] = sum / ch;
            }
        }
        else if (wf.Encoding == WaveFormatEncoding.Pcm && wf.BitsPerSample == 16)
        {
            int frames = e.BytesRecorded / (2 * ch);
            mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                int sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    sum += wb.ShortBuffer[f * ch + c];
                }
                mono[f] = sum / (float)ch / 32768f;
            }
        }
        else
        {
            return; // shared-mode loopback is virtually always 32-bit float; ignore others
        }

        if (mono.Length > 0)
        {
            BlockReady?.Invoke(mono);
        }
    }

    public void Start() => _cap.StartRecording();

    public void Stop()
    {
        try { _cap.StopRecording(); } catch { /* already stopped */ }
    }

    public void Dispose()
    {
        _cap.DataAvailable -= OnData;
        _cap.Dispose();
    }
}

/// <summary>In-memory float[] as an ISampleProvider, so the WDL resampler can
/// downsample a captured buffer to 16 kHz.</summary>
internal sealed class FloatArraySampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private int _pos;

    public WaveFormat WaveFormat { get; }

    public FloatArraySampleProvider(float[] data, int sampleRate)
    {
        _data = data;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int n = Math.Min(count, _data.Length - _pos);
        if (n <= 0)
        {
            return 0;
        }
        Array.Copy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }
}
