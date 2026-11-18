// @parity src/InkCanvas.Core/Recognition/ShapeRecognizer.cs
// 托管几何图形识别：阈值与判定顺序 1:1 照抄 C#。
export enum RecognizedShape {
  None = 0,
  Line = 1,
  Circle = 2,
  Ellipse = 3,
  Triangle = 4,
  Rectangle = 5
}

export interface InkPointLike {
  X: number;
  Y: number;
  Pressure?: number;
}

export interface RecognitionPoint {
  X: number;
  Y: number;
}

export interface RecognitionSummary {
  kind: number;
  centerX: number;
  centerY: number;
  radiusX: number;
  radiusY: number;
  confidence: number;
  corners: RecognitionPoint[];
}

interface BoundsInfo {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  width: number;
  height: number;
}

interface CircleFit {
  radius: number;
  residual: number;
}

interface EllipseFit {
  rx: number;
  ry: number;
  residual: number;
}

interface Solve3Result {
  valid: boolean;
  x: number;
  y: number;
  z: number;
}

/** 最小图形尺寸（px，世界坐标）；小于此值不猜测。 */
export const MinSize: number = 40;

function none(): RecognitionSummary {
  return {
    kind: RecognizedShape.None,
    centerX: 0,
    centerY: 0,
    radiusX: 0,
    radiusY: 0,
    confidence: 0,
    corners: []
  };
}

function distance(a: RecognitionPoint, b: RecognitionPoint): number {
  const dx = b.X - a.X;
  const dy = b.Y - a.Y;
  return Math.sqrt(dx * dx + dy * dy);
}

function normalize(x: number, y: number): RecognitionPoint {
  const len = Math.sqrt(x * x + y * y);
  return { X: x / len, Y: y / len };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function centroid(pts: RecognitionPoint[]): RecognitionPoint {
  let sumX = 0;
  let sumY = 0;
  for (let i = 0; i < pts.length; i++) {
    sumX += pts[i].X;
    sumY += pts[i].Y;
  }
  return { X: sumX / pts.length, Y: sumY / pts.length };
}

function boundsOf(pts: RecognitionPoint[]): BoundsInfo {
  let minX = Number.MAX_VALUE;
  let minY = Number.MAX_VALUE;
  let maxX = -Number.MAX_VALUE;
  let maxY = -Number.MAX_VALUE;
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    if (p.X < minX) {
      minX = p.X;
    }
    if (p.Y < minY) {
      minY = p.Y;
    }
    if (p.X > maxX) {
      maxX = p.X;
    }
    if (p.Y > maxY) {
      maxY = p.Y;
    }
  }
  return { minX: minX, minY: minY, maxX: maxX, maxY: maxY, width: maxX - minX, height: maxY - minY };
}

function distanceToSegment(p: RecognitionPoint, a: RecognitionPoint, b: RecognitionPoint): number {
  const abX = b.X - a.X;
  const abY = b.Y - a.Y;
  const len2 = abX * abX + abY * abY;
  if (len2 < 1e-6) {
    return distance(p, a);
  }
  const dot = (p.X - a.X) * abX + (p.Y - a.Y) * abY;
  const t = clamp(dot / len2, 0, 1);
  return distance(p, { X: a.X + abX * t, Y: a.Y + abY * t });
}

