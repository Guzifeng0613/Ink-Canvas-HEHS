using System.Diagnostics;
using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Features.Board;
using InkCanvas.Host.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Diagnostics;

internal static class LibraryRegression
{
    public static async Task Run(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, "settings"));
        services.Settings.Startup.IsAutoUpdate = false; services.Settings.PowerPoint.PowerPointSupport = false;
        services.Settings.Archive.DefaultDirectory = Path.Combine(directory, "文稿");
        services.Settings.Automation.MinimumAutomationStrokeNumber = int.MaxValue;
        Directory.CreateDirectory(services.Settings.Archive.DefaultDirectory);
        bool disabled = Environment.GetCommandLineArgs().Contains("--library-disabled");
        if (disabled) services.ExtensionPreferences.DisabledModules.Add("inkcanvas.library");
        using var shell = new ShellController(services); shell.Start(); shell.StartInClickThrough();
        try
        {
            var library = services.Modules.GetExport<IBoardLibraryService>();
            if (disabled)
            {
                Check("disabled library has no export or toolbar command", library is null && !shell.ExtensionCommands.Any(command => command.PluginId == "inkcanvas.library"));
                string standalone = Path.Combine(directory, "without-library.icink");
                await services.Board.SaveBoardAsync(standalone);
                Check("base board storage works without the library plugin", File.Exists(standalone));
                return;
            }
            Check("native library module loaded", library is not null);
            if (library is null) throw new InvalidOperationException(string.Join("\n", services.Modules.Status));
            shell.ToggleBoard();
            services.Board.Background = BoardBackground.White;
            for (int n = 0; n < 1000; n++)
            {
                var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 4, Color = InkColor.FromPackedArgb(0xFFE24D40) });
                stroke.AddPoint(20 + n % 50 * 20, 20 + n / 50 * 20); stroke.AddPoint(35 + n % 50 * 20, 34 + n / 50 * 20); stroke.Complete();
                services.Board.Current.Document.Add(stroke);
            }
            services.Board.InsertPageAfterCurrent(); services.Board.Background = BoardBackground.Grid;
            var line = new InkStroke(new StrokeAttributes { BaseWidth = 8 }); line.AddPoint(20, 30); line.AddPoint(500, 340); line.Complete(); services.Board.Current.Document.Add(line);
            services.Board.AddMedia(new() { Data = new byte[32 * 1024 * 1024], Name = "large.mp4" });
            string fixture = Path.Combine(services.Settings.Archive.DefaultDirectory, "函数课堂.icink");
            var watch = Stopwatch.StartNew(); await services.Board.SaveBoardAsync(fixture);
            var cover = await BoardPreview.ReadAsync(fixture);
            Check("new package embeds a bounded real cover", cover.Info?.PageCount == 2 && cover.Cover is { Length: > 100 and < 2 * 1024 * 1024 }, new { milliseconds = watch.ElapsedMilliseconds, bytes = cover.Cover?.Length });
            await File.WriteAllBytesAsync(Path.Combine(directory, "embedded-cover.png"), cover.Cover ?? Array.Empty<byte>());
            var secondPage = await BoardService.ReadPagePreviewAsync(fixture, 1);
            Check("on-demand page preview skips a 32 MB video", secondPage.PageCount == 2 && secondPage.Png is { Length: > 100 });
            string sub = Directory.CreateDirectory(Path.Combine(services.Settings.Archive.DefaultDirectory, "数学")).FullName;
            File.Copy(fixture, Path.Combine(sub, "二次函数.icink"), true);
            Check("normal toolbar exposes library command", shell.ExtensionCommands.Any(command => command.PluginId == "inkcanvas.library"));
            library.Open();
            Window window = await LibraryWindowAsync();
            var files = Descendants(window.Content).OfType<GridView>().Single();
            await Until(() => files.Items.Count == 2);
            Check("WinUI library lists folders and whiteboards", files.Items.Count == 2 && Descendants(window.Content).OfType<WindowFrame>().Any()
                && window.SystemBackdrop is MicaBackdrop);
            files.SelectedItem = files.Items.Cast<object>().Single(item => Value(item, "Path") == fixture);
            await Task.Delay(200);
            await BoardWindowRegression.SaveVisual((FrameworkElement)window.Content, Path.Combine(directory, "library-initial.png"));
            var preview = (Image)window.GetType().GetField("_preview", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(window);
            await Until(() => preview.Source is not null);
            Check("selecting a real document displays its preview", preview.Source is not null);
            foreach (int theme in new[] { 1, 2 })
            {
                WindowChrome.ApplyTheme(window, theme); await Task.Delay(150);
                Check("library renders in theme " + theme, await BoardWindowRegression.SaveVisual((FrameworkElement)window.Content, Path.Combine(directory, "library-" + theme + ".png")));
            }
            Invoke(Descendants(window.Content).OfType<Button>().Single(button => button.Content?.ToString() == "›"));
            await Until(() => Descendants(window.Content).OfType<TextBlock>().Any(text => text.Text == "2 / 2"));
            Check("preview page navigation works", true);
            Invoke(Descendants(window.Content).OfType<Button>().Single(button => button.Content?.ToString() == "收藏 / 取消收藏"));
            await Task.Delay(150);
            var locations = Descendants(window.Content).OfType<ListView>().Single(); locations.SelectedItem = locations.Items.Cast<object>().Single(item => Value(item, "Title") == "收藏");
            await Until(() => files.Items.Count == 1);
            Check("favorites filter uses persisted file paths", Value(files.Items[0], "Path") == fixture);
            locations.SelectedItem = locations.Items.Cast<object>().Single(item => Value(item, "Path") == services.Settings.Archive.DefaultDirectory);
            await Until(() => files.Items.Count == 2);
            var search = Descendants(window.Content).OfType<AutoSuggestBox>().Single(); search.Text = "二次";
            // Programmatic text changes are refreshed via the same user-facing refresh command.
            Invoke(Descendants(window.Content).OfType<Button>().Single(button => button.Content?.ToString() == "刷新"));
            await Until(() => files.Items.Count == 1);
            Check("recursive folder search finds matching whiteboards", Value(files.Items[0], "Path") == Path.Combine(sub, "二次函数.icink"));
            window.Close();
            var open = library.PickOpenAsync(new[] { ".icink" }); window = await LibraryWindowAsync();
            files = Descendants(window.Content).OfType<GridView>().Single(); await Until(() => files.Items.Count == 2);
            files.SelectedItem = files.Items.Cast<object>().Single(item => Value(item, "Path") == fixture);
            Invoke(Descendants(window.Content).OfType<Button>().Single(button => button.Content?.ToString() == "打开"));
            Check("open picker returns selected path", await open.WaitAsync(TimeSpan.FromSeconds(5)) == fixture);
            var cancelled = library.PickOpenAsync(new[] { ".icink" }); window = await LibraryWindowAsync(); window.Close();
            Check("cancel resolves picker without altering the board", await cancelled is null && services.Board.PageCount == 2);
            shell.SaveStrokes(); window = await LibraryWindowAsync();
            var saveButton = Descendants(window.Content).OfType<Button>().Single(button => button.Content?.ToString() == "保存");
            await Until(() => saveButton.IsEnabled);
            var name = Descendants(window.Content).OfType<TextBox>().Single(input => input.Header?.ToString() == "文稿名称"); name.Text = "课堂另存";
            Invoke(saveButton);
            string saved = Path.Combine(services.Settings.Archive.DefaultDirectory, "课堂另存.icink");
            await Until(() => File.Exists(saved) && services.Archives.LastSave?.Path == saved);
            var reopened = new BoardService();
            try { await reopened.LoadBoardAsync(saved); Check("normal SaveStrokes flow preserves editable pages", reopened.PageCount == 2 && reopened.Pages[0].Document.Count == 1000 && reopened.Pages[1].Document.Count == 1); }
            finally { foreach (var page in reopened.Pages) page.Dispose(); }
            library.Open(); window = await LibraryWindowAsync();
            locations = Descendants(window.Content).OfType<ListView>().Single();
            await Until(() => locations.Items.Count >= 3); locations.SelectedItem = locations.Items.Cast<object>().Single(item => Value(item, "Title") == "最近使用");
            files = Descendants(window.Content).OfType<GridView>().Single();
            await Until(() => files.Items.Count > 0);
            Check("saved documents appear in recent files", files.Items.Cast<object>().Any(item => Value(item, "Path") == saved));
            window.Close();
        }
        catch (Exception ex) { checks.Add(new { check = "library regression exception", passed = false, detail = ex.ToString() }); }
        finally
        {
            foreach (var window in shell.OpenWindows.Where(window => window.GetType().Name == "LibraryWindow").ToArray()) window.Close();
            services.Settings.Archive.SaveOnExit = false;
            foreach (var board in shell.OpenWindows.OfType<Views.BoardWindow>().ToArray()) board.Close();
            await File.WriteAllTextAsync(Path.Combine(directory, "library-check.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            shell.Dispose(); Application.Current.Exit();
        }
        void Check(string name, bool passed, object detail = null) => checks.Add(new { check = name, passed, detail });
        async Task<Window> LibraryWindowAsync()
        {
            await Until(() => shell.OpenWindows.Any(window => window.GetType().Name == "LibraryWindow"));
            var window = shell.OpenWindows.Single(window => window.GetType().Name == "LibraryWindow");
            await Until(() => (window.Content as FrameworkElement)?.XamlRoot is not null);
            return window;
        }
    }
    private static string Value(object item, string property) => item.GetType().GetProperty(property)?.GetValue(item)?.ToString();
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("文稿库验证超时。"); await Task.Delay(40); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }
}
