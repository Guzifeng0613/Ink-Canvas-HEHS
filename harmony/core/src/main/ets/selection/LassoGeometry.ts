// @parity src/InkCanvas.Core/Selection/LassoGeometry.cs
// 闭合手绘多边形（视图 DIP 坐标），自交用 even/odd 填充；点/线段/笔画/元素框相交判定。
// 有效性：包围盒 ≥4×4 且三角形面积和 ≥16（排除单击与直线横扫，绝对面积同时接受 8 字环）。
import { Rect, InkStroke, SelectionFrameState } from '../ink/InkStroke';
import { StrokeAttributes } from '../ink/StrokeAttributes';
import { Matrix3x2, multiplyMatrix, transformPointByMatrix, invertMatrix, identityMatrix } from '../math/Matrix3x2';

export interface LassoPoint {
  X: number;
  Y: number;
}

function onSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number): boolean {
  const cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
  if (Math.abs(cross) > 1e-9) {
    return false;
  }
  return px >= Math.min(ax, bx) - 1e-9 && px <= Math.max(ax, bx) + 1e-9
    && py >= Math.min(ay, by) - 1e-9 && py <= Math.max(ay, by) + 1e-9;
}

function segmentsIntersect(
  ax: number, ay: number, bx: number, by: number,
  cx: number, cy: number, dx: number, dy: number
): boolean {
  const d1 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
  const d2 = (bx - ax) * (dy - ay) - (by - ay) * (dx - ax);
  const d3 = (dx - cx) * (ay - cy) - (dy - cy) * (ax - cx);
  const d4 = (dx - cx) * (by - cy) - (dy - cy) * (bx - cx);
  return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
    && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
}

export class LassoGeometry {
  private readonly points: LassoPoint[];
  Bounds: Rect = Rect.empty();
  IsValid: boolean = false;

  constructor(points: LassoPoint[]) {
    const finite: LassoPoint[] = [];
    for (let i = 0; i < points.length; i++) {
      if (Number.isFinite(points[i].X) && Number.isFinite(points[i].Y)) {
        finite.push(points[i]);
      }
    }
    this.points = finite;
    if (this.points.length < 3) {
      return;
    }
    let left = Number.MAX_VALUE;
    let top = Number.MAX_VALUE;
    let right = -Number.MAX_VALUE;
    let bottom = -Number.MAX_VALUE;
    let area = 0;
    const first = this.points[0];
    for (let i = 0; i < this.points.length; i++) {
      const p = this.points[i];
      left = Math.min(left, p.X);
      top = Math.min(top, p.Y);
      right = Math.max(right, p.X);
      bottom = Math.max(bottom, p.Y);
      if (i > 0) {
        const next = this.points[(i + 1) % this.points.length];
        const cross = (p.X - first.X) * (next.Y - first.Y) - (p.Y - first.Y) * (next.X - first.X);
        area += Math.abs(cross);
      }
    }
    this.Bounds = new Rect(left, top, right, bottom);
    this.IsValid = this.Bounds.width() >= 4 && this.Bounds.height() >= 4 && area >= 16;
  }

  Contains(px: number, py: number): boolean {
    if (!this.IsValid || !this.Bounds.contains(px, py)) {
      return false;
    }
    let inside = false;
    for (let i = 0, j = this.points.length - 1; i < this.points.length; j = i++) {
      const a = this.points[j];
      const b = this.points[i];
      if (onSegment(px, py, a.X, a.Y, b.X, b.Y)) {
        return true;
      }
      if ((a.Y > py) !== (b.Y > py)
        && px < a.X + (py - a.Y) * (b.X - a.X) / (b.Y - a.Y)) {
        inside = !inside;
      }
    }
    return inside;
  }

  IntersectsSegment(ax: number, ay: number, bx: number, by: number): boolean {
    if (!this.IsValid) {
      return false;
    }
    const segmentBounds = new Rect(Math.min(ax, bx), Math.min(ay, by), Math.max(ax, bx), Math.max(ay, by));
    if (!this.Bounds.intersects(segmentBounds)) {
      return false;
    }
    if (this.Contains(ax, ay) || this.Contains(bx, by)) {
      return true;
    }
    for (let i = 0; i < this.points.length; i++) {
      const a = this.points[i];
      const b = this.points[(i + 1) % this.points.length];
      if (segmentsIntersect(ax, ay, bx, by, a.X, a.Y, b.X, b.Y)) {
        return true;
      }
    }
    return false;
  }

