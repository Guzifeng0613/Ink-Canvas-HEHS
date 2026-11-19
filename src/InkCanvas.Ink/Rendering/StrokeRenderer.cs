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
    private sealed record Entry(InkStroke Stroke, long Version, Matrix3x2 Nib, int PointCount, CanvasGeometry Geometry, LinkedListNode<Guid> Node);
    private readonly Dictionary<Guid, Entry> _cache = new();
    private readonly LinkedList<Guid> _usage = new();
    private const int MaxCachedPoints = 100_000;
    private int _cachedPoints;
    private readonly List<Guid> _expired = new();
    private readonly HashSet<InkStroke> _retained = new();
    private sealed class LiveEntry
    {
        public InkStroke Stroke;
        public long Version;
        public Matrix3x2 Nib;
        public int Count, StableSegments;
        public readonly List<CanvasGeometry> Chunks = new();
    }
    private readonly Dictionary<Guid, LiveEntry> _liveCache = new();
    public int CachedGeometryCount => _cache.Count;
    public long GeometryBuildCount { get; private set; }
    public long SegmentBuildCount { get; private set; }

    /// <summary>Geometry for finished strokes is immutable, so it is safe to cache.</summary>
    public void Draw(CanvasDrawingSession session, InkStroke stroke, Matrix3x2 transform)
    {
        if (stroke is null || stroke.Count == 0) return;

        var color = stroke.Attributes.EffectiveColor;
        var winColor = Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

        bool fromCache = stroke.IsCompleted && _cache.TryGetValue(stroke.Id, out var cached)
            && ReferenceEquals(cached.Stroke, stroke) && cached.Version == stroke.GeometryVersion && cached.Nib == stroke.Attributes.NibTransform;
        CanvasGeometry geometry;
        if (fromCache)
        {
            var entry = _cache[stroke.Id];
            _usage.Remove(entry.Node);
            _usage.AddLast(entry.Node);
            geometry = entry.Geometry;
        }
        else
        {
            Invalidate(stroke);
            geometry = !stroke.IsCompleted || _liveCache.ContainsKey(stroke.Id)
                ? BuildLiveGeometry(session, stroke) : BuildGeometry(session, stroke);
            GeometryBuildCount++;
        }
        if (geometry is null) return;
        if (stroke.IsCompleted) RemoveLive(stroke.Id);

        bool storedInCache = false;
        if (!fromCache && stroke.IsCompleted && stroke.Count <= MaxCachedPoints)
        {
            while (_usage.First is not null && (_cachedPoints + stroke.Count > MaxCachedPoints || _cache.Count >= 2048))
                Remove(_usage.First.Value);
            _cache[stroke.Id] = new Entry(stroke, stroke.GeometryVersion, stroke.Attributes.NibTransform, stroke.Count, geometry, _usage.AddLast(stroke.Id));
            _cachedPoints += stroke.Count;
            storedInCache = true;
        }

        var previous = session.Transform;
        try
        {
            session.Transform = transform;
            session.FillGeometry(geometry, winColor);
        }
        finally
        {
            session.Transform = previous;
            if (!fromCache && !storedInCache) geometry.Dispose();
        }
    }

    private CanvasGeometry BuildLiveGeometry(CanvasDrawingSession session, InkStroke stroke)
    {
        if (_liveCache.TryGetValue(stroke.Id, out var entry)
            && (!ReferenceEquals(entry.Stroke, stroke) || stroke.Count < entry.Count
                || entry.Nib != stroke.Attributes.NibTransform || stroke.GeometryVersion - entry.Version != stroke.Count - entry.Count))
        { RemoveLive(stroke.Id); entry = null; }
        if (entry is null)
        {
            if (_liveCache.Count >= 16) RemoveLive(_liveCache.Keys.First());
            _liveCache[stroke.Id] = entry = new LiveEntry { Stroke = stroke, Nib = stroke.Attributes.NibTransform };
        }
        // Tail taper can revise the last ten points; only freeze complete chunks behind that window.
        int stable = Math.Max(0, stroke.StablePointCount - 1);
        while (stable - entry.StableSegments >= 64)
        {
            entry.Chunks.Add(BuildGeometry(session, stroke, entry.StableSegments, 64));
            entry.StableSegments += 64;
        }
        entry.Count = stroke.Count; entry.Version = stroke.GeometryVersion;
        var tail = BuildGeometry(session, stroke, entry.StableSegments);
        if (entry.Chunks.Count == 0) return tail;
        var parts = new CanvasGeometry[entry.Chunks.Count + (tail is null ? 0 : 1)];
        entry.Chunks.CopyTo(parts);
        if (tail is not null) parts[^1] = tail;
        // Fill the entire group once so crossings keep the original highlighter opacity.
        try { return CanvasGeometry.CreateGroup(session, parts, CanvasFilledRegionDetermination.Winding); }
        finally { tail?.Dispose(); }
    }

    private CanvasGeometry BuildGeometry(CanvasDrawingSession session, InkStroke stroke, int firstSegment = 0, int segmentCount = int.MaxValue)
    {
        if (stroke.Count == 0) return null;
        int endSegment = Math.Max(1, stroke.Count - 1);
        if (firstSegment >= endSegment) return null;
        int lastSegment = (int)Math.Min(endSegment, (long)firstSegment + segmentCount);
        SegmentBuildCount += lastSegment - firstSegment;

        // All capsule contours share positive winding, so one Winding path is their union.
        // One native path per stroke avoids allocating a COM geometry for every sample pair.
        using var builder = new CanvasPathBuilder(session);
        builder.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Winding);
        Span<Vector2> scratch = stackalloc Vector2[26];
        var nib = stroke.Attributes.NibTransform;
        for (int segment = firstSegment; segment < lastSegment; segment++)
        {
            var start = stroke.Points[segment];
            var end = stroke.Points[Math.Min(segment + 1, stroke.Count - 1)];
            int count = StrokeGeometry.WriteSegmentOutline(start.Position, MathF.Max(start.Width, 0.5f) * 0.5f,
                end.Position, MathF.Max(end.Width, 0.5f) * 0.5f, scratch, nib);
            var outline = scratch[..count];
            if (outline.Length < 3) continue;
            builder.BeginFigure(outline[0]);
            for (int i = 1; i < outline.Length; i++) builder.AddLine(outline[i]);
            builder.EndFigure(CanvasFigureLoop.Closed);
        }
        return CanvasGeometry.CreatePath(builder);
    }

    /// <summary>Evict removed or edited strokes while keeping native geometry for unchanged ink.</summary>
    public void Retain(IReadOnlyList<InkStroke> strokes)
    {
        if (_cache.Count == 0) return;
        _retained.Clear();
        foreach (var stroke in strokes) _retained.Add(stroke);
        _expired.Clear();
        foreach (var (id, entry) in _cache)
            if (!_retained.Contains(entry.Stroke) || entry.Version != entry.Stroke.GeometryVersion) _expired.Add(id);
        foreach (var id in _expired)
        {
            Remove(id);
        }
        _retained.Clear();
        _expired.Clear();
    }

    public void Invalidate(InkStroke stroke)
    {
        if (stroke is not null) Remove(stroke.Id);
    }

    private void Remove(Guid id)
    {
        if (!_cache.Remove(id, out var entry)) return;
        _usage.Remove(entry.Node);
        _cachedPoints -= entry.PointCount;
        entry.Geometry.Dispose();
    }

    public void InvalidateAll()
    {
        foreach (var entry in _cache.Values) entry.Geometry.Dispose();
        _cache.Clear();
        _usage.Clear();
        _cachedPoints = 0;
        foreach (var id in _liveCache.Keys.ToArray()) RemoveLive(id);
    }

    public void RetainLive(IEnumerable<InkStroke> strokes)
    {
        if (_liveCache.Count == 0) return;
        _retained.Clear();
        foreach (var stroke in strokes) _retained.Add(stroke);
        foreach (var id in _liveCache.Where(p => !_retained.Contains(p.Value.Stroke)).Select(p => p.Key).ToArray()) RemoveLive(id);
        _retained.Clear();
    }
    private void RemoveLive(Guid id)
    {
        if (!_liveCache.Remove(id, out var entry)) return;
        foreach (var geometry in entry.Chunks) geometry.Dispose();
    }

    public void Dispose() => InvalidateAll();
}
