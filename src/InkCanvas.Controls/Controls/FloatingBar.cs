using InkCanvas.Controls.Animation;
using InkCanvas.Core.Settings;
using InkCanvas.Core.Shapes;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Controls.Controls;

/// <summary>App-level actions the bar triggers but does not own.</summary>
public interface IShellActions
{
    void ToggleBoard();
    void OpenSettings();
    void OpenTimer();
    void OpenLottery();
    void OpenRoster();
    void OpenSpotlight();
    void OpenMagnifier();
    void OpenToolkit();
    void OpenReplay();
    void OpenTimeMachine();
    void ToggleRecordingPause();
    void OpenGuide();
    void OpenAiAssistant();
    void OpenPenPalette(bool highlighter);
    void OpenShapePicker();
    Task ScreenshotAsync();
    void ToggleFold();
    void ToggleClickThrough();
    void ToggleBlackScreen();
    void PptNext();
    void PptPrevious();
    void PptExit();
    void PptOpenNavigation();
    void RotatePptNavigationCorner();
    void CollapsePptNavigation();
    void TogglePptNavigationWindow();
    void Exit();
}

/// <summary>
/// The floating toolbar. Built from <see cref="TouchIconButton"/> so every target is at least
/// 40px, with a collapsible secondary row for less-frequent actions.
/// </summary>
public sealed class FloatingBar : UserControl
{
    private readonly StackPanel _primary = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly StackPanel _secondary = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Border _secondaryHost;
    private readonly StackPanel _pptGroup = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private TouchIconButton _penButton, _highlighterButton, _eraserButton, _selectButton, _shapeButton;
    private TouchIconButton _foldButton, _passThroughButton, _deleteSelectionButton;
    private TouchIconButton _pptExitButton;
    private bool _pptMode;
    private InkController _controller;
    private bool _folded;

    public FloatingBar()
    {
        _secondaryHost = new Border { Child = _secondary, Visibility = Visibility.Collapsed };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        root.Children.Add(_secondaryHost);
        root.Children.Add(_primary);

        Content = new Border
        {
            // Fill the host window: the window is sized from the button count, so stretching
            // avoids both clipping and an invisible hit-testable strip beside the bar.
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(0),
            // Square: the host window's corners are rounded by DWM. A rounded content border
            // inside an opaque rectangular window would leave white corner slivers.
            CornerRadius = new CornerRadius(0),
            Padding = (Thickness)Application.Current.Resources["IcBarPadding"],
            Child = root,
        };

        Loaded += (_, _) => Motion.PlayEntrance(this);
    }

    public InkController Controller
    {
        get => _controller;
        set
        {
            if (ReferenceEquals(_controller, value)) return;
            if (_controller is not null) _controller.SelectionChanged -= OnControllerSelectionChanged;
            _controller = value;
            if (_controller is not null) _controller.SelectionChanged += OnControllerSelectionChanged;
            RefreshSelection();
            LayoutChanged?.Invoke();
        }
    }

    public event Action LayoutChanged;

    private void OnControllerSelectionChanged()
    {
        RefreshSelection();
        LayoutChanged?.Invoke();
    }

    public IShellActions Shell { get; set; }

    public bool IsFolded => _folded;

    /// <summary>Diagnostics: how many buttons the primary row actually holds.</summary>
    public int PrimaryChildCount => _primary.Children.Count;

    /// <summary>
    /// Natural size of the bar, measured child-by-child.
    /// <para>
    /// Measuring the whole control with an infinite constraint does not work reliably once it
    /// is rooted in a window: the returned DesiredSize stays constrained by the current window
    /// width, so a bar that grows (delete-selection button, PPT group, expanded row) would be
    /// clipped. Measuring the leaf children directly avoids that feedback loop.
    /// </para>
    /// </summary>
    public (double Width, double Height) ComputeLayoutSize()
    {
        double primary = MeasureRow(_primary);
        double secondary = _secondaryHost.Visibility == Visibility.Visible ? MeasureRow(_secondary) : 0;

        // Chrome: bar border padding 8+8, border 1+1, host padding 6+6.
        const double chrome = 16 + 2 + 12;
        double width = Math.Max(primary, secondary) + chrome + 8;   // +8 slack for rounding
        double height = 40 + chrome + (secondary > 0 ? 40 + 4 : 0);
        return (width, height);
    }

    private static double MeasureRow(StackPanel row)
    {
        double total = 0;
        int visible = 0;
        foreach (var child in row.Children)
        {
            if (child is not FrameworkElement fe || fe.Visibility != Visibility.Visible) continue;
            if (fe is StackPanel nested)
            {
                double nestedWidth = MeasureRow(nested);
                if (nestedWidth <= 0) continue;
                total += nestedWidth;
            }
            else
            {
                // ActualWidth is post-layout truth. A horizontal StackPanel arranges children
                // at their natural size even when the window clips them, so this stays correct
                // while DesiredSize/Measure can come back constrained by the current window.
                if (fe.ActualWidth > 0)
                {
                    total += fe.ActualWidth;
                }
                else
                {
                    fe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    total += Math.Max(fe.DesiredSize.Width, fe.MinWidth);
                }
            }
            visible++;
        }
        if (visible > 1) total += (visible - 1) * row.Spacing;
        return total;
    }

