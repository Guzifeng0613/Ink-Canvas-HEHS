// @parity src/InkCanvas.Controls/Animation/Motion.cs
// 动效 token：时长量级 140/240/380ms（与 parity/tokens.json 的动画曲线约定同量级）。
// 尊重系统“减少动效”（platform.systemMotionReduced 探测）+ 应用内开关；
// 减少动效时 transition 用 IDENTITY、animateTo 时长为 0。
// 系统动效缩放读取与实际曲线效果需真机核对。
import type { UIContext } from '@kit.ArkUI';

export const MOTION_FAST: number = 140;
export const MOTION_STANDARD: number = 240;
export const MOTION_SLOW: number = 380;

export function motionDuration(base: number, reduced: boolean): number {
  return reduced ? 0 : base;
}

// 底部浮层（调色盘/图形菜单/工具/回放）：Opacity + Translate，自下而上 24vp。
export function overlayTransition(reduced: boolean): TransitionEffect {
  if (reduced) {
    return TransitionEffect.IDENTITY;
  }
  return TransitionEffect.OPACITY
    .combine(TransitionEffect.translate({ y: 24 }))
    .animation({ duration: MOTION_STANDARD, curve: Curve.EaseOut });
}

// 居中面板（计时/抽奖/设置/指南/是否/更新/更新日志）：Opacity + Translate，16vp，380ms。
export function panelTransition(reduced: boolean): TransitionEffect {
  if (reduced) {
    return TransitionEffect.IDENTITY;
  }
  return TransitionEffect.OPACITY
    .combine(TransitionEffect.translate({ y: 16 }))
    .animation({ duration: MOTION_SLOW, curve: Curve.EaseOut });
}

// 浮条折叠：主栏/手势卡横向淡出，140ms。
export function barCollapseTransition(reduced: boolean): TransitionEffect {
  if (reduced) {
    return TransitionEffect.IDENTITY;
  }
  return TransitionEffect.OPACITY
    .combine(TransitionEffect.translate({ x: -20 }))
    .animation({ duration: MOTION_FAST, curve: Curve.EaseInOut });
}

// animateTo 包装（UIContext 版，全局 animateTo 自 API 18 起弃用）：
// 减少动效时时长为 0（状态立即落位）。
export function animateMotion(uiContext: UIContext, reduced: boolean, duration: number, change: () => void): void {
  uiContext.animateTo({ duration: motionDuration(duration, reduced), curve: Curve.EaseInOut }, change);
}