function tryLine(pts: RecognitionPoint[]): RecognitionSummary | null {
  const c = centroid(pts);
  let sxx = 0;
  let syy = 0;
  let sxy = 0;
  for (let i = 0; i < pts.length; i++) {
    const dx = pts[i].X - c.X;
    const dy = pts[i].Y - c.Y;
    sxx += dx * dx;
    syy += dy * dy;
    sxy += dx * dy;
  }
  const trace = sxx + syy;
  if (trace < 1e-6) {
    return null;
  }
  const det = sxx * syy - sxy * sxy;
  const disc = Math.sqrt(Math.max(0, trace * trace * 0.25 - det));
  const major = trace * 0.5 + disc;
  const minor = trace * 0.5 - disc;
  if (major < 1e-6) {
    return null;
  }

  const spread = Math.sqrt(Math.max(0, minor) / pts.length);
  const length = 2 * Math.sqrt(major / pts.length);
  if (length < MinSize) {
    return null;
  }
  if (spread / length > 0.035) {
    return null;
  }

  let axis = normalize(sxy, major - sxx);
  const axisLengthSquared = axis.X * axis.X + axis.Y * axis.Y;
  if (Number.isNaN(axis.X) || axisLengthSquared < 0.5) {
    axis = { X: 1, Y: 0 };
  }
  let min = Number.MAX_VALUE;
  let max = -Number.MAX_VALUE;
  for (let i = 0; i < pts.length; i++) {
    const t = (pts[i].X - c.X) * axis.X + (pts[i].Y - c.Y) * axis.Y;
    if (t < min) {
      min = t;
    }
    if (t > max) {
      max = t;
    }
  }
  const corners: RecognitionPoint[] = [
    { X: c.X + axis.X * min, Y: c.Y + axis.Y * min },
    { X: c.X + axis.X * max, Y: c.Y + axis.Y * max }
  ];
  // 基线演进加固：最大垂偏（2.5px 或跨度 2%）+ 路径/直线长比 ≥0.97（钩笔/回笔是手写，
  // 即使 RMS 展幅小）；<3px 的静止抖动包不计入路径（按住端点时笔包抖动）。
  const directLength = distance(pts[0], pts[pts.length - 1]);
  let pathLength = 0;
  const maxDeviation = Math.max(2.5, (max - min) * 0.02);
  for (let i = 0; i < pts.length; i++) {
    if (Math.abs((pts[i].X - c.X) * axis.Y - (pts[i].Y - c.Y) * axis.X) > maxDeviation) {
      return null;
    }
  }
  let lastProgress = pts[0];
  for (let i = 1; i < pts.length; i++) {
    const step = distance(lastProgress, pts[i]);
    if (step < 3) {
      continue;
    }
    pathLength += step;
    lastProgress = pts[i];
  }
  pathLength += distance(lastProgress, pts[pts.length - 1]);
  if (pathLength <= 0 || directLength / pathLength < 0.97) {
    return null;
  }
  return {
    kind: RecognizedShape.Line,
    centerX: c.X,
    centerY: c.Y,
    radiusX: 0,
    radiusY: 0,
    confidence: 1 - spread / length,
    corners: corners
  };
}

function solve3(m: number[][], rhs: number[]): Solve3Result {
  const a: number[][] = [];
  for (let r = 0; r < 3; r++) {
    a.push([m[r][0], m[r][1], m[r][2]]);
  }
  const b: number[] = [rhs[0], rhs[1], rhs[2]];
  for (let col = 0; col < 3; col++) {
    let pivot = col;
    for (let r = col + 1; r < 3; r++) {
      if (Math.abs(a[r][col]) > Math.abs(a[pivot][col])) {
        pivot = r;
      }
    }
    if (Math.abs(a[pivot][col]) < 1e-12) {
      return { valid: false, x: 0, y: 0, z: 0 };
    }
    if (pivot !== col) {
      for (let k = 0; k < 3; k++) {
        const temp = a[col][k];
        a[col][k] = a[pivot][k];
        a[pivot][k] = temp;
      }
      const tempB = b[col];
      b[col] = b[pivot];
      b[pivot] = tempB;
    }
    for (let r = col + 1; r < 3; r++) {
      const f = a[r][col] / a[col][col];
      for (let k = col; k < 3; k++) {
        a[r][k] -= f * a[col][k];
      }
      b[r] -= f * b[col];
    }
  }
  const z = b[2] / a[2][2];
  const y = (b[1] - a[1][2] * z) / a[1][1];
  const x = (b[0] - a[0][1] * y - a[0][2] * z) / a[0][0];
  if (Number.isNaN(x) || Number.isNaN(y) || Number.isNaN(z)) {
    return { valid: false, x: 0, y: 0, z: 0 };
  }
  return { valid: true, x: x, y: y, z: z };
}

