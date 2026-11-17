// @parity src/InkCanvas.Core/Ink/InkDocument.cs
import { InkStroke, Rect } from './InkStroke';
import type { StrokePointInput } from './InkStroke';
import { StrokeAttributes } from './StrokeAttributes';
import { InkPoint } from './InkPoint';
import {
  Matrix3x2,
  identityMatrix,
  transformPointByMatrix,
  Point2
} from '../math/Matrix3x2';

export class InkDocument {
  private strokes: InkStroke[] = [];

  get Strokes(): InkStroke[] {
    return this.strokes;
  }

  get Count(): number {
    return this.strokes.length;
  }

  add(stroke: InkStroke): void {
    if (stroke === null) {
      return;
    }
    this.strokes.push(stroke);
  }

  addRange(strokes: InkStroke[]): void {
    for (let i = 0; i < strokes.length; i++) {
      if (strokes[i] !== null) {
        this.strokes.push(strokes[i]);
      }
    }
  }

  remove(stroke: InkStroke): boolean {
    const index = this.strokes.indexOf(stroke);
    if (index < 0) {
      return false;
    }
    this.strokes.splice(index, 1);
    return true;
  }

  removeAll(strokes: InkStroke[]): number {
    let removed = 0;
    for (let i = 0; i < strokes.length; i++) {
      const index = this.strokes.indexOf(strokes[i]);
      if (index >= 0) {
        this.strokes.splice(index, 1);
        removed++;
      }
    }
    return removed;
  }

  insert(index: number, stroke: InkStroke): void {
    const at = Math.min(Math.max(index, 0), this.strokes.length);
    this.strokes.splice(at, 0, stroke);
  }

  insertRange(entries: { Index: number; Stroke: InkStroke }[]): void {
    for (let i = 0; i < entries.length; i++) {
      this.insert(entries[i].Index, entries[i].Stroke);
    }
  }

  replace(stroke: InkStroke, replacement: InkStroke[]): boolean {
    const index = this.strokes.indexOf(stroke);
    if (index < 0) {
      return false;
    }
    this.strokes.splice(index, 1);
    for (let i = 0; i < replacement.length; i++) {
      this.strokes.splice(index + i, 0, replacement[i]);
    }
    return true;
  }

  clear(): void {
    this.strokes = [];
  }

  bounds(): Rect {
    let result = Rect.empty();
    for (let i = 0; i < this.strokes.length; i++) {
      result = result.union(this.strokes[i].bounds());
    }
    return result;
  }

  hitTestPoint(x: number, y: number, radius: number): InkStroke[] {
    const hits: InkStroke[] = [];
    for (let i = 0; i < this.strokes.length; i++) {
      const stroke = this.strokes[i];
      if (!stroke.bounds().inflate(radius).contains(x, y)) {
        continue;
      }
      if (stroke.distanceTo(x, y) <= radius + stroke.Attributes.BaseWidth * 0.5) {
        hits.push(stroke);
      }
    }
    return hits;
  }

  hitTestRect(left: number, top: number, right: number, bottom: number, worldToView: Matrix3x2): InkStroke[] {
    const rect = new Rect(left, top, right, bottom);
    const hits: InkStroke[] = [];
    for (let i = 0; i < this.strokes.length; i++) {
      const stroke = this.strokes[i];
      const b = stroke.bounds();
      const corners: Point2[] = [
        transformPointByMatrix(worldToView, b.Left, b.Top),
        transformPointByMatrix(worldToView, b.Right, b.Top),
        transformPointByMatrix(worldToView, b.Right, b.Bottom),
        transformPointByMatrix(worldToView, b.Left, b.Bottom)
      ];
      let minX = corners[0].X;
      let minY = corners[0].Y;
      let maxX = minX;
      let maxY = minY;
      for (let c = 1; c < corners.length; c++) {
        minX = Math.min(minX, corners[c].X);
        minY = Math.min(minY, corners[c].Y);
        maxX = Math.max(maxX, corners[c].X);
        maxY = Math.max(maxY, corners[c].Y);
      }
      if (!new Rect(minX, minY, maxX, maxY).intersects(rect)) {
        continue;
      }
      if (stroke.Count === 0) {
        continue;
      }
      let previous = transformPointByMatrix(worldToView, stroke.Points[0].X, stroke.Points[0].Y);
      let hit = rect.contains(previous.X, previous.Y);
      for (let p = 1; !hit && p < stroke.Count; p++) {
        const point = stroke.Points[p];
        const current = transformPointByMatrix(worldToView, point.X, point.Y);
        hit = InkDocument.segmentIntersectsRect(previous.X, previous.Y, current.X, current.Y, rect);
        previous = current;
      }
      if (hit) {
        hits.push(stroke);
      }
    }
    return hits;
  }

