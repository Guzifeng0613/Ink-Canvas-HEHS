using InkCanvas.Core.Ink;
using InkCanvas.Core.Selection;
using System.Numerics;
using Xunit;

namespace InkCanvas.Core.Tests;

public class SelectionGeometryTests
{
    private static readonly Rect Bounds = Rect.FromLTRB(100, 100, 200, 200);

    [Theory]
    [InlineData(100, 100, SelectionHandle.TopLeft)]
    [InlineData(200, 100, SelectionHandle.TopRight)]
    [InlineData(200, 200, SelectionHandle.BottomRight)]
    [InlineData(100, 200, SelectionHandle.BottomLeft)]
    public void HitTestFindsCornerHandles(float x, float y, SelectionHandle expected)
        => Assert.Equal(expected, SelectionGeometry.HitTest(Bounds, new Vector2(x, y)));

    [Fact]
    public void HitTestInsideBoundsIsMove()
        => Assert.Equal(SelectionHandle.Move, SelectionGeometry.HitTest(Bounds, new Vector2(150, 150)));

    [Fact]
    public void HitTestFarOutsideIsNone()
        => Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(Bounds, new Vector2(400, 400)));

    [Fact]
    public void HitTestFindsRotateHandleAboveTopEdge()
    {
        var rotate = SelectionGeometry.RotateHandlePosition(Bounds);
        Assert.Equal(SelectionHandle.Rotate, SelectionGeometry.HitTest(Bounds, rotate));
        Assert.True(rotate.Y < Bounds.Top);
    }

    [Fact]
    public void CornerDragScalesAboutOppositeCorner()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.BottomRight, Bounds,
            new Vector2(200, 200), new Vector2(300, 300), keepAspect: false);

        Assert.Equal(new Vector2(100, 100), Vector2.Transform(new Vector2(100, 100), m));
        var moved = Vector2.Transform(new Vector2(200, 200), m);
        Assert.Equal(300f, moved.X, 3);
        Assert.Equal(300f, moved.Y, 3);
    }

    [Fact]
    public void MoveDragTranslates()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.Move, Bounds,
            new Vector2(150, 150), new Vector2(170, 160), keepAspect: false);
        var moved = Vector2.Transform(new Vector2(150, 150), m);
        Assert.Equal(170f, moved.X, 3);
        Assert.Equal(160f, moved.Y, 3);
    }

    [Fact]
    public void RotateDragRotatesAboutCenter()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.Rotate, Bounds,
            new Vector2(200, 150), new Vector2(150, 200), keepAspect: false);
        var moved = Vector2.Transform(new Vector2(200, 150), m);
        Assert.Equal(150f, moved.X, 2);
        Assert.Equal(200f, moved.Y, 2);
    }

    [Fact]
    public void KeepAspectUsesUniformScale()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.BottomRight, Bounds,
            new Vector2(200, 200), new Vector2(300, 250), keepAspect: true);
        var moved = Vector2.Transform(new Vector2(200, 200), m);
        // Uniform scale means both axes grow by the same factor.
        Assert.Equal(moved.X - 100f, moved.Y - 100f, 2);
    }

    [Fact]
    public void DegenerateDragDoesNotProduceNaN()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.BottomRight, Bounds,
            new Vector2(100, 100), new Vector2(100, 100), keepAspect: false);
        Assert.False(float.IsNaN(m.M11));
        Assert.False(float.IsNaN(m.M22));
    }

    [Fact]
    public void NoneHandleIsIdentity()
    {
        var m = SelectionGeometry.DragMatrix(SelectionHandle.None, Bounds,
            new Vector2(0, 0), new Vector2(50, 50), keepAspect: false);
        Assert.Equal(Matrix3x2.Identity, m);
    }
}