function tryCircle(pts: RecognitionPoint[], guess: RecognitionPoint): CircleFit | null {
  let sxx = 0;
  let sxy = 0;
  let syy = 0;
  let sxz = 0;
  let syz = 0;
  let sx = 0;
  let sy = 0;
  let sz = 0;
  const n = pts.length;
  for (let i = 0; i < n; i++) {
    const x = pts[i].X - guess.X;
    const y = pts[i].Y - guess.Y;
    const z = x * x + y * y;
    sxx += x * x;
    sxy += x * y;
    syy += y * y;
    sxz += x * z;
    syz += y * z;
    sx += x;
    sy += y;
    sz += z;
  }
  const m: number[][] = [
    [sxx, sxy, sx],
    [sxy, syy, sy],
    [sx, sy, n]
  ];
  const rhs: number[] = [-sxz, -syz, -sz];
  const solved = solve3(m, rhs);
  if (!solved.valid) {
    return null;
  }

  const cx = -solved.x / 2;
  const cy = -solved.y / 2;
  const r2 = cx * cx + cy * cy - solved.z;
  if (r2 <= 1) {
    return null;
  }
  const r = Math.sqrt(r2);

  let err = 0;
  const center: RecognitionPoint = { X: cx + guess.X, Y: cy + guess.Y };
  for (let i = 0; i < n; i++) {
    const d = distance(pts[i], center) - r;
    err += d * d;
  }
  const radius = r;
  const residual = Math.sqrt(err / n) / r;
  if (!(radius > 1)) {
    return null;
  }
  return { radius: radius, residual: residual };
}

function tryEllipse(pts: RecognitionPoint[], c: RecognitionPoint, bounds: BoundsInfo): EllipseFit {
  const rx = Math.max(bounds.width * 0.5, 1);
  const ry = Math.max(bounds.height * 0.5, 1);
  let err = 0;
  for (let i = 0; i < pts.length; i++) {
    const u = (pts[i].X - c.X) / rx;
    const v = (pts[i].Y - c.Y) / ry;
    const d = Math.sqrt(u * u + v * v) - 1;
    err += d * d;
  }
  const residual = Math.sqrt(err / pts.length);
  return { rx: rx, ry: ry, residual: residual };
}

function simplify(pts: RecognitionPoint[], epsilon: number): RecognitionPoint[] {
  if (pts.length < 3) {
    const copy: RecognitionPoint[] = [];
    for (let i = 0; i < pts.length; i++) {
      copy.push(pts[i]);
    }
    return copy;
  }
  const keep: boolean[] = [];
  for (let i = 0; i < pts.length; i++) {
    keep.push(false);
  }
  keep[0] = true;
  keep[pts.length - 1] = true;
  const stack: number[] = [];
  stack.push(0);
  stack.push(pts.length - 1);
  while (stack.length > 0) {
    const last = stack[stack.length - 1];
    stack.pop();
    const first = stack[stack.length - 1];
    stack.pop();
    let maxDist = 0;
    let index = first;
    for (let i = first + 1; i < last; i++) {
      const d = distanceToSegment(pts[i], pts[first], pts[last]);
      if (d > maxDist) {
        maxDist = d;
        index = i;
      }
    }
    if (maxDist > epsilon && index > first && index < last) {
      keep[index] = true;
      stack.push(first);
      stack.push(index);
      stack.push(index);
      stack.push(last);
    }
  }
  const result: RecognitionPoint[] = [];
  for (let i = 0; i < pts.length; i++) {
    if (keep[i]) {
      result.push(pts[i]);
    }
  }
  return result;
}

