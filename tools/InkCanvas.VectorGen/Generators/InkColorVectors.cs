using InkCanvas.Core.Ink;

internal static class InkColorVectors
{
    public static void Write(string dir)
    {
        var cases = new List<object>();
        var packs = new (byte A, byte R, byte G, byte B)[]
        {
            (0, 0, 0, 0),
            (255, 0, 0, 0),
            (255, 255, 255, 255),
            (255, 220, 38, 38),
            (128, 12, 34, 56),
            (1, 2, 3, 4)
        };
        foreach (var p in packs)
        {
            var color = new InkColor(p.A, p.R, p.G, p.B);
            cases.Add(new
            {
                id = $"pack-{p.A}-{p.R}-{p.G}-{p.B}",
                fn = "packArgb",
                input = new object[] { p.A, p.R, p.G, p.B },
                expected = color.PackedArgb,
                tolerance = 0
            });
        }

        foreach (var packed in new uint[] { 0u, 0xFF000000u, 0xFFFFFFFFu, 0xFFDC2626u, 0x80122338u })
        {
            var c = InkColor.FromPackedArgb(packed);
            cases.Add(new
            {
                id = $"unpack-{packed:X8}",
                fn = "unpackArgb",
                input = new object[] { packed },
                expected = new { A = c.A, R = c.R, G = c.G, B = c.B },
                tolerance = 0
            });
        }

        var alphas = new (uint Packed, byte Alpha)[]
        {
            (0xFF000000u, 128),
            (0x80122338u, 20),
            (0xFFFFFFFFu, 240)
        };
        foreach (var a in alphas)
        {
            var c = InkColor.FromPackedArgb(a.Packed).WithAlpha(a.Alpha);
            cases.Add(new
            {
                id = $"alpha-{a.Packed:X8}-{a.Alpha}",
                fn = "withAlphaArgb",
                input = new object[] { a.Packed, a.Alpha },
                expected = c.PackedArgb,
                tolerance = 0
            });
        }

        JsonWriter.Write(dir, "ink-color.json", new
        {
            id = "ink-color",
            entry = "harmony/core/src/main/ets/ink/InkColor.ts",
            cases
        });
    }
}
