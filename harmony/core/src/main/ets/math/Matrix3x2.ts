// @parity src/InkCanvas.Core/Selection/SelectionGeometry.cs
// System.Numerics.Matrix3x2 的行向量语义：p' = p * M；a * b 表示先 a 后 b。
export class Point2 {
  X: number;
  Y: number;

  constructor(x: number = 0, y: number = 0) {
    this.X = x;
    this.Y = y;
  }
}

export class Matrix3x2 {
  M11: number;
  M12: number;
  M21: number;
  M22: number;
  M31: number;
  M32: number;

  constructor(m11: number = 1, m12: number = 0, m21: number = 0, m22: number = 1, m31: number = 0, m32: number = 0) {
    this.M11 = m11;
    this.M12 = m12;
    this.M21 = m21;
    this.M22 = m22;
    this.M31 = m31;
    this.M32 = m32;
  }
}

export function identityMatrix(): Matrix3x2 {
  return new Matrix3x2(1, 0, 0, 1, 0, 0);
}

export function matrixFromArray(values: number[]): Matrix3x2 {
  if (values.length < 6) {
    throw new Error('Matrix3x2 needs 6 values');
  }
  return new Matrix3x2(values[0], values[1], values[2], values[3], values[4], values[5]);
}

export function matrixToArray(m: Matrix3x2): number[] {
  return [m.M11, m.M12, m.M21, m.M22, m.M31, m.M32];
}

export function multiplyMatrix(a: Matrix3x2, b: Matrix3x2): Matrix3x2 {
  return new Matrix3x2(
    a.M11 * b.M11 + a.M12 * b.M21,
    a.M11 * b.M12 + a.M12 * b.M22,
    a.M21 * b.M11 + a.M22 * b.M21,
    a.M21 * b.M12 + a.M22 * b.M22,
    a.M31 * b.M11 + a.M32 * b.M21 + b.M31,
    a.M31 * b.M12 + a.M32 * b.M22 + b.M32
  );
}

export function transformPointByMatrix(m: Matrix3x2, x: number, y: number): Point2 {
  return new Point2(x * m.M11 + y * m.M21 + m.M31, x * m.M12 + y * m.M22 + m.M32);
}

export function createTranslationMatrix(x: number, y: number): Matrix3x2 {
  return new Matrix3x2(1, 0, 0, 1, x, y);
}

export function createScaleMatrix(sx: number, sy: number): Matrix3x2 {
  return new Matrix3x2(sx, 0, 0, sy, 0, 0);
}

export function createRotationMatrix(radians: number): Matrix3x2 {
  const c = Math.cos(radians);
  const s = Math.sin(radians);
  return new Matrix3x2(c, s, -s, c, 0, 0);
}

export function invertMatrix(m: Matrix3x2): Matrix3x2 {
  const det = m.M11 * m.M22 - m.M12 * m.M21;
  if (Math.abs(det) < 1e-12) {
    return identityMatrix();
  }
  return new Matrix3x2(
    m.M22 / det,
    -m.M12 / det,
    -m.M21 / det,
    m.M11 / det,
    (m.M21 * m.M32 - m.M22 * m.M31) / det,
    (m.M12 * m.M31 - m.M11 * m.M32) / det
  );
}

export function matrixEquals(a: Matrix3x2, b: Matrix3x2): boolean {
  return a.M11 === b.M11 && a.M12 === b.M12 && a.M21 === b.M21 &&
    a.M22 === b.M22 && a.M31 === b.M31 && a.M32 === b.M32;
}

// ---- 向量入口（JSON 数组形态）----

export function matrixMultiply(a: number[], b: number[]): number[] {
  return matrixToArray(multiplyMatrix(matrixFromArray(a), matrixFromArray(b)));
}

export function transformPoint(m: number[], x: number, y: number): Point2 {
  return transformPointByMatrix(matrixFromArray(m), x, y);
}

export function createRotation(radians: number): number[] {
  return matrixToArray(createRotationMatrix(radians));
}

export function createScale(sx: number, sy: number): number[] {
  return matrixToArray(createScaleMatrix(sx, sy));
}

export function createTranslation(x: number, y: number): number[] {
  return matrixToArray(createTranslationMatrix(x, y));
}

export function invertMatrixValues(m: number[]): number[] {
  return matrixToArray(invertMatrix(matrixFromArray(m)));
}
