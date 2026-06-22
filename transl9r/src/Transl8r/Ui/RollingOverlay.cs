using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Transl8r.Config;

namespace Transl8r.Ui;

/// <summary>
/// Bottom-anchored scrolling subtitle log: new lines fill downward, the box grows
/// upward, and the oldest lines drop off the top once it would exceed its max
/// height or pass their TTL. Used by the audio pipeline — a single instance.
/// </summary>
public sealed class RollingOverlay : OverlayWindow
{
    // accumulated lines (with arrival time), newest last; trimmed from the front
    // when out of space or expired.
    private const int MaxHistory = 60;
    private readonly LinkedList<(string Ja, string En, DateTime At)> _history = new();
    private readonly DispatcherTimer _ttlTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private double _maxHeightFrac = 0.4;   // of work-area height
    private double _ttlSeconds;            // 0 = no expiry

    public RollingOverlay(AppConfig cfg, int offsetX, int offsetY)
        : base(cfg, offsetX, offsetY)
    {
        _ttlTimer.Tick += (_, _) => ExpireOldLines();
        // grow/shrink with content; keep the bottom edge anchored
        SizeChanged += (_, _) =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                Place();
            }
        };
    }

    protected override void ApplyModeConfig(AppConfig cfg)
    {
        _maxHeightFrac = Math.Clamp(cfg.AudioOverlayMaxHeightPercent / 100.0, 0.1, 0.95);
        _ttlSeconds = Math.Max(0, cfg.AudioMessageSeconds);
        _ttlTimer.IsEnabled = _ttlSeconds > 0;
        if (HasText)
        {
            RenderRolling(); // re-trim to the new max height
        }
    }

    protected override (double X, double Y, double W) BasePosition()
    {
        // centred near the bottom; y is the BOTTOM-edge anchor so the box grows
        // upward as lines accumulate.
        Rect wa = SystemParameters.WorkArea;
        double ww = wa.Width * 0.6;
        return (wa.Left + ((wa.Width - ww) / 2), wa.Top + wa.Height - 60, ww);
    }

    protected override double ComputeTop(double y) => y - ActualHeight + _offsetY;

    protected override double DragAnchorY() => Top + ActualHeight;

    /// <summary>
    /// Append a line to the log. <paramref name="ja"/> is the original
    /// transcription, shown above the translation when ShowOriginal is on (pass ""
    /// if unavailable).
    /// </summary>
    public void AppendLine(string ja, string translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
        {
            return;
        }
        HasText = true;
        _showingPlaceholder = false;
        _history.AddLast((ja?.Trim() ?? string.Empty, translated.Trim(), DateTime.UtcNow));
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
        // The rolling log paints everything into TransText (the stacked OrigText is
        // only for region overlays). When ShowOriginal is on we render each entry
        // as a smaller blue JA line above its EN translation, via inline Runs.
        OrigText.Visibility = Visibility.Collapsed;
        RenderHistory();

        // trim oldest lines until the content fits the configured max height.
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
            RenderHistory();
        }
    }

    /// <summary>Paints the current history into TransText: plain EN text, or, when
    /// ShowOriginal is on, a smaller blue JA line above each EN line via Runs.</summary>
    private void RenderHistory()
    {
        if (!_cfg.ShowOriginal)
        {
            // EN-only: setting Text clears any inlines from a prior bilingual render
            TransText.Text = string.Join("\n", _history.Select(h => h.En));
            return;
        }

        TransText.Inlines.Clear();
        double jaSize = Math.Max(8, _cfg.OverlayOrigFontSize);
        Brush jaBrush = OrigText.Foreground;
        bool first = true;
        foreach ((string ja, string en, DateTime _) in _history)
        {
            if (!first)
            {
                TransText.Inlines.Add(new LineBreak());
            }
            first = false;
            if (ja.Length > 0)
            {
                TransText.Inlines.Add(new Run(ja) { FontSize = jaSize, Foreground = jaBrush });
                TransText.Inlines.Add(new LineBreak());
            }
            TransText.Inlines.Add(new Run(en)); // inherits TransText size/colour
        }
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

    /// <summary>Clear the log and hide. Also wipes history (the base only clears the
    /// visible text), but is a no-op for hide during edit mode like the base.</summary>
    public override void ClearText()
    {
        _history.Clear();
        base.ClearText();
    }
}
