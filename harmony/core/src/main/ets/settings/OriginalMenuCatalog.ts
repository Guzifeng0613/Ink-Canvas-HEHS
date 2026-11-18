// @parity src/InkCanvas.Core/Settings/OriginalMenuCatalog.cs
// 上游 WPF 菜单的标签/映射/量程（基线演进同步 2026-11-19：新增 4 种模拟压感笔锋、
// 笔尖标签、inkStyle↔TaperKind 映射、橡皮半径表、HighlighterWidthMaximum）。
// 颜色色板在 controls/PenPaletteCatalog（两侧一致，未变）。
import { TaperKind } from '../ink/StrokeAttributes';

export const PEN_NIBS: string[] = ['圆形', '椭圆 · 右斜 45°'];

export const PEN_STYLES: string[] = [
  '点集笔锋', '速率笔锋', '关闭笔锋',
  '平滑硬笔', '柔和压感', '自然笔锋', '书法笔锋'
];

// 持久化 inkStyle（菜单索引）→ TaperKind；非法值回落 Tail（与 C# switch 默认一致）。
export function toTaperKind(style: number): TaperKind {
  if (style === 1) {
    return TaperKind.Velocity;
  }
  if (style === 2) {
    return TaperKind.None;
  }
  if (style === 3) {
    return TaperKind.SmoothPen;
  }
  if (style === 4) {
    return TaperKind.SoftPen;
  }
  if (style === 5) {
    return TaperKind.Natural;
  }
  if (style === 6) {
    return TaperKind.Calligraphy;
  }
  return TaperKind.Tail;
}

export function toPenStyle(kind: TaperKind): number {
  if (kind === TaperKind.Velocity) {
    return 1;
  }
  if (kind === TaperKind.None) {
    return 2;
  }
  if (kind === TaperKind.SmoothPen) {
    return 3;
  }
  if (kind === TaperKind.SoftPen) {
    return 4;
  }
  if (kind === TaperKind.Natural) {
    return 5;
  }
  if (kind === TaperKind.Calligraphy) {
    return 6;
  }
  return 0;
}

const ERASER_FACTORS: number[] = [0.5, 0.8, 1, 1.25, 1.8];

// 橡皮半径表：45 × 档位系数（size 钳制 0..4）。
export function toEraserRadius(size: number): number {
  const index = Math.min(Math.max(Math.trunc(size), 0), ERASER_FACTORS.length - 1);
  return 45 * ERASER_FACTORS[index];
}

export const ERASER_SIZES: string[] = ['小', '较小', '中（默认）', '较大', '大'];
export const ERASER_MODES: string[] = ['图标切换', '面积擦', '线条擦'];
export const AUTO_DELETE_DAYS: number[] = [1, 3, 5, 7, 15, 30, 60, 100, 365];
export const THEME_OPTIONS: string[] = ['浅色主题', '深色主题', '跟随系统'];

export const PEN_WIDTH_MINIMUM: number = 1;
export const PEN_WIDTH_MAXIMUM: number = 30;
export const HIGHLIGHTER_WIDTH_MAXIMUM: number = 120;
export const PEN_WIDTH_STEP: number = 1;
export const HIGHLIGHTER_ALPHA_MINIMUM: number = 20;
export const HIGHLIGHTER_ALPHA_MAXIMUM: number = 240;
export const HIGHLIGHTER_ALPHA_STEP: number = 20;

export function toStoredPenWidth(menuWidth: number): number {
  return menuWidth / 2;
}

export function toMenuPenWidth(storedWidth: number): number {
  return storedWidth * 2;
}

// 确定性向量入口：inkStyle ↔ TaperKind 往返 + 橡皮半径表。
export function menuCatalogProbe(styles: number[], sizes: number[]): { Kinds: number[]; BackStyles: number[]; Radii: number[] } {
  const kinds: number[] = [];
  const back: number[] = [];
  for (let i = 0; i < styles.length; i++) {
    const kind = toTaperKind(styles[i]);
    kinds.push(kind);
    back.push(toPenStyle(kind));
  }
  const radii: number[] = [];
  for (let i = 0; i < sizes.length; i++) {
    radii.push(toEraserRadius(sizes[i]));
  }
  return { Kinds: kinds, BackStyles: back, Radii: radii };
}