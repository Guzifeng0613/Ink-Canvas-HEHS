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
    public static Vector2[][] BuildUnionContours(InkStroke stroke, float widthScale = 1)
    {
        if (stroke is null) throw new ArgumentNullException(nameof(stroke));
        var points = stroke.Points;
        if (points.Count == 0) return Array.Empty<Vector2[]>();
        var nib = stroke.Attributes.NibTransform;
        if (points.Count == 1)
        {
            var contour = Circle(Vector2.Zero, MathF.Max(points[0].Width, 0.5f) * 0.5f * widthScale, 24);
            for (int i = 0; i < contour.Length; i++) contour[i] = points[0].Position + Vector2.TransformNormal(contour[i], nib);
            return new[] { NormalizeWinding(contour) };
        }

        var result = new Vector2[points.Count - 1][];
        for (int i = 1; i < points.Count; i++)
        {
            result[i - 1] = BuildSegmentOutline(
                points[i - 1].Position, MathF.Max(points[i - 1].Width, 0.5f) * 0.5f * widthScale,
                points[i].Position, MathF.Max(points[i].Width, 0.5f) * 0.5f * widthScale, nib);
        }
        return result;
    }

    /// <summary>
    /// A round-ended, optionally variable-width segment. Its contour is always positive-winding,
    /// regardless of the direction the pointer travelled.
    /// </summary>
    public static Vector2[] BuildSegmentOutline(Vector2 start, float startRadius, Vector2 end, float endRadius, Matrix3x2? nib = null)
    {
        var buffer = new Vector2[26];
        int count = WriteSegmentOutline(start, startRadius, end, endRadius, buffer, nib);
        return count == buffer.Length ? buffer : buffer.AsSpan(0, count).ToArray();
    }

    public static int WriteSegmentOutline(Vector2 start, float startRadius, Vector2 end, float endRadius,
        Span<Vector2> outline, Matrix3x2? nib)
    {
        if (nib is null || nib == Matrix3x2.Identity) return WriteSegmentOutline(start, startRadius, end, endRadius, outline);
        Matrix3x2.Invert(nib.Value, out var inverse);
        int count = WriteSegmentOutline(Vector2.Zero, startRadius, Vector2.TransformNormal(end - start, inverse), endRadius, outline);
        for (int i = 0; i < count; i++) outline[i] = start + Vector2.TransformNormal(outline[i], nib.Value);
        return count;
    }

    /// <summary>Writes the same capsule into caller-owned scratch memory, avoiding per-sample arrays.</summary>
    public static int WriteSegmentOutline(Vector2 start, float startRadius, Vector2 end, float endRadius, Span<Vector2> outline)
    {
        if (outline.Length < 26) throw new ArgumentException("A capsule needs space for 26 vertices.", nameof(outline));
        var delta = end - start;
        float length = delta.Length();
        if (length < 1e-4f)
        {
            float radius = MathF.Max(startRadius, endRadius);
            for (int i = 0; i < 24; i++)
            {
                float angle = 2 * MathF.PI * i / 24;
                outline[i] = start + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }
            return 24;
        }

        var tangent = delta / length;
        var normal = new Vector2(-tangent.Y, tangent.X);
        float a0 = MathF.Atan2(normal.Y, normal.X);
        int count = 0;
        outline[count++] = start + normal * startRadius;
        outline[count++] = end + normal * endRadius;
        AppendArc(outline, ref count, end, endRadius, a0);
        outline[count++] = end - normal * endRadius;
        outline[count++] = start - normal * startRadius;
        AppendArc(outline, ref count, start, startRadius, a0 + MathF.PI);
        double area = 0;
        for (int i = 0; i < count; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % count];
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        if (area < 0) outline[..count].Reverse();
        return count;
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

    private static void AppendArc(Span<Vector2> points, ref int count, Vector2 center, float radius, float startAngle)
    {
        if (radius <= 0) return;
        for (int i = 1; i < 12; i++)
        {
            float angle = startAngle - MathF.PI * i / 12;
            points[count++] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
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
