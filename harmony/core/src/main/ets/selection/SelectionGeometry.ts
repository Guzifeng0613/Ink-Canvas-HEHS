// @parity src/InkCanvas.Core/Selection/SelectionGeometry.cs
import { Rect } from '../ink/InkStroke';
import {
  Matrix3x2,
  identityMatrix,
  multiplyMatrix,
  createTranslationMatrix,
  createScaleMatrix,
  createRotationMatrix,
  matrixToArray
} from '../math/Matrix3x2';
import { Point2 } from '../math/Matrix3x2';

export enum SelectionHandle {
  None = 0,
  Move = 1,
  TopLeft = 2,
  TopRight = 3,
  BottomRight = 4,
  BottomLeft = 5,
  Rotate = 6,
  Top = 7,
  Right = 8,
  Bottom = 9,
  Left = 10
}

export const HANDLE_RADIUS = 12;
export const ROTATE_OFFSET = 28;
const MIN_SCALE = 0.05;
const MAX_SCALE = 20;

export interface BoundsInput {
  Left: number;
  Top: number;
  Right: number;
  Bottom: number;
}

function toRect(bounds: BoundsInput): Rect {
  return new Rect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
}

export function rotateHandlePositionRect(bounds: Rect): Point2 {
  return new Point2(bounds.centerX(), bounds.Top - ROTATE_OFFSET);
}

function distanceSquared(ax: number, ay: number, bx: number, by: number): number {
  const dx = ax - bx;
  const dy = ay - by;
  return dx * dx + dy * dy;
}

export function hitTestRect(bounds: Rect, x: number, y: number): SelectionHandle {
  if (bounds.isEmpty()) {
    return SelectionHandle.None;
  }
  const rotate = rotateHandlePositionRect(bounds);
  if (distanceSquared(x, y, rotate.X, rotate.Y) <= HANDLE_RADIUS * HANDLE_RADIUS) {
    return SelectionHandle.Rotate;
  }
  if (distanceSquared(x, y, bounds.Left, bounds.Top) <= HANDLE_RADIUS * HANDLE_RADIUS) {
    return SelectionHandle.TopLeft;
  }
  if (distanceSquared(x, y, bounds.Right, bounds.Top) <= HANDLE_RADIUS * HANDLE_RADIUS) {
    return SelectionHandle.TopRight;
  }
  if (distanceSquared(x, y, bounds.Right, bounds.Bottom) <= HANDLE_RADIUS * HANDLE_RADIUS) {
    return SelectionHandle.BottomRight;
  }
  if (distanceSquared(x, y, bounds.Left, bounds.Bottom) <= HANDLE_RADIUS * HANDLE_RADIUS) {
    return SelectionHandle.BottomLeft;
  }
  return bounds.contains(x, y) ? SelectionHandle.Move : SelectionHandle.None;
}

function ratio(from: number, to: number): number {
  if (Math.abs(from) < 1e-3) {
    return 1;
  }
  return Math.min(Math.max(to / from, MIN_SCALE), MAX_SCALE);
}

function about(pivotX: number, pivotY: number, m: Matrix3x2): Matrix3x2 {
  return multiplyMatrix(
    multiplyMatrix(createTranslationMatrix(-pivotX, -pivotY), m),
    createTranslationMatrix(pivotX, pivotY)
  );
}

function oppositeCornerX(handle: SelectionHandle, b: Rect): number {
  if (handle === SelectionHandle.TopLeft || handle === SelectionHandle.BottomLeft) {
    return b.Right;
  }
  if (handle === SelectionHandle.TopRight || handle === SelectionHandle.BottomRight) {
    return b.Left;
  }
  if (handle === SelectionHandle.Top || handle === SelectionHandle.Bottom) {
    return b.centerX();
  }
  if (handle === SelectionHandle.Left) {
    return b.Right;
  }
  if (handle === SelectionHandle.Right) {
    return b.Left;
  }
  return b.centerX();
}

function oppositeCornerY(handle: SelectionHandle, b: Rect): number {
  if (handle === SelectionHandle.TopLeft || handle === SelectionHandle.TopRight) {
    return b.Bottom;
  }
  if (handle === SelectionHandle.BottomLeft || handle === SelectionHandle.BottomRight) {
    return b.Top;
  }
  if (handle === SelectionHandle.Left || handle === SelectionHandle.Right) {
    return b.centerY();
  }
  if (handle === SelectionHandle.Top) {
    return b.Bottom;
  }
  if (handle === SelectionHandle.Bottom) {
    return b.Top;
  }
  return b.centerY();
}

