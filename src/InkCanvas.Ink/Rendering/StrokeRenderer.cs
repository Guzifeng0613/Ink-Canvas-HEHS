using InkCanvas.Core.Ink;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using System.Numerics;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Draws strokes with Win2D. Each stroke is a Winding-rule union of round-ended segment
/// capsules. This prevents a single stroke that crosses itself from becoming an even/odd
/// boolean hole, and ensures its translucent color is composited only once at overlaps.
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

        if (!fromCache && !storedInCache) geometry.Dispose();
    }

    private static CanvasGeometry BuildGeometry(CanvasDrawingSession session, InkStroke stroke)
    {
        var contours = StrokeGeometry.BuildUnionContours(stroke);
        if (contours.Length == 0) return null;

        var regions = new List<CanvasGeometry>(contours.Length);
        try
        {
            foreach (var outline in contours)
            {
                if (outline.Length < 3) continue;
                using var builder = new CanvasPathBuilder(session);
                builder.BeginFigure(outline[0]);
                for (int i = 1; i < outline.Length; i++) builder.AddLine(outline[i]);
                builder.EndFigure(CanvasFigureLoop.Closed);
                regions.Add(CanvasGeometry.CreatePath(builder));
            }

            if (regions.Count == 0) return null;
            if (regions.Count == 1)
            {
                var only = regions[0];
                regions.Clear(); // transfer ownership to the caller
                return only;
            }

            // All region contours are normalized to the same winding direction. Winding fill
            // makes overlapping regions a union; Alternate/even-odd would punch holes where a
            // centerline self-crosses. The group is filled once, so a highlighter does not gain
            // opacity where one stroke overlaps itself.
            return CanvasGeometry.CreateGroup(session, regions.ToArray(), CanvasFilledRegionDetermination.Winding);
        }
        finally
        {
            // CreateGroup owns a combined geometry, not the input geometries.
            foreach (var region in regions) region.Dispose();
        }
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