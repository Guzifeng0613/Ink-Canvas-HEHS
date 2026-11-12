using System.Text.Json;
using System.Text.Json.Serialization;

namespace InkCanvas.Core.Settings;

/// <summary>
/// Application settings. JSON property names are kept identical to the WPF original so an
/// existing <c>Settings.json</c> loads unchanged. Serialised with System.Text.Json instead of
/// Newtonsoft (one less dependency); the three former public fields are now properties with
/// the same JSON names, which is wire-compatible.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("advanced")] public AdvancedSettings Advanced { get; set; } = new();
    [JsonPropertyName("appearance")] public AppearanceSettings Appearance { get; set; } = new();
    [JsonPropertyName("automation")] public AutomationSettings Automation { get; set; } = new();
    [JsonPropertyName("behavior")] public PowerPointSettings PowerPoint { get; set; } = new();
    [JsonPropertyName("canvas")] public CanvasSettings Canvas { get; set; } = new();
    [JsonPropertyName("gesture")] public GestureSettings Gesture { get; set; } = new();
    [JsonPropertyName("inkToShape")] public InkToShapeSettings InkToShape { get; set; } = new();
    [JsonPropertyName("startup")] public StartupSettings Startup { get; set; } = new();
    [JsonPropertyName("randSettings")] public RandSettings Rand { get; set; } = new();

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this)) ?? new AppSettings();
}

public enum OptionalOperation { Yes, No, Ask }

public sealed class CanvasSettings
{
    [JsonPropertyName("inkWidth")] public double InkWidth { get; set; } = 2.5;
    [JsonPropertyName("inkAlpha")] public int InkAlpha { get; set; } = 140;
    [JsonPropertyName("isCompressPicturesUploaded")] public bool IsCompressPicturesUploaded { get; set; }
    [JsonPropertyName("isShowCursor")] public bool IsShowCursor { get; set; }

    /// <summary>0 = nib taper, 1 = velocity taper, 2 = uniform. Maps to <see cref="Ink.TaperKind"/>.</summary>
    [JsonPropertyName("inkStyle")] public int InkStyle { get; set; }

    [JsonPropertyName("eraserSize")] public int EraserSize { get; set; } = 2;

    /// <summary>0 = toggle by icon, 1 = area eraser, 2 = stroke eraser.</summary>
    [JsonPropertyName("eraserType")] public int EraserType { get; set; }

    [JsonPropertyName("hideStrokeWhenSelecting")] public bool HideStrokeWhenSelecting { get; set; } = true;
    [JsonPropertyName("usingWhiteboard")] public bool UsingWhiteboard { get; set; }
    [JsonPropertyName("hyperbolaAsymptoteOption")] public OptionalOperation HyperbolaAsymptoteOption { get; set; } = OptionalOperation.Ask;
}

public enum MatrixTransformCenterPointOptions
{
    CanvasCenterPoint,
    SelectedElementsCenterPoint,
    GestureOperationCenterPoint,
}

public sealed class GestureSettings
{
    [JsonPropertyName("isEnableMultiTouchMode")] public bool IsEnableMultiTouchMode { get; set; } = true;
    [JsonPropertyName("isEnableTwoFingerZoom")] public bool IsEnableTwoFingerZoom { get; set; } = true;
    [JsonPropertyName("isEnableTwoFingerTranslate")] public bool IsEnableTwoFingerTranslate { get; set; } = true;
    [JsonPropertyName("AutoSwitchTwoFingerGesture")] public bool AutoSwitchTwoFingerGesture { get; set; } = true;
    [JsonPropertyName("isEnableTwoFingerRotation")] public bool IsEnableTwoFingerRotation { get; set; }
    [JsonPropertyName("isEnableTwoFingerRotationOnSelection")] public bool IsEnableTwoFingerRotationOnSelection { get; set; }
    [JsonPropertyName("matrixTransformCenterPoint")] public MatrixTransformCenterPointOptions MatrixTransformCenterPoint { get; set; } = MatrixTransformCenterPointOptions.CanvasCenterPoint;

    [JsonIgnore]
    public bool IsEnableTwoFingerGesture => IsEnableTwoFingerZoom || IsEnableTwoFingerTranslate || IsEnableTwoFingerRotation;

    [JsonIgnore]
    public bool IsEnableTwoFingerGestureTranslateOrRotation => IsEnableTwoFingerTranslate || IsEnableTwoFingerRotation;
}

public sealed class StartupSettings
{
    [JsonPropertyName("isAutoUpdate")] public bool IsAutoUpdate { get; set; } = true;
    [JsonPropertyName("isAutoUpdateWithSilence")] public bool IsAutoUpdateWithSilence { get; set; }
    [JsonPropertyName("isAutoUpdateWithSilenceStartTime")] public string AutoUpdateWithSilenceStartTime { get; set; } = "00:00";
    [JsonPropertyName("isAutoUpdateWithSilenceEndTime")] public string AutoUpdateWithSilenceEndTime { get; set; } = "00:00";
    [JsonPropertyName("isEnableNibMode")] public bool IsEnableNibMode { get; set; }
    [JsonPropertyName("isFoldAtStartup")] public bool IsFoldAtStartup { get; set; }
}

