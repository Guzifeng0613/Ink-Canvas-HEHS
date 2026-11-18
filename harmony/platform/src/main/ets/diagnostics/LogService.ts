// @parity src/InkCanvas.Core/Diagnostics/FileLog.cs（日志适配）
import { hilog } from '@kit.PerformanceAnalysisKit';

const DOMAIN = 0x0000;
const TAG = 'InkCanvas';

// advanced.isLogEnabled 接线：关闭后普通日志静默（错误仍输出，便于排障）。
let logEnabled: boolean = true;

export function setLogEnabled(enabled: boolean): void {
  logEnabled = enabled;
}

export function logInfo(message: string): void {
  if (!logEnabled) {
    return;
  }
  hilog.info(DOMAIN, TAG, '%{public}s', message);
}

export function logWarn(message: string): void {
  if (!logEnabled) {
    return;
  }
  hilog.warn(DOMAIN, TAG, '%{public}s', message);
}

export function logError(message: string): void {
  hilog.error(DOMAIN, TAG, '%{public}s', message);
}
