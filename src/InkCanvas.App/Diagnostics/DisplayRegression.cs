using System.Numerics;
using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Core.Ink;
using InkCanvas.Ink;
using InkCanvas.Platform.System;
using InkCanvas.Platform.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.App.Diagnostics;

internal static class DisplayRegression
{
    public static async Task Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>();
        var services = new AppServices(Path.Combine(directory, "settings"));
        services.Settings.Startup.IsAutoUpdate = false;
        services.Settings.PowerPoint.PowerPointSupport = false;
        using var shell = new ShellController(services);
        shell.Start();
        shell.StartInClickThrough();
        try
        {
            var screens = ScreenService.All();
            checks.Add(new { check = "physical monitor topology", monitors = screens.Select(s => new
                { s.Bounds, s.WorkArea, s.DpiScale }), mixedDpiHardwareAvailable = screens.Select(s => s.DpiScale).Distinct().Count() > 1 });
            var overlay = shell.Overlay;
            Check("annotation viewport covers virtual desktop", ScreenService.Virtual ==
                new ScreenRect(overlay.Window.X, overlay.Window.Y, overlay.Window.Width, overlay.Window.Height));
            var bar = shell.OpenWindows.OfType<FloatingBarWindow>().Single();
            await Task.Delay(250);
            int cornerResult = DwmGetWindowAttribute(WindowChrome.GetHwnd(bar), 33, out int corner, sizeof(int));
            int nonClientResult = DwmGetWindowAttribute(WindowChrome.GetHwnd(bar), 1 /* DWMWA_NCRENDERING_ENABLED */, out int nonClient, sizeof(int));
            Check("clipped toolbar suppresses rectangular native corner and shadow", cornerResult == 0 && corner == 1 && nonClientResult == 0 && nonClient == 0);
            foreach (var screen in screens)
            {
                WindowChrome.MoveToMonitor(bar, screen);
                foreach (double scale in new[] { 50d, 100, 200 })
                {
                    bar.Reposition(100, scale);
                    await Task.Delay(120);
                    Check($"toolbar fits monitor {screen.Bounds.X},{screen.Bounds.Y} at {scale}%", Fits(bar, screen.WorkArea));
                    Check("toolbar retains opaque controls", bar.Bar.Opacity == 1);
                }
                bar.Reposition(100, 100);
                foreach (bool highlighter in new[] { false, true })
                {
                    var popup = new PenPaletteWindow(services.OverlayInk, highlighter, 1, 200);
                    popup.PlaceAboveBar(() => bar.GetToolAnchor(highlighter ? InkTool.Highlighter : InkTool.Pen), WindowChrome.GetHwnd(bar));
                    WindowChrome.ShowWithoutActivation(popup);
                    await Task.Delay(120);
                    Check("palette fits owner monitor", Fits(popup, screen.WorkArea));
                    Check("palette uses owner monitor DPI", Math.Abs(WindowChrome.GetDpiScale(popup) - screen.DpiScale) < .01);
                    popup.Close();
                }
                for (int side = 0; side < 2; side++)
                {
                    shell.ToggleFold();
                    await Task.Delay(100);
                    Check("both folded handles stay on owner monitor", bar.FoldHandles.Count == 2 && bar.FoldHandles.All(h => Fits(h, screen.WorkArea)));
                    var button = (Button)bar.FoldHandles[side].Content;
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    await Task.Delay(1050);
                    Check("edge handle restores toolbar on owner monitor", !bar.IsFolded && Fits(bar, screen.WorkArea));
                }
                shell.OpenTimer();
                await Task.Delay(160);
                var timer = shell.OpenWindows.OfType<CountdownWindow>().Single();
                Check("countdown opens on toolbar monitor", Fits(timer, screen.WorkArea));
                timer.Close();
            }
            Check("mode and popup switches retain exact toolbar origin", await SelfTest.CheckToolbarPosition(shell, checks));
            bool? translucent = await SelfTest.CheckBackdropTransparency(shell, checks);
            checks.Add(new { check = "desktop translucency readback", passed = translucent, skipped = translucent is null });

            // Exercise real DIB/Win2D reallocation with negative origins and different aspect ratios.
            shell.ToggleClickThrough();
            var controller = services.OverlayInk;
            controller.RecognizeShapes = false;
            controller.Tool = InkTool.Pen;
            controller.ViewTransform = Matrix3x2.CreateScale(1.25f) * Matrix3x2.CreateTranslation(15.5f, -8.25f);
            var start = new OverlayPointer(791, 100, 120, .5f, 0, 0, OverlayPointerKind.Pen, 100);
            controller.OnPointerDown(start);
            controller.OnPointerMove(start with { X = 200, Y = 140, TimeMs = 120 });
            controller.OnPointerUp(start with { X = 250, Y = 160, TimeMs = 140 });
            var stroke = controller.Document.Strokes.Single();
            Vector2 world = stroke.Points[stroke.Points.Count / 2].Position;
            Vector2 initial = DesktopPoint(world);
            foreach (var bounds in new[] { new ScreenRect(-400, -200, 1600, 1000), new(0, 0, 900, 700), ScreenService.Virtual })
            {
                overlay.SetDisplayBounds(bounds);
                Check("viewport resize preserves desktop ink position", Vector2.Distance(initial, DesktopPoint(world)) < .01);
                Check("viewport resize preserves ink and undo", controller.Document.Strokes.Single() == stroke && controller.History.CanUndo);
                Check("viewport size matches new DIB", overlay.Window.Width == bounds.Width && overlay.Window.Height == bounds.Height);
                var pixel = Vector2.Transform(world, controller.ViewTransform) * overlay.Window.DpiScale;
                int pixelOffset = ((int)pixel.Y * bounds.Width + (int)pixel.X) * 4 + 3;
                Check("ink redraws at the preserved position after DIB resize", System.Runtime.InteropServices.Marshal.ReadByte(overlay.Window.PixelBuffer, pixelOffset) > 16);
                controller.Undo();
                Check("undo works after viewport change", controller.Document.Count == 0);
                controller.Redo();
                Check("redo works after viewport change", controller.Document.Strokes.Single() == stroke);
            }
            overlay.Hide();
            overlay.SetDisplayBounds(new(-100, -100, 1200, 800));
            Check("hidden viewport remains hidden after resize", !overlay.Window.IsVisible);
            overlay.RefreshDisplayBounds();
            overlay.Show();
            Check("resized hidden viewport restores", overlay.Window.IsVisible && Vector2.Distance(initial, DesktopPoint(world)) < .01);

            Vector2 DesktopPoint(Vector2 p) => Vector2.Transform(p, controller.ViewTransform) * overlay.Window.DpiScale
                + new Vector2(overlay.Window.X, overlay.Window.Y);
        }
        catch (Exception ex) { checks.Add(new { check = "display regression exception", passed = false, error = ex.ToString() }); }
        finally
        {
            foreach (var window in shell.OpenWindows.ToArray()) window.Close();
            File.WriteAllText(Path.Combine(directory, "displays.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }
        void Check(string name, bool passed) => checks.Add(new { check = name, passed });
    }

    private static bool Fits(Window window, ScreenRect work)
    {
        var position = WindowChrome.GetClientPosition(window);
        var size = window.AppWindow.ClientSize;
        return position.X >= work.X && position.Y >= work.Y
            && position.X + size.Width <= work.X + work.Width && position.Y + size.Height <= work.Y + work.Height;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
