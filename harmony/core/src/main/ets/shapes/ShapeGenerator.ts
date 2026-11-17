// @parity src/InkCanvas.Core/Shapes/ShapeGenerator.cs
// 预设图形生成：25 种 ShapeKind，语义 1:1 照抄 C#（上游 MW_ShapeDrawing.cs）。
import { InkColor } from '../ink/InkColor';
import { InkPoint } from '../ink/InkPoint';
import { InkStroke } from '../ink/InkStroke';
import { StrokeAttributes, TaperKind } from '../ink/StrokeAttributes';
import type { StrokeAttributesInput } from '../ink/StrokeAttributes';

export enum ShapeKind {
  Line = 0,
  DashedLine = 1,
  DotLine = 2,
  Arrow = 3,
  ParallelLine = 4,

  CoordinateBoth = 5,
  CoordinateXOneWay = 6,
  CoordinateYOneWay = 7,
  CoordinateFirstQuadrant = 8,
  Coordinate3D = 9,

  Rectangle = 10,
  RectangleFromCenter = 11,
  Ellipse = 12,
  Circle = 13,
  EllipseFromCenter = 14,
  EllipseWithFoci = 15,
  DashedCircle = 16,

  Hyperbola = 17,
  HyperbolaWithFoci = 18,
  ParabolaRight = 19,
  ParabolaWithFoci = 20,
  ParabolaUp = 21,

  Cylinder = 22,
  Cone = 23,
  Cuboid = 24
}

export interface PointLike {
  X: number;
  Y: number;
}

export interface StrokePointSummary {
  X: number;
  Y: number;
  Pressure: number;
  Width: number;
  TimeMs: number;
}

export interface StrokeSummary {
  Points: StrokePointSummary[];
  ColorPacked: number;
  BaseWidth: number;
  IsHighlighter: boolean;
  HighlighterAlpha: number;
  TaperKind: number;
  IsRecognizedShape: boolean;
}

export interface SnapResult {
  end: PointLike;
  sin: number;
  cos: number;
}

/** 整椭圆采样密度（与 C# FullEllipseSegments 一致）。 */
const FULL_ELLIPSE_SEGMENTS: number = 72;

/** 虚线：dash 5px，周期 5*2.76 = 13.8px。 */
export const DashLength: number = 5;
export const DashPeriod: number = 5 * 2.76;

/** 点线：每 3*2.76 = 8.28px 一个压力 0.8 的单点。 */
export const DotPeriod: number = 3 * 2.76;
export const DotPressure: number = 0.8;

/** 椭圆角向虚线：宽度 0.05π，周期 0.05π*1.66 = 0.083π。 */
export const EllipseDashStep: number = 0.05;
export const EllipseDashPeriod: number = 0.05 * 1.66;

/** 柱/锥底面椭圆短轴 = 宽度 / 2.646。 */
export const SolidEllipseRatio: number = 2.646;

function toAttributes(input: StrokeAttributesInput): StrokeAttributes {
  const attributes = new StrokeAttributes();
  attributes.Color = InkColor.fromPackedArgb(input.ColorPacked);
  attributes.BaseWidth = input.BaseWidth;
  attributes.IsHighlighter = input.IsHighlighter === true;
  attributes.HighlighterAlpha = input.HighlighterAlpha;
  attributes.TaperKind = input.TaperKind as TaperKind;
  return attributes;
}

function distance(a: PointLike, b: PointLike): number {
  const dx = b.X - a.X;
  const dy = b.Y - a.Y;
  return Math.sqrt(dx * dx + dy * dy);
}

