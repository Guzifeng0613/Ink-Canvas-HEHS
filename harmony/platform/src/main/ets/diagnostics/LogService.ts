// @parity src/InkCanvas.Core/Diagnostics/FileLog.cs（日志适配）
import { hilog } from '@kit.PerformanceAnalysisKit';

const DOMAIN = 0x0000;
const TAG = 'InkCanvas';

export function logInfo(message: string): void {
  hilog.info(DOMAIN, TAG, '%{public}s', message);
}

export function logWarn(message: string): void {
  hilog.warn(DOMAIN, TAG, '%{public}s', message);
}

export function logError(message: string): void {
  hilog.error(DOMAIN, TAG, '%{public}s', message);
}
