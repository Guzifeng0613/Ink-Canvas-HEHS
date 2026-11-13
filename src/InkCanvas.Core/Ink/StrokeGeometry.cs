using System.Numerics;

namespace InkCanvas.Core.Ink;

/// <summary>
/// Builds filled regions for variable-width ink. Each centerline segment becomes its own
/// consistently-wound capsule. The renderer groups those regions with the non-zero winding
/// rule, making self-crossings a union rather than an even/odd boolean hole.
/// </summary>
public static class StrokeGeometry
{
    /// <summary>
    /// Segment capsules whose Winding-rule union is the complete stroke.
    /// Adjacent capsules overlap at their round caps; they are grouped once, so translucent ink
    /// is composited once per stroke even when the centerline crosses itself.
    /// </summary>
    public static Vector2[][] BuildUnionContours(InkStroke stroke)
    {
        if (stroke is null) throw new ArgumentNullException(nameof(stroke));
        var points = stroke.Points;
        if (points.Count == 0) return Array.Empty<Vector2[]>();
        if (points.Count == 1)
            return new[] { NormalizeWinding(Circle(points[0].Position, MathF.Max(points[0].Width, 0.5f) * 0.5f, 24)) };

        var result = new Vector2[points.Count - 1][];
        for (int i = 1; i < points.Count; i++)
        {
            result[i - 1] = BuildSegmentOutline(
                points[i - 1].Position, MathF.Max(points[i - 1].Width, 0.5f) * 0.5f,
                points[i].Position, MathF.Max(points[i].Width, 0.5f) * 0.5f);
        }
        return result;
    }

    /// <summary>
    /// A round-ended, optionally variable-width segment. Its contour is always positive-winding,
    /// regardless of the direction the pointer travelled.
    /// </summary>
    public static Vector2[] BuildSegmentOutline(Vector2 start, float startRadius, Vector2 end, float endRadius)
    {
        var delta = end - start;
        float length = delta.Length();
        if (length < 1e-4f)
            return NormalizeWinding(Circle(start, MathF.Max(startRadius, endRadius), 24));

        var tangent = delta / length;
        var normal = new Vector2(-tangent.Y, tangent.X);
        float a0 = MathF.Atan2(normal.Y, normal.X);
        var outline = new List<Vector2>(28);

        outline.Add(start + normal * startRadius);
        outline.Add(end + normal * endRadius);
        AppendArc(outline, end, endRadius, a0, -MathF.PI, 12);
        outline.Add(end - normal * endRadius);
        outline.Add(start - normal * startRadius);
        AppendArc(outline, start, startRadius, a0 + MathF.PI, -MathF.PI, 12);

        return NormalizeWinding(outline.ToArray());
    }

    public static float SignedArea(IReadOnlyList<Vector2> points)
    {
        if (points is null || points.Count < 3) return 0;
        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (float)(area * 0.5);
    }

    private static Vector2[] NormalizeWinding(Vector2[] points)
    {
        if (SignedArea(points) >= 0) return points;
        Array.Reverse(points);
        return points;
    }

    private static void AppendArc(List<Vector2> points, Vector2 center, float radius,
        float startAngle, float delta, int steps)
    {
        if (radius <= 0) return;
        for (int i = 1; i < steps; i++)
        {
            float angle = startAngle + delta * i / steps;
            points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }
    }

    public static Vector2[] Circle(Vector2 center, float radius, int segments)
    {
        var result = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float a = 2 * MathF.PI * i / segments;
            result[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
        }
        return result;
    }

    /// <summary>Legacy single-outline helper used by replay comparisons and compatibility tests.</summary>
    public static Vector2[] BuildOutline(InkStroke stroke)
    {
        if (stroke is null) throw new ArgumentNullException(nameof(stroke));
        var contours = BuildUnionContours(stroke);
        if (contours.Length == 0) return Array.Empty<Vector2>();
        if (contours.Length == 1) return contours[0];
        // Flattening is for diagnostics/equality only. Production rendering uses the separate
        // contours grouped with Winding; connecting this list would reintroduce self-intersections.
        return contours.SelectMany(c => c).ToArray();
    }
}