function summarizeStroke(stroke: InkStroke): StrokeSummary {
  const points: StrokePointSummary[] = [];
  const source = stroke.Points;
  for (let i = 0; i < source.length; i++) {
    const p = source[i];
    points.push({ X: p.X, Y: p.Y, Pressure: p.Pressure, Width: p.Width, TimeMs: p.TimeMs });
  }
  return {
    Points: points,
    ColorPacked: stroke.Attributes.Color.packedArgb(),
    BaseWidth: stroke.Attributes.BaseWidth,
    IsHighlighter: stroke.Attributes.IsHighlighter,
    HighlighterAlpha: stroke.Attributes.HighlighterAlpha,
    TaperKind: stroke.Attributes.TaperKind,
    IsRecognizedShape: stroke.IsRecognizedShape
  };
}

function summarize(strokes: InkStroke[]): StrokeSummary[] {
  const result: StrokeSummary[] = [];
  for (let i = 0; i < strokes.length; i++) {
    result.push(summarizeStroke(strokes[i]));
  }
  return result;
}

function appendAll(target: InkStroke[], source: InkStroke[]): void {
  for (let i = 0; i < source.length; i++) {
    target.push(source[i]);
  }
}

// ---- primitives ---------------------------------------------------------

function strokeInk(attrs: StrokeAttributes, pts: PointLike[]): InkStroke {
  const stroke = new InkStroke(attrs.clone());
  let t = 0;
  for (let i = 0; i < pts.length; i++) {
    stroke.addPoint(pts[i].X, pts[i].Y, 0.5, t);
    t += 8;
  }
  return stroke;
}

function dotInk(attrs: StrokeAttributes, center: PointLike, pressure: number): InkStroke {
  const stroke = new InkStroke(attrs.clone());
  const width = attrs.BaseWidth * (pressure / 0.5);
  stroke.restorePoints([new InkPoint(center.X, center.Y, pressure, width, 0)]);
  return stroke;
}

function arrowWithHeadInk(attrs: StrokeAttributes, start: PointLike, end: PointLike, w: number, h: number): InkStroke {
  const dirX = end.X - start.X;
  const dirY = end.Y - start.Y;
  if (dirX * dirX + dirY * dirY < 1e-4) {
    return strokeInk(attrs, [start, end]);
  }
  const theta = Math.atan2(start.Y - end.Y, start.X - end.X);
  const sint = Math.sin(theta);
  const cost = Math.cos(theta);
  const barb1: PointLike = {
    X: end.X + (w * cost - h * sint),
    Y: end.Y + (w * sint + h * cost)
  };
  const barb2: PointLike = {
    X: end.X + (w * cost + h * sint),
    Y: end.Y - (h * cost - w * sint)
  };
  return strokeInk(attrs, [start, end, barb1, end, barb2]);
}

function dashedInk(attrs: StrokeAttributes, start: PointLike, end: PointLike, dash: number, gap: number): InkStroke[] {
  const result: InkStroke[] = [];
  const deltaX = end.X - start.X;
  const deltaY = end.Y - start.Y;
  const len = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
  if (len < 0.5) {
    return result;
  }
  const dirX = deltaX / len;
  const dirY = deltaY / len;
  let pos = 0;
  while (pos < len) {
    const stop = Math.min(pos + dash, len);
    result.push(strokeInk(attrs, [
      { X: start.X + dirX * pos, Y: start.Y + dirY * pos },
      { X: start.X + dirX * stop, Y: start.Y + dirY * stop }
    ]));
    pos = stop + gap;
  }
  return result;
}

function dotsInk(attrs: StrokeAttributes, start: PointLike, end: PointLike): InkStroke[] {
  const result: InkStroke[] = [];
  const deltaX = end.X - start.X;
  const deltaY = end.Y - start.Y;
  const len = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
  if (len <= 0) {
    return result;
  }
  const dirX = deltaX / len;
  const dirY = deltaY / len;
  for (let i = 0; i < len; i += DotPeriod) {
    result.push(dotInk(attrs, { X: start.X + dirX * i, Y: start.Y + dirY * i }, DotPressure));
  }
  return result;
}

