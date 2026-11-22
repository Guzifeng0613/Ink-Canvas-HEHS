using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Extensibility;
using InkCanvas.Extensions;
using InkCanvas.Host.Contracts;
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
            Check("native WinUI frame and Mica are installed", window.Content is WindowFrame && window.ExtendsContentIntoTitleBar && window.SystemBackdrop is MicaBackdrop);
            shell.OpenAiAssistant();
            Check("reopening reuses the active workspace", shell.OpenWindows.Count(item => item.GetType().Name == "AssistantWindow") == 1);
            for (int theme = 1; theme <= 2; theme++)
            {
                WindowChrome.ApplyTheme(window, theme);
                await Task.Delay(100);
                var frame = (WindowFrame)window.Content;
                Check("theme and caption match " + theme, frame.ActualTheme == (theme == 1 ? ElementTheme.Light : ElementTheme.Dark) && frame.Header.ActualTheme == frame.ActualTheme);
                var text = Descendants(frame).OfType<TextBlock>().First(item => item.Text == "二次方程");
                var color = ((SolidColorBrush)text.Foreground).Color;
                Check("answer text is readable " + theme, color.A > 200 && (theme == 1 ? color.R < 80 : color.R > 200));
                var background = frame.Background;
                frame.Background = new SolidColorBrush(theme == 1 ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
                try { await BoardWindowRegression.SaveVisual(frame, Path.Combine(directory, "assistant-" + theme + ".png")); }
                finally { frame.Background = background; }
            }
            window.Close();
            Check("closing clears the plugin session window", !assistant.IsOpen);

            // Empty endpoint exercises the complete capture, streaming and cancellation path locally.
            Environment.SetEnvironmentVariable("AI_ENDPOINT", null);
            var screen = Platform.System.ScreenService.WorkArea;
            await assistant.OpenRegionAsync(new(screen.X, screen.Y, 240, 140));
            await Task.Delay(1200);
            Check("capture starts a plugin-owned session", assistant.IsOpen);
            window = shell.OpenWindows.Single(item => item.GetType().Name == "AssistantWindow");
            var split = Descendants(window.Content).OfType<SplitView>().Single();
            var originalSize = window.AppWindow.Size;
            window.AppWindow.Resize(new SizeInt32(650, 600));
            await Task.Delay(120);
            Check("narrow window collapses its source sidebar", split.DisplayMode == SplitViewDisplayMode.Overlay && !split.IsPaneOpen);
            window.AppWindow.Resize(originalSize);
            await Task.Delay(120);
            var paste = Descendants(window.Content).OfType<Button>().Single(item => item.Content?.ToString() == "贴回白板");
            Check("stream completes with paste available", paste.IsEnabled && paste.Visibility == Visibility.Visible);
            ((IInvokeProvider)new ButtonAutomationPeer(paste).GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(350);
            Check("plugin result pastes through the board host", services.Board.CurrentImages.Any(item => item.Name == "AI 助教"));
            int count = services.Board.CurrentImages.Count;
            services.Board.Current.Undo();
            Check("pasted AI card remains undoable", services.Board.CurrentImages.Count == count - 1);
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
