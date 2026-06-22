using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Transl8r.Config;
using Transl8r.Interop;

namespace Transl8r.Ui;

/// <summary>
/// Shared base for the always-on-top, click-through, translucent translation
/// overlays. Owns the native plumbing and the common chrome (font/opacity from
/// config, edit-mode drag, offset persistence); the two concrete modes —
/// <see cref="RegionOverlay"/> (replace-in-place, anchored to a screen region)
/// and <see cref="RollingOverlay"/> (scrolling subtitle log) — override only the
/// positioning and how text is shown.
///
/// Click-through and topmost are owned natively (HANDOVER #9, #11a): WPF doesn't
/// re-sync window styles on activation the way Qt did, so we just set
/// WS_EX_TRANSPARENT once and re-assert HWND_TOPMOST on every show (games steal
/// z-order). The Qt "3 failed cuts" saga does not apply here.
/// </summary>
public abstract partial class OverlayWindow : Window
{
    private const string Placeholder = ">>  drag me  <<";

    protected AppConfig _cfg;
    protected int _offsetX;
    protected int _offsetY;
    protected IntPtr _hwnd;
    protected bool _editMode;
    protected bool _showingPlaceholder;

    public bool HasText { get; protected set; }

    /// <summary>Raised after a drag in edit mode with the new (dx, dy) offset.</summary>
    public event Action<int, int>? OffsetChanged;

    protected OverlayWindow(AppConfig cfg, int offsetX, int offsetY)
    {
        InitializeComponent();
        _cfg = cfg;
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

        ApplyModeConfig(cfg);

        // apply show-original live to a box already on screen. (Region overlays
        // use OrigText; the rolling log keeps it collapsed and renders JA inline.)
        if (HasText)
        {
            OrigText.Visibility = (cfg.ShowOriginal && OrigText.Text.Length > 0)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_hwnd != IntPtr.Zero)
        {
            ApplyCaptureExclusion();
            Place();
        }
    }

    /// <summary>Mode-specific config (e.g. the rolling log's max height + TTL).</summary>
    protected virtual void ApplyModeConfig(AppConfig cfg) { }

    /// <summary>Mark (or unmark) the window as excluded from screen capture, so our
    /// own overlay text can't be OCR'd by another region and re-translated. Visible
    /// to the user either way; only capture APIs (incl. screenshots) are affected.</summary>
    private void ApplyCaptureExclusion()
    {
        NativeMethods.SetWindowDisplayAffinity(_hwnd,
            _cfg.ExcludeOverlayFromCapture
                ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
                : NativeMethods.WDA_NONE);
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
        ApplyCaptureExclusion();
        Place();
        AssertTopmost();
    }

    /// <summary>physical->DIP scale for the monitor this window is on.</summary>
    protected double Scale()
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

    /// <summary>Computed (x, y, width) in DIPs BEFORE the offset is applied. The
    /// meaning of y differs by mode (see <see cref="ComputeTop"/>).</summary>
    protected abstract (double X, double Y, double W) BasePosition();

    /// <summary>Maps the base y to the window Top. Region overlays are top-anchored
    /// just below the region; the rolling log anchors its bottom edge so it grows
    /// upward (overridden there).</summary>
    protected virtual double ComputeTop(double y) => y + _offsetY;

    protected void Place()
    {
        (double x, double y, double w) = BasePosition();
        Width = w;
        Left = x + _offsetX;
        Top = ComputeTop(y);
    }

    /// <summary>Dialogue left the screen — clear and hide. No-op during edit mode:
    /// the loop keeps running, and a clear mid-drag would yank the box away.</summary>
    public virtual void ClearText()
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
        _offsetY = (int)Math.Round(DragAnchorY() - by);
        OffsetChanged?.Invoke(_offsetX, _offsetY);
    }

    /// <summary>The window edge whose delta the offset tracks: Top for region
    /// overlays, the bottom edge for the rolling log (overridden there) so growth
    /// stays anchored where the user dropped it.</summary>
    protected virtual double DragAnchorY() => Top;

    protected void SetClickThrough(bool on)
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

    protected void AssertTopmost()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
    }
}
