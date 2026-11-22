using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Extensibility;
using InkCanvas.Extensions;
using InkCanvas.Host.Contracts;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Ink;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Graphics;

namespace InkCanvas.App.Diagnostics;

internal static class AiPluginRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, "settings"));
        services.Settings.Startup.IsAutoUpdate = false;
        services.Settings.PowerPoint.PowerPointSupport = false;
        services.Settings.Automation.MinimumAutomationStrokeNumber = int.MaxValue;
        bool disabled = Environment.GetCommandLineArgs().Contains("--without-ai");
        if (disabled) services.ExtensionPreferences.DisabledModules.Add("inkcanvas.ai-assistant");
        using var shell = new ShellController(services);
        shell.Start();
        shell.StartInClickThrough();
        try
        {
            await Task.Delay(150);
            var status = services.Modules.Status.Single(item => item.Id == "inkcanvas.ai-assistant");
            bool available = status.State == ExtensionState.Running;
            Check("AI availability matches native plugin state", shell.AiAssistantAvailable == available, status);
            Check("host and platform have no AI engine or HTTP client", typeof(AppServices).Assembly.GetType("InkCanvas.App.Views.AssistantWindow") is null
                && typeof(Core.Ink.InkStroke).Assembly.GetType("InkCanvas.Core.AiAssistant.AssistantMachine") is null
                && typeof(Platform.System.ScreenService).Assembly.GetType("InkCanvas.Platform.Ai.AiClient") is null);
            Check("application has no static AI plugin dependency", !typeof(AppServices).Assembly.GetReferencedAssemblies().Any(item => item.Name == "InkCanvas.Modules.AiAssistant"));
            var bar = shell.OpenWindows.OfType<FloatingBarWindow>().Single();
            var button = Descendants(bar.Content).OfType<TouchIconButton>().Single(item => item.Label == "AI 助教");
            Check("primary toolbar hides unavailable AI", (button.Visibility == Visibility.Visible) == available);
            Check("plugin menu matches AI availability", shell.ExtensionCommands.Any(item => item.PluginId == "inkcanvas.ai-assistant") == available);
            if (!available)
            {
                shell.OpenAiAssistant();
                Check("missing or disabled plugin opens no window", !shell.OpenWindows.Any(item => item.GetType().Name == "AssistantWindow"));
                Check("disabled AI assembly is never loaded", !AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == "InkCanvas.Modules.AiAssistant"));
                return;
            }

            var assistant = services.Modules.GetExport<IAiAssistantService>();
            assistant.Review(1, 1, 0, "# 二次方程\n已知 x² − 5x + 6 = 0。\n将左边因式分解为 (x − 2)(x − 3)，解得 x = 2 或 x = 3。");
            await Task.Delay(300);
            var window = shell.OpenWindows.Single(item => item.GetType().Name == "AssistantWindow");
            Check("AI window belongs to the optional plugin", window.GetType().Assembly.GetName().Name == "InkCanvas.Modules.AiAssistant");
            Check("native WinUI frame and Acrylic are installed", window.Content is WindowFrame && window.ExtendsContentIntoTitleBar && window.SystemBackdrop is DesktopAcrylicBackdrop);
            Check("assistant starts at 380 DIP", Math.Abs(window.AppWindow.Size.Width / WindowChrome.GetDpiScale(window) - 380) < 3);
            shell.OpenAiAssistant();
            Check("reopening reuses the active workspace", shell.OpenWindows.Count(item => item.GetType().Name == "AssistantWindow") == 1);
            for (int theme = 1; theme <= 2; theme++)
            {
                WindowChrome.ApplyTheme(window, theme);
                await Task.Delay(100);
                var frame = (WindowFrame)window.Content;
                Check("theme and caption match " + theme, frame.ActualTheme == (theme == 1 ? ElementTheme.Light : ElementTheme.Dark) && frame.Header.ActualTheme == frame.ActualTheme);
                var text = Descendants(frame).OfType<RichTextBlock>().First();
                var color = ((SolidColorBrush)text.Foreground).Color;
                Check("answer text is readable " + theme, color.A > 200 && (theme == 1 ? color.R < 80 : color.R > 200));
                var background = frame.Background;
                frame.Background = new SolidColorBrush(theme == 1 ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                try { await BoardWindowRegression.SaveVisual(frame, Path.Combine(directory, "assistant-" + theme + ".png")); }
                finally { frame.Background = background; }
            }
            await CheckPanel(window, directory, checks);
            window.Close();
            Check("closing clears the plugin session window", !assistant.IsOpen);

            // Empty endpoint exercises the complete capture, streaming and cancellation path locally.
            Environment.SetEnvironmentVariable("AI_ENDPOINT", null);
            var screen = Platform.System.ScreenService.WorkArea;
            await assistant.OpenRegionAsync(new(screen.X, screen.Y, 240, 140));
            await Task.Delay(1600);
            Check("capture starts a plugin-owned session", assistant.IsOpen);
            window = shell.OpenWindows.Single(item => item.GetType().Name == "AssistantWindow");
            var previewButton = Descendants(window.Content).OfType<Button>().Single(item => AutomationProperties.GetName(item) == "预览和调整题目选区");
            previewButton.Flyout.ShowAt(previewButton); await Task.Delay(100);
            var previewPopup = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot)
                .Single(popup => Descendants(popup.Child).OfType<TextBlock>().Any(item => item.Text == "当前题目选区"));
            Check("context thumbnail opens a separate source preview", Descendants(previewPopup.Child).OfType<Image>().Any()
                && Descendants(previewPopup.Child).OfType<TextBlock>().Any(item => item.Text.Contains("局部纠错")));
            previewButton.Flyout.Hide();
            var originalSize = window.AppWindow.Size;
            double dpi = WindowChrome.GetDpiScale(window);
            window.AppWindow.Resize(new SizeInt32((int)(360 * dpi), (int)(600 * dpi)));
            await Task.Delay(120);
            Check("narrow assistant uses a single conversation column", !Descendants(window.Content).OfType<SplitView>().Any());
            window.AppWindow.Resize(originalSize);
            await Task.Delay(120);
            var paste = Descendants(window.Content).OfType<Button>().Single(item => AutomationProperties.GetName(item) == "贴回白板");
            Check("stream completes with paste available", paste.IsEnabled && paste.Visibility == Visibility.Visible);
            var firstCard = Descendants(window.Content).OfType<FrameworkElement>().Single(item => item.GetType().Name == "AssistantAnswerCard");
            var reveal = Descendants(firstCard).OfType<Button>().Single(item => AutomationProperties.GetName(item).StartsWith("展开下一步"));
            Invoke(reveal); await Task.Delay(80);
            Check("next step expands inside its response", AutomationProperties.GetName(reveal).Contains("剩余 1 步"));
            var input = Descendants(window.Content).OfType<TextBox>().Single(item => AutomationProperties.GetName(item) == "追问内容");
            input.Text = "再解释一次";
            Invoke(Descendants(window.Content).OfType<Button>().Single(item => AutomationProperties.GetName(item) == "发送 (Enter)"));
            await Task.Delay(1600);
            Check("follow-up creates an independent response", Descendants(window.Content).OfType<FrameworkElement>().Count(item => item.GetType().Name == "AssistantAnswerCard") == 2);
            Invoke(reveal); await Task.Delay(60);
            Check("earlier response can reveal its own remaining step", reveal.Visibility == Visibility.Collapsed
                && Descendants(firstCard).OfType<TextBlock>().Any(item => item.Text == "E = mc²"));
            for (int theme = 1; theme <= 2; theme++)
            {
                WindowChrome.ApplyTheme(window, theme); await Task.Delay(80);
                var frame = (WindowFrame)window.Content;
                var background = frame.Background;
                frame.Background = new SolidColorBrush(theme == 1 ? Microsoft.UI.Colors.WhiteSmoke : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                try { await BoardWindowRegression.SaveVisual(frame, Path.Combine(directory, $"assistant-conversation-{theme}.png")); }
                finally { frame.Background = background; }
            }
            ((IInvokeProvider)new ButtonAutomationPeer(paste).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(350);
            Check("plugin result pastes through the board host", services.Board.CurrentImages.Any(item => item.Name == "AI 助教"));
            int count = services.Board.CurrentImages.Count;
            services.Board.Current.Undo();
            Check("pasted AI card remains undoable", services.Board.CurrentImages.Count == count - 1);
            await CheckAgent(window, services, checks);
            shell.OpenWindows.OfType<BoardWindow>().Single().Close();
            await assistant.OpenRegionAsync(new(screen.X, screen.Y, 240, 140));
            services.Modules.Invoke<IAiAssistantService>(_ => throw new InvalidOperationException("AI plugin regression failure"));
            await Task.Delay(100);
            Check("failed plugin closes its active request and window", !assistant.IsOpen && !shell.AiAssistantAvailable);
            Check("failure removes AI toolbar and command entries", button.Visibility == Visibility.Collapsed
                && !shell.ExtensionCommands.Any(item => item.PluginId == "inkcanvas.ai-assistant"));
            services.OverlayInk.Tool = Ink.InkTool.Pen;
            Check("core canvas remains available after AI stops", services.OverlayInk.Tool == Ink.InkTool.Pen);
        }
        catch (Exception ex) { checks.Add(new { check = "AI plugin regression exception", passed = false, detail = ex.ToString() }); }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "ai-plugin.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            var windows = shell.OpenWindows.ToArray();
            shell.Dispose();
            foreach (var window in windows) if (window.AppWindow.IsVisible) window.Close();
            Application.Current.Exit();
        }
        void Check(string name, bool passed, object detail = null) => checks.Add(new { check = name, passed, detail });
    }

    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task CheckPanel(Window window, string directory, List<object> checks)
    {
        var frame = (WindowFrame)window.Content;
        foreach (int theme in new[] { 1, 2 })
        {
            WindowChrome.ApplyTheme(window, theme);
            foreach (int width in new[] { 360, 380, 400 })
            {
                double dpi = WindowChrome.GetDpiScale(window);
                window.AppWindow.Resize(new((int)(width * dpi), (int)(640 * dpi))); await Task.Delay(100);
                string[] controls = { "讲解方式", "显示或隐藏题目标注", "取消窗口置顶", "Agent 能力", "停靠与收起", "发送 (Enter)", "预览和调整题目选区", "追加选区", "重新获取当前画布" };
                var buttons = Descendants(frame).OfType<Button>().Where(button => controls.Contains(AutomationProperties.GetName(button))).ToArray();
                Check($"touch controls fit {width} DIP theme {theme}", buttons.Length == controls.Length && buttons.All(button =>
                {
                    var bounds = button.TransformToVisual(frame).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    return button.ActualWidth >= 39 && button.ActualHeight >= 39 && bounds.Left >= -1 && bounds.Right <= frame.ActualWidth + 1
                        && bounds.Top >= -1 && bounds.Bottom <= frame.ActualHeight + 1;
                }));
                var background = frame.Background;
                frame.Background = new SolidColorBrush(theme == 1 ? Microsoft.UI.Colors.WhiteSmoke : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                try { await BoardWindowRegression.SaveVisual(frame, Path.Combine(directory, $"assistant-{theme}-{width}.png")); }
                finally { frame.Background = background; }
            }
        }
        var agentButton = Descendants(frame).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Agent 能力");
        agentButton.Flyout.ShowAt(agentButton); await Task.Delay(80);
        var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Single(popup => Descendants(popup.Child).OfType<ToggleSwitch>().Any());
        Check("Agent is disabled with a reason before provider connection", Descendants(popup.Child).OfType<ToggleSwitch>().Single().IsEnabled == false
            && Descendants(popup.Child).OfType<TextBlock>().Any(text => text.Text == "尚未接入 Agent 服务"));
        agentButton.Flyout.Hide();
        var reading = new AssistantSession { State = AssistantState.Streaming, TeachingMode = AssistantTeachingMode.Full,
            Text = "### 阅读定位\n\n" + string.Join("\n\n", Enumerable.Range(1, 40).Select(index => $"第 {index} 段推导，用于核对流式阅读位置。"))
                + "\n\n$$" + string.Join(" + ", Enumerable.Repeat("x^{20}", 30)) + "$$\n\n```text\n" + new string('a', 300) + "\n```" };
        window.GetType().GetMethod("BeginTurn").Invoke(window, new object[] { "阅读回归" });
        window.GetType().GetMethod("Apply").Invoke(window, new object[] { reading, "", true });
        await Task.Delay(100);
        var conversation = Descendants(frame).OfType<ScrollViewer>().Single(scroll => AutomationProperties.GetName(scroll) == "助教对话");
        var current = Descendants(frame).OfType<FrameworkElement>().Last(item => item.GetType().Name == "AssistantAnswerCard");
        var heading = Descendants(current).OfType<RichTextBlock>().First();
        var horizontal = Descendants(current).OfType<ScrollViewer>().Where(scroll => scroll.HorizontalScrollMode == ScrollMode.Enabled).ToArray();
        Check("long formulas and code scroll within the narrow response", horizontal.Length >= 2 && horizontal.All(scroll => scroll.ScrollableWidth > 0 && scroll.ActualWidth < frame.ActualWidth));
        conversation.ChangeView(null, 80, null, true); await Task.Delay(80);
        double position = conversation.VerticalOffset;
        reading.Text += "\n\n新生成的说明";
        window.GetType().GetMethod("Apply").Invoke(window, new object[] { reading, "", true }); await Task.Delay(100);
        Check("streaming preserves a teacher's earlier reading position", Math.Abs(position - conversation.VerticalOffset) < 2);
        Check("streaming reuses completed content blocks", ReferenceEquals(heading, Descendants(current).OfType<RichTextBlock>().First()));
        conversation.ChangeView(null, conversation.ScrollableHeight, null, true); await Task.Delay(80);
        reading.Text += "\n\n继续追加内容";
        window.GetType().GetMethod("Apply").Invoke(window, new object[] { reading, "", true }); await Task.Delay(100);
        Check("streaming follows new content while already at the bottom", conversation.ScrollableHeight - conversation.VerticalOffset < 2);
        window.GetType().GetMethod("CollapseToEdge").Invoke(window, null); await Task.Delay(80);
        Check("collapse hides assistant while preserving its session", !window.AppWindow.IsVisible);
        window.GetType().GetMethod("ShowPanel").Invoke(window, null); await Task.Delay(100);
        Check("restore returns to the same response", window.AppWindow.IsVisible && Descendants(frame).OfType<RichTextBlock>().Any());
        void Check(string name, bool passed) => checks.Add(new { check = name, passed });
    }

    private static async Task CheckAgent(Window window, AppServices services, List<object> checks)
    {
        var bridge = services.Modules.GetExport<IAssistantAgentBridge>();
        var provider = new DiagnosticAgent();
        using (bridge.Register(provider))
        {
            await Task.Delay(80);
            var frame = (WindowFrame)window.Content;
            var button = Descendants(frame).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Agent 能力");
            button.Flyout.ShowAt(button); await Task.Delay(80);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(frame.XamlRoot).Single(popup => Descendants(popup.Child).OfType<ToggleSwitch>().Any());
            var toggle = Descendants(popup.Child).OfType<ToggleSwitch>().Single();
            Check("Agent registration enables its UI entry", toggle.IsEnabled); toggle.IsOn = true; button.Flyout.Hide();
            var input = Descendants(frame).OfType<TextBox>().Single(); input.Text = "创建一个图形";
            int count = services.Board.Current.Document.Strokes.Count;
            Invoke(Descendants(frame).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "发送 (Enter)"));
            await Task.Delay(450);
            Check("Agent reads a detached canvas snapshot and creates objects", provider.Read && provider.Created && services.Board.Current.Document.Strokes.Count == count + 1);
            bool expired = false;
            try { await provider.Canvas.ReadAsync(CancellationToken.None); }
            catch (OperationCanceledException) { expired = true; }
            Check("finished Agent runs cannot keep operating the canvas", expired);
            services.Board.Current.Undo();
            Check("Agent-created objects use canvas undo", services.Board.Current.Document.Strokes.Count == count);
        }
        await Task.Delay(80);
        Check("Agent unregister resets the UI mode", !(bool)window.GetType().GetProperty("AgentMode").GetValue(window));
        void Check(string name, bool passed) => checks.Add(new { check = name, passed });
    }

    private sealed class DiagnosticAgent : IAssistantAgentProvider
    {
        public string Name => "本机验收";
        public bool Read { get; private set; }
        public bool Created { get; private set; }
        public IAssistantAgentCanvas Canvas { get; private set; }
        public async Task<string> RunAsync(AssistantAgentRequest request, IAssistantAgentCanvas canvas,
            IProgress<AssistantAgentProgress> progress, CancellationToken cancellationToken)
        {
            Canvas = canvas;
            var snapshot = await canvas.ReadAsync(cancellationToken); Read = snapshot.Kind == "board";
            var stroke = new InkStroke(new StrokeAttributes()); stroke.AddPoint(40, 40); stroke.AddPoint(140, 140, timeMs: 10); stroke.Complete();
            progress.Report(new("正在创建对象", "图形"));
            Created = await canvas.CreateShapesAsync(new[] { stroke }, cancellationToken);
            return "已创建一个可撤销的画布对象。";
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
