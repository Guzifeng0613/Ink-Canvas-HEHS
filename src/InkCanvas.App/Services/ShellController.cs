using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Classroom;
using InkCanvas.Core.Ink;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Features.Board;
using InkCanvas.Features.Classroom;
using InkCanvas.Features.Ppt;
using InkCanvas.Features.Persistence;
using InkCanvas.Features.Tools;
using InkCanvas.Ink;
using InkCanvas.Platform.Ai;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.System;
using InkCanvas.Platform.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace InkCanvas.App.Services;

/// <summary>
/// Owns the windows and implements the shell actions the floating bar invokes. Keeps App.xaml.cs
/// down to startup wiring.
/// </summary>
public sealed class ShellController : IShellActions, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _pollTimer;
    private readonly PptInkFileStore _pptStore;
    private readonly PptInkSession _pptSession;
    private FloatingBarWindow _bar;
    private BoardWindow _board;
    private SettingsWindow _settings;
    private CountdownWindow _countdown;
    private LotteryWindow _lottery;
    private RosterWindow _roster;
    private SpotlightWindow _spotlight;
    private MagnifierWindow _magnifier;
    private ToolkitWindow _toolkit;
    private ReplayWindow _replay;
    private TimeMachineWindow _timeMachine;
    private InkRecorder _recorder;

    private InfoWindow _guide;
    private PenPaletteWindow _palette;
    private ShapePickerWindow _shapePicker;
    private readonly NavigationGuard _pptGuard = new();
    private readonly SlideSwitchGate _pptGate = new();
    private PptNavigationWindow _pptNav;
    private NavigationCorner _pptCorner = NavigationCorner.BottomLeft;
    private bool _pptNavCollapsed;
    private bool _manualPptNav;
    private PptSnapshot _pptShowSnapshot;
    private bool _pptAlignedToShow;
    private int _pptCaptureSession;
    private int _pptCaptureGeneration;
    private string _pptScreenshotPath;
    private int _pptScreenshotSlideIndex;
    private int _pptScreenshotSlideId;
    private bool _lastSlideShowState;
    private readonly PresentationSourceSelector _pptSources = new();
    private readonly ComPresentationSource _pptComSource;
    private readonly WindowPresentationSource _pptWindowSource = new();
    private readonly KeyPresentationSource _pptKeySource = new();
    private BlackScreenWindow _blackScreen;
    private bool _blackScreenEscapeHotkey;
    private bool _blackScreenEscapeFailureLogged;
    private bool _hotkeysReady;
    private bool _presentationKeyFailureLogged;
    private DateTime _lastPptRpcBusyLogUtc = DateTime.MinValue;
    private bool _exiting;
    private AssistantWindow _aiAssistant;
    private AiStreamHandle _aiStream;
    private AiStreamHandle _aiRegionStream;
    private AssistantSession _aiSession;
    private AssistantRect _aiAnchor;
    private AiClient _aiClient;
    private string _aiThumb = "";
    private bool _aiTwoStage;
    private string _aiStageFirstToken;
    private bool _aiStageReset;
    private string _aiPreStageText = "";
    private List<AssistantStep> _aiPreStageSteps = new();
    private string _aiPreStageFormula = "";
    private bool _aiSecondStageStarted;

    public ShellController(AppServices services)
    {
        Services = services;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Overlay = new OverlayHost(services.OverlayInk);
        _pptStore = new PptInkFileStore(services.Settings.Automation.AutoSavedStrokesLocation);
        _pptSession = new PptInkSession(services.Ppt, _pptStore)
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
        _pptSession.InkTransition += OnPptInkTransition;
        _pptSession.Failed += LogPptFailure;
        _pptComSource = new ComPresentationSource(
            () => _pptSession.Snapshot.IsAttached,
            () => _pptSession.Next(),
            () => _pptSession.Previous());
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
        RegisterOverlayHotkeys();

        // A show may already be running when the bar is created; page keys are only
        // registered during a show so they are not stolen from other apps.
        _hotkeysReady = true;
        if (_lastSlideShowState && !Overlay.SetPresentationKeysEnabled(true))
            Core.Diagnostics.FileLog.WriteLogToFile("PPT 翻页热键注册失败（可能被其他程序占用）", Core.Diagnostics.LogType.Info);

        _pollTimer.Start();

        // Delete-selection changes the button count; FloatingBar raises LayoutChanged for this.
        Services.OverlayInk.SlideFlipRequested += next => { if (next) PptNext(); else PptPrevious(); };

        Core.Diagnostics.FileLog.NewLog($"Ink Canvas 启动，版本 {Updater.CurrentVersion}");

        ApplyEdgeGestureSetting();

        if (Services.Settings.Startup.IsAutoUpdate)
            _ = Updater.CheckAsync(interactive: false);
    }

    /// <summary>
    /// The default hotkey set. Extracted so it can be replayed after the Win32 hotkey service
    /// drops a temporary key (the service only supports unregistering everything).
    /// </summary>
    private void RegisterOverlayHotkeys()
    {
        Overlay.RegisterHotkeys(
            undo: () => Services.OverlayInk.Undo(),
            redo: () => Services.OverlayInk.Redo(),
            togglePen: () => { Services.OverlayInk.Tool = InkTool.Pen; _bar.Bar.RefreshSelection(); },
            toggleEraser: () => { Services.OverlayInk.Tool = InkTool.Eraser; _bar.Bar.RefreshSelection(); },
            clear: () => Services.OverlayInk.Clear(),
            togglePassThrough: ToggleClickThrough,
            toggleBoard: ToggleBoard,
            pptNext: () => HandlePresentationKey(PresentationKeyMap.VkPageDown),
            pptPrevious: () => HandlePresentationKey(PresentationKeyMap.VkPageUp),
            pptExit: () => { if (_lastSlideShowState) PptExit(); },
            toggleBlackScreen: ToggleBlackScreen);
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
        if (notice is not null && _pptSession.Snapshot is { IsSlideShowRunning: true, Slide.Index: > 0 } snapshot)
        {
            // 与 InkTransition 的 Begin 事件共用同一载荷格式；有截图时带上截图路径，
            // 避免后续重复事件把已记录的截图清空。
            int slideIndex = snapshot.Slide.Index;
            int slideId = snapshot.Slide.SlideId;
            bool hasScreenshot = _pptScreenshotSlideIndex == slideIndex
                && (_pptScreenshotSlideId == slideId || _pptScreenshotSlideId == 0);
            string screenshot = hasScreenshot && File.Exists(_pptScreenshotPath) ? _pptScreenshotPath : "";
            RecordClassroomEvent(TimelineEventKind.PptSlideChanged,
                InkTimeline.EncodePptSlideChanged(slideId, slideIndex, screenshot));
            RecordClassroomEvent(TimelineEventKind.PptInkChanged,
                Services.OverlayInk.Document.Strokes.Count.ToString());
        }
        _dispatcher.TryEnqueue(async () => await ShowPptPresentationNoticeAsync(notice));
    }

    private async Task ShowPptPresentationNoticeAsync(PptPresentationNotice notice)
    {
        if (notice.PreviousSlideIndex is > 0)
        {
            string resumeMessage = $"检测到上次播放到第 {notice.PreviousSlideIndex} 页，是否跳转？";
            if (notice.ResumeIndexDegraded) resumeMessage += "（按旧页码降级恢复，页序可能已变化）";
            if (await AskAsync("继续上次放映？", resumeMessage, "跳转"))
                _pptSession.GoToSlide(notice.PreviousSlideIndex.Value);
        }

        if (notice.OfferRestoreHiddenSlides
            && await AskAsync("发现隐藏幻灯片", "是否恢复此演示文稿中的所有隐藏幻灯片？", "恢复"))
            _pptSession.RestoreHiddenSlides();

        if (notice.OfferDisableTimings
            && await AskAsync("发现自动播放计时", "是否清除此演示文稿中的自动换页计时？", "清除计时"))
            _pptSession.DisableSlideTimings();

        if (notice.LegacyInkFilesDetected)
            await NotifyAsync("发现旧版墨迹存档", "发现 .icstk/.icart 存档。该 WPF ISF 格式无法在 WinUI 3 直接读取；当前仅保留文件，不会覆盖或删除。");

        if (notice.OfferInkMappingReview)
            await ShowInkRecoveryWizardAsync(notice);
        else if (!string.IsNullOrWhiteSpace(notice.BackupDirectory))
            await NotifyAsync("已升级墨迹存档",
                $"旧版按页码保存的批注已按当前页序迁移到 SlideId，原文件保留。备份：{notice.BackupDirectory}");
    }

    /// <summary>
    /// Recovery wizard for archives whose slides no longer line up. Deletion is only ever
    /// offered after an explicit confirmation, and always backed up first.
    /// </summary>
    private async Task ShowInkRecoveryWizardAsync(PptPresentationNotice notice)
    {
        try
        {
            if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null) return;
            string summary = notice.InkMappingSummary ?? "课件页序与墨迹存档不一致。";
            string backupHint = string.IsNullOrWhiteSpace(notice.BackupDirectory)
                ? "选择清空前会先备份整个墨迹目录。"
                : $"迁移备份：{notice.BackupDirectory}";
            var dialog = new ContentDialog
            {
                Title = "课件批注与当前页面不一致",
                Content = new TextBlock
                {
                    Text = $"{summary}\n{backupHint}\n\n保留现有批注不会修改任何数据。",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "保留现有批注",
                SecondaryButtonText = "备份并导出",
                CloseButtonText = "稍后处理",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root.XamlRoot,
            };
            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    "PPT ink recovery: user kept existing ink after mapping mismatch", Core.Diagnostics.LogType.Info);
                return;
            }
            if (result != ContentDialogResult.Secondary)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    "PPT ink recovery: user postponed the mapping decision", Core.Diagnostics.LogType.Info);
                return;
            }

            string directory = _pptStore.GetPresentationDirectory(notice.Details.Presentation);
            string backup = SlideInkMigrator.Backup(directory);
            if (string.IsNullOrWhiteSpace(backup))
            {
                await NotifyAsync("备份失败", "无法备份该课件的墨迹目录，已取消操作，未删除任何数据。");
                Core.Diagnostics.FileLog.WriteLogToFile(
                    "PPT ink recovery: backup failed, nothing cleared", Core.Diagnostics.LogType.Error);
                return;
            }
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"PPT ink recovery: backup exported to {backup}", Core.Diagnostics.LogType.Info);
            if (!await AskAsync("清空该课件的批注？",
                    $"批注已备份到：\n{backup}\n\n仅在确认后清空此课件的批注，其他数据不受影响。", "清空批注"))
            {
                await NotifyAsync("已导出备份", $"未清空任何批注。备份位置：{backup}");
                return;
            }
            _pptSession.ClearPresentationInk();
            await NotifyAsync("批注已清空", $"已备份到 {backup}，随后清空了该课件的批注。");
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"PPT ink recovery: user cleared ink after backup {backup}", Core.Diagnostics.LogType.Info);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"PPT ink recovery wizard: {ex}", Core.Diagnostics.LogType.Error);
        }
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
        // ScreenshotRequested is raised synchronously from inside PptInkSession (navigation or
        // poll), so there is no in-flight async work to cancel here. The session/generation
        // check is still enforced: once the slide generation advanced, a late or replayed
        // request must not overwrite a screenshot for the slide that is now current.
        if (!_pptGate.IsCurrent(_pptCaptureSession, _pptCaptureGeneration))
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                "PPT screenshot skipped: stale slide generation", Core.Diagnostics.LogType.Info);
            return;
        }

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
            // 记住最近一次成功请求的截图，后续 PptSlideChanged 事件会把它的路径写入时间轴。
            _pptScreenshotPath = path;
            _pptScreenshotSlideIndex = request.Snapshot.Slide?.Index ?? request.InkSlide?.Index ?? 0;
            _pptScreenshotSlideId = request.Snapshot.Slide?.SlideId ?? request.InkSlide?.SlideId ?? 0;
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
            try
            {
                // Each poll opens a new slide generation. Screenshot requests raised synchronously
                // from inside PptInkSession.Poll() are validated against it on the handler side.
                int generation = _pptGate.NextGeneration();
                _pptCaptureSession = _pptGate.Session;
                _pptCaptureGeneration = generation;

                var current = _pptSession.Poll();

                // COM unavailable and no show visible through automation: WPS/PowerPoint may still
                // be running a full-screen viewer. Treat its window as a degraded slideshow so the
                // pager stays usable (page numbers stay 0/0, the snapshot has no slide data).
                bool windowAvailable = false;
                if (!current.IsAttached && !current.IsSlideShowRunning)
                {
                    FallbackShowWindow fallback = PresentationFallbackDetector.Detect();
                    if (fallback is not null)
                    {
                        windowAvailable = true;
                        current = new PptSnapshot
                        {
                            State = PptConnectionState.SlideShow,
                            WindowHandle = fallback.Handle,
                        };
                    }
                }
                _pptSources.Select(current.IsAttached, windowAvailable);

                bool isShow = current.IsSlideShowRunning;
                if (isShow != _lastSlideShowState)
                {
                    _lastSlideShowState = isShow;
                    if (isShow)
                    {
                        _pptGate.BeginSession();
                        // Re-anchor the capture generation: BeginSession bumped the session, so the
                        // values captured at the top of Poll are no longer current.
                        _pptCaptureSession = _pptGate.Session;
                        _pptCaptureGeneration = _pptGate.Generation;
                    }
                    else
                    {
                        // A new show starts from the top of the matrix; the black screen is a
                        // presentation aid and must not survive into editing mode.
                        _pptSources.Reset();
                        CloseBlackScreen();
                    }
                    if (_hotkeysReady)
                    {
                        bool keysReady = Overlay.SetPresentationKeysEnabled(isShow);
                        if (isShow && !keysReady && !_presentationKeyFailureLogged)
                        {
                            _presentationKeyFailureLogged = true;
                            Core.Diagnostics.FileLog.WriteLogToFile(
                                "PPT 翻页热键注册失败（可能被其他程序占用）", Core.Diagnostics.LogType.Info);
                        }
                        if (!isShow) _presentationKeyFailureLogged = false;

                        // Toggling presentation keys rebuilds the whole group, which drops the
                        // temporary black-screen Esc; put it back while the window is still up.
                        if (_blackScreen is not null)
                        {
                            _blackScreenEscapeHotkey = false;
                            TryRegisterBlackScreenEscape();
                        }
                    }
                    _bar?.Bar.SetPptModeVisible(isShow);
                    _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                                     Services.Settings.Appearance.FloatingBarScale);
                    if (isShow && Services.Settings.PowerPoint.IsShowCanvasAtNewSlideShow)
                        Overlay.Show();
                }
                UpdatePptNavigation(current);
            }
            catch (Exception ex)
            {
                // The poll must never take the app down: COM can fail while PowerPoint is busy.
                LogPptFailure(new PptFailure("Poll", ex.Message, Marshal.GetHRForException(ex)));
            }
        }
        else
        {
            _lastSlideShowState = false;
            _pptSources.Reset();
            if (_hotkeysReady) Overlay.SetPresentationKeysEnabled(false);
            CloseBlackScreen();
            UpdatePptNavigation(PptSnapshot.Detached);
        }

        // Slideshow polling must be responsive; idle polling only watches for a show starting.
        int intervalMs = _lastSlideShowState ? 250 : 1200;
        if ((int)_pollTimer.Interval.TotalMilliseconds != intervalMs)
            _pollTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);

        if (Services.Settings.Automation.IsEnableAutoFold) ApplyAutoFold();

        // 原条件：放映中 + 尚无墨迹 + 允许手指手势控制放映。未验证。
        Services.OverlayInk.Palm.PptGestureEligible =
            _lastSlideShowState
            && Services.OverlayInk.Document.Count == 0
            && Services.Settings.PowerPoint.IsEnableFingerGestureSlideShowControl;

        // Keep chrome above the ink overlay without stealing focus from a slideshow.
        if (_bar is not null) WindowChrome.EnsureAbove(_bar);
    }

    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
    private static readonly TimeSpan PptRpcBusyLogInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// PowerPoint 忙（模态对话框、正在启动/退出放映）时会拒绝 RPC 调用。这类错误是预期内的
    /// 瞬时状态，降级为 Trace 并最多每 10 秒记录一次；其它 PPT 失败仍记为 Error。
    /// </summary>
    private void LogPptFailure(PptFailure failure)
    {
        if (failure is null) return;
        if (IsPptRpcBusy(failure.ErrorCode, failure.Message))
        {
            DateTime now = DateTime.UtcNow;
            if (now - _lastPptRpcBusyLogUtc < PptRpcBusyLogInterval) return;
            _lastPptRpcBusyLogUtc = now;
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"PPT 忙，已忽略 RPC 拒绝呼叫：{failure.Operation}: {failure.Message} (0x{failure.ErrorCode:X8})",
                Core.Diagnostics.LogType.Trace);
            return;
        }

        Core.Diagnostics.FileLog.WriteLogToFile(
            $"PPT/{failure.Operation}: {failure.Message} (0x{failure.ErrorCode:X8})", Core.Diagnostics.LogType.Error);
    }

    private static bool IsPptRpcBusy(int errorCode, string message)
    {
        if (errorCode == RpcCallRejected || errorCode == RpcServerCallRetryLater) return true;
        if (string.IsNullOrEmpty(message)) return false;
        return message.Contains("被呼叫方拒绝接收呼叫", StringComparison.Ordinal)
            || message.Contains("Could not get dispatch ID", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdatePptNavigation(PptSnapshot snapshot)
    {
        bool inShow = snapshot?.IsSlideShowRunning == true;
        var s = Services.Settings.PowerPoint;
        if (!inShow)
        {
            _pptShowSnapshot = null;
            if (_manualPptNav)
            {
                // Manual mode: keep the window on the primary display, without touching the
                // collapsed/corner state the user picked while a show was running.
                if (_pptAlignedToShow)
                {
                    Overlay.ResetToPrimary();
                    _pptAlignedToShow = false;
                }

                List<NavigationCorner> manualAllowed = AllowedCorners();
                NavigationCorner corner = manualAllowed.Count == 0
                    ? NavigationCorner.BottomLeft
                    : manualAllowed.Contains(_pptCorner) ? _pptCorner : manualAllowed[0];

                _pptNav ??= CreatePptNavigation(MapPlacement(corner));
                _pptNav.Placement = MapPlacement(corner);
                // No slide counter outside a show: 0/0 would read as real page numbers.
                _pptNav.Update(PptSnapshot.Detached, !_pptNavCollapsed, false, default);
                return;
            }

            ClosePptNavigationWindows();
            _pptNavCollapsed = false;
            _pptCorner = NavigationCorner.BottomLeft;
            // Only undo the full-screen alignment if a show had moved the ink surface.
            if (_pptAlignedToShow)
            {
                Overlay.ResetToPrimary();
                _pptAlignedToShow = false;
            }
            return;
        }

        _pptShowSnapshot = snapshot;

        ShowWindowInfo bounds = default;
        if (ShowWindowTracker.TryGet(snapshot.WindowHandle, out var measured))
        {
            bounds = measured;
            Overlay.AlignTo(bounds);
            _pptAlignedToShow = true;
        }

        List<NavigationCorner> allowed = AllowedCorners();
        if (allowed.Count == 0)
        {
            // Both navigation groups are switched off: keep the window hidden.
            _pptNav?.Update(snapshot, false, false, bounds);
            return;
        }

        if (!allowed.Contains(_pptCorner)) _pptCorner = allowed[0];

        bool counterVisible = _pptCorner is NavigationCorner.SideLeft or NavigationCorner.SideRight
            ? s.IsShowPPTNavigationSides
            : s.IsShowPPTNavigationBottom;

        _pptNav ??= CreatePptNavigation(MapPlacement(_pptCorner));
        _pptNav.Placement = MapPlacement(_pptCorner);
        _pptNav.Update(snapshot, !_pptNavCollapsed, counterVisible, bounds);
    }

    /// <summary>Corners enabled by the current navigation-panel settings.</summary>
    private List<NavigationCorner> AllowedCorners()
    {
        var s = Services.Settings.PowerPoint;
        var allowed = new List<NavigationCorner>(4);
        if (s.IsShowBottomPPTNavigationPanel)
        {
            allowed.Add(NavigationCorner.BottomLeft);
            allowed.Add(NavigationCorner.BottomRight);
        }
        if (s.IsShowSidePPTNavigationPanel)
        {
            allowed.Add(NavigationCorner.SideLeft);
            allowed.Add(NavigationCorner.SideRight);
        }
        return allowed;
    }

    private static PptNavigationPlacement MapPlacement(NavigationCorner corner) => corner switch
    {
        NavigationCorner.BottomRight => PptNavigationPlacement.BottomRight,
        NavigationCorner.SideLeft => PptNavigationPlacement.SideLeft,
        NavigationCorner.SideRight => PptNavigationPlacement.SideRight,
        _ => PptNavigationPlacement.BottomLeft,
    };

    private PptNavigationWindow CreatePptNavigation(PptNavigationPlacement placement)
    {
        var window = new PptNavigationWindow(placement, this, Services.Settings.Appearance.Theme);
        return window;
    }

    private void ClosePptNavigationWindows()
    {
        _pptNav?.Close();
        _pptNav = null;
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
            _recorder = new InkRecorder(Services.Board);
            _recorder.Start();
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
        StopRecording();
        _board = null;
        if (_bar is not null) _bar.Bar.Controller = Services.OverlayInk;
        Overlay.Show();
    }

    private void StopRecording()
    {
        if (_recorder is null) return;
        _recorder.Stop();

        try { _recorder.Save(); }
        catch { /* 磁盘问题不应影响关闭流程 */ }
        _recorder.Dispose();
        _recorder = null;
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
        if (_lastSlideShowState) UpdatePptNavigation(_pptShowSnapshot ?? _pptSession.Snapshot);
        ApplyEdgeGestureSetting();
    }

    public void OpenTimer()
    {
        if (_countdown is not null) { _countdown.Activate(); return; }
        var window = new CountdownWindow();
        window.TimerStarted += OnTimerStarted;
        window.TimerStopped += OnTimerStopped;
        window.TimerEnded += OnTimerEnded;
        _countdown = window;
        WindowChrome.ApplyTheme(window, Services.Settings.Appearance.Theme);
        window.Closed += (_, _) =>
        {
            window.TimerStarted -= OnTimerStarted;
            window.TimerStopped -= OnTimerStopped;
            window.TimerEnded -= OnTimerEnded;
            if (ReferenceEquals(_countdown, window)) _countdown = null;
        };
        window.Activate();
    }

    private void OnTimerStarted(TimeSpan remaining)
        => RecordClassroomEvent(TimelineEventKind.TimerStarted,
            ((int)Math.Max(0, remaining.TotalMilliseconds)).ToString());

    private void OnTimerStopped(TimeSpan remaining)
        => RecordClassroomEvent(TimelineEventKind.TimerStopped,
            ((int)Math.Max(0, remaining.TotalMilliseconds)).ToString());

    private void OnTimerEnded() => _ = CaptureTimerEndedAsync();

    /// <summary>
    /// 倒计时归零：录制中写一条 TimerEnded（自动生成「倒计时结束」书签），
    /// 保存当前板书快照并把路径写入该事件的 Payload（<c>{"screenshot":"..."}</c>）。
    /// 非录制状态不做任何事，计时语义不变。
    /// </summary>
    private async Task CaptureTimerEndedAsync()
    {
        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || recorder.Session is null) return;

        long timeMs = recorder.ElapsedMs;
        int pageIndex = Services.Board.CurrentIndex;

        string screenshot = null;
        try
        {
            screenshot = await CaptureScreenshotToFileAsync();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"倒计时结束快照失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }

        string payload = string.IsNullOrEmpty(screenshot)
            ? "倒计时结束"
            : JsonSerializer.Serialize(new { screenshot });

        try
        {
            recorder.Append(new TimelineEvent
            {
                TimeMs = timeMs,
                PageIndex = pageIndex,
                Kind = TimelineEventKind.TimerEnded,
                Payload = payload,
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"Timeline/TimerEnded: {ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    public void OpenLottery()
    {
        if (_lottery is not null) { _lottery.Activate(); return; }
        _lottery = new LotteryWindow(Services.Lottery);
        WindowChrome.ApplyTheme(_lottery, Services.Settings.Appearance.Theme);
        _lottery.Closed += (_, _) => _lottery = null;
        _lottery.Activate();
    }

    public void OpenRoster()
    {
        if (_roster is not null) { _roster.Activate(); return; }
        var window = new RosterWindow(PasteRosterNameToBoard);
        window.NameDrawn += OnRosterNameDrawn;
        _roster = window;
        WindowChrome.ApplyTheme(window, Services.Settings.Appearance.Theme);
        window.Closed += (_, _) =>
        {
            window.NameDrawn -= OnRosterNameDrawn;
            if (ReferenceEquals(_roster, window)) _roster = null;
        };
        window.Activate();
    }

    private void OnRosterNameDrawn(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        RecordClassroomEvent(TimelineEventKind.RosterDrawn, name);
    }

    private void PasteRosterNameToBoard(string name) => _ = PasteRosterNameAsync(name);

    /// <summary>把点名结果渲染成卡片贴到当前白板页，画板未开时先打开。</summary>
    private async Task PasteRosterNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            const int width = 220;
            const int height = 72;
            byte[] png = await PasteCardRenderer.RenderPngAsync(
                "点名结果", new[] { name }, width, height);
            if (png is null || png.Length == 0)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"点名贴板：渲染 PNG 失败（{name}）", Core.Diagnostics.LogType.Error);
                return;
            }

            var work = ScreenService.WorkArea;
            double dpi = CurrentDpi();
            double viewWidth = work.Width / dpi;
            double viewHeight = work.Height / dpi;
            double left = Math.Clamp(viewWidth / 2.0 - width / 2.0, 8, Math.Max(8, viewWidth - width - 8));
            double top = Math.Clamp(viewHeight / 2.0 - height / 2.0, 8, Math.Max(8, viewHeight - height - 8));

            string metadata = new AiPasteMetadata
            {
                Kind = AiPasteKind.Steps,
                Text = name,
                Lines = new List<string> { name },
            }.Serialize();

            bool added = Services.Board.AddImage(new BoardImageAsset
            {
                Name = "点名结果",
                Data = png,
                ContentType = "image/png",
                Left = (float)left,
                Top = (float)top,
                Width = width,
                Height = height,
                Metadata = metadata,
            });

            if (!added)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"点名贴板：AddImage 失败（{name}）", Core.Diagnostics.LogType.Error);
                return;
            }

            if (_board is null) ToggleBoard();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"点名贴板失败（{name}）：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    public void OpenSpotlight()
    {
        if (_spotlight is not null) { _spotlight.Activate(); return; }
        _spotlight = new SpotlightWindow();
        _spotlight.Closed += (_, _) => _spotlight = null;
        _spotlight.Activate();
    }

    public void OpenMagnifier()
    {
        if (_magnifier is not null) { _magnifier.Activate(); return; }
        _magnifier = new MagnifierWindow();
        _magnifier.Closed += (_, _) => _magnifier = null;
        _magnifier.Activate();
    }

    public void OpenToolkit()
    {
        if (_toolkit is not null) { _toolkit.Activate(); return; }
        _toolkit = new ToolkitWindow(this, Services.Settings.Appearance.Theme);
        _toolkit.Closed += (_, _) => _toolkit = null;
        _toolkit.Activate();
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

    public void OpenTimeMachine() => _ = OpenTimeMachineAsync();

    private async Task OpenTimeMachineAsync()
    {
        if (_timeMachine is not null) { _timeMachine.Activate(); return; }

        // 正在录制画板：直接回放当前会话，无需弹会话管理。
        if (_recorder?.IsRecording == true && _recorder.Session.Events.Count > 0)
        {
            ShowTimeMachine(_recorder.Session);
            return;
        }

        var sessions = ClassroomSessionStore
            .LoadAll(ClassroomSessionStore.MaxSessionsLoaded, out int omitted)
            .Where(s => s.Events.Count > 0)
            .ToList();
        if (omitted > 0)
        {
            // 仅列表截断，不删除磁盘上的旧会话；提示用户可通过显式清理回收空间。
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"板书时光机：仅列出最近 {ClassroomSessionStore.MaxSessionsLoaded} 个会话，省略 {omitted} 个（未删除）",
                Core.Diagnostics.LogType.Info);
        }
        if (sessions.Count == 0)
        {
            await NotifyAsync("板书时光机", "暂无录制记录。先在画板中书写，再打开时光机。");
            return;
        }

        var chosen = await PickSessionAsync(sessions);
        if (chosen is not null) ShowTimeMachine(chosen);
    }

    private void ShowTimeMachine(ClassroomSession session)
    {
        _timeMachine = new TimeMachineWindow(session, Services.Board, Services.Settings.Appearance.Theme);
        _timeMachine.Closed += (_, _) => _timeMachine = null;
        _timeMachine.Activate();
    }

    /// <summary>简单会话列表对话框：选择 / 重命名 / 删除 / 导入 / 导出。</summary>
    private async Task<ClassroomSession> PickSessionAsync(List<ClassroomSession> sessions)
    {
        if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null) return null;

        var list = new ListView { Height = 300, SelectionMode = ListViewSelectionMode.Single };
        void Rebuild()
        {
            list.Items.Clear();
            foreach (var s in sessions)
                list.Items.Add($"{s.Name}  ·  {s.CreatedAt:MM-dd HH:mm}  ·  {s.Events.Count} 事件");
        }
        Rebuild();

        var nameBox = new TextBox { PlaceholderText = "新名称", MinWidth = 140 };
        var renameButton = new Button { Content = "重命名选中" };
        var deleteButton = new Button { Content = "删除选中" };
        var importButton = new Button { Content = "导入…" };
        var exportButton = new Button { Content = "导出选中…" };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actions.Children.Add(nameBox);
        actions.Children.Add(renameButton);
        actions.Children.Add(deleteButton);
        actions.Children.Add(importButton);
        actions.Children.Add(exportButton);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "选择要回放的板书会话：", Opacity = 0.8 });
        panel.Children.Add(list);
        panel.Children.Add(actions);

        renameButton.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0 || string.IsNullOrWhiteSpace(nameBox.Text)) return;
            string newName = nameBox.Text.Trim();
            if (ClassroomSessionStore.Rename(sessions[i].Id, newName)) { sessions[i].Name = newName; Rebuild(); list.SelectedIndex = i; }
        };
        deleteButton.Click += (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            ClassroomSessionStore.Delete(sessions[i].Id);
            sessions.RemoveAt(i);
            Rebuild();
        };
        importButton.Click += async (_, _) =>
        {
            var file = await PickSessionFileAsync(save: false);
            if (file is null) return;
            var imported = ClassroomSessionStore.Import(file.Path);
            if (imported is null) return;
            sessions.Insert(0, imported);
            Rebuild();
        };
        exportButton.Click += async (_, _) =>
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            var file = await PickSessionFileAsync(save: true);
            if (file is null) return;
            ClassroomSessionStore.Export(sessions[i], file.Path);
        };

        var dialog = new ContentDialog
        {
            Title = "板书时光机 · 会话",
            Content = panel,
            PrimaryButtonText = "回放所选",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        int index = list.SelectedIndex;
        return index >= 0 && index < sessions.Count ? sessions[index] : null;
    }

    private async Task<StorageFile> PickSessionFileAsync(bool save)
    {
        if (_bar is null) return null;
        if (save)
        {
            var picker = new FileSavePicker { SuggestedFileName = "classroom-session" };
            picker.FileTypeChoices.Add("板书会话", new List<string> { ".json" });
            InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(_bar));
            return await picker.PickSaveFileAsync();
        }
        else
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(_bar));
            return await picker.PickSingleFileAsync();
        }
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

    public async Task ScreenshotAsync() => await CaptureScreenshotToFileAsync();

    /// <summary>整屏截图并保存；返回文件路径，失败返回 null。</summary>
    private async Task<string> CaptureScreenshotToFileAsync()
    {
        try
        {
            var screen = ScreenService.Primary;
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "InkCanvas");
            string path = await ScreenshotService.CaptureToFileAsync(dir, screen.X, screen.Y, screen.Width, screen.Height,
                Services.Settings.Automation.IsSaveScreenshotsInDateFolders);

            if (Services.Settings.Automation.IsAutoSaveStrokesAtScreenshot)
                Services.AutoSave.SaveSnapshot(Services.OverlayInk.Document.Strokes, "screenshot");

            return path;
        }
        catch (Exception ex)
        {
            // Capture can fail on a locked-down desktop; never take the app down for it.
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"截图失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            return null;
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

    public void PptNext() => NavigatePpt(next: true);

    public void PptPrevious() => NavigatePpt(next: false);

    private void NavigatePpt(bool next)
    {
        long now = Environment.TickCount64;
        if (!_pptGuard.TryEnter(now)) return;
        try
        {
            // The generation guards the screenshot request raised synchronously by the session.
            int generation = _pptGate.NextGeneration();
            _pptCaptureSession = _pptGate.Session;
            _pptCaptureGeneration = generation;

            bool moved = NavigateThroughSources(next);

            // The host receives its own simulated navigation back as a slide change; remember
            // that it was ours so the poll loop does not treat it as an external event.
            if (moved) _pptGuard.SuppressExternal(now);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"PPT navigation: {ex}", Core.Diagnostics.LogType.Error);
        }
        finally
        {
            _pptGuard.Exit(now);
        }
    }

    /// <summary>
    /// COM → full-screen window → raw keys. Outside a slide show only COM is attempted, which
    /// preserves the previous behaviour and avoids injecting Page Down into unrelated apps.
    /// Each failure is recorded once per show via <see cref="PresentationSourceSelector"/>.
    /// </summary>
    private bool NavigateThroughSources(bool next)
    {
        PresentationSourceLevel level = _pptSources.Active;
        while (level != PresentationSourceLevel.None)
        {
            bool moved;
            switch (level)
            {
                case PresentationSourceLevel.Com:
                    moved = next ? _pptComSource.Next() : _pptComSource.Previous();
                    if (moved) return true;
                    if (!_lastSlideShowState) return false;
                    DegradePptSource(level);
                    break;
                case PresentationSourceLevel.Window:
                    if (!_pptWindowSource.IsAvailable)
                    {
                        DegradePptSource(level);
                        break;
                    }
                    moved = next ? _pptWindowSource.Next() : _pptWindowSource.Previous();
                    if (moved) return true;
                    DegradePptSource(level);
                    break;
                case PresentationSourceLevel.Keys:
                    moved = next ? _pptKeySource.Next() : _pptKeySource.Previous();
                    if (moved) return true;
                    DegradePptSource(level);
                    return false;
                default:
                    return false;
            }
            level = _pptSources.Active;
        }
        return false;
    }

    private void DegradePptSource(PresentationSourceLevel failed)
    {
        PresentationSourceLevel previous = _pptSources.Active;
        _pptSources.ReportFailure(failed);
        Core.Diagnostics.FileLog.WriteLogToFile(
            $"PPT 翻页降级：{previous} → {_pptSources.Active}（{_pptSources.LastReason}）",
            Core.Diagnostics.LogType.Info);
    }

    private void HandlePresentationKey(int virtualKey)
    {
        if (!_lastSlideShowState) return;
        switch (PresentationKeyMap.Map(virtualKey))
        {
            case PresentationKeyAction.Next:
                PptNext();
                break;
            case PresentationKeyAction.Previous:
                PptPrevious();
                break;
            case PresentationKeyAction.ToggleBlackScreen:
                ToggleBlackScreen();
                break;
        }
    }

    public void ToggleBlackScreen()
    {
        if (_blackScreen is not null)
        {
            CloseBlackScreen();
            return;
        }
        _blackScreen = new BlackScreenWindow();
        _blackScreen.Closed += (_, _) =>
        {
            _blackScreen = null;
            ReleaseBlackScreenEscape();
        };
        _blackScreen.Activate();
        // The window can show without owning focus, so cover that case with a global Esc that
        // only exists while the black screen is up. The window also handles Esc itself.
        TryRegisterBlackScreenEscape();
        Core.Diagnostics.FileLog.WriteLogToFile("黑屏已开启（F9/B 或点击/Esc 恢复）", Core.Diagnostics.LogType.Info);
    }

    /// <summary>
    /// Escape is reserved only while the black screen is visible. Registration fails when the
    /// key is already taken (e.g. Esc exits a running slideshow), in which case the window's own
    /// KeyDown still closes it.
    /// </summary>
    private void TryRegisterBlackScreenEscape()
    {
        if (_blackScreen is null || _blackScreenEscapeHotkey) return;
        if (Overlay.Hotkeys.Register(HotkeyModifiers.None, 0x1B /* Escape */, ToggleBlackScreen))
        {
            _blackScreenEscapeHotkey = true;
            _blackScreenEscapeFailureLogged = false;
            return;
        }
        if (_blackScreenEscapeFailureLogged) return;
        _blackScreenEscapeFailureLogged = true;
        Core.Diagnostics.FileLog.WriteLogToFile(
            "黑屏 Esc 全局热键注册失败，改用窗口内按键关闭", Core.Diagnostics.LogType.Info);
    }

    private void ReleaseBlackScreenEscape()
    {
        if (!_blackScreenEscapeHotkey) return;
        _blackScreenEscapeHotkey = false;
        // HotkeyService can only drop the whole group: rebuild it so Esc is not held after close.
        Overlay.Hotkeys.UnregisterAll();
        RegisterOverlayHotkeys();
    }

    private void CloseBlackScreen()
    {
        BlackScreenWindow window = _blackScreen;
        if (window is null) return;
        _blackScreen = null;
        try { window.Close(); }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"关闭黑屏失败：{ex}", Core.Diagnostics.LogType.Error);
        }
        finally { ReleaseBlackScreenEscape(); }
    }

    public void RotatePptNavigationCorner()
    {
        List<NavigationCorner> allowed = AllowedCorners();
        if (allowed.Count == 0) return;

        NavigationCorner candidate = _pptCorner;
        for (int i = 0; i < 4; i++)
        {
            candidate = NavigationLayout.NextCorner(candidate);
            if (allowed.Contains(candidate)) break;
        }
        _pptCorner = candidate;

        var snapshot = _pptShowSnapshot;
        if (snapshot is not null) UpdatePptNavigation(snapshot);
    }

    public void CollapsePptNavigation()
    {
        _pptNavCollapsed = true;
        var snapshot = _pptShowSnapshot;
        if (snapshot is not null) UpdatePptNavigation(snapshot);
        else _pptNav?.Update(PptSnapshot.Detached, false, false, default);
    }

    public void TogglePptNavigationWindow()
    {
        if (_manualPptNav)
        {
            _manualPptNav = false;
            ClosePptNavigationWindows();
            _pptNavCollapsed = false;
            return;
        }

        _manualPptNav = true;
        _pptNavCollapsed = false;
        UpdatePptNavigation(_pptSession.Snapshot ?? PptSnapshot.Detached);
    }

    private void OnPptInkTransition(PptInkTransition transition)
    {
        if (transition is null) return;

        // 统一课堂时间轴：翻页与批注变化都写入当前录制会话。
        if (transition.Kind is PptInkTransitionKind.Slide or PptInkTransitionKind.Begin)
        {
            int slideIndex = transition.Current?.Slide?.Index ?? 0;
            int slideId = transition.Current?.Slide?.SlideId ?? 0;
            if (slideIndex > 0)
            {
                string screenshot = ResolvePptScreenshotPath(transition, slideIndex, slideId);
                RecordClassroomEvent(TimelineEventKind.PptSlideChanged,
                    InkTimeline.EncodePptSlideChanged(slideId, slideIndex, screenshot));
            }
            RecordClassroomEvent(TimelineEventKind.PptInkChanged,
                (transition.Incoming?.Count ?? 0).ToString());
        }

        // A slide change invalidates any selection made on the previous slide.
        if (transition.Kind != PptInkTransitionKind.Slide) return;
        Services.OverlayInk.Selection.Clear();
        _bar?.Bar.RefreshSelection();
    }

    /// <summary>
    /// 为翻页事件解析可用的截图路径：
    /// 优先复用最近一次 <see cref="OnPptScreenshotRequested"/> 的截图；
    /// 录制中且没有可用截图时主动截取放映窗口并保存到会话目录。
    /// 任何失败都只记日志并返回空串，绝不阻塞或中断 PPT 翻页。
    /// </summary>
    private string ResolvePptScreenshotPath(PptInkTransition transition, int slideIndex, int slideId)
    {
        if (!string.IsNullOrWhiteSpace(_pptScreenshotPath)
            && ((_pptScreenshotSlideId > 0 && _pptScreenshotSlideId == slideId)
                || _pptScreenshotSlideIndex == slideIndex))
        {
            return _pptScreenshotPath;
        }

        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || recorder.Session is null) return "";

        try
        {
            string root = recorder.SessionDirectory ?? ClassroomSessionStore.DefaultDirectory;
            string directory = Path.Combine(root, recorder.Session.Id);
            string path = Path.Combine(directory, $"ppt-{slideIndex}.png");
            if (File.Exists(path))
            {
                RememberPptScreenshot(path, slideIndex, slideId);
                return path;
            }

            ScreenRect area = ResolveSlideShowBounds(transition);
            var (pixels, width, height) = ScreenshotService.Capture(area.X, area.Y, area.Width, area.Height);
            if (width <= 0 || height <= 0) return "";

            Directory.CreateDirectory(directory);
            _ = ScreenshotService.SavePngAsync(path, pixels, width, height);
            RememberPptScreenshot(path, slideIndex, slideId);
            return path;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"PPT timeline screenshot: {ex}", Core.Diagnostics.LogType.Error);
            return "";
        }
    }

    private void RememberPptScreenshot(string path, int slideIndex, int slideId)
    {
        _pptScreenshotPath = path;
        _pptScreenshotSlideIndex = slideIndex;
        _pptScreenshotSlideId = slideId;
    }

    private static ScreenRect ResolveSlideShowBounds(PptInkTransition transition)
    {
        nint hwnd = transition.Current?.WindowHandle ?? 0;
        if (hwnd != 0 && ShowWindowTracker.TryGet(hwnd, out ShowWindowInfo info)
            && info.IsFullScreen && info.Width > 0 && info.Height > 0)
        {
            return new ScreenRect(info.X, info.Y, info.Width, info.Height);
        }
        return ScreenService.Primary;
    }

    /// <summary>
    /// 向录制中的课堂时间轴追加一个非墨迹事件；未录制时静默忽略，异常只记录日志。
    /// </summary>
    private void RecordClassroomEvent(TimelineEventKind kind, string payload = null, int? pageIndex = null)
    {
        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || recorder.Session is null) return;

        try
        {
            recorder.Append(new TimelineEvent
            {
                TimeMs = recorder.ElapsedMs,
                PageIndex = pageIndex ?? Services.Board.CurrentIndex,
                Kind = kind,
                Payload = payload,
            });
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"Timeline/{kind}: {ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    public void PptExit() => _pptSession.ExitSlideShow();

    public void PptOpenNavigation()
    {
        if (!_lastSlideShowState) return;
        if (!Overlay.ClickThrough) ToggleClickThrough();
        _pptSession.ShowSlideNavigationPane();
    }

    // ---- AI 助教 ---------------------------------------------------------

    public void OpenAiAssistant()
    {
        var controller = ActiveController;
        IReadOnlyList<InkStroke> sourceStrokes = controller.Selection;
        var bounds = controller.SelectionBounds;

        if (sourceStrokes.Count == 0 || bounds.Width < 16 || bounds.Height < 16)
        {
            // Friendly fallback: the selection tool is tap-only today. When nothing is
            // selected we recognize every stroke on the canvas; when the canvas is empty
            // we recognize the whole overlay viewport (screenshot-only chat).
            sourceStrokes = controller.Document.Strokes;
            if (sourceStrokes.Count == 0)
            {
                sourceStrokes = Array.Empty<InkStroke>();
                bounds = new Rect(
                    0, 0,
                    Overlay.Window.Width / Overlay.Window.DpiScale,
                    Overlay.Window.Height / Overlay.Window.DpiScale);
            }
            else
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                foreach (InkStroke stroke in sourceStrokes)
                {
                    foreach (var point in stroke.Points)
                    {
                        minX = MathF.Min(minX, point.X);
                        minY = MathF.Min(minY, point.Y);
                        maxX = MathF.Max(maxX, point.X);
                        maxY = MathF.Max(maxY, point.Y);
                    }
                }
                bounds = new Rect(minX - 8, minY - 8, maxX - minX + 16, maxY - minY + 16);
                if (bounds.Width < 16 || bounds.Height < 16)
                {
                    ShowAssistantMessage("画布上还没有可识别的内容，请先书写或用「选择」工具点选。");
                    return;
                }
            }
        }

        Matrix3x2 matrix = controller.ViewTransform;
        Vector2 topLeft = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
        Vector2 bottomRight = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);

        var viewRect = new AssistantRect(
            Math.Min(topLeft.X, bottomRight.X),
            Math.Min(topLeft.Y, bottomRight.Y),
            Math.Max(topLeft.X, bottomRight.X),
            Math.Max(topLeft.Y, bottomRight.Y));
        _aiAnchor = viewRect;

        List<AssistantStroke> strokes = BuildAssistantStrokes(sourceStrokes, matrix);
        double scale = Math.Sqrt(Math.Abs(matrix.GetDeterminant()));
        double dpi = CurrentDpi();

        int px = (int)Math.Round(viewRect.Left * dpi);
        int py = (int)Math.Round(viewRect.Top * dpi);
        int pw = (int)Math.Round(viewRect.Width * dpi);
        int ph = (int)Math.Round(viewRect.Height * dpi);

        _ = StartAssistantAsync(strokes, scale, px, py, pw, ph);
    }

    private void ShowAssistantMessage(string message)
    {
        _aiThumb = "";
        _aiAnchor = CenterAnchor();
        _aiSession = AssistantMachine.CreateSession(AssistantMode.Auto, "");
        AssistantMachine.Fail(_aiSession, message);
        PrepareAssistantWindow().Apply(_aiSession, _aiThumb);
    }

    private async Task StartAssistantAsync(
        List<AssistantStroke> strokes, double scale, int px, int py, int pw, int ph)
    {
        _aiThumb = await AssistantCapture.CapturePngBase64Async(px, py, pw, ph);

        var request = new AssistantRequest
        {
            ImageBase64 = _aiThumb,
            InkStrokes = strokes,
            Viewport = new AssistantViewport { Scale = scale, Rect = _aiAnchor },
            Mode = AssistantMode.Auto,
            SessionId = Guid.NewGuid().ToString("N"),
        };

        PrepareAssistantWindow();
        StartAssistantStream(request, followUp: false, question: "");
    }

    private AssistantWindow PrepareAssistantWindow()
    {
        if (_aiAssistant is not null) _aiAssistant.Close();

        var window = new AssistantWindow(Services.Settings.Appearance.Theme);
        _aiAssistant = window;
        window.StopRequested += OnAiStopRequested;
        window.RetryRequested += OnAiRetryRequested;
        window.CopyRequested += OnAiCopyRequested;
        window.PasteRequested += OnAiPasteRequested;
        window.FollowUpRequested += OnAiFollowUpRequested;
        window.RegionSelected += OnAiRegionSelected;
        window.Closed += () => OnAiWindowClosed(window);
        window.SetAnchor(_aiAnchor);
        window.RegionSelectionEnabled = Services.Settings.Ai.EnableLocalCorrection;

        var work = ScreenService.WorkArea;
        double dpi = CurrentDpi();
        double workWidth = work.Width / dpi;
        double workHeight = work.Height / dpi;
        double cardWidth = AssistantWindow.CardWidth;
        double cardHeight = Math.Max(200, Math.Min(460, workHeight * AssistantWindow.MaxHeightRatio));

        AssistantRect anchor = _aiAnchor.IsEmpty ? CenterAnchor() : _aiAnchor;

        AssistantPlacement placement;
        if (anchor.Width > workWidth * 0.6 || anchor.Height > workHeight * 0.6)
        {
            // 整屏/大范围识别时锚点几乎铺满视口，跟随锚点会把浮窗顶到左上角；
            // 改为固定在右下角，并做边界 clamp。
            double x = workWidth - cardWidth - 16;
            double y = workHeight - cardHeight - 16;
            x = Math.Clamp(x, 12, Math.Max(12, workWidth - cardWidth - 12));
            y = Math.Clamp(y, 12, Math.Max(12, workHeight - cardHeight - 12));
            placement = new AssistantPlacement(x, y, false, false, 0);
        }
        else
        {
            placement = AssistantLayout.Place(
                anchor, cardWidth, cardHeight, workWidth, workHeight);
        }

        window.ShowAt(placement, dpi);
        return window;
    }

    private void StartAssistantStream(AssistantRequest request, bool followUp, string question)
    {
        _aiClient ??= new AiClient(AiConfig.ReadFromEnvironment());
        _aiStream?.Abort();
        _aiStream = null;

        long now = Environment.TickCount64;
        if (followUp)
        {
            _aiSession ??= AssistantMachine.CreateSession(AssistantMode.Chat, request.SessionId);
            AssistantMachine.BeginFollowUp(_aiSession, question, now);
            _aiSession.Mode = AssistantMode.Chat;
        }
        else
        {
            _aiSession = AssistantMachine.CreateSession(request.Mode, request.SessionId);
            AssistantMachine.BeginCapture(_aiSession, now);
            AssistantMachine.BeginRecognize(_aiSession);
        }

        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(true);

        _aiTwoStage = Services.Settings.Ai.EnableTwoStage;
        _aiStageFirstToken = null;
        _aiStageReset = false;
        ResetAiPreStage();

        if (_aiTwoStage)
        {
            try
            {
                _aiSession.LocalHint = LocalFormulaTranscriber.Hint(request.InkStrokes, _aiSession);
                _aiAssistant?.Apply(_aiSession, _aiThumb);
            }
            catch (Exception ex)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"AI 本地提示失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            }
        }

        try
        {
            if (_aiTwoStage)
            {
                AiStreamHandle handle = _aiClient.StartTwoStage(
                    request,
                    ev => _dispatcher.TryEnqueue(() => OnAiEvent(ev)),
                    err => _dispatcher.TryEnqueue(() => OnAiError(err)),
                    () => _dispatcher.TryEnqueue(OnAiDone));
                _aiStream = handle;
                handle.SecondStageStarted += () => _dispatcher.TryEnqueue(() =>
                {
                    if (ReferenceEquals(_aiStream, handle)) OnAiSecondStageStarted();
                });
            }
            else
            {
                _aiStream = _aiClient.Start(
                    request,
                    ev => _dispatcher.TryEnqueue(() => OnAiEvent(ev)),
                    err => _dispatcher.TryEnqueue(() => OnAiError(err)),
                    () => _dispatcher.TryEnqueue(OnAiDone));
            }
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 请求启动失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            OnAiError("AI 请求启动失败");
        }
    }

    private void OnAiEvent(AssistantEvent ev)
    {
        if (_aiSession is null) return;
        try
        {
            if (_aiTwoStage) ApplyTwoStageEvent(ev);
            else AssistantMachine.ApplyEvent(_aiSession, ev, Environment.TickCount64);

            if (Services.Settings.Ai.EnableVerification && ev.Kind == AssistantEventKind.Formula)
                VerifySessionFormula(_aiSession);
            _aiAssistant?.Apply(_aiSession, _aiThumb);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 事件处理失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>
    /// 两阶段去重：InkOnly 只作首屏预览，WithImage 通常从头重放最终结果。
    /// 检测到首 token 重放时清空草稿，避免两份内容叠加。
    /// </summary>
    private void ApplyTwoStageEvent(AssistantEvent ev)
    {
        if (ev is null || _aiSession is null) return;

        if (ev.Kind == AssistantEventKind.Token)
        {
            bool replay = !_aiStageReset
                && _aiStageFirstToken is not null
                && _aiSession.Text.Length > 0
                && string.Equals(ev.Text, _aiStageFirstToken, StringComparison.Ordinal);

            if (replay)
            {
                _aiSession.Text = "";
                _aiSession.Formula = "";
                _aiSession.Steps.Clear();
                _aiSession.Annotations.Clear();
                _aiSession.TokenCount = 0;
                _aiSession.FirstTokenAtMs = 0;
                _aiSession.LocalHint = "";
                _aiStageReset = true;
            }

            _aiStageFirstToken ??= ev.Text ?? "";
        }

        AssistantMachine.ApplyEvent(_aiSession, ev, Environment.TickCount64);
    }

    /// <summary>
    /// WithImage 阶段启动前，把 InkOnly 阶段产生的草稿暂存并清空会话；
    /// 两段是同一份正文的重放/续写，完成后按语义合并去重。
    /// </summary>
    private void OnAiSecondStageStarted()
    {
        if (_aiSession is null || !_aiTwoStage) return;

        _aiPreStageText = _aiSession.Text;
        _aiPreStageSteps = _aiSession.Steps
            .Select(step => CloneStep(step))
            .ToList();
        _aiPreStageFormula = _aiSession.Formula;
        _aiSecondStageStarted = true;

        _aiSession.Text = "";
        _aiSession.Steps.Clear();
        _aiSession.Formula = "";
        _aiSession.TokenCount = 0;
        _aiSession.State = AssistantState.Streaming;
        _aiAssistant?.Apply(_aiSession, _aiThumb, preserveScroll: true);
    }

    /// <summary>合并两阶段结果：新文本已包含旧文本时取较长的，否则拼接；步骤按内容去重。</summary>
    private void MergeAiPreStage()
    {
        if (!_aiSecondStageStarted || _aiSession is null) return;

        string newText = _aiSession.Text ?? "";
        string preText = _aiPreStageText ?? "";
        if (string.IsNullOrEmpty(newText))
        {
            _aiSession.Text = preText;
        }
        else if (!string.IsNullOrEmpty(preText))
        {
            if (newText.Contains(preText, StringComparison.Ordinal)
                || preText.Contains(newText, StringComparison.Ordinal))
            {
                _aiSession.Text = newText.Length >= preText.Length ? newText : preText;
            }
            else
            {
                _aiSession.Text = preText + newText;
            }
        }

        if (_aiPreStageSteps.Count > 0)
        {
            List<AssistantStep> current = _aiSession.Steps;
            if (current.Count == 0)
            {
                ReplaceSteps(current, _aiPreStageSteps);
            }
            else if (current.Count != _aiPreStageSteps.Count || !StepsEquivalent(current, _aiPreStageSteps))
            {
                var merged = new List<AssistantStep>(current.Count + _aiPreStageSteps.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (AssistantStep step in _aiPreStageSteps)
                {
                    if (seen.Add(StepKey(step))) merged.Add(CloneStep(step));
                }

                foreach (AssistantStep step in current)
                {
                    if (seen.Add(StepKey(step))) merged.Add(CloneStep(step));
                }

                ReplaceSteps(current, merged);
            }
        }

        if (string.IsNullOrEmpty(_aiSession.Formula))
            _aiSession.Formula = _aiPreStageFormula;

        ResetAiPreStage();
    }

    private void ResetAiPreStage()
    {
        _aiPreStageText = "";
        _aiPreStageSteps.Clear();
        _aiPreStageFormula = "";
        _aiSecondStageStarted = false;
    }

    private static AssistantStep CloneStep(AssistantStep step) => new()
    {
        Index = step.Index,
        Text = step.Text ?? "",
        Latex = step.Latex ?? "",
    };

    private static string StepKey(AssistantStep step) =>
        step.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "\u0001" + (step.Text ?? "")
        + "\u0001" + (step.Latex ?? "");

    private static bool StepsEquivalent(List<AssistantStep> left, List<AssistantStep> right)
    {
        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Text ?? "", right[i].Text ?? "", StringComparison.Ordinal)
                || !string.Equals(left[i].Latex ?? "", right[i].Latex ?? "", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ReplaceSteps(List<AssistantStep> target, IReadOnlyList<AssistantStep> source)
    {
        target.Clear();
        foreach (AssistantStep step in source) target.Add(CloneStep(step));
    }

    private static void VerifySessionFormula(AssistantSession session)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.Formula)) return;

        try
        {
            VerificationResult result = EquationVerifier.Verify(session.Formula, session.Confidence);
            session.Verified = result.Status == VerificationStatus.Verified;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 自检失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private void OnAiError(string message)
    {
        if (_aiSession is null) { _aiAssistant?.SetBusy(false); return; }
        ResetAiPreStage();
        AssistantMachine.Fail(_aiSession, message);
        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(false);
        _aiStream = null;
    }

    private void OnAiDone()
    {
        if (_aiSession is null) { _aiAssistant?.SetBusy(false); return; }
        try
        {
            MergeAiPreStage();
            AssistantMachine.Finish(_aiSession);
            if (Services.Settings.Ai.EnableVerification) VerifySessionFormula(_aiSession);
            _aiSession.LocalHint = "";
            _aiAssistant?.Apply(_aiSession, _aiThumb);
            _aiAssistant?.SetPasted(false);
            _aiAssistant?.SetBusy(false);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 完成处理失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            _aiAssistant?.SetBusy(false);
        }

        _aiStream = null;
    }

    private void OnAiStopRequested()
    {
        _aiStream?.Abort();
        _aiStream = null;
        ResetAiPreStage();
        if (_aiSession is null) return;
        AssistantMachine.Finish(_aiSession);
        _aiAssistant?.Apply(_aiSession, _aiThumb);
        _aiAssistant?.SetBusy(false);
    }

    private void OnAiRetryRequested() => OpenAiAssistant();

    private void OnAiCopyRequested()
    {
        if (_aiSession is null) return;
        string text = string.IsNullOrEmpty(_aiSession.Formula) ? _aiSession.Text : _aiSession.Formula;
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"AI 复制失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private void OnAiPasteRequested() => _ = PasteAssistantAsync();

    private async Task PasteAssistantAsync()
    {
        if (_aiSession is null || _aiAssistant is null) return;
        if (_aiSession.State != AssistantState.Done) return;

        var window = _aiAssistant;
        AssistantSession session = _aiSession;
        List<string> lines = BuildPasteLines(session);
        var (width, height) = PasteCardLayout.Estimate(lines);

        window.SetBusy(true);
        byte[] png = await PasteCardRenderer.RenderPngAsync("AI 助教", lines, width, height);
        if (!ReferenceEquals(_aiAssistant, window)) return;

        if (png is null || png.Length == 0)
        {
            window.SetBusy(false);
            AssistantMachine.Fail(session, "贴回失败");
            window.Apply(session, _aiThumb);
            return;
        }

        var work = ScreenService.WorkArea;
        double dpi = CurrentDpi();
        var (left, top, w, h) = PasteCardLayout.Place(
            _aiAnchor, width, height, work.Width / dpi, work.Height / dpi);

        string metadata = "";
        if (Services.Settings.Ai.EnableEditablePaste)
        {
            try
            {
                metadata = BuildPasteMetadata(session).Serialize();
            }
            catch (Exception ex)
            {
                Core.Diagnostics.FileLog.WriteLogToFile(
                    $"AI 贴回元数据生成失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            }
        }

        Services.Board.AddImage(new BoardImageAsset
        {
            Name = "AI 助教",
            Data = png,
            ContentType = "image/png",
            Left = (float)left,
            Top = (float)top,
            Width = (float)w,
            Height = (float)h,
            Metadata = metadata,
        });

        if (_board is null) ToggleBoard();

        window.SetBusy(false);
        window.SetPasted(true);
    }

    private void OnAiFollowUpRequested(string question)
    {
        if (_aiSession is null) return;

        var request = new AssistantRequest
        {
            ImageBase64 = _aiThumb,
            InkStrokes = Array.Empty<AssistantStroke>(),
            Viewport = new AssistantViewport { Scale = 1.0, Rect = _aiAnchor },
            Mode = AssistantMode.Chat,
            SessionId = _aiSession.SessionId,
        };
        StartAssistantStream(request, followUp: true, question: question);
    }

    private void OnAiWindowClosed(AssistantWindow window)
    {
        if (!ReferenceEquals(_aiAssistant, window)) return;
        _aiStream?.Abort();
        _aiStream = null;
        _aiRegionStream?.Abort();
        _aiRegionStream = null;
        ResetAiPreStage();
        _aiAssistant = null;
    }

    private void OnAiRegionSelected(AssistantRect rect)
    {
        AssistantSession receivedSession = _aiSession;
        AssistantWindow receivedWindow = _aiAssistant;
        Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 局部纠错入口：recv rect=({rect.Left:0.0},{rect.Top:0.0})-({rect.Right:0.0},{rect.Bottom:0.0})，"
            + $"EnableLocalCorrection={Services.Settings.Ai.EnableLocalCorrection}，"
            + $"会话={(receivedSession is null ? "null" : receivedSession.State.ToString())}，"
            + $"窗口={(receivedWindow is null ? "null" : "ok")}",
            Core.Diagnostics.LogType.Trace);

        if (!Services.Settings.Ai.EnableLocalCorrection) return;
        if (rect.IsEmpty) return;

        AssistantSession session = _aiSession;
        AssistantWindow window = _aiAssistant;
        if (session is null || window is null) return;
        if (session.State != AssistantState.Done)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 局部纠错入口：会话状态 {session.State} 非 Done，提示后返回",
                Core.Diagnostics.LogType.Trace);
            window.ShowNotice("生成完成后才能局部纠错");
            return;
        }

        bool formulaTarget = !string.IsNullOrWhiteSpace(session.Formula);
        string target = formulaTarget ? session.Formula ?? "" : session.Text ?? "";
        AssistantRect anchor = _aiAnchor;
        var (start, length) = RegionTextMapper.Map(anchor, rect, target);
        AssistantRegion region = new()
        {
            Rect = rect,
            TextStart = start,
            TextLength = length,
            Source = "region",
        };

        _aiRegionStream?.Abort();
        _aiRegionStream = null;

        try
        {
            _aiClient ??= new AiClient(AiConfig.ReadFromEnvironment());

            var request = new AssistantRequest
            {
                ImageBase64 = _aiThumb,
                InkStrokes = Array.Empty<AssistantStroke>(),
                Viewport = new AssistantViewport { Scale = 1.0, Rect = _aiAnchor },
                Mode = session.Mode,
                SessionId = session.SessionId ?? "",
                Stage = AssistantStage.WithImage,
                Region = region,
            };

            var buffer = new StringBuilder();
            string formula = null;
            bool failure = false;

            window.ShowNotice("局部纠错中…");

            _aiRegionStream = _aiClient.Start(
                request,
                ev => _dispatcher.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(_aiAssistant, window)) return;
                    if (ev.Kind == AssistantEventKind.Token)
                    {
                        buffer.Append(ev.Text ?? "");
                    }
                    else if (ev.Kind == AssistantEventKind.Formula && !string.IsNullOrEmpty(ev.Latex))
                    {
                        formula = ev.Latex;
                    }
                }),
                err => _dispatcher.TryEnqueue(() =>
                {
                    failure = true;
                    Core.Diagnostics.FileLog.WriteLogToFile(
                        $"AI 局部纠错失败：{err}", Core.Diagnostics.LogType.Error);
                    if (!ReferenceEquals(_aiAssistant, window)) return;
                    window.ShowNotice("局部纠错失败，已保留原结果");
                }),
                () => _dispatcher.TryEnqueue(() =>
                {
                    _aiRegionStream = null;
                    if (failure || !ReferenceEquals(_aiAssistant, window)) return;
                    if (!ReferenceEquals(_aiSession, session)) return;

                    string replacement = string.IsNullOrWhiteSpace(formula) ? buffer.ToString() : formula;
                    if (string.IsNullOrWhiteSpace(replacement))
                    {
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            "AI 局部纠错结果：返回内容为空，保留原结果",
                            Core.Diagnostics.LogType.Trace);
                        window.ShowNotice("局部纠错未返回内容");
                        return;
                    }

                    try
                    {
                        string trimmed = replacement.Trim();
                        LocalCorrectionResult result = LocalCorrectionFlow.Apply(
                            session, anchor, rect, trimmed);
                        AssistantSession updated = result.Session;

                        if (updated is null)
                        {
                            Core.Diagnostics.FileLog.WriteLogToFile(
                                $"AI 局部纠错结果应用失败：流程返回 null（Field={result.Field}，替换长度 {trimmed.Length}）",
                                Core.Diagnostics.LogType.Error);
                            window.ShowNotice("局部纠错结果应用失败");
                            return;
                        }

                        int previousLength = result.Field == nameof(AssistantSession.Formula)
                            ? (session.Formula ?? "").Length
                            : (session.Text ?? "").Length;
                        int updatedLength = result.Field == nameof(AssistantSession.Formula)
                            ? (updated.Formula ?? "").Length
                            : (updated.Text ?? "").Length;

                        updated.Regions.Add(region);
                        _aiSession = updated;
                        window.Apply(updated, _aiThumb, preserveScroll: true);

                        bool appended = result.Field == nameof(AssistantSession.Text)
                            && string.IsNullOrEmpty(session.Text)
                            && !string.IsNullOrEmpty(updated.Text);
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            $"AI 局部纠错结果应用成功：Field={result.Field}，"
                            + $"start={result.Start}，length={result.Length}，长度 {previousLength}->{updatedLength}，"
                            + $"替换长度 {trimmed.Length}，追加={appended}，"
                            + $"region=({region.Rect.Left:0.0},{region.Rect.Top:0.0})-({region.Rect.Right:0.0},{region.Rect.Bottom:0.0})",
                            Core.Diagnostics.LogType.Event);
                        window.ShowNotice(appended ? "已将局部结果追加到正文" : "已应用局部纠错");
                    }
                    catch (Exception ex)
                    {
                        Core.Diagnostics.FileLog.WriteLogToFile(
                            $"AI 局部纠错结果应用失败：{ex.Message}", Core.Diagnostics.LogType.Error);
                        window.ShowNotice("局部纠错结果应用失败");
                    }
                }));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 局部纠错启动失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            window.ShowNotice("局部纠错失败，已保留原结果");
        }
    }


    private double CurrentDpi()
    {
        double dpi = Overlay.Window.DpiScale;
        return dpi > 0 ? dpi : 1.0;
    }

    private AssistantRect CenterAnchor()
    {
        var work = ScreenService.WorkArea;
        double dpi = CurrentDpi();
        double centerX = work.Width / dpi / 2.0;
        double centerY = work.Height / dpi / 2.0;
        return new AssistantRect(centerX - 8, centerY - 8, centerX + 8, centerY + 8);
    }

    private static List<AssistantStroke> BuildAssistantStrokes(IReadOnlyList<InkStroke> selection, Matrix3x2 matrix)
    {
        var strokes = new List<AssistantStroke>(selection.Count);
        foreach (InkStroke stroke in selection)
        {
            var points = new List<AssistantPoint>(stroke.Points.Count);
            foreach (InkPoint point in stroke.Points)
            {
                Vector2 view = Vector2.Transform(new Vector2(point.X, point.Y), matrix);
                points.Add(new AssistantPoint
                {
                    X = view.X,
                    Y = view.Y,
                    Pressure = point.Pressure,
                    TimeMs = point.TimeMs,
                });
            }

            strokes.Add(new AssistantStroke
            {
                BaseWidth = stroke.Attributes.BaseWidth,
                ColorArgb = stroke.Attributes.EffectiveColor.PackedArgb,
                Points = points,
            });
        }

        return strokes;
    }

    private static List<string> BuildPasteLines(AssistantSession session)
    {
        var lines = new List<string>();
        string text = (session.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0) lines.Add(line);
        }

        int number = 1;
        foreach (AssistantStep step in session.Steps)
        {
            string detail = step.Text ?? "";
            string latex = AssistantText.LatexToPlain(step.Latex ?? "");
            if (!string.IsNullOrEmpty(latex))
                detail = string.IsNullOrEmpty(detail) ? latex : detail + " / " + latex;
            if (string.IsNullOrEmpty(detail)) continue;

            int index = step.Index > 0 ? step.Index : number;
            lines.Add(index + ". " + detail);
            number = index + 1;
        }

        if (!string.IsNullOrEmpty(session.Formula))
            lines.Add(AssistantText.LatexToPlain(session.Formula));

        if (lines.Count == 0) lines.Add("AI 助教暂无返回内容");
        return lines;
    }

    /// <summary>贴回卡片的可编辑源数据：公式优先，其次步骤，再看图形批注。</summary>
    private static AiPasteMetadata BuildPasteMetadata(AssistantSession session)
    {
        var metadata = new AiPasteMetadata
        {
            Kind = DerivePasteKind(session),
            Latex = session.Formula ?? "",
            Text = session.Text ?? "",
            Lines = BuildPasteLines(session),
        };

        foreach (var annotation in session.Annotations ?? new List<IReadOnlyList<IReadOnlyList<double>>>())
        {
            var points = new List<double>();
            foreach (var element in annotation ?? Array.Empty<IReadOnlyList<double>>())
            {
                if (element is null) continue;
                foreach (double value in element) points.Add(value);
            }

            if (points.Count > 0) metadata.Annotations.Add(points);
        }

        return metadata;
    }

    private static AiPasteKind DerivePasteKind(AssistantSession session)
    {
        if (!string.IsNullOrEmpty(session.Formula)) return AiPasteKind.Formula;
        if (session.Steps is { Count: > 0 }) return AiPasteKind.Steps;
        if (session.Annotations is { Count: > 0 }) return AiPasteKind.Shape;
        return AiPasteKind.Steps;
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
        _recorder?.Dispose();
        _timeMachine?.Close();
        ResetAiPreStage();
        _aiStream?.Abort();
        _aiRegionStream?.Abort();
        _aiAssistant?.Close();
        _countdown?.Dispose();
        _spotlight?.Close();
        _magnifier?.Close();
        _roster?.Close();
        _toolkit?.Close();
        CloseBlackScreen();
        _pptSession.InkTransition -= OnPptInkTransition;
        ClosePptNavigationWindows();
        _pptSession.Dispose();
        Services.Ppt.Dispose();
        Updater.Dispose();
        Overlay.Dispose();
        Services.Dispose();
    }
}