function parabolaBranchesInk(attrs: StrokeAttributes, vertex: PointLike, a: number, extent: number): InkStroke[] {
  const down: PointLike[] = [];
  const up: PointLike[] = [];
  for (let i = 0; i <= extent; i += 0.5) {
    down.push({ X: vertex.X - a * i * i, Y: vertex.Y + i });
    up.push({ X: vertex.X - a * i * i, Y: vertex.Y - i });
  }
  return [strokeInk(attrs, down), strokeInk(attrs, up)];
}

export function ellipsePoints(center: PointLike, rx: number, ry: number, from: number, to: number, segments: number): PointLike[] {
  const pts: PointLike[] = [];
  for (let i = 0; i <= segments; i++) {
    const angle = from + (to - from) * i / segments;
    pts.push({ X: center.X + Math.cos(angle) * rx, Y: center.Y + Math.sin(angle) * ry });
  }
  return pts;
}

function ellipseStrokeInk(attrs: StrokeAttributes, center: PointLike, rx: number, ry: number): InkStroke {
  return strokeInk(attrs, ellipsePoints(center, Math.max(rx, 0.5), Math.max(ry, 0.5), 0, 2 * Math.PI, FULL_ELLIPSE_SEGMENTS));
}

function segmentsFor(from: number, to: number): number {
  return Math.max(2, Math.ceil(FULL_ELLIPSE_SEGMENTS * (to - from) / (2 * Math.PI)));
}

function dashedEllipseArcsInk(
  attrs: StrokeAttributes,
  center: PointLike,
  rx: number,
  ry: number,
  frontHalf: boolean,
  backHalf: boolean
): InkStroke[] {
  const result: InkStroke[] = [];
  const safeRx = Math.max(rx, 0.5);
  const safeRy = Math.max(ry, 0.5);
  // C# 以 float 累加角向参数；用 fround 复刻 32 位步进，避免末段累积误差超过 1e-4。
  const step = Math.fround(EllipseDashStep);
  const period = Math.fround(Math.fround(0.05) * Math.fround(1.66));
  const piF = Math.fround(Math.PI);
  const unitF = Math.fround(0.01);

  const addHalf = (from: number, to: number): void => {
    for (let i = from; i < to; i = Math.fround(i + period)) {
      const a0 = Math.fround(piF * i);
      const a1 = Math.fround(piF * Math.fround(i + step));
      const segments = Math.max(2, Math.ceil(Math.fround(Math.fround(a1 - a0) / unitF)));
      result.push(strokeInk(attrs, ellipsePoints(center, safeRx, safeRy, a0, a1, segments)));
    }
  };

  if (frontHalf) {
    addHalf(0, 1);
  }
  if (backHalf) {
    addHalf(1, 2);
  }
  return result;
}

function snapParallel(start: PointLike, end: PointLike): SnapResult {
  let snapped: PointLike = { X: end.X, Y: end.Y };
  const d = distance(start, end);
  if (d <= 0) {
    return { end: snapped, sin: 0, cos: 0 };
  }
  let sin = (start.Y - end.Y) / d;
  let cos = (end.X - start.X) / d;
  const tan = Math.abs(sin / cos);

  if (tan < 1 / 12) {
    sin = 0;
    cos = 1;
    snapped = { X: end.X, Y: start.Y };
  }
  if (tan > 0.52 && tan < 0.63) {
    sin = Math.sign(sin) * 0.5;
    cos = Math.sign(cos) * 0.866;
    snapped = { X: start.X + d * cos, Y: start.Y - d * sin };
  }
  if (tan > 0.92 && tan < 1.08) {
    sin = Math.sign(sin) * 0.707;
    cos = Math.sign(cos) * 0.707;
    snapped = { X: start.X + d * cos, Y: start.Y - d * sin };
  }
  if (tan > 1.63 && tan < 1.95) {
    sin = Math.sign(sin) * 0.866;
    cos = Math.sign(cos) * 0.5;
    snapped = { X: start.X + d * cos, Y: start.Y - d * sin };
  }
  if (Math.abs(cos / sin) < 1 / 12) {
    snapped = { X: start.X, Y: snapped.Y };
    sin = 1;
    cos = 0;
  }
  return { end: snapped, sin: sin, cos: cos };
}