public sealed class AppearanceSettings
{
    [JsonPropertyName("isEnableDisPlayFloatBarText")] public bool IsEnableDisPlayFloatBarText { get; set; }
    [JsonPropertyName("isEnableDisPlayNibModeToggler")] public bool IsEnableDisPlayNibModeToggler { get; set; } = true;
    [JsonPropertyName("isColorfulViewboxFloatingBar")] public bool IsColorfulViewboxFloatingBar { get; set; }
    [JsonPropertyName("enableViewboxFloatingBarScaleTransform")] public double FloatingBarScale { get; set; } = 100.0;
    [JsonPropertyName("enableViewboxBlackBoardScaleTransform")] public double BlackboardScale { get; set; } = 100.0;
    [JsonPropertyName("floatingBarBottomMargin")] public double FloatingBarBottomMargin { get; set; } = 100.0;
    [JsonPropertyName("isTransparentButtonBackground")] public bool IsTransparentButtonBackground { get; set; } = true;
    [JsonPropertyName("isShowExitButton")] public bool IsShowExitButton { get; set; } = true;
    [JsonPropertyName("isShowEraserButton")] public bool IsShowEraserButton { get; set; } = true;
    [JsonPropertyName("isShowHideControlButton")] public bool IsShowHideControlButton { get; set; }
    [JsonPropertyName("isShowLRSwitchButton")] public bool IsShowLRSwitchButton { get; set; }
    [JsonPropertyName("isShowModeFingerToggleSwitch")] public bool IsShowModeFingerToggleSwitch { get; set; } = true;

    /// <summary>0 = follow system, 1 = light, 2 = dark.</summary>
    [JsonPropertyName("theme")] public int Theme { get; set; }
}

public sealed class PowerPointSettings
{
    [JsonPropertyName("isShowPPTNavigationBottom")] public bool IsShowPPTNavigationBottom { get; set; } = true;
    [JsonPropertyName("isShowPPTNavigationSides")] public bool IsShowPPTNavigationSides { get; set; } = true;
    [JsonPropertyName("isShowBottomPPTNavigationPanel")] public bool IsShowBottomPPTNavigationPanel { get; set; } = true;
    [JsonPropertyName("isShowSidePPTNavigationPanel")] public bool IsShowSidePPTNavigationPanel { get; set; } = true;
    [JsonPropertyName("powerPointSupport")] public bool PowerPointSupport { get; set; } = true;
    [JsonPropertyName("isShowCanvasAtNewSlideShow")] public bool IsShowCanvasAtNewSlideShow { get; set; } = true;
    [JsonPropertyName("isNoClearStrokeOnSelectWhenInPowerPoint")] public bool IsNoClearStrokeOnSelectWhenInPowerPoint { get; set; } = true;
    [JsonPropertyName("isShowStrokeOnSelectInPowerPoint")] public bool IsShowStrokeOnSelectInPowerPoint { get; set; }
    [JsonPropertyName("isAutoSaveStrokesInPowerPoint")] public bool IsAutoSaveStrokesInPowerPoint { get; set; } = true;
    [JsonPropertyName("isAutoSaveScreenShotInPowerPoint")] public bool IsAutoSaveScreenShotInPowerPoint { get; set; }
    [JsonPropertyName("isNotifyPreviousPage")] public bool IsNotifyPreviousPage { get; set; }
    [JsonPropertyName("isNotifyHiddenPage")] public bool IsNotifyHiddenPage { get; set; } = true;
    [JsonPropertyName("isNotifyAutoPlayPresentation")] public bool IsNotifyAutoPlayPresentation { get; set; } = true;
    [JsonPropertyName("isEnableTwoFingerGestureInPresentationMode")] public bool IsEnableTwoFingerGestureInPresentationMode { get; set; }
    [JsonPropertyName("isEnableFingerGestureSlideShowControl")] public bool IsEnableFingerGestureSlideShowControl { get; set; } = true;
    [JsonPropertyName("isSupportWPS")] public bool IsSupportWPS { get; set; } = true;
}

