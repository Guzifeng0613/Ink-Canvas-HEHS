using InkCanvas.Core.Settings;

namespace InkCanvas.Core.Input;

/// <summary>What a touch contact should do, mirroring the original InkCanvasEditingMode choice.</summary>
public enum TouchIntent
{
    /// <summary>InkCanvasEditingMode.None — the contact draws.</summary>
    Draw,

    /// <summary>InkCanvasEditingMode.EraseByStroke.</summary>
    EraseByStroke,

    /// <summary>InkCanvasEditingMode.EraseByPoint (area eraser).</summary>
    EraseByPoint,

    /// <summary>InkCanvasEditingMode.GestureOnly — finger flips slides in a slideshow.</summary>
    GestureOnly,

    /// <summary>The original returned early without changing mode.</summary>
    Ignore,
}

public readonly record struct TouchDecision(TouchIntent Intent, double EraserDiameter);

/// <summary>
/// 1:1 port of the touch-contact classification in the WPF original
/// (<c>MW_TouchEvents.cs</c>: <c>MainWindow_TouchDown</c> / <c>Main_Grid_TouchDown</c>,
/// plus <c>GetTouchBoundWidth</c> and the <c>BoundsWidth</c> assignment in <c>MW_Settings.cs</c>).
/// <para>
/// Covers the special-screen behaviour that was previously left unported: four-sided IR
/// panels (<c>IsQuadIR</c>), the contact-size multiplier (<c>TouchMultiplier</c>), and the
/// separate nib/finger bounds, thresholds and eraser sizes.
/// </para>
/// <para>
/// <b>未验证</b>：按要求跳过行为验证 —— 没有单元测试，也没有在真实红外屏/压感笔上实机验证。
/// 逻辑按原实现逐条对应，但阈值手感需在实际希沃设备上复核。
/// </para>
/// <para>
/// <b>刻意未移植</b>：原 <c>Main_Grid_TouchDown</c> 里的 <c>inkCanvas.Opacity = 1</c>
/// （以及配套的 0.1 压暗）没有搬过来 —— 那正是笔锋闪烁的第二个来源，本次迁移已修掉。
/// </para>
/// </summary>
public static class TouchContactClassifier
{
    /// <summary>
    /// 1:1 <c>GetTouchBoundWidth</c>: four-sided IR panels report a contact rectangle whose
    /// width alone is misleading, so the original uses the geometric mean of width and height.
    /// </summary>
    public static double MeasureContactWidth(double width, double height, bool isQuadIr)
        => isQuadIr ? Math.Sqrt(width * height) : width;

    /// <summary>1:1 the <c>BoundsWidth</c> field: nib mode and finger mode use separate bounds.</summary>
    public static double BoundsWidth(AdvancedSettings advanced, bool nibMode)
        => nibMode ? advanced.NibModeBoundsWidth : advanced.FingerModeBoundsWidth;

    /// <param name="contactWidth">Already passed through <see cref="MeasureContactWidth"/>.</param>
    /// <param name="forceEraser">Original <c>forceEraser</c> flag.</param>
    /// <param name="isDrawingShape">Original <c>drawingShapeMode != 0</c>.</param>
    /// <param name="forcePointEraser">Original <c>forcePointEraser</c> flag (default true).</param>
    /// <param name="pptGestureEligible">
    /// Original condition: slideshow end button visible, no strokes yet, and
    /// <c>IsEnableFingerGestureSlideShowControl</c> enabled.
    /// </param>
    public static TouchDecision Classify(
        double contactWidth,
        AdvancedSettings advanced,
        bool nibMode,
        bool forceEraser = false,
        bool isDrawingShape = false,
        bool forcePointEraser = true,
        bool pptGestureEligible = false)
    {
        if (advanced is null) return new TouchDecision(TouchIntent.Draw, 5);

        double boundsWidth = BoundsWidth(advanced, nibMode);

        // 启用特殊屏幕且触摸倍数为 0 时禁用橡皮（原注释）。
        bool eraserEnabled = advanced.TouchMultiplier != 0 || !advanced.IsSpecialScreen;

        if (eraserEnabled && contactWidth > boundsWidth)
        {
            if (!isDrawingShape && forceEraser) return new TouchDecision(TouchIntent.Ignore, 0);

            double threshold = nibMode
                ? advanced.NibModeBoundsWidthThresholdValue
                : advanced.FingerModeBoundsWidthThresholdValue;

            if (contactWidth > boundsWidth * threshold)
            {
                double diameter = contactWidth * (nibMode
                    ? advanced.NibModeBoundsWidthEraserSize
                    : advanced.FingerModeBoundsWidthEraserSize);
                if (advanced.IsSpecialScreen) diameter *= advanced.TouchMultiplier;
                return new TouchDecision(TouchIntent.EraseByPoint, diameter);
            }

            if (pptGestureEligible) return new TouchDecision(TouchIntent.GestureOnly, 5);
            return new TouchDecision(TouchIntent.EraseByStroke, 5);
        }

        return new TouchDecision(TouchIntent.Draw, forcePointEraser ? 50 : 5);
    }
}