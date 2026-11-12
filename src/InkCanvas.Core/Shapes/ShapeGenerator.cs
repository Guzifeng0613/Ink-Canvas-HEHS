using InkCanvas.Core.Ink;
using System.Numerics;

namespace InkCanvas.Core.Shapes;

/// <summary>
/// The preset shape catalogue, ported from the original <c>drawingShapeMode</c> switch.
/// </summary>
public enum ShapeKind
{
    Line,
    DashedLine,
    DotLine,
    Arrow,
    ParallelLine,

    /// <summary>Both axes extend either side of the origin (original mode 11).</summary>
    CoordinateBoth,
    /// <summary>x one-way, y both (mode 12).</summary>
    CoordinateXOneWay,
    /// <summary>x both, y one-way (mode 13).</summary>
    CoordinateYOneWay,
    /// <summary>First quadrant only (mode 14).</summary>
    CoordinateFirstQuadrant,
    /// <summary>3D axes with a 45° z-axis (mode 17).</summary>
    Coordinate3D,

    Rectangle,
    RectangleFromCenter,
    Ellipse,
    Circle,
    EllipseFromCenter,
    EllipseWithFoci,
    DashedCircle,

    Hyperbola,
    HyperbolaWithFoci,
    ParabolaRight,
    ParabolaWithFoci,
    ParabolaUp,

    Cylinder,
    Cone,
    Cuboid,
}

/// <summary>
/// Builds preset geometry as ordinary <see cref="InkStroke"/>s, so shapes share the same
/// rendering, hit-testing, transform and persistence paths as freehand ink.
/// </summary>
public static class ShapeGenerator
{
    private const int EllipseSegments = 72;

