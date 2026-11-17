using InkCanvas.Core.Ink;
using InkCanvas.Core.Recognition;
using InkCanvas.Core.Shapes;
using System.Numerics;

internal static class ShapeVectors
{
    private sealed record AttrSpec(uint ColorPacked, float BaseWidth, bool IsHighlighter, byte HighlighterAlpha, TaperKind Kind)
    {
        public StrokeAttributes ToAttributes() => new()
        {
            Color = InkColor.FromPackedArgb(ColorPacked),
            BaseWidth = BaseWidth,
            IsHighlighter = IsHighlighter,
            HighlighterAlpha = HighlighterAlpha,
            TaperKind = Kind
        };

        public object Input() => new
        {
            ColorPacked,
            BaseWidth,
            IsHighlighter,
            HighlighterAlpha = (int)HighlighterAlpha,
            TaperKind = (int)Kind
        };
    }

    private static readonly AttrSpec Default = new(0xFF000000u, 3f, false, 96, TaperKind.None);
    private static readonly AttrSpec Tail = new(0xFF000000u, 4f, false, 96, TaperKind.Tail);
    private static readonly AttrSpec Highlighter = new(0xFFFFD000u, 6f, true, 96, TaperKind.Velocity);
    private static readonly AttrSpec Colored = new(0xFF2060C0u, 5.5f, false, 96, TaperKind.None);

    public static void Write(string dir)
    {
        JsonWriter.Write(dir, "shapes.json", new
        {
            id = "shapes",
            entry = "harmony/core/src/main/ets/shapes/ShapeGenerator.ts",
            cases = ShapeCases()
        });
        JsonWriter.Write(dir, "recognizer.json", new
        {
            id = "recognizer",
            entry = "harmony/core/src/main/ets/recognition/ShapeRecognizer.ts",
            cases = RecognizerCases()
        });
    }

