// @parity src/InkCanvas.Core/Ink/InkStroke.cs
import { InkPoint } from './InkPoint';
import { StrokeAttributes, TaperKind } from './StrokeAttributes';
import { applyTaperAt } from './TaperProfile';

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

let nextStrokeId: number = 1;

export class InkStroke {
  Id: string;
  Attributes: StrokeAttributes;
  IsCompleted: boolean = false;
  IsRecognizedShape: boolean = false;
  private points: InkPoint[] = [];
  private cachedBounds: Rect | null = null;

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
    this.Attributes.TaperKind = value;
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
  }

  complete(): void {
    this.IsCompleted = true;
  }

  restorePoints(points: InkPoint[]): void {
    const copy: InkPoint[] = [];
    for (let i = 0; i < points.length; i++) {
      copy.push(points[i].clone());
    }
    this.points = copy;
    this.cachedBounds = null;
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
    for (let i = 1; i < this.points.length; i++) {
      const p = this.points[i];
      minX = Math.min(minX, p.X);
      minY = Math.min(minY, p.Y);
      maxX = Math.max(maxX, p.X);
      maxY = Math.max(maxY, p.Y);
    }
    this.cachedBounds = new Rect(minX, minY, maxX, maxY);
    return this.cachedBounds;
  }

  clone(): InkStroke {
    const copy = new InkStroke(this.Attributes.clone());
    copy.Id = this.Id;
    copy.IsRecognizedShape = this.IsRecognizedShape;
    copy.IsCompleted = this.IsCompleted;
    copy.restorePoints(this.points);
    return copy;
  }
}
