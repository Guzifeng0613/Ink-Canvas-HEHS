using System.Runtime.InteropServices;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace InkCanvas.App.Services;

/// <summary>Dismiss transient tools even while the presentation retains foreground focus.</summary>
internal static class TransientWindowDismissal
{
    public static void Attach(Window window, Func<IntPtr> owner, Func<bool> enabled = null)
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        bool mouseDown = IsDown(1), escapeDown = IsDown(0x1B);
        bool closed = false;
        ((FrameworkElement)window.Content).Loaded += (_, _) => foreground = NativeMethods.GetForegroundWindow();
        timer.Tick += (_, _) =>
        {
            bool mouse = IsDown(1), escape = IsDown(0x1B);
            bool click = mouse && !mouseDown, dismissKey = escape && !escapeDown;
            mouseDown = mouse; escapeDown = escape;
            if (closed || enabled?.Invoke() == false || !window.AppWindow.IsVisible) return;
            var current = NativeMethods.GetForegroundWindow();
            bool movedAway = current != IntPtr.Zero && current != foreground && !BelongsTo(current, WindowChrome.GetHwnd(window), owner());
            bool outsideClick = false;
            if (click && NativeMethods.GetCursorPos(out var point))
                outsideClick = !BelongsTo(WindowFromPoint(point), WindowChrome.GetHwnd(window), owner());
            if (movedAway || outsideClick || dismissKey) window.Close();
        };
        timer.Start();
        window.Closed += (_, _) => { closed = true; timer.Stop(); };
    }

    private static bool BelongsTo(IntPtr target, IntPtr window, IntPtr owner)
    {
        if (target == IntPtr.Zero) return false;
        var root = GetAncestor(target, 2 /* GA_ROOT */);
        if (root == window || root == owner) return true;
        for (var next = NativeMethods.GetWindow(root, 4 /* GW_OWNER */); next != IntPtr.Zero; next = NativeMethods.GetWindow(next, 4))
            if (next == window || next == owner) return true;
        return false;
    }
    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativeMethods.POINT point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