    private static List<object> ShapeCases()
    {
        return new List<object>
        {
            // 0 Line (plus taper / highlighter attribute handling).
            Gen("line-basic", 0, 100, 100, 260, 180, Default),
            Gen("line-negative", 0, 300, 300, 120, 220, Default),
            Gen("line-tail-taper", 0, 100, 100, 180, 100, Tail),
            Gen("line-highlighter", 0, 80, 80, 220, 160, Highlighter),

            // 1 DashedLine.
            Gen("dashed-line", 1, 100, 100, 200, 100, Default),
            Gen("dashed-line-negative", 1, 300, 300, 180, 240, Default),
            Gen("dashed-line-short", 1, 100, 100, 100.2f, 100, Default),

            // 2 DotLine.
            Gen("dot-line", 2, 100, 100, 180, 100, Default),
            Gen("dot-line-negative", 2, 200, 200, 140, 140, Default),
            Gen("dot-line-zero", 2, 100, 100, 100, 100, Default),

            // 3 Arrow.
            Gen("arrow", 3, 100, 100, -40, 180, Default),
            Gen("arrow-degenerate", 3, 100, 100, 100, 100, Default),

            // 4 ParallelLine (angle snapping).
            Gen("parallel-horizontal", 4, 100, 100, 300, 100, Default),
            Gen("parallel-30", 4, 100, 100, 300, -20, Default),
            Gen("parallel-vertical", 4, 100, 100, 102, 300, Default),
            Gen("parallel-zero", 4, 100, 100, 100, 100, Default),

            // 5..9 coordinate systems.
            Gen("coordinate-both", 5, 200, 200, 320, 120, Default),
            Gen("coordinate-both-degenerate", 5, 100, 100, 100, 100, Default),
            Gen("coordinate-x-one-way", 6, 200, 200, 320, 260, Default),
            Gen("coordinate-x-one-way-degenerate", 6, 200, 200, 200, 260, Default),
            Gen("coordinate-y-one-way", 7, 200, 200, 100, 80, Default),
            Gen("coordinate-y-one-way-degenerate", 7, 200, 200, 100, 200, Default),
            Gen("coordinate-first-quadrant", 8, 200, 200, 320, 120, Default),
            Gen("coordinate-first-quadrant-degenerate", 8, 200, 200, 200, 120, Default),
            Gen("coordinate-3d", 9, 200, 200, 320, 120, Default),
            Gen("coordinate-3d-negative", 9, 200, 200, 80, 260, Default),

            // 10..16 rectangles / ellipses.
            Gen("rectangle", 10, 100, 100, 250, 180, Default),
            Gen("rectangle-from-center", 11, 150, 150, 220, 200, Default),
            Gen("ellipse", 12, 100, 100, 280, 180, Colored),
            Gen("ellipse-degenerate", 12, 100, 100, 100, 100, Default),
            Gen("circle", 13, 150, 150, 190, 180, Default),
            Gen("circle-negative", 13, 150, 150, 110, 110, Default),
            Gen("ellipse-from-center", 14, 150, 150, 260, 200, Default),
            Gen("ellipse-foci-wide", 15, 100, 100, 220, 140, Default),
            Gen("ellipse-foci-tall", 15, 100, 100, 140, 220, Default),
            Gen("ellipse-foci-round", 15, 100, 100, 150, 150, Default),
            Gen("dashed-circle", 16, 100, 100, 130, 140, Default),

            // 17..21 conics.
            Gen("hyperbola-step1", 17, 100, 100, 220, 160, Default),
            Gen("hyperbola-step1-degenerate", 17, 100, 100, 100, 160, Default),
            Gen("hyperbola-foci-step1", 18, 200, 200, 120, 260, Default),
            Gen("parabola-right", 19, 100, 100, 200, 180, Default),
            Gen("parabola-right-negative", 19, 200, 200, 100, 140, Default),
            Gen("parabola-foci", 20, 100, 100, 200, 200, Default),
            Gen("parabola-foci-degenerate", 20, 100, 100, 100, 200, Default),
            Gen("parabola-up", 21, 100, 100, 200, 180, Default),
            Gen("parabola-up-negative", 21, 200, 200, 100, 150, Default),

            // 22..24 solids.
            Gen("cylinder-swapped", 22, 200, 200, 260, 120, Default),
            Gen("cylinder-normal", 22, 200, 100, 260, 220, Default),
            Gen("cone-swapped", 23, 200, 200, 260, 120, Default),
            Gen("cone-normal", 23, 200, 100, 260, 220, Default),
            Gen("cuboid-step1", 24, 100, 100, 220, 180, Default),

            // Two-step second passes.
            Cuboid("cuboid-depth-40", 100, 100, 220, 180, 40, Default),
            Cuboid("cuboid-depth-negative", 100, 100, 220, 180, -40, Default),
            Cuboid("cuboid-depth-zero", 100, 100, 220, 180, 0, Default),
            Hyperbola("hyperbola-branches-x", 100, 100, 1, 200, 130, false, Default),
            Hyperbola("hyperbola-branches-x-foci", 100, 100, 1, 200, 130, true, Default),
            Hyperbola("hyperbola-branches-y-foci", 100, 100, 1, 130, 200, true, Default),
            Hyperbola("hyperbola-branches-k2", 100, 100, 1.2f, 200, 160, false, Default),
            Hyperbola("hyperbola-branches-negative", 100, 100, 1, 20, 60, true, Default),
            Hyperbola("hyperbola-branches-degenerate", 100, 100, 1, 100.005f, 160, false, Default),
            Hyperbola("hyperbola-branches-k-zero", 100, 100, 0, 200, 130, false, Default),

            // Angle snapping.
            Snap("snap-horizontal", 100, 100, 300, 105),
            Snap("snap-30", 100, 100, 300, -20),
            Snap("snap-45", 100, 100, 200, 0),
            Snap("snap-60", 100, 100, 200, -180),
            Snap("snap-vertical", 100, 100, 103, 300),
            Snap("snap-none", 100, 100, 200, 140)
        };
    }

    private static List<object> RecognizerCases()
    {
        return new List<object>
        {
            RecognizeCase("recognize-line", LinePoints()),
            RecognizeCase("recognize-circle", ShapeGenerator.EllipsePoints(new Vector2(200, 200), 90, 90, 0, 2 * MathF.PI, 80)),
            RecognizeCase("recognize-ellipse", ShapeGenerator.EllipsePoints(new Vector2(300, 200), 140, 70, 0, 2 * MathF.PI, 90)),
            RecognizeCase("recognize-rectangle", RectanglePoints()),
            RecognizeCase("recognize-triangle", TrianglePoints()),
            RecognizeCase("reject-tiny", ShapeGenerator.EllipsePoints(new Vector2(20, 20), 6, 6, 0, 2 * MathF.PI, 30)),
            RecognizeCase("reject-short", new[]
            {
                new Vector2(100, 100), new Vector2(110, 105), new Vector2(120, 100),
                new Vector2(130, 105), new Vector2(140, 100)
            })
        };
    }

    private static object Gen(string id, int kind, float sx, float sy, float ex, float ey, AttrSpec attrs)
    {
        var strokes = ShapeGenerator.Generate((ShapeKind)kind, new Vector2(sx, sy), new Vector2(ex, ey), attrs.ToAttributes());
        var input = new object[] { kind, sx, sy, ex, ey, attrs.Input() };
        return Case(id, "generate", input, Summaries(strokes));
    }

