using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Grouped settings surface. Rebuilt from the real settings model rather than porting the
/// original 502 KB monolithic XAML; every row writes through to disk immediately.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly UpdateCoordinator _updater;
    private readonly StackPanel _content = new() { Spacing = 10, Padding = new Thickness(20) };

    public SettingsWindow(AppServices services, UpdateCoordinator updater = null)
    {
        _services = services;
        _updater = updater;
        Title = "Ink Canvas 设置";

        BuildSections();

        var scroll = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var header = new Border
        {
            Padding = new Thickness(20, 14, 20, 10),
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            Child = new TextBlock
            {
                Text = "设置",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            },
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Background = (Brush)Application.Current.Resources["IcSurfaceBrush"];
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
        WindowChrome.CenterOnPrimary(this, 720, 820);
        WindowChrome.ApplyTheme(this, services.Settings.Appearance.Theme);
        Activated += (_, _) => Motion.PlayEntrance(_content, 18f);
    }

    private AppSettings S => _services.Settings;

    private void Commit()
    {
        _services.ApplySettings();
        _services.SaveSettings();
        SettingsChanged?.Invoke();
    }

    public event Action SettingsChanged;

    private void BuildSections()
    {
        _content.Children.Add(Section("笔迹与画布"));
        _content.Children.Add(SliderRow("画笔粗细", S.Canvas.InkWidth, 0.5, 40,
            v => S.Canvas.InkWidth = v));
        _content.Children.Add(ComboRow("笔迹风格", new[] { "笔锋（笔尖收细）", "速度笔锋", "均匀线条" },
            S.Canvas.InkStyle, i => S.Canvas.InkStyle = i));
        _content.Children.Add(SliderRow("橡皮大小", S.Canvas.EraserSize, 0, 8,
            v => S.Canvas.EraserSize = (int)v));
        _content.Children.Add(ComboRow("橡皮模式", new[] { "图标切换", "面积擦", "线条擦" },
            S.Canvas.EraserType, i => S.Canvas.EraserType = i));
        _content.Children.Add(ToggleRow("使用白板配色", S.Canvas.UsingWhiteboard,
            v => S.Canvas.UsingWhiteboard = v));
        _content.Children.Add(ToggleRow("显示光标", S.Canvas.IsShowCursor,
            v => S.Canvas.IsShowCursor = v));

        _content.Children.Add(Section("手势与触摸"));
        _content.Children.Add(ToggleRow("多点触控模式", S.Gesture.IsEnableMultiTouchMode,
            v => S.Gesture.IsEnableMultiTouchMode = v));
        _content.Children.Add(ToggleRow("双指缩放", S.Gesture.IsEnableTwoFingerZoom,
            v => S.Gesture.IsEnableTwoFingerZoom = v));
        _content.Children.Add(ToggleRow("双指平移", S.Gesture.IsEnableTwoFingerTranslate,
            v => S.Gesture.IsEnableTwoFingerTranslate = v));
        _content.Children.Add(ToggleRow("双指旋转", S.Gesture.IsEnableTwoFingerRotation,
            v => S.Gesture.IsEnableTwoFingerRotation = v));
        _content.Children.Add(ToggleRow("双指手势作用于选区（有选区时）", S.Gesture.IsEnableTwoFingerRotationOnSelection,
            v => S.Gesture.IsEnableTwoFingerRotationOnSelection = v));
        _content.Children.Add(SliderRow("手掌判定接触宽度（px）", S.Advanced.FingerModeBoundsWidth, 8, 80,
            v => S.Advanced.FingerModeBoundsWidth = (int)v));

        _content.Children.Add(Section("外观"));
        _content.Children.Add(ComboRow("主题", new[] { "跟随系统", "浅色", "深色" },
            S.Appearance.Theme, i => S.Appearance.Theme = i));
        _content.Children.Add(SliderRow("工具栏缩放（%）", S.Appearance.FloatingBarScale, 50, 200,
            v => S.Appearance.FloatingBarScale = v));
        _content.Children.Add(SliderRow("工具栏距底边（px）", S.Appearance.FloatingBarBottomMargin, 0, 400,
            v => S.Appearance.FloatingBarBottomMargin = v));
        _content.Children.Add(ToggleRow("显示工具栏文字", S.Appearance.IsEnableDisPlayFloatBarText,
            v => S.Appearance.IsEnableDisPlayFloatBarText = v));
        _content.Children.Add(ToggleRow("显示退出按钮", S.Appearance.IsShowExitButton,
            v => S.Appearance.IsShowExitButton = v));
        _content.Children.Add(ToggleRow("显示橡皮按钮", S.Appearance.IsShowEraserButton,
            v => S.Appearance.IsShowEraserButton = v));

        _content.Children.Add(Section("图形识别"));
        _content.Children.Add(ToggleRow("墨迹转图形（自研几何识别）", S.InkToShape.IsInkToShapeEnabled,
            v => S.InkToShape.IsInkToShapeEnabled = v));
        _content.Children.Add(InfoRow("已用托管几何拟合替换 Tablet PC InkAnalyzer，因此不再受 x86 限制。判定边界与旧实现不完全一致。"));

        _content.Children.Add(Section("PowerPoint / WPS"));
        _content.Children.Add(ToggleRow("启用 PPT 支持", S.PowerPoint.PowerPointSupport,
            v => S.PowerPoint.PowerPointSupport = v));
        _content.Children.Add(ToggleRow("支持 WPS 演示", S.PowerPoint.IsSupportWPS,
            v => S.PowerPoint.IsSupportWPS = v));
        _content.Children.Add(ToggleRow("显示底部翻页面板", S.PowerPoint.IsShowBottomPPTNavigationPanel,
            v => S.PowerPoint.IsShowBottomPPTNavigationPanel = v));
        _content.Children.Add(ToggleRow("放映中自动保存墨迹", S.PowerPoint.IsAutoSaveStrokesInPowerPoint,
            v => S.PowerPoint.IsAutoSaveStrokesInPowerPoint = v));
        _content.Children.Add(ToggleRow("放映中启用双指手势", S.PowerPoint.IsEnableTwoFingerGestureInPresentationMode,
            v => S.PowerPoint.IsEnableTwoFingerGestureInPresentationMode = v));

        _content.Children.Add(Section("自动化与保存"));
        _content.Children.Add(TextRow("自动保存目录", S.Automation.AutoSavedStrokesLocation,
            v => S.Automation.AutoSavedStrokesLocation = v));
        _content.Children.Add(ToggleRow("清空时自动保存", S.Automation.IsAutoSaveStrokesAtClear,
            v => S.Automation.IsAutoSaveStrokesAtClear = v));
        _content.Children.Add(ToggleRow("截图时自动保存墨迹", S.Automation.IsAutoSaveStrokesAtScreenshot,
            v => S.Automation.IsAutoSaveStrokesAtScreenshot = v));
        _content.Children.Add(ToggleRow("按日期分文件夹", S.Automation.IsSaveScreenshotsInDateFolders,
            v => S.Automation.IsSaveScreenshotsInDateFolders = v));
        _content.Children.Add(ToggleRow("自动清理旧存档", S.Automation.AutoDelSavedFiles,
            v => S.Automation.AutoDelSavedFiles = v));
        _content.Children.Add(SliderRow("清理阈值（天）", S.Automation.AutoDelSavedFilesDaysThreshold, 1, 120,
            v => S.Automation.AutoDelSavedFilesDaysThreshold = (int)v));
        _content.Children.Add(ToggleRow("在希沃白板中自动折叠", S.Automation.IsAutoFoldInEasiNote,
            v => S.Automation.IsAutoFoldInEasiNote = v));
        _content.Children.Add(ToggleRow("在 PPT 放映中自动折叠", S.Automation.IsAutoFoldInPPTSlideShow,
            v => S.Automation.IsAutoFoldInPPTSlideShow = v));

        _content.Children.Add(Section("启动"));
        _content.Children.Add(ToggleRow("启动时折叠工具栏", S.Startup.IsFoldAtStartup,
            v => S.Startup.IsFoldAtStartup = v));
        _content.Children.Add(ToggleRow("开机自动启动", Platform.System.AutoStartService.IsEnabled(),
            v => Platform.System.AutoStartService.SetEnabled(v,
                Environment.ProcessPath ?? "InkCanvas.exe")));
        _content.Children.Add(ToggleRow("检查更新", S.Startup.IsAutoUpdate,
            v => S.Startup.IsAutoUpdate = v));

        _content.Children.Add(Section("抽奖"));
        _content.Children.Add(SliderRow("人数", S.Rand.PeopleCount, 1, 200,
            v => S.Rand.PeopleCount = (int)v));
        _content.Children.Add(ToggleRow("不重复抽取", S.Rand.IsNotRepeatName,
            v => S.Rand.IsNotRepeatName = v));

        _content.Children.Add(Section("特殊屏 / 触摸标定"));
        _content.Children.Add(InfoRow("以下为上游特殊屏逻辑的 1:1 移植（四边红外、触摸倍数、笔尖/手指分档）。按要求已跳过行为验证，需在真实红外屏与压感笔上复核手感。"));
        _content.Children.Add(ToggleRow("启用特殊屏幕", S.Advanced.IsSpecialScreen,
            v => S.Advanced.IsSpecialScreen = v));
        _content.Children.Add(ToggleRow("四边红外屏（接触尺寸取宽高几何平均）", S.Advanced.IsQuadIR,
            v => S.Advanced.IsQuadIR = v));
        _content.Children.Add(SliderRow("触摸倍数（特殊屏为 0 时禁用橡皮）", S.Advanced.TouchMultiplier, 0, 3,
            v => S.Advanced.TouchMultiplier = v));
        _content.Children.Add(ToggleRow("笔尖模式", S.Startup.IsEnableNibMode,
            v => S.Startup.IsEnableNibMode = v));
        _content.Children.Add(SliderRow("笔尖模式接触宽度阈值", S.Advanced.NibModeBoundsWidth, 1, 100,
            v => S.Advanced.NibModeBoundsWidth = (int)v));
        _content.Children.Add(SliderRow("手指模式接触宽度阈值", S.Advanced.FingerModeBoundsWidth, 1, 100,
            v => S.Advanced.FingerModeBoundsWidth = (int)v));
        _content.Children.Add(SliderRow("笔尖模式面积擦阈值倍数", S.Advanced.NibModeBoundsWidthThresholdValue, 1, 10,
            v => S.Advanced.NibModeBoundsWidthThresholdValue = v));
        _content.Children.Add(SliderRow("手指模式面积擦阈值倍数", S.Advanced.FingerModeBoundsWidthThresholdValue, 1, 10,
            v => S.Advanced.FingerModeBoundsWidthThresholdValue = v));
        _content.Children.Add(SliderRow("笔尖模式橡皮尺寸系数", S.Advanced.NibModeBoundsWidthEraserSize, 0.1, 3,
            v => S.Advanced.NibModeBoundsWidthEraserSize = v));
        _content.Children.Add(SliderRow("手指模式橡皮尺寸系数", S.Advanced.FingerModeBoundsWidthEraserSize, 0.1, 3,
            v => S.Advanced.FingerModeBoundsWidthEraserSize = v));

        _content.Children.Add(Section("高级"));
        _content.Children.Add(ToggleRow("退出前二次确认", S.Advanced.IsSecondConfimeWhenShutdownApp,
            v => S.Advanced.IsSecondConfimeWhenShutdownApp = v));
        _content.Children.Add(ToggleRow("启用日志", S.Advanced.IsLogEnabled,
            v => S.Advanced.IsLogEnabled = v));
        _content.Children.Add(ToggleRow("全屏时抑制 Windows 边缘手势", S.Advanced.IsEnableEdgeGestureUtil,
            v => S.Advanced.IsEnableEdgeGestureUtil = v));
        _content.Children.Add(InfoRow("墨迹存档格式为 .icink（JSON）。旧版 WPF 的 .isf 存档无法读取——这是迁移到 WinUI 3 的已知兼容性中断。"));

        _content.Children.Add(Section("关于"));
        _content.Children.Add(InfoRow(_updater is null
            ? "Ink Canvas · WinUI 3"
            : $"Ink Canvas · WinUI 3　当前版本 {_updater.CurrentVersion}"));
        _content.Children.Add(ButtonRow("检查更新", "从 GitHub 发行版检查新版本", async () =>
        {
            if (_updater is not null) await _updater.CheckAsync(interactive: true);
        }));
        _content.Children.Add(ButtonRow("操作指南", "模式、工具、手势与快捷键", () =>
        {
            var w = InfoWindow.OpenGuide(S.Appearance.Theme);
            w.Activate();
        }));
        _content.Children.Add(ButtonRow("更新日志", "查看版本变更记录", () =>
        {
            var w = InfoWindow.OpenChangeLog(S.Appearance.Theme);
            w.Activate();
        }));

        var close = new Button
        {
            Content = "关闭",
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 24),
        };
        close.Click += (_, _) => Close();
        _content.Children.Add(close);
    }

    // ---- row helpers ------------------------------------------------------

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 17,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 2),
    };

    private Border Card(UIElement child) => new()
    {
        Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
        BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = (CornerRadius)Application.Current.Resources["IcControlCorner"],
        Padding = (Thickness)Application.Current.Resources["IcCardPadding"],
        Child = child,
    };

    private UIElement ToggleRow(string header, bool value, Action<bool> set)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "开", OffContent = "关" };
        toggle.Toggled += (_, _) => { set(toggle.IsOn); Commit(); };
        return Card(Row(header, toggle));
    }

    private UIElement SliderRow(string header, double value, double min, double max, Action<double> set)
    {
        var readout = new TextBlock { Text = value.ToString("0.#"), MinWidth = 44, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Width = 260 };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = e.NewValue.ToString("0.#");
            set(e.NewValue);
            Commit();
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(slider);
        right.Children.Add(readout);
        return Card(Row(header, right));
    }

    private UIElement ComboRow(string header, string[] options, int selected, Action<int> set)
    {
        var combo = new ComboBox { Width = 220, SelectedIndex = Math.Clamp(selected, 0, options.Length - 1) };
        foreach (var o in options) combo.Items.Add(o);
        combo.SelectionChanged += (_, _) => { set(combo.SelectedIndex); Commit(); };
        return Card(Row(header, combo));
    }

    private UIElement TextRow(string header, string value, Action<string> set)
    {
        var box = new TextBox { Text = value, Width = 300 };
        box.LostFocus += (_, _) => { set(box.Text); Commit(); };
        return Card(Row(header, box));
    }

    private UIElement ButtonRow(string header, string description, Action onClick)
    {
        var button = new Button { Content = header, MinHeight = 40, MinWidth = 120 };
        button.Click += (_, _) => onClick();
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
        });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(button, 1);
        grid.Children.Add(text);
        grid.Children.Add(button);
        return Card(grid);
    }

    private UIElement InfoRow(string text) => Card(new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
        FontSize = 12,
    });

    private static Grid Row(string header, FrameworkElement control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock
        {
            Text = header,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(label, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(label);
        grid.Children.Add(control);
        return grid;
    }
}