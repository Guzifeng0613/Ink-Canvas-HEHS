// @parity src/InkCanvas.App/Services/WindowChrome.cs（浮窗/面板定位的纯计算部分）
// 平板与 2in1 共用同一套浮窗定位逻辑；断点只改变尺寸，不改变机制。
export interface PointResult {
  X: number;
  Y: number;
}

export function floatingBarPosition(
  windowWidth: number,
  windowHeight: number,
  barWidth: number,
  barHeight: number,
  bottomMargin: number
): PointResult {
  const x = (windowWidth - barWidth) / 2;
  const y = windowHeight - barHeight - bottomMargin;
  return { X: Math.max(0, x), Y: Math.max(0, y) };
}

export function clampToWindow(
  x: number,
  y: number,
  width: number,
  height: number,
  windowWidth: number,
  windowHeight: number
): PointResult {
  return {
    X: Math.min(Math.max(0, x), Math.max(0, windowWidth - width)),
    Y: Math.min(Math.max(0, y), Math.max(0, windowHeight - height))
  };
}

// PPT/课件导航面板四角与底部定位；尺寸由调用方给出。
export function cornerPanelPosition(
  corner: number,
  windowWidth: number,
  windowHeight: number,
  panelWidth: number,
  panelHeight: number,
  margin: number
): PointResult {
  if (corner === 0) {
    return { X: margin, Y: margin };
  }
  if (corner === 1) {
    return { X: windowWidth - panelWidth - margin, Y: margin };
  }
  if (corner === 2) {
    return { X: windowWidth - panelWidth - margin, Y: windowHeight - panelHeight - margin };
  }
  return { X: margin, Y: windowHeight - panelHeight - margin };
}
