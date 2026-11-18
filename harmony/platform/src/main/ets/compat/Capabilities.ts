// @parity src/InkCanvas.Platform/System/SystemServices.cs（能力探测部分）
// 只使用已核实的 SystemCapability 字符串；其余能力用 try/catch 或方法存在性探测。
import { deviceInfo } from '@kit.BasicServicesKit';
import {
  SYSCAP_ARKUI_FULL,
  SYSCAP_INPUT_POINTER,
  SYSCAP_STYLUS_HANDWRITE,
  SYSCAP_STYLUS_SERVICE,
  capabilityMatrix
} from './CapabilityMatrix';

export {
  SYSCAP_ARKUI_FULL,
  SYSCAP_INPUT_POINTER,
  SYSCAP_STYLUS_HANDWRITE,
  SYSCAP_STYLUS_SERVICE,
  capabilityMatrix
} from './CapabilityMatrix';
export type { CapabilityMatrix } from './CapabilityMatrix';

export function sdkApiVersion(): number {
  return deviceInfo.sdkApiVersion;
}

export function isApi26(): boolean {
  return sdkApiVersion() >= 26;
}

export function hasSysCap(capability: string): boolean {
  return canIUse(capability);
}

export function supportsStylusBoost(): boolean {
  return isApi26() && hasSysCap(SYSCAP_STYLUS_HANDWRITE);
}
