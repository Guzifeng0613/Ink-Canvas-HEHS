using InkCanvas.AnalyticGeometry.Handwriting;
using Xunit;

namespace InkCanvas.Core.Tests;

public sealed class MathInkRecognizerTests
{
    private static MathInkStroke Stroke(int id, params (double X, double Y)[] points)
        => new(id, points.Select(p => new InkSample(p.X, p.Y)).ToArray());

    [Fact]
    public void RecognizesBasicEquationWithEqualsAndVariables()
    {
        // Draw: y = x
        var strokes = new[]
        {
            // 'y' (two strokes)
            Stroke(0, (20, 40), (35, 70)),
            Stroke(1, (50, 40), (22, 100)),
            // '=' (two horizontal bars)
            Stroke(2, (70, 58), (105, 58)),
            Stroke(3, (70, 74), (105, 74)),
            // 'x' (two crossing diagonals)
            Stroke(4, (125, 45), (160, 88)),
            Stroke(5, (160, 45), (125, 88)),
        };

        var result = MathInkRecognizer.Recognize(strokes);
        Assert.Equal("y=x", result.Expression);
        Assert.NotNull(result.MathML);
        Assert.Contains("<mo>=</mo>", result.MathML);
        Assert.Equal(3, result.Symbols.Count);
    }

    [Fact]
    public void RecognizesTwoDimensionalFractionAndGeneratesMfracMathMl()
    {
        // Draw: x / 2 (numerator 'x' above horizontal fraction bar, denominator '2' below)
        var strokes = new[]
        {
            // Numerator 'x'
            Stroke(0, (80, 20), (110, 55)),
            Stroke(1, (110, 20), (80, 55)),
            // Long fraction bar
            Stroke(2, (50, 75), (145, 75)),
            // Denominator '2'
            Stroke(3, (78, 95), (92, 85), (110, 85), (118, 95), (108, 110), (76, 130), (120, 130)),
        };

        var result = MathInkRecognizer.Recognize(strokes);
        Assert.Equal("(x)/(2)", result.Expression);
        Assert.NotNull(result.MathML);
        Assert.Contains("<mfrac>", result.MathML);
    }

    [Fact]
    public void RecognizesSuperscriptExponentAndGeneratesMsupMathMl()
    {
        // Draw: x^2 (base 'x' + smaller '2' at upper-right)
        var strokes = new[]
        {
            // Base 'x'
            Stroke(0, (30, 60), (80, 120)),
            Stroke(1, (80, 60), (30, 120)),
            // Small '2' at upper-right
            Stroke(2, (92, 42), (102, 34), (116, 34), (122, 42), (112, 54), (90, 68), (124, 68)),
        };

        var result = MathInkRecognizer.Recognize(strokes);
        Assert.Equal("x^2", result.Expression);
        Assert.NotNull(result.MathML);
        Assert.Contains("<msup>", result.MathML);
    }

    [Fact]
    public void RecognizesRadicalWithEnclosedRadicand()
    {
        // Draw: √x
        var strokes = new[]
        {
            // Radical '√' with roof extending over x
            Stroke(0, (20, 75), (32, 75), (46, 115), (65, 35), (155, 35)),
            // 'x' underneath the roof
            Stroke(1, (82, 58), (122, 102)),
            Stroke(2, (122, 58), (82, 102)),
        };

        var result = MathInkRecognizer.Recognize(strokes);
        Assert.Equal("sqrt(x)", result.Expression);
        Assert.NotNull(result.MathML);
        Assert.Contains("<msqrt>", result.MathML);
    }

    [Fact]
    public void SymbolOverrideCorrectsRecognizedExpressionImmediately()
    {
        // Draw: x + 1, then override stroke 0 ('x') to 'a' (Select & Correct workflow)
        var strokes = new[]
        {
            Stroke(0, (20, 45), (55, 85)),
            Stroke(1, (55, 45), (20, 85)),
            Stroke(2, (80, 45), (80, 85)),
            Stroke(3, (65, 65), (95, 65)),
            Stroke(4, (120, 45), (120, 85)),
        };

        var initial = MathInkRecognizer.Recognize(strokes);
        Assert.Equal("x+1", initial.Expression);

        var corrected = MathInkRecognizer.Recognize(strokes, new Dictionary<int, string> { [0] = "a" });
        Assert.Equal("a+1", corrected.Expression);
    }
}
