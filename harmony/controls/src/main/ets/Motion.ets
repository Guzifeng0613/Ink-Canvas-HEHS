// @parity src/InkCanvas.Controls/Animation/Motion.cs
// 动效 token：时长量级 140/240/380ms（与 parity/tokens.json 的动画曲线约定同量级）。
// 尊重系统“减少动效”（platform.systemMotionReduced 探测）+ 应用内开关；
// 减少动效时 transition 用 IDENTITY、animateTo 时长为 0。
// API 26 主基线：面板入场用弹簧曲线（curves.springMotion，API 10+）替代 WinUI 弹簧近似；
// 共享元素过渡用 geometryTransition（API 10+）对应 ConnectedAnimation。低版本回退 EaseOut。
// 系统动效缩放读取与实际曲线效果需真机核对。
import type { UIContext } from '@kit.ArkUI';
import { curves } from '@kit.ArkUI';
import { deviceInfo } from '@kit.BasicServicesKit';

export const MOTION_FAST: number = 140;
export const MOTION_STANDARD: number = 240;
export const MOTION_SLOW: number = 380;

export function motionDuration(base: number, reduced: boolean): number {
  return reduced ? 0 : base;
}

// 弹簧曲线（API 10+）：response 0.4s、dampingFraction 0.85（轻微过冲，接近 WinUI 入场弹簧）。
// 低版本或减少动效回退 EaseOut。返回 ICurve 供 animateTo/TransitionEffect.animation 使用。
export function springCurve(reduced: boolean): Curve | ICurve {
  if (reduced || deviceInfo.sdkApiVersion < 10) {
    return Curve.EaseOut;
  }
  return curves.springMotion(0.4, 0.85);
}

// 共享元素过渡 id（geometryTransition）：同一 id 在源/目标组件间做 ConnectedAnimation 式几何过渡。
// 用法：触发按钮与目标面板各 .geometryTransition(sharedId('countdown'))；减少动效时返回 '' 禁用。
export function sharedId(name: string, reduced: boolean): string {
  return reduced ? '' : 'inkcanvas-' + name;
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

// 居中面板（计时/抽奖/设置/指南/是否/更新/更新日志）：Opacity + Translate + 弹簧，380ms 量级。
export function panelTransition(reduced: boolean): TransitionEffect {
  if (reduced) {
    return TransitionEffect.IDENTITY;
  }
  return TransitionEffect.OPACITY
    .combine(TransitionEffect.translate({ y: 16 }))
    .animation({ duration: MOTION_SLOW, curve: springCurve(reduced) });
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
// 减少动效时时长为 0（状态立即落位）；否则用弹簧曲线。
export function animateMotion(uiContext: UIContext, reduced: boolean, duration: number, change: () => void): void {
  uiContext.animateTo({ duration: motionDuration(duration, reduced), curve: springCurve(reduced) }, change);
}