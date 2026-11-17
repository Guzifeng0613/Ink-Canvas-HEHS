using System.Numerics;
using InkCanvas.Core.History;
using InkCanvas.Core.Ink;

internal static class GeometryVectors
{
    public static void Write(string dir)
    {
        WriteGeometry(dir);
        WriteEraser(dir);
        WriteUndo(dir);
    }

    private static void WriteGeometry(string dir)
    {
        var cases = new List<object>();
        AddGeometry(cases, "geom-single-point", new[] { (10f, 10f, 6f) });
        AddGeometry(cases, "geom-single-zero-width", new[] { (10f, 10f, 0f) });
        AddGeometry(cases, "geom-two-equal", new[] { (0f, 0f, 4f), (20f, 0f, 4f) });
        AddGeometry(cases, "geom-two-tapered", new[] { (0f, 0f, 4f), (20f, 0f, 12f) });
        AddGeometry(cases, "geom-curve-3", new[] { (0f, 0f, 5f), (10f, 8f, 6f), (20f, 0f, 7f) });
        AddGeometry(cases, "geom-self-intersect", new[]
        {
            (0f, 0f, 5f), (20f, 20f, 5f), (20f, 0f, 5f), (0f, 20f, 5f)
        });
        AddGeometry(cases, "geom-zero-length-segment", new[] { (5f, 5f, 8f), (5f, 5f, 2f) });
        AddGeometry(cases, "geom-mixed-widths", new[]
        {
            (0f, 0f, 1f), (5f, 3f, 8f), (12f, -2f, 3f), (18f, 4f, 15f)
        });
        JsonWriter.Write(dir, "stroke-geometry.json", new
        {
            id = "stroke-geometry",
            entry = "harmony/core/src/main/ets/ink/StrokeGeometry.ts",
            cases
        });
    }

    private static void AddGeometry(List<object> cases, string id, (float X, float Y, float Width)[] points)
    {
        var contours = StrokeGeometry.BuildUnionContours(MakeStroke(points))
            .Select(c => c.SelectMany(v => new[] { (double)v.X, (double)v.Y }).ToArray())
            .ToArray();
        cases.Add(new
        {
            id,
            fn = "buildUnionContours",
            input = new object[] { PointsJson(points) },
            expected = contours,
            tolerance = 1e-4
        });
    }

    private static void WriteEraser(string dir)
    {
        var cases = new List<object>();
        AddEraser(cases, "erase-single-inside", new[] { (10f, 10f, 6f) }, 10f, 10f, 5f);
        AddEraser(cases, "erase-single-outside", new[] { (10f, 10f, 6f) }, 100f, 100f, 5f);
        AddEraser(cases, "erase-line-center", new[] { (0f, 0f, 4f), (20f, 0f, 4f) }, 10f, 0f, 3f);
        AddEraser(cases, "erase-line-edge", new[] { (0f, 0f, 4f), (20f, 0f, 4f) }, 0f, 2f, 3f);
        AddEraser(cases, "erase-line-miss", new[] { (0f, 0f, 4f), (20f, 0f, 4f) }, 10f, 30f, 3f);
        AddEraser(cases, "erase-two-cuts", new[] { (0f, 0f, 4f), (10f, 0f, 4f), (20f, 0f, 4f) }, 10f, 0f, 2f);
        AddEraser(cases, "erase-variable-width", new[] { (0f, 0f, 2f), (10f, 0f, 12f), (20f, 0f, 2f) }, 10f, 0f, 4f);
        AddEraser(cases, "erase-zero-radius", new[] { (0f, 0f, 4f), (20f, 0f, 4f) }, 10f, 0f, 0f);
        AddEraser(cases, "erase-duplicate-points", new[] { (5f, 5f, 4f), (5f, 5f, 4f), (15f, 5f, 4f) }, 5f, 5f, 3f);
        JsonWriter.Write(dir, "stroke-eraser.json", new
        {
            id = "stroke-eraser",
            entry = "harmony/core/src/main/ets/ink/StrokeEraser.ts",
            cases
        });
    }

