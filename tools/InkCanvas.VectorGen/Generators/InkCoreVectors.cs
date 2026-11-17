using InkCanvas.Core.Ink;
using System.Numerics;

internal static class InkCoreVectors
{
    public static void Write(string dir)
    {
        WriteInkCore(dir);
        WriteDocument(dir);
    }

    private static void WriteInkCore(string dir)
    {
        var cases = new List<object>();
        AddBounds(cases, "bounds-single-wide", new[] { (10f, 10f, 30f) });
        AddBounds(cases, "bounds-two-widths", new[] { (0f, 0f, 4f), (20f, 10f, 10f) });
        AddBounds(cases, "bounds-empty", Array.Empty<(float, float, float)>());
        AddBounds(cases, "bounds-zero-width", new[] { (5f, 5f, 0f) });
        AddSetter(cases, "setter-velocity-rebuild",
            new[] { (0f, 0f, 1f), (5f, 0f, 1f), (10f, 0f, 1f), (15f, 0f, 1f) }, 10f, TaperKind.Velocity);
        AddSetter(cases, "setter-none-rebuild",
            new[] { (0f, 0f, 1f), (5f, 0f, 1f) }, 10f, TaperKind.None);

        JsonWriter.Write(dir, "ink-core.json", new
        {
            id = "ink-core",
            entry = "harmony/core/src/main/ets/ink/InkStroke.ts",
            cases
        });
    }

    private static void AddBounds(List<object> cases, string id, (float X, float Y, float Width)[] points)
    {
        var stroke = MakeStroke(points);
        var b = stroke.Bounds;
        cases.Add(new
        {
            id,
            fn = "strokeBounds",
            input = new object[] { PointsJson(points), 3.0 },
            expected = new double[] { b.Left, b.Top, b.Right, b.Bottom },
            tolerance = 1e-4
        });
    }

    private static void AddSetter(List<object> cases, string id,
        (float X, float Y, float Width)[] points, float baseWidth, TaperKind kind)
    {
        var stroke = MakeStroke(points);
        stroke.Attributes.BaseWidth = baseWidth;
        stroke.Complete();
        stroke.TaperKind = kind;
        var widths = stroke.Points.Select(p => (double)p.Width).ToArray();
        cases.Add(new
        {
            id,
            fn = "setTaperAndWidths",
            input = new object[] { PointsJson(points), baseWidth, (int)kind },
            expected = widths,
            tolerance = 1e-4
        });
    }

    private static void WriteDocument(string dir)
    {
        var specs = new[]
        {
            (BaseWidth: 3f, Points: new[] { (0f, 0f, 3f), (20f, 0f, 3f) }),
            (BaseWidth: 5f, Points: new[] { (0f, 40f, 5f), (20f, 40f, 5f) })
        };
        var cases = new List<object>();
        AddDocumentCase(cases, specs, "document-hit-point", "documentHitTestPoint", new object[] { 5f, 1f, 4f });
        AddDocumentCase(cases, specs, "document-hit-miss", "documentHitTestPoint", new object[] { 5f, 20f, 2f });
        AddDocumentCase(cases, specs, "document-hit-rect", "documentHitTestRect", new object[] { -5f, -5f, 25f, 5f });
        AddDocumentErase(cases, specs, "document-erase", 5f, 0f, 2f);
        AddDocumentBounds(cases, specs, "document-bounds");

        JsonWriter.Write(dir, "document.json", new
        {
            id = "document",
            entry = "harmony/core/src/main/ets/ink/InkDocument.ts",
            cases
        });
    }

    private static void AddDocumentCase(List<object> cases,
        (float BaseWidth, (float X, float Y, float Width)[] Points)[] specs,
        string id, string fn, object[] args)
    {
        var doc = MakeDocument(specs);
        int[] hits;
        if (fn == "documentHitTestPoint")
        {
            hits = IndexOf(doc, doc.HitTest(new Vector2((float)args[0], (float)args[1]), (float)args[2]));
        }
        else
        {
            hits = IndexOf(doc, doc.HitTest(new Rect((float)args[0], (float)args[1], (float)args[2], (float)args[3])));
        }
        var input = new List<object> { DocumentJson(specs) };
        input.AddRange(args);
        cases.Add(new { id, fn, input = input.ToArray(), expected = hits, tolerance = 0 });
    }

    private static void AddDocumentErase(List<object> cases,
        (float BaseWidth, (float X, float Y, float Width)[] Points)[] specs,
        string id, float x, float y, float radius)
    {
        var doc = MakeDocument(specs);
        var all = doc.Strokes.ToList();
        var hits = doc.EraseAt(new Vector2(x, y), radius).Select(h => all.IndexOf(h)).ToArray();
        cases.Add(new
        {
            id,
            fn = "documentEraseAt",
            input = new object[] { DocumentJson(specs), x, y, radius },
            expected = new { hits, remaining = doc.Count },
            tolerance = 0
        });
    }

    private static void AddDocumentBounds(List<object> cases,
        (float BaseWidth, (float X, float Y, float Width)[] Points)[] specs, string id)
    {
        var b = MakeDocument(specs).Bounds;
        cases.Add(new
        {
            id,
            fn = "documentBounds",
            input = new object[] { DocumentJson(specs) },
            expected = new double[] { b.Left, b.Top, b.Right, b.Bottom },
            tolerance = 1e-4
        });
    }

    private static InkStroke MakeStroke((float X, float Y, float Width)[] points)
    {
        var stroke = new InkStroke(new StrokeAttributes());
        stroke.RestorePoints(points.Select(p => new InkPoint(p.X, p.Y, 0.5f, p.Width, 0f)).ToArray());
        return stroke;
    }

    private static InkDocument MakeDocument((float BaseWidth, (float X, float Y, float Width)[] Points)[] specs)
    {
        var doc = new InkDocument();
        foreach (var spec in specs)
        {
            var stroke = MakeStroke(spec.Points);
            stroke.Attributes.BaseWidth = spec.BaseWidth;
            doc.Add(stroke);
        }
        return doc;
    }

    private static int[] IndexOf(InkDocument doc, List<InkStroke> hits)
    {
        var list = doc.Strokes.ToList();
        return hits.Select(h => list.IndexOf(h)).ToArray();
    }

    private static object[] PointsJson((float X, float Y, float Width)[] points) =>
        points.Select(p => (object)new { p.X, p.Y, p.Width }).ToArray();

    private static object[] DocumentJson((float BaseWidth, (float X, float Y, float Width)[] Points)[] specs) =>
        specs.Select(s => (object)new
        {
            BaseWidth = (double)s.BaseWidth,
            Points = PointsJson(s.Points)
        }).ToArray();
}
