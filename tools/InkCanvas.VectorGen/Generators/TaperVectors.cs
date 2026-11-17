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
