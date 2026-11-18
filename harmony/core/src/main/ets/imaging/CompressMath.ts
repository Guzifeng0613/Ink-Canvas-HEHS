// @parity src/InkCanvas.Features/Board/BoardService.cs（上传图片压缩至 1080P 的目标尺寸数学）
// 压缩目标尺寸：最长边不超过 maxDim，等比缩放；已小于阈值则不压缩（scale=1）。
export interface CompressTarget {
  width: number;
  height: number;
  scale: number;
}

export function compressTargetSize(width: number, height: number, maxDim: number): CompressTarget {
  const max = Math.max(width, height);
  if (max <= maxDim || max <= 0) {
    return { width, height, scale: 1 };
  }
  const scale = maxDim / max;
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
    scale
  };
}