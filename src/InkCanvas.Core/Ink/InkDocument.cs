using System.Numerics;

namespace InkCanvas.Core.Ink;

/// <summary>
/// The stroke collection for one surface (a board page, or the screen overlay).
/// Raises <see cref="Changed"/> so the renderer knows when to rebuild its cached layer.
/// </summary>
public sealed class InkDocument
{
    private readonly List<InkStroke> _strokes = new();

    public IReadOnlyList<InkStroke> Strokes => _strokes;

    public int Count => _strokes.Count;

    public event Action Changed;

    public void Add(InkStroke stroke)
    {
        if (stroke is null) return;
        _strokes.Add(stroke);
        Changed?.Invoke();
    }

    public void AddRange(IEnumerable<InkStroke> strokes)
    {
        bool any = false;
        foreach (var s in strokes)
        {
            if (s is null) continue;
            _strokes.Add(s);
            any = true;
        }
        if (any) Changed?.Invoke();
    }

    public bool Remove(InkStroke stroke)
    {
        bool removed = _strokes.Remove(stroke);
        if (removed) Changed?.Invoke();
        return removed;
    }

    public int RemoveAll(IEnumerable<InkStroke> strokes)
    {
        var set = new HashSet<InkStroke>(strokes);
        int n = _strokes.RemoveAll(set.Contains);
        if (n > 0) Changed?.Invoke();
        return n;
    }

    public void Insert(int index, InkStroke stroke)
    {
        _strokes.Insert(Math.Clamp(index, 0, _strokes.Count), stroke);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_strokes.Count == 0) return;
        _strokes.Clear();
        Changed?.Invoke();
    }

    /// <summary>Notify that stroke geometry mutated in place (e.g. a transform).</summary>
    public void Touch() => Changed?.Invoke();

    public Rect Bounds
    {
        get
        {
            var r = Rect.Empty;
            foreach (var s in _strokes) r = r.Union(s.Bounds);
            return r;
        }
    }

    /// <summary>Strokes whose centreline comes within <paramref name="radius"/> of a point.</summary>
    public List<InkStroke> HitTest(Vector2 point, float radius)
    {
        var hits = new List<InkStroke>();
        foreach (var s in _strokes)
        {
            if (!s.Bounds.Inflate(radius).Contains(point)) continue;
            if (s.DistanceTo(point) <= radius + s.Attributes.BaseWidth * 0.5f) hits.Add(s);
        }
        return hits;
    }

    /// <summary>Strokes with any sample inside the rectangle (lasso/rectangle selection).</summary>
    public List<InkStroke> HitTest(Rect rect)
    {
        var hits = new List<InkStroke>();
        foreach (var s in _strokes)
        {
            if (!s.Bounds.Intersects(rect)) continue;
            foreach (var p in s.Points)
            {
                if (rect.Contains(p.Position)) { hits.Add(s); break; }
            }
        }
        return hits;
    }

    /// <summary>
    /// Erase by area: removes whole strokes touched by the eraser disc. Matches the
    /// original's stroke-level (not pixel-level) eraser behaviour.
    /// </summary>
    public List<InkStroke> EraseAt(Vector2 point, float radius)
    {
        var hits = HitTest(point, radius);
        if (hits.Count > 0) RemoveAll(hits);
        return hits;
    }

    public static void Transform(IEnumerable<InkStroke> strokes, Matrix3x2 m)
    {
        foreach (var s in strokes) s.Transform(m);
    }
}