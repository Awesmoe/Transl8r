using System;
using System.Windows;
using Transl8r.Config;
using Region = Transl8r.Config.Region;

namespace Transl8r.Ui;

/// <summary>
/// Overlay that replaces its text in place, anchored just below a fixed screen
/// region. Used by the screen-OCR pipeline — one instance per region.
/// </summary>
public sealed class RegionOverlay : OverlayWindow
{
    private readonly Region _region;

    public RegionOverlay(AppConfig cfg, Region region, int offsetX, int offsetY)
        : base(cfg, offsetX, offsetY)
    {
        _region = region;
    }

    protected override (double X, double Y, double W) BasePosition()
    {
        // region coords are physical px; WPF positions in DIPs (HANDOVER #8)
        double s = Scale();
        double x = _region.Left / s;
        double y = ((_region.Top + _region.Height) / s) + 8;
        double w = Math.Max(280, _region.Width / s);
        return (x, y, w);
    }

    /// <summary>Show a translated line in place, replacing any previous text.</summary>
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
}
