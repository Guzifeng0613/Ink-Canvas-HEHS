// @parity src/InkCanvas.Platform/System/SystemServices.cs（设备/屏幕部分）
import { deviceInfo } from '@kit.BasicServicesKit';

export function deviceType(): string {
  return deviceInfo.deviceType;
}

export function is2in1(): boolean {
  return deviceType() === '2in1' || deviceType() === 'pc';
}

export function isTablet(): boolean {
  return deviceType() === 'tablet';
}

export function isPhone(): boolean {
  return deviceType() === 'phone';
}

// 平板与 2in1 共用完整布局与同一套浮窗逻辑；手机仅初步适配。
export function isFullDevice(): boolean {
  return is2in1() || isTablet();
}
