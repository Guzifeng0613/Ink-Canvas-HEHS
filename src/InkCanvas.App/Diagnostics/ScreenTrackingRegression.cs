using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using InkCanvas.App.Services;
using InkCanvas.App.Views;
using InkCanvas.Core.Ink;
using InkCanvas.Core.ScreenTracking;
using InkCanvas.Core.Settings;
using InkCanvas.Platform.Interop;
using InkCanvas.Platform.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using InkCanvas.Host.Contracts;

namespace InkCanvas.App.Diagnostics;

internal static class ScreenTrackingRegression
{
    public static async Task Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var checks = new List<object>();
        using var services = new AppServices(Path.Combine(directory, "settings"));
        services.Settings.Startup.IsAutoUpdate = false;
        using var overlay = new OverlayHost(services.OverlayInk);
        bool boardOpen = false;
        services.StartModules(new ScreenTrackingHost(services, overlay, () => boardOpen));
        var coordinator = services.Modules.GetExport<IScreenTrackingService>() ?? throw new InvalidOperationException("Screen tracking module is unavailable.");
        var window = new SettingsWindow(services); window.Activate();
        bool failed = false;
        try
        {
            await Task.Delay(400); window.SelectCategory("实验性设置"); await Task.Delay(120);
            var toggle = Descendants(window.Content).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == "启用屏幕内容跟随（实验性）");
            Check("experiment starts disabled", !toggle.IsOn && !services.Settings.Experimental.ScreenContentTracking);
            ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
            Check("experiment toggle persists", services.Store.Load().Experimental.ScreenContentTracking);
            // Disable the real observer while deterministic snapshots exercise the real layered presenter.
            ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)).Toggle();
            await BoardWindowRegression.SaveVisual((FrameworkElement)window.Content, Path.Combine(directory, "experimental-settings.png"));
            window.AppWindow.Hide();
            var origin = new Vector2(overlay.Window.X, overlay.Window.Y); float dpi = overlay.Window.DpiScale;
            var snapshot = new ScreenViewSnapshot { Application = TrackedApplication.Diagnostic, WindowHandle = 42, ContextKey = "A",
                Viewport = new Rect(origin.X + 100, origin.Y + 100, origin.X + 700, origin.Y + 700), HasScrollMetrics = true, HasZoomMetrics = true, Status = "diagnostic" };
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 20, TaperKind = TaperKind.None, Color = InkColor.Red });
            stroke.AddPoint(300 / dpi, 300 / dpi); stroke.AddPoint(500 / dpi, 300 / dpi); stroke.Complete(); services.OverlayInk.Document.Add(stroke);
            coordinator.ApplySnapshot(snapshot);
            overlay.Presenter.InkVisible = true; services.OverlayInk.NotifyElementsChanged();
            Check("layered ink rendered inside viewport", Alpha(350, 300) > 50);
            coordinator.ApplySnapshot(snapshot with { Occlusions = new[] { new Rect(origin.X + 320, origin.Y + 250, origin.X + 400, origin.Y + 350) } });
            Check("occlusion clips real ink pixels", Alpha(350, 300) <= 1 && Alpha(450, 300) > 50);
            overlay.SetInteractionMode(true, false);
            coordinator.ApplySnapshot(snapshot with { ScrollPixels = new Vector2(0, 80) });
            Check("hidden ink still follows scroll", Math.Abs(services.OverlayInk.ViewTransform.M32 + 80 / dpi) < .01);
            overlay.SetInteractionMode(false, true);
            Check("restored ink appears at scrolled position", Alpha(350, 220) > 50 && Alpha(350, 300) <= 1);
            coordinator.ApplySnapshot(snapshot with { AlignmentReliable = false });
            Check("unreliable alignment hides ink", Alpha(350, 220) <= 1);
            coordinator.ApplySnapshot(snapshot with { ContextKey = "B" });
            Check("new page has independent ink", services.OverlayInk.Document.Count == 0);
            coordinator.ApplySnapshot(snapshot);
            Check("returning page restores same stroke", services.OverlayInk.Document.Strokes.Single() == stroke && Alpha(350, 300) > 50);
            overlay.SetInteractionMode(true, false);
            if (NativeMethods.GetForegroundWindow() == 0)
            {
                checks.Add(new { check = "native Office and Edge interaction", skipped = true, reason = "No interactive input desktop is available. GetForegroundWindow returned zero." });
                return;
            }
            using dynamic reader = Activator.CreateInstance(coordinator.GetType().Assembly.GetType("InkCanvas.Platform.System.ScreenContentReader", true)!);
            reader.Configure(new ExperimentalSettings { ScreenContentTracking = true });
            await CheckOffice(TrackedApplication.Word, "Word.Application");
            await CheckOffice(TrackedApplication.PowerPoint, "PowerPoint.Application");
            await CheckEdge();

            async Task CheckOffice(TrackedApplication application, string progId)
            {
                string processName = application == TrackedApplication.Word ? "WINWORD" : "POWERPNT";
                var existing = Process.GetProcessesByName(processName);
                bool alreadyRunning = existing.Length > 0;
                foreach (var process in existing) process.Dispose();
                if (alreadyRunning)
                {
                    checks.Add(new { check = application + " native integration", skipped = true, reason = "An existing Office instance is open. Diagnostic creates only its own instance." });
                    return;
                }
                dynamic app = null, document = null;
                var owned = new List<object>();
                object Own(object value) { if (value is not null && Marshal.IsComObject(value)) owned.Add(value); return value; }
                try
                {
                    app = Own(Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)));
                    app.Visible = true;
                    if (application == TrackedApplication.Word)
                    {
                        dynamic documents = Own(app.Documents); document = Own(documents.Add());
                        dynamic content = Own(document.Content); content.Text = string.Join("\r", Enumerable.Range(1, 180).Select(i => $"InkCanvas screen tracking classroom paragraph {i}. Test scrolling and zoom alignment."));
                    }
                    else
                    {
                        dynamic presentations = Own(app.Presentations); document = Own(presentations.Add());
                        dynamic slides = Own(document.Slides); Own(slides.Add(1, 12)); Own(slides.Add(2, 12));
                    }
                    await Task.Delay(600);
                    dynamic active = Own(app.ActiveWindow);
                    nint handle = 0;
                    if (application == TrackedApplication.Word) handle = new(Convert.ToInt64(active.HWND));
                    else
                    {
                        active.Activate(); await Task.Delay(250);
                        handle = FindProcessWindow(GetCurrentProcessIdByName(processName));
                    }
                    if (handle == 0) { Check(application + " native document window", false); return; }
                    SetForegroundWindow(handle); await Task.Delay(600);
                    var initial = await WaitFor(value => value.Application == application && value.IsValid);
                    Check(application + " native document detected", initial is not null, initial);
                    if (initial is null) return;
                    if (application == TrackedApplication.Word)
                    {
                        dynamic view = Own(active.View); dynamic zoom = Own(view.Zoom); zoom.Percentage = 150;
                        var zoomed = await WaitFor(value => value.Application == application && value.IsValid && value.Scale > initial.Scale * 1.2f);
                        Check("Word native zoom follows", zoomed is not null, zoomed);
                        active.VerticalPercentScrolled = 45;
                        var scrolled = await WaitFor(value => value.Application == application && value.IsValid && value.HasScrollMetrics && value.ScrollPixels.Y > initial.ScrollPixels.Y + 100);
                        Check("Word native scroll follows", scrolled is not null, scrolled);
                    }
                    else
                    {
                        dynamic view = Own(active.View); view.GotoSlide(2);
                        var other = await WaitFor(value => value.Application == application && value.IsValid && value.ContextKey != initial.ContextKey);
                        Check("PowerPoint native slide changes identity", other is not null, other);
                        view.GotoSlide(1);
                        Check("PowerPoint native slide return restores identity", await WaitFor(value => value.IsValid && value.ContextKey == initial.ContextKey) is not null);
                        view.Zoom = 125;
                        var zoomed = await WaitFor(value => value.Application == application && value.IsValid && value.HasZoomMetrics && Math.Abs(value.Scale - initial.Scale) > .05);
                        Check("PowerPoint native zoom follows", zoomed is not null, zoomed);
                    }
                }
                catch (Exception ex) { Check(application + " native integration", false, new { error = ex.Message }); }
                finally
                {
                    try { if (document is not null) { if (application == TrackedApplication.Word) document.Close(0); else { document.Saved = -1; document.Close(); } } } catch { }
                    try { app?.Quit(); } catch { }
                    foreach (var value in owned.AsEnumerable().Reverse()) if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
                }
            }

            async Task CheckEdge()
            {
                string profile = Path.Combine(directory, "edge-test-profile");
                string page = Path.Combine(directory, "edge-a.html"), other = Path.Combine(directory, "edge-b.html");
                string body = "<style>body{font:20px sans-serif;margin:0}header{position:sticky;top:0;background:white;padding:30px}p{margin:50px}</style><header>InkCanvas fixed classroom header</header>" + string.Concat(Enumerable.Range(0, 100).Select(i => $"<p>Classroom page paragraph {i} for native browser scroll testing. The unchanged text remains attached to annotations.</p>"));
                File.WriteAllText(page, "<!doctype html><title>InkCanvas native page A</title>" + body);
                File.WriteAllText(other, "<!doctype html><title>InkCanvas native page B</title>" + body.Replace("Classroom", "Mathematics"));
                using var process = Process.Start(new ProcessStartInfo(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")
                { ArgumentList = { "--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--new-window", page }, UseShellExecute = false });
                try
                {
                    var initial = await WaitFor(value => value.Application == TrackedApplication.Edge && value.IsValid, 12000);
                    Check("Edge native document detected", initial is not null, initial); if (initial is null) return;
                    SetForegroundWindow((nint)initial.WindowHandle); Key(0x22);
                    var scrolled = await WaitFor(value => value.Application == TrackedApplication.Edge && value.IsValid && value.ScrollPixels.Y > initial.ScrollPixels.Y + 80);
                    Check("Edge native scroll follows", scrolled is not null, scrolled);
                    Key(0x6B, control: true); Key(0x6B, control: true);
                    var zoomed = await WaitFor(value => value.Application == TrackedApplication.Edge && value.IsValid && value.Scale > initial.Scale * 1.1);
                    Check("Edge native zoom follows", zoomed is not null, zoomed);
                    // Navigate only the fresh test window via its address bar.
                    Key(0x4C, control: true); await Task.Delay(100);
                    foreach (char ch in new Uri(other).AbsoluteUri) { short key = VkKeyScan(ch); Key((byte)(key & 255), shift: (key & 256) != 0); }
                    Key(0x0D);
                    var changed = await WaitFor(value => value.Application == TrackedApplication.Edge && value.IsValid && value.ContextKey != initial.ContextKey);
                    Check("Edge native navigation changes page identity", changed is not null, changed);
                }
                finally
                {
                    // The profile belongs exclusively to this diagnostic invocation.
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow(); await Task.Delay(200);
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                    }
                }
            }
            async Task<ScreenViewSnapshot> WaitFor(Func<ScreenViewSnapshot, bool> predicate, int timeout = 6000)
            {
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < timeout)
                {
                    ScreenViewSnapshot current = reader.Latest;
                    if (current is not null && reader.SampleAge < TimeSpan.FromSeconds(2) && predicate(current)) return current;
                    await Task.Delay(100);
                }
                File.WriteAllText(Path.Combine(directory, "last-observation.json"), JsonSerializer.Serialize(reader.Latest, new JsonSerializerOptions { WriteIndented = true }));
                return null;
            }
        }
        catch (Exception ex) { Check("unhandled diagnostic failure", false, ex.ToString()); }
        finally
        {
            window?.Close();
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = !failed, checks }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Exit();
        }
        void Check(string name, bool passed, object detail = null)
        {
            failed |= !passed; checks.Add(new { check = name, passed, detail });
            File.WriteAllText(Path.Combine(directory, "progress.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        byte Alpha(int x, int y) => Marshal.ReadByte(overlay.Window.PixelBuffer, ((y * overlay.Window.Width + x) * 4) + 3);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Key(byte key, bool control = false, bool shift = false)
    {
        if (control) KeybdEvent(0x11, 0, 0, 0); if (shift) KeybdEvent(0x10, 0, 0, 0);
        KeybdEvent(key, 0, 0, 0); KeybdEvent(key, 0, 2, 0);
        if (shift) KeybdEvent(0x10, 0, 2, 0); if (control) KeybdEvent(0x11, 0, 2, 0);
    }
    private static int GetCurrentProcessIdByName(string name) => Process.GetProcessesByName(name).Select(process =>
    { using (process) return process.Id; }).FirstOrDefault();
    private static nint FindProcessWindow(int processId)
    {
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == processId && IsWindowVisible(window) && GetAncestor(window, 2) == window) { found = window; return false; }
            return true;
        }, 0);
        return found;
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumWindows(WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    private delegate bool WindowCallback(nint hwnd, nint parameter);
    [DllImport("user32.dll", EntryPoint = "keybd_event")] private static extern void KeybdEvent(byte key, byte scan, uint flags, nuint extra);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScan(char value);
}
