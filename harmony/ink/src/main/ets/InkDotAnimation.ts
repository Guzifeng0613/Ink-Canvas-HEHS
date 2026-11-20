// @parity src/InkCanvas.Ink/InkDotAnimation.cs
// 落点 120ms 圆点成形动画：从收拢前的临时形状平滑过渡到最终持久宽度。
import { InkStroke, InkPoint, StrokeAttributes } from '@inkcanvas/core';

export class InkDotAnimation {
  static readonly DURATION_MS: number = 120;
  private readonly initial: InkPoint[] = [];
  private points: InkPoint[] = [];
  private elapsed: number = 0;
  Source: InkStroke;
  Preview: InkStroke;
  SourceVersion: number;

  constructor(source: InkStroke, provisional: InkStroke) {
    this.Source = source;
    this.SourceVersion = source.GeometryVersion;
    for (let i = 0; i < provisional.Count; i++) {
      this.initial.push(provisional.Points[i].clone());
    }
    this.Preview = new InkStroke(source.Attributes.clone());
    this.updateShape();
  }

  advance(milliseconds: number): boolean {
    this.elapsed = Math.min(Math.max(this.elapsed + milliseconds, 0), InkDotAnimation.DURATION_MS);
    this.updateShape();
    return this.elapsed >= InkDotAnimation.DURATION_MS;
  }

  private updateShape(): void {
    const progress = this.elapsed / InkDotAnimation.DURATION_MS;
    const eased = progress * progress * (3 - 2 * progress);
    const target = this.Source.Points[0];
    this.points = [];
    for (let i = 0; i < this.initial.length; i++) {
      const initial = this.initial[i];
      this.points.push(new InkPoint(
        initial.X + (target.X - initial.X) * eased,
        initial.Y + (target.Y - initial.Y) * eased,
        initial.Pressure,
        initial.Width + (target.Width - initial.Width) * eased,
        initial.TimeMs));
    }
    this.Preview.restorePoints(this.points);
  }
}

// 确定性向量入口：源（最终圆点）[X,Y,Width] + 临时（原始笔画）点行 [X,Y,Pressure,Width]
// → 按 steps 逐帧推进，返回每帧 Preview 首点 [X, Y, Width]。
// 两笔均用 restorePoints 构建（宽度显式，不被锥化覆写——真实路径中原始宽度已结算）。
export function dotAnimationProbe(
  sourcePoint: number[],
  provisionalPoints: number[][],
  steps: number[]
): number[][] {
  const attributes = new StrokeAttributes();
  attributes.BaseWidth = sourcePoint[2];
  const source = new InkStroke(attributes);
  source.restorePoints([new InkPoint(sourcePoint[0], sourcePoint[1], 0.5, sourcePoint[2], 0)]);
  source.complete();
  const provisional = new InkStroke(attributes.clone());
  const provisionalInitial: InkPoint[] = [];
  for (let i = 0; i < provisionalPoints.length; i++) {
    const row = provisionalPoints[i];
    provisionalInitial.push(new InkPoint(row[0], row[1], row.length > 2 ? row[2] : 0.5, row[3], 0));
  }
  provisional.restorePoints(provisionalInitial);
  provisional.complete();
  const animation = new InkDotAnimation(source, provisional);
  const frames: number[][] = [];
  for (let i = 0; i < steps.length; i++) {
    animation.advance(steps[i]);
    const p = animation.Preview.Points[0];
    frames.push([p.X, p.Y, p.Width]);
  }
  return frames;
}