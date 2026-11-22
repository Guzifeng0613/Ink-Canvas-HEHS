using InkCanvas.Controls.Controls;
using InkCanvas.Core.AiAssistant;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace InkCanvas.Modules.AiAssistant;

public sealed partial class AssistantWindow
{
    private Border _demoBadge;
    public void SetConnectionStatus(bool demoMode) => _demoBadge.Visibility = demoMode ? Visibility.Visible : Visibility.Collapsed;

    private void BuildLayout(bool demoMode)
    {
        for (int row = 0; row < 5; row++) _root.RowDefinitions.Add(new() { Height = row == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var modeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _modeIcon, _modeText,
            new FontIcon { Glyph = Glyphs.ChevronDown, FontFamily = IconFontStyle.Font, FontSize = 12 } } };
        var mode = CreateSecondaryButton(""); mode.Content = modeContent; mode.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(mode, "讲解方式");
        var menu = new MenuFlyout();
        foreach (AssistantTeachingMode option in Enum.GetValues<AssistantTeachingMode>())
        {
            var item = new MenuFlyoutItem { Text = AssistantTeachingPolicy.Label(option), Icon = new FontIcon
                { Glyph = ModeGlyph(option), FontFamily = IconFontStyle.Font, FontSize = 18 } };
            item.Click += (_, _) => TeachingModeRequested?.Invoke(option); menu.Items.Add(item);
        }
        mode.Flyout = menu; toolbar.Children.Add(mode);
        var globals = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _annotationButton = CreateIconButton("显示或隐藏题目标注", Symbol.View);
        _annotationButton.Click += (_, _) =>
        {
            _annotationsVisible = !_annotationsVisible;
            _annotationButton.Opacity = _annotationsVisible ? 1 : .55;
            ToolTipService.SetToolTip(_annotationButton, _annotationsVisible ? "隐藏题目标注" : "显示题目标注");
            ApplyAnnotations(_lastSession);
        };
        _pinButton = CreateIconButton("取消窗口置顶", Symbol.Pin);
        _pinButton.Click += (_, _) => TogglePin();
        _agentButton = CreateIconButton("Agent 能力", Symbol.Emoji2);
        _agentButton.Content = new FontIcon { Glyph = Glyphs.Agent, FontFamily = IconFontStyle.Font, FontSize = 20 };
        BuildAgentMenu(); _agentButton.Flyout = _agentFlyout;
        var dock = CreateIconButton("停靠与收起", Symbol.BackToWindow);
        dock.Content = new FontIcon { Glyph = Glyphs.ChevronRight, FontFamily = IconFontStyle.Font, FontSize = 18 };
        var docking = new MenuFlyout();
        Add("停靠屏幕左侧", () => DockPanel(false)); Add("停靠屏幕右侧", () => DockPanel(true));
        docking.Items.Add(new MenuFlyoutSeparator()); Add("收起为边缘悬浮条", CollapseToEdge);
        dock.ContextFlyout = docking;
        dock.Click += (_, _) => CollapseToEdge();
        ToolTipService.SetToolTip(dock, "收起助教，长按或右键选择停靠位置");
        globals.Children.Add(_annotationButton); globals.Children.Add(_pinButton); globals.Children.Add(_agentButton); globals.Children.Add(dock);
        Grid.SetColumn(globals, 1); toolbar.Children.Add(globals); _root.Children.Add(toolbar);
        void Add(string title, Action action)
        { var item = new MenuFlyoutItem { Text = title }; item.Click += (_, _) => action(); docking.Items.Add(item); }

        var context = new Grid { ColumnSpacing = 8, MinHeight = 40 };
        context.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        context.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        context.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var preview = CreateIconButton("预览和调整题目选区", Symbol.Pictures);
        preview.Content = new Grid { Width = 36, Height = 36, Children = { new SymbolIcon(Symbol.Pictures) { Opacity = .35 }, _contextThumbnail } };
        preview.Flyout = _preview;
        context.Children.Add(preview);
        Grid.SetColumn(_contextTitle, 1); _contextTitle.VerticalAlignment = VerticalAlignment.Center; context.Children.Add(_contextTitle);
        var contextActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var add = CreateIconButton("追加选区", Symbol.Add); add.Click += (_, _) => { _preview.Hide(); AddRegionRequested?.Invoke(); };
        var capture = CreateIconButton("重新获取当前画布", Symbol.Refresh); capture.Click += (_, _) => { _preview.Hide(); RetryRequested?.Invoke(); };
        contextActions.Children.Add(add); contextActions.Children.Add(capture); Grid.SetColumn(contextActions, 2); context.Children.Add(contextActions);
        var strip = new Border { Padding = new Thickness(4), Background = _inputBgBrush, CornerRadius = new CornerRadius(10), Child = context };
        Grid.SetRow(strip, 1); _root.Children.Add(strip);
        Grid.SetRow(_notice, 2); _root.Children.Add(_notice);
        _scroll.Content = _conversation; Grid.SetRow(_scroll, 3); _root.Children.Add(_scroll);
        AutomationProperties.SetName(_scroll, "助教对话");

        var composer = new StackPanel { Spacing = 8 };
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _variantBasic.Click += (_, _) => VariantRequested?.Invoke(false);
        _variantAdvanced.Click += (_, _) => VariantRequested?.Invoke(true);
        foreach (var chip in new[] { _variantBasic, _variantAdvanced })
        { chip.CornerRadius = new CornerRadius(20); chip.FontSize = 12; chips.Children.Add(chip); }
        _summary.CornerRadius = new CornerRadius(20); _summary.FontSize = 12;
        _summary.Click += (_, _) => { if (!_busy && _aiTeachingEnabled) FollowUpRequested?.Invoke("请总结当前题目的关键考点与易错点。"); }; chips.Children.Add(_summary);
        composer.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, ZoomMode = ZoomMode.Disabled });
        var input = new Grid { ColumnSpacing = 4 };
        input.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        input.Children.Add(_followUp); Grid.SetColumn(_send, 1); input.Children.Add(_send);
        _send.VerticalAlignment = VerticalAlignment.Bottom;
        _send.Click += (_, _) => SendFollowUp();
        _followUp.TextCompositionStarted += (_, _) => _composing = true;
        _followUp.TextCompositionEnded += (_, _) => DispatcherQueue.TryEnqueue(() => _composing = false);
        _followUp.KeyDown += (_, args) =>
        {
            bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (args.Key != VirtualKey.Enter || shift || _composing) return;
            SendFollowUp(); args.Handled = true;
        };
        AutomationProperties.SetName(_followUp, "追问内容");
        composer.Children.Add(new Border { Background = _inputBgBrush, BorderBrush = _controlStrokeBrush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(4), Child = input });
        Grid.SetRow(composer, 4); _root.Children.Add(composer);
        _demoBadge = new Border { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
            Background = ThemeBrushes.Resolve(_root, "SystemFillColorCautionBackgroundBrush"),
            Visibility = demoMode ? Visibility.Visible : Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "演示", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) } };
        ToolTipService.SetToolTip(_demoBadge, "尚未配置 AI 服务，当前显示本地示例。");
    }

    private Button CreateSecondaryButton(string text) => new()
    { Content = text, MinHeight = 40, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(8), FontSize = 13 };

    private static TouchIconButton CreateIconButton(string name, Symbol symbol)
    {
        var button = new TouchIconButton { Width = 40, Height = 40, MinWidth = 40, MinHeight = 40, Padding = new Thickness(8),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8), Content = new SymbolIcon(symbol) { Symbol = symbol } };
        AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
        return button;
    }
}
