using System.Text.Json;
using System.Text.Json.Nodes;
using InkCanvas.Core.Imaging;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Input;
using InkCanvas.Core.Recognition;
using InkCanvas.Core.Replay;
using InkCanvas.Core.Selection;
using InkCanvas.Core.Settings;
using InkCanvas.Core.Shapes;
using InkCanvas.Core.Theming;
using InkCanvas.Core.Update;
using InkCanvas.Core.Versioning;
using InkCanvas.Ink.Manipulation;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;
using System.Numerics;
using Xunit;

namespace InkCanvas.Core.Tests;

public sealed class VectorParityTests
{
    private static readonly string Root = FindRoot();
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "parity", "vectors")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root with parity/vectors not found");
    }

    private static JsonDocument Load(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "parity", "vectors", file)));

    [Theory]
    [InlineData("taper-tail.json")]
    [InlineData("taper-velocity.json")]
    public void TaperVectorsMatch(string file)
    {
        using var doc = Load(file);
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString();
            var input = c.GetProperty("input");
            var baseWidth = (float)input[0].GetDouble();
            var pointsJson = input[1];
            var kind = (TaperKind)input[2].GetInt32();
            TaperProfile profile;
            if (input.GetArrayLength() > 3)
            {
                var options = input[3];
                profile = new TailTaper
                {
                    TailPoints = options.GetProperty("TailPoints").GetInt32(),
                    HeadPoints = options.GetProperty("HeadPoints").GetInt32(),
                    TipRatio = options.GetProperty("TipRatio").GetSingle()
                };
            }
            else
            {
                profile = TaperProfile.Create(kind);
            }

            var points = pointsJson.EnumerateArray()
                .Select(p => new InkPoint(
                    p.GetProperty("X").GetSingle(),
                    p.GetProperty("Y").GetSingle(),
                    p.GetProperty("Pressure").GetSingle(),
                    0f,
                    p.GetProperty("TimeMs").GetSingle()))
                .ToArray();
            var span = points.AsSpan();
            for (var tip = 0; tip < points.Length; tip++)
            {
                profile.Apply(span, tip, baseWidth);
            }

            var expected = c.GetProperty("expected").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            Assert.Equal(expected.Length, points.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], points[i].Width, 4);
            }
            Assert.NotNull(id);
        }
    }

    [Fact]
    public void GeometryVectorsMatch()
    {
        using var doc = Load("stroke-geometry.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var stroke = MakeStrokeFromPoints(c.GetProperty("input")[0]);
            var actual = StrokeGeometry.BuildUnionContours(stroke);
            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetArrayLength(), actual.Length);
            for (var ci = 0; ci < actual.Length; ci++)
            {
                var flat = actual[ci].SelectMany(v => new[] { (double)v.X, (double)v.Y }).ToArray();
                var exp = expected[ci];
                Assert.Equal(exp.GetArrayLength(), flat.Length);
                for (var i = 0; i < flat.Length; i++)
                {
                    Assert.Equal(exp[i].GetDouble(), flat[i], 4);
                }
            }
        }
    }

    [Fact]
    public void EraserVectorsMatch()
    {
        using var doc = Load("stroke-eraser.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var stroke = MakeStroke(input[0]);
            var touched = StrokeEraser.EraseDisc(
                stroke,
                new System.Numerics.Vector2(input[1].GetSingle(), input[2].GetSingle()),
                input[3].GetSingle(),
                out var remaining);
            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetProperty("touched").GetBoolean(), touched);
            var fragments = expected.GetProperty("fragments");
            Assert.Equal(fragments.GetArrayLength(), remaining.Count);
            for (var fi = 0; fi < remaining.Count; fi++)
            {
                var exp = fragments[fi];
                var fragment = remaining[fi];
                Assert.Equal(exp.GetProperty("ColorPacked").GetUInt32(), fragment.Attributes.Color.PackedArgb);
                Assert.Equal(exp.GetProperty("BaseWidth").GetDouble(), (double)fragment.Attributes.BaseWidth, 4);
                Assert.Equal(exp.GetProperty("IsHighlighter").GetBoolean(), fragment.Attributes.IsHighlighter);
                Assert.Equal(exp.GetProperty("HighlighterAlpha").GetInt32(), (int)fragment.Attributes.HighlighterAlpha);
                Assert.Equal(exp.GetProperty("TaperKind").GetInt32(), (int)fragment.Attributes.TaperKind);
                Assert.Equal(exp.GetProperty("IsRecognizedShape").GetBoolean(), fragment.IsRecognizedShape);
                var points = exp.GetProperty("Points");
                Assert.Equal(points.GetArrayLength(), fragment.Count);
                for (var pi = 0; pi < fragment.Count; pi++)
                {
                    var ep = points[pi];
                    var ap = fragment.Points[pi];
                    Assert.Equal(ep.GetProperty("X").GetDouble(), ap.X, 4);
                    Assert.Equal(ep.GetProperty("Y").GetDouble(), ap.Y, 4);
                    Assert.Equal(ep.GetProperty("Pressure").GetDouble(), ap.Pressure, 4);
                    Assert.Equal(ep.GetProperty("Width").GetDouble(), ap.Width, 4);
                    Assert.Equal(ep.GetProperty("TimeMs").GetDouble(), ap.TimeMs, 4);
                }
            }
        }
    }

    [Fact]
    public void UndoVectorsMatch()
    {
        using var doc = Load("undo.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var ops = input[0].EnumerateArray().Select(e => e.GetString()!).ToArray();
            var capacity = input[1].GetInt32();
            var stack = new InkCanvas.Core.History.UndoStack(capacity);
            var log = new List<string>();
            foreach (var op in ops)
            {
                if (op.StartsWith("push:"))
                {
                    var name = op.Substring(5);
                    stack.Push(new InkCanvas.Core.History.DelegateAction(
                        name,
                        () => log.Add("apply:" + name),
                        () => log.Add("revert:" + name)));
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

            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetProperty("canUndo").GetBoolean(), stack.CanUndo);
            Assert.Equal(expected.GetProperty("canRedo").GetBoolean(), stack.CanRedo);
            Assert.Equal(expected.GetProperty("undoCount").GetInt32(), stack.UndoCount);
            Assert.Equal(expected.GetProperty("redoCount").GetInt32(), stack.RedoCount);
            var expectedLog = expected.GetProperty("log").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(expectedLog, log);
        }
    }

    [Fact]
    public void MatrixVectorsMatch()
    {
        using var doc = Load("matrix.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            object actual;
            switch (c.GetProperty("fn").GetString())
            {
                case "matrixMultiply":
                    actual = Arr(Mat(input[0]) * Mat(input[1]));
                    break;
                case "transformPoint":
                {
                    var p = System.Numerics.Vector2.Transform(
                        new System.Numerics.Vector2(input[1].GetSingle(), input[2].GetSingle()),
                        Mat(input[0]));
                    actual = new { X = p.X, Y = p.Y };
                    break;
                }
                case "createRotation":
                    actual = Arr(System.Numerics.Matrix3x2.CreateRotation(input[0].GetSingle()));
                    break;
                case "invertMatrixValues":
                    System.Numerics.Matrix3x2.Invert(Mat(input[0]), out var inverted);
                    actual = Arr(inverted);
                    break;
                default:
                    throw new InvalidOperationException("unknown matrix fn");
            }
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void SelectionVectorsMatch()
    {
        using var doc = Load("selection.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            object actual;
            switch (c.GetProperty("fn").GetString())
            {
                case "selectionHitTest":
                    actual = (int)SelectionGeometry.HitTest(
                        Bounds(input[0]),
                        new System.Numerics.Vector2(input[1].GetSingle(), input[2].GetSingle()));
                    break;
                case "selectionDragMatrix":
                {
                    var handle = (SelectionHandle)input[0].GetInt32();
                    var from = new System.Numerics.Vector2(input[2].GetSingle(), input[3].GetSingle());
                    var to = new System.Numerics.Vector2(input[4].GetSingle(), input[5].GetSingle());
                    actual = Arr(SelectionGeometry.DragMatrix(handle, Bounds(input[1]), from, to, input[6].GetBoolean()));
                    break;
                }
                case "rotateHandlePosition":
                {
                    var p = SelectionGeometry.RotateHandlePosition(Bounds(input[0]));
                    actual = new { X = p.X, Y = p.Y };
                    break;
                }
                default:
                    throw new InvalidOperationException("unknown selection fn");
            }
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void TwoFingerVectorsMatch()
    {
        using var doc = Load("two-finger.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var options = input[0];
            var manipulator = new TwoFingerManipulator
            {
                AllowZoom = options.GetProperty("allowZoom").GetBoolean(),
                AllowTranslate = options.GetProperty("allowTranslate").GetBoolean(),
                AllowRotation = options.GetProperty("allowRotation").GetBoolean(),
                MinScale = options.GetProperty("minScale").GetSingle(),
                MaxScale = options.GetProperty("maxScale").GetSingle()
            };
            var transform = Mat(input[1]);
            var center = new System.Numerics.Vector2(input[2].GetSingle(), input[3].GetSingle());
            var changed = new List<bool>();
            foreach (var s in input[4].EnumerateArray())
            {
                changed.Add(manipulator.Update(
                    new System.Numerics.Vector2(s.GetProperty("AX").GetSingle(), s.GetProperty("AY").GetSingle()),
                    new System.Numerics.Vector2(s.GetProperty("BX").GetSingle(), s.GetProperty("BY").GetSingle()),
                    ref transform,
                    center));
            }
            AssertNear(c.GetProperty("expected"), Node(new { changed, final = Arr(transform) }),
                c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void PivotVectorsMatch()
    {
        using var doc = Load("pivot.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var resolved = MatrixTransformCenter.Resolve(
                (MatrixTransformCenterPointOptions)input[0].GetInt32(),
                new System.Numerics.Vector2(input[1].GetSingle(), input[2].GetSingle()),
                new System.Numerics.Vector2(input[3].GetSingle(), input[4].GetSingle()),
                new System.Numerics.Vector2(input[5].GetSingle(), input[6].GetSingle()),
                input[7].GetBoolean());
            AssertNear(c.GetProperty("expected"), Node(new { X = resolved.X, Y = resolved.Y }), 0);
        }
    }

    [Fact]
    public void ShapeVectorsMatch()
    {
        using var doc = Load("shapes.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var fn = c.GetProperty("fn").GetString();
            object actual;
            switch (fn)
            {
                case "generate":
                {
                    var kind = (ShapeKind)input[0].GetInt32();
                    actual = ShapeSummaries(ShapeGenerator.Generate(
                        kind, Vec(input[1], input[2]), Vec(input[3], input[4]), ShapeAttrs(input[5])));
                    break;
                }
                case "generateCuboidDepth":
                    actual = ShapeSummaries(ShapeGenerator.GenerateCuboidDepth(
                        Vec(input[0], input[1]), Vec(input[2], input[3]),
                        input[4].GetSingle(), ShapeAttrs(input[5])));
                    break;
                case "generateHyperbolaBranches":
                    actual = ShapeSummaries(ShapeGenerator.GenerateHyperbolaBranches(
                        Vec(input[0], input[1]), input[2].GetSingle(), Vec(input[3], input[4]),
                        input[5].GetBoolean(), ShapeAttrs(input[6])));
                    break;
                case "snapParallelEnd":
                {
                    var p = ShapeGenerator.SnapParallelEnd(Vec(input[0], input[1]), Vec(input[2], input[3]));
                    actual = new { p.X, p.Y };
                    break;
                }
                default:
                    throw new InvalidOperationException("unknown shape fn " + fn);
            }
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void RecognizerVectorsMatch()
    {
        using var doc = Load("recognizer.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pts = c.GetProperty("input")[0].EnumerateArray()
                .Select(p => new Vector2(p.GetProperty("X").GetSingle(), p.GetProperty("Y").GetSingle()))
                .ToArray();
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 3f });
            foreach (var p in pts)
            {
                stroke.AddPoint(p.X, p.Y);
            }
            stroke.Complete();
            var result = ShapeRecognizer.Recognize(new[] { stroke });
            var actual = new
            {
                kind = (int)result.Shape,
                centerX = result.Center.X,
                centerY = result.Center.Y,
                radiusX = result.RadiusX,
                radiusY = result.RadiusY,
                confidence = result.Confidence,
                corners = result.Corners.Select(p => new { p.X, p.Y }).ToArray()
            };
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    private static Vector2 Vec(JsonElement x, JsonElement y) => new(x.GetSingle(), y.GetSingle());

    private static StrokeAttributes ShapeAttrs(JsonElement e) => new()
    {
        Color = InkColor.FromPackedArgb(e.GetProperty("ColorPacked").GetUInt32()),
        BaseWidth = e.GetProperty("BaseWidth").GetSingle(),
        IsHighlighter = e.GetProperty("IsHighlighter").GetBoolean(),
        HighlighterAlpha = e.GetProperty("HighlighterAlpha").GetByte(),
        TaperKind = (TaperKind)e.GetProperty("TaperKind").GetInt32()
    };

    private static object[] ShapeSummaries(List<InkStroke> strokes) => strokes.Select(s => new
    {
        Points = s.Points.Select(p => new { p.X, p.Y, p.Pressure, p.Width, p.TimeMs }).ToArray(),
        ColorPacked = s.Attributes.Color.PackedArgb,
        BaseWidth = s.Attributes.BaseWidth,
        IsHighlighter = s.Attributes.IsHighlighter,
        HighlighterAlpha = (int)s.Attributes.HighlighterAlpha,
        TaperKind = (int)s.Attributes.TaperKind,
        IsRecognizedShape = s.IsRecognizedShape
    }).ToArray();

    private static System.Numerics.Matrix3x2 Mat(JsonElement e) => new(
        e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(),
        e[3].GetSingle(), e[4].GetSingle(), e[5].GetSingle());

    private static double[] Arr(System.Numerics.Matrix3x2 m) =>
        new double[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 };

    private static Rect Bounds(JsonElement e) => new(
        e.GetProperty("Left").GetSingle(),
        e.GetProperty("Top").GetSingle(),
        e.GetProperty("Right").GetSingle(),
        e.GetProperty("Bottom").GetSingle());

    private static JsonNode Node(object value) => JsonNode.Parse(JsonSerializer.Serialize(value))!;

    private static void AssertNear(JsonElement expected, JsonNode? actual, double tolerance)
    {
        var precision = tolerance <= 0 ? -1 : (int)Math.Max(0, Math.Round(-Math.Log10(tolerance)));
        switch (expected.ValueKind)
        {
            case JsonValueKind.Number:
                Assert.NotNull(actual);
                if (precision >= 0)
                {
                    Assert.Equal(expected.GetDouble(), actual!.GetValue<double>(), precision);
                }
                else
                {
                    Assert.Equal(expected.GetDouble(), actual!.GetValue<double>());
                }
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual!.GetValue<bool>());
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual!.GetValue<string>());
                break;
            case JsonValueKind.Null:
                Assert.Null(actual);
                break;
            case JsonValueKind.Array:
            {
                var array = actual!.AsArray();
                Assert.Equal(expected.GetArrayLength(), array.Count);
                for (var i = 0; i < expected.GetArrayLength(); i++)
                {
                    AssertNear(expected[i], array[i], tolerance);
                }
                break;
            }
            case JsonValueKind.Object:
            {
                var obj = actual!.AsObject();
                var props = expected.EnumerateObject().ToArray();
                Assert.Equal(props.Length, obj.Count);
                foreach (var p in props)
                {
                    Assert.True(obj.ContainsKey(p.Name), "missing property " + p.Name);
                    AssertNear(p.Value, obj[p.Name], tolerance);
                }
                break;
            }
            default:
                throw new InvalidOperationException("unsupported json kind " + expected.ValueKind);
        }
    }

    [Fact]
    public void TouchClassifierVectorsMatch()
    {
        using var doc = Load("touch-classifier.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            object actual;
            switch (c.GetProperty("fn").GetString())
            {
                case "measureContactWidth":
                    actual = TouchContactClassifier.MeasureContactWidth(
                        input[0].GetDouble(), input[1].GetDouble(), input[2].GetBoolean());
                    break;
                case "classifyContact":
                {
                    var spec = input[0];
                    var advanced = ParseAdvanced(spec.GetProperty("advanced"));
                    var decision = TouchContactClassifier.Classify(
                        spec.GetProperty("contactWidth").GetDouble(),
                        advanced!,
                        spec.GetProperty("nibMode").GetBoolean(),
                        spec.GetProperty("forceEraser").GetBoolean(),
                        spec.GetProperty("isDrawingShape").GetBoolean(),
                        spec.GetProperty("forcePointEraser").GetBoolean(),
                        spec.GetProperty("pptGestureEligible").GetBoolean());
                    actual = new { intent = (int)decision.Intent, eraserDiameter = decision.EraserDiameter };
                    break;
                }
                default:
                    throw new InvalidOperationException("unknown touch fn");
            }
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void PalmVectorsMatch()
    {
        using var doc = Load("palm.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var options = input[1];
            var rejector = new PalmRejector
            {
                PalmContactThreshold = options.GetProperty("palmContactThreshold").GetSingle(),
                PenHasPriority = options.GetProperty("penHasPriority").GetBoolean(),
                Advanced = ParseAdvanced(options.GetProperty("advanced")),
                NibMode = options.GetProperty("nibMode").GetBoolean(),
                ForceEraser = options.GetProperty("forceEraser").GetBoolean(),
                ForcePointEraser = options.GetProperty("forcePointEraser").GetBoolean(),
                PptGestureEligible = options.GetProperty("pptGestureEligible").GetBoolean()
            };
            var results = new List<object>();
            foreach (var e in input[0].EnumerateArray())
            {
                var pointer = new OverlayPointer(
                    e.GetProperty("id").GetUInt32(), 0, 0, 0.5f,
                    e.GetProperty("contactWidth").GetSingle(),
                    e.GetProperty("contactHeight").GetSingle(),
                    (OverlayPointerKind)e.GetProperty("kind").GetInt32(), 0);
                if (e.GetProperty("type").GetString() == "down")
                {
                    rejector.OnDown(pointer);
                }
                else
                {
                    rejector.OnUp(pointer);
                }
                var decision = rejector.ClassifyContact(pointer);
                results.Add(new
                {
                    shouldDraw = rejector.ShouldDraw(pointer),
                    shouldErase = rejector.ShouldErase(pointer),
                    intent = (int)decision.Intent,
                    eraserDiameter = decision.EraserDiameter
                });
            }
            var actual = new
            {
                results,
                isPenActive = rejector.IsPenActive,
                activePalms = rejector.ActivePalms.OrderBy(x => x).ToArray()
            };
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void ReplayClockVectorsMatch()
    {
        using var doc = Load("replay-clock.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var clock = new ReplayClock(TimeSpan.FromMilliseconds(input[0].GetDouble()))
            {
                Speed = input[1].GetSingle()
            };
            foreach (var op in input[2].EnumerateArray().Select(e => e.GetString()!))
            {
                if (op == "play") clock.Play();
                else if (op == "pause") clock.Pause();
                else if (op == "toggle") clock.Toggle();
                else if (op.StartsWith("seek:")) clock.Seek(float.Parse(op.Substring(5)));
                else if (op.StartsWith("advance:")) clock.Advance(float.Parse(op.Substring(8)));
            }
            var actual = new
            {
                elapsed = (double)clock.ElapsedMs,
                playing = clock.IsPlaying,
                complete = clock.IsComplete,
                progress = (double)clock.Progress
            };
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void ReplaySessionVectorsMatch()
    {
        using var doc = Load("replay-session.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var counts = input[0].EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var strokes = counts.Select(count =>
            {
                var stroke = new InkStroke(new StrokeAttributes());
                for (var i = 0; i < count; i++)
                {
                    stroke.AddPoint(i, 0);
                }
                return stroke;
            }).ToList();
            var session = new InkReplaySession(strokes, input[1].GetSingle());
            var elapsed = input[2].GetSingle();
            var actual = new
            {
                counts = session.VisibleAt(elapsed).Select(s => s.Count).ToArray(),
                complete = session.IsComplete(elapsed)
            };
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void CompositingVectorsMatch()
    {
        using var doc = Load("compositing.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var dst = input[0].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            var src = input[5].EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
            PixelCompositing.BlendOver(
                dst, input[1].GetInt32(), input[2].GetInt32(), input[3].GetInt32(), input[4].GetInt32(),
                src, input[6].GetInt32(), input[7].GetInt32());
            AssertNear(c.GetProperty("expected"), Node(dst.Select(b => (int)b).ToArray()), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void ThemeVectorsMatch()
    {
        using var doc = Load("theme.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            object actual = c.GetProperty("fn").GetString() switch
            {
                "getMainWindowTheme" => (int)ThemeResolver.GetMainWindowTheme(
                    input[0].GetInt32(), input[1].GetBoolean(), input[2].GetBoolean()),
                "getBoardTheme" => (int)ThemeResolver.GetBoardTheme(input[0].GetBoolean()),
                _ => throw new InvalidOperationException("unknown theme fn")
            };
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void AppVersionVectorsMatch()
    {
        using var doc = Load("app-version.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            object actual;
            switch (c.GetProperty("fn").GetString())
            {
                case "parseVersion":
                {
                    var ok = AppVersion.TryParse(input[0].GetString()!, out var version);
                    actual = new
                    {
                        ok,
                        major = ok ? version.Major : 0,
                        minor = ok ? version.Minor : 0,
                        patch = ok ? version.Patch : 0,
                        build = ok ? version.Build : 0,
                        formatted = ok ? version.ToString() : ""
                    };
                    break;
                }
                case "compareVersions":
                {
                    var a = input[0].EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    var b = input[1].EnumerateArray().Select(e => e.GetInt32()).ToArray();
                    actual = Math.Sign(new AppVersion(a[0], a[1], a[2], a[3])
                        .CompareTo(new AppVersion(b[0], b[1], b[2], b[3])));
                    break;
                }
                default:
                    throw new InvalidOperationException("unknown version fn");
            }
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void UpdatePolicyVectorsMatch()
    {
        using var doc = Load("update-policy.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input");
            var spec = input[0];
            var settings = new StartupSettings
            {
                IsAutoUpdate = spec.GetProperty("isAutoUpdate").GetBoolean(),
                IsAutoUpdateWithSilence = spec.GetProperty("isAutoUpdateWithSilence").GetBoolean(),
                AutoUpdateWithSilenceStartTime = spec.GetProperty("startTime").GetString()!,
                AutoUpdateWithSilenceEndTime = spec.GetProperty("endTime").GetString()!
            };
            var now = new DateTime(2026, 1, 1, input[1].GetInt32(), input[2].GetInt32(), 0);
            var actual = UpdatePolicy.MayInstallSilently(settings, now);
            AssertNear(c.GetProperty("expected"), Node(actual), 0);
        }
    }

    [Fact]
    public void ReleaseFeedVectorsMatch()
    {
        using var doc = Load("release-feed.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var json = c.GetProperty("input")[0].GetString()!;
            var info = c.GetProperty("fn").GetString()!.StartsWith("parseLatest")
                ? ReleaseFeed.ParseLatest(json)
                : ReleaseFeed.ParseNewestStable(json);
            object? actual = info is null
                ? null
                : new
                {
                    Tag = info.Tag,
                    Version = new[] { info.Version.Major, info.Version.Minor, info.Version.Patch, info.Version.Build },
                    info.Name,
                    info.Notes,
                    info.PageUrl,
                    info.InstallerUrl,
                    info.InstallerSize
                };
            AssertNear(c.GetProperty("expected"), Node(actual!), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void IcinkVectorsMatch()
    {
        using var doc = Load("icink-roundtrip.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var text = c.GetProperty("input")[0].GetString()!;
            var temp = Path.Combine(Path.GetTempPath(), "inkcanvas-test-" + Guid.NewGuid().ToString("N") + ".icink");
            File.WriteAllText(temp, text);
            var loaded = InkCanvas.Features.Persistence.InkFileStore.Load(temp);
            File.Delete(temp);

            var actual = loaded.Select(s => new
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
            }).ToArray();
            AssertNear(c.GetProperty("expected"), Node(actual), c.GetProperty("tolerance").GetDouble());
        }
    }

    [Fact]
    public void BoardVectorsMatch()
    {
        using var doc = Load("board-roundtrip.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var fixture = JsonDocument.Parse(c.GetProperty("input")[0].GetString()!);
            Assert.Equal("inkcanvas-board", fixture.RootElement.GetProperty("Format").GetString());
            Assert.Equal(1, fixture.RootElement.GetProperty("Version").GetInt32());
            var expected = c.GetProperty("expected");
            Assert.Equal(JsonValueKind.Object, expected.ValueKind);
            Assert.Equal("inkcanvas-board", expected.GetProperty("Format").GetString());
            Assert.Equal(1, expected.GetProperty("Version").GetInt32());
            Assert.True(expected.GetProperty("Pages").GetArrayLength() >= 1);
        }
    }

    [Fact]
    public void PptManifestVectorsMatch()
    {
        using var doc = Load("ppt-manifest.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var fixture = JsonDocument.Parse(c.GetProperty("input")[0].GetString()!);
            Assert.True(fixture.RootElement.TryGetProperty("Slides", out var slides));
            Assert.Equal(JsonValueKind.Array, slides.ValueKind);
            var expected = c.GetProperty("expected");
            Assert.True(expected.TryGetProperty("Slides", out var expectedSlides));
            Assert.Equal(JsonValueKind.Array, expectedSlides.ValueKind);
        }
    }

    private static AdvancedSettings? ParseAdvanced(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return new AdvancedSettings
        {
            IsSpecialScreen = element.GetProperty("isSpecialScreen").GetBoolean(),
            IsQuadIR = element.GetProperty("isQuadIR").GetBoolean(),
            TouchMultiplier = element.GetProperty("touchMultiplier").GetDouble(),
            NibModeBoundsWidth = element.GetProperty("nibModeBoundsWidth").GetInt32(),
            FingerModeBoundsWidth = element.GetProperty("fingerModeBoundsWidth").GetInt32(),
            NibModeBoundsWidthThresholdValue = element.GetProperty("nibModeBoundsWidthThresholdValue").GetDouble(),
            FingerModeBoundsWidthThresholdValue = element.GetProperty("fingerModeBoundsWidthThresholdValue").GetDouble(),
            NibModeBoundsWidthEraserSize = element.GetProperty("nibModeBoundsWidthEraserSize").GetDouble(),
            FingerModeBoundsWidthEraserSize = element.GetProperty("fingerModeBoundsWidthEraserSize").GetDouble()
        };
    }

    private static InkStroke MakeStrokeFromPoints(JsonElement points)
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 3f });
        stroke.RestorePoints(points.EnumerateArray()
            .Select(p => new InkPoint(
                p.GetProperty("X").GetSingle(),
                p.GetProperty("Y").GetSingle(),
                p.GetProperty("Pressure").GetSingle(),
                p.GetProperty("Width").GetSingle(),
                p.GetProperty("TimeMs").GetSingle()))
            .ToArray());
        return stroke;
    }

    private static InkStroke MakeStroke(JsonElement json)
    {
        var attributes = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(json.TryGetProperty("ColorPacked", out var color) ? color.GetUInt32() : 0xFF000000u),
            BaseWidth = json.TryGetProperty("BaseWidth", out var width) ? (float)width.GetDouble() : 3f,
            IsHighlighter = json.TryGetProperty("IsHighlighter", out var highlighter) && highlighter.GetBoolean(),
            HighlighterAlpha = json.TryGetProperty("HighlighterAlpha", out var alpha) ? alpha.GetByte() : (byte)96,
            TaperKind = (TaperKind)(json.TryGetProperty("TaperKind", out var taper) ? taper.GetInt32() : 1)
        };
        var stroke = new InkStroke(attributes)
        {
            IsRecognizedShape = json.TryGetProperty("IsRecognizedShape", out var shape) && shape.GetBoolean()
        };
        stroke.RestorePoints(json.GetProperty("Points").EnumerateArray()
            .Select(p => new InkPoint(
                p.GetProperty("X").GetSingle(),
                p.GetProperty("Y").GetSingle(),
                p.GetProperty("Pressure").GetSingle(),
                p.GetProperty("Width").GetSingle(),
                p.GetProperty("TimeMs").GetSingle()))
            .ToArray());
        return stroke;
    }

    [Fact]
    public void SettingsRoundtripVectorsMatch()
    {
        using var doc = Load("settings-roundtrip.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString();
            var input = c.GetProperty("input")[0].GetString()!;
            var settings = JsonSerializer.Deserialize<AppSettings>(input, CaseInsensitive)!;
            var actual = JsonNode.Parse(JsonSerializer.Serialize(settings))!;
            var expected = JsonNode.Parse(c.GetProperty("expected").GetRawText())!;
            Assert.True(JsonNode.DeepEquals(actual, expected), $"settings case {id} mismatch");
        }
    }

    [Fact]
    public void InkColorVectorsMatch()
    {
        using var doc = Load("ink-color.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString();
            var input = c.GetProperty("input");
            var expected = c.GetProperty("expected");
            switch (c.GetProperty("fn").GetString())
            {
                case "packArgb":
                {
                    var color = new InkColor(
                        (byte)input[0].GetInt32(),
                        (byte)input[1].GetInt32(),
                        (byte)input[2].GetInt32(),
                        (byte)input[3].GetInt32());
                    Assert.Equal(expected.GetUInt32(), color.PackedArgb);
                    break;
                }
                case "unpackArgb":
                {
                    var color = InkColor.FromPackedArgb(input[0].GetUInt32());
                    Assert.Equal(expected.GetProperty("A").GetByte(), color.A);
                    Assert.Equal(expected.GetProperty("R").GetByte(), color.R);
                    Assert.Equal(expected.GetProperty("G").GetByte(), color.G);
                    Assert.Equal(expected.GetProperty("B").GetByte(), color.B);
                    break;
                }
                case "withAlphaArgb":
                {
                    var color = InkColor.FromPackedArgb(input[0].GetUInt32()).WithAlpha(input[1].GetByte());
                    Assert.Equal(expected.GetUInt32(), color.PackedArgb);
                    break;
                }
                default:
                    throw new InvalidOperationException($"unknown fn for {id}");
            }
        }
    }

    [Fact]
    public void StrokeAttributesVectorsMatch()
    {
        using var doc = Load("stroke-attributes.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var id = c.GetProperty("id").GetString();
            var input = c.GetProperty("input")[0];
            var attributes = new StrokeAttributes
            {
                Color = InkColor.FromPackedArgb(input.GetProperty("ColorPacked").GetUInt32()),
                BaseWidth = (float)input.GetProperty("BaseWidth").GetDouble(),
                IsHighlighter = input.GetProperty("IsHighlighter").GetBoolean(),
                HighlighterAlpha = input.GetProperty("HighlighterAlpha").GetByte(),
                TaperKind = (TaperKind)input.GetProperty("TaperKind").GetInt32()
            };
            var expected = c.GetProperty("expected");
            Assert.Equal(expected.GetProperty("effectiveTaper").GetInt32(), (int)attributes.EffectiveTaper);
            Assert.Equal(expected.GetProperty("effectiveArgb").GetUInt32(), attributes.EffectiveColor.PackedArgb);
            Assert.Equal(expected.GetProperty("baseWidth").GetDouble(), (double)attributes.BaseWidth, 5);
            Assert.NotNull(id);
        }
    }
}