    /// <summary>
    /// Visible button and separator counts, including the nested PPT group.
    /// The host window sizes itself from these rather than from a measure pass: a rooted
    /// horizontal StackPanel keeps reporting a width constrained by the current window, so
    /// measurement cannot discover the natural size.
    /// </summary>
    public (int Buttons, int Separators) CountVisibleItems()
    {
        int buttons = 0, separators = 0;
        Count(_primary, ref buttons, ref separators);
        if (_secondaryHost.Visibility == Visibility.Visible)
        {
            int b2 = 0, s2 = 0;
            Count(_secondary, ref b2, ref s2);
            buttons = Math.Max(buttons, b2);
            separators = Math.Max(separators, s2);
        }
        return (buttons, separators);
    }

    private static void Count(StackPanel row, ref int buttons, ref int separators)
    {
        foreach (var child in row.Children)
        {
            if (child is not FrameworkElement fe || fe.Visibility != Visibility.Visible) continue;
            if (fe is StackPanel nested) Count(nested, ref buttons, ref separators);
            else if (fe is Border) separators++;
            else buttons++;
        }
    }

    /// <summary>Diagnostics: natural width computed for the primary row.</summary>
    public double ComputedPrimaryWidth => MeasureRow(_primary);

    /// <summary>
    /// The rows' laid-out widths. These are only the true natural widths while the host window
    /// is wide enough not to constrain them — see FloatingBarWindow's two-phase fit.
    /// </summary>
    public double PrimaryRowActualWidth => _primary.ActualWidth;

    public double SecondaryRowActualWidth =>
        _secondaryHost.Visibility == Visibility.Visible ? _secondary.ActualWidth : 0;

    public bool ClickThroughActive { get; set; }

    /// <summary>Build the buttons once the controller and shell are available.</summary>
    public void Build()
    {
        _primary.Children.Clear();
        _secondary.Children.Clear();
        _pptGroup.Children.Clear();

        _penButton = Add(_primary, Glyphs.Pen, "画笔（点击打开颜色与粗细）", () =>
        {
            SetTool(InkTool.Pen);
            Shell?.OpenPenPalette(false);
        });
        _highlighterButton = Add(_primary, Glyphs.Highlight, "荧光笔（点击打开颜色与粗细）", () =>
        {
            SetTool(InkTool.Highlighter);
            Shell?.OpenPenPalette(true);
        });
        _eraserButton = Add(_primary, Glyphs.Eraser, "橡皮擦", () => SetTool(InkTool.Eraser));
        _selectButton = Add(_primary, Glyphs.Select, "选择", () => SetTool(InkTool.Select));
        Add(_primary, Glyphs.Ai, "AI 识别（先框选内容）", () => Shell?.OpenAiAssistant());
        _deleteSelectionButton = Add(_primary, Glyphs.Delete, "删除选中墨迹", () => Controller?.DeleteSelection());
        _deleteSelectionButton.Visibility = Visibility.Collapsed;
        _shapeButton = Add(_primary, Glyphs.Shapes, "图形", () => Shell?.OpenShapePicker());
        _shapeButton.IconFont = Glyphs.ShapesFont;

        _primary.Children.Add(Separator());
        Add(_primary, Glyphs.Undo, "撤销", () => Controller?.Undo());
        Add(_primary, Glyphs.Redo, "重做", () => Controller?.Redo());
        Add(_primary, Glyphs.Delete, "清空", () => Controller?.Clear());

        _primary.Children.Add(Separator());
        _pptExitButton = Add(_pptGroup, Glyphs.Close, "退出放映", () => Shell?.PptExit());
        _pptGroup.Visibility = Visibility.Collapsed;
        _primary.Children.Add(_pptGroup);

        Add(_primary, Glyphs.Board, "画板", () => Shell?.ToggleBoard());
        _passThroughButton = Add(_primary, Glyphs.Unlock, "当前：标注模式（可书写）— 点击进入鼠标模式，快捷键 Alt+P", () =>
        {
            Shell?.ToggleClickThrough();
            RefreshSelection();
        });
        _passThroughButton.UseMousePointerIcon();
        _foldButton = Add(_primary, Glyphs.ChevronUp, "展开更多", ToggleFold);

        Add(_secondary, Glyphs.Ai, "AI 助教", () => Shell?.OpenAiAssistant());
        Add(_secondary, Glyphs.ChevronRight, "翻页悬浮窗", () => Shell?.TogglePptNavigationWindow());
        Add(_secondary, Glyphs.Lock, "黑屏/恢复（快捷键 F9）", () => Shell?.ToggleBlackScreen());
        Add(_secondary, Glyphs.Board, "课堂工具", () => Shell?.OpenToolkit());
        Add(_secondary, Glyphs.Stopwatch, "倒计时", () => Shell?.OpenTimer());
        Add(_secondary, Glyphs.People, "抽奖", () => Shell?.OpenLottery());
        Add(_secondary, Glyphs.Replay, "墨迹回放", () => Shell?.OpenReplay());
        Add(_secondary, Glyphs.TimeMachine, "板书时光机", () => Shell?.OpenTimeMachine());
        Add(_secondary, Glyphs.Pause, "暂停录制 / 继续录制", () => Shell?.ToggleRecordingPause());
        Add(_secondary, Glyphs.Camera, "截图", async () => { if (Shell is not null) await Shell.ScreenshotAsync(); });
        Add(_secondary, Glyphs.Help, "操作指南", () => Shell?.OpenGuide());
        Add(_secondary, Glyphs.Settings, "设置", () => Shell?.OpenSettings());
        Add(_secondary, Glyphs.Exit, "退出", () => Shell?.Exit());

        RefreshSelection();
    }

