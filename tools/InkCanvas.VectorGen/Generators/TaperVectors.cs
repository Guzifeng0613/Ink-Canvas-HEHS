using InkCanvas.Core.Ink;

internal static class TaperVectors
{
    public static void Write(string dir)
    {
        JsonWriter.Write(dir, "taper-tail.json", new
        {
            id = "taper-tail",
            entry = "harmony/core/src/main/ets/ink/TaperProfile.ts",
            cases = TailCases()
        });
        JsonWriter.Write(dir, "taper-velocity.json", new
        {
            id = "taper-velocity",
            entry = "harmony/core/src/main/ets/ink/TaperProfile.ts",
            cases = VelocityCases()
        });
        JsonWriter.Write(dir, "taper-simulated.json", new
        {
            id = "taper-simulated",
            entry = "harmony/core/src/main/ets/ink/TaperProfile.ts",
            cases = SimulatedCases()
        });
        JsonWriter.Write(dir, "taper-stroke.json", new
        {
            id = "taper-stroke",
            entry = "harmony/core/src/main/ets/ink/InkStroke.ts",
            cases = StrokeCases()
        });
    }

    // 基线演进：4 种模拟压感笔锋（SmoothPen/SoftPen/Natural/Calligraphy）宽度序列。
    private static List<object> SimulatedCases()
    {
        return new List<object>
        {
            Case("sim-smoothpen-line", 10, Line(12, 10f), TaperKind.SmoothPen),
            Case("sim-softpen-line", 10, Line(12, 10f), TaperKind.SoftPen),
            Case("sim-natural-line", 10, Line(12, 10f), TaperKind.Natural),
            Case("sim-calligraphy-line", 10, Line(12, 10f), TaperKind.Calligraphy),
            Case("sim-natural-accelerate", 8, new[]
            {
                (0f, 0f, 0f), (3f, 0f, 10f), (7f, 0f, 20f), (15f, 0f, 30f),
                (30f, 0f, 40f), (52f, 0f, 50f), (80f, 0f, 60f), (81f, 0f, 70f)
            }, TaperKind.Natural),
            Case("sim-calligraphy-turn", 10, new[]
            {
                (0f, 0f, 0f), (6f, 0f, 10f), (12f, 1f, 20f), (17f, 4f, 30f), (20f, 9f, 40f),
                (20f, 15f, 50f), (17f, 20f, 60f), (12f, 23f, 70f), (6f, 24f, 80f), (0f, 22f, 90f)
            }, TaperKind.Calligraphy),
            Case("sim-smoothpen-40", 6, Line(40, 6f), TaperKind.SmoothPen),
            Case("sim-softpen-irregular", 7.5f, new[]
            {
                (0f, 0f, 0f), (3f, 1f, 8f), (9f, -2f, 17f), (12f, 4f, 25f), (20f, 8f, 31f),
                (21f, 15f, 40f), (18f, 19f, 49f)
            }, TaperKind.SoftPen)
        };
    }

    // 笔画级探针：InkStroke 全链路（OneEuroFilter 位置滤波 → 状态化笔锋 → StablePointCount）。
    private static List<object> StrokeCases()
    {
        return new List<object>
        {
            StrokeCase("stroke-natural-filter-jitter", 10, new[]
            {
                (0f, 0f, 0f), (5f, 0.8f, 10f), (9f, -0.5f, 20f), (14f, 0.9f, 30f),
                (18f, 0f, 40f), (23f, -0.7f, 50f), (27f, 0.6f, 60f), (32f, 0f, 70f)
            }, TaperKind.Natural),
            StrokeCase("stroke-calligraphy-line", 10, Line(14, 10f), TaperKind.Calligraphy),
            StrokeCase("stroke-smoothpen-line", 10, Line(14, 10f), TaperKind.SmoothPen),
            StrokeCase("stroke-tail-stable", 10, Line(24, 10f), TaperKind.Tail),
            StrokeCase("stroke-natural-stable", 10, Line(60, 8f), TaperKind.Natural)
        };
    }

