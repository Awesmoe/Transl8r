using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Transl8r.Interop;
using Region = Transl8r.Config.Region;

namespace Transl8r.Ui;

/// <summary>
/// Fullscreen drag-to-select region picker (ports region.py). Returns a
/// PHYSICAL-pixel rect in <see cref="Result"/>; shown via ShowDialog (DialogResult
/// true = picked, false/null = cancelled). Also draws existing regions as dashed
/// amber outlines.
///
/// DPI note: this assumes uniform monitor scaling (converts via the picker
/// window's own DPI). Mixed per-monitor scaling would need per-screen DPI like
/// the Python version did — deferred until it's actually needed.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    private readonly IReadOnlyList<Region> _existing;
    private Point? _start;
    private Rectangle? _selRect;
    private double _scale = 1.0;

    public Region? Result { get; private set; }

    public RegionSelectorWindow(IReadOnlyList<Region> existing)
    {
        InitializeComponent();
        _existing = existing;
        // cover the whole virtual desktop (all monitors), in DIPs
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi > 0)
        {
            _scale = dpi / 96.0;
        }

        Root.Width = Width;
        Root.Height = Height;
        UpdateDim(null);
        DrawExisting();
        Activate();
    }

    private void DrawExisting()
    {
        for (int i = 0; i < _existing.Count; i++)
        {
            Region r = _existing[i];
            double x = (r.Left / _scale) - Left;
            double y = (r.Top / _scale) - Top;
            double w = r.Width / _scale;
            double h = r.Height / _scale;

            var outline = new Rectangle
            {
                Width = w,
                Height = h,
                Stroke = new SolidColorBrush(Color.FromRgb(255, 180, 40)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(outline, x);
            Canvas.SetTop(outline, y);
            Root.Children.Add(outline);

            var label = new TextBlock
            {
                Text = $"Region {i + 1}",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 210, 120)),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, x + 4);
            Canvas.SetTop(label, y + 3);
            Root.Children.Add(label);
        }
    }

    private void UpdateDim(Rect? hole)
    {
        var full = new RectangleGeometry(new Rect(0, 0, Root.Width, Root.Height));
        if (hole is { } h)
        {
            var grp = new GeometryGroup { FillRule = FillRule.EvenOdd };
            grp.Children.Add(full);
            grp.Children.Add(new RectangleGeometry(h));
            Dim.Data = grp;
        }
        else
        {
            Dim.Data = full;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _start = e.GetPosition(Root);
        if (_selRect == null)
        {
            _selRect = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 200, 255)),
                StrokeThickness = 2,
                IsHitTestVisible = false,
            };
            Root.Children.Add(_selRect);
        }
        _selRect.Width = 0;
        _selRect.Height = 0;
        Canvas.SetLeft(_selRect, _start.Value.X);
        Canvas.SetTop(_selRect, _start.Value.Y);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_start is not { } s || _selRect == null)
        {
            return;
        }
        Point p = e.GetPosition(Root);
        var rect = new Rect(s, p);
        Canvas.SetLeft(_selRect, rect.X);
        Canvas.SetTop(_selRect, rect.Y);
        _selRect.Width = rect.Width;
        _selRect.Height = rect.Height;
        UpdateDim(rect);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_start is not { } s)
        {
            return;
        }
        ReleaseMouseCapture();
        Point p = e.GetPosition(Root);
        var rect = new Rect(s, p);
        _start = null;

        if (rect.Width > 10 && rect.Height > 10)
        {
            Result = new Region
            {
                Left = (int)Math.Round((rect.X + Left) * _scale),
                Top = (int)Math.Round((rect.Y + Top) * _scale),
                Width = (int)Math.Round(rect.Width * _scale),
                Height = (int)Math.Round(rect.Height * _scale),
            };
            DialogResult = true; // closes the dialog
        }
        else
        {
            DialogResult = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
