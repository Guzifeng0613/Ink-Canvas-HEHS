export { MODULE } from './IndexConstants';
export { InkPoint } from './src/main/ets/ink/InkPoint';
export { InkColor, packArgb, unpackArgb, withAlphaArgb } from './src/main/ets/ink/InkColor';
export { StrokeAttributes, TaperKind, strokeAttributesEffective } from './src/main/ets/ink/StrokeAttributes';
export { InkStroke, Rect } from './src/main/ets/ink/InkStroke';
export { InkDocument } from './src/main/ets/ink/InkDocument';
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
