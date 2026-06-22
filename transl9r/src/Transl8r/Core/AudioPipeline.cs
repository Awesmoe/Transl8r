using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Transl8r.Audio;
using Transl8r.Config;
using Transl8r.Translation;

namespace Transl8r.Core;

/// <summary>
/// System-audio → English pipeline. Ports AudioWorker.run: WASAPI loopback →
/// silence-based chunking → Whisper. A background consumer Task does the heavy
/// work; the capture callback only enqueues blocks (so slow transcription never
/// stalls capture).
///
/// Key difference from the Python version: WASAPI loopback delivers no buffers
/// while nothing is playing, so the "trailing silence" flush is driven by a
/// read timeout (absence of audio == silence), not by quiet sample blocks.
/// </summary>
internal sealed class AudioPipeline
{
    // chunking constants, ported from AudioWorker
    private const float SilenceRms = 0.01f;
    private const double MinChunkS = 2.0;
    private const double MaxChunkS = 8.0;
    private const double TailSilenceS = 0.6;
    private const int ReadTimeoutMs = 200;

    private readonly AppConfig _cfg;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<string, string>? TextReady; // ja (or ""), en
    public event Action<string>? Status;
    public event Action<string>? Error;

    public AudioPipeline(AppConfig cfg) => _cfg = cfg;

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        _task = Task.Run(() => Run(token), token);
    }

    public void Stop() => _cts?.Cancel();

    private void Run(CancellationToken token)
    {
        WhisperTranscriber? whisper = null;
        VadGate? vad = null;
        ITranslator? translator = null;
        AudioCapture? capture = null;
        var queue = new BlockingCollection<float[]>(new ConcurrentQueue<float[]>());

        try
        {
            // transcribe-then-translate when an external translator is configured;
            // otherwise let Whisper's own translate task produce English directly.
            bool useExternal = _cfg.AudioUseTranslator;
            translator = useExternal ? TranslatorFactory.Build(_cfg) : null;

            Status?.Invoke($"Loading Whisper '{_cfg.WhisperModel}'…");
            string model = WhisperModelStore
                .EnsureAsync(_cfg.WhisperModel, m => Status?.Invoke(m), token)
                .GetAwaiter().GetResult();
            whisper = new WhisperTranscriber(model, translate: translator == null);

            if (_cfg.AudioVad)
            {
                Status?.Invoke("Loading voice-activity detection…");
                string vadModel = WhisperModelStore
                    .EnsureVadAsync(m => Status?.Invoke(m), token)
                    .GetAwaiter().GetResult();
                vad = new VadGate(vadModel);
            }

            capture = new AudioCapture();
            capture.BlockReady += block =>
            {
                if (!queue.IsAddingCompleted)
                {
                    queue.Add(block);
                }
            };
            int sr = capture.SampleRate;
            capture.Start();
            Status?.Invoke($"Listening to: {capture.DeviceName}");

            var buf = new List<float[]>();
            int bufSamples = 0;
            double silentTail = 0.0;

            while (!token.IsCancellationRequested)
            {
                if (queue.TryTake(out float[]? block, ReadTimeoutMs, token))
                {
                    buf.Add(block);
                    bufSamples += block.Length;
                    silentTail = Rms(block) < SilenceRms
                        ? silentTail + block.Length / (double)sr
                        : 0.0;
                }
                else
                {
                    // no audio arrived this interval → silence on the output device
                    silentTail += ReadTimeoutMs / 1000.0;
                }

                double bufLen = bufSamples / (double)sr;

                // drop a tiny stale residual after prolonged silence so it can't
                // get glued onto the next utterance across a long gap
                if (bufLen > 0 && bufLen < MinChunkS && silentTail >= 2.0)
                {
                    buf.Clear();
                    bufSamples = 0;
                    silentTail = 0.0;
                    continue;
                }

                bool flush = bufLen >= MaxChunkS ||
                             (bufLen >= MinChunkS && silentTail >= TailSilenceS);
                if (!flush)
                {
                    continue;
                }

                float[] chunk = Concat(buf, bufSamples);
                buf.Clear();
                bufSamples = 0;
                silentTail = 0.0;

                if (Rms(chunk) < SilenceRms)
                {
                    continue; // whole chunk is silence
                }

                float[] chunk16 = AudioFile.Resample16kMono(chunk, sr);

                // VAD gate: drop chunks with no speech so Whisper can't hallucinate
                if (vad != null && !vad.HasSpeech(chunk16))
                {
                    continue;
                }

                string raw = whisper.TranscribeAsync(chunk16, token).GetAwaiter().GetResult();
                string? text = HallucinationFilter.Filter(raw);
                if (text == null)
                {
                    continue; // empty, a sound cue, or a known hallucination phrase
                }

                if (translator != null)
                {
                    TextReady?.Invoke(text, translator.Translate(text));
                }
                else
                {
                    TextReady?.Invoke("", text);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Audio pipeline error: {ex.Message}");
        }
        finally
        {
            queue.CompleteAdding();
            capture?.Stop();
            capture?.Dispose();
            vad?.Dispose();
            whisper?.Dispose();
        }
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0)
        {
            return 0f;
        }
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += (double)samples[i] * samples[i];
        }
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static float[] Concat(List<float[]> blocks, int total)
    {
        var outArr = new float[total];
        int pos = 0;
        foreach (float[] b in blocks)
        {
            Array.Copy(b, 0, outArr, pos, b.Length);
            pos += b.Length;
        }
        return outArr;
    }
}
