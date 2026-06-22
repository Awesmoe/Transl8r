using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Transl8r.Config;
using Transl8r.Interop;
using Region = Transl8r.Config.Region;

namespace Transl8r.Ui;

/// <summary>
/// Always-on-top, click-through, translucent translation overlay. One per
/// region (region == null would be the audio overlay, Phase 3).
///
/// Click-through and topmost are owned natively (HANDOVER #9, #11a): WPF doesn't
/// re-sync window styles on activation the way Qt did, so we just set
/// WS_EX_TRANSPARENT once and re-assert HWND_TOPMOST on every show (games steal
/// z-order). The Qt "3 failed cuts" saga does not apply here.
/// </summary>
public partial class OverlayWindow : Window
{
    private const string Placeholder = ">>  drag me  <<";

    private AppConfig _cfg;
    private readonly Region? _region;
    private int _offsetX;
    private int _offsetY;
    private IntPtr _hwnd;
    private bool _editMode;
    private bool _showingPlaceholder;

    // Rolling-log state (audio overlay only, i.e. region == null): accumulated
    // translated lines (with arrival time), newest last; trimmed from the front
    // when out of space or expired.
    private const int MaxHistory = 60;
    private readonly LinkedList<(string Text, DateTime At)> _history = new();
    private double _maxHeightFrac = 0.4;   // of work-area height
    private double _ttlSeconds;            // 0 = no expiry
    private System.Windows.Threading.DispatcherTimer? _ttlTimer;

    /// <summary>The region-less audio overlay behaves as a scrolling subtitle
    /// log, bottom-anchored and growing upward. Region overlays replace in place.</summary>
    private bool Rolling => _region == null;

    public bool HasText { get; private set; }

    /// <summary>Raised after a drag in edit mode with the new (dx, dy) offset.</summary>
    public event Action<int, int>? OffsetChanged;

    public OverlayWindow(AppConfig cfg, Region? region, int offsetX, int offsetY)
    {
        InitializeComponent();
        _cfg = cfg;
        _region = region;
        _offsetX = offsetX;
        _offsetY = offsetY;

        if (Rolling)
        {
            _ttlTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _ttlTimer.Tick += (_, _) => ExpireOldLines();
        }

        ApplyConfig(cfg);

        // rolling overlay grows/shrinks with content; keep its bottom edge anchored
        SizeChanged += (_, _) =>
        {
            if (Rolling && _hwnd != IntPtr.Zero)
            {
                Place();
            }
        };
    }

