// Node 向量运行器专用：把 `@inkcanvas/core` 映射到 core HAR 的纯逻辑导出。
// ArkTS 侧使用 harmony/core/Index.ets（oh-package main），本文件不参与 HAP 构建。
export const MODULE: string = 'core';
export { InkPoint } from '../../harmony/core/src/main/ets/ink/InkPoint';
export { InkColor, packArgb, unpackArgb, withAlphaArgb } from '../../harmony/core/src/main/ets/ink/InkColor';
export { StrokeAttributes, TaperKind, PenNib, strokeAttributesEffective } from '../../harmony/core/src/main/ets/ink/StrokeAttributes';
export {
  effectiveWidths, applyTaperAt, createTaperProfile, TaperProfile,
  NoneTaperProfile, VelocityTaperProfile, TailTaperProfile, SimulatedPressureTaperProfile
} from '../../harmony/core/src/main/ets/ink/TaperProfile';
export { OneEuroFilter } from '../../harmony/core/src/main/ets/ink/OneEuroFilter';
export { toTaperKind, toPenStyle, toEraserRadius, menuCatalogProbe } from '../../harmony/core/src/main/ets/settings/OriginalMenuCatalog';
export { InkStroke, Rect, SelectionFrameState, strokeBounds, setTaperAndWidths, strokeTaperProbe } from '../../harmony/core/src/main/ets/ink/InkStroke';
export type { StrokePointInput, StrokeTaperSampleInput, StrokeTaperProbeResult } from '../../harmony/core/src/main/ets/ink/InkStroke';
export { InkDocument, documentHitTestPoint, documentHitTestRect, documentEraseAt, documentBounds } from '../../harmony/core/src/main/ets/ink/InkDocument';
export type { DocumentStrokeInput, DocumentEraseResult } from '../../harmony/core/src/main/ets/ink/InkDocument';
export { buildSegmentOutline, buildUnionContours, buildOutlineFlat, signedAreaOfFlat } from '../../harmony/core/src/main/ets/ink/StrokeGeometry';
export { eraseDisc, eraseDiscSummary, EraseOutcome } from '../../harmony/core/src/main/ets/ink/StrokeEraser';
export { UndoStack, simulateUndo } from '../../harmony/core/src/main/ets/history/UndoStack';
export {
  Point2, Matrix3x2, identityMatrix, matrixFromArray, matrixToArray, multiplyMatrix,
  transformPointByMatrix, createTranslationMatrix, createScaleMatrix, createRotationMatrix,
  invertMatrix, matrixEquals
} from '../../harmony/core/src/main/ets/math/Matrix3x2';
export {
  SelectionHandle, HANDLE_RADIUS, ROTATE_OFFSET, rotateHandlePositionRect, hitTestRect,
  dragMatrixRect, selectionHitTest, selectionDragMatrix, rotateHandlePosition
} from '../../harmony/core/src/main/ets/selection/SelectionGeometry';
export { TwoFingerManipulator, simulateTwoFinger } from '../../harmony/core/src/main/ets/ink/TwoFingerManipulator';
export { MatrixTransformCenterPointOptions, resolvePivot } from '../../harmony/core/src/main/ets/ink/MatrixTransformCenter';
export { TouchIntent, TouchDecision, measureContactWidth, boundsWidth, classifyContact } from '../../harmony/core/src/main/ets/input/TouchContactClassifier';
export { PalmRejector, OverlayPointerKind, simulatePalm } from '../../harmony/core/src/main/ets/input/PalmRejector';
export { ReplayClock, simulateReplayClock } from '../../harmony/core/src/main/ets/replay/ReplayClock';
export { InkReplaySession, replayVisibleCounts } from '../../harmony/core/src/main/ets/replay/InkReplaySession';
export { blendOver } from '../../harmony/core/src/main/ets/imaging/PixelCompositing';
export { AppTheme, getMainWindowTheme, getBoardTheme } from '../../harmony/core/src/main/ets/theming/ThemeResolver';
export { AppVersion, parseVersion, compareVersions } from '../../harmony/core/src/main/ets/versioning/AppVersion';
export { mayInstallSilently } from '../../harmony/core/src/main/ets/update/UpdatePolicy';
export { ReleaseInfo, parseLatest, parseNewestStable } from '../../harmony/core/src/main/ets/update/ReleaseFeed';
export { parseSettings, stringifySettings, settingsRoundtrip } from '../../harmony/core/src/main/ets/persistence/SettingsCodec';
export { parseIcink, stringifyIcink, icinkRoundtrip } from '../../harmony/core/src/main/ets/persistence/IcinkCodec';
export { parseBoardPack, stringifyBoardPack, boardRoundtrip } from '../../harmony/core/src/main/ets/persistence/BoardPackCodec';
export { parsePptManifest, stringifyPptManifest, pptManifestRoundtrip } from '../../harmony/core/src/main/ets/persistence/PptManifestCodec';
export { ShapeKind, generate, generateCuboidDepth, generateHyperbolaBranches, generateCuboid, isHyperbola, hasHyperbolaFoci, hasHyperbolaAsymptotes, stroke, snapParallelEnd } from '../../harmony/core/src/main/ets/shapes/ShapeGenerator';
export { RecognizedShape, recognize, recognizeStrokes } from '../../harmony/core/src/main/ets/recognition/ShapeRecognizer';
