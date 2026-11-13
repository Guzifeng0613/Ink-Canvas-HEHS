using InkCanvas.Core.Imaging;
using Xunit;

namespace InkCanvas.Core.Tests;

/// <summary>
/// Premultiplied-alpha compositing, used to blit a small live-ink region over a cached
/// committed layer instead of reading the whole screen back from the GPU each frame.
/// </summary>
public class PixelCompositingTests
{
    private static byte[] Solid(int w, int h, byte b, byte g, byte r, byte a)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4 + 0] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = a;
        }
        return px;
    }

    [Fact]
    public void OpaqueSourceReplacesDestination()
    {
        var dst = Solid(2, 2, 10, 20, 30, 255);
        var src = Solid(2, 2, 200, 100, 50, 255);
        PixelCompositing.BlendOver(dst, 2, 2, 0, 0, src, 2, 2);
        Assert.Equal(src, dst);
    }

    [Fact]
    public void FullyTransparentSourceLeavesDestinationUntouched()
    {
        var dst = Solid(2, 2, 10, 20, 30, 255);
        var expected = (byte[])dst.Clone();
        var src = Solid(2, 2, 0, 0, 0, 0);
        PixelCompositing.BlendOver(dst, 2, 2, 0, 0, src, 2, 2);
        Assert.Equal(expected, dst);
    }

    [Fact]
    public void HalfAlphaSourceOverOpaqueBlackIsHalfSource()
    {
        var dst = Solid(1, 1, 0, 0, 0, 255);
        // Premultiplied: colour channels already scaled by alpha (200 * 0.5 = 100).
        var src = new byte[] { 100, 100, 100, 128 };
        PixelCompositing.BlendOver(dst, 1, 1, 0, 0, src, 1, 1);
        Assert.Equal(100, dst[0]);
        Assert.Equal(255, dst[3]);
    }

    [Fact]
    public void WritesAtRequestedOffsetOnly()
    {
        var dst = Solid(4, 4, 0, 0, 0, 255);
        var src = Solid(1, 1, 255, 255, 255, 255);
        PixelCompositing.BlendOver(dst, 4, 4, 2, 1, src, 1, 1);

        int touched = (1 * 4 + 2) * 4;
        Assert.Equal(255, dst[touched]);
        // A neighbour pixel must be untouched.
        Assert.Equal(0, dst[touched - 4]);
    }

    [Fact]
    public void ClipsSourceThatOverflowsDestination()
    {
        var dst = Solid(2, 2, 0, 0, 0, 255);
        var src = Solid(4, 4, 255, 255, 255, 255);
        // Would run off the right/bottom edge; must clip rather than throw.
        PixelCompositing.BlendOver(dst, 2, 2, 1, 1, src, 4, 4);
        Assert.Equal(255, dst[(1 * 2 + 1) * 4]);
    }

    [Fact]
    public void NegativeOffsetClips()
    {
        var dst = Solid(2, 2, 0, 0, 0, 255);
        var src = Solid(2, 2, 255, 255, 255, 255);
        PixelCompositing.BlendOver(dst, 2, 2, -1, -1, src, 2, 2);
        Assert.Equal(255, dst[0]);
        Assert.Equal(0, dst[3 * 4]);
    }

    [Fact]
    public void EmptySourceIsNoOp()
    {
        var dst = Solid(2, 2, 7, 8, 9, 255);
        var expected = (byte[])dst.Clone();
        PixelCompositing.BlendOver(dst, 2, 2, 0, 0, Array.Empty<byte>(), 0, 0);
        Assert.Equal(expected, dst);
    }
}