export function snapParallelEnd(startX: number, startY: number, endX: number, endY: number): PointLike {
  return snapParallel({ X: startX, Y: startY }, { X: endX, Y: endY }).end;
}

export function parallelLines(attrs: StrokeAttributesInput, startX: number, startY: number, endX: number, endY: number): StrokeSummary[] {
  const a = toAttributes(attrs);
  const start: PointLike = { X: startX, Y: startY };
  const end: PointLike = { X: endX, Y: endY };
  const result: InkStroke[] = [];
  const snapped = snapParallel(start, end);
  if (distance(start, snapped.end) <= 0) {
    return summarize(result);
  }
  const nX = snapped.sin;
  const nY = snapped.cos;
  const x = 25;
  const factors: number[] = [-3, -1, 1, 3];
  for (let i = 0; i < factors.length; i++) {
    const offset = x * factors[i];
    result.push(strokeInk(a, [
      { X: start.X + nX * offset, Y: start.Y + nY * offset },
      { X: snapped.end.X + nX * offset, Y: snapped.end.Y + nY * offset }
    ]));
  }
  return summarize(result);
}

// ---- public primitives (returning InkStroke, like C#) -------------------

export function stroke(attrs: StrokeAttributesInput, pts: PointLike[]): InkStroke {
  return strokeInk(toAttributes(attrs), pts);
}

export function dot(attrs: StrokeAttributesInput, center: PointLike, pressure: number): InkStroke {
  return dotInk(toAttributes(attrs), center, pressure);
}

export function arrow(attrs: StrokeAttributesInput, start: PointLike, end: PointLike): InkStroke {
  return arrowWithHeadInk(toAttributes(attrs), start, end, 30, 10);
}

export function axisArrow(attrs: StrokeAttributesInput, start: PointLike, end: PointLike): InkStroke {
  return arrowWithHeadInk(toAttributes(attrs), start, end, 20, 7);
}

export function dashedLine(
  attrs: StrokeAttributesInput,
  startX: number,
  startY: number,
  endX: number,
  endY: number,
  dash: number,
  gap: number
): StrokeSummary[] {
  return summarize(dashedInk(
    toAttributes(attrs),
    { X: startX, Y: startY },
    { X: endX, Y: endY },
    dash,
    gap
  ));
}

export function dots(attrs: StrokeAttributesInput, startX: number, startY: number, endX: number, endY: number): StrokeSummary[] {
  return summarize(dotsInk(toAttributes(attrs), { X: startX, Y: startY }, { X: endX, Y: endY }));
}

export function dottedLine(attrs: StrokeAttributesInput, startX: number, startY: number, endX: number, endY: number): StrokeSummary[] {
  return dots(attrs, startX, startY, endX, endY);
}

export function ellipseStroke(attrs: StrokeAttributesInput, centerX: number, centerY: number, rx: number, ry: number): StrokeSummary {
  return summarizeStroke(ellipseStrokeInk(toAttributes(attrs), { X: centerX, Y: centerY }, rx, ry));
}

export function dashedEllipseArcs(
  attrs: StrokeAttributesInput,
  centerX: number,
  centerY: number,
  rx: number,
  ry: number,
  frontHalf: boolean,
  backHalf: boolean
): StrokeSummary[] {
  return summarize(dashedEllipseArcsInk(toAttributes(attrs), { X: centerX, Y: centerY }, rx, ry, frontHalf, backHalf));
}

// ---- total entry --------------------------------------------------------

