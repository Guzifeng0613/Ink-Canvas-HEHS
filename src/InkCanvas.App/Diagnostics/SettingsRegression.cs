using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Settings;
using InkCanvas.Features.Board;
using InkCanvas.Ink;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Diagnostics;

internal static class SettingsRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, Guid.NewGuid().ToString("N")));
        var bar = new FloatingBarWindow();
        bar.Bar.Settings = services.Settings;
        bar.Bar.Controller = services.OverlayInk;
        bar.Bar.Build();
        WindowChrome.ShowWithoutActivation(bar);
        SettingsWindow window = null;
        try
        {
            string original = JsonSerializer.Serialize(services.Settings);
            Open();
            await Task.Delay(600);
            Check("opening settings preserves stored values", JsonSerializer.Serialize(services.Settings) == original);
            Check("settings permit keyboard activation", !NativeMethods.HasExStyle(WindowChrome.GetHwnd(window), NativeMethods.WS_EX_NOACTIVATE));

            var toggles = new (string Label, Func<bool> Get)[]
            {
                ("上传图片时将画质压缩至 1080P 及以下", () => services.Settings.Canvas.IsCompressPicturesUploaded),
                ("浮动工具栏显示文字注释", () => services.Settings.Appearance.IsEnableDisPlayFloatBarText),
                ("浮动工具栏背景色", () => services.Settings.Appearance.IsColorfulViewboxFloatingBar),
                ("按钮背景透明", () => services.Settings.Appearance.IsTransparentButtonBackground),
                ("显示退出按钮", () => services.Settings.Appearance.IsShowExitButton),
                ("显示橡皮按钮", () => services.Settings.Appearance.IsShowEraserButton),
                ("显示隐藏控制按钮", () => services.Settings.Appearance.IsShowHideControlButton),
                ("显示左右切换按钮", () => services.Settings.Appearance.IsShowLRSwitchButton),
                ("显示手指模式切换按钮", () => services.Settings.Appearance.IsShowModeFingerToggleSwitch),
            };
            foreach (var item in toggles)
            {
                var toggle = Find<ToggleSwitch>(item.Label);
                bool before = item.Get();
                var peer = new ToggleSwitchAutomationPeer(toggle);
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                bool changed = item.Get() != before;
                Check(item.Label, changed && toggle.IsOn == item.Get());
                ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
                Check(item.Label + " restores", item.Get() == before);
            }
            var showEraser = Find<ToggleSwitch>("显示橡皮按钮");
            showEraser.IsOn = false;
            Check("eraser visibility applies immediately", bar.Bar.GetToolButton(InkTool.Eraser).Visibility == Visibility.Collapsed);
            showEraser.IsOn = true;
            Check("eraser visibility restores", bar.Bar.GetToolButton(InkTool.Eraser).Visibility == Visibility.Visible);

            var eraser = Find<ComboBox>("橡皮模式");
            eraser.SelectedIndex = 2;
            Check("whole stroke eraser applies", services.OverlayInk.EraserMode == InkEraserMode.WholeStroke);
            eraser.SelectedIndex = 1;
            Check("area eraser applies", services.OverlayInk.EraserMode == InkEraserMode.Area);
            eraser.SelectedIndex = 0;
            var palette = (EraserPalette)bar.Bar.CreateEraserPanelContent();
            var lasso = Descendants(palette).OfType<Button>().Single(button => AutomationProperties.GetName(button).Contains("套索擦"));
            Invoke(lasso);
            services.ApplySettings();
            Check("manual eraser choice survives other settings", services.OverlayInk.EraserMode == InkEraserMode.Lasso);

            var boardColor = Find<ToggleSwitch>("白板背景配色");
            boardColor.IsOn = true;
            Check("whiteboard color applies", services.Board.Background == BoardBackground.White);
            boardColor.IsOn = false;
            Check("blackboard color applies", services.Board.Background == BoardBackground.Black);

            CheckShortcut("工具栏缩放快捷值 125%", "浮动工具栏 UI 缩放 (%)", 125, () => services.Settings.Appearance.FloatingBarScale);
            CheckShortcut("画板缩放快捷值 80%", "画板 UI 缩放 (%)", 80, () => services.Settings.Appearance.BlackboardScale);
            Find<Slider>("浮动工具栏底部边距 (像素)").Value = 40;
            CheckShortcut("底部边距快捷值 重置为 100", "浮动工具栏底部边距 (像素)", 100, () => services.Settings.Appearance.FloatingBarBottomMargin);

            Find<Slider>("荧光笔粗细").Value = 120;
            services.OverlayInk.Tool = InkTool.Highlighter;
            Check("extended highlighter width applies", services.OverlayInk.CurrentAttributes.BaseWidth == 60);
            services.OverlayInk.Tool = InkTool.Pen;
            Check("highlighter width does not change pen", services.OverlayInk.CurrentAttributes.BaseWidth == services.Settings.Canvas.InkWidth);
            var alpha = Find<Slider>("荧光笔透明度");
            alpha.Value = 180;
            Check("highlighter alpha applies", services.OverlayInk.CurrentAttributes.HighlighterAlpha == 180);
            var path = Find<TextBox>("墨迹、截图自动保存位置");
            path.Text = Path.Combine(directory, "typed-path");
            await Task.Delay(450);
            Check("typed path automatically saves", services.Store.Load().Automation.AutoSavedStrokesLocation == path.Text);
            Invoke(Find<Button>("将自动保存目录设置为文档目录"));
            Check("path shortcut refreshes editor", path.Text == Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            foreach (int themeIndex in new[] { 1, 0, 2 })
            {
                Find<ComboBox>("颜色主题").SelectedIndex = themeIndex;
                await Task.Delay(100);
                Check("theme rebuild preserves edited values " + themeIndex,
                    Find<Slider>("浮动工具栏 UI 缩放 (%)").Value == 125
                    && Find<Slider>("荧光笔透明度").Value == 180);
            }

            string lastPath = Path.Combine(directory, "close-before-debounce");
            Find<TextBox>("墨迹、截图自动保存位置").Text = lastPath;
            window.Close();
            window = null;
            Check("closing flushes pending text", services.Store.Load().Automation.AutoSavedStrokesLocation == lastPath);
            string persisted = JsonSerializer.Serialize(services.Store.Load());
            Check("all edited values persist", persisted == JsonSerializer.Serialize(services.Settings));
            Open();
            await Task.Delay(150);
            Check("reopening restores saved controls", Find<TextBox>("墨迹、截图自动保存位置").Text == lastPath
                && Find<Slider>("浮动工具栏 UI 缩放 (%)").Value == 125 && Find<ComboBox>("橡皮模式").SelectedIndex == 0);

            void Open()
            {
                window = new SettingsWindow(services);
                window.SettingsChanged += bar.Bar.RefreshSettingsVisuals;
                window.Activate();
            }
            T Find<T>(string label) where T : DependencyObject
            {
                T FindOnPage() => Descendants(window.Content).OfType<T>().SingleOrDefault(control => AutomationProperties.GetName(control) == label);
                var result = FindOnPage();
                if (result is not null) return result;
                foreach (string category in new[] { "启动选项", "画板选项", "手势选项", "墨迹识别选项", "外观选项", "PPT 选项", "高级选项", "自动选项", "抽奖（移植附加）" })
                {
                    window.SelectCategory(category);
                    ((FrameworkElement)window.Content).UpdateLayout();
                    result = FindOnPage();
                    if (result is not null) return result;
                }
                throw new InvalidOperationException("Missing setting " + label);
            }
            void Check(string name, bool passed) => checks.Add(new { check = name, passed });
            void CheckShortcut(string button, string slider, double expected, Func<double> get)
            {
                Invoke(Find<Button>(button));
                Check(button, Find<Slider>(slider).Value == expected && get() == expected);
            }
        }
        catch (Exception exception) { checks.Add(new { check = "settings regression exception", passed = false, error = exception.ToString() }); }
        finally
        {
            window?.Close();
            bar.Close();
            File.WriteAllText(Path.Combine(directory, "settings.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