export function dragMatrixRect(
  handle: SelectionHandle,
  bounds: Rect,
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  keepAspect: boolean
): Matrix3x2 {
  if (handle === SelectionHandle.None) {
    return identityMatrix();
  }
  if (handle === SelectionHandle.Move) {
    return createTranslationMatrix(toX - fromX, toY - fromY);
  }
  if (handle === SelectionHandle.Rotate) {
    const cx = bounds.centerX();
    const cy = bounds.centerY();
    const ax = fromX - cx;
    const ay = fromY - cy;
    const bx = toX - cx;
    const by = toY - cy;
    if (ax * ax + ay * ay < 1e-4 || bx * bx + by * by < 1e-4) {
      return identityMatrix();
    }
    const angle = Math.atan2(by, bx) - Math.atan2(ay, ax);
    return about(cx, cy, createRotationMatrix(angle));
  }

  const anchorX = oppositeCornerX(handle, bounds);
  const anchorY = oppositeCornerY(handle, bounds);
  let sx = ratio(fromX - anchorX, toX - anchorX);
  let sy = ratio(fromY - anchorY, toY - anchorY);
  if (handle === SelectionHandle.Top || handle === SelectionHandle.Bottom) {
    sx = 1;
  }
  if (handle === SelectionHandle.Left || handle === SelectionHandle.Right) {
    sy = 1;
  }
  const isEdge = handle === SelectionHandle.Top || handle === SelectionHandle.Bottom ||
    handle === SelectionHandle.Left || handle === SelectionHandle.Right;
  if (keepAspect && !isEdge) {
    const uniform = Math.max(sx, sy);
    sx = uniform;
    sy = uniform;
  }
  return about(anchorX, anchorY, createScaleMatrix(sx, sy));
}

// ---- 向量入口（JSON 普通对象）----

export function selectionHitTest(bounds: BoundsInput, x: number, y: number): number {
  return hitTestRect(toRect(bounds), x, y);
}

export function selectionDragMatrix(
  handle: number,
  bounds: BoundsInput,
  fromX: number,
  fromY: number,
  toX: number,
  toY: number,
  keepAspect: boolean
): number[] {
  return matrixToArray(dragMatrixRect(handle as SelectionHandle, toRect(bounds), fromX, fromY, toX, toY, keepAspect));
}

export function rotateHandlePosition(bounds: BoundsInput): Point2 {
  return rotateHandlePositionRect(toRect(bounds));
}

// @parity src/InkCanvas.Core/Selection/SelectionFrame.cs（VisibleResizeHandles）
// G14：短/薄对象省略重叠的边中点柄（保持主体可拖）。宽/高为框经 Transform*view 的
// 线性变换后的实际尺寸。规则：Top/Bottom 在 width<48 或 height<24 时省略；
// Left/Right 在 height<48 或 width<24 时省略；角柄与旋转柄始终显示。
export function visibleResizeHandles(
  width: number,
  height: number
): number[] {
  const result: number[] = [];
  result.push(SelectionHandle.TopLeft);
  result.push(SelectionHandle.TopRight);
  result.push(SelectionHandle.BottomRight);
  result.push(SelectionHandle.BottomLeft);
  result.push(SelectionHandle.Rotate);
  if (!(width < 48 || height < 24)) {
    result.push(SelectionHandle.Top);
    result.push(SelectionHandle.Bottom);
  }
  if (!(height < 48 || width < 24)) {
    result.push(SelectionHandle.Left);
    result.push(SelectionHandle.Right);
  }
  return result;
}

// 确定性向量入口：框尺寸 + 变换 → 可见柄集合（变换后的法向长度）。
export function visibleHandlesForFrame(
  boundsWidth: number,
  boundsHeight: number,
  m11: number, m12: number, m21: number, m22: number
): number[] {
  const width = Math.sqrt(m11 * boundsWidth * (m11 * boundsWidth) + m12 * boundsWidth * (m12 * boundsWidth));
  const height = Math.sqrt(m21 * boundsHeight * (m21 * boundsHeight) + m22 * boundsHeight * (m22 * boundsHeight));
  return visibleResizeHandles(width, height).sort((a: number, b: number): number => a - b);
}
