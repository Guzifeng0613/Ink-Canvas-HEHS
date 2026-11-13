using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Features.Tools;
using InkCanvas.Ink;
using InkCanvas.Platform.System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.App.Services;

/// <summary>
/// Owns the windows and implements the shell actions the floating bar invokes. Keeps App.xaml.cs
/// down to startup wiring.
/// </summary>
public sealed class ShellController : IShellActions, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _pollTimer;
    private FloatingBarWindow _bar;
    private BoardWindow _board;
    private SettingsWindow _settings;
    private CountdownWindow _countdown;
    private LotteryWindow _lottery;
    private ReplayWindow _replay;
    private InfoWindow _guide;
    private bool _lastSlideShowState;
    private bool _exiting;

    public ShellController(AppServices services)
    {
        Services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Overlay = new OverlayHost(services.OverlayInk);
        Updater = new UpdateCoordinator(services)
        {
            Confirm = ConfirmAsync,
            Notify = NotifyAsync,
            ExitForInstall = Exit,
        };

        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(900);
        _pollTimer.Tick += (_, _) => Poll();
    }

    public AppServices Services { get; }

    public OverlayHost Overlay { get; }

    public UpdateCoordinator Updater { get; }

    /// <summary>Diagnostics for the floating bar's window-vs-content sizing.</summary>
    public string BarDiagnostics
    {
        get
        {
            if (_bar is null) return "no bar";
            var size = _bar.AppWindow.Size;
            var desired = _bar.Bar.DesiredSize;
            return $"window={size.Width}x{size.Height}; desired={desired.Width:0.#}x{desired.Height:0.#}; "
                 + $"primaryChildren={_bar.Bar.PrimaryChildCount}; computedRow={_bar.Bar.ComputedPrimaryWidth:0.#}";
        }
    }

    public void Start()
    {
        _bar = new FloatingBarWindow();
        _bar.Configure(Services.OverlayInk, this);
        _bar.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                        Services.Settings.Appearance.FloatingBarScale);
        WindowChrome.ApplyTheme(_bar, Services.Settings.Appearance.Theme);
        _bar.Activate();

        Overlay.Show();
        Overlay.RegisterHotkeys(
            undo: () => Services.OverlayInk.Undo(),
            redo: () => Services.OverlayInk.Redo(),
            togglePen: () => { Services.OverlayInk.Tool = InkTool.Pen; _bar.Bar.RefreshSelection(); },
            toggleEraser: () => { Services.OverlayInk.Tool = InkTool.Eraser; _bar.Bar.RefreshSelection(); },
            clear: () => Services.OverlayInk.Clear(),
            togglePassThrough: ToggleClickThrough,
            toggleBoard: ToggleBoard);

        _pollTimer.Start();

        // The delete-selection button only exists while something is selected, which changes
        // the bar's content width — re-fit the window or the right-hand buttons get clipped.
        Services.OverlayInk.SelectionChanged += () =>
        {
            if (_bar is null) return;
            _bar.Bar.RefreshSelection();
            _bar.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                            Services.Settings.Appearance.FloatingBarScale);
        };

        // 1:1 手指翻页（原 GestureOnly 模式）。未验证。
        Services.OverlayInk.SlideFlipRequested += next => { if (next) PptNext(); else PptPrevious(); };

        Core.Diagnostics.FileLog.NewLog($"Ink Canvas 启动，版本 {Updater.CurrentVersion}");

        ApplyEdgeGestureSetting();

        if (Services.Settings.Startup.IsAutoUpdate)
            _ = Updater.CheckAsync(interactive: false);
    }

    private void ApplyEdgeGestureSetting()
        => Platform.System.EdgeGestureService.SetEdgeGesturesDisabled(
            Overlay.Window.Handle, Services.Settings.Advanced.IsEnableEdgeGestureUtil);

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null) return false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "下载并安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task NotifyAsync(string title, string message)
    {
        if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "知道了",
            XamlRoot = root.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>Periodic mode maintenance: slideshow detection, auto-fold, layering.</summary>
    private void Poll()
    {
        if (_exiting) return;

        if (Services.Settings.PowerPoint.PowerPointSupport)
        {
            if (!Services.Ppt.IsAttached) Services.Ppt.TryAttach();
            if (Services.Ppt.IsAttached && Services.Ppt.PollSlideShowState(ref _lastSlideShowState))
            {
                _bar?.Bar.SetPptModeVisible(_lastSlideShowState);
                // The PPT button group also changes the bar's width.
                _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                                 Services.Settings.Appearance.FloatingBarScale);
                if (_lastSlideShowState && Services.Settings.PowerPoint.IsShowCanvasAtNewSlideShow)
                    Overlay.Show();
            }
        }

        if (Services.Settings.Automation.IsEnableAutoFold) ApplyAutoFold();

        // 原条件：放映中 + 尚无墨迹 + 允许手指手势控制放映。未验证。
        Services.OverlayInk.Palm.PptGestureEligible =
            _lastSlideShowState
            && Services.OverlayInk.Document.Count == 0
            && Services.Settings.PowerPoint.IsEnableFingerGestureSlideShowControl;

        // Keep chrome above the ink overlay without stealing focus from a slideshow.
        if (_bar is not null) WindowChrome.EnsureAbove(_bar);
    }

    private void ApplyAutoFold()
    {
        string foreground = ForegroundWindowService.GetForegroundProcessName();
        if (string.IsNullOrEmpty(foreground)) return;

        var a = Services.Settings.Automation;
        bool fold =
            (a.IsAutoFoldInEasiNote && foreground.Contains("EasiNote", StringComparison.OrdinalIgnoreCase)) ||
            (a.IsAutoFoldInEasiCamera && foreground.Contains("EasiCamera", StringComparison.OrdinalIgnoreCase)) ||
            (a.IsAutoFoldInSeewoPincoTeacher && foreground.Contains("Pinco", StringComparison.OrdinalIgnoreCase)) ||
            (a.IsAutoFoldInHiteTouchPro && foreground.Contains("HiteTouch", StringComparison.OrdinalIgnoreCase)) ||
            (a.IsAutoFoldInMSWhiteboard && foreground.Contains("Whiteboard", StringComparison.OrdinalIgnoreCase)) ||
            (a.IsAutoFoldInPPTSlideShow && _lastSlideShowState);

        Overlay.ClickThrough = fold;
        if (_bar is not null)
        {
            _bar.Bar.ClickThroughActive = fold;
            _bar.Bar.RefreshSelection();
        }
    }

    // ---- IShellActions ----------------------------------------------------

    public void ToggleBoard()
    {
        if (_board is null)
        {
            _board = new BoardWindow(Services);
            WindowChrome.ApplyTheme(_board, Services.Settings.Appearance.Theme);
            _board.Closed += (_, _) => _board = null;
            _board.Activate();
            // The board is opaque and full-screen; the screen overlay would draw over it.
            Overlay.Hide();
        }
        else
        {
            _board.Close();
            _board = null;
            Overlay.Show();
        }
    }

    public void OpenSettings()
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(Services, Updater);
        _settings.SettingsChanged += OnSettingsChanged;
        _settings.Closed += (_, _) =>
        {
            _settings.SettingsChanged -= OnSettingsChanged;
            _settings = null;
        };
        _settings.Activate();
    }

    private void OnSettingsChanged()
    {
        _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                         Services.Settings.Appearance.FloatingBarScale);
        if (_bar is not null) WindowChrome.ApplyTheme(_bar, Services.Settings.Appearance.Theme);
        if (_board is not null) WindowChrome.ApplyTheme(_board, Services.Settings.Appearance.Theme);
        ApplyEdgeGestureSetting();
    }

    public void OpenTimer()
    {
        if (_countdown is not null) { _countdown.Activate(); return; }
        _countdown = new CountdownWindow();
        WindowChrome.ApplyTheme(_countdown, Services.Settings.Appearance.Theme);
        _countdown.Closed += (_, _) => _countdown = null;
        _countdown.Activate();
    }

    public void OpenLottery()
    {
        if (_lottery is not null) { _lottery.Activate(); return; }
        _lottery = new LotteryWindow(Services.Lottery);
        WindowChrome.ApplyTheme(_lottery, Services.Settings.Appearance.Theme);
        _lottery.Closed += (_, _) => _lottery = null;
        _lottery.Activate();
    }

    public void OpenReplay()
    {
        if (_replay is not null) { _replay.Activate(); return; }
        // Replay whichever surface the user is looking at: the board page when open, else the overlay.
        var source = _board is not null
            ? Services.Board.Current.Document.Strokes
            : Services.OverlayInk.Document.Strokes;
        _replay = new ReplayWindow(source, Services.Settings.Appearance.Theme);
        _replay.Closed += (_, _) => _replay = null;
        _replay.Activate();
    }

    public void OpenGuide()
    {
        if (_guide is not null) { _guide.Activate(); return; }
        _guide = InfoWindow.OpenGuide(Services.Settings.Appearance.Theme);
        _guide.Closed += (_, _) => _guide = null;
        _guide.Activate();
    }

    public async Task ScreenshotAsync()
    {
        try
        {
            var screen = ScreenService.Primary;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "InkCanvas");
            await ScreenshotService.CaptureToFileAsync(dir, screen.X, screen.Y, screen.Width, screen.Height,
                Services.Settings.Automation.IsSaveScreenshotsInDateFolders);

            if (Services.Settings.Automation.IsAutoSaveStrokesAtScreenshot)
                Services.AutoSave.SaveSnapshot(Services.OverlayInk.Document.Strokes, "screenshot");
        }
        catch
        {
            // Capture can fail on a locked-down desktop; never take the app down for it.
        }
    }

    public void ToggleFold() => _bar?.Reposition(
        Services.Settings.Appearance.FloatingBarBottomMargin,
        Services.Settings.Appearance.FloatingBarScale);

    public void ToggleClickThrough()
    {
        Overlay.ClickThrough = !Overlay.ClickThrough;
        if (_bar is not null)
        {
            _bar.Bar.ClickThroughActive = Overlay.ClickThrough;
            _bar.Bar.RefreshSelection();
        }
    }

    /// <summary>Launch in pass-through mode so the overlay does not capture the desktop's input.</summary>
    public void StartInClickThrough()
    {
        if (Overlay.ClickThrough) return;
        ToggleClickThrough();
    }

    public void PptNext() => Services.Ppt.Next();

    public void PptPrevious() => Services.Ppt.Previous();

    public void PptExit() => Services.Ppt.ExitSlideShow();

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _pollTimer.Stop();

        if (Services.Settings.Automation.IsAutoSaveStrokesAtClear)
            Services.AutoSave.SaveSnapshot(Services.OverlayInk.Document.Strokes, "exit");
        Services.AutoSave.Prune();
        Services.SaveSettings();

        Dispose();
        Application.Current.Exit();
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _countdown?.Dispose();
        Updater.Dispose();
        Overlay.Dispose();
        Services.Dispose();
    }
}