using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Features.Ppt;
using InkCanvas.Features.Persistence;
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
    private readonly PptInkSession _pptSession;
    private FloatingBarWindow _bar;
    private BoardWindow _board;
    private SettingsWindow _settings;
    private CountdownWindow _countdown;
    private LotteryWindow _lottery;
    private ReplayWindow _replay;
    private InfoWindow _guide;
    private PenPaletteWindow _palette;
    private ShapePickerWindow _shapePicker;
    private PptNavigationWindow _pptBottomLeft, _pptBottomRight, _pptSideLeft, _pptSideRight;
    private bool _lastSlideShowState;
    private bool _exiting;

    public ShellController(AppServices services)
    {
        Services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Overlay = new OverlayHost(services.OverlayInk);
        _pptSession = new PptInkSession(services.Ppt,
            new PptInkFileStore(services.Settings.Automation.AutoSavedStrokesLocation))
        {
            CaptureCurrentInk = () => Services.OverlayInk.Document.Strokes,
            ApplyCurrentInk = strokes =>
            {
                var controller = Services.OverlayInk;
                controller.Document.Clear();
                controller.Document.AddRange(strokes);
                controller.Selection.Clear();
                controller.History.Clear();
                _bar?.Bar.RefreshSelection();
            },
            AutoSavePresentationInk = () => Services.Settings.PowerPoint.IsAutoSaveStrokesInPowerPoint,
            AutoSavePptScreenshots = () => Services.Settings.PowerPoint.IsAutoSaveScreenShotInPowerPoint,
            NotifyPreviousPage = () => Services.Settings.PowerPoint.IsNotifyPreviousPage,
            NotifyHiddenSlides = () => Services.Settings.PowerPoint.IsNotifyHiddenPage,
            NotifySlideTimings = () => Services.Settings.PowerPoint.IsNotifyAutoPlayPresentation,
            AutoSaveRoot = () => Services.Settings.Automation.AutoSavedStrokesLocation,
            UseScreenshotDateFolders = () => Services.Settings.Automation.IsSaveScreenshotsInDateFolders,
            SaveInkWithScreenshot = () => Services.Settings.Automation.IsAutoSaveStrokesAtScreenshot,
            MinimumScreenshotStrokeCount = () => Services.Settings.Automation.MinimumAutomationStrokeNumber,
        };
        _pptSession.PresentationNotice += OnPptPresentationNotice;
        _pptSession.ScreenshotRequested += OnPptScreenshotRequested;
        _pptSession.Failed += failure => Core.Diagnostics.FileLog.WriteLogToFile(
            $"PPT/{failure.Operation}: {failure.Message} (0x{failure.ErrorCode:X8})", Core.Diagnostics.LogType.Error);
        _pptSession.Poll();
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
        _bar.Bar.LayoutChanged += OnBarLayoutChanged;
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
            toggleBoard: ToggleBoard,
            pptNext: () => { if (_lastSlideShowState) PptNext(); },
            pptPrevious: () => { if (_lastSlideShowState) PptPrevious(); },
            pptExit: () => { if (_lastSlideShowState) PptExit(); });

        _pollTimer.Start();

        // Delete-selection changes the button count; FloatingBar raises LayoutChanged for this.
        Services.OverlayInk.SlideFlipRequested += next => { if (next) PptNext(); else PptPrevious(); };

        Core.Diagnostics.FileLog.NewLog($"Ink Canvas 启动，版本 {Updater.CurrentVersion}");

        ApplyEdgeGestureSetting();

        if (Services.Settings.Startup.IsAutoUpdate)
            _ = Updater.CheckAsync(interactive: false);
    }

    private void OnBarLayoutChanged()
    {
        _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                         Services.Settings.Appearance.FloatingBarScale);
    }

    private InkController ActiveController => _board is not null
        ? Services.Board.Current
        : Services.OverlayInk;

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

    private void OnPptPresentationNotice(PptPresentationNotice notice)
    {
        _dispatcher.TryEnqueue(async () => await ShowPptPresentationNoticeAsync(notice));
    }

    private async Task ShowPptPresentationNoticeAsync(PptPresentationNotice notice)
    {
        if (notice.PreviousSlideIndex is > 0
            && await AskAsync("继续上次放映？", $"检测到上次播放到第 {notice.PreviousSlideIndex} 页，是否跳转？", "跳转"))
            _pptSession.GoToSlide(notice.PreviousSlideIndex.Value);

        if (notice.OfferRestoreHiddenSlides
            && await AskAsync("发现隐藏幻灯片", "是否恢复此演示文稿中的所有隐藏幻灯片？", "恢复"))
            _pptSession.RestoreHiddenSlides();

        if (notice.OfferDisableTimings
            && await AskAsync("发现自动播放计时", "是否清除此演示文稿中的自动换页计时？", "清除计时"))
            _pptSession.DisableSlideTimings();

        if (notice.LegacyInkFilesDetected)
            await NotifyAsync("发现旧版墨迹存档", "发现 .icstk/.icart 存档。该 WPF ISF 格式无法在 WinUI 3 直接读取；当前仅保留文件，不会覆盖或删除。");
    }

    private async Task<bool> AskAsync(string title, string message, string primary)
    {
        if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null) return false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnPptScreenshotRequested(PptScreenshotRequest request)
    {
        // Capture screen pixels synchronously before the COM navigation mutates the slideshow.
        try
        {
            var area = ScreenService.Primary;
            var (pixels, width, height) = ScreenshotService.Capture(area.X, area.Y, area.Width, area.Height);
            if (width <= 0 || height <= 0) return;
            string path = request.SuggestedPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                string root = Services.Settings.Automation.AutoSavedStrokesLocation;
                path = Path.Combine(root, "Auto Saved - PPT Screenshots", DateTime.Now.ToString("yyyy-MM-dd"),
                    $"{request.Snapshot.ShowPosition}_{DateTime.Now:HH-mm-ss}.png");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _ = ScreenshotService.SavePngAsync(path, pixels, width, height);
            if (request.SaveInkAlongside)
            {
                string ink = Path.ChangeExtension(path, ".icink");
                InkFileStore.Save(ink, request.Strokes);
            }
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"PPT screenshot: {ex}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>Periodic mode maintenance: slideshow detection, auto-fold, layering.</summary>
    private void Poll()
    {
        if (_exiting) return;

        if (Services.Settings.PowerPoint.PowerPointSupport)
        {
            var previous = _pptSession.Snapshot;
            var current = _pptSession.Poll();
            bool isShow = current.IsSlideShowRunning;
            if (isShow != _lastSlideShowState)
            {
                _lastSlideShowState = isShow;
                _bar?.Bar.SetPptModeVisible(isShow);
                _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                                 Services.Settings.Appearance.FloatingBarScale);
                if (isShow && Services.Settings.PowerPoint.IsShowCanvasAtNewSlideShow)
                    Overlay.Show();
            }
            UpdatePptNavigation(current);
        }
        else
        {
            UpdatePptNavigation(PptSnapshot.Detached);
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

    private void UpdatePptNavigation(PptSnapshot snapshot)
    {
        bool inShow = snapshot?.IsSlideShowRunning == true;
        var s = Services.Settings.PowerPoint;
        if (!inShow)
        {
            ClosePptNavigationWindows();
            return;
        }

        if (s.IsShowBottomPPTNavigationPanel)
        {
            _pptBottomLeft ??= CreatePptNavigation(PptNavigationPlacement.BottomLeft);
            _pptBottomRight ??= CreatePptNavigation(PptNavigationPlacement.BottomRight);
        }
        if (s.IsShowSidePPTNavigationPanel)
        {
            _pptSideLeft ??= CreatePptNavigation(PptNavigationPlacement.SideLeft);
            _pptSideRight ??= CreatePptNavigation(PptNavigationPlacement.SideRight);
        }

        _pptBottomLeft?.Update(snapshot, s.IsShowBottomPPTNavigationPanel, s.IsShowPPTNavigationBottom);
        _pptBottomRight?.Update(snapshot, s.IsShowBottomPPTNavigationPanel, s.IsShowPPTNavigationBottom);
        _pptSideLeft?.Update(snapshot, s.IsShowSidePPTNavigationPanel, s.IsShowPPTNavigationSides);
        _pptSideRight?.Update(snapshot, s.IsShowSidePPTNavigationPanel, s.IsShowPPTNavigationSides);
    }

    private PptNavigationWindow CreatePptNavigation(PptNavigationPlacement placement)
    {
        var window = new PptNavigationWindow(placement, this, Services.Settings.Appearance.Theme);
        return window;
    }

    private void ClosePptNavigationWindows()
    {
        _pptBottomLeft?.Close(); _pptBottomLeft = null;
        _pptBottomRight?.Close(); _pptBottomRight = null;
        _pptSideLeft?.Close(); _pptSideLeft = null;
        _pptSideRight?.Close(); _pptSideRight = null;
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
            _board = new BoardWindow(Services, this);
            _board.CurrentPageChanged += OnBoardPageChanged;
            if (_bar is not null) _bar.Bar.Controller = _board.CurrentController;
            WindowChrome.ApplyTheme(_board, Services.Settings.Appearance.Theme);
            _board.Closed += (_, _) => OnBoardClosed();
            _board.Activate();
            // The board is opaque and full-screen; the screen overlay would draw over it.
            Overlay.Hide();
        }
        else
        {
            _board.Close();
        }
    }

    private void OnBoardPageChanged()
    {
        if (_board is null || _bar is null) return;
        _bar.Bar.Controller = _board.CurrentController;
        _palette?.Close();
        _palette = null;
    }

    private void OnBoardClosed()
    {
        _board.CurrentPageChanged -= OnBoardPageChanged;
        _board = null;
        if (_bar is not null) _bar.Bar.Controller = Services.OverlayInk;
        Overlay.Show();
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
        if (_lastSlideShowState) UpdatePptNavigation(_pptSession.Snapshot);
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

    public void OpenPenPalette(bool highlighter)
    {
        if (_palette is not null) { _palette.Close(); _palette = null; }
        var controller = _board is not null ? Services.Board.Current : Services.OverlayInk;
        _palette = new PenPaletteWindow(controller, highlighter, Services.Settings.Appearance.Theme)
        {
            HighlighterAlpha = Services.Settings.Canvas.InkAlpha,
            LightColorScheme = Services.Settings.Appearance.Theme == 1,
            InkWidthChanged = width =>
            {
                Services.Settings.Canvas.InkWidth = width;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            InkStyleChanged = style =>
            {
                Services.Settings.Canvas.InkStyle = style;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            InkAlphaChanged = alpha =>
            {
                Services.Settings.Canvas.InkAlpha = alpha;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            AttributesChanged = () => _bar?.Bar.RefreshSelection(),
        };
        _palette.Closed += (_, _) => _palette = null;
        _palette.Activate();
        if (_bar is not null)
        {
            var pos = _bar.AppWindow.Position;
            _palette.PlaceAboveBar(pos.X, pos.Y);
        }
        WindowChrome.EnsureAbove(_palette);
    }

    public void OpenShapePicker()
    {
        if (_shapePicker is not null) { _shapePicker.Close(); _shapePicker = null; }
        _shapePicker = new ShapePickerWindow(ActiveController,
            () => _bar?.Bar.RefreshSelection(), Services.Settings.Appearance.Theme);
        _shapePicker.Closed += (_, _) => _shapePicker = null;
        _shapePicker.Activate();
        if (_bar is not null)
        {
            var pos = _bar.AppWindow.Position;
            _shapePicker.PlaceAboveBar(pos.X, pos.Y);
        }
        WindowChrome.EnsureAbove(_shapePicker);
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
        bool enableMouseMode = !Overlay.ClickThrough;
        if (enableMouseMode)
        {
            SaveBeforeMouseMode();
            bool showInk = _lastSlideShowState
                ? Services.Settings.PowerPoint.IsShowStrokeOnSelectInPowerPoint
                : !Services.Settings.Canvas.HideStrokeWhenSelecting;
            Overlay.SetInteractionMode(clickThrough: true, inkVisible: showInk);
        }
        else
        {
            // CursorIcon_Click in the WPF source restores drawing without clearing strokes.
            Overlay.SetInteractionMode(clickThrough: false, inkVisible: true);
        }

        if (_bar is not null)
        {
            _bar.Bar.ClickThroughActive = Overlay.ClickThrough;
            _bar.Bar.RefreshSelection();
        }
    }

    private void SaveBeforeMouseMode()
    {
        var strokes = ActiveController.Document.Strokes;
        if (strokes.Count <= Services.Settings.Automation.MinimumAutomationStrokeNumber) return;
        if (_lastSlideShowState) _pptSession.RequestScreenshot();
        else _ = ScreenshotAsync();
        Services.AutoSave.SaveSnapshot(strokes, _lastSlideShowState ? "ppt-mouse-mode" : "mouse-mode");
    }

    /// <summary>Launch in pass-through mode so the overlay does not capture the desktop's input.</summary>
    public void StartInClickThrough()
    {
        if (Overlay.ClickThrough) return;
        ToggleClickThrough();
    }

    public void PptNext() => _pptSession.Next();

    public void PptPrevious() => _pptSession.Previous();

    public void PptExit() => _pptSession.ExitSlideShow();

    public void PptOpenNavigation()
    {
        if (!_lastSlideShowState) return;
        if (!Overlay.ClickThrough) ToggleClickThrough();
        _pptSession.ShowSlideNavigationPane();
    }

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
        ClosePptNavigationWindows();
        _pptSession.Dispose();
        Services.Ppt.Dispose();
        Updater.Dispose();
        Overlay.Dispose();
        Services.Dispose();
    }
}