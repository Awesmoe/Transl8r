using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Transl8r.Capture;
using Transl8r.Config;
using Transl8r.Imaging;
using Transl8r.Ocr;
using Region = Transl8r.Config.Region;

namespace Transl8r.Core;

/// <summary>
/// Screen OCR loop. Polls each region round-robin in one background Task, runs
/// per-region change detection, and calls the (direct) VLM backend. Ports
/// OcrWorker.run for the direct path; QThread + zombie management become a
/// CancellationToken (HANDOVER #4). Events fire on the worker thread — the
/// caller marshals to the UI thread.
/// </summary>
internal sealed class OcrPipeline
{
    private sealed class RegionState
    {
        public byte[]? Prev;
        public byte[]? Processed;
        public string? LastText;
        public int Unstable;
    }

    private readonly AppConfig _cfg;
    private readonly IReadOnlyList<Region> _regions;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public event Action<int, string, string>? TextReady;  // region idx, ja, en
    public event Action<int>? TextCleared;                 // region idx
    public event Action<string>? Status;
    public event Action<string>? Error;

    public OcrPipeline(AppConfig cfg)
    {
        _cfg = cfg;
        _regions = cfg.Regions;
    }

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
        if (_regions.Count == 0)
        {
            Error?.Invoke("No screen region selected.");
            return;
        }

        IOcrProcessor processor;
        try
        {
            processor = OcrBackendFactory.Build(_cfg, out string? note);
            if (note != null)
            {
                Status?.Invoke(note);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke($"OCR pipeline init failed: {ex.Message}");
            return;
        }

        Status?.Invoke($"OCR running ({_cfg.OcrBackend}, {_regions.Count} region{(_regions.Count > 1 ? "s" : "")})");
        double poll = _cfg.PollInterval;
        double ratio = _cfg.FrameChangeRatio;

        var states = new RegionState[_regions.Count];
        for (int i = 0; i < states.Length; i++)
        {
            states[i] = new RegionState();
        }

        while (!token.IsCancellationRequested)
        {
            for (int i = 0; i < _regions.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                RegionState st = states[i];
                try
                {
                    byte[] cur;
                    Bitmap shot = ScreenCapture.Grab(_regions[i]);
                    try
                    {
                        cur = ImageOps.GetBgraBytes(shot);

                        bool stable = !ImageOps.FrameChanged(cur, st.Prev, ratio);
                        st.Prev = cur;
                        st.Unstable = stable ? 0 : st.Unstable + 1;

                        // OCR when the frame settled, or force after 5 restless
                        // polls (perpetually animated textboxes), AND only if it
                        // differs from what we last processed.
                        if (!((stable || st.Unstable >= 5) &&
                              ImageOps.FrameChanged(cur, st.Processed, ratio)))
                        {
                            continue;
                        }
                        st.Unstable = 0;
                        st.Processed = cur;

                        (string ja, string en) = processor.Process(shot);

                        if (string.IsNullOrEmpty(ja))
                        {
                            // dialogue gone: clear overlay, forget last_text so a
                            // repeat line shows again later
                            if (!string.IsNullOrEmpty(st.LastText))
                            {
                                st.LastText = null;
                                TextCleared?.Invoke(i);
                            }
                            continue;
                        }
                        if (ja != st.LastText && JapaneseText.LooksJapanese(ja))
                        {
                            st.LastText = ja;
                            TextReady?.Invoke(i, ja, en);
                        }
                    }
                    finally
                    {
                        shot.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Error?.Invoke($"OCR loop error: {ex.Message}");
                    Wait(2000, token);
                }
            }
            Wait((int)(poll * 1000), token);
        }
    }

    // cancellable sleep: returns early when the token is signalled
    private static void Wait(int ms, CancellationToken token)
    {
        if (ms > 0)
        {
            token.WaitHandle.WaitOne(ms);
        }
    }
}
