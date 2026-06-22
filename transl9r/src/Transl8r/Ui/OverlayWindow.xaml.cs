using System;
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
        ApplyConfig(cfg);
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
        Rect wa = SystemParameters.WorkArea;
        double ww = wa.Width * 0.6;
        return (wa.Left + ((wa.Width - ww) / 2), wa.Top + wa.Height - 160, ww);
    }

    private void Place()
    {
        (double x, double y, double w) = BasePosition();
        Width = w;
        Left = x + _offsetX;
        Top = y + _offsetY;
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

    /// <summary>Dialogue left the screen — clear and hide. No-op during edit mode:
    /// the OCR loop keeps running, and a clear mid-drag would yank the box away.</summary>
    public void ClearText()
    {
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
        _offsetY = (int)Math.Round(Top - by);
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