function tryPolygon(pts: RecognitionPoint[]): RecognitionPoint[] | null {
  const simplified = simplify(pts, 6);
  if (simplified.length < 4) {
    return null;
  }

  const found: RecognitionPoint[] = [];
  for (let i = 1; i < simplified.length - 1; i++) {
    const v1 = normalize(simplified[i].X - simplified[i - 1].X, simplified[i].Y - simplified[i - 1].Y);
    const v2 = normalize(simplified[i + 1].X - simplified[i].X, simplified[i + 1].Y - simplified[i].Y);
    const dot = clamp(v1.X * v2.X + v1.Y * v2.Y, -1, 1);
    const angle = Math.acos(dot) * 180 / Math.PI;
    if (angle > 45) {
      if (found.length === 0 || distance(found[found.length - 1], simplified[i]) > 20) {
        found.push(simplified[i]);
      }
    }
  }
  if (found.length === 3 || found.length === 4) {
    return found;
  }
  return null;
}

/**
 * 与 C# ShapeRecognizer.Recognize(IReadOnlyList<InkStroke>) 等价：
 * 每个内层数组代表一条笔画的点序列。
 */
export function recognizeStrokes(strokes: InkPointLike[][]): RecognitionSummary {
  if (strokes === null || strokes === undefined || strokes.length === 0) {
    return none();
  }

  const pts: RecognitionPoint[] = [];
  let arc = 0;
  for (let si = 0; si < strokes.length; si++) {
    const stroke = strokes[si];
    let prev: RecognitionPoint | null = null;
    for (let pi = 0; pi < stroke.length; pi++) {
      const point: RecognitionPoint = { X: stroke[pi].X, Y: stroke[pi].Y };
      pts.push(point);
      if (prev !== null) {
        arc += distance(prev, point);
      }
      prev = point;
    }
  }
  if (pts.length < 8 || arc < MinSize) {
    return none();
  }

  const bounds = boundsOf(pts);
  if (bounds.width < MinSize && bounds.height < MinSize) {
    return none();
  }

  const c = centroid(pts);

  // 基线演进：直线只认单笔输入（多笔拼接不走直线分支）。
  const line = strokes.length === 1 ? tryLine(pts) : null;
  if (line !== null) {
    return line;
  }

  const closed = distance(pts[0], pts[pts.length - 1]) < Math.max(12, arc * 0.18);
  if (!closed) {
    return none();
  }

  const circle = tryCircle(pts, c);
  if (circle !== null) {
    const aspect = bounds.height <= 0.01 ? 1 : bounds.width / bounds.height;
    if (circle.residual < 0.10 && aspect > 0.82 && aspect < 1.22) {
      return {
        kind: RecognizedShape.Circle,
        centerX: c.X,
        centerY: c.Y,
        radiusX: circle.radius,
        radiusY: circle.radius,
        confidence: 1 - circle.residual,
        corners: []
      };
    }
  }

  const ellipse = tryEllipse(pts, c, bounds);
  if (ellipse.residual < 0.14) {
    return {
      kind: RecognizedShape.Ellipse,
      centerX: c.X,
      centerY: c.Y,
      radiusX: ellipse.rx,
      radiusY: ellipse.ry,
      confidence: 1 - ellipse.residual,
      corners: []
    };
  }

  const corners = tryPolygon(pts);
  if (corners !== null) {
    if (corners.length === 3) {
      return {
        kind: RecognizedShape.Triangle,
        centerX: c.X,
        centerY: c.Y,
        radiusX: 0,
        radiusY: 0,
        confidence: 0.8,
        corners: corners
      };
    }
    if (corners.length === 4) {
      return {
        kind: RecognizedShape.Rectangle,
        centerX: c.X,
        centerY: c.Y,
        radiusX: 0,
        radiusY: 0,
        confidence: 0.8,
        corners: corners
      };
    }
  }

  return none();
}

/** 向量入口：单条笔画的点序列。 */
export function recognize(points: InkPointLike[]): RecognitionSummary {
  return recognizeStrokes([points]);
}
