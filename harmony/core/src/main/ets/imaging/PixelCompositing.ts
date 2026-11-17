// @parity src/InkCanvas.Core/Imaging/PixelCompositing.cs
// 预乘 BGRA source-over：out = src + dst * (1 - srcA)，四通道同式。
export function blendOver(
  dst: number[],
  dstWidth: number,
  dstHeight: number,
  dstX: number,
  dstY: number,
  src: number[],
  srcWidth: number,
  srcHeight: number
): number[] {
  if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0) {
    return dst;
  }
  if (src.length < srcWidth * srcHeight * 4 || dst.length < dstWidth * dstHeight * 4) {
    return dst;
  }

  const srcStartX = Math.max(0, -dstX);
  const srcStartY = Math.max(0, -dstY);
  const dstOriginX = Math.max(0, dstX);
  const dstOriginY = Math.max(0, dstY);
  const copyWidth = Math.min(srcWidth - srcStartX, dstWidth - dstOriginX);
  const copyHeight = Math.min(srcHeight - srcStartY, dstHeight - dstOriginY);
  if (copyWidth <= 0 || copyHeight <= 0) {
    return dst;
  }

  for (let y = 0; y < copyHeight; y++) {
    const srcRow = ((srcStartY + y) * srcWidth + srcStartX) * 4;
    const dstRow = ((dstOriginY + y) * dstWidth + dstOriginX) * 4;
    for (let x = 0; x < copyWidth; x++) {
      const si = srcRow + x * 4;
      const di = dstRow + x * 4;
      const alpha = src[si + 3];
      if (alpha === 0) {
        continue;
      }
      if (alpha === 255) {
        dst[di] = src[si];
        dst[di + 1] = src[si + 1];
        dst[di + 2] = src[si + 2];
        dst[di + 3] = 255;
        continue;
      }
      const inv = 255 - alpha;
      dst[di] = Math.trunc(src[si] + dst[di] * inv / 255);
      dst[di + 1] = Math.trunc(src[si + 1] + dst[di + 1] * inv / 255);
      dst[di + 2] = Math.trunc(src[si + 2] + dst[di + 2] * inv / 255);
      dst[di + 3] = Math.trunc(alpha + dst[di + 3] * inv / 255);
    }
  }
  return dst;
}
