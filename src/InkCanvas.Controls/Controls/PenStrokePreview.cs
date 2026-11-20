using InkCanvas.Core.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace InkCanvas.Controls.Controls;

/// <summary>A sample generated with the same width, taper and nib geometry as canvas ink.</summary>
public sealed class PenStrokePreview : UserControl
{
    private readonly Canvas _surface = new() { Height = 80, IsHitTestVisible = false };
    private readonly Path _checks = new();
    private readonly Path _ink = new();
    private StrokeAttributes _attributes = new();
    private double _previewHeight = 80;
    private bool _showChecks = true;
    public double PreviewHeight
    {
        get => _previewHeight;
        set { _previewHeight = value; _surface.Height = value; Render(); }
    }
    public bool ShowChecks
    {
        get => _showChecks;
        set
        {
            _showChecks = value;
            _checks.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            if (!value) _surface.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
    public InkStroke Stroke { get; private set; }

    public PenStrokePreview()
    {
        _surface.Children.Add(_checks);
        _surface.Children.Add(_ink);
        Content = new Border { CornerRadius = new CornerRadius(5), Child = _surface };
        AutomationProperties.SetName(this, "笔触预览");
        SizeChanged += (_, _) => Render();
        ApplyTheme(1);
        Render();
    }

    public void Update(StrokeAttributes attributes, bool highlighter)
    {
        _attributes = attributes.Clone();
        _attributes.IsHighlighter = highlighter;
        Render();
    }

    public void ApplyTheme(int theme)
    {
        bool dark = Theming.PopupBrushes.IsDark(theme);
        _surface.Background = new SolidColorBrush(!_showChecks ? Microsoft.UI.Colors.Transparent
            : dark ? Windows.UI.Color.FromArgb(255, 43, 51, 63) : Windows.UI.Color.FromArgb(255, 231, 235, 242));
        _checks.Fill = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 59, 69, 83) : Windows.UI.Color.FromArgb(255, 210, 217, 228));
    }

    private void Render()
    {
        double width = ActualWidth > 0 ? ActualWidth : 200;
        float height = (float)_previewHeight;
        _surface.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, width, height) };
        var checks = new GeometryGroup();
        for (int y = 0; y < height; y += 12)
        for (int x = y % 24; x < width; x += 24)
            checks.Children.Add(new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, y, 12, 12) });
        _checks.Data = checks;

        float inset = MathF.Max(8, _attributes.BaseWidth / 2 + 4);
        float amplitude = Math.Clamp(height / 2 - _attributes.BaseWidth / 2 - 8, 0, 12);
        Stroke = new InkStroke(_attributes.Clone());
        var previous = System.Numerics.Vector2.Zero;
        float time = 0;
        for (int i = 0; i <= 40; i++)
        {
            float t = i / 40f;
            var point = new System.Numerics.Vector2(inset + ((float)width - 2 * inset) * t,
                height / 2 + MathF.Sin(t * MathF.PI * 2) * amplitude);
            if (i > 0) time += System.Numerics.Vector2.Distance(previous, point) / (.08f + 2.5f * t * t);
            Stroke.AddPoint(point.X, point.Y, .5f, time);
            previous = point;
        }
        Stroke.Complete();
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var contour in StrokeGeometry.BuildUnionContours(Stroke))
        {
            var figure = new PathFigure { StartPoint = new Point(contour[0].X, contour[0].Y), IsClosed = true, IsFilled = true };
            var segment = new PolyLineSegment();
            for (int i = 1; i < contour.Length; i++) segment.Points.Add(new Point(contour[i].X, contour[i].Y));
            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);
        }
        _ink.Data = geometry;
        var color = Stroke.Attributes.EffectiveColor;
        _ink.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B));
    }
}