    private static object StrokeCase(string id, float baseWidth, (float X, float Y, float TimeMs)[] samples, TaperKind kind)
    {
        var attributes = new StrokeAttributes { BaseWidth = baseWidth, TaperKind = kind };
        var stroke = new InkStroke(attributes);
        var widths = new List<double>();
        var stable = new List<int>();
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var s in samples)
        {
            stroke.AddPoint(s.X, s.Y, 0.5f, s.TimeMs);
            var last = stroke.Points[^1];
            widths.Add(last.Width);
            stable.Add(stroke.StablePointCount);
            xs.Add(last.X);
            ys.Add(last.Y);
        }
        var inputPoints = samples.Select(s => new { s.X, s.Y, s.TimeMs }).ToArray();
        return new
        {
            id,
            fn = "strokeTaperProbe",
            input = new object[] { baseWidth, inputPoints, (int)kind },
            expected = new { widths, stable, x = xs, y = ys },
            tolerance = 1e-4
        };
    }

    private static List<object> TailCases()
    {
        return new List<object>
        {
            Case("tail-1-point", 10, new[] { (0f, 0f, 0f) }, TaperKind.Tail),
            Case("tail-2-points", 10, new[] { (0f, 0f, 0f), (10f, 0f, 10f) }, TaperKind.Tail),
            Case("tail-10-points", 10, Line(10, 10f), TaperKind.Tail),
            Case("tail-40-points", 10, Line(40, 10f), TaperKind.Tail),
            Case("tail-irregular", 7.5f, new[]
            {
                (0f, 0f, 0f), (3f, 1f, 8f), (9f, -2f, 17f), (12f, 4f, 25f), (20f, 8f, 31f),
                (21f, 15f, 40f), (18f, 19f, 49f), (11f, 21f, 60f), (4f, 18f, 71f), (1f, 12f, 80f),
                (2f, 6f, 91f), (6f, 3f, 99f)
            }, TaperKind.Tail),
            Case("tail-custom-options", 10, Line(8, 6f), TaperKind.Tail,
                new { TailPoints = 4, HeadPoints = 2, TipRatio = 0.5 })
        };
    }

    private static List<object> VelocityCases()
    {
        return new List<object>
        {
            Case("velocity-constant", 10, Line(12, 10f), TaperKind.Velocity),
            Case("velocity-zero", 10, new[]
            {
                (5f, 5f, 0f), (5f, 5f, 8f), (5f, 5f, 16f), (5f, 5f, 24f)
            }, TaperKind.Velocity),
            Case("velocity-slow", 10, Line(6, 0.5f), TaperKind.Velocity),
            Case("velocity-fast", 10, Line(6, 20f), TaperKind.Velocity),
            Case("velocity-mixed", 8, new[]
            {
                (0f, 0f, 0f), (1f, 0f, 4f), (6f, 0f, 9f), (26f, 0f, 15f), (26f, 0f, 20f),
                (28f, 0f, 26f), (29f, 0f, 33f), (34f, 0f, 40f)
            }, TaperKind.Velocity),
            Case("velocity-2-points", 10, new[] { (0f, 0f, 0f), (10f, 0f, 10f) }, TaperKind.Velocity),
            Case("none-5-points", 10, Line(5, 10f), TaperKind.None)
        };
    }

    private static (float X, float Y, float TimeMs)[] Line(int count, float step)
    {
        var result = new (float, float, float)[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = (i * step, 0f, i * 10f);
        }
        return result;
    }

    private static object Case(string id, float baseWidth, (float X, float Y, float TimeMs)[] samples, TaperKind kind, object? options = null)
    {
        var points = samples.Select(s => new InkPoint(s.X, s.Y, 0.5f, 0f, s.TimeMs)).ToArray();
        TaperProfile profile = options is null
            ? TaperProfile.Create(kind)
            : new TailTaper
            {
                TailPoints = GetInt(options, "TailPoints"),
                HeadPoints = GetInt(options, "HeadPoints"),
                TipRatio = GetFloat(options, "TipRatio")
            };
        var span = points.AsSpan();
        for (var tip = 0; tip < points.Length; tip++)
        {
            profile.Apply(span, tip, baseWidth);
        }

        var inputPoints = samples.Select(s => new { s.X, s.Y, Pressure = 0.5f, s.TimeMs, Width = 0f }).ToArray();
        var input = options is null
            ? new object[] { baseWidth, inputPoints, (int)kind }
            : new object[] { baseWidth, inputPoints, (int)kind, options };

        return new
        {
            id,
            fn = "effectiveWidths",
            input,
            expected = points.Select(p => (double)p.Width).ToArray(),
            tolerance = 1e-4
        };
    }

    private static int GetInt(object options, string name) =>
        (int)options.GetType().GetProperty(name)!.GetValue(options)!;

    private static float GetFloat(object options, string name) =>
        (float)(double)options.GetType().GetProperty(name)!.GetValue(options)!;
}
