// @parity src/InkCanvas.Platform/System/SystemServices.cs（能力探测部分）
// 只使用已核实的 SystemCapability 字符串；其余能力用 try/catch 或方法存在性探测。
import { deviceInfo, settings } from '@kit.BasicServicesKit';
import type { common } from '@kit.AbilityKit';
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

// 系统材质（backgroundBlurStyle 的组件材质渲染）按 API 26 门控；
// API 24 回退现有半透/不透明卡片。BlurStyle 枚举本身 API 9+ 可用，
// 但低版本设备的材质渲染效果未验证，保守按 26 开启（需真机核对）。
export function supportsSystemMaterial(): boolean {
  return isApi26();
}

// SymbolGlyph 组件 API 11+ 可用；具体 sys.symbol 图标名的起始版本与
// 真机渲染效果需实机核对（见 IconGlyph.ets 映射表注释）。
export function supportsSymbolGlyph(): boolean {
  return sdkApiVersion() >= 11;
}

// 系统“减少动效”探测：读取动效缩放设置，任一为 0 即视为减少动效。
// settings.display 常量为 SDK 声明的键名（deprecated since 21 但仍可读）；
// 读取失败时回退“未减少”，由应用内开关兜底。需真机核对键值行为。
export function systemMotionReduced(context: common.UIAbilityContext): boolean {
  try {
    const animator = settings.getValueSync(context, settings.display.ANIMATOR_DURATION_SCALE, '1');
    const transition = settings.getValueSync(context, settings.display.TRANSITION_ANIMATION_SCALE, '1');
    return Number(animator) === 0 || Number(transition) === 0;
  } catch (e) {
    return false;
  }
}
