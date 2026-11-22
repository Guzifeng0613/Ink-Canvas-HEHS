using InkCanvas.App.Views;
using InkCanvas.Controls.Controls;
using InkCanvas.Features.Ppt;
using InkCanvas.Features.Persistence;
using InkCanvas.Features.Tools;
using InkCanvas.Features.Board;
using InkCanvas.Ink;
using InkCanvas.Platform.System;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using InkCanvas.Host.Contracts;
using InkCanvas.Extensibility;

using InkCanvas.Core.AiAssistant;
using InkCanvas.Core.Classroom;
using InkCanvas.Core.Ink;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Features.Classroom;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

using ClassroomRoster = InkCanvas.Features.Classroom.ClassroomRoster;

namespace InkCanvas.App.Services;

/// <summary>
/// Owns the windows and implements the shell actions the floating bar invokes. Keeps App.xaml.cs
/// down to startup wiring.
/// </summary>
public sealed partial class ShellController : IShellActions, IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _pollTimer;
    private readonly PptInkFileStore _pptStore;
    private readonly PptInkSession _pptSession;
    private RosterWindow _roster;
    private SpotlightWindow _spotlight;
    private MagnifierWindow _magnifier;
    private ToolkitWindow _toolkit;
    private TimeMachineWindow _timeMachine;
    private InkRecorder _recorder;
    private readonly NavigationGuard _pptGuard = new();
    private readonly SlideSwitchGate _pptGate = new();
    private readonly Dictionary<PptNavigationPlacement, PptNavigationWindow> _pptNavigations = new();
    private SlideNavigatorWindow _slideNav;
    private SlideContentIndex _slideIndex;
    private DateTime _lastSlideIndexRefreshUtc = DateTime.MinValue;
    private bool _pptNavCollapsed;
    private bool _manualPptNav;
    private PptSnapshot _pptShowSnapshot;
    private int _pptCaptureSession;
    private int _pptCaptureGeneration;
    private string _pptScreenshotPath;
    private int _pptScreenshotSlideIndex;
    private int _pptScreenshotSlideId;
    private readonly PresentationSourceSelector _pptSources = new();
    private readonly ComPresentationSource _pptComSource;
    private readonly WindowPresentationSource _pptWindowSource = new();
    private readonly KeyPresentationSource _pptKeySource = new();
    private BlackScreenWindow _blackScreen;
    private DateTime _lastPptRpcBusyLogUtc = DateTime.MinValue;
    private TeachingSession _teachingSession;
    private PracticePlan _activePractice;
    private GroupScoreService _groupScore;
    private GroupScoreWindow _groupScoreWindow;
    private ExampleLibraryWindow _exampleLibrary;
    private ResourceBundleWindow _resourceBundle;
    private ClassroomPreset _preset;
    private FocusRegionStore _focusRegions;
    private PresetWindow _presetWindow;

    private FloatingBarWindow _bar;
    private SelectionToolbarWindow _selectionToolbar;
    private bool _selectionToolbarCollapsed;
    private BoardWindow _board;
    private SettingsWindow _settings;
    private bool _settingsOpening;
    private IntPtr _settingsDrawingSurface;
    private bool _settingsSurfaceWasTopmost;
    private LotteryWindow _settingsRosterSource;
    private string _requestedSettingsCategory;
    internal string RosterDirectory { get; set; } = LotteryWindow.DataDir;
    private bool _imagePicking;
    private CancellationTokenSource _imageImportCancellation;
    private CountdownWindow _countdown;
    private LotteryWindow _lottery;
    private ReplayWindow _replay;
    private InfoWindow _guide;
    private readonly List<NoticeWindow> _notices = new();
    private PenPaletteWindow _palette;
    private readonly List<PenPaletteWindow> _detachedPalettes = new();
    private ShapePickerWindow _shapePicker;
    private bool _lastSlideShowState;
    private bool _lastAutoFoldRequested;
    private bool? _manualFoldOverride;
    private bool _exiting;
    private bool _exitPromptOpen;
    private bool _paletteHighlighter;
    private bool _disposed;
    private InkController _boundBoardController;
    private IReadOnlyList<ScreenInfo> _screens = Array.Empty<ScreenInfo>();
    private bool _displayRefreshPending;
    private long _lastDisplayCheck;

    public ShellController(AppServices services)
    {
        Services = services;
        services.SettingsApplied += ApplyDocumentSettings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Overlay = new OverlayHost(services.OverlayInk);
        Overlay.InteractionChanged += RefreshShortcutRegistration;
        Overlay.ConfigureDisplays(services.Settings.Display);
        services.SettingsApplied += ApplyDisplaySettings;
        services.SettingsApplied += RefreshPaletteNibModes;
        Overlay.Window.DisplaySettingsChanged += QueueDisplayRefresh;
        _screens = ScreenService.All();
        _documentAi = new DocumentAiHost(this);
        services.StartModules(new ScreenTrackingHost(services, Overlay, () => _board is not null || DocumentActive), new DocumentHost(this), new CanvasToolsHost(this), new AssistantHost(this), new LibraryHost(this), _documentAi);
        _pptStore = new PptInkFileStore(services.Settings.Automation.AutoSavedStrokesLocation);
        services.SettingsApplied += ApplyArchivePaths;
        _pptSession = new PptInkSession(services.Ppt, _pptStore)
        {
            InkManagedExternally = () => Services.Modules.HasExport<IScreenTrackingService>() && Services.Settings.Experimental.ScreenContentTracking && Services.Settings.Experimental.PowerPoint,
            CaptureCurrentInk = () => Services.OverlayInk.Document.Strokes,
            ApplyCurrentInk = strokes =>
            {
                var controller = Services.OverlayInk;
                Overlay.Presenter.BeginSceneUpdate();
                try
                {
                    Overlay.Window.CancelInput();
                    controller.EndPlayback();
                    controller.FinishDotAnimations();
                    controller.ClearSelection();
                    controller.Document.ReplaceAll(strokes);
                    controller.History.Clear();
                    _bar?.Bar.RefreshSelection();
                }
                finally { Overlay.Presenter.EndSceneUpdate(); }
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
            () => _pptSession.Snapshot.IsAttached, () => _pptSession.Next(), () => _pptSession.Previous());
        _teachingSession = TeachingSession.Create("课堂", Environment.TickCount64);
        _preset = ClassroomPresetLibrary.ForId(Services.Settings.Classroom.PresetId)
            ?? ClassroomPresetLibrary.ForScene(ClassroomSceneKind.Teach);
        _focusRegions = FocusRegionStore.Deserialize(Services.Settings.Classroom.SpotlightRegions);
        Updater = new UpdateCoordinator(services)
        {
            Confirm = ConfirmAsync,
            ConfirmRelease = ConfirmUpdateReleaseAsync,
            Notify = ShowUpdateNoticeAsync,
            ExitForInstall = () => { _installingUpdate = true; Exit(); },
            CanInstallSilently = CanInstallUpdateSilently,
            PrepareForInstall = PrepareForUpdateAsync,
            ChooseRelease = ChooseUpdateReleaseAsync,
        };

        _pollTimer = _dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(900);
        _pollTimer.Tick += (_, _) => Poll();
        services.Ppt.RefreshRequested += QueuePresentationRefresh;
    }

    /// <summary>Raised whenever a chrome window is created (for the --shot self-check hook).</summary>
    public event Action<Window> WindowOpened;

    private void NotifyWindowOpened(Window w)
    {
        w.Activated += (_, _) => RefreshShortcutRegistration();
        if (w.Content is FrameworkElement root)
            root.GotFocus += (_, _) => RefreshShortcutRegistration();
        if (w is SettingsWindow or CountdownWindow or LotteryWindow or InfoWindow)
        {
            var owner = _documentWorkspace?.Window ?? (Window)_board ?? _bar;
            if (owner is not null) WindowChrome.CenterOnOwner(w, owner);
        }
        WindowOpened?.Invoke(w);
    }

    private void QueueDisplayRefresh()
    {
        if (_displayRefreshPending || _disposed) return;
        _displayRefreshPending = true;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _displayRefreshPending = false;
            if (!_disposed) RefreshDisplays();
        });
    }

    private void RefreshDisplays() => RefreshDisplays(ScreenService.All());

    internal void RefreshDisplays(IReadOnlyList<ScreenInfo> screens)
    {
        _lastDisplayCheck = Environment.TickCount64;
        if (screens.Count == 0) return;
        if (_screens.SequenceEqual(screens)) return;
        try
        {
            Overlay.RefreshDisplayBounds(screens);
            _bar?.RefreshDisplayPlacement();
            _palette?.RefreshDisplayPlacement();
            foreach (var palette in _detachedPalettes) palette.RefreshDisplayPlacement();
            _shapePicker?.RefreshDisplayPlacement();
            if (_board is not null) foreach (var panel in _board.ToolPanels) panel.Fit();
            _replay?.RefreshDisplayPlacement();
            foreach (var window in OpenWindows)
                if (window is SettingsWindow or CountdownWindow or LotteryWindow or InfoWindow or ReplayWindow or NoticeWindow or UpdateWindow
                    || _documentWindows.Contains(window))
                    WindowChrome.KeepVisible(window);
            QueueDesktopSelection();
            Services.Modules.Invoke<IScreenTrackingService>(tracking => tracking.Refresh());
            _screens = screens.ToArray();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"Display layout: {ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>All live chrome windows (for the --shot headless visual self-check).</summary>
    public IEnumerable<Window> OpenWindows
    {
        get
        {
            if (_bar is not null) yield return _bar;
            foreach (var navigation in _pptNavigations.Values) yield return navigation;
            if (_selectionToolbar is not null) yield return _selectionToolbar;
            if (_board is not null) yield return _board;
            if (_documentWorkspace is not null) yield return _documentWorkspace.Window;
            foreach (var window in _documentWindows) if (!ReferenceEquals(window, _documentWorkspace?.Window)) yield return window;
            if (_settings is not null) yield return _settings;
            if (_countdown is not null) yield return _countdown;
            if (_lottery is not null) yield return _lottery;
            if (_replay is not null) yield return _replay;
            if (_guide is not null) yield return _guide;
            if (_updateWindow is not null) yield return _updateWindow;
            foreach (var notice in _notices) yield return notice;
            if (_palette is not null) yield return _palette;
            foreach (var palette in _detachedPalettes) yield return palette;
            if (_shapePicker is not null) yield return _shapePicker;
        }
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
            var client = _bar.AppWindow.ClientSize;
            var desired = _bar.Bar.DesiredSize;
            return $"window={size.Width}x{size.Height}; client={client.Width}x{client.Height}; "
                 + $"dpi={WindowChrome.GetDpiScale(_bar):0.##}; desired={desired.Width:0.#}x{desired.Height:0.#}; "
                 + $"backdrop={(_bar.SystemBackdrop as ChromeBackdrop)?.State}; "
                 + $"primaryChildren={_bar.Bar.PrimaryChildCount}; computedRow={_bar.Bar.ComputedPrimaryWidth:0.#}";
        }
    }

    public void Start()
    {
        _bar = new FloatingBarWindow();
        NotifyWindowOpened(_bar);
        _bar.Bar.Settings = Services.Settings;
        _bar.Configure(Services.OverlayInk, this);
        _displayedExtensionCommands = ExtensionCommands.ToArray();
        Services.Plugins.Changed += OnExtensionCommandsChanged;
        Services.Modules.Changed += OnExtensionCommandsChanged;
        _ = StartPluginsAsync();
        _bar.Bar.LayoutChanged += OnBarLayoutChanged;
        _bar.Bar.SettingsEdited += () => { Services.ApplySettings(); Services.SaveSettings(); };
        _bar.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                        Services.Settings.Appearance.FloatingBarScale);
        WindowChrome.ApplyTheme(_bar, Services.Settings.Appearance.Theme);
        WindowChrome.ShowWithoutActivation(_bar);

        Services.OverlayInk.Invalidated += QueueDesktopSelection;
        Services.OverlayInk.Invalidated += QueueDesktopMedia;
        Services.ScreenImages.ImagesChanged += QueueDesktopMedia;
        Services.ScreenImages.PageChanged += QueueDesktopMedia;
        Services.ScreenImages.MediaPlacementChanged += OnDesktopMediaPlacement;
        Services.OverlayInk.SelectionChanged += QueueDesktopSelection;
        _bar.AppWindow.Changed += OnBarWindowChanged;

        Overlay.Show();
        // A click on the overlay raises it to the top of the topmost Z-band, above every chrome
        // window. Re-assert the chrome immediately so close buttons and title bars stay clickable.
        Overlay.Window.InputBegan += () =>
        {
            RaiseChromeAboveOverlay();
            _bar?.DismissPanelsOnInput();
            // WPF HideSubPanels: laying ink dismisses the pen/shape popups (pin only survives picks).
            _palette?.DismissOnOverlayInput();
            _shapePicker?.DismissOnOverlayInput();
        };
        if (Services.Settings.Startup.IsFoldAtStartup) _bar.SetFolded(true);
        Overlay.RegisterHotkeys(
            undo: () => ActiveController.Undo(),
            redo: () => ActiveController.Redo(),
            toggleEraser: () => { ActiveController.Tool = InkTool.Eraser; _bar.Bar.RefreshSelection(); },
            clear: () =>
            {
                if (ActiveController.HasSelection) ActiveController.DeleteSelection();
                else ClearCanvas();
            },
            togglePassThrough: ToggleClickThrough,
            toggleBoard: ToggleBoard,
            pptNext: () => HandlePresentationKey(PresentationKeyMap.VkPageDown),
            pptPrevious: () => HandlePresentationKey(PresentationKeyMap.VkPageUp),
            pptExit: () => { if (_lastSlideShowState) PptExit(); },
            screenshot: () => _ = ScreenshotAsync(),
            fold: ToggleFold,
            select: () => { if (_board is not null) _board.SelectOrSelectAll(); else ActiveController.Tool = InkTool.Select; _bar.Bar.RefreshSelection(); },
            line: () =>
            {
                ActiveController.SelectShape(Core.Shapes.ShapeKind.Line);
                _bar?.Bar.RefreshSelection();
            },
            draw: () =>
            {
                bool active = ActiveController.Tool == InkTool.Pen && (DocumentActive || _board is not null || !Overlay.ClickThrough);
                if (!DocumentActive && _board is null && Overlay.ClickThrough) ToggleClickThrough();
                ActiveController.Tool = InkTool.Pen;
                if (active) OpenPenPalette(false);
            },
            editingEnabled: () => CanvasShortcutsEnabled,
            overlayEditingEnabled: () => OverlayShortcutsEnabled,
            presentationEnabled: () => PresentationShortcutsEnabled);

        Overlay.Hotkeys.Register(HotkeyModifiers.None, 0x78 /* F9 */, ToggleBlackScreen);
        Overlay.Hotkeys.Register(HotkeyModifiers.None, 0x42 /* B */, ToggleBlackScreen, () => PresentationShortcutsEnabled);
        Overlay.Hotkeys.Register(HotkeyModifiers.None, 0x1B /* Escape */, CloseBlackScreen, () => _blackScreen is not null);
        _ = NotifyPendingJournalAsync();
        StartShortcutTracking();
        _pollTimer.Start();

        // Delete-selection changes the button count; FloatingBar raises LayoutChanged for this.
        Services.OverlayInk.SlideFlipRequested += next => { if (next) PptNext(); else PptPrevious(); };
        Services.OverlayInk.ToolChanged += OnControllerToolChanged;

        Core.Diagnostics.FileLog.NewLog($"Ink Canvas 启动，版本 {Updater.CurrentVersion}");

        ApplyEdgeGestureSetting();

        if (Services.Settings.Startup.IsAutoUpdate)
            _ = Updater.CheckAsync(interactive: false);
    }

    private async Task NotifyPendingJournalAsync()
    {
        try
        {
            await NotifyAsync("板书时光机", "检测到未完成的课堂录制，已恢复为最近会话。");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"板书时光机恢复提示失败：{ex.Message}", Core.Diagnostics.LogType.Info);
        }
    }

    /// <summary>
    /// The default hotkey set. Extracted so it can be replayed after the Win32 hotkey service
    /// drops a temporary key (the service only supports unregistering everything).
    /// </summary>
    private void OnBarLayoutChanged()
    {
        _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                         Services.Settings.Appearance.FloatingBarScale, recenter: false);
        QueueDesktopSelection();
    }

    private void OnBarWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange || args.DidSizeChange) QueueDesktopSelection();
    }

    private void QueueDesktopSelection()
    {
        if (_disposed || _exiting) return;
        var ink = Services.OverlayInk;
        if (_selectionToolbar is null && DesktopSelectionAvailable && ink.HasSelection && ink.Playback is null
            && !ink.IsMarqueeSelecting && !ink.IsRotatingSelection)
        {
            var toolbar = new SelectionToolbarWindow(this);
            _selectionToolbar = toolbar;
            if (_selectionToolbarCollapsed) toolbar.Toolbar.ToggleCollapsed();
            toolbar.Closed += (_, _) =>
            {
                _selectionToolbarCollapsed = toolbar.Toolbar.IsCollapsed;
                if (ReferenceEquals(_selectionToolbar, toolbar)) _selectionToolbar = null;
            };
            NotifyWindowOpened(toolbar);
        }
        if (ink.HasSelection || _selectionToolbar?.IsShown == true) _selectionToolbar?.QueueUpdate();
    }

    internal bool DesktopSelectionAvailable => _board is null && !DocumentActive && !_exiting && !Overlay.ClickThrough
        && Services.OverlayInk.Tool == InkTool.Select;

    internal Core.Ink.Rect DesktopToolbarBounds
    {
        get
        {
            if (_bar is null || _bar.IsFolded) return Core.Ink.Rect.Empty;
            var pos = _bar.AppWindow.Position;
            var size = _bar.AppWindow.Size;
            return new Core.Ink.Rect(pos.X, pos.Y, pos.X + size.Width, pos.Y + size.Height);
        }
    }

    private InkController ActiveController => _board is not null
        ? Services.Board.Current
        : DocumentActive ? _documentWorkspace.Controller : Services.OverlayInk;

    /// <summary>
    /// WPF GetMainWindowTheme: in board mode the aux windows follow the black/white board
    /// choice (light iff whiteboard), otherwise the application theme.
    /// </summary>
    /// <summary>Auto-revert to pen changes Tool outside bar clicks; keep the bar selection in sync.</summary>
    private void OnControllerToolChanged()
    {
        RefreshShortcutRegistration();
        if (_bar is not null) _bar.Bar.ClickThroughActive = DocumentActive ? ActiveController.Tool == InkTool.Pan : Overlay.ClickThrough;
        _bar?.Bar.RefreshSelection();
        QueueDesktopSelection();
    }

    private void BindBoardController(InkController controller)
    {
        if (!ReferenceEquals(controller, _boundBoardController)) CloseDetachedPalettes();
        if (_boundBoardController is not null)
        {
            _boundBoardController.ToolChanged -= OnControllerToolChanged;
        }
        _boundBoardController = controller;
        if (controller is not null)
        {
            controller.ToolChanged += OnControllerToolChanged;
        }
    }

    private int EffectiveTheme => _board is not null
        ? (Services.Settings.Canvas.UsingWhiteboard ? 1 : 2)
        : Services.Settings.Appearance.Theme;

    private void ApplyEdgeGestureSetting()
        => Platform.System.EdgeGestureService.SetEdgeGesturesDisabled(
            Overlay.Window.Handle, Services.Settings.Advanced.IsEnableEdgeGestureUtil);

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = DocumentActive ? _documentWorkspace.Window : (Window)_board ?? _bar;
        return await YesOrNoWindow.AskAsync(owner, message, title);
    }

    private Task NotifyAsync(string title, string message)
    {
        if (_disposed || _exiting) return Task.CompletedTask;
        _bar?.ClosePanels();
        var notice = _notices.FirstOrDefault(window => window.Title == title && window.Message == message);
        if (notice is not null) { notice.Activate(); return notice.Completion; }
        notice = new NoticeWindow(title, message, EffectiveTheme);
        _notices.Add(notice);
        notice.Closed += (_, _) => _notices.Remove(notice);
        var owner = DocumentActive ? _documentWorkspace.Window : (Window)_board ?? _bar;
        if (owner is not null) WindowChrome.CenterOnOwner(notice, owner);
        NotifyWindowOpened(notice);
        notice.Activate();
        WindowChrome.EnsureAbove(notice, Overlay.Window.Handle);
        return notice.Completion;
    }

    private void OnPptPresentationNotice(PptPresentationNotice notice)
    {
        _dispatcher.TryEnqueue(async () => await ShowPptPresentationNoticeAsync(notice));
    }

    private async Task ShowPptPresentationNoticeAsync(PptPresentationNotice notice)
    {
        // WPF YesOrNoNotificationWindow parity: exact strings, topmost 450x160 window that also
        // works while the floating bar is folded (the old ContentDialog hung on a hidden XamlRoot).
        var owner = (Window)_board ?? _bar;
        string resumeHint = notice.ResumeIndexDegraded ? "（按旧页码恢复，页序可能已变化）" : "";
        if (notice.PreviousSlideIndex is > 0
            && await YesOrNoWindow.AskAsync(owner, $"上次播放到了第 {notice.PreviousSlideIndex} 页，是否立即跳转？{resumeHint}"))
            _pptSession.GoToSlide(notice.PreviousSlideIndex.Value);

        if (notice.OfferRestoreHiddenSlides
            && await YesOrNoWindow.AskAsync(owner, "检测到此演示文档中包含隐藏的幻灯片，是否取消隐藏？"))
            _pptSession.RestoreHiddenSlides();

        if (notice.OfferDisableTimings
            && await YesOrNoWindow.AskAsync(owner, "检测到此演示文档中自动播放或排练计时已经启用，可能导致幻灯片自动翻页，是否取消？"))
            _pptSession.DisableSlideTimings();

        if (notice.LegacyInkFilesDetected || notice.ResumeIndexDegraded)
            await ShowInkRecoveryWizardAsync(notice);
    }

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
        if (!_pptGate.IsCurrent(_pptCaptureSession, _pptCaptureGeneration)) return;
        // Capture screen pixels synchronously before the COM navigation mutates the slideshow.
        try
        {
            var area = ShowWindowTracker.TryGet(request.Snapshot.WindowHandle, out var measured)
                ? new ScreenRect(measured.X, measured.Y, measured.Width, measured.Height)
                : request.Snapshot.WindowHandle != IntPtr.Zero
                    ? ScreenService.FromWindow(request.Snapshot.WindowHandle).Bounds : GetScreenshotBounds();
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
            RememberPptScreenshot(path, request.Snapshot.Slide?.Index ?? request.InkSlide?.Index ?? 0,
                request.Snapshot.Slide?.SlideId ?? request.InkSlide?.SlideId ?? 0);
            _ = ScreenshotService.SavePngAsync(path, pixels, width, height);
            SaveSlideNavigatorThumbnail(request.Snapshot.Presentation, _pptScreenshotSlideId, pixels, width, height);
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
        RefreshDocumentTarget();
        MaintainAutomation();
        Overlay.TrimIdleResources();
        if (_selectionToolbar is { IsShown: false } toolbar && Environment.TickCount64 - toolbar.HiddenSince >= 15_000)
            toolbar.Close();
        if (Environment.TickCount64 - _lastDisplayCheck >= 1000) RefreshDisplays();
        RefreshPresentation();

        // Always evaluate auto-fold so disabling its settings also unfolds a bar that was
        // previously folded automatically.
        if (_documentWorkspace is null) ApplyAutoFold();
        RaiseChromeAboveOverlay();
        RefreshShortcutRegistration();
    }

    private bool _presentationRefreshQueued, _refreshingPresentation;
    private void QueuePresentationRefresh()
    {
        if (_exiting || _disposed || _presentationRefreshQueued) return;
        _presentationRefreshQueued = true;
        if (!_dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _presentationRefreshQueued = false;
            if (_exiting || _disposed) return;
            RefreshPresentation();
            RaiseChromeAboveOverlay();
        })) _presentationRefreshQueued = false;
    }

    private void RefreshPresentation()
    {
        if (_refreshingPresentation) return;
        _refreshingPresentation = true;
        try
        {
            if (Services.Settings.PowerPoint.PowerPointSupport && _documentWorkspace is null)
            {
                _pptCaptureGeneration = _pptGate.NextGeneration();
                _pptCaptureSession = _pptGate.Session;
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
                    if (isShow) _bar?.SetPresentationMode(true, current.WindowHandle, current.IsWindowedShow);
                    _lastSlideShowState = isShow;
                    Services.PresentationActive = isShow;
                    Services.ApplySettings();
                    _bar?.Bar.SetPptModeVisible(isShow);
                    if (isShow)
                    {
                        _pptGate.BeginSession();
                        _pptCaptureSession = _pptGate.Session;
                        _pptCaptureGeneration = _pptGate.Generation;
                    }
                    else
                    {
                        _pptSources.Reset();
                        CloseBlackScreen();
                    }
                    if (isShow)
                    {
                        // MW_PPT.cs:283-286: slideshow begin leaves board mode.
                        if (_board is not null) ToggleBoard();
                        if (Services.Settings.PowerPoint.IsShowCanvasAtNewSlideShow)
                        {
                            Overlay.Show();
                            // MW_PPT.cs:302,397-400: lastDesktopInkColor=1 -> red pen on show begin.
                            Services.OverlayInk.CurrentAttributes.Color = Core.Ink.InkColor.Red;
                            _bar?.Bar.RefreshSelection();
                        }
                    }
                    else
                    {
                        // MW_PPT.cs:505-508: slideshow end leaves board mode; canvas hide state
                        // only re-applies when a non-transparent board cover was active (exited above).
                        if (_board is not null) ToggleBoard();
                    }
                }
                _bar?.SetPresentationMode(isShow, current.WindowHandle, current.IsWindowedShow);
                if (isShow && _board is null) Overlay.Window.EnsureAbove(current.WindowHandle);
                UpdatePptNavigation(current);
            }
            else
            {
                if (_lastSlideShowState)
                {
                    _lastSlideShowState = false;
                    Services.PresentationActive = false;
                    Services.ApplySettings();
                    _bar?.Bar.SetPptModeVisible(false);
                    _pptSources.Reset();
                    CloseBlackScreen();
                }
                _bar?.SetPresentationMode(false);
                UpdatePptNavigation(PptSnapshot.Detached);
            }

            // 原条件：放映中 + 尚无墨迹 + 允许手指手势控制放映。未验证。
            Services.OverlayInk.Palm.PptGestureEligible =
                _lastSlideShowState
                && Services.OverlayInk.Document.Count == 0
                && Services.Settings.PowerPoint.IsEnableFingerGestureSlideShowControl;

            _pollTimer.Interval = Services.Ppt.LastFailure is null ? _pptSession.RecommendedPollInterval : TimeSpan.FromMilliseconds(80);
        }
        catch (Exception ex) { LogPptFailure(new PptFailure("Poll", ex.Message, Marshal.GetHRForException(ex))); }
        finally { _refreshingPresentation = false; }
    }

    /// <summary>
    /// Keep floating tools above the annotation surface after overlay input and placement.
    /// </summary>
    private void RaiseChromeAboveOverlay()
    {
        ImageFilePicker.EnsureAbove(_board is not null ? WindowChrome.GetHwnd(_board) : Overlay.Window.Handle);
        if (_bar is not null && _board is null) WindowChrome.EnsureAbove(_bar, Overlay.Window.Handle);
        if (_selectionToolbar?.IsShown == true) WindowChrome.EnsureAbove(_selectionToolbar, Overlay.Window.Handle);
        _bar?.EnsureHandlesAbove(Overlay.Window.Handle);
        if (_board is not null && _settingsDrawingSurface != WindowChrome.GetHwnd(_board)) WindowChrome.EnsureAbove(_board, Overlay.Window.Handle);
        if (_countdown is not null) WindowChrome.EnsureAbove(_countdown, Overlay.Window.Handle);
        if (_lottery is not null) WindowChrome.EnsureAbove(_lottery, Overlay.Window.Handle);
        if (_replay is not null) WindowChrome.EnsureAbove(_replay, Overlay.Window.Handle);
        if (_guide is not null) WindowChrome.EnsureAbove(_guide, Overlay.Window.Handle);
        if (_updateWindow is not null) WindowChrome.EnsureAbove(_updateWindow, Overlay.Window.Handle);
        foreach (var notice in _notices) WindowChrome.EnsureAbove(notice, Overlay.Window.Handle);
        foreach (var navigation in _pptNavigations.Values)
            if (navigation.AppWindow.IsVisible) WindowChrome.EnsureAbove(navigation, Overlay.Window.Handle);
        if (_toolkit is not null) WindowChrome.EnsureAbove(_toolkit, Overlay.Window.Handle);

        if (_palette is not null) WindowChrome.EnsureAbove(_palette, Overlay.Window.Handle);
        foreach (var palette in _detachedPalettes) WindowChrome.EnsureAbove(palette, Overlay.Window.Handle);
        if (_shapePicker is not null) WindowChrome.EnsureAbove(_shapePicker, Overlay.Window.Handle);
        if (_board is not null)
            foreach (var panel in _board.ToolPanels) WindowChrome.EnsureAbove(panel, Overlay.Window.Handle);
        foreach (var window in _documentWindows) if (!ReferenceEquals(window, _documentWorkspace?.Window) && window.AppWindow.IsVisible) WindowChrome.EnsureAbove(window, Overlay.Window.Handle);
        UpdateSettingsDrawingOrder();
        RefreshDesktopMedia();
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
        NotifyAiSlideChange(snapshot);

        bool inShow = snapshot?.IsSlideShowRunning == true;
        _pptShowSnapshot = inShow ? snapshot : null;
        if (inShow) RefreshSlideIndex(snapshot);
        if (!inShow && !_manualPptNav)
        {
            ClosePptNavigationWindows();
            _pptNavCollapsed = false;
            return;
        }
        bool visible = _board is null && _bar?.IsFolded != true && !_pptNavCollapsed;
        var settings = Services.Settings.PowerPoint;
        foreach (var placement in Enum.GetValues<PptNavigationPlacement>())
        {
            bool side = placement is PptNavigationPlacement.SideLeft or PptNavigationPlacement.SideRight;
            bool enabled = side ? settings.IsShowSidePPTNavigationPanel : settings.IsShowBottomPPTNavigationPanel;
            if (!_pptNavigations.TryGetValue(placement, out var window) && enabled && visible)
            {
                window = CreatePptNavigation(placement);
                _pptNavigations.Add(placement, window);
            }
            window?.Update(snapshot, visible && enabled,
                inShow && (side ? settings.IsShowPPTNavigationSides : settings.IsShowPPTNavigationBottom));
        }
    }

    /// <summary>
    /// 翻页后只提示「题目来自旧页」，不自动清空或多选区；保留课堂上的题目上下文。
    /// </summary>
    private void NotifyAiSlideChange(PptSnapshot snapshot)
    {
        if (Assistant is null) return;
        Assistant.NotifySlideChanged(snapshot?.Slide?.SlideId ?? 0, snapshot?.Slide?.Index ?? 0,
            snapshot?.IsSlideShowRunning == true ? Services.Ppt.GetAnimationStep() : 0);
    }

    private PptNavigationWindow CreatePptNavigation(PptNavigationPlacement placement)
    {
        var window = new PptNavigationWindow(placement, this, Services.Settings.Appearance.Theme, _bar);
        return window;
    }

    private void ClosePptNavigationWindows()
    {
        foreach (var window in _pptNavigations.Values) window.Close();
        _pptNavigations.Clear();
    }

    private void ApplyAutoFold()
    {
        if (_bar is null) return;
        var foreground = ForegroundWindowService.DescribeForeground();
        var a = Services.Settings.Automation;
        bool fold = Core.Settings.AutomationPolicy.ShouldFold(a, foreground.Process, foreground.Title, foreground.Height, _lastSlideShowState);
        if (!fold && a.IsAutoFoldInOldZyBoard)
            fold = WindowEnumerator.WindowExists("WhiteBoard - DrawingWindow") || WindowEnumerator.WindowExists("InstantAnnotationWindow");

        if (fold != _lastAutoFoldRequested)
        {
            _lastAutoFoldRequested = fold;
            _manualFoldOverride = null;
        }
        bool desiredFold = _manualFoldOverride ?? fold;

        if (desiredFold && !_bar.IsFolded)
        {
            // WPF FoldFloatingBar_Click always invokes CursorIcon_Click before hiding.
            if (!Overlay.ClickThrough) ToggleClickThrough();
            _bar.SetFolded(true);
        }
        else if (!desiredFold && _bar.IsFolded)
        {
            // WPF unfold restores only the toolbar; the overlay remains in mouse mode until
            // the user selects an ink tool.
            _bar.SetFolded(false);
        }
        if (desiredFold) Overlay.ClickThrough = true;
    }

    // ---- IShellActions ----------------------------------------------------

    public void ToggleBoard()
    {
        CloseDetachedPalettes();
        _imageImportCancellation?.Cancel();
        _videoImportCancellation?.Cancel();
        _selectionToolbar?.Hide();
        _replay?.Close();
        if (_board is null)
        {
            Services.BoardModeActive = true;
            Services.ApplySettings();
            _documentWorkspace?.Window.AppWindow.Hide();
            _palette?.Close(); _shapePicker?.Close();
            _board = new BoardWindow(Services, this, _bar);
            NotifyWindowOpened(_board);
            _board.CanvasInputBegan += OnBoardInputBegan;
            _board.CurrentPageChanged += OnBoardPageChanged;
            _recorder = new InkRecorder(Services.Board);
            _recorder.Start();
            BindBoardController(_board.CurrentController);
            BindToolbar(_board.CurrentController);
            var boardWindow = _board;
            _board.Closed += (_, _) => OnBoardClosed(boardWindow);
            _board.Activate();
            // The board is opaque and full-screen; the screen overlay would draw over it.
            Overlay.Hide();
            // WPF slides the floating bar off-edge in board mode; hide it here (handles stay).
            if (_bar is not null && !_bar.IsFolded) _bar.AppWindow.Hide();
        }
        else
        {
            _board.Close();
        }
    }

    private void OnBoardPageChanged()
    {
        _replay?.Close();
        if (_board is null || _bar is null) return;
        BindToolbar(_board.CurrentController);
        BindBoardController(_board.CurrentController);
        _palette?.Close();
        _palette = null;
        _shapePicker?.Close(); _shapePicker = null;
        Assistant?.NotifyBoardChanged();
        if (_teachingSession is not null) _teachingSession.CurrentBoardPageIndex = Services.Board.CurrentIndex;
    }

    private void OnBoardInputBegan()
    {
        if (_board is not null) ImageFilePicker.EnsureAbove(WindowChrome.GetHwnd(_board));
        _palette?.DismissOnOverlayInput();
        _shapePicker?.DismissOnOverlayInput();
    }

    private void OnBoardClosed(BoardWindow board)
    {
        if (!ReferenceEquals(_board, board)) return;
        _board = null;
        Services.BoardModeActive = false;
        Services.ApplySettings();
        board.CurrentPageChanged -= OnBoardPageChanged;
        board.CanvasInputBegan -= OnBoardInputBegan;
        StopRecording();
        if (_disposed || _exiting) return;
        _replay?.Close();
        _palette?.Close(); _shapePicker?.Close();
        BindBoardController(null);
        BindToolbar(ActiveController);
        Services.Modules.Invoke<IScreenTrackingService>(tracking => tracking.Refresh());
        if (_documentWorkspace is not null) { _documentWorkspace.Window.Activate(); OnDocumentPageChanged(); }
        else Overlay.Show();
        if (_bar is not null && !_bar.IsFolded)
        {
            WindowChrome.ShowWithoutActivation(_bar);
            _bar.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                            Services.Settings.Appearance.FloatingBarScale, recenter: false);
        }
        QueueDesktopSelection();
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

    /// <summary>暂停录制（仅录制中有效）；暂停期间事件不入时间轴，计时扣除暂停时长。</summary>
    public void PauseRecording()
    {
        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || recorder.IsPaused) return;
        recorder.Pause();
    }

    /// <summary>继续录制（仅录制中有效）。</summary>
    public void ResumeRecording()
    {
        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || !recorder.IsPaused) return;
        recorder.Resume();
    }

    /// <summary>浮条入口：在暂停 / 继续之间切换；未在录制时静默忽略。</summary>
    public void ToggleRecordingPause()
    {
        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording) return;
        if (recorder.IsPaused) recorder.Resume();
        else recorder.Pause();
    }

    public void OpenSettings() => OpenSettingsPage(null);

    private void OpenSettingsPage(string category)
    {
        if (category is not null) _requestedSettingsCategory = category;
        if (_settings is not null)
        {
            ShowRequestedSettingsPage(_settings);
            ActivateSettingsWindow(_settings); return;
        }
        if (_settingsOpening || _exiting) return;
        _settingsOpening = true;
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (_exiting) return;
                _bar?.ClosePanels();
                _palette?.DismissOnOverlayInput();
                _shapePicker?.DismissOnOverlayInput();
                var window = new SettingsWindow(Services, Updater, this, RosterDirectory);
                _settings = window;
                ShowRequestedSettingsPage(window);
                NotifyWindowOpened(window);
                window.SettingsChanged += OnSettingsChanged;
                window.Activated += (_, _) => UpdateSettingsDrawingOrder();
                window.Closed += (_, _) =>
                {
                    window.SettingsChanged -= OnSettingsChanged;
                    _settings = null;
                    UpdateSettingsDrawingOrder();
                    _settingsRosterSource?.AppWindow.Show(activateWindow: false);
                    _settingsRosterSource = null;
                };
                ActivateSettingsWindow(window);
            }
            finally { _settingsOpening = false; }
        });
    }

    private void ActivateSettingsWindow(SettingsWindow window)
    {
        _bar?.ClosePanels();
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        window.Activate();
        // Settings is opened by an explicit user action from nonactivating tool chrome.
        // Request native foreground focus as well as XAML activation, then reorder ink.
        NativeMethods.SetForegroundWindow(WindowChrome.GetHwnd(window));
        UpdateSettingsDrawingOrder();
    }

    private void ShowRequestedSettingsPage(SettingsWindow window)
    {
        if (_requestedSettingsCategory is null) return;
        window.SelectCategory(_requestedSettingsCategory);
        if (_requestedSettingsCategory == "抽奖与点名") window.RevealRosterEditor();
        _requestedSettingsCategory = null;
    }

    private void UpdateSettingsDrawingOrder()
    {
        if (_disposed) return;
        var settings = _settings is null ? IntPtr.Zero : WindowChrome.GetHwnd(_settings);
        var surface = IntPtr.Zero;
        if (settings != IntPtr.Zero && NativeMethods.GetForegroundWindow() == settings)
        {
            if (_board is not null) surface = WindowChrome.GetHwnd(_board);
            else if (!DocumentActive) surface = Overlay.Window.Handle;
        }
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        if (surface != _settingsDrawingSurface)
        {
            if (_settingsDrawingSurface != IntPtr.Zero)
                NativeMethods.SetWindowPos(_settingsDrawingSurface,
                    _settingsSurfaceWasTopmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            _settingsDrawingSurface = surface;
            _settingsSurfaceWasTopmost = surface != IntPtr.Zero && NativeMethods.HasExStyle(surface, NativeMethods.WS_EX_TOPMOST);
        }
        // A normal settings window must receive input while the drawing surface stays visible.
        // Inserting the drawing HWND after it also removes that surface from the topmost band.
        if (surface != IntPtr.Zero) NativeMethods.SetWindowPos(surface, settings, 0, 0, 0, 0, flags);
    }

    private void OpenRosterSettings(LotteryWindow source)
    {
        if (!ReferenceEquals(_settingsRosterSource, source))
        {
            _settingsRosterSource?.AppWindow.Show(activateWindow: false);
            _settingsRosterSource = source;
            source.Closed += (_, _) => { if (ReferenceEquals(_settingsRosterSource, source)) _settingsRosterSource = null; };
        }
        source.AppWindow.Hide();
        OpenSettingsPage("抽奖与点名");
    }

    private void OnSettingsChanged()
    {
        QueueDesktopSelection();
        _bar?.Reposition(Services.Settings.Appearance.FloatingBarBottomMargin,
                         Services.Settings.Appearance.FloatingBarScale);
        if (_bar is not null) WindowChrome.ApplyTheme(_bar, Services.Settings.Appearance.Theme);
        // WPF ApplyScaling: menus live inside the scaled bar/board Viewbox, so scale them live.
        double menuScale = _board is not null
            ? Services.Settings.Appearance.BlackboardScale
            : Services.Settings.Appearance.FloatingBarScale;
        _palette?.ApplyScale(menuScale);
        foreach (var palette in _detachedPalettes) palette.ApplyScale(menuScale);
        _shapePicker?.ApplyScale(menuScale);
        _board?.RefreshUiScale();
        _bar?.Bar.RefreshSettingsVisuals();
        if (_lastSlideShowState) UpdatePptNavigation(_pptSession.Snapshot);
        ApplyEdgeGestureSetting();
    }

    private void ApplyDisplaySettings() => Overlay.ConfigureDisplays(Services.Settings.Display);

    private void RefreshPaletteNibModes()
    {
        _board?.RefreshToolSettings();
        if (_palette is not null) { ConfigurePaletteNibMode(_palette); _palette.RefreshFromController(); }
        foreach (var palette in _detachedPalettes) { ConfigurePaletteNibMode(palette); palette.RefreshFromController(); }
    }

    private void ConfigurePaletteNibMode(PenPaletteWindow palette) => palette.ConfigureNibMode(
        Services.Settings.Appearance.IsEnableDisPlayNibModeToggler, Services.Settings.Startup.IsEnableNibMode,
        value => { Services.Settings.Startup.IsEnableNibMode = value; Services.ApplySettings(); Services.SaveSettings(); });

    public void OpenTimer()
    {
        if (_countdown is not null) { _countdown.Activate(); return; }
        var window = new CountdownWindow(EffectiveTheme, Services.Settings.Classroom.CountdownMinutes);
        NotifyWindowOpened(window);
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
    /// 倒计时归零：练习会话先生成板书快照（未录制也保存），录制中再写 TimerEnded
    /// 并追加「练习结束」章节书签；非练习的普通计时只影响原有录制语义。
    /// </summary>
    private async Task CaptureTimerEndedAsync()
    {
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

        PracticePlan practice = _activePractice;
        if (practice is not null)
        {
            PracticeFinishRecord record = PracticeFinishPolicy.Plan(
                practice, _recorder?.IsRecording == true, !string.IsNullOrEmpty(screenshot));
            record.FinishedAtMs = Environment.TickCount64;
            record.SnapshotPath = screenshot ?? "";
            _teachingSession?.Link(
                "practice", practice.Id, _pptSession.Snapshot?.Slide?.SlideId ?? 0, _pptSession.Snapshot?.Slide?.Index ?? 0, pageIndex,
                record.BookmarkName, record.FinishedAtMs);
            _activePractice = null;
            _ = NotifyAsync("随堂练习",
                string.IsNullOrEmpty(record.SnapshotPath)
                    ? "练习结束，板书快照保存失败。"
                    : "练习结束，已保存板书快照。");
        }

        var recorder = _recorder;
        if (recorder is null || !recorder.IsRecording || recorder.Session is null) return;

        long timeMs = recorder.ElapsedMs;
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

            if (practice is not null)
                recorder.Timeline.AddBookmark(timeMs, practice.BookmarkName, "practice");
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
        _lottery = new LotteryWindow(Services.Lottery, autoClose: false, EffectiveTheme, RosterDirectory, OpenRosterSettings);
        _lottery.Picked += OnLotteryPicked;
        _lottery.OptionsChanged += () =>
        {
            Services.Settings.Rand.PeopleCount = Services.Lottery.PeopleCount;
            Services.Settings.Rand.SamplingMode = Services.Lottery.SamplingMode;
            Services.Settings.Rand.CooldownHalfLifeDays = Services.Lottery.CooldownHalfLifeDays;
            Services.Settings.Rand.CooldownPenaltyStrength = Services.Lottery.CooldownPenaltyStrength;
            Services.SaveSettings();
        };
        NotifyWindowOpened(_lottery);
        WindowChrome.ApplyTheme(_lottery, EffectiveTheme);
        _lottery.Closed += (_, _) => _lottery = null;
        _lottery.Activate();
        WindowChrome.EnsureAbove(_lottery);
    }

    /// <summary>WPF 单次抽选: quick-pick lottery that auto-rolls and auto-closes after results.</summary>
    public void OpenLotteryQuick()
    {
        var quick = new LotteryWindow(Services.Lottery, autoClose: true, EffectiveTheme, RosterDirectory, OpenRosterSettings);
        quick.Picked += OnLotteryPicked;
        WindowChrome.ApplyTheme(quick, EffectiveTheme);
        quick.Activate();
        WindowChrome.EnsureAbove(quick);
    }

    private void OnLotteryPicked(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _ = PasteTextCardAsync("抽奖结果", name);
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
        var window = new SpotlightWindow(AiAssistantAvailable);
        window.CaptureRequested += askAi => _ = CaptureFocusAsync(askAi);
        window.SaveRegionRequested += OnSaveFocusRegion;
        window.CycleRegionRequested += OnCycleFocusRegion;
        _spotlight = window;
        _spotlight.Closed += (_, _) => _spotlight = null;
        _spotlight.Activate();
    }

    public void OpenMagnifier()
    {
        if (_magnifier is not null) { _magnifier.Activate(); return; }
        var window = new MagnifierWindow(AiAssistantAvailable);
        window.CaptureRequested += askAi => _ = CaptureFocusAsync(askAi);
        RestoreWindowPosition(
            window.MoveTo, Services.Settings.Classroom.MagnifierX, Services.Settings.Classroom.MagnifierY);
        _magnifier = window;
        _magnifier.Closed += (_, _) =>
        {
            try
            {
                Services.Settings.Classroom.MagnifierX = window.PositionX;
                Services.Settings.Classroom.MagnifierY = window.PositionY;
                Services.SaveSettings();
            }
            catch (Exception) { }

            if (ReferenceEquals(_magnifier, window)) _magnifier = null;
        };
        _magnifier.Activate();
    }

    public void OpenToolkit()
    {
        if (_toolkit is not null) { _toolkit.ShowWithoutActivating(); return; }
        var window = new ToolkitWindow(this, Services.Settings.Appearance.Theme,
            OpenExampleLibrary, OpenResourceBundle);
        window.SetCollapsed(Services.Settings.Classroom.ToolkitCollapsed);
        RestoreWindowPosition(
            window.MoveTo, Services.Settings.Classroom.ToolkitX, Services.Settings.Classroom.ToolkitY);
        _toolkit = window;
        _toolkit.Closed += (_, _) =>
        {
            Services.Settings.Classroom.ToolkitCollapsed = window.IsCollapsed;
            try
            {
                Services.Settings.Classroom.ToolkitX = window.PositionX;
                Services.Settings.Classroom.ToolkitY = window.PositionY;
            }
            catch (Exception) { }
            Services.SaveSettings();
            if (ReferenceEquals(_toolkit, window)) _toolkit = null;
        };
        _toolkit.ShowWithoutActivating();
    }

    private static void RestoreWindowPosition(Action<int, int> move, int x, int y)
    {
        if (move is null || x == int.MinValue || y == int.MinValue) return;
        var work = ScreenService.WorkArea;
        int clampedX = Math.Clamp(x, work.X, work.X + Math.Max(0, work.Width - 120));
        int clampedY = Math.Clamp(y, work.Y, work.Y + Math.Max(0, work.Height - 80));
        move(clampedX, clampedY);
    }

    /// <summary>
    /// 打开本地例题库窗口。窗口采用单实例：已创建（包括通过关闭按钮隐藏）时重新显示并置前，
    /// 不重复创建，避免丢失当前筛选与选中状态。
    /// </summary>
    public void OpenExampleLibrary()
    {
        if (_exampleLibrary is not null)
        {
            _exampleLibrary.ShowWindow();
            return;
        }

        var window = new ExampleLibraryWindow(Services.Settings, Services.Settings.Appearance.Theme);
        _exampleLibrary = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_exampleLibrary, window)) _exampleLibrary = null;
        };
        window.ShowWindow();
    }

    /// <summary>
    /// 打开课堂资源包窗口。与例题库一致采用单实例：已创建（包括关闭按钮隐藏）时重新显示并置前，
    /// 不重复创建，避免丢失当前文件列表与操作结果提示。打包受
    /// <see cref="InkCanvas.Core.Settings.FeatureSettings.EnableResourceBundle"/> 控制。
    /// </summary>
    public void OpenResourceBundle()
    {
        if (_resourceBundle is not null)
        {
            _resourceBundle.ShowWindow();
            return;
        }

        var window = new ResourceBundleWindow(Services.Settings.Features, Services.Settings.Appearance.Theme);
        _resourceBundle = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_resourceBundle, window)) _resourceBundle = null;
        };
        window.ShowWindow();
    }

    // ---- 课堂场景 / 分组积分 / 讲义导出 / 聚焦贴图 -----------------------

    public void CyclePreset()
    {
        var presets = new List<ClassroomPreset>(ClassroomPresetLibrary.BuiltIns);
        presets.AddRange(LoadCustomPresets());

        int current = 0;
        for (int i = 0; i < presets.Count; i++)
        {
            if (string.Equals(presets[i].Id, _preset?.Id, StringComparison.Ordinal))
            {
                current = i;
                break;
            }
        }

        ApplyPreset(presets[(current + 1) % presets.Count]);
    }

    public void OpenPresetEditor()
    {
        if (_presetWindow is not null)
        {
            _presetWindow.Activate();
            return;
        }

        var window = new PresetWindow(_preset, Services.Settings.Appearance.Theme, AiAssistantAvailable);
        window.Saved += preset =>
        {
            SaveCustomPreset(preset);
            ApplyPreset(preset);
        };
        _presetWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_presetWindow, window)) _presetWindow = null;
        };
        window.Activate();
    }

    private IReadOnlyList<ClassroomPreset> LoadCustomPresets()
        => ClassroomPresetLibrary.Deserialize(Services.Settings.Classroom.CustomPresets);

    private void SaveCustomPreset(ClassroomPreset preset)
    {
        if (preset is null || preset.IsBuiltIn) return;

        var presets = new List<ClassroomPreset>(LoadCustomPresets());
        int index = presets.FindIndex(item =>
            string.Equals(item.Id, preset.Id, StringComparison.Ordinal));
        if (index >= 0) presets[index] = preset;
        else presets.Add(preset);

        Services.Settings.Classroom.CustomPresets =
            ClassroomPresetLibrary.Serialize(presets);
        Services.SaveSettings();
    }

    public void ApplyPreset(ClassroomPreset preset)
    {
        if (preset is null) return;

        ClassroomPreset normalized = ClassroomPresetLibrary.Normalize(preset.Clone(), preset.Id);
        _preset = normalized;
        Services.Settings.Classroom.PresetId = normalized.Id;
        Services.SaveSettings();

        PresetAction action = ClassroomPresetLibrary.Plan(normalized);
        foreach (ClassroomTool tool in action.Open) OpenClassroomTool(tool);
        foreach (ClassroomTool tool in action.Close) CloseClassroomTool(tool);
        if (action.CollapseToolkit && _toolkit is not null) _toolkit.SetCollapsed(true);

        if (_teachingSession is not null)
        {
            _teachingSession.PresetId = normalized.Id;
            _teachingSession.UpdatedAtMs = Environment.TickCount64;
        }

        if (!normalized.IsBuiltIn)
        {
            SaveCustomPreset(normalized);
        }

        _ = NotifyAsync("课堂场景", "已切换到「" + normalized.Name + "」。");
    }

    private void OpenClassroomTool(ClassroomTool tool)
    {
        switch (tool)
        {
            case ClassroomTool.SlideNavigator:
                if (_slideNav?.IsVisible != true) ToggleSlideNavigator();
                break;
            case ClassroomTool.AiAssistant:
            case ClassroomTool.AiStepGuide:
                OpenAiAssistant();
                break;
            case ClassroomTool.Spotlight:
                OpenSpotlight();
                break;
            case ClassroomTool.Magnifier:
                OpenMagnifier();
                break;
            case ClassroomTool.Board:
                if (_board is null) ToggleBoard();
                break;
            case ClassroomTool.Countdown:
                OpenTimer();
                break;
            case ClassroomTool.GroupScore:
                OpenGroupScore();
                break;
            case ClassroomTool.HandoutExport:
                OpenHandout();
                break;
            case ClassroomTool.Toolkit:
                OpenToolkit();
                break;
        }
    }

    private void CloseClassroomTool(ClassroomTool tool)
    {
        switch (tool)
        {
            case ClassroomTool.Countdown:
                _countdown?.Close();
                break;
            case ClassroomTool.Spotlight:
                _spotlight?.Close();
                break;
            case ClassroomTool.Magnifier:
                _magnifier?.Close();
                break;
            case ClassroomTool.GroupScore:
                _groupScoreWindow?.Close();
                break;
            case ClassroomTool.SlideNavigator:
                _slideNav?.HidePanel();
                break;
        }
    }

    public void OpenGroupScore()
    {
        EnsureGroupScore();

        if (_groupScoreWindow is not null)
        {
            _groupScoreWindow.Activate();
            return;
        }

        var window = new GroupScoreWindow(
            _groupScore,
            LoadRosterNames(),
            ParseGroupMode(Services.Settings.Classroom.GroupSplitMode),
            Services.Settings.Classroom.GroupSplitValue,
            Services.Settings.Appearance.Theme,
            text => _ = PasteTextCardAsync("小组积分", text));
        window.StateChanged += _ =>
        {
            Services.Settings.Classroom.GroupSplitMode =
                window.Mode == GroupSplitMode.BySize ? "size" : "groupCount";
            Services.Settings.Classroom.GroupSplitValue = window.SplitValue;
            SaveGroupScore();
            Services.SaveSettings();
        };
        window.Closed += (_, _) =>
        {
            SaveGroupScore();
            if (ReferenceEquals(_groupScoreWindow, window)) _groupScoreWindow = null;
        };

        _groupScoreWindow = window;
        WindowChrome.ApplyTheme(window, Services.Settings.Appearance.Theme);
        window.Activate();
    }

    private void EnsureGroupScore()
    {
        if (_groupScore is not null) return;
        _groupScore = GroupScoreService.Deserialize(TryRead(GroupScorePath()));
    }

    private static GroupSplitMode ParseGroupMode(string value)
        => string.Equals(value, "size", StringComparison.OrdinalIgnoreCase)
            ? GroupSplitMode.BySize
            : GroupSplitMode.ByGroupCount;

    private static string GroupScorePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InkCanvas", "groups.json");

    private void SaveGroupScore()
    {
        try
        {
            if (_groupScore is null) return;
            string path = GroupScorePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _groupScore.Serialize());
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"分组积分保存失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private IReadOnlyList<string> LoadRosterNames()
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InkCanvas", "roster.json");
            if (!File.Exists(path)) return Array.Empty<string>();

            var roster = new ClassroomRoster();
            return roster.Load(path) ? roster.Names : Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static string TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public void OpenHandout() => _ = ShowHandoutAsync();

    private async Task ShowHandoutAsync()
    {
        SlideContentIndex index = EnsureSlideIndexForExport();
        if (index is null || index.Count == 0)
        {
            await NotifyAsync("课后讲义", "请先在课件放映中翻几页，让页索引建立后再导出。");
            return;
        }

        var mode = new RadioButtons();
        mode.Items.Add("批注 + 教师解析");
        mode.Items.Add("仅批注");
        mode.SelectedIndex = 0;

        var selectionOnly = new CheckBox { Content = "仅导出勾选页（默认全部）" };
        var pageChecks = new List<(int SlideId, CheckBox Box)>();
        var pageList = new StackPanel { Spacing = 2 };
        foreach (SlideContentEntry entry in index.Slides)
        {
            var box = new CheckBox
            {
                Content = "第 " + entry.Index + " 页"
                    + (entry.HasInk ? " · 有批注" : "")
                    + (entry.Favorite ? " · 收藏" : "")
                    + (entry.HasAi ? " · AI" : ""),
                IsChecked = true,
                MinHeight = 30,
            };
            pageChecks.Add((entry.SlideId, box));
            pageList.Children.Add(box);
        }

        var preview = new Image
        {
            Height = 132,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
        };
        SlideContentEntry first = index.Slides[0];
        if (!string.IsNullOrWhiteSpace(first.ThumbnailPath) && File.Exists(first.ThumbnailPath))
        {
            try { preview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(first.ThumbnailPath)); }
            catch (Exception) { }
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "导出前预览（第一页）", FontSize = 12, Opacity = 0.75 });
        content.Children.Add(preview);
        content.Children.Add(mode);
        content.Children.Add(selectionOnly);
        content.Children.Add(new ScrollViewer
        {
            Content = pageList,
            Height = 170,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        var dialog = new ContentDialog
        {
            Title = "导出课后讲义",
            Content = content,
            PrimaryButtonText = "导出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        var options = new HandoutOptions
        {
            Content = mode.SelectedIndex == 1
                ? HandoutContentMode.AnnotationsOnly
                : HandoutContentMode.AnnotationsWithTeacherNotes,
        };

        if (selectionOnly.IsChecked == true)
        {
            options.SelectedSlideIds = pageChecks
                .Where(pair => pair.Box.IsChecked == true)
                .Select(pair => pair.SlideId)
                .ToList();
        }

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "InkCanvas",
            "handout-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        HandoutExportResult result = await HandoutExportService.ExportAsync(
            new HandoutRenderRequest { Index = index, Options = options }, directory);

        if (!result.Success)
        {
            string reason = result.Warnings.Count > 0 ? string.Join("；", result.Warnings) : "没有生成任何页面。";
            await NotifyAsync("讲义导出失败", reason + "（原课件未被修改）");
            return;
        }

        _teachingSession?.RecordExport(result.PdfPath, Environment.TickCount64);
        SaveTeachingSession();
        await NotifyAsync("讲义已导出",
            "共 " + result.Pages.Count + " 页，目录：" + directory
            + (string.IsNullOrEmpty(result.PdfPath) ? "（仅 PNG）" : "（含 PDF）"));
    }

    private SlideContentIndex EnsureSlideIndexForExport()
    {
        if (_slideIndex is { Count: > 0 }) return _slideIndex;

        PptSnapshot snapshot = _pptSession.Snapshot;
        PptPresentationInfo presentation = snapshot?.Presentation;

        try
        {
            if (presentation is not null)
            {
                SlideContentIndex sidecar = LoadSlideIndexSidecar(presentation);
                if (sidecar is { Count: > 0 }) return _slideIndex = sidecar;
            }

            // 不在放映时仍允许导出最近一次课件的讲义（课后沉淀场景）。
            SlideContentIndex latest = LoadLatestSlideIndexSidecar();
            if (latest is not null) return _slideIndex = latest;
            if (presentation is null) return _slideIndex;

            PptPresentationDetails details = Services.Ppt.GetPresentationDetails();
            if (details is null) return _slideIndex;

            _slideIndex = SlideContentIndex.Build(
                details, _pptSession.CreateArchive(), null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            string directory = _pptStore.GetPresentationDirectory(presentation);
            foreach (SlideContentEntry entry in _slideIndex.Slides)
            {
                if (entry.SlideId <= 0 || !string.IsNullOrEmpty(entry.ThumbnailPath)) continue;
                string thumb = Path.Combine(directory, "thumb-" + entry.SlideId + ".png");
                if (File.Exists(thumb)) entry.ThumbnailPath = thumb;
            }

            return _slideIndex;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"讲义导出索引失败：{ex.Message}", Core.Diagnostics.LogType.Error);
            return _slideIndex;
        }
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_bar?.Content is not FrameworkElement root || root.XamlRoot is null)
            return ContentDialogResult.None;
        dialog.XamlRoot = root.XamlRoot;
        return await dialog.ShowAsync();
    }

    public void PasteSpotlightToBoard() => _ = CaptureFocusAsync(askAi: false);

    public void AskAiFromSpotlight() => _ = CaptureFocusAsync(askAi: true);

    private async Task CaptureFocusAsync(bool askAi)
    {
        ScreenRect rect;
        bool fromSpotlight = _spotlight is not null;

        if (_spotlight is not null)
        {
            rect = _spotlight.CurrentScreenRect;
            _spotlight.SetMaskVisible(false);
        }
        else if (_magnifier is not null)
        {
            rect = _magnifier.CurrentScreenRect;
            _magnifier.HideForCapture();
        }
        else
        {
            await NotifyAsync("聚焦贴图", "请先打开聚光灯或放大镜，调整好聚焦区域后再贴图。");
            return;
        }

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            RestoreFocusWindow(fromSpotlight);
            await NotifyAsync("聚焦贴图", "聚焦区域尚未确定，请先拖动聚光灯或移动放大镜取景。");
            return;
        }

        try
        {
            await Task.Delay(90);

            if (askAi)
            {
                await StartAssistantFromRegionAsync(rect);
                return;
            }

            string base64 = await AssistantCapture.CapturePngBase64Async(
                rect.X, rect.Y, rect.Width, rect.Height);
            byte[] png = string.IsNullOrEmpty(base64)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(base64);
            if (png.Length == 0)
            {
                await NotifyAsync("聚焦贴图", "截图失败，请重试。");
                return;
            }

            await PasteImageCardAsync("聚焦区域", png, 320, 220, "聚光灯/放大镜截图");
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"聚焦贴图失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
        finally
        {
            RestoreFocusWindow(fromSpotlight);
        }
    }

    private void RestoreFocusWindow(bool fromSpotlight)
    {
        if (fromSpotlight) _spotlight?.SetMaskVisible(true);
        else _magnifier?.ShowAfterCapture();
    }

    private void OnSaveFocusRegion(ScreenRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            _ = NotifyAsync("聚焦区域", "当前聚焦区无效，请先拖动聚光灯。");
            return;
        }

        FocusRegionItem saved = _focusRegions.Add(new FocusRegionItem
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            SlideId = _pptSession.Snapshot?.Slide?.SlideId ?? 0,
            SlideIndex = _pptSession.Snapshot?.Slide?.Index ?? 0,
            AnimationStep = _pptSession.Snapshot?.IsSlideShowRunning == true
                ? Services.Ppt.GetAnimationStep()
                : 0,
            BoardPageIndex = _board is null ? -1 : Services.Board.CurrentIndex,
        }, Environment.TickCount64);

        if (saved is null)
        {
            _ = NotifyAsync("聚焦区域", "保存失败（最多保存 " + FocusRegionStore.MaximumRegions + " 个区域）。");
            return;
        }

        PersistFocusRegions();
        _ = NotifyAsync("聚焦区域", "已保存「" + saved.Name + "」，共 " + _focusRegions.Count + " 个。");
    }

    private void OnCycleFocusRegion(int delta)
    {
        if (_focusRegions.Count == 0)
        {
            _ = NotifyAsync("聚焦区域", "还没有保存的聚焦区域；点「保存区」先保存当前区域。");
            return;
        }

        ScreenRect currentRect = _spotlight?.CurrentScreenRect ?? default;
        FocusRegionItem current = currentRect.Width > 0
            ? _focusRegions.FindMatching(
                currentRect.X, currentRect.Y, currentRect.Width, currentRect.Height)
            : null;

        FocusRegionItem next = _focusRegions.Next(current, delta);
        if (next is null) return;

        _spotlight?.SetRegion(new ScreenRect(next.X, next.Y, next.Width, next.Height));
        _ = NotifyAsync("聚焦区域", "切换到「" + next.Name + "」（" + next.Order + "/" + _focusRegions.Count + "）。");
    }

    private void PersistFocusRegions()
    {
        Services.Settings.Classroom.SpotlightRegions = _focusRegions.Serialize();
        Services.SaveSettings();
    }

    private Task StartAssistantFromRegionAsync(ScreenRect rect) => Assistant?.OpenRegionAsync(rect) ?? Task.CompletedTask;

    private async Task PasteTextCardAsync(string title, string text)
    {
        var lines = new List<string>();
        foreach (string line in (text ?? "").Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0) lines.Add(trimmed);
        }

        if (lines.Count == 0) return;

        var (width, height) = PasteCardLayout.Estimate(lines);
        byte[] png = await PasteCardRenderer.RenderPngAsync(title, lines, width, height);
        if (png.Length == 0) return;

        await PasteImageCardAsync(title, png, width, height, text);
    }

    private async Task PasteImageCardAsync(
        string title, byte[] png, int width, int height, string metadataText)
    {
        string source = "";
        if (_pptSession.Snapshot?.Presentation is not null)
            source = "课件第 " + (_pptSession.Snapshot?.Slide?.Index ?? 0) + " 页";
        else if (_board is not null)
            source = "画板第 " + (Services.Board.CurrentIndex + 1) + " 页";

        if (!string.IsNullOrEmpty(source))
            metadataText = (metadataText ?? "") + "\n来源：" + source;

        var work = ScreenService.WorkArea;
        double dpi = CurrentDpi() > 0 ? CurrentDpi() : 1.0;
        double viewWidth = work.Width / dpi;
        double viewHeight = work.Height / dpi;
        double left = Math.Clamp(viewWidth / 2.0 - width / 2.0, 8, Math.Max(8, viewWidth - width - 8));
        double top = Math.Clamp(viewHeight / 2.0 - height / 2.0, 8, Math.Max(8, viewHeight - height - 8));

        string metadata = new AiPasteMetadata
        {
            Kind = AiPasteKind.Steps,
            Text = metadataText ?? "",
            Lines = (metadataText ?? "").Split('\n').Where(line => line.Trim().Length > 0).ToList(),
        }.Serialize();

        bool added = Services.Board.AddImage(new BoardImageAsset
        {
            Name = title,
            Data = png,
            ContentType = "image/png",
            Left = (float)left,
            Top = (float)top,
            Width = width,
            Height = height,
            Metadata = metadata,
        });

        if (added && _board is null) ToggleBoard();
        if (added)
        {
            _teachingSession?.Link(
                "tool", Guid.NewGuid().ToString("N"), _pptSession.Snapshot?.Slide?.SlideId ?? 0, _pptSession.Snapshot?.Slide?.Index ?? 0,
                Services.Board.CurrentIndex, title, Environment.TickCount64);
        }
    }

    /// <summary>WPF 保存墨迹: save the active surface (board page or overlay) as .icink.</summary>
    public void SaveStrokes() => _ = SaveStrokesAsync();

    public void InsertImage() => _ = InsertImageAsync();

    internal async Task InsertImageAsync()
    {
        if (DocumentActive) { await NotifyAsync("PDF 批注", "PDF 文档中可使用笔、荧光笔和形状批注。图片可插入画板页。"); return; }
        if (_imagePicking) return;
        _imagePicking = true;
        var cancellation = new CancellationTokenSource();
        _imageImportCancellation = cancellation;
        try
        {
            if (_board is not null) { await _board.InsertImageAsync(); return; }
            var controller = Services.OverlayInk;
            bool IsCurrent() => !_exiting && !_disposed && _board is null && !DocumentActive;
            var viewport = new System.Numerics.Vector2(Overlay.Window.Width / Overlay.Window.DpiScale,
                Overlay.Window.Height / Overlay.Window.DpiScale);
            var asset = await ImageImport.PickAsync(_bar, Services.Settings.Canvas.IsCompressPicturesUploaded,
                viewport, controller.ViewTransform, cancellation.Token);
            if (asset is not null && await ImageImport.WaitForCanvasAsync(controller, IsCurrent, cancellation.Token))
            {
                viewport = new(Overlay.Window.Width / Overlay.Window.DpiScale, Overlay.Window.Height / Overlay.Window.DpiScale);
                ImageImport.PlaceInView(asset, viewport, controller.ViewTransform);
                InsertDesktopImage(asset);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_exiting && !_disposed) await NotifyAsync("插入图片", $"图片插入失败：{ex.Message}"); }
        finally
        {
            _imagePicking = false;
            if (ReferenceEquals(_imageImportCancellation, cancellation)) _imageImportCancellation = null;
            cancellation.Dispose();
        }
    }

    internal void InsertDesktopImage(BoardImageAsset asset)
    {
        if (!Services.ScreenImages.AddImage(asset)) return;
        Overlay.SetInteractionMode(clickThrough: false, inkVisible: true);
        Services.OverlayInk.SelectElement(Services.ScreenImages.GetSelectionElements().Single(element => element.Id == asset.Id));
        if (_bar is not null) { _bar.Bar.ClickThroughActive = false; _bar.Bar.RefreshSelection(); }
    }

    internal void CloneDesktopSelectionToBoard()
    {
        var strokes = Core.Ink.InkStroke.CloneObjects(Services.OverlayInk.Selection);
        var images = Services.OverlayInk.SelectedElements.OfType<BoardSelectionElement>()
            .Where(element => element.Image is not null).Select(element => element.Image.Copy(preserveId: false, copyData: false)).ToList();
        if (strokes.Count == 0 && images.Count == 0) return;
        var center = Services.OverlayInk.SelectionBounds.Center;
        if (Services.Board.Current.Document.Count > 0 || Services.Board.CurrentImages.Count > 0 || Services.Board.CurrentMedia.Count > 0)
        {
            if (!Services.Board.InsertPageAfterCurrent())
            {
                _ = NotifyAsync("衍至画板", "已达到画板页数上限。");
                return;
            }
        }
        ToggleBoard();
        var target = Services.Board.Current;
        var delta = System.Numerics.Matrix3x2.CreateTranslation(target.ViewCenter - center);
        foreach (var stroke in strokes) stroke.Transform(delta, scaleWidth: false);
        foreach (var image in images) image.Transform = BoardElementMath.Append(image.Transform, delta);
        target.Document.AddRange(strokes);
        foreach (var image in images) Services.Board.SetImagePresent(target, image, true);
        target.History.Push(new Core.History.DelegateAction("clone to board",
            () => { target.Document.AddRange(strokes); foreach (var image in images) Services.Board.SetImagePresent(target, image, true); },
            () => { target.Document.RemoveAll(strokes); foreach (var image in images) Services.Board.SetImagePresent(target, image, false); }));
        target.Tool = InkTool.Select;
        target.SelectAll();
    }

    internal async Task ExportDesktopSelectionAsync()
    {
        var strokes = Services.OverlayInk.Selection.Select(stroke => stroke.Clone()).ToList();
        var images = Services.OverlayInk.SelectedElements.OfType<BoardSelectionElement>()
            .Where(element => element.Image is not null).Select(element => element.Image.Copy(copyData: false)).ToList();
        var bounds = Services.OverlayInk.SelectionBounds;
        if (strokes.Count == 0 && images.Count == 0 || bounds.IsEmpty) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = $"选中墨迹 {DateTime.Now:yyyy-MM-dd HH-mm-ss}" };
            picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd((Window)_selectionToolbar ?? _bar));
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            using var controller = new InkController();
            controller.Document.AddRange(strokes);
            var imageStore = new BoardService(controller);
            foreach (var image in images) imageStore.SetImagePresent(controller, image, true);
            controller.GetSelectableElements = imageStore.GetSelectionElements;
            controller.ViewTransform = System.Numerics.Matrix3x2.CreateTranslation(-bounds.Left + 5, -bounds.Top + 5);
            using var renderer = new Ink.Rendering.InkSceneRenderer { ShowSelectionOutline = false };
            await renderer.Images.PrepareAsync(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(), controller);
            using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(),
                Math.Clamp(MathF.Ceiling(bounds.Width + 10), 1, 8192), Math.Clamp(MathF.Ceiling(bounds.Height + 10), 1, 8192), 96);
            using (var drawing = target.CreateDrawingSession())
            {
                drawing.Clear(Microsoft.UI.Colors.Transparent);
                renderer.DrawCommitted(drawing, controller);
            }
            await target.SaveAsync(file.Path, Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"export selection: {ex}", Core.Diagnostics.LogType.Error);
            await NotifyAsync("存为图片", $"存为图片失败：{ex.Message}");
        }
    }

    private async Task SaveStrokesAsync()
    {
        if (DocumentActive) { await _documentWorkspace.SaveAsync(); return; }
        try
        {
            var target = _board is not null ? Services.Board : Services.ScreenImages;
            var owner = (Window)_board ?? _bar;
            bool entireBoard = _board is not null;
            string name = Core.Settings.ArchivePolicy.FileName(Services.Settings.Archive, DateTimeOffset.Now, entireBoard ? target.PageCount : 1, entireBoard ? "整板" : "批注");
            var library = Services.Modules.GetExport<IBoardLibraryService>();
            string path = library is not null ? await library.PickSaveAsync(name) : await ImageFilePicker.SaveArchiveAsync(WindowChrome.GetWorkArea(owner),
                Core.Settings.ArchivePolicy.ResolveDirectory(Services.Settings.Archive.DefaultDirectory), name);
            if (path is null || _disposed) return;
            await Services.Archives.SaveManualAsync(target, path, entireBoard);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"save strokes: {ex}", Core.Diagnostics.LogType.Error);
            if (!_disposed) await NotifyAsync("存档保存失败", ex.GetBaseException().Message);
        }
    }

    /// <summary>WPF 打开墨迹: replace the active surface's strokes from a .icink file (confirmed).</summary>
    public void OpenStrokes() => _ = OpenStrokesAsync();

    private async Task OpenStrokesAsync()
    {
        try
        {
            var owner = (Window)_board ?? _bar;
            var store = _board is not null ? Services.Board : Services.ScreenImages;
            var extensions = new[] { ".icink" }.Concat(Services.Plugins.ImportExtensions).ToList();
            if (Services.Modules.HasExport<IPdfWorkspaceService>()) extensions.Add(".pdf");
            var library = Services.Modules.GetExport<IBoardLibraryService>();
            string path = library is not null ? await library.PickOpenAsync(extensions.Distinct().ToArray()) : await ImageFilePicker.OpenArchiveAsync(WindowChrome.GetWorkArea(owner),
                Core.Settings.ArchivePolicy.ResolveDirectory(Services.Settings.Archive.DefaultDirectory), extensions.Distinct().ToArray());
            if (path is null || _disposed) return;
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) { await OpenPdfAsync(path); return; }
            var page = store.Current;
            bool entireBoard = ReferenceEquals(store, Services.Board);
            var apply = await Services.PrepareInkOpenAsync(path, store, entireBoard);
            if (_disposed || owner != ((Window)_board ?? _bar) || page != store.Current) return;
            if (entireBoard ? store.HasContent : store.Current.Document.Count > 0 || store.CurrentImages.Count > 0 || store.CurrentMedia.Count > 0)
            {
                if (!await YesOrNoWindow.AskAsync(owner, "打开将替换当前墨迹、图片和视频，是否继续？")) return;
            }
            if (_disposed || owner != ((Window)_board ?? _bar) || page != store.Current) return;
            apply();
            _bar?.Bar.RefreshSelection();
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"open strokes: {ex}", Core.Diagnostics.LogType.Error);
            if (!_disposed) await NotifyAsync("文件打开失败", ex.GetBaseException().Message);
        }
    }

    public void OpenReplay()
    {
        if (_replay is not null) { WindowChrome.ShowWithoutActivation(_replay); return; }
        var controller = ActiveController;
        if (!controller.BeginPlayback()) return;
        bool restoreOverlay = _board is null && !DocumentActive && !Overlay.Presenter.InkVisible;
        if (_board is null && !DocumentActive) Overlay.Presenter.InkVisible = true;
        _replay = new ReplayWindow(controller, EffectiveTheme,
            _board is not null ? Services.Settings.Appearance.BlackboardScale : Services.Settings.Appearance.FloatingBarScale,
            _documentWorkspace?.Window ?? (Window)_board ?? _bar);
        NotifyWindowOpened(_replay);
        _replay.Closed += (_, _) => { _replay = null; if (restoreOverlay) Overlay.Presenter.InkVisible = false; };
        WindowChrome.ShowWithoutActivation(_replay);
        WindowChrome.EnsureAbove(_replay);
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
            {
                int pageCount = s.Events is { Count: > 0 } ? s.Events.Max(e => e.PageIndex) + 1 : 0;
                long durationMs = s.Events is { Count: > 0 } ? Math.Max(0, s.Events[^1].TimeMs) : 0;
                list.Items.Add(
                    $"{s.Name}  ·  录制 {s.CreatedAt:yyyy-MM-dd HH:mm}  ·  时长 {FormatSessionDuration(durationMs)}"
                    + $"  ·  {pageCount} 页  ·  {s.Events.Count} 事件");
            }
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

    private static string FormatSessionDuration(long durationMs)
    {
        long totalSeconds = Math.Max(0, durationMs) / 1000;
        long minutes = totalSeconds / 60;
        long seconds = totalSeconds % 60;
        return minutes >= 60
            ? $"{minutes / 60}:{minutes % 60:00}:{seconds:00}"
            : $"{minutes}:{seconds:00}";
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
        _guide = InfoWindow.OpenGuide(EffectiveTheme);
        NotifyWindowOpened(_guide);
        _guide.Closed += (_, _) => _guide = null;
        _guide.Activate();
        WindowChrome.EnsureAbove(_guide);
    }

    public void OpenPenPalette(bool highlighter)
    {
        ActiveController.Tool = highlighter ? InkTool.Highlighter : InkTool.Pen;
        if (_board is not null)
        {
            _board.ShowPenPalette(); return;
        }
        // WPF MW_FloatBarIcons.cs:721-731: clicking the same pen button toggles the palette closed.
        var detachedPalette = _detachedPalettes.FirstOrDefault(window => window.IsHighlighter == highlighter);
        if (detachedPalette is not null) { WindowChrome.EnsureAbove(detachedPalette); return; }
        if (_palette?.IsPinned == true)
        {
            if (_paletteHighlighter == highlighter) { WindowChrome.EnsureAbove(_palette); return; }
            _detachedPalettes.Add(_palette);
            _palette = null;
        }
        if (_palette is not null && _paletteHighlighter == highlighter) { _palette.Close(); _palette = null; return; }
        if (_palette is not null) { _palette.Close(); _palette = null; }
        _paletteHighlighter = highlighter;
        var controller = ActiveController;
        _palette = new PenPaletteWindow(controller, highlighter, EffectiveTheme,
            _board is not null ? Services.Settings.Appearance.BlackboardScale : Services.Settings.Appearance.FloatingBarScale)
        {
            HighlighterAlpha = Services.Settings.Canvas.InkAlpha,
            LightColorScheme = Services.Settings.Appearance.Theme == 1,
            InkWidthChanged = width =>
            {
                if (highlighter) Services.Settings.Canvas.HighlighterWidth = width;
                else if (controller.CurrentAttributes.TaperKind == Core.Ink.TaperKind.InkWash) Services.Settings.Canvas.InkWashWidth = width;
                else Services.Settings.Canvas.InkWidth = width;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            InkStyleChanged = style =>
            {
                Services.Settings.Canvas.InkStyle = style;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            PenNibChanged = nib =>
            {
                Services.Settings.Canvas.PenNib = nib;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            InkAlphaChanged = alpha =>
            {
                Services.Settings.Canvas.InkAlpha = alpha;
                Services.ApplySettings();
                Services.SaveSettings();
            },
            AttributesChanged = () => { Services.RememberToolOptions(controller); _bar?.Bar.RefreshSelection(); },
        };
        NotifyWindowOpened(_palette);
        ConfigurePaletteNibMode(_palette);
        var openedPalette = _palette;
        _palette.Closed += (_, _) =>
        {
            if (ReferenceEquals(_palette, openedPalette)) _palette = null;
            _detachedPalettes.Remove(openedPalette);
        };
        if (_board is not null || _bar is not null)
        {
            var tool = highlighter ? InkTool.Highlighter : InkTool.Pen;
            _palette.PlaceAboveBar(() => _board is not null ? _board.GetToolAnchor(tool) : _bar.GetToolAnchor(tool),
                WindowChrome.GetHwnd((Window)_board ?? _bar),
                () => _board is not null ? _board.GetToolbarBounds() : WindowChrome.GetScreenBounds(_bar, _bar.Bar));
        }
        WindowChrome.ShowWithoutActivation(_palette);
        WindowChrome.EnsureAbove(_palette);
    }

    private void CloseDetachedPalettes()
    {
        foreach (var palette in _detachedPalettes.ToArray()) palette.Close();
    }

    public void OpenShapePicker()
    {
        if (_shapePicker is not null)
        {
            if (!_shapePicker.AutoHide) WindowChrome.EnsureAbove(_shapePicker);
            else _shapePicker.Close();
            return;
        }
        _shapePicker = new ShapePickerWindow(ActiveController,
            () => { Services.RememberToolOptions(ActiveController); _bar?.Bar.RefreshSelection(); }, EffectiveTheme,
            _board is not null ? Services.Settings.Appearance.BlackboardScale : Services.Settings.Appearance.FloatingBarScale,
            boardMode: _board is not null);
        NotifyWindowOpened(_shapePicker);
        var openedPicker = _shapePicker;
        _shapePicker.Closed += (_, _) => { if (ReferenceEquals(_shapePicker, openedPicker)) _shapePicker = null; };
        if (_board is not null || _bar is not null)
        {
            _shapePicker.PlaceAboveBar(() => _board is not null ? _board.GetToolAnchor(InkTool.Shape) : _bar.GetToolAnchor(InkTool.Shape),
                WindowChrome.GetHwnd((Window)_board ?? _bar),
                () => _board is not null ? _board.GetToolbarBounds() : WindowChrome.GetScreenBounds(_bar, _bar.Bar));
        }
        WindowChrome.ShowWithoutActivation(_shapePicker);
        WindowChrome.EnsureAbove(_shapePicker);
    }

    internal ScreenRect GetScreenshotBounds()
    {
        var workspace = _documentWorkspace?.Window ?? (Window)_board;
        return workspace is not null ? ScreenService.FromWindow(WindowChrome.GetHwnd(workspace)).Bounds : ScreenService.Virtual;
    }

    public async Task ScreenshotAsync() => await CaptureScreenshotToFileAsync();

    private async Task<string> CaptureScreenshotToFileAsync()
    {
        try
        {
            var screen = GetScreenshotBounds();
            string dir = Path.Combine(Services.Settings.Automation.AutoSavedStrokesLocation, "Screenshots");
            string path = await ScreenshotService.CaptureToFileAsync(dir, screen.X, screen.Y, screen.Width, screen.Height,
                Services.Settings.Automation.IsSaveScreenshotsInDateFolders);

            if (Services.Settings.Automation.IsAutoSaveStrokesAtScreenshot)
                await Services.AutoSave.SaveSnapshotAsync(ActiveController.Document.Strokes, "screenshot");
            return path;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile($"Screenshot: {ex.Message}", Core.Diagnostics.LogType.Error);
            return null;
        }
    }

    public bool ClearCanvas()
    {
        var controller = ActiveController;
        if (Services.Settings.Archive.ScreenCapture.SaveOnClear) _ = SaveScreenArchiveAsync("清屏前存档");
        var automation = Services.Settings.Automation;
        if (automation.IsAutoSaveStrokesAtClear && controller.Document.Count > automation.MinimumAutomationStrokeNumber)
        {
            _ = Services.AutoSave.SaveSnapshotAsync(controller.Document.Strokes, "clear");
            _ = ScreenshotAsync();
        }
        if (DocumentActive) controller.Clear();
        else (_board is not null ? Services.Board : Services.ScreenImages).ClearCurrentPage();
        return true;
    }

    public void ToggleFold()
    {
        if (_bar is null) return;
        bool fold = !_bar.IsFolded;
        _manualFoldOverride = fold;
        if (fold)
        {
            // WPF FoldFloatingBar_Click invokes CursorIcon_Click before hiding, in desktop and PPT modes.
            if (!Overlay.ClickThrough) ToggleClickThrough();
            _bar.SetFolded(true);
        }
        else
        {
            _bar.SetFolded(false);
        }
    }

    public void ToggleClickThrough()
    {
        if (DocumentActive) { ActiveController.Tool = ActiveController.Tool == InkTool.Pan ? InkTool.Pen : InkTool.Pan; return; }
        if (_board is not null)
        {
            _ = CloseBoardToMouseModeAsync();
            return;
        }
        bool enableMouseMode = !Overlay.ClickThrough;
        if (enableMouseMode)
        {
            SaveBeforeMouseMode();
            bool showInk = _lastSlideShowState
                ? !Services.Settings.PowerPoint.HideStrokesAfterLeavingAnnotation
                : !Services.Settings.Canvas.HideStrokeWhenSelecting;
            Overlay.SetInteractionMode(clickThrough: true, inkVisible: showInk);
            if (Core.Settings.AutomationPolicy.ShouldClearOnMouseMode(Services.Settings.Automation, Services.Settings.PowerPoint, _lastSlideShowState))
                Services.ScreenImages.ClearCurrentPage();
        }
        else
        {
            // CursorIcon_Click in the WPF source restores drawing without clearing strokes.
            Services.Modules.Invoke<IScreenTrackingService>(tracking => tracking.Refresh());
            Overlay.SetInteractionMode(clickThrough: false, inkVisible: true);
        }

        if (_bar is not null)
        {
            _bar.Bar.ClickThroughActive = Overlay.ClickThrough;
            _bar.Bar.RefreshSelection();
        }
        QueueDesktopSelection();
    }

    private void SaveBeforeMouseMode()
    {
        if (Services.Settings.Archive.ScreenCapture.SaveOnMouseMode) _ = SaveScreenArchiveAsync("切换鼠标模式");
        var strokes = ActiveController.Document.Strokes;
        if (strokes.Count <= Services.Settings.Automation.MinimumAutomationStrokeNumber) return;
        if (_lastSlideShowState) _pptSession.RequestScreenshot();
        else _ = ScreenshotAsync();
        _ = Services.AutoSave.SaveSnapshotAsync(strokes, _lastSlideShowState ? "ppt-mouse-mode" : "mouse-mode");
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

            if (_board is not null) ToggleBoard();
            if (_lastSlideShowState) _ = SaveScreenArchiveAsync("PPT 翻页前存档");
            bool moved = NavigateThroughSources(next);
            QueuePresentationRefresh();

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
            RefreshShortcutRegistration();
        };
        _blackScreen.Activate();
        // The window can show without owning focus, so cover that case with a global Esc that
        // only exists while the black screen is up. The window also handles Esc itself.
        RefreshShortcutRegistration();
        Core.Diagnostics.FileLog.WriteLogToFile("黑屏已开启（F9/B 或点击/Esc 恢复）", Core.Diagnostics.LogType.Info);
    }

    private void CloseBlackScreen()
    {
        var window = _blackScreen;
        _blackScreen = null;
        window?.Close();
        RefreshShortcutRegistration();
    }

    public void RotatePptNavigationCorner()
    {
        // Kept for shell action compatibility. All enabled corners are displayed together.
        UpdatePptNavigation(_pptShowSnapshot ?? PptSnapshot.Detached);
    }

    public void CollapsePptNavigation()
    {
        _pptNavCollapsed = !_pptNavCollapsed;
        var snapshot = _pptShowSnapshot;
        UpdatePptNavigation(snapshot ?? PptSnapshot.Detached);
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

    // ---- 课件页导航（批注/收藏索引） ------------------------------------

    public void ToggleSlideNavigator()
    {
        _slideNav ??= CreateSlideNavigator();
        _slideNav.Toggle();
        if (_slideNav.IsVisible) RefreshSlideIndex(_pptSession.Snapshot);
    }

    public void PptGotoSlide(int oneBasedIndex)
    {
        if (oneBasedIndex <= 0) return;
        if (_pptSession.GoToSlide(oneBasedIndex)) return;
        _ = NotifyAsync("无法跳页", "当前放映源不支持直接跳页，请用上一页/下一页；PowerPoint 放映时可正常跳转。");
    }

    private SlideNavigatorWindow CreateSlideNavigator()
    {
        var window = new SlideNavigatorWindow(Services.Settings.Appearance.Theme);
        window.SlideActivated += index => PptGotoSlide(index);
        window.FavoriteToggled += OnSlideFavoriteToggled;
        window.AiReviewRequested += OnSlideAiReviewRequested;
        return window;
    }

    private void OnSlideFavoriteToggled(int slideId)
    {
        if (_slideIndex is null || slideId <= 0) return;
        if (!_slideIndex.ToggleFavorite(slideId)) return;

        SaveSlideIndex();
        _slideNav?.Update(_slideIndex, _pptSession.Snapshot?.Slide?.SlideId ?? 0);
    }

    /// <summary>翻到有 AI 讲解的页时，从页索引取出已保存讲解并重新显示在浮窗里。</summary>
    private void OnSlideAiReviewRequested(int slideId)
    {
        var entry = _slideIndex?.Find(slideId);
        if (entry is not null) Assistant?.Review(slideId, entry.Index, entry.AnimationStep, entry.Text);
    }

    /// <summary>每 2 秒按当前页序重建索引；收藏/AI 标记从 sidecar 与内存保留。</summary>
    private void RefreshSlideIndex(PptSnapshot snapshot)
    {
        if (snapshot?.IsSlideShowRunning != true || snapshot.Presentation is null) return;

        DateTime now = DateTime.UtcNow;
        if (now - _lastSlideIndexRefreshUtc < TimeSpan.FromSeconds(2)) return;
        _lastSlideIndexRefreshUtc = now;

        try
        {
            PptPresentationDetails details = Services.Ppt.GetPresentationDetails();
            if (details?.Presentation is null) return;

            SlideContentIndex sidecar = LoadSlideIndexSidecar(details.Presentation);
            SlideContentIndex previous = sidecar ?? _slideIndex;
            _slideIndex = SlideContentIndex.Build(
                details, _pptSession.CreateArchive(), previous,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            string directory = _pptStore.GetPresentationDirectory(details.Presentation);
            foreach (SlideContentEntry entry in _slideIndex.Slides)
            {
                if (entry.SlideId <= 0 || !string.IsNullOrEmpty(entry.ThumbnailPath)) continue;
                string thumb = Path.Combine(directory, "thumb-" + entry.SlideId + ".png");
                if (File.Exists(thumb)) entry.ThumbnailPath = thumb;
            }

            int currentSlideId = snapshot.Slide?.SlideId ?? 0;
            int animationStep = Services.Ppt.GetAnimationStep();
            SlideContentEntry current = _slideIndex.Find(currentSlideId);
            if (current is not null && string.IsNullOrEmpty(current.Text))
            {
                string text = SafeSlideText(currentSlideId);
                if (!string.IsNullOrWhiteSpace(text)) current.Text = text;
            }

            if (current is not null) current.AnimationStep = animationStep;

            _teachingSession?.SetCurrentSlide(
                currentSlideId, snapshot.Slide?.Index ?? 0, animationStep, Environment.TickCount64);

            _slideNav?.Update(_slideIndex, currentSlideId);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"课件页索引刷新失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    private string SlideIndexPath(PptPresentationInfo presentation)
        => Path.Combine(_pptStore.GetPresentationDirectory(presentation), "SlideIndex.json");

    private SlideContentIndex LoadSlideIndexSidecar(PptPresentationInfo presentation)
    {
        try
        {
            string path = SlideIndexPath(presentation);
            return File.Exists(path) ? SlideContentIndex.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>无放映时读取最近一次课件的 SlideIndex.json，并把缺失缩略图按同目录 thumb-{slideId}.png 回填。</summary>
    private SlideContentIndex LoadLatestSlideIndexSidecar()
    {
        try
        {
            string root = Path.Combine(
                Services.Settings.Automation.AutoSavedStrokesLocation,
                "Auto Saved - Presentations");
            if (!Directory.Exists(root)) return null;

            var files = new DirectoryInfo(root)
                .GetFiles("SlideIndex.json", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(3)
                .ToList();

            foreach (FileInfo file in files)
            {
                SlideContentIndex index = SlideContentIndex.Deserialize(File.ReadAllText(file.FullName));
                if (index is null || index.Count == 0) continue;

                string directory = file.DirectoryName ?? root;
                foreach (SlideContentEntry entry in index.Slides)
                {
                    if (!string.IsNullOrEmpty(entry.ThumbnailPath) && File.Exists(entry.ThumbnailPath))
                        continue;

                    string thumb = Path.Combine(directory, "thumb-" + entry.SlideId + ".png");
                    if (File.Exists(thumb)) entry.ThumbnailPath = thumb;
                }

                return index;
            }

            return null;
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"读取最近课件索引失败：{ex.Message}", Core.Diagnostics.LogType.Info);
            return null;
        }
    }

    private void SaveSlideIndex()
    {
        PptPresentationInfo presentation = _pptSession.Snapshot?.Presentation;
        if (_slideIndex is null || presentation is null) return;

        try
        {
            string path = SlideIndexPath(presentation);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, _slideIndex.Serialize());
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"课件页索引保存失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }

    /// <summary>把当页截图另存为固定缩略图（thumb-{slideId}.png），供课件页导航复用。</summary>
    private void SaveSlideNavigatorThumbnail(
        PptPresentationInfo presentation, int slideId, byte[] pixels, int width, int height)
    {
        if (presentation is null || slideId <= 0) return;

        try
        {
            string directory = _pptStore.GetPresentationDirectory(presentation);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "thumb-" + slideId + ".png");
            if (!File.Exists(path)) _ = ScreenshotService.SavePngAsync(path, pixels, width, height);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"课件页缩略图保存失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
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

    public void PptExit()
    {
        _pptSession.ExitSlideShow();
        QueuePresentationRefresh();
        // MW_PPT.cs:685 HideSubPanels("cursor") after View.Exit.
        _bar?.ClosePanels();
        _palette?.Close();
        _shapePicker?.Close();
    }

    public void PptOpenNavigation()
    {
        if (!_lastSlideShowState) return;
        if (!Overlay.ClickThrough) ToggleClickThrough();
        _pptSession.ShowSlideNavigationPane();
    }

    private void SaveTeachingSession()
    {
        if (_teachingSession is null || string.IsNullOrEmpty(_teachingSession.Id)) return;

        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "InkCanvas", "teaching");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "session-" + _teachingSession.Id + ".json"),
                _teachingSession.Serialize());
        }
        catch (Exception ex)
        {
            Core.Diagnostics.FileLog.WriteLogToFile(
                $"课堂会话保存失败：{ex.Message}", Core.Diagnostics.LogType.Error);
        }
    }


    private async Task StartPracticeAsync(VariantQuestionDraft draft, bool startTimer)
    {
        if (_board is null) ToggleBoard();
        if (_board is null)
        {
            await NotifyAsync("随堂练习", "画板未能打开，请稍后重试。");
            return;
        }

        if (!Services.Board.InsertPageAfterCurrent())
        {
            await NotifyAsync("随堂练习", "无法新建画板页（可能已达页数上限）。");
            return;
        }

        int pageIndex = Services.Board.CurrentIndex;
        Services.Board.RenamePage(pageIndex, Services.Settings.Classroom.PracticePageName);

        IReadOnlyList<string> lines = ExerciseFlowPolicy.BuildPasteLines(draft);
        var (width, height) = PasteCardLayout.Estimate(lines);
        byte[] png = await PasteCardRenderer.RenderPngAsync("随堂练习", lines, width, height);
        if (png.Length == 0)
        {
            await NotifyAsync("随堂练习", "练习卡片渲染失败，已保留新页。");
            return;
        }

        var work = ScreenService.WorkArea;
        double dpi = CurrentDpi() > 0 ? CurrentDpi() : 1.0;
        double left = Math.Clamp(
            (work.Width / dpi) / 2.0 - width / 2.0, 8, Math.Max(8, work.Width / dpi - width - 8));
        double top = Math.Clamp(
            (work.Height / dpi) / 2.0 - height / 2.0, 8, Math.Max(8, work.Height / dpi - height - 8));

        string metadata = new AiPasteMetadata
        {
            Kind = AiPasteKind.Steps,
            Text = draft.Text,
            Lines = new List<string>(lines),
        }.Serialize();

        bool added = Services.Board.AddImage(new BoardImageAsset
        {
            Name = "随堂练习",
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
            await NotifyAsync("随堂练习", "练习卡片贴入失败，已保留空的新页。");
            return;
        }

        PracticePlan plan = ExerciseFlowPolicy.Plan(
            draft, pageIndex, Services.Settings.Classroom.CountdownMinutes);
        _activePractice = plan;

        ApplyPreset(ClassroomPresetLibrary.ForId("practice"));
        if (startTimer)
        {
            OpenTimer();
            _countdown?.StartCountdown(TimeSpan.FromMinutes(plan.CountdownMinutes));
        }

        _teachingSession?.Link(
            "exercise", draft.Id, draft.SlideId, draft.SlideIndex, pageIndex, draft.Text,
            Environment.TickCount64);
        SaveTeachingSession();

        await NotifyAsync("随堂练习",
            "已贴入第 " + (pageIndex + 1) + " 页"
            + (startTimer ? "，倒计时已开始（" + plan.CountdownMinutes + " 分钟）。" : "。"));
    }


    private string SafeSlideText(int slideId)
    {
        if (slideId <= 0) return "";
        try
        {
            return Services.Ppt.GetSlideText(slideId) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }


    private string CurrentPresentationName()
    {
        try
        {
            return _pptSession.Snapshot?.Presentation?.Name ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }


    private double CurrentDpi()
    {
        double dpi = Overlay.Window.DpiScale;
        return dpi > 0 ? dpi : 1.0;
    }


    public void Exit() => _ = ExitAsync(restart: false);
    public void Restart() => _ = ExitAsync(restart: true);

    private async Task ExitAsync(bool restart)
    {
        if (_exiting || _exitPromptOpen) return;
        _exitPromptOpen = true;
        try
        {
            if (Services.Settings.Advanced.IsSecondConfimeWhenShutdownApp && !_installingUpdate
                && !await ConfirmAsync(restart ? "重启 Ink Canvas" : "退出 Ink Canvas", restart ? "确认保存设置并重启 Ink Canvas？" : "确认关闭 Ink Canvas？")) return;
            if (Services.Settings.Archive.ScreenCapture.SaveOnExit)
            {
                await SaveScreenArchiveAsync(restart ? "重启前屏幕存档" : "退出前屏幕存档");
                await WaitForScreenArchivesAsync();
                if (Services.ScreenArchives.LastError is { } captureError && Services.ScreenArchives.IsEnabled(_lastSlideShowState)
                    && Services.OverlayInk.Document.Count > 0 && _board is null)
                { await NotifyAsync("截图白板存档失败", captureError); return; }
            }
            if (_documentWorkspace is not null && !await _documentWorkspace.CloseAsync()) return;
            if (Services.Settings.Archive.SaveOnExit)
            {
                try
                {
                    if (_board is not null) await _board.SaveArchiveBeforeCloseAsync(restart ? "重启程序" : "退出程序");
                    else await Services.Archives.SaveAutomaticAsync(restart ? "重启程序" : "退出程序");
                }
                catch (Exception ex)
                {
                    await NotifyAsync("白板存档失败", "白板内容已保留，请检查保存设置后再退出。\n" + ex.GetBaseException().Message);
                    return;
                }
            }
            if (restart)
            {
                Services.SaveSettings();
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "-m") { UseShellExecute = true }); }
                catch (Exception ex) { await NotifyAsync("重启失败", ex.Message); return; }
            }
            _exiting = true;
            _pollTimer.Stop();

            if (Services.Settings.Automation.IsAutoSaveStrokesAtClear)
                await Services.AutoSave.SaveSnapshotAsync(Services.OverlayInk.Document.Strokes, "exit");
            await Services.AutoSave.WaitForPendingAsync();
            await Services.Archives.WaitForPendingAsync();
            await WaitForScreenArchivesAsync();
            Services.SaveSettings();

            Dispose();
            Application.Current.Exit();
        }
        finally { _exitPromptOpen = false; }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _documentAi?.Dispose();
        StopRecording();
        _timeMachine?.Close();
        _spotlight?.Close();
        _magnifier?.Close();
        _roster?.Close();
        _toolkit?.Close();
        _groupScoreWindow?.Close();
        _exampleLibrary?.Close();
        _resourceBundle?.Close();
        _presetWindow?.Close();
        _slideNav?.Close();
        CloseBlackScreen();
        _pptSession.InkTransition -= OnPptInkTransition;
        StopShortcutTracking();
        Overlay.InteractionChanged -= RefreshShortcutRegistration;
        StopDocumentForegroundTracking();
        foreach (var notice in _notices.ToArray()) notice.Close();
        _updateWindow?.Close();
        foreach (var palette in _detachedPalettes.ToArray()) palette.Close();
        _imageImportCancellation?.Cancel();
        _videoImportCancellation?.Cancel();
        Services.Plugins.Changed -= OnExtensionCommandsChanged;
        Services.Modules.Changed -= OnExtensionCommandsChanged;
        Services.SettingsApplied -= ApplyDocumentSettings;
        Services.SettingsApplied -= ApplyArchivePaths;
        Services.SettingsApplied -= ApplyDisplaySettings;
        Services.SettingsApplied -= RefreshPaletteNibModes;
        Services.Plugins.Dispose();
        Services.Modules.Dispose();
        Services.OverlayInk.Invalidated -= QueueDesktopSelection;
        Services.OverlayInk.Invalidated -= QueueDesktopMedia;
        Services.ScreenImages.ImagesChanged -= QueueDesktopMedia;
        Services.ScreenImages.PageChanged -= QueueDesktopMedia;
        Services.ScreenImages.MediaPlacementChanged -= OnDesktopMediaPlacement;
        foreach (var video in _desktopVideos.Values) video.Close();
        _desktopVideos.Clear();
        Services.OverlayInk.SelectionChanged -= QueueDesktopSelection;
        if (_bar is not null) _bar.AppWindow.Changed -= OnBarWindowChanged;
        if (_bar is not null) _bar.Bar.LayoutChanged -= OnBarLayoutChanged;
        _selectionToolbar?.Close();
        _selectionToolbar = null;
        _replay?.Close();
        _pollTimer.Stop();
        _countdown?.Dispose();
        ClosePptNavigationWindows();
        _pptSession.Dispose();
        Services.Ppt.RefreshRequested -= QueuePresentationRefresh;
        Services.Ppt.Dispose();
        Updater.Dispose();
        Overlay.Window.DisplaySettingsChanged -= QueueDisplayRefresh;
        Overlay.Dispose();
        Services.Dispose();
    }
}
