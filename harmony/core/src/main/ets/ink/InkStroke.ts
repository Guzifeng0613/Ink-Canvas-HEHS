// @parity src/InkCanvas.Core/Ink/InkStroke.cs
import { InkPoint } from './InkPoint';
import { StrokeAttributes, TaperKind } from './StrokeAttributes';
import { applyTaperAt } from './TaperProfile';
import { Matrix3x2, matrixFromArray } from '../math/Matrix3x2';

// Rect：LTRB，与 C# 语义一致（IsEmpty 包含退化矩形）。
export class Rect {
  Left: number;
  Top: number;
  Right: number;
  Bottom: number;

  constructor(left: number = 0, top: number = 0, right: number = 0, bottom: number = 0) {
    this.Left = left;
    this.Top = top;
    this.Right = right;
    this.Bottom = bottom;
  }

  static empty(): Rect {
    return new Rect(0, 0, 0, 0);
  }

  width(): number {
    return this.Right - this.Left;
  }

  height(): number {
    return this.Bottom - this.Top;
  }

  isEmpty(): boolean {
    return this.width() <= 0 || this.height() <= 0;
  }

  centerX(): number {
    return (this.Left + this.Right) / 2;
  }

  centerY(): number {
    return (this.Top + this.Bottom) / 2;
  }

  contains(x: number, y: number): boolean {
    return x >= this.Left && x <= this.Right && y >= this.Top && y <= this.Bottom;
  }

  intersects(other: Rect): boolean {
    return !(other.Left > this.Right || other.Right < this.Left || other.Top > this.Bottom || other.Bottom < this.Top);
  }

  union(other: Rect): Rect {
    if (this.isEmpty()) {
      return new Rect(other.Left, other.Top, other.Right, other.Bottom);
    }
    if (other.isEmpty()) {
      return new Rect(this.Left, this.Top, this.Right, this.Bottom);
    }
    return new Rect(
      Math.min(this.Left, other.Left),
      Math.min(this.Top, other.Top),
      Math.max(this.Right, other.Right),
      Math.max(this.Bottom, other.Bottom)
    );
  }

  inflate(delta: number): Rect {
    return new Rect(this.Left - delta, this.Top - delta, this.Right + delta, this.Bottom + delta);
  }
}

export class SelectionFrameState {
  Bounds: Rect;
  Transform: Matrix3x2;

  constructor(bounds: Rect, transform: Matrix3x2) {
    this.Bounds = bounds;
    this.Transform = transform;
  }
}

let nextStrokeId: number = 1;

export class InkStroke {
  Id: string;
  Attributes: StrokeAttributes;
  IsCompleted: boolean = false;
  IsRecognizedShape: boolean = false;
  private points: InkPoint[] = [];
  private cachedBounds: Rect | null = null;
  private objectFrame: SelectionFrameState | null = null;

  constructor(attributes: StrokeAttributes) {
    this.Attributes = attributes;
    this.Id = 'stroke-' + nextStrokeId;
    nextStrokeId += 1;
  }

  get Points(): InkPoint[] {
    return this.points;
  }

  get Count(): number {
    return this.points.length;
  }

  get TaperKind(): TaperKind {
    return this.Attributes.TaperKind;
  }

  set TaperKind(value: TaperKind) {
    if (this.Attributes.TaperKind === value) {
      return;
    }
    this.Attributes.TaperKind = value;
    if (this.IsCompleted) {
      this.rebuildWidths();
    }
  }

  addPoint(x: number, y: number, pressure: number = 0.5, timeMs: number = 0): void {
    this.points.push(new InkPoint(x, y, pressure, 0, timeMs));
    applyTaperAt(
      this.points,
      this.points.length - 1,
      this.Attributes.BaseWidth,
      this.Attributes.effectiveTaper()
    );
    this.cachedBounds = null;
    this.objectFrame = null;
  }

  complete(): void {
    this.IsCompleted = true;
  }

  private rebuildWidths(): void {
    for (let i = 0; i < this.points.length; i++) {
      this.points[i].Width = 0;
    }
    for (let tip = 0; tip < this.points.length; tip++) {
      applyTaperAt(this.points, tip, this.Attributes.BaseWidth, this.Attributes.TaperKind);
    }
    this.cachedBounds = null;
  }

  restorePoints(points: InkPoint[]): void {
    const copy: InkPoint[] = [];
    for (let i = 0; i < points.length; i++) {
      copy.push(points[i].clone());
    }
    this.points = copy;
    this.cachedBounds = null;
  }

  exportObjectFrame(): number[] | null {
    if (this.objectFrame === null) {
      return null;
    }
    const b = this.objectFrame.Bounds;
    const m = this.objectFrame.Transform;
    return [b.Left, b.Top, b.Right, b.Bottom, m.M11, m.M12, m.M21, m.M22, m.M31, m.M32];
  }