export function generate(
  kind: number,
  startX: number,
  startY: number,
  endX: number,
  endY: number,
  attrs: StrokeAttributesInput
): StrokeSummary[] {
  const a = toAttributes(attrs);
  const start: PointLike = { X: startX, Y: startY };
  const end: PointLike = { X: endX, Y: endY };
  const result: InkStroke[] = [];
  const w = end.X - start.X;
  const h = end.Y - start.Y;
  const center: PointLike = { X: (start.X + end.X) * 0.5, Y: (start.Y + end.Y) * 0.5 };
  const rx = Math.abs(w) * 0.5;
  const ry = Math.abs(h) * 0.5;

  switch (kind) {
    case ShapeKind.Line:
      result.push(strokeInk(a, [start, end]));
      break;

    case ShapeKind.DashedLine:
      appendAll(result, dashedInk(a, start, end, DashLength, DashPeriod - DashLength));
      break;

    case ShapeKind.DotLine:
      appendAll(result, dotsInk(a, start, end));
      break;

    case ShapeKind.Arrow:
      result.push(arrowWithHeadInk(a, start, end, 30, 10));
      break;

    case ShapeKind.ParallelLine:
      appendAll(result, parallelLinesInk(a, start, end));
      break;

    case ShapeKind.CoordinateBoth:
      result.push(arrowWithHeadInk(a, { X: 2 * start.X - (end.X - 20), Y: start.Y }, { X: end.X, Y: start.Y }, 20, 7));
      result.push(arrowWithHeadInk(a, { X: start.X, Y: 2 * start.Y - (end.Y + 20) }, { X: start.X, Y: end.Y }, 20, 7));
      break;

    case ShapeKind.CoordinateXOneWay:
      if (Math.abs(w) < 0.01) {
        break;
      }
      result.push(arrowWithHeadInk(a, { X: start.X + Math.sign(-w) * 25, Y: start.Y }, { X: end.X, Y: start.Y }, 20, 7));
      result.push(arrowWithHeadInk(a, { X: start.X, Y: 2 * start.Y - (end.Y + 20) }, { X: start.X, Y: end.Y }, 20, 7));
      break;

    case ShapeKind.CoordinateYOneWay:
      if (Math.abs(h) < 0.01) {
        break;
      }
      result.push(arrowWithHeadInk(a, { X: 2 * start.X - (end.X - 20), Y: start.Y }, { X: end.X, Y: start.Y }, 20, 7));
      result.push(arrowWithHeadInk(a, { X: start.X, Y: start.Y + Math.sign(-h) * 25 }, { X: start.X, Y: end.Y }, 20, 7));
      break;

    case ShapeKind.CoordinateFirstQuadrant:
      if (Math.abs(w) < 0.01 || Math.abs(h) < 0.01) {
        break;
      }
      result.push(arrowWithHeadInk(a, { X: start.X + Math.sign(-w) * 25, Y: start.Y }, { X: end.X, Y: start.Y }, 20, 7));
      result.push(arrowWithHeadInk(a, { X: start.X, Y: start.Y + Math.sign(-h) * 25 }, { X: start.X, Y: end.Y }, 20, 7));
      break;

    case ShapeKind.Coordinate3D: {
      result.push(arrowWithHeadInk(a, start, { X: start.X + Math.abs(w), Y: start.Y }, 20, 7));
      result.push(arrowWithHeadInk(a, start, { X: start.X, Y: start.Y - Math.abs(h) }, 20, 7));
      const d = (Math.abs(w) + Math.abs(h)) * 0.5;
      result.push(arrowWithHeadInk(a, start, { X: start.X - d / 1.76, Y: start.Y + d / 1.76 }, 20, 7));
      break;
    }

    case ShapeKind.Rectangle:
      result.push(strokeInk(a, [
        start,
        { X: start.X, Y: end.Y },
        end,
        { X: end.X, Y: start.Y },
        start
      ]));
      break;

    case ShapeKind.RectangleFromCenter:
      result.push(strokeInk(a, [
        { X: 2 * start.X - end.X, Y: 2 * start.Y - end.Y },
        { X: 2 * start.X - end.X, Y: end.Y },
        end,
        { X: end.X, Y: 2 * start.Y - end.Y },
        { X: 2 * start.X - end.X, Y: 2 * start.Y - end.Y }
      ]));
      break;

    case ShapeKind.Ellipse:
      result.push(ellipseStrokeInk(a, center, rx, ry));
      break;

    case ShapeKind.Circle: {
      const r = distance(start, end);
      result.push(ellipseStrokeInk(a, start, r, r));
      break;
    }

    case ShapeKind.EllipseFromCenter:
      result.push(ellipseStrokeInk(a, start, Math.abs(w), Math.abs(h)));
      break;

    case ShapeKind.EllipseWithFoci: {
      const aa = Math.abs(w);
      const bb = Math.abs(h);
      result.push(ellipseStrokeInk(a, start, aa, bb));
      const c = Math.sqrt(Math.abs(aa * aa - bb * bb));
      if (aa > bb) {
        result.push(dotInk(a, { X: start.X + c, Y: start.Y }, 1));
        result.push(dotInk(a, { X: start.X - c, Y: start.Y }, 1));
      } else if (aa < bb) {
        result.push(dotInk(a, { X: start.X, Y: start.Y - c }, 1));
        result.push(dotInk(a, { X: start.X, Y: start.Y + c }, 1));
      }
      break;
    }

    case ShapeKind.DashedCircle: {
      const r = distance(start, end);
      appendAll(result, dashedEllipseArcsInk(a, start, r, r, true, true));
      break;
    }

    case ShapeKind.Hyperbola:
    case ShapeKind.HyperbolaWithFoci:
      if (Math.abs(w) < 0.01 || Math.abs(h) < 0.01) {
        break;
      }
      appendAll(result, dashedInk(a, { X: 2 * start.X - end.X, Y: 2 * start.Y - end.Y }, end, DashLength, DashPeriod - DashLength));
      appendAll(result, dashedInk(a, { X: 2 * start.X - end.X, Y: end.Y }, { X: end.X, Y: 2 * start.Y - end.Y }, DashLength, DashPeriod - DashLength));
      break;

    case ShapeKind.ParabolaUp: {
      if (Math.abs(w) < 0.01 || Math.abs(h) < 0.01) {
        break;
      }
      const pa = (start.Y - end.Y) / (w * w);
      const right: PointLike[] = [];
      const left: PointLike[] = [];
      for (let i = 0; i <= Math.abs(w); i += 0.5) {
        right.push({ X: start.X + i, Y: start.Y - pa * i * i });
        left.push({ X: start.X - i, Y: start.Y - pa * i * i });
      }
      result.push(strokeInk(a, right));
      result.push(strokeInk(a, left));
      break;
    }

    case ShapeKind.ParabolaRight: {
      if (Math.abs(w) < 0.01 || Math.abs(h) < 0.01) {
        break;
      }
      const pa = (start.X - end.X) / (h * h);
      appendAll(result, parabolaBranchesInk(a, start, pa, Math.abs(h)));
      break;
    }

    case ShapeKind.ParabolaWithFoci: {
      if (Math.abs(w) < 0.01 || Math.abs(h) < 0.01) {
        break;
      }
      const p = h * h / (2 * (start.X - end.X));
      const pa = 0.5 / p;
      appendAll(result, parabolaBranchesInk(a, start, pa, Math.abs(h)));
      result.push(dotInk(a, { X: start.X - p / 2, Y: start.Y }, 1));
      break;
    }

    case ShapeKind.Cylinder: {
      const top: PointLike = start.Y > end.Y ? { X: start.X, Y: end.Y } : start;
      const bottom: PointLike = start.Y > end.Y ? { X: end.X, Y: start.Y } : end;
      const halfW = Math.abs(top.X - bottom.X) * 0.5;
      const halfH = halfW / SolidEllipseRatio;
      const midX = (top.X + bottom.X) * 0.5;
      result.push(ellipseStrokeInk(a, { X: midX, Y: top.Y }, halfW, halfH));
      result.push(strokeInk(a, ellipsePoints({ X: midX, Y: bottom.Y }, halfW, halfH, 0, Math.PI, segmentsFor(0, Math.PI))));
      appendAll(result, dashedEllipseArcsInk(a, { X: midX, Y: bottom.Y }, halfW, halfH, false, true));
      result.push(strokeInk(a, [{ X: top.X, Y: top.Y }, { X: top.X, Y: bottom.Y }]));
      result.push(strokeInk(a, [{ X: bottom.X, Y: top.Y }, { X: bottom.X, Y: bottom.Y }]));
      break;
    }

    case ShapeKind.Cone: {
      const top: PointLike = start.Y > end.Y ? { X: start.X, Y: end.Y } : start;
      const bottom: PointLike = start.Y > end.Y ? { X: end.X, Y: start.Y } : end;
      const halfW = Math.abs(top.X - bottom.X) * 0.5;
      const halfH = halfW / SolidEllipseRatio;
      const midX = (top.X + bottom.X) * 0.5;
      const apex: PointLike = { X: midX, Y: top.Y };
      result.push(strokeInk(a, ellipsePoints({ X: midX, Y: bottom.Y }, halfW, halfH, 0, Math.PI, segmentsFor(0, Math.PI))));
      appendAll(result, dashedEllipseArcsInk(a, { X: midX, Y: bottom.Y }, halfW, halfH, false, true));
      result.push(strokeInk(a, [apex, { X: top.X, Y: bottom.Y }]));
      result.push(strokeInk(a, [apex, { X: bottom.X, Y: bottom.Y }]));
      break;
    }

    case ShapeKind.Cuboid:
      result.push(strokeInk(a, [start, { X: start.X, Y: end.Y }]));
      result.push(strokeInk(a, [{ X: start.X, Y: end.Y }, end]));
      result.push(strokeInk(a, [end, { X: end.X, Y: start.Y }]));
      result.push(strokeInk(a, [start, { X: end.X, Y: start.Y }]));
      break;
  }

  for (let i = 0; i < result.length; i++) {
    result[i].IsRecognizedShape = true;
    result[i].complete();
  }
  return summarize(result);
}

