// @parity src/InkCanvas.Core/Settings/AppSettings.cs
// 字段名采用 Settings.json 的 JSON 键名（camelCase/历史大小写原样），便于编解码与互读。
// 枚举（theme/inkStyle/penNib/eraserSize/eraserType/matrixTransformCenterPoint）以数字存储。

export interface AdvancedSettings {
  isSpecialScreen: boolean;
  isQuadIR: boolean;
  touchMultiplier: number;
  nibModeBoundsWidth: number;
  fingerModeBoundsWidth: number;
  nibModeBoundsWidthThresholdValue: number;
  fingerModeBoundsWidthThresholdValue: number;
  nibModeBoundsWidthEraserSize: number;
  fingerModeBoundsWidthEraserSize: number;
  isEnableEdgeGestureUtil: boolean;
  isLogEnabled: boolean;
  isSecondConfimeWhenShutdownApp: boolean;
}

export interface AppearanceSettings {
  isEnableDisPlayFloatBarText: boolean;
  isEnableDisPlayNibModeToggler: boolean;
  isColorfulViewboxFloatingBar: boolean;
  enableViewboxFloatingBarScaleTransform: number;
  enableViewboxBlackBoardScaleTransform: number;
  floatingBarBottomMargin: number;
  isTransparentButtonBackground: boolean;
  isShowExitButton: boolean;
  isShowEraserButton: boolean;
  isShowHideControlButton: boolean;
  isShowLRSwitchButton: boolean;
  isShowModeFingerToggleSwitch: boolean;
  theme: number;
}

export interface CanvasSettings {
  inkWidth: number;
  inkAlpha: number;
  isCompressPicturesUploaded: boolean;
  isShowCursor: boolean;
  inkStyle: number;
  eraserSize: number;
  eraserType: number;
  hideStrokeWhenSelecting: boolean;
  usingWhiteboard: boolean;
  penNib: number;
}

export interface GestureSettings {
  isEnableMultiTouchMode: boolean;
  isEnableTwoFingerZoom: boolean;
  isEnableTwoFingerTranslate: boolean;
  AutoSwitchTwoFingerGesture: boolean;
  isEnableTwoFingerRotation: boolean;
  isEnableTwoFingerRotationOnSelection: boolean;
  matrixTransformCenterPoint: number;
}

export interface PowerPointSettings {
  isShowPPTNavigationBottom: boolean;
  isShowPPTNavigationSides: boolean;
  isShowBottomPPTNavigationPanel: boolean;
  isShowSidePPTNavigationPanel: boolean;
  powerPointSupport: boolean;
  isShowCanvasAtNewSlideShow: boolean;
  isNoClearStrokeOnSelectWhenInPowerPoint: boolean;
  isShowStrokeOnSelectInPowerPoint: boolean;
  isAutoSaveStrokesInPowerPoint: boolean;
  isAutoSaveScreenShotInPowerPoint: boolean;
  isNotifyPreviousPage: boolean;
  isNotifyHiddenPage: boolean;
  isNotifyAutoPlayPresentation: boolean;
  isEnableTwoFingerGestureInPresentationMode: boolean;
  isEnableFingerGestureSlideShowControl: boolean;
  isSupportWPS: boolean;
}

export interface AutomationSettings {
  isAutoFoldInEasiNote: boolean;
  isAutoFoldInEasiNoteIgnoreDesktopAnno: boolean;
  isAutoFoldInEasiCamera: boolean;
  isAutoFoldInEasiNote3C: boolean;
  isAutoFoldInSeewoPincoTeacher: boolean;
  isAutoFoldInHiteTouchPro: boolean;
  isAutoFoldInHiteCamera: boolean;
  isAutoFoldInWxBoardMain: boolean;
  isAutoFoldInOldZyBoard: boolean;
  isAutoFoldInMSWhiteboard: boolean;
  isAutoFoldInPPTSlideShow: boolean;
  isAutoKillPptService: boolean;
  isAutoKillEasiNote: boolean;
  isSaveScreenshotsInDateFolders: boolean;
  isAutoSaveStrokesAtScreenshot: boolean;
  isAutoSaveStrokesAtClear: boolean;
  isAutoClearWhenExitingWritingMode: boolean;
  minimumAutomationStrokeNumber: number;
  autoSavedStrokesLocation: string;
  autoDelSavedFiles: boolean;
  autoDelSavedFilesDaysThreshold: number;
}

export interface InkToShapeSettings {
  isInkToShapeEnabled: boolean;
}

export interface StartupSettings {
  isAutoUpdate: boolean;
  isAutoUpdateWithSilence: boolean;
  isAutoUpdateWithSilenceStartTime: string;
  isAutoUpdateWithSilenceEndTime: string;
  isEnableNibMode: boolean;
  isFoldAtStartup: boolean;
}