    public static List<InkStroke> Generate(ShapeKind kind, Vector2 start, Vector2 end, StrokeAttributes attrs)
    {
        var result = new List<InkStroke>();
        var a = attrs ?? new StrokeAttributes();
        float w = end.X - start.X, h = end.Y - start.Y;
        var center = (start + end) * 0.5f;
        float rx = MathF.Abs(w) * 0.5f, ry = MathF.Abs(h) * 0.5f;

        switch (kind)
        {
            case ShapeKind.Line:
                result.Add(Stroke(a, start, end));
                break;

            case ShapeKind.DashedLine:
                result.AddRange(Dashed(a, start, end, 12f, 8f));
                break;

            case ShapeKind.DotLine:
                result.AddRange(Dashed(a, start, end, 2f, 7f));
                break;

            case ShapeKind.Arrow:
                result.Add(Arrow(a, start, end));
                break;

            case ShapeKind.ParallelLine:
                {
                    var dir = end - start;
                    if (dir.LengthSquared() < 1e-4f) break;
                    var n = Vector2.Normalize(new Vector2(-dir.Y, dir.X)) * 10f;
                    result.Add(Stroke(a, start + n, end + n));
                    result.Add(Stroke(a, start - n, end - n));
                    break;
                }

            case ShapeKind.CoordinateBoth:
                result.Add(Arrow(a, new Vector2(2 * start.X - (end.X - 20), start.Y), new Vector2(end.X, start.Y)));
                result.Add(Arrow(a, new Vector2(start.X, 2 * start.Y - (end.Y + 20)), new Vector2(start.X, end.Y)));
                break;

            case ShapeKind.CoordinateXOneWay:
                if (MathF.Abs(w) < 0.01f) break;
                result.Add(Arrow(a, new Vector2(start.X + MathF.Sign(-w) * 25, start.Y), new Vector2(end.X, start.Y)));
                result.Add(Arrow(a, new Vector2(start.X, 2 * start.Y - (end.Y + 20)), new Vector2(start.X, end.Y)));
                break;

            case ShapeKind.CoordinateYOneWay:
                if (MathF.Abs(h) < 0.01f) break;
                result.Add(Arrow(a, new Vector2(2 * start.X - (end.X - 20), start.Y), new Vector2(end.X, start.Y)));
                result.Add(Arrow(a, new Vector2(start.X, start.Y + MathF.Sign(-h) * 25), new Vector2(start.X, end.Y)));
                break;

            case ShapeKind.CoordinateFirstQuadrant:
                if (MathF.Abs(w) < 0.01f || MathF.Abs(h) < 0.01f) break;
                result.Add(Arrow(a, new Vector2(start.X + MathF.Sign(-w) * 25, start.Y), new Vector2(end.X, start.Y)));
                result.Add(Arrow(a, new Vector2(start.X, start.Y + MathF.Sign(-h) * 25), new Vector2(start.X, end.Y)));
                break;

            case ShapeKind.Coordinate3D:
                {
                    result.Add(Arrow(a, start, new Vector2(start.X + MathF.Abs(w), start.Y)));
                    result.Add(Arrow(a, start, new Vector2(start.X, start.Y - MathF.Abs(h))));
                    float d = (MathF.Abs(w) + MathF.Abs(h)) * 0.5f;
                    result.Add(Arrow(a, start, new Vector2(start.X - d / 1.76f, start.Y + d / 1.76f)));
                    break;
                }

            case ShapeKind.Rectangle:
                result.Add(Stroke(a, start, new Vector2(start.X, end.Y), end, new Vector2(end.X, start.Y), start));
                break;

            case ShapeKind.RectangleFromCenter:
                result.Add(Stroke(a, center - new Vector2(rx, ry), center + new Vector2(rx, -ry),
                                     center + new Vector2(rx, ry), center + new Vector2(-rx, ry), center - new Vector2(rx, ry)));
                result.Add(Stroke(a, center - new Vector2(2, 0), center + new Vector2(2, 0)));
                break;

            case ShapeKind.Ellipse:
                result.Add(EllipseStroke(a, center, rx, ry));
                break;

            case ShapeKind.Circle:
                {
                    float r = MathF.Max(rx, ry);
                    result.Add(EllipseStroke(a, center, r, r));
                    break;
                }

            case ShapeKind.EllipseFromCenter:
                result.Add(EllipseStroke(a, start, rx, ry));
                result.Add(Stroke(a, start - new Vector2(2, 0), start + new Vector2(2, 0)));
                break;

            case ShapeKind.EllipseWithFoci:
                {
                    result.Add(EllipseStroke(a, center, rx, ry));
                    float aa = MathF.Max(rx, ry), bb = MathF.Min(rx, ry);
                    float c = MathF.Sqrt(MathF.Max(0, aa * aa - bb * bb));
                    var axis = rx >= ry ? new Vector2(1, 0) : new Vector2(0, 1);
                    foreach (var f in new[] { center - axis * c, center + axis * c })
                        result.Add(Stroke(a, f - new Vector2(2.5f, 0), f + new Vector2(2.5f, 0)));
                    break;
                }

            case ShapeKind.DashedCircle:
                {
                    float r = MathF.Max(rx, ry);
                    result.AddRange(DashedEllipse(a, center, r, r, 10f, 7f));
                    break;
                }

            case ShapeKind.Hyperbola:
            case ShapeKind.HyperbolaWithFoci:
                {
                    float aa = MathF.Max(rx * 0.5f, 4f), bb = MathF.Max(ry * 0.5f, 4f);
                    foreach (int sign in new[] { -1, 1 })
                    {
                        var pts = new List<Vector2>();
                        for (int i = 0; i <= 48; i++)
                        {
                            float t = -1.6f + 3.2f * i / 48f;
                            pts.Add(center + new Vector2(sign * aa * MathF.Cosh(t), bb * MathF.Sinh(t)));
                        }
                        result.Add(Stroke(a, pts.ToArray()));
                    }
                    if (kind == ShapeKind.HyperbolaWithFoci)
                    {
                        float c = MathF.Sqrt(aa * aa + bb * bb);
                        foreach (var f in new[] { center - new Vector2(c, 0), center + new Vector2(c, 0) })
                            result.Add(Stroke(a, f - new Vector2(2.5f, 0), f + new Vector2(2.5f, 0)));
                    }
                    break;
                }

            case ShapeKind.ParabolaRight:
            case ShapeKind.ParabolaWithFoci:
                {
                    float p = MathF.Max(rx, 6f);
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 48; i++)
                    {
                        float y = -MathF.Abs(h) * 0.5f + MathF.Abs(h) * i / 48f;
                        pts.Add(start + new Vector2(y * y / (2 * p), y));
                    }
                    result.Add(Stroke(a, pts.ToArray()));
                    if (kind == ShapeKind.ParabolaWithFoci)
                    {
                        var f = start + new Vector2(p * 0.5f, 0);
                        result.Add(Stroke(a, f - new Vector2(2.5f, 0), f + new Vector2(2.5f, 0)));
                    }
                    break;
                }

            case ShapeKind.ParabolaUp:
                {
                    float p = MathF.Max(ry, 6f);
                    var pts = new List<Vector2>();
                    for (int i = 0; i <= 48; i++)
                    {
                        float x = -MathF.Abs(w) * 0.5f + MathF.Abs(w) * i / 48f;
                        pts.Add(new Vector2(end.X + x, end.Y - x * x / (2 * p)));
                    }
                    result.Add(Stroke(a, pts.ToArray()));
                    break;
                }

            case ShapeKind.Cylinder:
                {
                    float er = ry * 0.22f;
                    result.Add(EllipseStroke(a, new Vector2(center.X, start.Y + er), rx, er));
                    result.AddRange(DashedEllipse(a, new Vector2(center.X, end.Y - er), rx, er, 9f, 6f));
                    result.Add(Stroke(a, new Vector2(start.X, start.Y + er), new Vector2(start.X, end.Y - er)));
                    result.Add(Stroke(a, new Vector2(end.X, start.Y + er), new Vector2(end.X, end.Y - er)));
                    break;
                }

            case ShapeKind.Cone:
                {
                    float er = ry * 0.22f;
                    var apex = new Vector2(center.X, start.Y);
                    result.AddRange(DashedEllipse(a, new Vector2(center.X, end.Y - er), rx, er, 9f, 6f));
                    result.Add(Stroke(a, apex, new Vector2(start.X, end.Y - er)));
                    result.Add(Stroke(a, apex, new Vector2(end.X, end.Y - er)));
                    break;
                }

            case ShapeKind.Cuboid:
                {
                    var offset = new Vector2(MathF.Abs(w) * 0.28f, -MathF.Abs(h) * 0.28f);
                    var f0 = start; var f1 = new Vector2(end.X, start.Y);
                    var f2 = end; var f3 = new Vector2(start.X, end.Y);
                    result.Add(Stroke(a, f0, f1, f2, f3, f0));
                    result.Add(Stroke(a, f0 + offset, f1 + offset, f2 + offset));
                    result.Add(Stroke(a, f1, f1 + offset));
                    result.Add(Stroke(a, f2, f2 + offset));
                    result.AddRange(Dashed(a, f0, f0 + offset, 8f, 6f));
                    result.AddRange(Dashed(a, f3, f3 + offset, 8f, 6f));
                    result.AddRange(Dashed(a, f0 + offset, f3 + offset, 8f, 6f));
                    result.AddRange(Dashed(a, f3 + offset, f2 + offset, 8f, 6f));
                    break;
                }
        }