// ---- two-step second passes --------------------------------------------

export function generateCuboidDepth(
  frontTopX: number,
  frontTopY: number,
  frontBottomX: number,
  frontBottomY: number,
  depth: number,
  attrs: StrokeAttributesInput
): StrokeSummary[] {
  const a = toAttributes(attrs);
  const result: InkStroke[] = [];
  const frontTop: PointLike = { X: frontTopX, Y: frontTopY };
  const frontBottom: PointLike = { X: frontBottomX, Y: frontBottomY };
  const d = Math.abs(depth);
  const ft: PointLike = { X: frontTop.X + d, Y: frontTop.Y - d };
  const fb: PointLike = { X: frontBottom.X + d, Y: frontBottom.Y - d };
  const ftr: PointLike = { X: frontBottom.X, Y: frontTop.Y };

  result.push(strokeInk(a, [ft, { X: fb.X, Y: ft.Y }]));
  appendAll(result, dashedInk(a, { X: ft.X, Y: fb.Y }, fb, DashLength, DashPeriod - DashLength));
  result.push(strokeInk(a, [frontTop, ft]));
  result.push(strokeInk(a, [ftr, { X: ftr.X + d, Y: ftr.Y - d }]));
  appendAll(result, dashedInk(a, { X: frontTop.X, Y: frontBottom.Y }, { X: ft.X, Y: fb.Y }, DashLength, DashPeriod - DashLength));
  result.push(strokeInk(a, [frontBottom, fb]));
  appendAll(result, dashedInk(a, ft, { X: ft.X, Y: fb.Y }, DashLength, DashPeriod - DashLength));
  result.push(strokeInk(a, [{ X: fb.X, Y: ft.Y }, fb]));

  for (let i = 0; i < result.length; i++) {
    result[i].IsRecognizedShape = true;
    result[i].complete();
  }
  return summarize(result);
}

