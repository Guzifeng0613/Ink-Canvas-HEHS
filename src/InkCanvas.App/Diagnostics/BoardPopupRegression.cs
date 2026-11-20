using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Features.Board;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Diagnostics;

internal static class BoardPopupRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, "settings"));
        var window = new BoardWindow(services);
        try
        {
            window.Activate();
            await Task.Delay(300);
            var root = (FrameworkElement)window.Content;
            foreach (var background in new[] { BoardBackground.White, BoardBackground.Black })
            foreach (double scale in new[] { 50d, 100d, 150d })
            {
                services.Board.SetBackground(background);
                services.Settings.Appearance.BlackboardScale = scale;
                window.RefreshUiScale();
                await Task.Delay(80);
                var more = Descendants(root).OfType<Button>().First(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "更多"));
                Invoke(more);
                await Task.Delay(200);
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                    .Single(item => Descendants(item.Child).OfType<TextBlock>().Any(text => text.Text == "更多功能"));
                var presenter = Descendants(popup.Child).OfType<FlyoutPresenter>().Single();
                string name = $"{background}-{scale}";
                CheckSurface(presenter, name);
                var view = (Viewbox)presenter.Content;
                bool previewAvailable = await BoardWindowRegression.SaveVisual((FrameworkElement)view.Child, Path.Combine(directory, $"{name}.png"));
                checks.Add(new { check = "more menu preview", name, available = previewAvailable });
                services.Settings.Appearance.BlackboardScale = scale == 150 ? 50 : 150;
                window.RefreshUiScale();
                await Task.Delay(120);
                CheckSurface(presenter, name + " after scale change");
                Invoke(more);
                await Task.Delay(80);
            }
            // Changing the owner size while a menu is open must update its scale and placement.
            services.Board.SetBackground(BoardBackground.White);
            services.Settings.Appearance.BlackboardScale = 150;
            window.RefreshUiScale();
            var moreButton = Descendants(root).OfType<Button>().First(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "更多"));
            Invoke(moreButton);
            await Task.Delay(120);
            var active = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .Single(item => Descendants(item.Child).OfType<TextBlock>().Any(text => text.Text == "更多功能"));
            var windowMode = Descendants(active.Child).OfType<Button>()
                .Single(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "窗口模式"));
            Invoke(windowMode);
            await Task.Delay(120);
            root = (FrameworkElement)window.Content;
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 650));
            await Task.Delay(120);
            Invoke(moreButton);
            await Task.Delay(120);
            active = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .Single(item => Descendants(item.Child).OfType<TextBlock>().Any(text => text.Text == "更多功能"));
            var activePresenter = Descendants(active.Child).OfType<FlyoutPresenter>().Single();
            CheckSurface(activePresenter, "small window");
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(920, 760));
            await Task.Delay(120);
            // WinUI may dismiss an anchored flyout when its owner resizes. Reopening must
            // measure the new viewport instead of reusing dimensions from the closed popup.
            active = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .SingleOrDefault(item => Descendants(item.Child).OfType<TextBlock>().Any(text => text.Text == "更多功能"));
            if (active is null)
            {
                Invoke(moreButton);
                await Task.Delay(120);
                active = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                    .Single(item => Descendants(item.Child).OfType<TextBlock>().Any(text => text.Text == "更多功能"));
            }
            activePresenter = Descendants(active.Child).OfType<FlyoutPresenter>().Single();
            CheckSurface(activePresenter, "menu fits after owner resize");
            var paper = Descendants(active.Child).OfType<Button>()
                .Single(button => Descendants(button).OfType<TextBlock>().Any(text => text.Text == "画布纸型"));
            Invoke(paper);
            await Task.Delay(120);
            bool submenu = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .Any(item => Descendants(item.Child).OfType<MenuFlyoutPresenter>().Any());
            checks.Add(new { check = "more menu opens paper submenu", passed = submenu });
        }
        catch (Exception ex) { checks.Add(new { check = "board popup exception", passed = false, error = ex.ToString() }); }
        finally
        {
            window.Close();
            File.WriteAllText(Path.Combine(directory, "board-popup.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            Application.Current.Exit();
        }

        void CheckSurface(FlyoutPresenter presenter, string name)
        {
            var view = (Viewbox)presenter.Content;
            var surface = (Border)view.Child;
            var bounds = WindowChrome.GetScreenBounds(window, surface);
            var outer = WindowChrome.GetScreenBounds(window, presenter);
            bool exactSize = outer.Width > 0 && outer.Height > 0 && Math.Abs(outer.X - bounds.X) <= 1 && Math.Abs(outer.Y - bounds.Y) <= 1
                && Math.Abs(outer.Width - bounds.Width) <= 2 && Math.Abs(outer.Height - bounds.Height) <= 2;
            bool buttonsFit = Descendants(surface).OfType<Button>().All(button =>
            {
                var rect = button.TransformToVisual(presenter).TransformBounds(new Windows.Foundation.Rect(0, 0, button.ActualWidth, button.ActualHeight));
                return rect.Left >= -1 && rect.Top >= -1 && rect.Right <= presenter.ActualWidth + 1 && rect.Bottom <= presenter.ActualHeight + 1;
            });
            bool transparent = presenter.Background is SolidColorBrush brush && brush.Color.A == 0;
            checks.Add(new { check = "more presenter fits its painted surface", name, exactSize, buttonsFit, transparent,
                passed = exactSize && buttonsFit && transparent, bounds, presenterBounds = outer });
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