export interface RandSettings {
  peopleCount: number;
  isNotRepeatName: boolean;
}

export interface AppSettings {
  advanced: AdvancedSettings;
  appearance: AppearanceSettings;
  automation: AutomationSettings;
  behavior: PowerPointSettings;
  canvas: CanvasSettings;
  gesture: GestureSettings;
  inkToShape: InkToShapeSettings;
  startup: StartupSettings;
  randSettings: RandSettings;
}

export function createDefaultSettings(): AppSettings {
  return {
    advanced: {
      isSpecialScreen: false,
      isQuadIR: false,
      touchMultiplier: 0.25,
      nibModeBoundsWidth: 10,
      fingerModeBoundsWidth: 30,
      nibModeBoundsWidthThresholdValue: 2.5,
      fingerModeBoundsWidthThresholdValue: 2.5,
      nibModeBoundsWidthEraserSize: 0.8,
      fingerModeBoundsWidthEraserSize: 0.8,
      isEnableEdgeGestureUtil: false,
      isLogEnabled: true,
      isSecondConfimeWhenShutdownApp: false
    },
    appearance: {
      isEnableDisPlayFloatBarText: false,
      isEnableDisPlayNibModeToggler: true,
      isColorfulViewboxFloatingBar: false,
      enableViewboxFloatingBarScaleTransform: 100.0,
      enableViewboxBlackBoardScaleTransform: 100.0,
      floatingBarBottomMargin: 100.0,
      isTransparentButtonBackground: true,
      isShowExitButton: true,
      isShowEraserButton: true,
      isShowHideControlButton: false,
      isShowLRSwitchButton: false,
      isShowModeFingerToggleSwitch: true,
      theme: 0
    },
    automation: {
      isAutoFoldInEasiNote: false,
      isAutoFoldInEasiNoteIgnoreDesktopAnno: false,
      isAutoFoldInEasiCamera: false,
      isAutoFoldInEasiNote3C: false,
      isAutoFoldInSeewoPincoTeacher: false,
      isAutoFoldInHiteTouchPro: false,
      isAutoFoldInHiteCamera: false,
      isAutoFoldInWxBoardMain: false,
      isAutoFoldInOldZyBoard: false,
      isAutoFoldInMSWhiteboard: false,
      isAutoFoldInPPTSlideShow: false,
      isAutoKillPptService: false,
      isAutoKillEasiNote: false,
      isSaveScreenshotsInDateFolders: false,
      isAutoSaveStrokesAtScreenshot: false,
      isAutoSaveStrokesAtClear: false,
      isAutoClearWhenExitingWritingMode: false,
      minimumAutomationStrokeNumber: 0,
      autoSavedStrokesLocation: 'D:\\Ink Canvas',
      autoDelSavedFiles: false,
      autoDelSavedFilesDaysThreshold: 15
    },
    behavior: {
      isShowPPTNavigationBottom: true,
      isShowPPTNavigationSides: true,
      isShowBottomPPTNavigationPanel: true,
      isShowSidePPTNavigationPanel: true,
      powerPointSupport: true,
      isShowCanvasAtNewSlideShow: true,
      isNoClearStrokeOnSelectWhenInPowerPoint: true,
      isShowStrokeOnSelectInPowerPoint: false,
      isAutoSaveStrokesInPowerPoint: true,
      isAutoSaveScreenShotInPowerPoint: false,
      isNotifyPreviousPage: false,
      isNotifyHiddenPage: true,
      isNotifyAutoPlayPresentation: true,
      isEnableTwoFingerGestureInPresentationMode: false,
      isEnableFingerGestureSlideShowControl: true,
      isSupportWPS: true
    },
    canvas: {
      inkWidth: 2.5,
      inkAlpha: 140,
      isCompressPicturesUploaded: false,
      isShowCursor: false,
      inkStyle: 3,
      eraserSize: 2,
      eraserType: 0,
      hideStrokeWhenSelecting: true,
      usingWhiteboard: false,
      penNib: 0
    },
    gesture: {
      isEnableMultiTouchMode: true,
      isEnableTwoFingerZoom: true,
      isEnableTwoFingerTranslate: true,
      AutoSwitchTwoFingerGesture: true,
      isEnableTwoFingerRotation: false,
      isEnableTwoFingerRotationOnSelection: false,
      matrixTransformCenterPoint: 0
    },
    inkToShape: {
      isInkToShapeEnabled: true
    },
    startup: {
      isAutoUpdate: true,
      isAutoUpdateWithSilence: false,
      isAutoUpdateWithSilenceStartTime: '00:00',
      isAutoUpdateWithSilenceEndTime: '00:00',
      isEnableNibMode: false,
      isFoldAtStartup: false
    },
    randSettings: {
      peopleCount: 60,
      isNotRepeatName: false
    }
  };
}
