using System.Runtime.InteropServices;
using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Ink;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Diagnostics;

internal static class PopupRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, "settings"));
        services.Settings.Startup.IsAutoUpdate = false;
        using var shell = new ShellController(services);
        var outside = new Window { Title = "Popup input acceptance", Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.White) } };
        WindowChrome.MakeStandard(outside);
        WindowChrome.PlaceClient(outside, 50, 60, 600, 400);
        outside.Activate();
        shell.Start(); shell.StartInClickThrough();
        NativeMethods.GetCursorPos(out var original);
        try
        {
            foreach (bool highlighter in new[] { false, true })
            {
                shell.OpenPenPalette(highlighter); await Task.Delay(200);
                var window = shell.OpenWindows.OfType<PenPaletteWindow>().Single();
                var palette = Descendants(window.Content).OfType<PenPalette>().Single();
                palette.Header.IsPinned = false;
                bool closed = false; window.Closed += (_, _) => closed = true;
                Check("pen panel has compact title actions " + highlighter, Descendants(palette.Header).OfType<Button>().Any()
                    && palette.Header.ActualHeight >= 40 && palette.Header.ActualHeight <= 44);
                Check("pen panel preserves no-activation " + highlighter, NativeMethods.HasExStyle(WindowChrome.GetHwnd(window), NativeMethods.WS_EX_NOACTIVATE));
                await BoardWindowRegression.SaveVisual((FrameworkElement)window.Content, Path.Combine(directory, highlighter ? "highlighter.png" : "pen.png"));
                Click(100, 200); await Task.Delay(200);
                Check("quick outside click dismisses pen panel " + highlighter, closed);
                if (!closed) window.Close();

                shell.OpenPenPalette(highlighter); await Task.Delay(150);
                window = shell.OpenWindows.OfType<PenPaletteWindow>().Single();
                palette = Descendants(window.Content).OfType<PenPalette>().Single();
                palette.Header.IsPinned = true;
                closed = false; window.Closed += (_, _) => closed = true;
                Click(100, 200); await Task.Delay(200);
                window.DismissOnOverlayInput();
                Check("pinned pen panel survives outside click and writing " + highlighter, !closed);
                palette.Header.IsPinned = false;
                Click(100, 200); await Task.Delay(200);
                Check("unpin re-enables light dismiss " + highlighter, closed);
                if (!closed) window.Close();
            }

            var bar = shell.OpenWindows.OfType<FloatingBarWindow>().Single();
            foreach (var kind in new[] { BarPanelKind.More, BarPanelKind.Gesture, BarPanelKind.Eraser })
            {
                using var close = new WindowCloseGuard();
                var panel = new BarPanelWindow(bar, bar.Bar, kind); close.Window = panel;
                var anchor = bar.AppWindow.Position;
                panel.ShowAboveBar(anchor.X, anchor.Y, bar.AppWindow.Size.Width); await Task.Delay(180);
                var header = Descendants(panel.Content).OfType<PopupHeader>().Single();
                bool closed = false; panel.Closed += (_, _) => { closed = true; close.Window = null; };
                Check("toolbar panel uses shared compact header " + kind, header.ActualHeight >= 40);
                if (kind == BarPanelKind.Eraser)
                {
                    var combo = Descendants(panel.Content).OfType<ComboBox>().Single();
                    combo.IsDropDownOpen = true; await Task.Delay(180);
                    var item = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)panel.Content).XamlRoot)
                        .SelectMany(popup => Descendants(popup.Child)).OfType<ComboBoxItem>().Last();
                    var screen = WindowChrome.GetScreenBounds(panel, item);
                    var bounds = new Windows.Foundation.Rect(screen.X, screen.Y, screen.Width, screen.Height);
                    SetCursorPos((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));
                    var target = WindowFromPoint(new NativeMethods.POINT { X = (int)(bounds.X + bounds.Width / 2), Y = (int)(bounds.Y + bounds.Height / 2) });
                    var nativeClass = new System.Text.StringBuilder(256); GetClassName(target, nativeClass, nativeClass.Capacity);
                    Click((int)(bounds.X + bounds.Width / 2), (int)(bounds.Y + bounds.Height / 2));
                    await Task.Delay(180);
                    checks.Add(new { check = "combo selection keeps its unpinned parent panel open", passed = !closed && !combo.IsDropDownOpen,
                        closed, combo.IsDropDownOpen, combo.SelectedIndex, nativeClass = nativeClass.ToString(), bounds });
                    if (closed) continue;
                }
                header.IsPinned = true; Click(100, 200); await Task.Delay(180);
                panel.DismissOnInput(); Check("pinned toolbar panel remains open " + kind, !closed);
                header.IsPinned = false; Click(100, 200); await Task.Delay(180);
                Check("toolbar panel dismisses after unpin " + kind, closed);
            }

            shell.ToggleBoard(); await Task.Delay(200);
            var board = shell.OpenWindows.OfType<BoardWindow>().Single();
            shell.OpenShapePicker(); await Task.Delay(150);
            var shapes = shell.OpenWindows.OfType<ShapePickerWindow>().Single();
            shapes.AutoHide = false;
            bool shapeClosed = false; shapes.Closed += (_, _) => shapeClosed = true;
            Click(150, 200); await Task.Delay(180);
            Check("pinned shapes survive clicks on owner board", !shapeClosed);
            shapes.AutoHide = true;
            Click(150, 200); await Task.Delay(180);
            Check("quick owner-board click dismisses unpinned shapes", shapeClosed);
            if (!shapeClosed) shapes.Close();
            board.ShowPenPalette(); await Task.Delay(180);
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)board.Content).XamlRoot)
                .Single(item => Descendants(item.Child).OfType<BoardPenPalette>().Any());
            var inkHeader = Descendants(popup.Child).OfType<PopupHeader>().Single();
            Check("board ink settings use shared compact title actions", inkHeader.ActualHeight >= 40);
            inkHeader.IsPinned = true;
            var controller = services.Board.Current;
            controller.Tool = InkTool.Pen;
            int before = controller.Document.Count;
            Click(160, 210); await Task.Delay(200);
            Check("pinned board ink panel stays open while first click writes", popup.IsOpen && controller.Document.Count == before + 1);
            inkHeader.IsPinned = false;
            Click(170, 220); await Task.Delay(180);
            Check("unpinned board ink panel dismisses and first click writes", !popup.IsOpen && controller.Document.Count == before + 2);
            board.ShowPenPalette(); await Task.Delay(150);
            popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)board.Content).XamlRoot)
                .Single(item => Descendants(item.Child).OfType<BoardPenPalette>().Any());
            inkHeader = Descendants(popup.Child).OfType<PopupHeader>().Single();
            Invoke(Descendants(inkHeader).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "关闭面板"));
            await Task.Delay(120);
            Check("board panel close action dismisses its flyout", !popup.IsOpen);
            board.Close();
        }
        catch (Exception exception) { checks.Add(new { check = "popup regression exception", passed = false, error = exception.ToString() }); }
        finally
        {
            foreach (var window in shell.OpenWindows.ToArray()) window.Close();
            outside.Close(); SetCursorPos(original.X, original.Y);
            File.WriteAllText(Path.Combine(directory, "popups.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }
        void Check(string name, bool passed) => checks.Add(new { check = name, passed });
    }
    private sealed class WindowCloseGuard : IDisposable
    {
        public Window Window { get; set; }
        public void Dispose() => Window?.Close();
    }
    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        MouseEvent(2, 0, 0, 0, UIntPtr.Zero);
        MouseEvent(4, 0, 0, 0, UIntPtr.Zero);
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativeMethods.POINT point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int length);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEvent(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
