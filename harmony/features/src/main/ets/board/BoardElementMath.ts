// @parity src/InkCanvas.Features/Board/BoardService.cs (BoardElementMath)
import {
  Matrix3x2,
  identityMatrix,
  matrixFromArray,
  matrixToArray,
  multiplyMatrix,
  transformPointByMatrix
} from '@inkcanvas/core';

export function readTransform(values: number[] | null): Matrix3x2 {
  if (values !== null && values.length === 6) {
    return matrixFromArray(values);
  }
  return identityMatrix();
}

export function readTransformArray(values: number[] | null): number[] {
  return matrixToArray(readTransform(values));
}

export function writeTransform(m: Matrix3x2): number[] {
  return matrixToArray(m);
}

// WPF TransformGroup.Children.Add：新矩阵作用在既有矩阵之后（行向量 Read(existing) * m）。
export function appendTransform(existing: number[] | null, m: number[]): number[] {
  return matrixToArray(multiplyMatrix(readTransform(existing), matrixFromArray(m)));
}

export function sameTransform(a: number[] | null, b: number[] | null): boolean {
  if (a === null || b === null) {
    return a === null && b === null;
  }
  if (a.length !== b.length) {
    return false;
  }
  for (let i = 0; i < a.length; i++) {
    if (Math.abs(a[i] - b[i]) > 1e-4) {
      return false;
    }
  }
  return true;
}

export function transformedBounds(
  left: number,
  top: number,
  width: number,
  height: number,
  transform: number[] | null
): number[] {
  const m = readTransform(transform);
  const corners: number[][] = [
    transformPointByMatrix(m, left, top),
    transformPointByMatrix(m, left + width, top),
    transformPointByMatrix(m, left + width, top + height),
    transformPointByMatrix(m, left, top + height)
  ].map((p: { X: number; Y: number }): number[] => [p.X, p.Y]);
  let minX = corners[0][0];
  let minY = corners[0][1];
  let maxX = minX;
  let maxY = minY;
  for (let i = 1; i < corners.length; i++) {
    minX = Math.min(minX, corners[i][0]);
    minY = Math.min(minY, corners[i][1]);
    maxX = Math.max(maxX, corners[i][0]);
    maxY = Math.max(maxY, corners[i][1]);
  }
  return [minX, minY, maxX, maxY];
}

export function renderMatrix(left: number, top: number, transform: number[] | null): number[] {
  const m = readTransform(transform);
  const t = transformPointByMatrix(m, left, top);
  return [m.M11, m.M12, m.M21, m.M22, t.X - left, t.Y - top];
}

// ArkUI .transform() 默认以组件中心 (50%,50%) 为锚点；C# BoardImageLayer 的
// RenderTransformOrigin 为左上角 (0,0)。把左上原点的 renderMatrix 共轭到中心锚点：
// 平移分量 = m6 * center - center，线性部分不变。输出列主序 4x4（matrix4.init 入参）。
export function renderMatrix4Values(
  left: number,
  top: number,
  width: number,
  height: number,
  transform: number[] | null
): number[] {
  const m6 = renderMatrix(left, top, transform);
  const cx = left + width / 2;
  const cy = top + height / 2;
  const tc = transformPointByMatrix(matrixFromArray(m6), cx, cy);
  const dx = tc.X - cx;
  const dy = tc.Y - cy;
  return [
    m6[0], m6[1], 0, 0,
    m6[2], m6[3], 0, 0,
    0, 0, 1, 0,
    dx, dy, 0, 1
  ];
}
