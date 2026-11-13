namespace InkCanvas.Core.Imaging;

/// <summary>Premultiplied-BGRA pixel helpers. Pure buffer maths, no graphics dependency.</summary>
public static class PixelCompositing
{
    /// <summary>
    /// Composites a premultiplied-BGRA source rectangle over a premultiplied-BGRA destination
    /// at (<paramref name="dstX"/>, <paramref name="dstY"/>), clipping to the destination.
    /// <para>
    /// Both buffers are premultiplied, so "source over" is <c>out = src + dst * (1 - srcA)</c>
    /// applied to all four channels including alpha.
    /// </para>
    /// </summary>
    public static void BlendOver(Span<byte> dst, int dstWidth, int dstHeight, int dstX, int dstY,
                                 ReadOnlySpan<byte> src, int srcWidth, int srcHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0) return;
        if (src.Length < srcWidth * srcHeight * 4 || dst.Length < dstWidth * dstHeight * 4) return;

        int srcStartX = Math.Max(0, -dstX);
        int srcStartY = Math.Max(0, -dstY);
        int dstOriginX = Math.Max(0, dstX);
        int dstOriginY = Math.Max(0, dstY);

        int copyWidth = Math.Min(srcWidth - srcStartX, dstWidth - dstOriginX);
        int copyHeight = Math.Min(srcHeight - srcStartY, dstHeight - dstOriginY);
        if (copyWidth <= 0 || copyHeight <= 0) return;

        for (int y = 0; y < copyHeight; y++)
        {
            int srcRow = ((srcStartY + y) * srcWidth + srcStartX) * 4;
            int dstRow = ((dstOriginY + y) * dstWidth + dstOriginX) * 4;
            for (int x = 0; x < copyWidth; x++)
            {
                int si = srcRow + x * 4;
                int di = dstRow + x * 4;
                byte alpha = src[si + 3];

                if (alpha == 0) continue;
                if (alpha == 255)
                {
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                    continue;
                }

                int inv = 255 - alpha;
                dst[di] = (byte)(src[si] + dst[di] * inv / 255);
                dst[di + 1] = (byte)(src[si + 1] + dst[di + 1] * inv / 255);
                dst[di + 2] = (byte)(src[si + 2] + dst[di + 2] * inv / 255);
                dst[di + 3] = (byte)(alpha + dst[di + 3] * inv / 255);
            }
        }
    }
}