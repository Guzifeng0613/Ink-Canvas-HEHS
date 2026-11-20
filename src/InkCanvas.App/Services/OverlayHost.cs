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
        if (clickThrough || !inkVisible) Controller.CancelLassoErase();
        if (clickThrough || !inkVisible) Controller.HideEraserPreview();
        Window.ClickThrough = clickThrough;
        Presenter.InkVisible = inkVisible;
    }

    public void Show()
    {
        Window.Show();
        Presenter.Attach();
    }

    public void Hide()
    {
        Controller.CancelLassoErase();
        Presenter.Suspend();
        Window.Hide();
    }

    /// <summary>
    /// Register the WPF guide's hotkey set (OperatingGuideWindow rows): Ctrl+Z/Y undo/redo,
    /// Ctrl+E clear, Alt+C screenshot, Alt+V fold, Alt+D draw/palette, Alt+Q mouse mode,
    /// Alt+B board, Alt+S select, Alt+E eraser, Alt+L line, Shift+Esc exit slideshow.
    /// PageUp/PageDown remain as slideshow conveniences (documented in ppt-porting-notes).
    /// Failures are non-fatal (key may be taken).
    /// </summary>
    public void RegisterHotkeys(Action undo, Action redo, Action toggleEraser, Action clear,
        Action togglePassThrough = null, Action toggleBoard = null,
        Action pptNext = null, Action pptPrevious = null, Action pptExit = null,
        Action screenshot = null, Action fold = null, Action select = null, Action line = null, Action draw = null)
    {
        Hotkeys.Register(HotkeyModifiers.Control, 0x5A /* Z */, undo);
        Hotkeys.Register(HotkeyModifiers.Control, 0x59 /* Y */, redo);
        Hotkeys.Register(HotkeyModifiers.Control, 0x45 /* E */, clear);
        Hotkeys.Register(HotkeyModifiers.Alt, 0x45 /* E */, toggleEraser);
        if (select is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x53 /* S */, select);
        if (line is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x4C /* L */, line);
        if (toggleBoard is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x42 /* B */, toggleBoard);
        if (fold is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x56 /* V */, fold);
        if (screenshot is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x43 /* C */, screenshot);
        if (draw is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x44 /* D */, draw);
        // Escape hatch: the overlay captures the whole screen while writing, so mouse mode must
        // work even if the bar is unreachable.
        if (togglePassThrough is not null) Hotkeys.Register(HotkeyModifiers.Alt, 0x51 /* Q */, togglePassThrough);
        if (pptNext is not null) Hotkeys.Register(HotkeyModifiers.None, 0x22 /* PageDown */, pptNext);
        if (pptPrevious is not null) Hotkeys.Register(HotkeyModifiers.None, 0x21 /* PageUp */, pptPrevious);
        if (pptExit is not null) Hotkeys.Register(HotkeyModifiers.Shift, 0x1B /* Escape */, pptExit);
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Presenter.Dispose();
        Window.Dispose();
    }
}
