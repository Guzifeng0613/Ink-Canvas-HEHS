// @parity src/InkCanvas.Core/Ink/MatrixTransformCenter.cs
import { Point2 } from '../math/Matrix3x2';

export enum MatrixTransformCenterPointOptions {
  CanvasCenterPoint = 0,
  SelectedElementsCenterPoint = 1,
  GestureOperationCenterPoint = 2
}

export function resolvePivot(
  option: number,
  canvasX: number,
  canvasY: number,
  gestureX: number,
  gestureY: number,
  selectedX: number,
  selectedY: number,
  isLoaded: boolean
): Point2 {
  if (!isLoaded) {
    return new Point2(canvasX, canvasY);
  }
  if (option === MatrixTransformCenterPointOptions.GestureOperationCenterPoint) {
    return new Point2(gestureX, gestureY);
  }
  if (option === MatrixTransformCenterPointOptions.SelectedElementsCenterPoint) {
    return new Point2(selectedX, selectedY);
  }
  return new Point2(canvasX, canvasY);
}
