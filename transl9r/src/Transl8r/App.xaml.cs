using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Transl8r.Audio;
using Transl8r.Config;
using Transl8r.Core;
using Transl8r.Interop;
using Transl8r.Ui;
using WinForms = System.Windows.Forms;

namespace Transl8r;

/// <summary>
/// Application entry point and wiring hub (the C# counterpart of app.py).
/// Phase 1: tray, overlays per region, the screen-OCR pipeline (vlm-direct),
/// and an overlay-toggle hotkey. Region picker, edit mode, settings dialog,
/// audio, and the other backends come in later phases.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "transl8r-single-instance-7c1f9b3a";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    private WinForms.NotifyIcon? _tray;
    private WinForms.ToolStripMenuItem? _ocrItem;
    private WinForms.ToolStripMenuItem? _overlayItem;
    private WinForms.ToolStripMenuItem? _editItem;

    private WinForms.ToolStripMenuItem? _listenItem;

    private readonly List<RegionOverlay> _overlays = new();
    private RollingOverlay? _audioOverlay; // single, region-less (bottom-center)
    private OcrPipeline? _pipeline;
    private AudioPipeline? _audioPipeline;
    private GlobalHotkeys? _hotkeys;
    private bool _suppressToggle; // set while syncing tray checkmarks from config

    public AppConfig Config { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show(
                "transl8r is already running (check the system tray).",
                "transl8r", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Config = AppConfig.Load();

        BuildTray();
        BuildOverlays();

        BuildAudioOverlay();

        _hotkeys = new GlobalHotkeys();
        _hotkeys.Triggered += OnHotkey;
        ApplyHotkeys();

        if (Config.OcrEnabled)
        {
            StartPipeline();
        }
        if (Config.AudioEnabled)
        {
            StartAudioPipeline();
        }
    }

    // ----------------------------------------------------------------- tray

    private void BuildTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application, // placeholder
            Text = "transl8r",
            Visible = true,
        };

        var menu = new WinForms.ContextMenuStrip();

        _ocrItem = new WinForms.ToolStripMenuItem("Screen OCR")
        {
            CheckOnClick = true,
            Checked = Config.OcrEnabled, // set before subscribing: no event now
        };
        _ocrItem.CheckedChanged += (_, _) => ToggleOcr(_ocrItem.Checked);
        menu.Items.Add(_ocrItem);

        _overlayItem = new WinForms.ToolStripMenuItem("Show overlay")
        {
            CheckOnClick = true,
            Checked = Config.OutputOverlay,
        };
        _overlayItem.CheckedChanged += (_, _) => ToggleOverlay(_overlayItem.Checked);
        menu.Items.Add(_overlayItem);

        _listenItem = new WinForms.ToolStripMenuItem("Listen (system audio)")
        {
            CheckOnClick = true,
            Checked = Config.AudioEnabled,
        };
        _listenItem.CheckedChanged += (_, _) => ToggleAudio(_listenItem.Checked);
        menu.Items.Add(_listenItem);

        _editItem = new WinForms.ToolStripMenuItem("Edit overlay positions") { CheckOnClick = true };
        _editItem.CheckedChanged += (_, _) => ToggleEditMode(_editItem.Checked);
        menu.Items.Add(_editItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Select screen region…", null, (_, _) => PickRegion(add: false));
        menu.Items.Add("Add screen region", null, (_, _) => PickRegion(add: true));

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Quit transl8r", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;
    }

    // ------------------------------------------------------------- overlays

    private void BuildOverlays()
    {
        foreach (OverlayWindow ov in _overlays)
        {
            ov.Close();
        }
        _overlays.Clear();

        List<int[]> offsets = Config.OverlayOffsets;
        for (int i = 0; i < Config.Regions.Count; i++)
        {
            int dx = 0, dy = 0;
            if (i < offsets.Count && offsets[i] is { Length: >= 2 } o)
            {
                dx = o[0];
                dy = o[1];
            }
            int idx = i;
            var ov = new RegionOverlay(Config, Config.Regions[i], dx, dy);
            ov.OffsetChanged += (ndx, ndy) => SaveOverlayOffset(idx, ndx, ndy);
            if (_editItem?.Checked == true)
            {
                ov.SetEditMode(true); // re-entered edit after a rebuild
            }
            _overlays.Add(ov);
        }
    }

    private void BuildAudioOverlay()
    {
        int dx = 0, dy = 0;
        if (Config.AudioOverlayOffset is { Length: >= 2 } o)
        {
            dx = o[0];
            dy = o[1];
        }
        _audioOverlay = new RollingOverlay(Config, dx, dy);
        _audioOverlay.OffsetChanged += SaveAudioOverlayOffset;
        if (_editItem?.Checked == true)
        {
            _audioOverlay.SetEditMode(true);
        }
    }

    private void SaveAudioOverlayOffset(int dx, int dy)
    {
        Config.AudioOverlayOffset = new[] { dx, dy };
        Config.Save();
    }

    // -------------------------------------------------------------- hotkeys

    private void ApplyHotkeys()
    {
        var bindings = new Dictionary<string, string>
        {
            ["overlay"] = Config.HotkeyOverlay,
            ["region"] = Config.HotkeyRegion,
            ["add"] = Config.HotkeyAdd,
            ["edit"] = Config.HotkeyEdit,
        };
        List<string> failed = _hotkeys!.SetBindings(bindings);
        foreach (string combo in failed)
        {
            Notify($"Hotkey '{combo}' could not be registered (in use by another app?).");
        }
    }

    private void OnHotkey(string name)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            switch (name)
            {
                case "overlay" when _overlayItem != null:
                    _overlayItem.Checked = !_overlayItem.Checked; // fires ToggleOverlay
                    break;
                case "region":
                    PickRegion(add: false);
                    break;
                case "add":
                    PickRegion(add: true);
                    break;
                case "edit" when _editItem != null:
                    _editItem.Checked = !_editItem.Checked; // fires ToggleEditMode
                    break;
            }
        }));
    }

    // ------------------------------------------------------------- pipeline

    private void StartPipeline()
    {
        if (_pipeline != null)
        {
            return;
        }
        if (Config.Regions.Count == 0)
        {
            Notify("No screen region in config. The region picker arrives in Phase 2.");
            return;
        }
        _pipeline = new OcrPipeline(Config);
        _pipeline.TextReady += OnTextReady;
        _pipeline.TextCleared += OnTextCleared;
        _pipeline.Status += OnStatus;
        _pipeline.Error += OnError;
        _pipeline.Start();
    }

    private void StopPipeline()
    {
        _pipeline?.Stop();
        _pipeline = null;
    }

    private void StartAudioPipeline()
    {
        if (_audioPipeline != null)
        {
            return;
        }
        _audioPipeline = new AudioPipeline(Config);
        _audioPipeline.TextReady += OnAudioTextReady;
        _audioPipeline.Status += OnStatus;
        _audioPipeline.Error += OnError;
        _audioPipeline.Start();
    }

    private void StopAudioPipeline()
    {
        _audioPipeline?.Stop();
        _audioPipeline = null;
        Dispatcher.BeginInvoke((Action)(() => _audioOverlay?.ClearText()));
    }

    private void OnAudioTextReady(string ja, string en)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            Debug.WriteLine($"[audio] {ja}  =>  {en}");
            if (Config.OutputOverlay)
            {
                _audioOverlay?.AppendLine(ja, en); // rolling subtitle log (JA shown when ShowOriginal)
            }
            FileOutput.Write(Config, ja, en); // independent sink
        }));
    }

    // --------------------------------------------------------------- region picker

    private void ToggleEditMode(bool on)
    {
        foreach (OverlayWindow ov in _overlays)
        {
            ov.SetEditMode(on);
        }
        _audioOverlay?.SetEditMode(on);
        if (on)
        {
            Notify("Edit mode: drag overlays to reposition, then toggle off to lock.");
        }
    }

    private void SaveOverlayOffset(int idx, int dx, int dy)
    {
        List<int[]> offs = Config.OverlayOffsets;
        while (offs.Count <= idx)
        {
            offs.Add(new[] { 0, 0 });
        }
        offs[idx] = new[] { dx, dy };
        Config.Save();
    }

    private void PickRegion(bool add)
    {
        if (_editItem?.Checked == true)
        {
            _editItem.Checked = false; // don't carry edit mode into the picker
        }
        bool wasRunning = _pipeline != null;
        StopPipeline();
        foreach (OverlayWindow ov in _overlays)
        {
            ov.Hide();
        }

        var picker = new RegionSelectorWindow(Config.Regions);
        bool? ok = picker.ShowDialog();

        if (ok == true && picker.Result is { } region)
        {
            if (add)
            {
                Config.Regions.Add(region);
            }
            else
            {
                // fresh single region — drop offsets so a leftover offset can't
                // nudge the new box (mirrors app.py _region_done)
                Config.Regions = new List<Region> { region };
                Config.OverlayOffsets = new List<int[]>();
            }
            Config.Save();
            BuildOverlays();
            Notify($"Region saved ({Config.Regions.Count} active). 'Add screen region' appends more.");
            if (Config.OcrEnabled)
            {
                StartPipeline();
            }
        }
        else if (wasRunning && Config.OcrEnabled && Config.Regions.Count > 0)
        {
            // cancelled — resume what was running
            StartPipeline();
        }
    }

    private void OnTextReady(int idx, string ja, string en)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            Debug.WriteLine($">> {ja}  =>  {en}");
            if (Config.OutputOverlay && idx >= 0 && idx < _overlays.Count)
            {
                _overlays[idx].ShowText(ja, en);
            }
            FileOutput.Write(Config, ja, en); // independent sink
        }));
    }

    private void OnTextCleared(int idx)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            if (idx >= 0 && idx < _overlays.Count)
            {
                _overlays[idx].ClearText();
            }
        }));
    }

    private void OnStatus(string msg)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            Debug.WriteLine($"[status] {msg}");
            if (_tray != null)
            {
                _tray.Text = Truncate($"transl8r — {msg}", 63);
            }
        }));
    }

    private void OnError(string msg)
    {
        Dispatcher.BeginInvoke((Action)(() =>
        {
            Debug.WriteLine($"[error] {msg}");
            Notify(msg);
        }));
    }

    // --------------------------------------------------------------- toggles

    private void ToggleOcr(bool on)
    {
        if (_suppressToggle)
        {
            return;
        }
        Config.OcrEnabled = on;
        Config.Save();
        if (on)
        {
            StartPipeline();
        }
        else
        {
            StopPipeline();
        }
    }

    private void ToggleAudio(bool on)
    {
        if (_suppressToggle)
        {
            return;
        }
        Config.AudioEnabled = on;
        Config.Save();
        if (on)
        {
            StartAudioPipeline();
        }
        else
        {
            StopAudioPipeline();
        }
    }

    private void ToggleOverlay(bool on)
    {
        if (_suppressToggle)
        {
            return;
        }
        Config.OutputOverlay = on;
        Config.Save();
        foreach (OverlayWindow ov in _overlays)
        {
            if (on)
            {
                ov.ShowIfText();
            }
            else
            {
                ov.Hide();
            }
        }
        if (on)
        {
            _audioOverlay?.ShowIfText();
        }
        else
        {
            _audioOverlay?.Hide();
        }
    }

    private void OpenSettings()
    {
        var dlg = new SettingsWindow(Config);
        if (dlg.ShowDialog() != true || dlg.Result is null)
        {
            return;
        }

        AppConfig old = Config;
        Config = dlg.Result;
        Config.Save();

        // apply overlay display options (font/opacity/show-original) live
        foreach (OverlayWindow ov in _overlays)
        {
            ov.ApplyConfig(Config);
        }
        _audioOverlay?.ApplyConfig(Config);

        // sync tray checkmarks to the new config without firing their toggles
        _suppressToggle = true;
        if (_ocrItem != null) _ocrItem.Checked = Config.OcrEnabled;
        if (_overlayItem != null) _overlayItem.Checked = Config.OutputOverlay;
        if (_listenItem != null) _listenItem.Checked = Config.AudioEnabled;
        _suppressToggle = false;

        // apply overlay visibility per the new output_overlay
        foreach (OverlayWindow ov in _overlays)
        {
            if (Config.OutputOverlay) ov.ShowIfText();
            else ov.Hide();
        }
        if (Config.OutputOverlay) _audioOverlay?.ShowIfText();
        else _audioOverlay?.Hide();

        ApplyHotkeys(); // combos may have changed

        // restart the OCR pipeline if a setting it captured at start changed
        bool ocrKeysChanged =
            old.PollInterval != Config.PollInterval ||
            old.FrameChangeRatio != Config.FrameChangeRatio ||
            old.OcrBackend != Config.OcrBackend ||
            old.PaddleMinConfidence != Config.PaddleMinConfidence ||
            old.VlmUrl != Config.VlmUrl ||
            old.VlmModel != Config.VlmModel ||
            old.Translator != Config.Translator ||
            old.DeeplApiKey != Config.DeeplApiKey ||
            old.ServerUrl != Config.ServerUrl ||
            old.ServerModel != Config.ServerModel;

        if (Config.OcrEnabled != old.OcrEnabled)
        {
            if (Config.OcrEnabled) StartPipeline();
            else StopPipeline();
        }
        else if (Config.OcrEnabled && ocrKeysChanged)
        {
            StopPipeline();
            StartPipeline();
        }

        // restart the audio pipeline if a setting it captured at start changed
        bool audioKeysChanged =
            old.WhisperModel != Config.WhisperModel ||
            old.AudioVad != Config.AudioVad ||
            old.AudioUseTranslator != Config.AudioUseTranslator ||
            (Config.AudioUseTranslator &&
                (old.Translator != Config.Translator ||
                 old.DeeplApiKey != Config.DeeplApiKey ||
                 old.ServerUrl != Config.ServerUrl ||
                 old.ServerModel != Config.ServerModel));

        if (Config.AudioEnabled != old.AudioEnabled)
        {
            if (Config.AudioEnabled) StartAudioPipeline();
            else StopAudioPipeline();
        }
        else if (Config.AudioEnabled && audioKeysChanged)
        {
            StopAudioPipeline();
            StartAudioPipeline();
        }
    }

    private void Notify(string msg)
    {
        _tray?.ShowBalloonTip(4000, "transl8r", msg, WinForms.ToolTipIcon.Info);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    protected override void OnExit(ExitEventArgs e)
    {
        StopPipeline();
        StopAudioPipeline();
        _hotkeys?.Dispose();
        foreach (OverlayWindow ov in _overlays)
        {
            ov.Close();
        }
        _audioOverlay?.Close();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_ownsMutex)
        {
            _instanceMutex?.ReleaseMutex();
        }
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }
}
