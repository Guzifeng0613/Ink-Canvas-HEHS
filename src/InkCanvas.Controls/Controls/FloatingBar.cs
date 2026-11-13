using InkCanvas.Controls.Animation;
using InkCanvas.Core.Shapes;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;
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
    void OpenReplay();
    void OpenGuide();
    Task ScreenshotAsync();
    void ToggleFold();
    void ToggleClickThrough();
    void PptNext();
    void PptPrevious();
    void PptExit();
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
    private bool _folded;

    public FloatingBar()
    {
        _secondaryHost = new Border { Child = _secondary, Visibility = Visibility.Collapsed };

        var root = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        root.Children.Add(_secondaryHost);
        root.Children.Add(_primary);

        Content = new Border
        {
            // Hug the content: a stretched border would leave a transparent strip that still
            // hit-tests, swallowing clicks meant for the application underneath.
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["IcBarCorner"],
            Padding = (Thickness)Application.Current.Resources["IcBarPadding"],
            Child = root,
        };

        Loaded += (_, _) => Motion.PlayEntrance(this);
    }

    public InkController Controller { get; set; }

    public IShellActions Shell { get; set; }

    public bool IsFolded => _folded;

    public bool ClickThroughActive { get; set; }

    /// <summary>Build the buttons once the controller and shell are available.</summary>
    public void Build()
    {
        _primary.Children.Clear();
        _secondary.Children.Clear();
        _pptGroup.Children.Clear();

        _penButton = Add(_primary, Glyphs.Pen, "画笔", () => SetTool(InkTool.Pen));
        _highlighterButton = Add(_primary, Glyphs.Highlight, "荧光笔", () => SetTool(InkTool.Highlighter));
        _eraserButton = Add(_primary, Glyphs.Eraser, "橡皮擦", () => SetTool(InkTool.Eraser));
        _selectButton = Add(_primary, Glyphs.Select, "选择", () => SetTool(InkTool.Select));
        _deleteSelectionButton = Add(_primary, Glyphs.Delete, "删除选中墨迹", () => Controller?.DeleteSelection());
        _deleteSelectionButton.Visibility = Visibility.Collapsed;
        _shapeButton = Add(_primary, Glyphs.Shapes, "图形", ShowShapeMenu);
        _shapeButton.IconFont = Glyphs.ShapesFont;

        AttachPenPalette(_penButton);
        AttachPenPalette(_highlighterButton);

        _primary.Children.Add(Separator());
        Add(_primary, Glyphs.Undo, "撤销", () => Controller?.Undo());
        Add(_primary, Glyphs.Redo, "重做", () => Controller?.Redo());
        Add(_primary, Glyphs.Delete, "清空", () => Controller?.Clear());

        _primary.Children.Add(Separator());
        Add(_pptGroup, Glyphs.ChevronLeft, "上一页", () => Shell?.PptPrevious());
        Add(_pptGroup, Glyphs.ChevronRight, "下一页", () => Shell?.PptNext());
        Add(_pptGroup, Glyphs.Close, "退出放映", () => Shell?.PptExit());
        _pptGroup.Visibility = Visibility.Collapsed;
        _primary.Children.Add(_pptGroup);

        Add(_primary, Glyphs.Board, "画板", () => Shell?.ToggleBoard());
        _passThroughButton = Add(_primary, Glyphs.Unlock, "穿透模式（可点击下方窗口）", () =>
        {
            Shell?.ToggleClickThrough();
            RefreshSelection();
        });
        _foldButton = Add(_primary, Glyphs.ChevronUp, "展开更多", ToggleFold);

        Add(_secondary, Glyphs.Stopwatch, "倒计时", () => Shell?.OpenTimer());
        Add(_secondary, Glyphs.People, "抽奖", () => Shell?.OpenLottery());
        Add(_secondary, Glyphs.Replay, "墨迹回放", () => Shell?.OpenReplay());
        Add(_secondary, Glyphs.Camera, "截图", async () => { if (Shell is not null) await Shell.ScreenshotAsync(); });
        Add(_secondary, Glyphs.Help, "操作指南", () => Shell?.OpenGuide());
        Add(_secondary, Glyphs.Settings, "设置", () => Shell?.OpenSettings());
        Add(_secondary, Glyphs.Exit, "退出", () => Shell?.Exit());

        RefreshSelection();
    }

    public void SetPptModeVisible(bool visible)
        => _pptGroup.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private TouchIconButton Add(StackPanel host, string glyph, string tooltip, Action onClick)
    {
        var button = new TouchIconButton { Glyph = glyph };
        ToolTipService.SetToolTip(button, tooltip);
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
            _passThroughButton.Glyph = ClickThroughActive ? Glyphs.Lock : Glyphs.Unlock;
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
        var palette = new PenPalette { Controller = Controller };
        var flyout = new Flyout { Placement = FlyoutPlacementMode.Top };
        palette.RequestClose = () => flyout.Hide();
        flyout.Content = palette;
        button.Flyout = flyout;
        flyout.Opening += (_, _) =>
        {
            palette.Controller = Controller;
            palette.RefreshFromController();
        };
    }

    private void ShowShapeMenu()
    {
        if (Controller is null) return;
        var menu = new MenuFlyout();
        foreach (var (kind, label) in ShapeCatalog)
        {
            var item = new MenuFlyoutItem { Text = label };
            var captured = kind;
            item.Click += (_, _) =>
            {
                Controller.ShapeKind = captured;
                Controller.Tool = InkTool.Shape;
                RefreshSelection();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(_shapeButton);
    }

    private static readonly (ShapeKind, string)[] ShapeCatalog =
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