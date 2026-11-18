// @parity src/InkCanvas.App/Services/WindowChrome.cs（断点/布局决策部分）
export type Breakpoint = 'xs' | 'sm' | 'md' | 'lg' | 'xl';

// 按窗口宽度（vp）划分：xs<320、sm 320–600、md 600–840、lg 840–1200、xl>1200。
export function breakpointForWidth(widthVp: number): Breakpoint {
  if (widthVp < 320) {
    return 'xs';
  }
  if (widthVp < 600) {
    return 'sm';
  }
  if (widthVp < 840) {
    return 'md';
  }
  if (widthVp <= 1200) {
    return 'lg';
  }
  return 'xl';
}

// 平板/2in1 走完整布局；手机初步适配。
export function isFullLayout(bp: Breakpoint): boolean {
  return bp === 'md' || bp === 'lg' || bp === 'xl';
}