    private static void AddEraser(List<object> cases, string id, (float X, float Y, float Width)[] points,
        float centerX, float centerY, float radius)
    {
        var touched = StrokeEraser.EraseDisc(MakeStroke(points), new Vector2(centerX, centerY), radius, out var remaining);
        cases.Add(new
        {
            id,
            fn = "eraseDiscSummary",
            input = new object[] { StrokeJson(points), centerX, centerY, radius },
            expected = new
            {
                touched,
                fragments = remaining.Select(f => FragmentJson(f)).ToArray()
            },
            tolerance = 1e-4
        });
    }

    private static void WriteUndo(string dir)
    {
        var cases = new List<object>();
        AddUndo(cases, "undo-basic", 100, new[] { "push:a", "push:b", "undo", "redo" });
        AddUndo(cases, "undo-capacity", 2, new[] { "push:a", "push:b", "push:c", "undo", "undo", "undo" });
        AddUndo(cases, "undo-clear", 100, new[] { "push:a", "undo", "clear", "redo" });
        AddUndo(cases, "undo-redo-cleared-on-push", 100, new[] { "push:a", "undo", "push:b", "redo" });
        JsonWriter.Write(dir, "undo.json", new
        {
            id = "undo",
            entry = "harmony/core/src/main/ets/history/UndoStack.ts",
            cases
        });
    }

    private static void AddUndo(List<object> cases, string id, int capacity, string[] ops)
    {
        var stack = new UndoStack(capacity);
        var log = new List<string>();
        foreach (var op in ops)
        {
            if (op.StartsWith("push:"))
            {
                var name = op.Substring(5);
                stack.Push(new DelegateAction(name, () => log.Add("apply:" + name), () => log.Add("revert:" + name)));
            }
            else if (op == "undo")
            {
                stack.Undo();
            }
            else if (op == "redo")
            {
                stack.Redo();
            }
            else if (op == "clear")
            {
                stack.Clear();
            }
        }
        cases.Add(new
        {
            id,
            fn = "simulateUndo",
            input = new object[] { ops, capacity },
            expected = new
            {
                canUndo = stack.CanUndo,
                canRedo = stack.CanRedo,
                undoCount = stack.UndoCount,
                redoCount = stack.RedoCount,
                log
            },
            tolerance = 0
        });
    }

    private static InkStroke MakeStroke((float X, float Y, float Width)[] points)
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 3f });
        stroke.RestorePoints(points.Select(p => new InkPoint(p.X, p.Y, 0.5f, p.Width, 0f)).ToArray());
        return stroke;
    }

    private static object[] PointsJson((float X, float Y, float Width)[] points) =>
        points.Select(p => (object)new { p.X, p.Y, Pressure = 0.5f, p.Width, TimeMs = 0f }).ToArray();

    private static object StrokeJson((float X, float Y, float Width)[] points) => new
    {
        ColorPacked = 0xFF000000u,
        BaseWidth = 3.0,
        IsHighlighter = false,
        HighlighterAlpha = 96,
        TaperKind = 1,
        IsRecognizedShape = false,
        Points = PointsJson(points)
    };

    private static object FragmentJson(InkStroke stroke) => new
    {
        ColorPacked = stroke.Attributes.Color.PackedArgb,
        BaseWidth = (double)stroke.Attributes.BaseWidth,
        IsHighlighter = stroke.Attributes.IsHighlighter,
        HighlighterAlpha = (int)stroke.Attributes.HighlighterAlpha,
        TaperKind = (int)stroke.Attributes.TaperKind,
        IsRecognizedShape = stroke.IsRecognizedShape,
        Points = stroke.Points.Select(p => new
        {
            X = (double)p.X,
            Y = (double)p.Y,
            Pressure = (double)p.Pressure,
            Width = (double)p.Width,
            TimeMs = (double)p.TimeMs
        }).ToArray()
    };
}