    public void ApplyConfig(AppConfig cfg)
    {
        _cfg = cfg;
        TransText.FontSize = cfg.OverlayFontSize;
        // JA original has its own independent size (only shown when ShowOriginal),
        // so it can stay legible regardless of the EN size.
        OrigText.FontSize = Math.Max(8, cfg.OverlayOrigFontSize);
        byte alpha = (byte)Math.Clamp((int)(cfg.OverlayOpacity * 255), 0, 255);
        Root.Background = new SolidColorBrush(Color.FromArgb(alpha, 10, 10, 10));

        if (Rolling)
        {
            _maxHeightFrac = Math.Clamp(cfg.AudioOverlayMaxHeightPercent / 100.0, 0.1, 0.95);
            _ttlSeconds = Math.Max(0, cfg.AudioMessageSeconds);
            if (_ttlTimer != null)
            {
                _ttlTimer.IsEnabled = _ttlSeconds > 0;
            }
            if (HasText)
            {
                RenderRolling(); // re-trim to the new max height
            }
        }
        // apply show-original live to a box that's already on screen
        if (HasText)
        {
            OrigText.Visibility = (cfg.ShowOriginal && OrigText.Text.Length > 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_hwnd != IntPtr.Zero)
        {
            Place();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // keep out of alt-tab and never take focus; preserve WPF's layered bit
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);

        SetClickThrough(true);
        Place();
        AssertTopmost();
    }

    /// <summary>physical->DIP scale for the monitor this window is on.</summary>
    private double Scale()
    {
        if (_hwnd != IntPtr.Zero)
        {
            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            if (dpi > 0)
            {
                return dpi / 96.0;
            }
        }
        return 1.0;
    }

    /// <summary>Computed (x, y, width) in DIPs BEFORE the offset is applied.</summary>
    private (double X, double Y, double W) BasePosition()
    {
        double s = Scale();
        if (_region != null)
        {
            // region coords are physical px; WPF positions in DIPs (HANDOVER #8)
            double x = _region.Left / s;
            double y = ((_region.Top + _region.Height) / s) + 8;
            double w = Math.Max(280, _region.Width / s);
            return (x, y, w);
        }
        // audio overlay: centred near the bottom; y is the BOTTOM-edge anchor so
        // the box grows upward as lines accumulate.
        Rect wa = SystemParameters.WorkArea;
        double ww = wa.Width * 0.6;
        return (wa.Left + ((wa.Width - ww) / 2), wa.Top + wa.Height - 60, ww);
    }

    private void Place()
    {
        (double x, double y, double w) = BasePosition();
        Width = w;
        Left = x + _offsetX;
        // rolling: anchor the bottom edge (y + offset) so growth goes upward;
        // region overlays: top-anchored just below the region.
        Top = Rolling ? (y - ActualHeight + _offsetY) : (y + _offsetY);
    }

    public void ShowText(string original, string translated)
    {
        HasText = true;
        _showingPlaceholder = false;
        OrigText.Text = original;
        TransText.Text = translated;
        OrigText.Visibility = (_cfg.ShowOriginal && original.Length > 0)
            ? Visibility.Visible : Visibility.Collapsed;
        if (!IsVisible)
        {
            Show();
        }
        // re-assert after show: cheap insurance against z-order / style churn.
        // stay click-through unless we're being dragged in edit mode.
        SetClickThrough(!_editMode);
        AssertTopmost();
    }

    /// <summary>
    /// Audio overlay: append a translated line to the rolling log. Fills downward
    /// then scrolls — oldest lines drop off the top once the box would exceed its
    /// max height. (Region overlays use <see cref="ShowText"/> instead.)
    /// </summary>
    public void AppendLine(string translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
        {
            return;
        }
        HasText = true;
        _showingPlaceholder = false;
        _history.AddLast((translated.Trim(), DateTime.UtcNow));
        while (_history.Count > MaxHistory)
        {
            _history.RemoveFirst();
        }
        RenderRolling();
        if (!IsVisible)
        {
            Show();
        }
        SetClickThrough(!_editMode);
        AssertTopmost();
    }

    private void RenderRolling()
    {
        OrigText.Visibility = Visibility.Collapsed; // EN-only in the rolling log
        TransText.Text = JoinHistory();

        // trim oldest lines until the text fits the configured max height.
        // Measure at the current wrap width to account for line wrapping.
        double maxH = SystemParameters.WorkArea.Height * _maxHeightFrac;
        double availW = Math.Max(50, Width - 24); // minus Border padding
        while (_history.Count > 1)
        {
            TransText.Measure(new Size(availW, double.PositiveInfinity));
            if (TransText.DesiredSize.Height <= maxH)
            {
                break;
            }
            _history.RemoveFirst();
            TransText.Text = JoinHistory();
        }
    }

    private string JoinHistory()
    {
        var sb = new System.Text.StringBuilder();
        foreach ((string text, DateTime _) in _history)
        {
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>Drops lines older than the configured TTL (timer-driven).</summary>
    private void ExpireOldLines()
    {
        if (_ttlSeconds <= 0 || _history.Count == 0 || _editMode)
        {
            return;
        }
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-_ttlSeconds);
        bool changed = false;
        while (_history.First is { } first && first.Value.At < cutoff)
        {
            _history.RemoveFirst();
            changed = true;
        }
        if (!changed)
        {
            return;
        }
        if (_history.Count == 0)
        {
            HasText = false;
            TransText.Text = string.Empty;
            Hide();
        }
        else
        {
            RenderRolling();
        }
    }

    /// <summary>Dialogue left the screen — clear and hide. No-op during edit mode:
    /// the OCR loop keeps running, and a clear mid-drag would yank the box away.</summary>
    public void ClearText()
    {
        _history.Clear();
        if (_editMode)
        {
            return;
        }
        HasText = false;
        OrigText.Text = string.Empty;
        TransText.Text = string.Empty;
        Hide();
    }

    /// <summary>Re-show (e.g. overlay output toggled back on) if holding text.</summary>
    public void ShowIfText()
    {
        if (HasText)
        {
            Show();
            SetClickThrough(!_editMode);
            AssertTopmost();
        }
    }

    /// <summary>
    /// Enter/leave edit mode: drop click-through so the box can be dragged, show a
    /// border + drag cursor, and a "drag me" placeholder for empty boxes. Far
    /// simpler than the Qt version (HANDOVER #11) — WPF doesn't re-sync the native
    /// ex-style on activation, so toggling WS_EX_TRANSPARENT just sticks.
    /// </summary>
    public void SetEditMode(bool on)
    {
        _editMode = on;
        if (on)
        {
            Root.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 200, 255));
            Root.BorderThickness = new Thickness(2);
            Cursor = Cursors.SizeAll;
            if (!HasText)
            {
                TransText.Text = Placeholder;
                _showingPlaceholder = true;
            }
            if (!IsVisible)
            {
                Show();
            }
            SetClickThrough(false); // editable: receive the mouse
            Place();
            AssertTopmost();
        }
        else
        {
            Root.BorderThickness = new Thickness(0);
            Cursor = Cursors.Arrow;
            if (_showingPlaceholder)
            {
                _showingPlaceholder = false;
                TransText.Text = string.Empty;
            }
            if (HasText)
            {
                Show();
                SetClickThrough(true);
                AssertTopmost();
            }
            else
            {
                Hide();
                SetClickThrough(true); // safe for the next show
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_editMode)
        {
            return;
        }
        DragMove(); // blocks until mouse-up
        (double bx, double by, double _) = BasePosition();
        _offsetX = (int)Math.Round(Left - bx);
        // rolling: store the bottom-edge delta so growth stays anchored where
        // the user dropped it; region overlays store the top-edge delta.
        _offsetY = (int)Math.Round((Rolling ? (Top + ActualHeight) : Top) - by);
        OffsetChanged?.Invoke(_offsetX, _offsetY);
    }

    private void SetClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if (on)
        {
            ex |= NativeMethods.WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        }
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    private void AssertTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }
}
