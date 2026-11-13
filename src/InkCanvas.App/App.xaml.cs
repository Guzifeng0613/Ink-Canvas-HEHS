using InkCanvas.App.Services;
using Microsoft.UI.Xaml;

namespace InkCanvas.App;

public partial class App : Application
{
    private ShellController _shell;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var services = new AppServices();
        _shell = new ShellController(services);
        _shell.Start();

        // "--board" opens straight into the blackboard, mirroring the original launcher switches.
        foreach (var a in Environment.GetCommandLineArgs())
        {
            if (string.Equals(a, "--board", StringComparison.OrdinalIgnoreCase))
                _shell.ToggleBoard();

            // Start with clicks falling through to the desktop, so launching the app does not
            // trap the mouse on a machine that is in use. Toggle it off from the bar to draw.
            if (string.Equals(a, "--passthrough", StringComparison.OrdinalIgnoreCase))
                _shell.StartInClickThrough();
        }

        var diag = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--diag=", StringComparison.OrdinalIgnoreCase));
        if (diag is not null)
        {
            string path = diag.Substring("--diag=".Length);
            var t = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            t.Interval = TimeSpan.FromMilliseconds(700);
            t.Tick += (_, _) =>
            {
                try
                {
                    var w = _shell.Overlay.Window;
                    File.WriteAllText(path,
                        $"pointerMessages={w.PointerMessageCount} (down={w.PointerDownCount} move={w.PointerMoveCount} up={w.PointerUpCount}); "
                      + $"mouseMessages={w.MouseMessageCount}; "
                      + $"mouseAsPointer={Platform.Windows.LayeredInkWindow.MouseAsPointer}; "
                      + $"clickThrough={w.ClickThrough}; visible={w.IsVisible}; "
                      + $"strokes={_shell.Services.OverlayInk.Document.Count}; "
                      + $"live={_shell.Services.OverlayInk.LiveStrokes.Count}; "
                      + $"tool={_shell.Services.OverlayInk.Tool}\n");
                }
                catch { }
            };
            t.Start();
        }

        var selfTest = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--selftest=", StringComparison.OrdinalIgnoreCase));
        if (selfTest is not null)
        {
            string dir = selfTest.Substring("--selftest=".Length);
            // Let the first frame present before drawing, so the report counts real frames.
            var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1200);
            timer.IsRepeating = false;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Diagnostics.SelfTest.Run(_shell, dir);
            };
            timer.Start();
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // A classroom tool must not vanish mid-lesson. Log and keep running where possible.
        Core.Diagnostics.FileLog.WriteLogToFile(e.Exception.ToString(), Core.Diagnostics.LogType.Error);
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InkCanvas");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"),
                $"{DateTime.Now:O} {e.Exception}\n\n");
        }
        catch { }
        e.Handled = true;
    }
}