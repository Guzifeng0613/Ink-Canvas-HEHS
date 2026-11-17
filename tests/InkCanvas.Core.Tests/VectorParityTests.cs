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
