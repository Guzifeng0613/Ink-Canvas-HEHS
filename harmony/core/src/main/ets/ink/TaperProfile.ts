// @parity src/InkCanvas.Core/Ink/TaperProfile.cs
// 因果增量笔锋：只读写 tipIndex 及之前的点；宽度只增不减；抬笔不重算。
import { InkPoint } from './InkPoint';
import { TaperKind } from './StrokeAttributes';

export interface TaperOptionsInput {
  TailPoints?: number;
  HeadPoints?: number;
  TipRatio?: number;
}

class ResolvedOptions {
  tailPoints: number = 10;
  headPoints: number = 4;
  tipRatio: number = 0.2;
}

function resolveOptions(options?: TaperOptionsInput): ResolvedOptions {
  const resolved = new ResolvedOptions();
  if (options !== undefined) {
    if (options.TailPoints !== undefined) {
      resolved.tailPoints = options.TailPoints;
    }
    if (options.HeadPoints !== undefined) {
      resolved.headPoints = options.HeadPoints;
    }
    if (options.TipRatio !== undefined) {
      resolved.tipRatio = options.TipRatio;
    }
  }
  return resolved;
}

function distance(a: InkPoint, b: InkPoint): number {
  const dx = b.X - a.X;
  const dy = b.Y - a.Y;
  return Math.sqrt(dx * dx + dy * dy);
}

function settle(points: InkPoint[], index: number, candidate: number): void {
  if (candidate > points[index].Width) {
    points[index].Width = candidate;
  }
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function applyTail(points: InkPoint[], tipIndex: number, baseWidth: number, options: ResolvedOptions): void {
  const n = Math.max(2, options.tailPoints);
  const start = Math.max(0, tipIndex - n + 1);
  for (let j = start; j <= tipIndex; j++) {
    const d = tipIndex - j;
    const t = d / (n - 1);
    const ratio = options.tipRatio + (1 - options.tipRatio) * t;
    settle(points, j, baseWidth * ratio);
  }

  if (tipIndex < options.headPoints) {
    const t = (tipIndex + 1) / (options.headPoints + 1);
    const ratio = options.tipRatio + (1 - options.tipRatio) * t;
    settle(points, tipIndex, baseWidth * ratio);
  }
}

function applyVelocity(points: InkPoint[], tipIndex: number, baseWidth: number): void {
  let pressure = 0.5;
  if (tipIndex >= 2) {
    const d1 = distance(points[tipIndex - 2], points[tipIndex - 1]);
    const d2 = distance(points[tipIndex - 1], points[tipIndex]);
    const speed = (d1 + d2) / 20.0;
    if (speed >= 0.25) {
      pressure = 0.5 - 0.3 * (Math.min(speed, 1.5) - 0.3) / 1.2;
    } else if (speed < 0.05) {
      pressure = 0.5 + 0.4 * (0.05 - speed) / 0.05;
    }
  }
  const ratio = clamp(pressure / 0.5, 0.15, 1.8);
  settle(points, tipIndex, baseWidth * ratio);
}

export function applyTaperAt(
  points: InkPoint[],
  tipIndex: number,
  baseWidth: number,
  kind: TaperKind,
  options?: TaperOptionsInput
): void {
  if (kind === TaperKind.None) {
    settle(points, tipIndex, baseWidth);
    return;
  }
  if (kind === TaperKind.Velocity) {
    applyVelocity(points, tipIndex, baseWidth);
    return;
  }
  // 非法枚举值与 Tail 一样走默认分支（与 C# switch 默认一致）。
  applyTail(points, tipIndex, baseWidth, resolveOptions(options));
}

// 向量入口：从零宽度开始，按 AddPoint 的时序逐点调用 Apply，返回逐点全宽。
export function effectiveWidths(
  baseWidth: number,
  inputPoints: InkPoint[],
  kind: TaperKind,
  options?: TaperOptionsInput
): number[] {
  const points: InkPoint[] = [];
  for (let i = 0; i < inputPoints.length; i++) {
    const p = inputPoints[i];
    points.push(new InkPoint(p.X, p.Y, p.Pressure, 0, p.TimeMs));
  }
  for (let tip = 0; tip < points.length; tip++) {
    applyTaperAt(points, tip, baseWidth, kind, options);
  }
  const widths: number[] = [];
  for (let i = 0; i < points.length; i++) {
    widths.push(points[i].Width);
  }
  return widths;
}
