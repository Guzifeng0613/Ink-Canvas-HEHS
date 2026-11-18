// @parity src/InkCanvas.Features/Board/BoardService.cs（上传图片压缩至 1080P 适配）
// 插入画板图片时按设置压缩：最长边 ≤1080 等比缩放后重打包 PNG。尽力而为，失败回退原图。
import { image } from '@kit.ImageKit';
import { compressTargetSize } from '@inkcanvas/core';
import { logWarn } from '../diagnostics/LogService';

export const COMPRESS_MAX_DIM: number = 1080;

export async function compressImageBuffer(buffer: ArrayBuffer, maxDim: number): Promise<ArrayBuffer> {
  let source: image.ImageSource | null = null;
  let pixelMap: image.PixelMap | null = null;
  let packer: image.ImagePacker | null = null;
  try {
    source = image.createImageSource(buffer);
    const info = await source.getImageInfo();
    const target = compressTargetSize(info.size.width, info.size.height, maxDim);
    if (target.scale >= 1) {
      return buffer;
    }
    pixelMap = await source.createPixelMap({ desiredSize: { width: target.width, height: target.height } });
    packer = image.createImagePacker();
    const packed = await packer.packing(pixelMap, { format: 'image/png', quality: 100 });
    return packed;
  } catch (e) {
    logWarn('image compress failed, using original: ' + String(e));
    return buffer;
  } finally {
    if (packer !== null) {
      try {
        packer.release();
      } catch (e) {
        void e;
      }
    }
    if (pixelMap !== null) {
      try {
        pixelMap.release();
      } catch (e) {
        void e;
      }
    }
    if (source !== null) {
      try {
        source.release();
      } catch (e) {
        void e;
      }
    }
  }
}