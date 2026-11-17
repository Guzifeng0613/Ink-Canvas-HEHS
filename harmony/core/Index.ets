export { MODULE } from './IndexConstants';
export { InkPoint } from './src/main/ets/ink/InkPoint';
export { InkColor, packArgb, unpackArgb, withAlphaArgb } from './src/main/ets/ink/InkColor';
export { StrokeAttributes, TaperKind, strokeAttributesEffective } from './src/main/ets/ink/StrokeAttributes';
export { effectiveWidths, applyTaperAt } from './src/main/ets/ink/TaperProfile';
export type { TaperOptionsInput } from './src/main/ets/ink/TaperProfile';
export { InkStroke, Rect } from './src/main/ets/ink/InkStroke';
export { InkDocument } from './src/main/ets/ink/InkDocument';
export {
  buildSegmentOutline,
  buildUnionContours,
  buildOutlineFlat,
  signedAreaOfFlat
} from './src/main/ets/ink/StrokeGeometry';
export { eraseDisc, eraseDiscSummary, EraseOutcome } from './src/main/ets/ink/StrokeEraser';
export type { EraserStrokeInput, EraserSummary, EraserPointInput, EraserFragmentSummary } from './src/main/ets/ink/StrokeEraser';
export { UndoStack, simulateUndo } from './src/main/ets/history/UndoStack';
export type { UndoAction, UndoSimResult } from './src/main/ets/history/UndoStack';
export {
  Point2,
  Matrix3x2,
  identityMatrix,
  matrixFromArray,
  matrixToArray,
  multiplyMatrix,
  transformPointByMatrix,
  createTranslationMatrix,
  createScaleMatrix,
  createRotationMatrix,
  invertMatrix,
  matrixEquals,
  matrixMultiply,
  transformPoint,
  createRotation,
  createScale,
  createTranslation,
  invertMatrixValues
} from './src/main/ets/math/Matrix3x2';
export {
  SelectionHandle,
  HANDLE_RADIUS,
  ROTATE_OFFSET,
  rotateHandlePositionRect,
  hitTestRect,
  dragMatrixRect,
  selectionHitTest,
  selectionDragMatrix,
  rotateHandlePosition
} from './src/main/ets/selection/SelectionGeometry';
export type { BoundsInput } from './src/main/ets/selection/SelectionGeometry';
export { TwoFingerManipulator, simulateTwoFinger } from './src/main/ets/ink/TwoFingerManipulator';
export type { TwoFingerOptionsInput, TwoFingerSample, TwoFingerUpdate, TwoFingerSimResult } from './src/main/ets/ink/TwoFingerManipulator';
export { MatrixTransformCenterPointOptions, resolvePivot } from './src/main/ets/ink/MatrixTransformCenter';
export { createDefaultSettings } from './src/main/ets/settings/AppSettings';
export type {
  AppSettings,
  AdvancedSettings,
  AppearanceSettings,
  AutomationSettings,
  CanvasSettings,
  GestureSettings,
  PowerPointSettings,
  InkToShapeSettings,
  StartupSettings,
  RandSettings
} from './src/main/ets/settings/AppSettings';
export { parseSettings, stringifySettings, settingsRoundtrip } from './src/main/ets/persistence/SettingsCodec';