        foreach (var s in result)
        {
            s.IsRecognizedShape = true;
            s.Complete();
        }
        return result;
    }

    // ---- primitives -------------------------------------------------------

    public static InkStroke Stroke(StrokeAttributes attrs, params Vector2[] pts)
    {
        var s = new InkStroke(attrs.Clone());
        float t = 0;
        foreach (var p in pts)
        {
            s.AddPoint(p.X, p.Y, 0.5f, t);
            t += 8f;
        }
        return s;
    }

    /// <summary>A line with a solid arrow head at <paramref name="end"/>.</summary>
    public static InkStroke Arrow(StrokeAttributes attrs, Vector2 start, Vector2 end)
    {
        var dir = end - start;
        if (dir.LengthSquared() < 1e-4f) return Stroke(attrs, start, end);
        dir = Vector2.Normalize(dir);
        var n = new Vector2(-dir.Y, dir.X);
        float head = MathF.Min(16f, MathF.Max(6f, (end - start).Length() * 0.12f));
        var barb1 = end - dir * head + n * head * 0.45f;
        var barb2 = end - dir * head - n * head * 0.45f;
        return Stroke(attrs, start, end, barb1, end, barb2);
    }

    public static List<InkStroke> Dashed(StrokeAttributes attrs, Vector2 start, Vector2 end, float dash, float gap)
    {
        var result = new List<InkStroke>();
        var delta = end - start;
        float len = delta.Length();
        if (len < 0.5f) return result;
        var dir = delta / len;
        float pos = 0;
        while (pos < len)
        {
            float stop = MathF.Min(pos + dash, len);
            result.Add(Stroke(attrs, start + dir * pos, start + dir * stop));
            pos = stop + gap;
        }
        return result;
    }

    public static InkStroke EllipseStroke(StrokeAttributes attrs, Vector2 center, float rx, float ry)
        => Stroke(attrs, EllipsePoints(center, MathF.Max(rx, 0.5f), MathF.Max(ry, 0.5f), 0, 2 * MathF.PI, EllipseSegments));

    public static Vector2[] EllipsePoints(Vector2 center, float rx, float ry, float from, float to, int segments)
    {
        var pts = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = from + (to - from) * i / segments;
            pts[i] = center + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }
        return pts;
    }

    public static List<InkStroke> DashedEllipse(StrokeAttributes attrs, Vector2 center, float rx, float ry, float dash, float gap)
    {
        var result = new List<InkStroke>();
        rx = MathF.Max(rx, 0.5f); ry = MathF.Max(ry, 0.5f);
        // Approximate perimeter well enough to space dashes evenly in angle.
        float perimeter = MathF.PI * (3 * (rx + ry) - MathF.Sqrt((3 * rx + ry) * (rx + 3 * ry)));
        int steps = Math.Max(8, (int)(perimeter / (dash + gap)));
        for (int i = 0; i < steps; i++)
        {
            float a0 = 2 * MathF.PI * i / steps;
            float a1 = a0 + 2 * MathF.PI / steps * (dash / (dash + gap));
            result.Add(Stroke(attrs, EllipsePoints(center, rx, ry, a0, a1, 6)));
        }
        return result;
    }
}