using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using InkCanvas.Ink;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Diagnostics;

internal static class WorkspaceRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        var services = new AppServices(Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N")));
        services.Settings.Startup.IsAutoUpdate = false;
        services.Settings.Automation.MinimumAutomationStrokeNumber = int.MaxValue;
        using var shell = new ShellController(services);
        shell.Start();
        shell.StartInClickThrough();
        try
        {
            var bar = shell.OpenWindows.OfType<FloatingBarWindow>().Single();
            for (int i = 0; i < 10; i++)
            {
                Invoke(FindButton(bar.Content, "工具"));
                await Task.Delay(35);
                var panel = bar.GetOpenPanel(BarPanelKind.More);
                var watch = Stopwatch.StartNew();
                Invoke(FindButton(panel.Content, "设置"));
                shell.OpenSettings(); shell.OpenSettings();
                await Task.Delay(160);
                var settings = shell.OpenWindows.OfType<SettingsWindow>().Single();
                Check("settings opens once and dispatcher remains responsive " + i, watch.ElapsedMilliseconds < 2000
                    && Descendants(settings.Content).OfType<NavigationView>().Count() == 1
                    && ClassroomWindowRegression.CheckNativeFrame(settings, extended: true));
                foreach (string category in new[] { "画板选项", "手势选项", "墨迹识别选项", "外观选项", "自动选项" })
                {
                    settings.SelectCategory(category);
                    ((FrameworkElement)settings.Content).UpdateLayout();
                    await Task.Delay(8);
                }
                settings.SelectCategory("画板选项");
                ((FrameworkElement)settings.Content).UpdateLayout();
                var width = Descendants(settings.Content).OfType<Slider>().Single(slider => AutomationProperties.GetName(slider) == "荧光笔粗细");
                width.Value = 120;
                Check("settings retain independent highlighter width " + i, services.Settings.Canvas.HighlighterWidth == 60);
                if (i == 9)
                {
                    await BoardWindowRegression.SaveVisual((FrameworkElement)settings.Content, Path.Combine(directory, "settings-canvas.png"));
                    settings.SelectCategory("外观选项");
                    await Task.Delay(30);
                    await BoardWindowRegression.SaveVisual((FrameworkElement)settings.Content, Path.Combine(directory, "settings-appearance.png"));
                }
                settings.Close();
                await Task.Delay(20);
            }

            var guide = InfoWindow.OpenGuide(1);
            guide.Activate(); await Task.Delay(160);
            Check("guide uses left navigation and system frame", ClassroomWindowRegression.CheckNativeFrame(guide)
                && Descendants(guide.Content).OfType<WindowWorkspace>().Count() == 1);
            await BoardWindowRegression.SaveVisual((FrameworkElement)guide.Content, Path.Combine(directory, "guide.png"));
            guide.Close();
            await ClassroomWindowRegression.Run(directory);

            var controller = services.OverlayInk;
            shell.ToggleClickThrough();
            controller.Tool = InkTool.Select;
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 4 });
            stroke.AddPoint(150, 160); stroke.AddPoint(300, 220); stroke.Complete();
            controller.Document.Add(stroke);
            controller.SelectAll();
            await Task.Delay(180);
            var toolbar = shell.OpenWindows.OfType<SelectionToolbarWindow>().Single();
            Check("desktop selection exposes a compact nonactivating menu", toolbar.IsShown
                && NativeMethods.HasExStyle(WindowChrome.GetHwnd(toolbar), NativeMethods.WS_EX_NOACTIVATE)
                && toolbar.AppWindow.ClientSize.Width < 450);
            CheckPlacement("desktop menu initial placement");
            Invoke(FindButton(toolbar.Content, "克隆"));
            Check("desktop clone mode toggles", controller.CloneMode);
            Invoke(FindButton(toolbar.Content, "样式")); await Task.Delay(50);
            Invoke(FindButton(toolbar.Content, "加粗"));
            Check("selection style changes with undo", stroke.Attributes.BaseWidth == 5);
            controller.Undo();
            Check("selection width undo restores original", stroke.Attributes.BaseWidth == 4);
            await BoardWindowRegression.SaveVisual((FrameworkElement)toolbar.Content, Path.Combine(directory, "selection-style.png"));
            CheckPlacement("expanded menu avoids main bar");
            Invoke(FindButton(toolbar.Content, "变换")); await Task.Delay(50);
            Invoke(FindButton(toolbar.Content, "45°"));
            Check("selection transform applies", Math.Abs(controller.SelectionRotationDegrees - 45) < .1);
            controller.Undo();
            Invoke(FindButton(toolbar.Content, "收起")); await Task.Delay(50);
            Check("selection toolbar folds", toolbar.Toolbar.IsCollapsed && toolbar.AppWindow.ClientSize.Width < 100);
            Invoke(FindButton(toolbar.Content, "展开")); await Task.Delay(50);
            Check("selection toolbar unfolds", !toolbar.Toolbar.IsCollapsed && toolbar.AppWindow.ClientSize.Width > 200);
            var work = WindowChrome.GetWorkArea(toolbar);
            var overlay = shell.Overlay.Window;
            foreach (var point in new[] { new Vector2(work.X + 5, work.Y + 5), new Vector2(work.X + work.Width - 5, work.Y + work.Height - 5),
                new Vector2(bar.AppWindow.Position.X + bar.AppWindow.Size.Width / 2, bar.AppWindow.Position.Y) })
            {
                var center = controller.SelectionBounds.Center;
                var target = (point - new Vector2(overlay.X, overlay.Y)) / overlay.DpiScale;
                controller.TransformSelectionUndoable(Matrix3x2.CreateTranslation(target - center), "move selection", false);
                await Task.Delay(50);
                CheckPlacement("selection menu stays on screen at " + point);
            }
            await BoardWindowRegression.SaveVisual((FrameworkElement)toolbar.Content, Path.Combine(directory, "selection.png"));
            var beforePosition = toolbar.AppWindow.Position;
            for (int i = 0; i < 100; i++) toolbar.QueueUpdate();
            await Task.Delay(50);
            Check("unchanged selection keeps stable window position", toolbar.AppWindow.Position == beforePosition);
            shell.ToggleClickThrough(); await Task.Delay(50);
            Check("selection menu hides in mouse mode", !toolbar.IsShown);
            shell.ToggleClickThrough(); await Task.Delay(50);
            Check("selection menu returns with annotation", toolbar.IsShown);
            shell.ToggleBoard(); await Task.Delay(160);
            Check("desktop selection menu hides in board", !toolbar.IsShown);
            var board = shell.OpenWindows.OfType<BoardWindow>().Single();
            var boardStroke = stroke.Clone(preserveId: false);
            boardStroke.Transform(Matrix3x2.CreateTranslation(new Vector2(350, 250) - boardStroke.Bounds.Center), false);
            board.CurrentController.Document.Add(boardStroke);
            board.CurrentController.Tool = InkTool.Select; board.CurrentController.SelectAll();
            await Task.Delay(80);
            var boardMenu = Descendants(board.Content).OfType<SelectionToolbar>().Single();
            Check("board shares compact selection toolbar", boardMenu.ActualWidth < 400);
            Invoke(FindButton(boardMenu, "收起")); await Task.Delay(40);
            Check("board selection toolbar folds", boardMenu.IsCollapsed);
            Invoke(FindButton(boardMenu, "展开"));
            await Task.Delay(80);
            var boardRoot = (FrameworkElement)board.Content;
            var menuBounds = boardMenu.TransformToVisual(boardRoot).TransformBounds(new Windows.Foundation.Rect(0, 0, boardMenu.ActualWidth, boardMenu.ActualHeight));
            Check("board menu remains near selected objects after unfolding", Math.Abs(menuBounds.X + menuBounds.Width / 2 - board.CurrentController.SelectionVisualBounds.Center.X) < 180
                && menuBounds.Bottom <= boardRoot.ActualHeight && menuBounds.Right <= boardRoot.ActualWidth);
            await BoardWindowRegression.SaveVisual((FrameworkElement)board.Content, Path.Combine(directory, "board-selection.png"));
            board.Close();

            void CheckPlacement(string name)
            {
                var position = toolbar.AppWindow.Position; var size = toolbar.AppWindow.Size;
                var area = WindowChrome.GetWorkArea(toolbar);
                var rect = new InkCanvas.Core.Ink.Rect(position.X, position.Y, position.X + size.Width, position.Y + size.Height);
                var primary = shell.DesktopToolbarBounds;
                Check(name, rect.Left >= area.X && rect.Top >= area.Y && rect.Right <= area.X + area.Width
                    && rect.Bottom <= area.Y + area.Height && !rect.Intersects(primary));
            }
        }
        catch (Exception ex) { checks.Add(new { check = "workspace regression exception", passed = false, error = ex.ToString() }); }
        finally
        {
            foreach (var window in shell.OpenWindows.ToArray()) window.Close();
            File.WriteAllText(Path.Combine(directory, "workspace.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }
        void Check(string name, bool passed) => checks.Add(new { check = name, passed });
    }

    private static Button FindButton(DependencyObject root, string name) => Descendants(root).OfType<Button>()
        .First(button => AutomationProperties.GetName(button) == name || button is TouchIconButton touch && touch.Label == name);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