  IntersectsStroke(stroke: InkStroke, worldToView: Matrix3x2): boolean {
    if (!this.IsValid || stroke.Count === 0) {
      return false;
    }
    const strokeBounds = this.transformBounds(stroke.bounds(), worldToView);
    if (!this.Bounds.intersects(strokeBounds)) {
      return false;
    }
    const previous = transformPointByMatrix(worldToView, stroke.Points[0].X, stroke.Points[0].Y);
    if (this.Contains(previous.X, previous.Y)) {
      return true;
    }
    for (let i = 1; i < stroke.Count; i++) {
      const current = transformPointByMatrix(worldToView, stroke.Points[i].X, stroke.Points[i].Y);
      if (this.IntersectsSegment(previous.X, previous.Y, current.X, current.Y)) {
        return true;
      }
      previous.X = current.X;
      previous.Y = current.Y;
    }
    return false;
  }

  IntersectsFrame(frame: SelectionFrameState, worldToView: Matrix3x2): boolean {
    if (!this.IsValid || frame.Bounds.isEmpty()) {
      return false;
    }
    const combined = multiplyMatrix(frame.Transform, worldToView);
    const bounds = frame.Bounds;
    const corners: LassoPoint[] = [
      { X: bounds.Left, Y: bounds.Top },
      { X: bounds.Right, Y: bounds.Top },
      { X: bounds.Right, Y: bounds.Bottom },
      { X: bounds.Left, Y: bounds.Bottom }
    ];
    for (let i = 0; i < corners.length; i++) {
      const a = transformPointByMatrix(combined, corners[i].X, corners[i].Y);
      const b = transformPointByMatrix(combined, corners[(i + 1) % corners.length].X, corners[(i + 1) % corners.length].Y);
      if (this.IntersectsSegment(a.X, a.Y, b.X, b.Y)) {
        return true;
      }
    }
    // 套索可整个落在实心图片/视频矩形内部。
    if (this.points.length === 0) {
      return false;
    }
    const inverse = invertMatrix(combined);
    const local = transformPointByMatrix(inverse, this.points[0].X, this.points[0].Y);
    return bounds.contains(local.X, local.Y);
  }

  private transformBounds(bounds: Rect, transform: Matrix3x2): Rect {
    const corners: LassoPoint[] = [
      { X: bounds.Left, Y: bounds.Top },
      { X: bounds.Right, Y: bounds.Top },
      { X: bounds.Right, Y: bounds.Bottom },
      { X: bounds.Left, Y: bounds.Bottom }
    ];
    let left = Number.MAX_VALUE;
    let top = Number.MAX_VALUE;
    let right = -Number.MAX_VALUE;
    let bottom = -Number.MAX_VALUE;
    for (let i = 0; i < corners.length; i++) {
      const p = transformPointByMatrix(transform, corners[i].X, corners[i].Y);
      left = Math.min(left, p.X);
      top = Math.min(top, p.Y);
      right = Math.max(right, p.X);
      bottom = Math.max(bottom, p.Y);
    }
    return new Rect(left, top, right, bottom);
  }
}

// ---- 确定性向量入口（视图=单位阵） --------------------------------------------

export function lassoValidProbe(points: number[][]): { Valid: boolean; Bounds: number[] } {
  const lasso = new LassoGeometry(toPoints(points));
  return { Valid: lasso.IsValid, Bounds: [lasso.Bounds.Left, lasso.Bounds.Top, lasso.Bounds.Right, lasso.Bounds.Bottom] };
}

export function lassoContainsProbe(points: number[][], tests: number[][]): boolean[] {
  const lasso = new LassoGeometry(toPoints(points));
  const results: boolean[] = [];
  for (let i = 0; i < tests.length; i++) {
    results.push(lasso.Contains(tests[i][0], tests[i][1]));
  }
  return results;
}

export function lassoSegmentProbe(points: number[][], segments: number[][]): boolean[] {
  const lasso = new LassoGeometry(toPoints(points));
  const results: boolean[] = [];
  for (let i = 0; i < segments.length; i++) {
    results.push(lasso.IntersectsSegment(segments[i][0], segments[i][1], segments[i][2], segments[i][3]));
  }
  return results;
}

export function lassoStrokeProbe(points: number[][], strokes: number[][]): boolean[] {
  const lasso = new LassoGeometry(toPoints(points));
  const results: boolean[] = [];
  for (let i = 0; i < strokes.length; i++) {
    const stroke = new InkStroke(new StrokeAttributes());
    const rows = strokes[i];
    for (let j = 0; j + 1 < rows.length; j += 2) {
      stroke.addPoint(rows[j], rows[j + 1]);
    }
    stroke.complete();
    results.push(lasso.IntersectsStroke(stroke, identityMatrix()));
  }
  return results;
}

function toPoints(rows: number[][]): LassoPoint[] {
  const points: LassoPoint[] = [];
  for (let i = 0; i < rows.length; i++) {
    points.push({ X: rows[i][0], Y: rows[i][1] });
  }
  return points;
}