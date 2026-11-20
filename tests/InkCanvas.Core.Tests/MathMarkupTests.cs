using InkCanvas.AnalyticGeometry.Algebra;
using Xunit;

namespace InkCanvas.Core.Tests;

public sealed class MathMarkupTests
{
    [Theory]
    [InlineData("y=a*x^2+b*x+c", "y=a*x^2+b*x+c")]
    [InlineData("y=sin(x)/2", "y=(sin(x))/(2)")]
    [InlineData("x^2/9-y^2/4=1", "(x)^(2)/(9)-(y)^(2)/(4)=1")]
    [InlineData("y=sqrt(abs(x))", "y=sqrt(abs(x))")]
    [InlineData("x=2π*t", "x=2*pi*t")]
    public void ExpressionAndMathMlRoundTrip(string input, string expected)
    {
        string mathMl = MathMarkup.ToMathML(input);
        Assert.Contains("<math", mathMl);
        Assert.Contains("<mfrac", mathMl, StringComparison.Ordinal);
        Assert.Equal(expected, MathMarkup.ToExpression(mathMl));
    }

    [Fact]
    public void NativeMathMlFractionsAndPowerBecomePlotExpression()
    {
        const string mathMl = "<math xmlns='http://www.w3.org/1998/Math/MathML'><mrow><mi>y</mi><mo>=</mo><mfrac><msup><mi>x</mi><mn>2</mn></msup><mn>4</mn></mfrac></mrow></math>";
        Assert.Equal("y=((x)^(2))/(4)", MathMarkup.ToExpression(mathMl));
    }

    [Theory]
    [InlineData("<!DOCTYPE math [<!ENTITY x SYSTEM 'file:///C:/windows/win.ini'>]><math>&x;</math>")]
    [InlineData("<math><mroot><mi>x</mi></mroot></math>")]
    public void RejectsUnsafeOrMalformedMathMl(string mathMl) => Assert.ThrowsAny<Exception>(() => MathMarkup.ToExpression(mathMl));
}