public sealed class AutomationSettings
{
    [JsonPropertyName("isAutoFoldInEasiNote")] public bool IsAutoFoldInEasiNote { get; set; }
    [JsonPropertyName("isAutoFoldInEasiNoteIgnoreDesktopAnno")] public bool IsAutoFoldInEasiNoteIgnoreDesktopAnno { get; set; }
    [JsonPropertyName("isAutoFoldInEasiCamera")] public bool IsAutoFoldInEasiCamera { get; set; }
    [JsonPropertyName("isAutoFoldInEasiNote3C")] public bool IsAutoFoldInEasiNote3C { get; set; }
    [JsonPropertyName("isAutoFoldInSeewoPincoTeacher")] public bool IsAutoFoldInSeewoPincoTeacher { get; set; }
    [JsonPropertyName("isAutoFoldInHiteTouchPro")] public bool IsAutoFoldInHiteTouchPro { get; set; }
    [JsonPropertyName("isAutoFoldInHiteCamera")] public bool IsAutoFoldInHiteCamera { get; set; }
    [JsonPropertyName("isAutoFoldInWxBoardMain")] public bool IsAutoFoldInWxBoardMain { get; set; }
    [JsonPropertyName("isAutoFoldInOldZyBoard")] public bool IsAutoFoldInOldZyBoard { get; set; }
    [JsonPropertyName("isAutoFoldInMSWhiteboard")] public bool IsAutoFoldInMSWhiteboard { get; set; }
    [JsonPropertyName("isAutoFoldInPPTSlideShow")] public bool IsAutoFoldInPPTSlideShow { get; set; }
    [JsonPropertyName("isAutoKillPptService")] public bool IsAutoKillPptService { get; set; }
    [JsonPropertyName("isAutoKillEasiNote")] public bool IsAutoKillEasiNote { get; set; }
    [JsonPropertyName("isSaveScreenshotsInDateFolders")] public bool IsSaveScreenshotsInDateFolders { get; set; }
    [JsonPropertyName("isAutoSaveStrokesAtScreenshot")] public bool IsAutoSaveStrokesAtScreenshot { get; set; }
    [JsonPropertyName("isAutoSaveStrokesAtClear")] public bool IsAutoSaveStrokesAtClear { get; set; }
    [JsonPropertyName("isAutoClearWhenExitingWritingMode")] public bool IsAutoClearWhenExitingWritingMode { get; set; }
    [JsonPropertyName("minimumAutomationStrokeNumber")] public int MinimumAutomationStrokeNumber { get; set; }
    [JsonPropertyName("autoSavedStrokesLocation")] public string AutoSavedStrokesLocation { get; set; } = @"D:\Ink Canvas";
    [JsonPropertyName("autoDelSavedFiles")] public bool AutoDelSavedFiles { get; set; }
    [JsonPropertyName("autoDelSavedFilesDaysThreshold")] public int AutoDelSavedFilesDaysThreshold { get; set; } = 15;

    [JsonIgnore]
    public bool IsEnableAutoFold =>
        IsAutoFoldInEasiNote || IsAutoFoldInEasiCamera || IsAutoFoldInEasiNote3C ||
        IsAutoFoldInSeewoPincoTeacher || IsAutoFoldInHiteTouchPro || IsAutoFoldInHiteCamera ||
        IsAutoFoldInWxBoardMain || IsAutoFoldInOldZyBoard || IsAutoFoldInPPTSlideShow ||
        IsAutoFoldInMSWhiteboard;
}

public sealed class AdvancedSettings
{
    [JsonPropertyName("isSpecialScreen")] public bool IsSpecialScreen { get; set; }
    [JsonPropertyName("isQuadIR")] public bool IsQuadIR { get; set; }
    [JsonPropertyName("touchMultiplier")] public double TouchMultiplier { get; set; } = 0.25;
    [JsonPropertyName("nibModeBoundsWidth")] public int NibModeBoundsWidth { get; set; } = 10;
    [JsonPropertyName("fingerModeBoundsWidth")] public int FingerModeBoundsWidth { get; set; } = 30;
    [JsonPropertyName("nibModeBoundsWidthThresholdValue")] public double NibModeBoundsWidthThresholdValue { get; set; } = 2.5;
    [JsonPropertyName("fingerModeBoundsWidthThresholdValue")] public double FingerModeBoundsWidthThresholdValue { get; set; } = 2.5;
    [JsonPropertyName("nibModeBoundsWidthEraserSize")] public double NibModeBoundsWidthEraserSize { get; set; } = 0.8;
    [JsonPropertyName("fingerModeBoundsWidthEraserSize")] public double FingerModeBoundsWidthEraserSize { get; set; } = 0.8;
    [JsonPropertyName("isEnableEdgeGestureUtil")] public bool IsEnableEdgeGestureUtil { get; set; }
    [JsonPropertyName("isLogEnabled")] public bool IsLogEnabled { get; set; } = true;
    [JsonPropertyName("isSecondConfimeWhenShutdownApp")] public bool IsSecondConfimeWhenShutdownApp { get; set; }
}

public sealed class InkToShapeSettings
{
    [JsonPropertyName("isInkToShapeEnabled")] public bool IsInkToShapeEnabled { get; set; } = true;
}

public sealed class RandSettings
{
    [JsonPropertyName("peopleCount")] public int PeopleCount { get; set; } = 60;
    [JsonPropertyName("isNotRepeatName")] public bool IsNotRepeatName { get; set; }
}