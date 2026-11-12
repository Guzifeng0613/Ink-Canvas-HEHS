using System.Numerics;

namespace InkCanvas.Core.Ink;

/// <summary>
/// Turns a variable-width centreline into a fillable outline.
/// <para>
/// Pure function of the stored points and their stored widths — no hidden state, no
/// dependence on whether the stroke is still being drawn. Rendering the same stroke
/// twice therefore produces identical geometry, which the flicker regression test relies on.
/// </para>
/// </summary>
public static class StrokeGeometry
{
    /// <summary>
    /// Builds a closed outline: forward along the left offset, back along the right offset,
    /// with round caps. Returns an empty array for an empty stroke.
    /// </summary>
    public static Vector2[] BuildOutline(InkStroke stroke)
    {
        if (stroke is null) throw new ArgumentNullException(nameof(stroke));
        int n = stroke.Count;
        if (n == 0) return Array.Empty<Vector2>();

        var pts = stroke.Points;
        if (n == 1) return Circle(pts[0].Position, MathF.Max(pts[0].Width, 0.5f) * 0.5f, 12);

        var left = new Vector2[n];
        var right = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 tangent;
            if (i == 0) tangent = pts[1].Position - pts[0].Position;
            else if (i == n - 1) tangent = pts[n - 1].Position - pts[n - 2].Position;
            else
            {
                var a = Vector2.Normalize(pts[i].Position - pts[i - 1].Position);
                var b = Vector2.Normalize(pts[i + 1].Position - pts[i].Position);
                tangent = a + b;
                if (tangent.LengthSquared() < 1e-8f) tangent = pts[i + 1].Position - pts[i - 1].Position;
            }

            if (tangent.LengthSquared() < 1e-8f) tangent = new Vector2(1, 0);
            tangent = Vector2.Normalize(tangent);
            var normal = new Vector2(-tangent.Y, tangent.X);
            float half = MathF.Max(pts[i].Width, 0.35f) * 0.5f;
            left[i] = pts[i].Position + normal * half;
            right[i] = pts[i].Position - normal * half;
        }

        var outline = new List<Vector2>(2 * n + 24);
        outline.AddRange(left);
        AppendCap(outline, pts[n - 1].Position, left[n - 1], right[n - 1]);
        for (int i = n - 1; i >= 0; i--) outline.Add(right[i]);
        AppendCap(outline, pts[0].Position, right[0], left[0]);
        return outline.ToArray();
    }

    /// <summary>Semicircular cap from one offset side to the other, around the endpoint.</summary>
    private static void AppendCap(List<Vector2> outline, Vector2 center, Vector2 from, Vector2 to)
    {
        float r = (from - center).Length();
        if (r < 0.2f) return;
        float a0 = MathF.Atan2(from.Y - center.Y, from.X - center.X);
        float a1 = MathF.Atan2(to.Y - center.Y, to.X - center.X);
        float delta = a1 - a0;
        while (delta <= -MathF.PI) delta += 2 * MathF.PI;
        while (delta > MathF.PI) delta -= 2 * MathF.PI;
        const int steps = 6;
        for (int s = 1; s < steps; s++)
        {
            float a = a0 + delta * s / steps;
            outline.Add(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
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
}