using InkCanvas.Core.Ink;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System.Numerics;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Draws strokes with Win2D. Geometry comes from <see cref="StrokeGeometry"/>, i.e. purely
/// from stored points and stored widths, so a committed stroke draws exactly as it did on
/// the last live frame.
/// </summary>
public sealed class StrokeRenderer : IDisposable
{
    private readonly Dictionary<Guid, CanvasGeometry> _cache = new();

    /// <summary>Geometry for finished strokes is immutable, so it is safe to cache.</summary>
    public void Draw(CanvasDrawingSession session, InkStroke stroke, Matrix3x2 transform)
    {
        if (stroke is null || stroke.Count == 0) return;

        var color = stroke.Attributes.EffectiveColor;
        var winColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

        CanvasGeometry cached = null;
        bool fromCache = stroke.IsCompleted && _cache.TryGetValue(stroke.Id, out cached);
        CanvasGeometry geometry = fromCache ? cached : BuildGeometry(session, stroke);
        if (geometry is null) return;

        bool storedInCache = false;
        if (!fromCache && stroke.IsCompleted)
        {
            _cache[stroke.Id] = geometry;
            storedInCache = true;
        }

        var previous = session.Transform;
        session.Transform = transform;
        session.FillGeometry(geometry, winColor);
        session.Transform = previous;

        // Dispose only geometries we own: neither already cached nor just stored.
        if (!fromCache && !storedInCache) geometry.Dispose();
    }

    private static CanvasGeometry BuildGeometry(CanvasDrawingSession session, InkStroke stroke)
    {
        var outline = StrokeGeometry.BuildOutline(stroke);
        if (outline.Length < 3) return null;

        using var builder = new CanvasPathBuilder(session);
        builder.BeginFigure(outline[0]);
        for (int i = 1; i < outline.Length; i++) builder.AddLine(outline[i]);
        builder.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(builder);
    }

    public void Invalidate(InkStroke stroke)
    {
        if (stroke is not null && _cache.Remove(stroke.Id, out var g)) g.Dispose();
    }

    public void InvalidateAll()
    {
        foreach (var g in _cache.Values) g.Dispose();
        _cache.Clear();
    }

    public void Dispose() => InvalidateAll();
}