  importObjectFrame(data: number[] | null): void {
    if (data === null || data === undefined || data.length !== 10) {
      return;
    }
    for (let i = 0; i < 10; i++) {
      if (!isFinite(data[i])) {
        return;
      }
    }
    const bounds = new Rect(data[0], data[1], data[2], data[3]);
    const transform = matrixFromArray([data[4], data[5], data[6], data[7], data[8], data[9]]);
    const determinant = transform.M11 * transform.M22 - transform.M12 * transform.M21;
    if (!bounds.isEmpty() && Math.abs(determinant) > 1e-6) {
      this.objectFrame = new SelectionFrameState(bounds, transform);
    }
  }

  restorePrefix(points: InkPoint[], count: number): void {
    const copy: InkPoint[] = [];
    const n = Math.min(count, points.length);
    for (let i = 0; i < n; i++) {
      copy.push(points[i].clone());
    }
    this.points = copy;
    this.cachedBounds = null;
    this.objectFrame = null;
  }

  bounds(): Rect {
    if (this.cachedBounds !== null) {
      return this.cachedBounds;
    }
    if (this.points.length === 0) {
      this.cachedBounds = Rect.empty();
      return this.cachedBounds;
    }
    let minX = this.points[0].X;
    let minY = this.points[0].Y;
    let maxX = minX;
    let maxY = minY;
    let maxHalf = 0;
    for (let i = 1; i < this.points.length; i++) {
      const p = this.points[i];
      minX = Math.min(minX, p.X);
      minY = Math.min(minY, p.Y);
      maxX = Math.max(maxX, p.X);
      maxY = Math.max(maxY, p.Y);
      maxHalf = Math.max(maxHalf, p.Width);
    }
    maxHalf = Math.max(Math.max(maxHalf, this.points[0].Width), 0.5) * 0.5;
    const nib = this.Attributes.nibTransform();
    const rx = maxHalf * Math.sqrt(nib.M11 * nib.M11 + nib.M21 * nib.M21);
    const ry = maxHalf * Math.sqrt(nib.M12 * nib.M12 + nib.M22 * nib.M22);
    this.cachedBounds = new Rect(minX - rx, minY - ry, maxX + rx, maxY + ry);
    return this.cachedBounds;
  }

  distanceTo(x: number, y: number): number {
    if (this.points.length === 0) {
      return Number.MAX_VALUE;
    }
    if (this.points.length === 1) {
      return Math.sqrt((x - this.points[0].X) * (x - this.points[0].X) + (y - this.points[0].Y) * (y - this.points[0].Y));
    }
    let best = Number.MAX_VALUE;
    for (let i = 1; i < this.points.length; i++) {
      const d = InkStroke.distanceToSegment(x, y, this.points[i - 1], this.points[i]);
      if (d < best) {
        best = d;
      }
    }
    return best;
  }

  static distanceToSegment(x: number, y: number, a: InkPoint, b: InkPoint): number {
    const abx = b.X - a.X;
    const aby = b.Y - a.Y;
    const len2 = abx * abx + aby * aby;
    const px = x - a.X;
    const py = y - a.Y;
    if (len2 < 1e-6) {
      return Math.sqrt(px * px + py * py);
    }
    let t = (px * abx + py * aby) / len2;
    t = Math.min(Math.max(t, 0), 1);
    const dx = x - (a.X + abx * t);
    const dy = y - (a.Y + aby * t);
    return Math.sqrt(dx * dx + dy * dy);
  }

  clone(): InkStroke {
    const copy = new InkStroke(this.Attributes.clone());
    copy.Id = this.Id;
    copy.IsRecognizedShape = this.IsRecognizedShape;
    copy.IsCompleted = this.IsCompleted;
    copy.restorePoints(this.points);
    if (this.objectFrame !== null) {
      copy.objectFrame = new SelectionFrameState(this.objectFrame.Bounds, this.objectFrame.Transform);
    }
    return copy;
  }
}

export interface StrokePointInput {
  X: number;
  Y: number;
  Width: number;
}

function buildStroke(points: StrokePointInput[], baseWidth: number): InkStroke {
  const stroke = new InkStroke(new StrokeAttributes());
  stroke.Attributes.BaseWidth = baseWidth;
  const inputPoints: InkPoint[] = [];
  for (let i = 0; i < points.length; i++) {
    inputPoints.push(new InkPoint(points[i].X, points[i].Y, 0.5, points[i].Width, 0));
  }
  stroke.restorePoints(inputPoints);
  return stroke;
}

export function strokeBounds(points: StrokePointInput[], baseWidth: number): number[] {
  const b = buildStroke(points, baseWidth).bounds();
  return [b.Left, b.Top, b.Right, b.Bottom];
}

export function setTaperAndWidths(points: StrokePointInput[], baseWidth: number, kind: number): number[] {
  const stroke = buildStroke(points, baseWidth);
  stroke.complete();
  stroke.TaperKind = kind as TaperKind;
  const widths: number[] = [];
  for (let i = 0; i < stroke.Count; i++) {
    widths.push(stroke.Points[i].Width);
  }
  return widths;
}
