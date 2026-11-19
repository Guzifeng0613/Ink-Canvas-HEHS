using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System.Numerics;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace InkCanvas.Controls.Controls;

/// <summary>Shared vector thumbnails computed once per style with the actual ink engine.</summary>
public sealed class PenStyleIcon : UserControl
{
    private sealed record Sample(Vector2[][] Contours, double Opacity);
    private static readonly Dictionary<TaperKind, Sample[]> Samples = new();

    public PenStyleIcon(int style)
    {
        var kind = OriginalMenuCatalog.ToTaperKind(style);
        if (!Samples.TryGetValue(kind, out var samples))
        {
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 8, TaperKind = kind });
            float time = 0;
            for (int point = 0; point <= 32; point++)
            {
                float t = point / 32f;
                time += 20 - 17 * t;
                stroke.AddPoint(6 + 84 * t, 16 + MathF.Sin(t * MathF.PI * 2) * 6, .5f, time);
            }
            stroke.Complete();
            var layers = InkBrush.Layers(stroke);
            samples = new Sample[layers.Length];
            for (int layer = 0; layer < layers.Length; layer++)
                samples[layer] = new(StrokeGeometry.BuildUnionContours(stroke, layers[layer].WidthScale), layers[layer].Opacity / 255d);
            Samples.Add(kind, samples);
        }
        var canvas = new Canvas { Width = 96, Height = 32 };
        foreach (var sample in samples)
        {
            var path = new Path { Data = CreateGeometry(sample.Contours), Opacity = sample.Opacity };
            path.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.FillProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            canvas.Children.Add(path);
        }
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(this, OriginalMenuCatalog.PenStyles[Math.Clamp(style, 0, OriginalMenuCatalog.PenStyles.Count - 1)]);
        IsHitTestVisible = false;
    }

    internal static PathGeometry CreateGeometry(InkStroke stroke, float widthScale)
        => CreateGeometry(StrokeGeometry.BuildUnionContours(stroke, widthScale));

    private static PathGeometry CreateGeometry(Vector2[][] contours)
    {
        var geometry = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var contour in contours)
        {
            var figure = new PathFigure { StartPoint = new Point(contour[0].X, contour[0].Y), IsClosed = true, IsFilled = true };
            var segment = new PolyLineSegment();
            for (int point = 1; point < contour.Length; point++) segment.Points.Add(new Point(contour[point].X, contour[point].Y));
            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);
        }
        return geometry;
    }
}