export function generateHyperbolaBranches(
  centerX: number,
  centerY: number,
  k: number,
  endX: number,
  endY: number,
  withFoci: boolean,
  attrs: StrokeAttributesInput
): StrokeSummary[] {
  const a = toAttributes(attrs);
  const result: InkStroke[] = [];
  const center: PointLike = { X: centerX, Y: centerY };
  const end: PointLike = { X: endX, Y: endY };
  const dx = end.X - center.X;
  const dy = end.Y - center.Y;
  if (Math.abs(dx) < 0.01 || Math.abs(dy) < 0.01 || k <= 0) {
    return summarize(result);
  }

  const fociOnX = Math.abs(dy / dx) < k;
  let aa = 0;
  let bb = 0;
  const arcs: PointLike[][] = [[], [], [], []];

  if (fociOnX) {
    aa = Math.sqrt(Math.abs(dx * dx - dy * dy / (k * k)));
    bb = aa * k;
    const steps = Math.max(0, Math.floor((Math.abs(dx) - aa) / 0.5));
    for (let j = 0; j <= steps; j++) {
      const i = aa + j * 0.5;
      const rY = Math.sqrt(Math.abs(k * k * i * i - bb * bb));
      arcs[0].push({ X: center.X + i, Y: center.Y - rY });
      arcs[1].push({ X: center.X + i, Y: center.Y + rY });
      arcs[2].push({ X: center.X - i, Y: center.Y - rY });
      arcs[3].push({ X: center.X - i, Y: center.Y + rY });
    }
  } else {
    aa = Math.sqrt(Math.abs(dy * dy - dx * dx * k * k));
    bb = aa / k;
    const steps = Math.max(0, Math.floor((Math.abs(dy) - aa) / 0.5));
    for (let j = 0; j <= steps; j++) {
      const i = aa + j * 0.5;
      const rX = Math.sqrt(Math.abs(i * i / k / k - bb * bb));
      arcs[0].push({ X: center.X - rX, Y: center.Y + i });
      arcs[1].push({ X: center.X + rX, Y: center.Y + i });
      arcs[2].push({ X: center.X - rX, Y: center.Y - i });
      arcs[3].push({ X: center.X + rX, Y: center.Y - i });
    }
  }

  for (let i = 0; i < arcs.length; i++) {
    if (arcs[i].length > 0) {
      result.push(strokeInk(a, arcs[i]));
    }
  }

  if (withFoci) {
    const c = Math.sqrt(aa * aa + bb * bb);
    if (fociOnX) {
      result.push(dotInk(a, { X: center.X + c, Y: center.Y }, 1));
      result.push(dotInk(a, { X: center.X - c, Y: center.Y }, 1));
    } else {
      result.push(dotInk(a, { X: center.X, Y: center.Y + c }, 1));
      result.push(dotInk(a, { X: center.X, Y: center.Y - c }, 1));
    }
  }

  for (let i = 0; i < result.length; i++) {
    result[i].IsRecognizedShape = true;
    result[i].complete();
  }
  return summarize(result);
}

function parallelLinesInk(attrs: StrokeAttributes, start: PointLike, end: PointLike): InkStroke[] {
  const result: InkStroke[] = [];
  const snapped = snapParallel(start, end);
  if (distance(start, snapped.end) <= 0) {
    return result;
  }
  const nX = snapped.sin;
  const nY = snapped.cos;
  const x = 25;
  const factors: number[] = [-3, -1, 1, 3];
  for (let i = 0; i < factors.length; i++) {
    const offset = x * factors[i];
    result.push(strokeInk(attrs, [
      { X: start.X + nX * offset, Y: start.Y + nY * offset },
      { X: snapped.end.X + nX * offset, Y: snapped.end.Y + nY * offset }
    ]));
  }
  return result;
}