  static segmentIntersectsRect(ax: number, ay: number, bx: number, by: number, rect: Rect): boolean {
    let low = 0;
    let high = 1;
    const dx = bx - ax;
    const dy = by - ay;
    const clip = (direction: number, distance: number): boolean => {
      if (Math.abs(direction) < 1e-6) {
        return distance >= 0;
      }
      const t = distance / direction;
      if (direction < 0) {
        low = Math.max(low, t);
      } else {
        high = Math.min(high, t);
      }
      return low <= high;
    };
    return clip(-dx, ax - rect.Left)
      && clip(dx, rect.Right - ax)
      && clip(-dy, ay - rect.Top)
      && clip(dy, rect.Bottom - ay);
  }

  eraseAt(x: number, y: number, radius: number): InkStroke[] {
    const hits = this.hitTestPoint(x, y, radius);
    if (hits.length > 0) {
      this.removeAll(hits);
    }
    return hits;
  }

  clone(): InkDocument {
    const copy = new InkDocument();
    for (let i = 0; i < this.strokes.length; i++) {
      copy.add(this.strokes[i].clone());
    }
    return copy;
  }
}

export interface DocumentStrokeInput {
  BaseWidth: number;
  Points: StrokePointInput[];
}

function buildDocument(strokes: DocumentStrokeInput[]): InkDocument {
  const doc = new InkDocument();
  for (let i = 0; i < strokes.length; i++) {
    const input = strokes[i];
    const stroke = new InkStroke(new StrokeAttributes());
    stroke.Attributes.BaseWidth = input.BaseWidth;
    const points: InkPoint[] = [];
    for (let j = 0; j < input.Points.length; j++) {
      const p = input.Points[j];
      points.push(new InkPoint(p.X, p.Y, 0.5, p.Width, 0));
    }
    stroke.restorePoints(points);
    doc.add(stroke);
  }
  return doc;
}

export function documentHitTestPoint(
  strokes: DocumentStrokeInput[],
  x: number,
  y: number,
  radius: number
): number[] {
  const doc = buildDocument(strokes);
  return indexOf(doc.Strokes, doc.hitTestPoint(x, y, radius));
}

export function documentHitTestRect(
  strokes: DocumentStrokeInput[],
  left: number,
  top: number,
  right: number,
  bottom: number
): number[] {
  const doc = buildDocument(strokes);
  return indexOf(doc.Strokes, doc.hitTestRect(left, top, right, bottom, identityMatrix()));
}

export function documentEraseAt(
  strokes: DocumentStrokeInput[],
  x: number,
  y: number,
  radius: number
): DocumentEraseResult {
  const doc = buildDocument(strokes);
  const all = doc.Strokes.slice();
  const hits = doc.eraseAt(x, y, radius);
  return { hits: indexOf(all, hits), remaining: doc.Count };
}

export function documentBounds(strokes: DocumentStrokeInput[]): number[] {
  const b = buildDocument(strokes).bounds();
  return [b.Left, b.Top, b.Right, b.Bottom];
}

export interface DocumentEraseResult {
  hits: number[];
  remaining: number;
}

function indexOf(all: InkStroke[], subset: InkStroke[]): number[] {
  const result: number[] = [];
  for (let i = 0; i < subset.length; i++) {
    const index = all.indexOf(subset[i]);
    if (index >= 0) {
      result.push(index);
    }
  }
  return result;
}