    private static object Cuboid(string id, float ftx, float fty, float fbx, float fby, float depth, AttrSpec attrs)
    {
        var strokes = ShapeGenerator.GenerateCuboidDepth(
            new Vector2(ftx, fty), new Vector2(fbx, fby), depth, attrs.ToAttributes());
        var input = new object[] { ftx, fty, fbx, fby, depth, attrs.Input() };
        return Case(id, "generateCuboidDepth", input, Summaries(strokes));
    }

    private static object Hyperbola(string id, float cx, float cy, float k, float ex, float ey, bool withFoci, AttrSpec attrs)
    {
        var strokes = ShapeGenerator.GenerateHyperbolaBranches(
            new Vector2(cx, cy), k, new Vector2(ex, ey), withFoci, attrs.ToAttributes());
        var input = new object[] { cx, cy, k, ex, ey, withFoci, attrs.Input() };
        return Case(id, "generateHyperbolaBranches", input, Summaries(strokes));
    }

    private static object Snap(string id, float sx, float sy, float ex, float ey)
    {
        var p = ShapeGenerator.SnapParallelEnd(new Vector2(sx, sy), new Vector2(ex, ey));
        return Case(id, "snapParallelEnd", new object[] { sx, sy, ex, ey }, new { p.X, p.Y });
    }

    private static object RecognizeCase(string id, Vector2[] points)
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 3f });
        foreach (var p in points)
        {
            stroke.AddPoint(p.X, p.Y);
        }
        stroke.Complete();
        var result = ShapeRecognizer.Recognize(new[] { stroke });
        var input = new object[]
        {
            points.Select(p => new { p.X, p.Y }).ToArray()
        };
        var expected = new
        {
            kind = (int)result.Shape,
            centerX = result.Center.X,
            centerY = result.Center.Y,
            radiusX = result.RadiusX,
            radiusY = result.RadiusY,
            confidence = result.Confidence,
            corners = result.Corners.Select(c => new { c.X, c.Y }).ToArray()
        };
        return Case(id, "recognize", input, expected);
    }

    private static object Case(string id, string fn, object[] input, object expected) => new
    {
        id,
        fn,
        input,
        expected,
        tolerance = 1e-4
    };

    private static object[] Summaries(List<InkStroke> strokes) => strokes.Select(s => new
    {
        Points = s.Points.Select(p => new { p.X, p.Y, p.Pressure, p.Width, p.TimeMs }).ToArray(),
        ColorPacked = s.Attributes.Color.PackedArgb,
        BaseWidth = s.Attributes.BaseWidth,
        IsHighlighter = s.Attributes.IsHighlighter,
        HighlighterAlpha = (int)s.Attributes.HighlighterAlpha,
        TaperKind = (int)s.Attributes.TaperKind,
        IsRecognizedShape = s.IsRecognizedShape
    }).ToArray();

    private static Vector2[] LinePoints()
    {
        var pts = new Vector2[20];
        for (var i = 0; i < pts.Length; i++)
        {
            pts[i] = new Vector2(50 + i * 4f, 100 + i * 1f);
        }
        return pts;
    }

    private static Vector2[] RectanglePoints()
    {
        var pts = new List<Vector2>();
        AddSegment(pts, new Vector2(200, 100), new Vector2(300, 100), 10);
        AddSegment(pts, new Vector2(300, 100), new Vector2(300, 220), 12);
        AddSegment(pts, new Vector2(300, 220), new Vector2(100, 220), 20);
        AddSegment(pts, new Vector2(100, 220), new Vector2(100, 100), 12);
        AddSegment(pts, new Vector2(100, 100), new Vector2(200, 100), 10);
        return pts.ToArray();
    }

    private static Vector2[] TrianglePoints()
    {
        var pts = new List<Vector2>();
        AddSegment(pts, new Vector2(200, 220), new Vector2(300, 220), 10);
        AddSegment(pts, new Vector2(300, 220), new Vector2(200, 100), 20);
        AddSegment(pts, new Vector2(200, 100), new Vector2(100, 220), 20);
        AddSegment(pts, new Vector2(100, 220), new Vector2(200, 220), 10);
        return pts.ToArray();
    }

    private static void AddSegment(List<Vector2> pts, Vector2 a, Vector2 b, int steps)
    {
        var delta = b - a;
        for (var i = 0; i < steps; i++)
        {
            pts.Add(a + delta * (i / (float)steps));
        }
    }
}
