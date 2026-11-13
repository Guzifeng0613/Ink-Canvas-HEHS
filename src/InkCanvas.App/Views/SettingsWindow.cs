using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Responsive settings surface. Options that have no live implementation are shown as
/// read-only saved values instead of switches that imply an effect the app does not have.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppServices _services;
    private readonly UpdateCoordinator _updater;
    private readonly StackPanel _content = new()
    {
        Spacing = 10,
        Padding = new Thickness(20),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

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
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
        WindowChrome.ApplyTheme(this, S.Appearance.Theme);
        SettingsChanged?.Invoke();
    }

    public event Action SettingsChanged;

    private void BuildSections()
    {
        _content.Children.Add(Section("画笔与画板"));
        _content.Children.Add(SliderRow("画笔粗细（原菜单 1–30；设置值为菜单值的一半）",
            S.Canvas.InkWidth, 0.5, 15, v => S.Canvas.InkWidth = v, 0.5));
        _content.Children.Add(InfoRow($"荧光笔透明度：{S.Canvas.InkAlpha}（原菜单范围 20–240，步长 20；在荧光笔调色盘中调整，按笔迹保存）"));
        _content.Children.Add(ComboRow("笔迹风格", OriginalMenuCatalog.PenStyles,
            S.Canvas.InkStyle, i => S.Canvas.InkStyle = i));
        _content.Children.Add(ComboRow("橡皮大小", OriginalMenuCatalog.EraserSizes,
            S.Canvas.EraserSize, i => S.Canvas.EraserSize = i));
        _content.Children.Add(DeferredRow("橡皮模式", OriginalMenuCatalog.EraserModes[Math.Clamp(S.Canvas.EraserType, 0, 2)],
            "设置值会保存，但目前只有一种橡皮行为；面积擦与线条擦模式尚未接入。"));
        _content.Children.Add(DeferredRow("白板背景配色", OnOff(S.Canvas.UsingWhiteboard),
            "此设置目前仅在应用启动时用于初始化画板，运行中切换尚未接入。"));
        _content.Children.Add(DeferredRow("上传图片时压缩至 1080P", OnOff(S.Canvas.IsCompressPicturesUploaded),
            "图片上传压缩尚未接入。"));
        _content.Children.Add(DeferredRow("显示画笔光标", OnOff(S.Canvas.IsShowCursor),
            "画笔光标行为尚未接入。"));
        _content.Children.Add(DeferredRow("选中墨迹时隐藏笔迹", OnOff(S.Canvas.HideStrokeWhenSelecting),
            "选区渲染尚未读取此设置。"));
        _content.Children.Add(DeferredRow("双曲线渐近线", OptionalText(S.Canvas.HyperbolaAsymptoteOption),
            "图形生成当前尚未读取此选项。"));

        _content.Children.Add(Section("手势"));
        _content.Children.Add(ToggleRow("启用多点触控模式", S.Gesture.IsEnableMultiTouchMode,
            v => S.Gesture.IsEnableMultiTouchMode = v));
        _content.Children.Add(ToggleRow("双指缩放", S.Gesture.IsEnableTwoFingerZoom,
            v => S.Gesture.IsEnableTwoFingerZoom = v));
        _content.Children.Add(ToggleRow("双指平移", S.Gesture.IsEnableTwoFingerTranslate,
            v => S.Gesture.IsEnableTwoFingerTranslate = v));
        _content.Children.Add(ToggleRow("双指旋转", S.Gesture.IsEnableTwoFingerRotation,
            v => S.Gesture.IsEnableTwoFingerRotation = v));
        _content.Children.Add(ToggleRow("双指手势作用于选区", S.Gesture.IsEnableTwoFingerRotationOnSelection,
            v => S.Gesture.IsEnableTwoFingerRotationOnSelection = v));
        _content.Children.Add(DeferredRow("自动开关两指手势", OnOff(S.Gesture.AutoSwitchTwoFingerGesture),
            "离开/进入画板时自动切换手势尚未接入。"));
        _content.Children.Add(ComboRow("缩放/旋转变换中心点", new[]
            {
                "画布中心点（默认）", "全部内容的中心点", "手势操作的中心点",
            }, (int)S.Gesture.MatrixTransformCenterPoint,
            i => S.Gesture.MatrixTransformCenterPoint = (MatrixTransformCenterPointOptions)i));

        _content.Children.Add(Section("外观"));
        int[] themeMap = { 1, 2, 0 };
        _content.Children.Add(ComboRow("颜色主题", OriginalMenuCatalog.ThemeOptions,
            Array.IndexOf(themeMap, S.Appearance.Theme), i => S.Appearance.Theme = themeMap[i]));
        _content.Children.Add(SliderRow("浮动工具栏 UI 缩放（%）", S.Appearance.FloatingBarScale,
            50, 150, v => S.Appearance.FloatingBarScale = v));
        _content.Children.Add(SliderRow("浮动工具栏底部边距（像素）", S.Appearance.FloatingBarBottomMargin,
            0, 200, v => S.Appearance.FloatingBarBottomMargin = v));
        _content.Children.Add(QuickValuesRow("工具栏缩放快捷值", new[] { 80d, 90d, 100d, 110d, 125d },
            v => S.Appearance.FloatingBarScale = v));
        _content.Children.Add(DeferredRow("画板 UI 缩放（%）", $"{S.Appearance.BlackboardScale:0}",
            "旧版范围为 50–150，当前画板布局尚未读取该值。"));
        _content.Children.Add(DeferredRow("浮动工具栏显示文字", OnOff(S.Appearance.IsEnableDisPlayFloatBarText),
            "工具栏文字显示尚未接入。"));
        _content.Children.Add(DeferredRow("浮动工具栏彩色背景", OnOff(S.Appearance.IsColorfulViewboxFloatingBar),
            "彩色工具栏样式尚未接入。"));
        _content.Children.Add(DeferredRow("按钮背景透明", OnOff(S.Appearance.IsTransparentButtonBackground),
            "按钮背景样式尚未读取此设置。"));
        _content.Children.Add(DeferredRow("显示退出按钮", OnOff(S.Appearance.IsShowExitButton),
            "工具栏按钮显隐尚未接入。"));
        _content.Children.Add(DeferredRow("显示橡皮按钮", OnOff(S.Appearance.IsShowEraserButton),
            "工具栏按钮显隐尚未接入。"));
        _content.Children.Add(DeferredRow("显示隐藏控制按钮", OnOff(S.Appearance.IsShowHideControlButton),
            "工具栏按钮显隐尚未接入。"));
        _content.Children.Add(DeferredRow("显示左右切换按钮", OnOff(S.Appearance.IsShowLRSwitchButton),
            "工具栏按钮显隐尚未接入。"));
        _content.Children.Add(DeferredRow("显示手指模式切换按钮", OnOff(S.Appearance.IsShowModeFingerToggleSwitch),
            "工具栏按钮显隐尚未接入。"));
        _content.Children.Add(DeferredRow("调色盘窗口显示笔尖模式切换", OnOff(S.Appearance.IsEnableDisPlayNibModeToggler),
            "笔尖模式切换控件尚未在调色盘中实现。"));

        _content.Children.Add(Section("启动与更新"));
        _content.Children.Add(ToggleRow("自动检查更新", S.Startup.IsAutoUpdate,
            v => S.Startup.IsAutoUpdate = v));
        _content.Children.Add(DeferredRow("静默自动更新", OnOff(S.Startup.IsAutoUpdateWithSilence),
            "静默安装策略尚未接入；当前只保留原配置。"));
        _content.Children.Add(DeferredRow("静默更新时间段", $"{S.Startup.AutoUpdateWithSilenceStartTime}–{S.Startup.AutoUpdateWithSilenceEndTime}",
            "原版选项为 00:00–23:40、每 20 分钟一档；静默更新未接入，时间不可编辑。"));
        _content.Children.Add(ToggleRow("启动时折叠工具栏", S.Startup.IsFoldAtStartup,
            v => S.Startup.IsFoldAtStartup = v, enabled: false,
            disabledReason: "启动时折叠行为尚未接入。"));
        _content.Children.Add(ToggleRow("启用笔尖模式", S.Startup.IsEnableNibMode,
            v => S.Startup.IsEnableNibMode = v));
        _content.Children.Add(ToggleRow("开机自动启动", Platform.System.AutoStartService.IsEnabled(),
            v => Platform.System.AutoStartService.SetEnabled(v, Environment.ProcessPath ?? "InkCanvas.exe")));

        _content.Children.Add(Section("PowerPoint / WPS"));
        _content.Children.Add(ToggleRow("Microsoft PowerPoint 支持", S.PowerPoint.PowerPointSupport,
            v => S.PowerPoint.PowerPointSupport = v));
        _content.Children.Add(ToggleRow("支持 WPS 演示", S.PowerPoint.IsSupportWPS,
            v => S.PowerPoint.IsSupportWPS = v));
        _content.Children.Add(ToggleRow("放映开始时显示画板", S.PowerPoint.IsShowCanvasAtNewSlideShow,
            v => S.PowerPoint.IsShowCanvasAtNewSlideShow = v));
        _content.Children.Add(ToggleRow("放映中启用手指翻页手势", S.PowerPoint.IsEnableFingerGestureSlideShowControl,
            v => S.PowerPoint.IsEnableFingerGestureSlideShowControl = v));
        _content.Children.Add(ToggleRow("显示 PPT 下方页码按钮", S.PowerPoint.IsShowPPTNavigationBottom,
            v => S.PowerPoint.IsShowPPTNavigationBottom = v));
        _content.Children.Add(ToggleRow("显示 PPT 侧边页码按钮", S.PowerPoint.IsShowPPTNavigationSides,
            v => S.PowerPoint.IsShowPPTNavigationSides = v));
        _content.Children.Add(ToggleRow("显示 PPT 底部翻页面板", S.PowerPoint.IsShowBottomPPTNavigationPanel,
            v => S.PowerPoint.IsShowBottomPPTNavigationPanel = v));
        _content.Children.Add(ToggleRow("显示 PPT 侧边翻页面板", S.PowerPoint.IsShowSidePPTNavigationPanel,
            v => S.PowerPoint.IsShowSidePPTNavigationPanel = v));
        _content.Children.Add(DeferredRow("PPT 选择时不清除墨迹", OnOff(S.PowerPoint.IsNoClearStrokeOnSelectWhenInPowerPoint),
            "原选项作用于单独的隐藏/显示墨迹控件；该控件尚未移植，因此此项仅保留配置。"));
        _content.Children.Add(ToggleRow("鼠标模式时在 PPT 上显示墨迹", S.PowerPoint.IsShowStrokeOnSelectInPowerPoint,
            v => S.PowerPoint.IsShowStrokeOnSelectInPowerPoint = v));
        _content.Children.Add(ToggleRow("放映结束时自动保存墨迹", S.PowerPoint.IsAutoSaveStrokesInPowerPoint,
            v => S.PowerPoint.IsAutoSaveStrokesInPowerPoint = v));
        _content.Children.Add(ToggleRow("PPT 翻页时自动截图", S.PowerPoint.IsAutoSaveScreenShotInPowerPoint,
            v => S.PowerPoint.IsAutoSaveScreenShotInPowerPoint = v));
        _content.Children.Add(ToggleRow("记忆并提示上次播放位置", S.PowerPoint.IsNotifyPreviousPage,
            v => S.PowerPoint.IsNotifyPreviousPage = v));
        _content.Children.Add(ToggleRow("提示隐藏幻灯片", S.PowerPoint.IsNotifyHiddenPage,
            v => S.PowerPoint.IsNotifyHiddenPage = v));
        _content.Children.Add(ToggleRow("提示是否已启用自动播放", S.PowerPoint.IsNotifyAutoPlayPresentation,
            v => S.PowerPoint.IsNotifyAutoPlayPresentation = v));
        _content.Children.Add(DeferredRow("放映中启用双指手势", OnOff(S.PowerPoint.IsEnableTwoFingerGestureInPresentationMode), "放映专属双指手势门控尚未移植。"));

        _content.Children.Add(Section("自动收纳与自动保存"));
        _content.Children.Add(ToggleRow("进入希沃白板时自动收纳", S.Automation.IsAutoFoldInEasiNote,
            v => S.Automation.IsAutoFoldInEasiNote = v));
        _content.Children.Add(ToggleRow("进入希沃视频展台时自动收纳", S.Automation.IsAutoFoldInEasiCamera,
            v => S.Automation.IsAutoFoldInEasiCamera = v));
        _content.Children.Add(ToggleRow("进入希沃品课时自动收纳", S.Automation.IsAutoFoldInSeewoPincoTeacher,
            v => S.Automation.IsAutoFoldInSeewoPincoTeacher = v));
        _content.Children.Add(ToggleRow("进入鸿合白板时自动收纳", S.Automation.IsAutoFoldInHiteTouchPro,
            v => S.Automation.IsAutoFoldInHiteTouchPro = v));
        _content.Children.Add(ToggleRow("进入微软 Whiteboard 时自动收纳", S.Automation.IsAutoFoldInMSWhiteboard,
            v => S.Automation.IsAutoFoldInMSWhiteboard = v));
        _content.Children.Add(ToggleRow("PPT 放映时自动收纳", S.Automation.IsAutoFoldInPPTSlideShow,
            v => S.Automation.IsAutoFoldInPPTSlideShow = v));
        _content.Children.Add(DeferredRow("希沃白板忽略桌面批注窗口", OnOff(S.Automation.IsAutoFoldInEasiNoteIgnoreDesktopAnno), "窗口识别排除规则尚未接入。"));
        _content.Children.Add(DeferredRow("进入希沃轻白板时自动收纳", OnOff(S.Automation.IsAutoFoldInEasiNote3C), "此程序识别尚未接入。"));
        _content.Children.Add(DeferredRow("进入鸿合视频展台时自动收纳", OnOff(S.Automation.IsAutoFoldInHiteCamera), "此程序识别尚未接入。"));
        _content.Children.Add(DeferredRow("进入文香白板时自动收纳", OnOff(S.Automation.IsAutoFoldInWxBoardMain), "此程序识别尚未接入。"));
        _content.Children.Add(DeferredRow("进入中原旧白板时自动收纳", OnOff(S.Automation.IsAutoFoldInOldZyBoard), "此程序识别尚未接入。"));
        _content.Children.Add(DeferredRow("自动查杀希沃 PPT 小工具", OnOff(S.Automation.IsAutoKillPptService), "进程查杀尚未接入。"));
        _content.Children.Add(DeferredRow("自动查杀希沃白板", OnOff(S.Automation.IsAutoKillEasiNote), "进程查杀尚未接入。"));
        _content.Children.Add(ToggleRow("截图按日期分文件夹保存", S.Automation.IsSaveScreenshotsInDateFolders,
            v => S.Automation.IsSaveScreenshotsInDateFolders = v));
        _content.Children.Add(ToggleRow("截图时自动保存墨迹", S.Automation.IsAutoSaveStrokesAtScreenshot,
            v => S.Automation.IsAutoSaveStrokesAtScreenshot = v));
        _content.Children.Add(ToggleRow("清屏时自动保存墨迹", S.Automation.IsAutoSaveStrokesAtClear,
            v => S.Automation.IsAutoSaveStrokesAtClear = v));
        _content.Children.Add(SliderRow("自动保存所需最少墨迹数（0 表示不限制）",
            S.Automation.MinimumAutomationStrokeNumber, 0, 10,
            v => S.Automation.MinimumAutomationStrokeNumber = (int)v));
        _content.Children.Add(TextRow("墨迹与截图自动保存目录", S.Automation.AutoSavedStrokesLocation,
            v => S.Automation.AutoSavedStrokesLocation = v));
        _content.Children.Add(PathShortcutsRow());
        _content.Children.Add(DeferredRow("自动清理旧存档", OnOff(S.Automation.AutoDelSavedFiles),
            "自动清理尚未接入定期任务；开启时不会删除文件。"));
        _content.Children.Add(DeferredRow("自动清理保留天数", $"{S.Automation.AutoDelSavedFilesDaysThreshold} 天（原选项：{string.Join("、", OriginalMenuCatalog.AutoDeleteDays)}）",
            "自动清理任务未接入，期限不可编辑。"));
        _content.Children.Add(DeferredRow("退出书写模式时自动清屏", OnOff(S.Automation.IsAutoClearWhenExitingWritingMode),
            "退出书写模式自动清屏尚未接入。"));

        _content.Children.Add(Section("特殊屏幕与触摸校准"));
        _content.Children.Add(ToggleRow("启用特殊屏幕模式", S.Advanced.IsSpecialScreen,
            v => S.Advanced.IsSpecialScreen = v));
        _content.Children.Add(SliderRow("特殊屏幕触摸大小倍数（0 在特殊屏上禁用橡皮）",
            S.Advanced.TouchMultiplier, 0, 5, v => S.Advanced.TouchMultiplier = v, 0.01));
        _content.Children.Add(SliderRow("笔尖模式 BoundsWidth", S.Advanced.NibModeBoundsWidth,
            1, 50, v => S.Advanced.NibModeBoundsWidth = (int)v));
        _content.Children.Add(SliderRow("手指模式 BoundsWidth", S.Advanced.FingerModeBoundsWidth,
            1, 50, v => S.Advanced.FingerModeBoundsWidth = (int)v));
        _content.Children.Add(SliderRow("笔尖模式 ThresholdValue", S.Advanced.NibModeBoundsWidthThresholdValue,
            0, 10, v => S.Advanced.NibModeBoundsWidthThresholdValue = v, 0.01));
        _content.Children.Add(SliderRow("手指模式 ThresholdValue", S.Advanced.FingerModeBoundsWidthThresholdValue,
            0, 10, v => S.Advanced.FingerModeBoundsWidthThresholdValue = v, 0.01));
        _content.Children.Add(SliderRow("笔尖模式 EraserSize", S.Advanced.NibModeBoundsWidthEraserSize,
            0.01, 10, v => S.Advanced.NibModeBoundsWidthEraserSize = v, 0.01));
        _content.Children.Add(SliderRow("手指模式 EraserSize", S.Advanced.FingerModeBoundsWidthEraserSize,
            0.01, 10, v => S.Advanced.FingerModeBoundsWidthEraserSize = v, 0.01));
        _content.Children.Add(ToggleRow("四边红外模式（以宽高几何平均计算接触尺寸）", S.Advanced.IsQuadIR,
            v => S.Advanced.IsQuadIR = v));
        _content.Children.Add(InfoRow("以上特殊屏幕与触摸参数按原 WPF 算法移植；按要求未做行为验证，请在实际触控屏与压感笔上复核。"));

        _content.Children.Add(Section("高级"));
        _content.Children.Add(ToggleRow("启用墨迹转图形", S.InkToShape.IsInkToShapeEnabled,
            v => S.InkToShape.IsInkToShapeEnabled = v));
        _content.Children.Add(ToggleRow("启用日志", S.Advanced.IsLogEnabled,
            v => S.Advanced.IsLogEnabled = v));
        _content.Children.Add(ToggleRow("抑制 Windows 边缘手势", S.Advanced.IsEnableEdgeGestureUtil,
            v => S.Advanced.IsEnableEdgeGestureUtil = v));
        _content.Children.Add(DeferredRow("退出前二次确认", OnOff(S.Advanced.IsSecondConfimeWhenShutdownApp),
            "退出确认流程尚未接入。"));

        _content.Children.Add(Section("抽奖"));
        _content.Children.Add(SliderRow("抽取人数", S.Rand.PeopleCount, 1, 200,
            v => S.Rand.PeopleCount = (int)v));
        _content.Children.Add(ToggleRow("不重复抽取", S.Rand.IsNotRepeatName,
            v => S.Rand.IsNotRepeatName = v));

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
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 24),
        };
        Accessible(close, "关闭设置窗口");
        close.Click += (_, _) => Close();
        _content.Children.Add(close);
    }

    private UIElement PathShortcutsRow()
    {
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var drive = new Button { Content = "设置为 D:\\Ink Canvas", MinHeight = 40 };
        Accessible(drive, "将自动保存目录设置为 D:\\Ink Canvas");
        drive.Click += (_, _) => { S.Automation.AutoSavedStrokesLocation = @"D:\Ink Canvas"; Commit(); };
        var documents = new Button { Content = "设置为文档目录", MinHeight = 40 };
        Accessible(documents, "将自动保存目录设置为文档目录");
        documents.Click += (_, _) =>
        {
            S.Automation.AutoSavedStrokesLocation = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            Commit();
        };
        panel.Children.Add(drive);
        panel.Children.Add(documents);
        return Card(panel);
    }

    private UIElement QuickValuesRow(string label, double[] values, Action<double> set)
    {
        var panel = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        StackPanel row = null;
        for (int i = 0; i < values.Length; i++)
        {
            if (i % 2 == 0)
            {
                row = new StackPanel { Spacing = 6, Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
                panel.Children.Add(row);
            }
            double value = values[i];
            var button = new Button { Content = $"{value:0}%", MinHeight = 40, MinWidth = 48 };
            Accessible(button, $"{label} {value:0}%");
            button.Click += (_, _) => { set(value); Commit(); };
            row.Children.Add(button);
        }
        return Card(panel);
    }

    // ---- row helpers ------------------------------------------------------

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 17,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 16, 0, 2),
        TextWrapping = TextWrapping.Wrap,
    };

    private Border Card(UIElement child) => new()
    {
        Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
        BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = (CornerRadius)Application.Current.Resources["IcControlCorner"],
        Padding = (Thickness)Application.Current.Resources["IcCardPadding"],
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = child,
    };

    private UIElement ToggleRow(string header, bool value, Action<bool> set,
        bool enabled = true, string disabledReason = null)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = value,
            OnContent = "开",
            OffContent = "关",
            IsEnabled = enabled,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Accessible(toggle, header, enabled ? null : disabledReason);
        toggle.Toggled += (_, _) => { if (enabled) { set(toggle.IsOn); Commit(); } };
        return Card(Row(header, toggle, enabled ? null : disabledReason));
    }

    private UIElement SliderRow(string header, double value, double min, double max,
        Action<double> set, double step = 1)
    {
        var readout = new TextBlock
        {
            Text = value.ToString("0.##"),
            MinWidth = 48,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            StepFrequency = step,
            Value = Math.Clamp(value, min, max),
            MinWidth = 100,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Accessible(slider, header, $"范围 {min:0.##} 到 {max:0.##}，步长 {step:0.##}");
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = e.NewValue.ToString("0.##");
            set(e.NewValue);
            Commit();
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(readout, 1);
        row.Children.Add(slider);
        row.Children.Add(readout);
        return Card(Row(header, row));
    }

    private UIElement ComboRow(string header, IReadOnlyList<string> options, int selected, Action<int> set)
    {
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
        };
        foreach (string option in options) combo.Items.Add(option);
        // Set the index after items are present: WinUI otherwise coerces it back to -1.
        combo.SelectedIndex = Math.Clamp(selected, 0, options.Count - 1);
        Accessible(combo, header);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex < 0) return;
            set(combo.SelectedIndex);
            Commit();
        };
        return Card(Row(header, combo));
    }

    private UIElement TextRow(string header, string value, Action<string> set)
    {
        var box = new TextBox
        {
            Text = value ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0,
        };
        Accessible(box, header);
        box.LostFocus += (_, _) => { set(box.Text); Commit(); };
        return Card(Row(header, box));
    }

    private UIElement ButtonRow(string header, string description, Action onClick)
    {
        var button = new Button
        {
            Content = header,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Accessible(button, header, description);
        button.Click += (_, _) => onClick();
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
        });
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(text);
        panel.Children.Add(button);
        return Card(panel);
    }

    private UIElement DeferredRow(string header, string currentValue, string reason) =>
        Card(Row(header, new TextBlock
        {
            Text = $"当前保存值：{currentValue}",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
        }, reason));

    private UIElement InfoRow(string text) => Card(new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
        FontSize = 12,
    });

    private static StackPanel Row(string header, FrameworkElement control, string note = null)
    {
        var panel = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        var label = new TextBlock
        {
            Text = header,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(label, header);
        panel.Children.Add(label);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        panel.Children.Add(control);
        if (!string.IsNullOrWhiteSpace(note))
        {
            var hint = new TextBlock
            {
                Text = note,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["IcTextSecondaryBrush"],
            };
            panel.Children.Add(hint);
        }
        return panel;
    }

    private static void Accessible(DependencyObject control, string name, string helpText = null)
    {
        AutomationProperties.SetName(control, name);
        if (!string.IsNullOrWhiteSpace(helpText)) AutomationProperties.SetHelpText(control, helpText);
    }

    private static string OnOff(bool value) => value ? "开" : "关";

    private static string OptionalText(OptionalOperation operation) => operation switch
    {
        OptionalOperation.Yes => "是",
        OptionalOperation.No => "否",
        _ => "每次询问",
    };
}
