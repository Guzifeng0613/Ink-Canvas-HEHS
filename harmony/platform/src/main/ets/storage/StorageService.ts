// @parity src/InkCanvas.Features/Persistence/Stores.cs（文件系统适配；原子写与目录）
import { fileIo } from '@kit.CoreFileKit';
import { BusinessError } from '@kit.BasicServicesKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

const DOMAIN = 0x0000;
const TAG = 'InkStorage';

export class StorageService {
  private rootDir: string;

  constructor(rootDir: string) {
    this.rootDir = rootDir;
  }

  private fullPath(relative: string): string {
    if (relative.length === 0) {
      return this.rootDir;
    }
    return this.rootDir + '/' + relative;
  }

  exists(relative: string): boolean {
    try {
      return fileIo.accessSync(this.fullPath(relative));
    } catch (e) {
      return false;
    }
  }

  readText(relative: string): string | null {
    const path = this.fullPath(relative);
    try {
      if (!fileIo.accessSync(path)) {
        return null;
      }
      return fileIo.readTextSync(path);
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'readText failed: %{public}s', String(e));
      return null;
    }
  }

  writeTextAtomic(relative: string, text: string): boolean {
    const path = this.fullPath(relative);
    const temp = path + '.tmp';
    try {
      const dir = path.substring(0, path.lastIndexOf('/'));
      if (dir.length > 0 && !fileIo.accessSync(dir)) {
        fileIo.mkdirSync(dir, true);
      }
      const file = fileIo.openSync(temp, fileIo.OpenMode.CREATE | fileIo.OpenMode.READ_WRITE | fileIo.OpenMode.TRUNC);
      fileIo.writeSync(file.fd, text);
      fileIo.closeSync(file);
      if (fileIo.accessSync(path)) {
        fileIo.unlinkSync(path);
      }
      fileIo.renameSync(temp, path);
      return true;
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'writeTextAtomic failed: %{public}s', String(e));
      return false;
    }
  }

  delete(relative: string): boolean {
    try {
      const path = this.fullPath(relative);
      if (fileIo.accessSync(path)) {
        fileIo.unlinkSync(path);
        return true;
      }
      return false;
    } catch (e) {
      return false;
    }
  }

  list(relativeDir: string): string[] {
    try {
      const dir = this.fullPath(relativeDir);
      if (!fileIo.accessSync(dir)) {
        return [];
      }
      return fileIo.listFileSync(dir);
    } catch (e) {
      return [];
    }
  }
}

export function describeError(e: object): string {
  const error = e as BusinessError;
  return error.code + ':' + error.message;
}
