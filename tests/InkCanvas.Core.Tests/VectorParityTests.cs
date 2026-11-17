using System.Text.Json;
using System.Text.Json.Nodes;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
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