    public void SetPptModeVisible(bool visible)
    {
        _pptMode = visible;
        _pptGroup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        LayoutChanged?.Invoke();
    }

    private TouchIconButton Add(StackPanel host, string glyph, string tooltip, Action onClick)
    {
        var button = new TouchIconButton { Glyph = glyph };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        host.Children.Add(button);
        return button;
    }

    private static Border Separator() => new()
    {
        Width = 1,
        Margin = new Thickness(4, 6, 4, 6),
        Background = (Brush)Application.Current.Resources["IcStrokeBrush"],
    };

    private void SetTool(InkTool tool)
    {
        if (Controller is null) return;
        Controller.Tool = tool;
        RefreshSelection();
    }

    public void RefreshSelection()
    {
        var tool = Controller?.Tool ?? InkTool.Pen;
        if (_penButton is not null) _penButton.IsSelected = tool == InkTool.Pen;
        if (_highlighterButton is not null) _highlighterButton.IsSelected = tool == InkTool.Highlighter;
        if (_eraserButton is not null) _eraserButton.IsSelected = tool == InkTool.Eraser;
        if (_selectButton is not null) _selectButton.IsSelected = tool == InkTool.Select;
        if (_shapeButton is not null) _shapeButton.IsSelected = tool == InkTool.Shape;
        if (_deleteSelectionButton is not null)
            _deleteSelectionButton.Visibility = (Controller?.Selection.Count ?? 0) > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (_passThroughButton is not null)
        {
            _passThroughButton.IsSelected = ClickThroughActive;
            ToolTipService.SetToolTip(_passThroughButton, ClickThroughActive
                ? "当前：鼠标模式（点击穿透到下方程序）— 点击回到标注模式，快捷键 Alt+P"
                : "当前：标注模式（可书写）— 点击进入鼠标模式，快捷键 Alt+P");
        }
    }

    private void ToggleFold()
    {
        _folded = !_folded;
        _secondaryHost.Visibility = _folded ? Visibility.Visible : Visibility.Collapsed;
        if (_foldButton is not null)
            _foldButton.Glyph = _folded ? Glyphs.ChevronDown : Glyphs.ChevronUp;
        if (_folded) Motion.PlayEntrance(_secondaryHost, 8f);
        Shell?.ToggleFold();
    }

    private void AttachPenPalette(TouchIconButton button)
    {
        // Intentionally unused: WinUI 3 flyouts render inside the host window, and the bar
        // window is only ~70px tall, so a palette flyout gets clipped. The shell opens the
        // palette in its own window instead (IShellActions.OpenPenPalette).
        _ = button;
    }

    /// <summary>Shape catalogue shared with the shape picker window.</summary>
    public static readonly (ShapeKind, string)[] ShapeCatalog =
    {
        (ShapeKind.Line, "直线"),
        (ShapeKind.DashedLine, "虚线"),
        (ShapeKind.DotLine, "点线"),
        (ShapeKind.Arrow, "箭头"),
        (ShapeKind.ParallelLine, "平行线"),
        (ShapeKind.Rectangle, "矩形"),
        (ShapeKind.RectangleFromCenter, "中心矩形"),
        (ShapeKind.Ellipse, "椭圆"),
        (ShapeKind.Circle, "圆"),
        (ShapeKind.EllipseFromCenter, "中心椭圆"),
        (ShapeKind.EllipseWithFoci, "椭圆（含焦点）"),
        (ShapeKind.DashedCircle, "虚线圆"),
        (ShapeKind.CoordinateBoth, "坐标系（双向）"),
        (ShapeKind.CoordinateXOneWay, "坐标系（x 单向）"),
        (ShapeKind.CoordinateYOneWay, "坐标系（y 单向）"),
        (ShapeKind.CoordinateFirstQuadrant, "坐标系（第一象限）"),
        (ShapeKind.Coordinate3D, "空间坐标系"),
        (ShapeKind.Hyperbola, "双曲线"),
        (ShapeKind.HyperbolaWithFoci, "双曲线（含焦点）"),
        (ShapeKind.ParabolaRight, "抛物线（右开口）"),
        (ShapeKind.ParabolaWithFoci, "抛物线（含焦点）"),
        (ShapeKind.ParabolaUp, "抛物线（上开口）"),
        (ShapeKind.Cylinder, "圆柱"),
        (ShapeKind.Cone, "圆锥"),
        (ShapeKind.Cuboid, "长方体"),
    };
}