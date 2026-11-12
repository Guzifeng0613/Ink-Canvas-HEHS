using InkCanvas.Platform.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace InkCanvas.App.Services;

/// <summary>
/// Window chrome helpers shared by every WinUI 3 window in the app. All chrome windows are
/// borderless and optionally topmost so they can sit over the transparent ink overlay.
/// </summary>
public static class WindowChrome
{
    public static IntPtr GetHwnd(Window window) => WindowNative.GetWindowHandle(window);

    public static void MakeBorderless(Window window, bool topmost = true, bool resizable = false)
    {
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = resizable;
            presenter.IsMaximizable = resizable;
            presenter.IsMinimizable = resizable;
            presenter.IsAlwaysOnTop = topmost;
        }
        // No system backdrop: these windows paint their own tokenised surface.
        window.SystemBackdrop = null;
    }

    public static void Place(Window window, int x, int y, int width, int height)
        => window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

    /// <summary>0 = follow system, 1 = light, 2 = dark.</summary>
    public static void ApplyTheme(Window window, int theme)
    {
        if (window.Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    /// <summary>
    /// Re-assert topmost without activating. Both the overlay and the chrome are topmost, so
    /// this keeps the chrome above the ink surface without stealing focus from a slideshow.
    /// </summary>
    public static void EnsureAbove(Window window)
    {
        var hwnd = GetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public static void CenterOnPrimary(Window window, int width, int height)
    {
        var screen = Platform.System.ScreenService.Primary;
        Place(window, screen.X + (screen.Width - width) / 2,
                      screen.Y + (screen.Height - height) / 2, width, height);
    }
}