using InkCanvas.Core.Ink;
using InkCanvas.Features.Persistence;
using System.Text.Json;

internal static class PersistenceVectors
{
    public static void Write(string dir)
    {
        WriteIcink(dir);
        WriteBoard(dir);
        WritePpt(dir);
    }

    private static void WriteIcink(string dir)
    {
        var cases = new List<object>();

        var strokes = new List<InkStroke>();
        var a = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(0xFF112233),
            BaseWidth = 2.5f,
            IsHighlighter = true,
            HighlighterAlpha = 140,
            TaperKind = TaperKind.None
        };
        var s1 = new InkStroke(a);
        s1.AddPoint(10, 20, 0.5f, 0);
        s1.AddPoint(30, 40, 0.7f, 16);
        s1.Complete();
        s1.IsRecognizedShape = true;
        strokes.Add(s1);

        var b = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(0x80FF0000),
            BaseWidth = 7.5f,
            IsHighlighter = false,
            HighlighterAlpha = 96,
            TaperKind = TaperKind.Velocity
        };
        var s2 = new InkStroke(b);
        for (var i = 0; i < 5; i++)
        {
            s2.AddPoint(i * 5, i * 2, 0.5f, i * 10);
        }
        s2.Complete();
        strokes.Add(s2);

        var temp = Path.Combine(Path.GetTempPath(), "inkcanvas-vec-" + Guid.NewGuid().ToString("N") + ".icink");
        InkFileStore.Save(temp, strokes);
        var text = File.ReadAllText(temp);
        var loaded = InkFileStore.Load(temp);
        AddIcinkCase(cases, "icink-roundtrip", text, loaded);

        const string legacy = "{\"Version\":1,\"Strokes\":[{\"Color\":4278190080,\"Width\":3,\"Highlighter\":false,\"Taper\":9,\"Shape\":false,\"Points\":[[1,2,0.5,3],[4,5,0.6,3,18]]}]}";
        File.WriteAllText(temp, legacy);
        AddIcinkCase(cases, "icink-legacy-fields", legacy, InkFileStore.Load(temp));
        File.Delete(temp);

        JsonWriter.Write(dir, "icink-roundtrip.json", new
        {
            id = "icink-roundtrip",
            entry = "harmony/core/src/main/ets/persistence/IcinkCodec.ts",
            cases
        });
    }

    private static void AddIcinkCase(List<object> cases, string id, string text, List<InkStroke> loaded)
    {
        cases.Add(new
        {
            id,
            fn = "icinkRoundtrip",
            input = new object[] { text },
            expected = loaded.Select(s => new
            {
                Color = s.Attributes.Color.PackedArgb,
                Width = (double)s.Attributes.BaseWidth,
                Highlighter = s.Attributes.IsHighlighter,
                HighlighterAlpha = (int)s.Attributes.HighlighterAlpha,
                Taper = (int)s.Attributes.TaperKind,
                Shape = s.IsRecognizedShape,
                Points = s.Points.Select(p => new double[]
                {
                    (double)p.X, (double)p.Y, (double)p.Pressure, (double)p.Width, (double)p.TimeMs
                }).ToArray()
            }).ToArray(),
            tolerance = 1e-4
        });
    }

    private static void WriteBoard(string dir)
    {
        const string full = "{\"Format\":\"inkcanvas-board\",\"Version\":1,\"Background\":\"Black\",\"GridSpacing\":40,\"Pages\":[{\"Strokes\":[{\"Color\":4278190080,\"Width\":2.5,\"Highlighter\":false,\"Taper\":1,\"Shape\":false,\"Points\":[[10,20,0.5,2.5,0],[30,40,0.5,2.5,10]]}],\"Images\":[{\"Id\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"Name\":\"a.png\",\"Data\":\"iVBORw0KGgo=\",\"ContentType\":\"image/png\",\"Left\":100,\"Top\":100,\"Width\":640,\"Height\":360,\"Transform\":[1,0,0,1,0,0]}],\"Media\":[{\"Id\":\"m1\",\"Name\":\"v.mp4\",\"Data\":\"AAAAIGZ0eXA=\",\"ContentType\":\"video/mp4\",\"Left\":200,\"Top\":200,\"Width\":360,\"Height\":240,\"Transform\":null}]}]}";
        const string videoOnly = "{\"Format\":\"inkcanvas-board\",\"Version\":1,\"Background\":\"grid\",\"GridSpacing\":30,\"Pages\":[{\"Strokes\":[],\"Images\":[],\"Media\":[{\"Id\":\"m2\",\"Name\":\"v.mp4\",\"Data\":\"AAAA\",\"ContentType\":\"video/mp4\",\"Left\":5,\"Top\":6}]}]}";
        const string emptyPages = "{\"Format\":\"inkcanvas-board\",\"Version\":1,\"Background\":\"white\",\"GridSpacing\":0,\"Pages\":[]}";

        var cases = new List<object>
        {
            new { id = "board-full", fn = "boardRoundtrip", input = new object[] { full }, expected = JsonDocument.Parse(full).RootElement.Clone(), tolerance = 1e-4 },
            new
            {
                id = "board-video-only-defaults",
                fn = "boardRoundtrip",
                input = new object[] { videoOnly },
                expected = new
                {
                    Format = "inkcanvas-board",
                    Version = 1,
                    Background = "Grid",
                    GridSpacing = 30,
                    Pages = new[]
                    {
                        new
                        {
                            Strokes = Array.Empty<object>(),
                            Images = Array.Empty<object>(),
                            Media = new[]
                            {
                                new
                                {
                                    Id = "m2", Name = "v.mp4", Data = "AAAA", ContentType = "video/mp4",
                                    Left = 5, Top = 6, Width = 360, Height = 240, Transform = (double[]?)null
                                }
                            }
                        }
                    }
                },
                tolerance = 1e-4
            },
            new
            {
                id = "board-empty-pages",
                fn = "boardRoundtrip",
                input = new object[] { emptyPages },
                expected = new
                {
                    Format = "inkcanvas-board",
                    Version = 1,
                    Background = "White",
                    GridSpacing = 40,
                    Pages = new[]
                    {
                        new { Strokes = Array.Empty<object>(), Images = Array.Empty<object>(), Media = Array.Empty<object>() }
                    }
                },
                tolerance = 1e-4
            }
        };

        JsonWriter.Write(dir, "board-roundtrip.json", new
        {
            id = "board-roundtrip",
            entry = "harmony/core/src/main/ets/persistence/BoardPackCodec.ts",
            cases
        });
    }

    private static void WritePpt(string dir)
    {
        const string full = "{\"Version\":1,\"LastSlideIndex\":5,\"LastShowPosition\":8,\"Slides\":[{\"SlideId\":256,\"SlideIndex\":1,\"File\":\"slide-256.icink\"}]}";
        const string nulls = "{\"Version\":1,\"LastSlideIndex\":null,\"LastShowPosition\":null,\"Slides\":[]}";
        const string partial = "{\"Slides\":[{\"SlideId\":1,\"SlideIndex\":2,\"File\":\"s.icink\"}]}";

        var cases = new List<object>
        {
            new { id = "ppt-full", fn = "pptManifestRoundtrip", input = new object[] { full }, expected = JsonDocument.Parse(full).RootElement.Clone(), tolerance = 1e-4 },
            new { id = "ppt-nulls", fn = "pptManifestRoundtrip", input = new object[] { nulls }, expected = JsonDocument.Parse(nulls).RootElement.Clone(), tolerance = 1e-4 },
            new
            {
                id = "ppt-partial-defaults",
                fn = "pptManifestRoundtrip",
                input = new object[] { partial },
                expected = new
                {
                    Version = 1,
                    LastSlideIndex = (int?)null,
                    LastShowPosition = (int?)null,
                    Slides = new[] { new { SlideId = 1, SlideIndex = 2, File = "s.icink" } }
                },
                tolerance = 1e-4
            }
        };

        JsonWriter.Write(dir, "ppt-manifest.json", new
        {
            id = "ppt-manifest",
            entry = "harmony/core/src/main/ets/persistence/PptManifestCodec.ts",
            cases
        });
    }
}
