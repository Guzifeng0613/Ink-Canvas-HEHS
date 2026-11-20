// @parity src/InkCanvas.Core/Imaging/SvgImage.cs（尺寸解析部分）
// D3：SVG 尺寸归一纯逻辑——viewBox/width/height 解析 + CSS 长度单位换算（px/pt/pc/in/cm/mm，96DPI）。
// 缺省回落 300×150；单边缺失按 viewBox 宽高比补齐。XML 校验/命名空间修正是宿主侧字符串处理。
export interface SvgSize {
  Width: number;
  Height: number;
}

/** CSS 长度 → 像素（96DPI）；无效/非正数返回 0。 */
export function svgLength(value: string): number {
  const trimmed = value.trim().toLowerCase();
  if (trimmed.length === 0) {
    return 0;
  }
  const pixels = Number(trimmed);
  if (Number.isFinite(pixels)) {
    return pixels > 0 ? pixels : 0;
  }
  if (trimmed.length < 3) {
    return 0;
  }
  const number = Number(trimmed.substring(0, trimmed.length - 2));
  const unit = trimmed.substring(trimmed.length - 2);
  if (!Number.isFinite(number) || number <= 0) {
    return 0;
  }
  if (unit === 'px') {
    return number;
  }
  if (unit === 'pt') {
    return number * 96 / 72;
  }
  if (unit === 'pc') {
    return number * 16;
  }
  if (unit === 'in') {
    return number * 96;
  }
  if (unit === 'cm') {
    return number * 96 / 2.54;
  }
  if (unit === 'mm') {
    return number * 96 / 25.4;
  }
  return 0;
}

/**
 * viewBox（"x y w h"，空格/逗号/制表分隔）+ width/height 属性字符串 → 归一尺寸。
 * 单边缺失按 viewBox 比例补齐；双双缺失用 viewBox；仍缺省 300×150。
 */
export function svgResolveSize(viewBox: string, widthAttribute: string, heightAttribute: string): SvgSize {
  const parts = viewBox.split(/[\s,\t\r\n]+/).filter((part: string): boolean => part.length > 0);
  let viewWidth = 0;
  let viewHeight = 0;
  if (parts.length === 4) {
    const w = Number(parts[2]);
    const h = Number(parts[3]);
    if (Number.isFinite(w) && Number.isFinite(h)) {
      viewWidth = w;
      viewHeight = h;
    }
  }
  let width = svgLength(widthAttribute);
  let height = svgLength(heightAttribute);
  if (viewWidth > 0 && viewHeight > 0) {
    if (width <= 0 && height > 0) {
      width = height * viewWidth / viewHeight;
    }
    if (height <= 0 && width > 0) {
      height = width * viewHeight / viewWidth;
    }
    if (width <= 0) {
      width = viewWidth;
    }
    if (height <= 0) {
      height = viewHeight;
    }
  }
  if (width <= 0) {
    width = 300;
  }
  if (height <= 0) {
    height = 150;
  }
  return { Width: width, Height: height };
}