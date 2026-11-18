// @parity src/InkCanvas.Features/Tools/Tools.cs (ScreenshotService 的应用内替代)
// HarmonyOS 无跨应用截图：仅截取本应用组件（componentSnapshot）。
import { componentSnapshot } from '@kit.ArkUI';
import { image } from '@kit.ImageKit';
import { fileIo } from '@kit.CoreFileKit';

export async function captureComponentToPng(componentId: string, path: string): Promise<boolean> {
  let pixelMap: image.PixelMap | null = null;
  let packer: image.ImagePacker | null = null;
  try {
    const dir = path.substring(0, path.lastIndexOf('/'));
    if (dir.length > 0 && !fileIo.accessSync(dir)) {
      fileIo.mkdirSync(dir, true);
    }
    pixelMap = await componentSnapshot.get(componentId);
    packer = image.createImagePacker();
    const data = await packer.packing(pixelMap, { format: 'image/png', quality: 100 });
    const file = fileIo.openSync(path, fileIo.OpenMode.CREATE | fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.TRUNC);
    fileIo.writeSync(file.fd, data);
    fileIo.closeSync(file);
    return true;
  } catch (e) {
    return false;
  } finally {
    if (packer !== null) {
      try { await packer.release(); } catch (e) { void e; }
    }
    if (pixelMap !== null) {
      try { pixelMap.release(); } catch (e) { void e; }
    }
  }
}
