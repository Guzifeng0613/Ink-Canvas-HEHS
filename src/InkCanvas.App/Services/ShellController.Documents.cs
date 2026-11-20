using InkCanvas.Host.Contracts;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;

namespace InkCanvas.App.Services;

public sealed partial class ShellController
{
    private IDocumentWorkspace _documentWorkspace;
    private readonly List<Window> _documentWindows = new();
    private bool DocumentActive => _documentWorkspace is not null && _board is null;
    internal IDocumentWorkspaceHost WorkspaceHost => new DocumentHost(this);
    private void ApplyDocumentSettings() => _documentWorkspace?.ApplySettings();
    private void BindToolbar(InkController controller)
    {
        if (_bar is null) return;
        _bar.Bar.GestureSettingsOverride = DocumentActive ? _documentWorkspace.Gestures : null;
        _bar.Bar.SupportsViewRotation = !DocumentActive || _documentWorkspace.SupportsViewRotation;
        _bar.Bar.Controller = controller;
        _bar.Bar.RefreshSettingsVisuals();
        CanvasToolTargetChanged?.Invoke();
    }

    public async Task OpenPdfAsync(string path = null)
    {
        var service = Services.Modules.GetExport<IPdfWorkspaceService>();
        if (service is null) { await NotifyAsync("PDF 模块", "请将 PDF 模块包解压到程序的 Modules 目录，重新启动后使用。"); return; }
        try { await service.OpenAsync(path); }
        catch (Exception ex) { await NotifyAsync("打开 PDF 失败", ex.GetBaseException().Message); }
    }

    private void AttachDocument(IDocumentWorkspace workspace)
    {
        if (_board is not null) _board.Close();
        _replay?.Close(); _palette?.Close(); _shapePicker?.Close(); _bar?.ClosePanels();
        _selectionToolbar?.Hide();
        _documentWorkspace = workspace;
        _lastSlideShowState = false;
        _bar?.Bar.SetPptModeVisible(false);
        ClosePptNavigationWindows();
        workspace.ControllerChanged += OnDocumentPageChanged;
        workspace.InputBegan += OnBoardInputBegan;
        workspace.Window.Activated += OnDocumentActivated;
        OnDocumentPageChanged();
        Overlay.Hide();
        if (_bar is not null && !_bar.IsFolded) WindowChrome.ShowWithoutActivation(_bar);
        Services.Modules.Invoke<IScreenTrackingService>(tracking => tracking.Refresh());
    }
    private void OnDocumentActivated(object sender, WindowActivatedEventArgs args)
    {
        if (DocumentActive && args.WindowActivationState != WindowActivationState.Deactivated) RaiseChromeAboveOverlay();
    }

    private void OnDocumentPageChanged()
    {
        _replay?.Close(); _palette?.Close(); _shapePicker?.Close();
        if (!DocumentActive) return;
        BindBoardController(_documentWorkspace.Controller);
        BindToolbar(_documentWorkspace.Controller);
        OnControllerToolChanged();
    }

    private void DetachDocument(IDocumentWorkspace workspace)
    {
        if (!ReferenceEquals(_documentWorkspace, workspace)) return;
        workspace.ControllerChanged -= OnDocumentPageChanged;
        workspace.InputBegan -= OnBoardInputBegan;
        workspace.Window.Activated -= OnDocumentActivated;
        _documentWorkspace = null;
        _replay?.Close(); _palette?.Close(); _shapePicker?.Close();
        BindBoardController(_board?.CurrentController);
        BindToolbar(ActiveController);
        OnControllerToolChanged();
        if (_board is null && !_exiting) Overlay.Show();
        Services.Modules.Invoke<IScreenTrackingService>(tracking => tracking.Refresh());
    }

    private sealed class DocumentHost(ShellController shell) : IDocumentWorkspaceHost
    {
        public Window Owner => shell.DocumentActive ? shell._documentWorkspace.Window : (Window)shell._board ?? shell._bar;
        public Core.Settings.AppSettings Settings => shell.Services.Settings;
        public Core.Ink.Rect ToolbarScreenBounds => shell.DesktopToolbarBounds;
        public Features.Board.BoardService Board => shell.Services.Board;
        public void ConfigureWindow(Window window, int width = 1120, int height = 820)
        {
            shell._documentWindows.Add(window);
            window.Closed += (_, _) => shell._documentWindows.Remove(window);
            shell.NotifyWindowOpened(window);
            WindowChrome.MakeStandard(window, width, height);
            WindowChrome.ApplyTheme(window, Settings.Appearance.Theme);
        }
        public void ConfigureController(InkController controller) => shell.Services.ConfigureController(controller);
        public void Attach(IDocumentWorkspace workspace) => shell.AttachDocument(workspace);
        public void Detach(IDocumentWorkspace workspace) => shell.DetachDocument(workspace);
        public void ShowBoard() { if (shell._board is null) shell.ToggleBoard(); }
    }
}
