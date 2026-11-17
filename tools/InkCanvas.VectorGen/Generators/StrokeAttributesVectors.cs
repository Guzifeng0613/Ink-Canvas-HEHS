using InkCanvas.Core.Ink;

internal static class StrokeAttributesVectors
{
    public static void Write(string dir)
    {
        var cases = new List<object>();
        Add(cases, "normal-tail", 0xFF000000u, 2.5, false, 96, TaperKind.Tail);
        Add(cases, "normal-velocity", 0xFFDC2626u, 7.5, false, 96, TaperKind.Velocity);
        Add(cases, "normal-none", 0x80122338u, 15.0, false, 96, TaperKind.None);
        Add(cases, "highlighter-tail", 0xFFFFC000u, 10.0, true, 140, TaperKind.Tail);
        Add(cases, "highlighter-alpha20", 0xFF00FF00u, 3.0, true, 20, TaperKind.Velocity);

        JsonWriter.Write(dir, "stroke-attributes.json", new
        {
            id = "stroke-attributes",
            entry = "harmony/core/src/main/ets/ink/StrokeAttributes.ts",
            cases
        });
    }

    private static void Add(List<object> cases, string id, uint packed, double baseWidth, bool highlighter, byte alpha, TaperKind taper)
    {
        var attributes = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(packed),
            BaseWidth = (float)baseWidth,
            IsHighlighter = highlighter,
            HighlighterAlpha = alpha,
            TaperKind = taper
        };
        cases.Add(new
        {
            id,
            fn = "strokeAttributesEffective",
            input = new object[]
            {
                new
                {
                    ColorPacked = packed,
                    BaseWidth = baseWidth,
                    IsHighlighter = highlighter,
                    HighlighterAlpha = alpha,
                    TaperKind = (int)taper
                }
            },
            expected = new
            {
                effectiveTaper = (int)attributes.EffectiveTaper,
                effectiveArgb = attributes.EffectiveColor.PackedArgb,
                baseWidth = (double)attributes.BaseWidth
            },
            tolerance = 0
        });
    }
}
