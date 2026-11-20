using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.System;
using InkCanvas.Platform.Windows;

namespace InkCanvas.App.Services;

/// <summary>
/// Owns the transparent screen-annotation surface: the layered window, its presenter and the
/// global hotkeys registered against it.
/// </summary>
public sealed class OverlayHost : IDisposable
{
    private Action _undo, _redo, _togglePen, _toggleEraser, _clear;
    private Action _togglePassThrough, _toggleBoard;
    private Action _pptNext, _pptPrevious, _pptExit, _toggleBlackScreen;
    private bool _presentationKeysEnabled;

    public OverlayHost(InkController controller)
    {
        var screen = ScreenService.Primary;
        Window = new LayeredInkWindow(screen.X, screen.Y, screen.Width, screen.Height);
        Presenter = new OverlayInkPresenter(Window, controller);
        Hotkeys = new HotkeyService(Window.Handle);
        Controller = controller;
        controller.ViewCenter = new System.Numerics.Vector2(
            screen.Width / Window.DpiScale / 2f, screen.Height / Window.DpiScale / 2f);

        Window.HotkeyPressed += id => Hotkeys.Dispatch(id);
        Presenter.Attach();
    }

    public LayeredInkWindow Window { get; }

    public OverlayInkPresenter Presenter { get; }

    public HotkeyService Hotkeys { get; }

    public InkController Controller { get; }

    public bool ClickThrough
    {
        get => Window.ClickThrough;
        set => SetInteractionMode(value, inkVisible: !value);
    }

    public void SetInteractionMode(bool clickThrough, bool inkVisible)
    {
        Window.ClickThrough = clickThrough;
        Presenter.InkVisible = inkVisible;
    }

    public void Show()
    {
        Window.Show();
        Presenter.Attach();
    }

    public void Hide() => Window.Hide();

    /// <summary>
    /// Re-anchors the ink surface to a slideshow window. While a show runs the visible
    /// picture can sit on a different monitor than the primary one, so both the bounds and
    /// the world centre have to follow it.
    /// </summary>
    public void AlignTo(ShowWindowInfo bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Window.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Controller.ViewCenter = new System.Numerics.Vector2(
            Window.Width / Window.DpiScale / 2f, Window.Height / Window.DpiScale / 2f);
        if (Window.IsVisible) Presenter.Attach();
    }

    /// <summary>Returns the ink surface to the primary screen once the show ends.</summary>
    public void ResetToPrimary()
    {
        var screen = ScreenService.Primary;
        Window.SetBounds(screen.X, screen.Y, screen.Width, screen.Height);
        Controller.ViewCenter = new System.Numerics.Vector2(
            Window.Width / Window.DpiScale / 2f, Window.Height / Window.DpiScale / 2f);
        if (Window.IsVisible) Presenter.Attach();
    }

    /// <summary>Register the default hotkey set. Failures are non-fatal (key may be taken).</summary>
    public void RegisterHotkeys(Action undo, Action redo, Action togglePen, Action toggleEraser, Action clear,
        Action togglePassThrough = null, Action toggleBoard = null,
        Action pptNext = null, Action pptPrevious = null, Action pptExit = null,
        Action toggleBlackScreen = null)
    {
        _undo = undo;
        _redo = redo;
        _togglePen = togglePen;
        _toggleEraser = toggleEraser;
        _clear = clear;
        _togglePassThrough = togglePassThrough;
        _toggleBoard = toggleBoard;
        _pptNext = pptNext;
        _pptPrevious = pptPrevious;
        _pptExit = pptExit;
        _toggleBlackScreen = toggleBlackScreen;
        RegisterBaselineHotkeys();
        if (_presentationKeysEnabled) RegisterPresentationHotkeys();
    }

    /// <summary>
    /// Registers only the keys that are valid during a slide show (Page Up/Down, Esc, B). They
    /// are deliberately absent outside a show so a browser or video player keeps its keys.
    /// Re-registering the baseline set is required because the Win32 hotkey service has no
    /// per-key removal; this runs only on show start/end.
    /// </summary>
    public bool SetPresentationKeysEnabled(bool enabled)
    {
        if (_presentationKeysEnabled == enabled) return true;
        _presentationKeysEnabled = enabled;
        Hotkeys.UnregisterAll();
        RegisterBaselineHotkeys();
        return enabled ? RegisterPresentationHotkeys() : true;
    }

    private void RegisterBaselineHotkeys()
    {
        Hotkeys.Register(HotkeyModifiers.Control, 0x5A /* Z */, _undo);
        Hotkeys.Register(HotkeyModifiers.Control, 0x59 /* Y */, _redo);
        Hotkeys.Register(HotkeyModifiers.Alt, 0x31 /* 1 */, _togglePen);
        Hotkeys.Register(HotkeyModifiers.Alt, 0x45 /* E */, _toggleEraser);
        Hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x43 /* C */, _clear);
        // Escape hatches: the overlay captures the whole screen while writing, so these must
        // work even if the bar is unreachable.
        if (_togglePassThrough is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x50 /* P */, _togglePassThrough);
        if (_toggleBoard is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x42 /* B */, _toggleBoard);
        // F9 is the global black-screen toggle and stays available outside a show.
        if (_toggleBlackScreen is not null) Hotkeys.Register(HotkeyModifiers.None, 0x78 /* F9 */, _toggleBlackScreen);
    }

    private bool RegisterPresentationHotkeys()
    {
        bool ok = true;
        if (_pptNext is not null) ok &= Hotkeys.Register(HotkeyModifiers.None, 0x22 /* PageDown */, _pptNext);
        if (_pptPrevious is not null) ok &= Hotkeys.Register(HotkeyModifiers.None, 0x21 /* PageUp */, _pptPrevious);
        if (_pptExit is not null) ok &= Hotkeys.Register(HotkeyModifiers.None, 0x1B /* Escape */, _pptExit);
        if (_toggleBlackScreen is not null) ok &= Hotkeys.Register(HotkeyModifiers.None, 0x42 /* B */, _toggleBlackScreen);
        return ok;
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Presenter.Dispose();
        Window.Dispose();
    }
}