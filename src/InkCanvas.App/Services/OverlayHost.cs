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
        controller.ViewCenter = new System.Numerics.Vector2(screen.Width / 2f, screen.Height / 2f);

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
        set => Window.ClickThrough = value;
    }

    public void Show()
    {
        Window.Show();
        Presenter.Attach();
    }

    public void Hide() => Window.Hide();

    /// <summary>Register the default hotkey set. Failures are non-fatal (key may be taken).</summary>
    public void RegisterHotkeys(Action undo, Action redo, Action togglePen, Action toggleEraser, Action clear)
    {
        Hotkeys.Register(HotkeyModifiers.Control, 0x5A /* Z */, undo);
        Hotkeys.Register(HotkeyModifiers.Control, 0x59 /* Y */, redo);
        Hotkeys.Register(HotkeyModifiers.Alt, 0x31 /* 1 */, togglePen);
        Hotkeys.Register(HotkeyModifiers.Alt, 0x45 /* E */, toggleEraser);
        Hotkeys.Register(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x43 /* C */, clear);
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Presenter.Dispose();
        Window.Dispose();
    }
}