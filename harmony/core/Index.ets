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
