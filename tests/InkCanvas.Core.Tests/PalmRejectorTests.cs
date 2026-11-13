using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;
using Xunit;

namespace InkCanvas.Core.Tests;

/// <summary>
/// Coverage for palm rejection, which previously had none. Rejection must never have a visual
/// side effect: the WPF original dimmed the whole ink layer, which was one of the two flicker
/// sources.
/// </summary>
public class PalmRejectorTests
{
    private static OverlayPointer Touch(uint id, float contact) =>
        new(id, 100, 100, 0.5f, contact, contact, OverlayPointerKind.Touch, 0);

    private static OverlayPointer Pen(uint id) =>
        new(id, 100, 100, 0.7f, 0, 0, OverlayPointerKind.Pen, 0);

    [Fact]
    public void SmallContactIsNotAPalm()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        Assert.False(r.IsPalmContact(Touch(1, 10f)));
    }

    [Fact]
    public void LargeContactIsAPalm()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        Assert.True(r.IsPalmContact(Touch(1, 45f)));
    }

    [Fact]
    public void PalmDoesNotDrawButDoesErase()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        var palm = Touch(1, 50f);
        r.OnDown(palm);
        Assert.False(r.ShouldDraw(palm));
        Assert.True(r.ShouldErase(palm));
    }

    [Fact]
    public void FingertipDrawsAndDoesNotErase()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        var finger = Touch(1, 8f);
        r.OnDown(finger);
        Assert.True(r.ShouldDraw(finger));
        Assert.False(r.ShouldErase(finger));
    }

    [Fact]
    public void ActivePenSuppressesTouchDrawing()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f, PenHasPriority = true };
        r.OnDown(Pen(7));
        Assert.True(r.IsPenActive);
        Assert.False(r.ShouldDraw(Touch(1, 8f)));
        Assert.True(r.ShouldDraw(Pen(7)));
    }

    [Fact]
    public void PenPriorityCanBeDisabled()
    {
        var r = new PalmRejector { PenHasPriority = false };
        r.OnDown(Pen(7));
        Assert.True(r.ShouldDraw(Touch(1, 8f)));
    }

    [Fact]
    public void LiftingPenReleasesTouchSuppression()
    {
        var r = new PalmRejector();
        r.OnDown(Pen(7));
        r.OnUp(Pen(7));
        Assert.False(r.IsPenActive);
        Assert.True(r.ShouldDraw(Touch(1, 8f)));
    }

    [Fact]
    public void LiftingPalmClearsIt()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        var palm = Touch(1, 50f);
        r.OnDown(palm);
        r.OnUp(palm);
        Assert.Empty(r.ActivePalms);
        Assert.False(r.ShouldErase(palm));
    }

    [Fact]
    public void ResetClearsAllState()
    {
        var r = new PalmRejector { PalmContactThreshold = 30f };
        r.OnDown(Pen(7));
        r.OnDown(Touch(1, 50f));
        r.Reset();
        Assert.False(r.IsPenActive);
        Assert.Empty(r.ActivePalms);
    }
}