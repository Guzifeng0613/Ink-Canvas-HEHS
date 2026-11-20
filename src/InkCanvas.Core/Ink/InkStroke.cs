using System.Numerics;
using System.Runtime.InteropServices;

namespace InkCanvas.Core.Ink;

/// <summary>
/// A single stroke. Widths are assigned on arrival by a <see cref="TaperProfile"/> and
/// stored in each <see cref="InkPoint"/>; <see cref="Complete"/> deliberately does not
/// recompute them. That is what makes the committed stroke render identically to the
/// last live frame.
/// </summary>
public sealed class InkStroke
{
    private readonly List<InkPoint> _points = new();
    private TaperProfile _taper;
    private bool _boundsDirty = true;
    private Rect _bounds;

    public InkStroke(StrokeAttributes attributes)
    {
        Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        _taper = TaperProfile.Create(attributes.EffectiveTaper);
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public StrokeAttributes Attributes { get; }

    public IReadOnlyList<InkPoint> Points => _points;

    public int Count => _points.Count;

    public bool IsCompleted { get; private set; }

    /// <summary>Set when the stroke was produced by shape recognition rather than freehand.</summary>
    public bool IsRecognizedShape { get; set; }

    // --- HarmonyOS-port fields, preserved so .icink round-trips without loss. ---
    /// <summary>Layer ordering hint (null = not layered).</summary>
    public int? LayerOrder { get; set; }

    /// <summary>Pen nib: 0 = round, 1 = oblique (matches HarmonyOS PenNib).</summary>
    public int Nib { get; set; }

    public float NibRotation { get; set; }

    /// <summary>Recognition source for AI/geometry-generated strokes.</summary>
    public ShapeSourceInfo? ShapeSource { get; set; }

    /// <summary>Object frame (10 floats) used by board image/video transforms.</summary>
    public float[]? ObjectFrame { get; set; }

    public TaperKind TaperKind
    {
        get => _taper.Kind;
        set
        {
            if (_taper.Kind == value) return;
            _taper = TaperProfile.Create(value);
            // Re-evaluate from scratch only for an already-finished stroke (e.g. a settings
            // change applied to history). Live strokes never take this path.
            if (IsCompleted) RebuildWidths();
        }
    }

    public void AddPoint(float x, float y, float pressure = 0.5f, float timeMs = 0f)
    {
        // Width starts at 0 so the profile's monotonic "settle" rule can raise it.
        _points.Add(new InkPoint(x, y, pressure, 0f, timeMs));
        _taper.Apply(CollectionsMarshal.AsSpan(_points), _points.Count - 1, Attributes.BaseWidth);
        _boundsDirty = true;
    }

    /// <summary>
    /// Marks the stroke finished. Intentionally a no-op with respect to geometry.
    /// </summary>
    public void Complete() => IsCompleted = true;

    private void RebuildWidths()
    {
        var span = CollectionsMarshal.AsSpan(_points);
        for (int i = 0; i < span.Length; i++) span[i].Width = 0f;
        for (int i = 0; i < span.Length; i++) _taper.Apply(span, i, Attributes.BaseWidth);
        _boundsDirty = true;
    }

    public Rect Bounds
    {
        get
        {
            if (_boundsDirty)
            {
                _bounds = ComputeBounds();
                _boundsDirty = false;
            }
            return _bounds;
        }
    }

    private Rect ComputeBounds()
    {
        if (_points.Count == 0) return Rect.Empty;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        float maxHalf = 0f;
        foreach (var p in _points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Width > maxHalf) maxHalf = p.Width;
        }
        maxHalf *= 0.5f;
        return Rect.FromLTRB(minX - maxHalf, minY - maxHalf, maxX + maxHalf, maxY + maxHalf);
    }

    /// <summary>Total centreline length, used by replay and by the recogniser.</summary>
    public float ArcLength()
    {
        float total = 0f;
        for (int i = 1; i < _points.Count; i++)
            total += Vector2.Distance(_points[i - 1].Position, _points[i].Position);
        return total;
    }

    public void Transform(Matrix3x2 m, bool scaleWidth = true)
    {
        var span = CollectionsMarshal.AsSpan(_points);
        float widthScale = 1f;
        if (scaleWidth)
        {
            widthScale = MathF.Sqrt(MathF.Abs(m.M11 * m.M22 - m.M12 * m.M21));
            if (widthScale <= 0.0001f) widthScale = 1f;
        }
        for (int i = 0; i < span.Length; i++)
        {
            var v = Vector2.Transform(new Vector2(span[i].X, span[i].Y), m);
            span[i].X = v.X;
            span[i].Y = v.Y;
            span[i].Width *= widthScale;
        }
        Attributes.BaseWidth *= widthScale;
        _boundsDirty = true;
    }

    /// <summary>Shortest distance from a point to this stroke's centreline.</summary>
    public float DistanceTo(Vector2 p)
    {
        if (_points.Count == 0) return float.MaxValue;
        if (_points.Count == 1) return Vector2.Distance(p, _points[0].Position);
        float best = float.MaxValue;
        for (int i = 1; i < _points.Count; i++)
        {
            float d = DistanceToSegment(p, _points[i - 1].Position, _points[i].Position);
            if (d < best) best = d;
        }
        return best;
    }

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    public InkStroke Clone()
    {
        var copy = new InkStroke(Attributes.Clone()) { Id = Id, IsRecognizedShape = IsRecognizedShape };
        copy._points.AddRange(_points);
        copy.IsCompleted = IsCompleted;
        copy._boundsDirty = true;
        return copy;
    }

    /// <summary>Replace all points, preserving already-computed widths (used by persistence).</summary>
    public void RestorePoints(IEnumerable<InkPoint> points)
    {
        _points.Clear();
        _points.AddRange(points);
        _boundsDirty = true;
    }
}

/// <summary>Recognition source preserved by the .icink codec (HarmonyOS shape pipeline).</summary>
public sealed class ShapeSourceInfo
{
    public string GroupId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}

/// <summary>UI-independent rectangle.</summary>
public readonly record struct Rect(float Left, float Top, float Right, float Bottom)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public Vector2 Center => new((Left + Right) * 0.5f, (Top + Bottom) * 0.5f);

    public static Rect FromLTRB(float l, float t, float r, float b) => new(l, t, r, b);

    public bool Contains(Vector2 p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    public bool Intersects(Rect o) => !(o.Left > Right || o.Right < Left || o.Top > Bottom || o.Bottom < Top);

    public Rect Union(Rect o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        return new Rect(MathF.Min(Left, o.Left), MathF.Min(Top, o.Top),
                        MathF.Max(Right, o.Right), MathF.Max(Bottom, o.Bottom));
    }

    public Rect Inflate(float d) => new(Left - d, Top - d, Right + d, Bottom + d);
}