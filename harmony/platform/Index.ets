export const MODULE: string = 'platform';
export {
  SYSCAP_ARKUI_FULL,
  SYSCAP_STYLUS_HANDWRITE,
  SYSCAP_STYLUS_SERVICE,
  SYSCAP_INPUT_POINTER,
  capabilityMatrix,
  sdkApiVersion,
  isApi26,
  hasSysCap,
  supportsStylusBoost,
  supportsSystemMaterial,
  supportsSymbolGlyph,
  systemMotionReduced
} from './src/main/ets/compat/Capabilities';
export type { CapabilityMatrix } from './src/main/ets/compat/CapabilityMatrix';
export { breakpointForWidth, isFullLayout } from './src/main/ets/device/Breakpoints';
export type { Breakpoint } from './src/main/ets/device/Breakpoints';
export { deviceType, is2in1, isTablet, isPhone, isFullDevice } from './src/main/ets/device/DeviceService';
export { StorageService, describeError } from './src/main/ets/storage/StorageService';
export { logInfo, logWarn, logError, setLogEnabled } from './src/main/ets/diagnostics/LogService';
export { PointerKind, normalizePointer, pointerKind, isMouseEvent } from './src/main/ets/input/InputAdapter';
export type { RawTouchEvent, NormalizedPointer } from './src/main/ets/input/InputAdapter';
export { floatingBarPosition, clampToWindow, cornerPanelPosition } from './src/main/ets/layout/WindowLayout';
export type { PointResult } from './src/main/ets/layout/WindowLayout';
export { CanvasInkRenderer } from './src/main/ets/rendering/CanvasInkRenderer';
export { InkCanvasSurface } from './src/main/ets/rendering/InkCanvasSurface';
export { PdfPageSource } from './src/main/ets/pdf/PdfPageSource';
export { AutoSaveService } from './src/main/ets/persistence/AutoSaveService';
export { captureComponentToPng } from './src/main/ets/persistence/SnapshotService';
export { openExternalUrl } from './src/main/ets/system/SystemServices';
