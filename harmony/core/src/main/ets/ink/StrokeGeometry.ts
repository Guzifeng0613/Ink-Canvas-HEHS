// @parity src/InkCanvas.Core/Ink/StrokeGeometry.cs
// 逐段胶囊 + 同向 Winding 并集轮廓。所有 contour 最终正面积；渲染按 nonzero 一次填充。
import { InkPoint } from './InkPoint';

class Pt {
  X: number;
  Y: number;

  constructor(x: number, y: number) {
    this.X = x;
    this.Y = y;
  }
}

function circlePoints(cx: number, cy: number, radius: number, segments: number): Pt[] {
  const result: Pt[] = [];
  for (let i = 0; i < segments; i++) {
    const angle = (2 * Math.PI * i) / segments;
    result.push(new Pt(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius));
  }
  return result;
}

function signedArea(points: Pt[]): number {
  if (points.length < 3) {
    return 0;
  }
  let area = 0;
  for (let i = 0; i < points.length; i++) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    area += a.X * b.Y - b.X * a.Y;
  }
  return 0.5 * area;
}

function normalizeWinding(points: Pt[]): Pt[] {
  if (signedArea(points) >= 0) {
    return points;
  }
  const reversed: Pt[] = [];
  for (let i = points.length - 1; i >= 0; i--) {
    reversed.push(points[i]);
  }
  return reversed;
}

function appendArc(list: Pt[], cx: number, cy: number, radius: number, startAngle: number, delta: number, steps: number): void {
  if (radius <= 0) {
    return;
  }
  for (let i = 1; i < steps; i++) {
    const angle = startAngle + (delta * i) / steps;
    list.push(new Pt(cx + Math.cos(angle) * radius, cy + Math.sin(angle) * radius));
  }
}

function toFlat(points: Pt[]): number[] {
  const flat: number[] = [];
  for (let i = 0; i < points.length; i++) {
    flat.push(points[i].X);
    flat.push(points[i].Y);
  }
  return flat;
}

export function buildSegmentOutline(
  startX: number,
  startY: number,
  startRadius: number,
  endX: number,
  endY: number,
  endRadius: number
): number[] {
  const dx = endX - startX;
  const dy = endY - startY;
  const length = Math.sqrt(dx * dx + dy * dy);
  if (length < 1e-4) {
    const radius = Math.max(startRadius, endRadius);
    return toFlat(normalizeWinding(circlePoints(startX, startY, radius, 24)));
  }

  const tx = dx / length;
  const ty = dy / length;
  const nx = -ty;
  const ny = tx;
  const a0 = Math.atan2(ny, nx);

  const outline: Pt[] = [];
  outline.push(new Pt(startX + nx * startRadius, startY + ny * startRadius));
  outline.push(new Pt(endX + nx * endRadius, endY + ny * endRadius));
  appendArc(outline, endX, endY, endRadius, a0, -Math.PI, 12);
  outline.push(new Pt(endX - nx * endRadius, endY - ny * endRadius));
  outline.push(new Pt(startX - nx * startRadius, startY - ny * startRadius));
  appendArc(outline, startX, startY, startRadius, a0 + Math.PI, -Math.PI, 12);
  return toFlat(normalizeWinding(outline));
}

export function buildUnionContours(points: InkPoint[]): number[][] {
  if (points.length === 0) {
    return [];
  }
  if (points.length === 1) {
    const radius = Math.max(points[0].Width, 0.5) * 0.5;
    return [toFlat(normalizeWinding(circlePoints(points[0].X, points[0].Y, radius, 24)))];
  }
  const contours: number[][] = [];
  for (let i = 1; i < points.length; i++) {
    const a = points[i - 1];
    const b = points[i];
    contours.push(
      buildSegmentOutline(
        a.X, a.Y, Math.max(a.Width, 0.5) * 0.5,
        b.X, b.Y, Math.max(b.Width, 0.5) * 0.5
      )
    );
  }
  return contours;
}

export function buildOutlineFlat(points: InkPoint[]): number[] {
  const contours = buildUnionContours(points);
  const flat: number[] = [];
  for (let i = 0; i < contours.length; i++) {
    for (let j = 0; j < contours[i].length; j++) {
      flat.push(contours[i][j]);
    }
  }
  return flat;
}

export function signedAreaOfFlat(flat: number[]): number {
  const points: Pt[] = [];
  for (let i = 0; i + 1 < flat.length; i += 2) {
    points.push(new Pt(flat[i], flat[i + 1]));
  }
  return signedArea(points);
}
