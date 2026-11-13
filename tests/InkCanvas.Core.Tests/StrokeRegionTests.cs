using InkCanvas.Core.Ink;
using System.Numerics;
using Xunit;

namespace InkCanvas.Core.Tests;

/// <summary>
/// Regression tests for a single stroke crossing itself. If its whole outline is submitted as
/// one even-odd contour, the crossing becomes a hole and the background shows through.
/// </summary>
public class StrokeRegionTests
{
    private static InkStroke CrossStroke(params Vector2[] points)
    {
        var stroke = new InkStroke(new StrokeAttributes
        {
            BaseWidth = 20,
            TaperKind = TaperKind.None,
            IsHighlighter = true,
            Color = InkColor.Opaque(240, 200, 0),
        });
        foreach (var p in points) stroke.AddPoint(p.X, p.Y);
        stroke.Complete();
        return stroke;
    }

    [Fact]
    public void SelfCrossingStrokeProducesOverlappingFilledRegionsNotOneEvenOddFigure()
    {
        var stroke = CrossStroke(
            new Vector2(0, 0),
            new Vector2(100, 100),
            new Vector2(0, 100),
            new Vector2(100, 0));

        var regions = StrokeGeometry.BuildUnionContours(stroke);

        // Three segment regions cross one another. Each is filled independently then grouped
        // with the Winding rule, so overlap adds coverage instead of cutting a boolean hole.
        Assert.Equal(stroke.Count - 1, regions.Length);
        Assert.All(regions, contour => Assert.True(StrokeGeometry.SignedArea(contour) > 0));
    }

    [Fact]
    public void ReversingSegmentDirectionKeepsSameFillWinding()
    {
        var forward = StrokeGeometry.BuildSegmentOutline(new Vector2(0, 0), 8,
            new Vector2(100, 20), 8);
        var backward = StrokeGeometry.BuildSegmentOutline(new Vector2(100, 20), 8,
            new Vector2(0, 0), 8);

        Assert.True(StrokeGeometry.SignedArea(forward) > 0);
        Assert.True(StrokeGeometry.SignedArea(backward) > 0);
    }

    [Fact]
    public void SinglePointStillHasOneFilledRegion()
    {
        var stroke = CrossStroke(new Vector2(30, 40));
        var regions = StrokeGeometry.BuildUnionContours(stroke);
        Assert.Single(regions);
        Assert.True(StrokeGeometry.SignedArea(regions[0]) > 0);
    }
}