using InkCanvas.Platform.Interop;
using System.Diagnostics;
using Microsoft.Win32;

namespace InkCanvas.Platform.System;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height);

public static class ScreenService
{
    public static ScreenRect Primary => new(0, 0,
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));

    public static ScreenRect Virtual => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

    /// <summary>
    /// Primary screen minus the taskbar / docked bars. Windows that carry their own bottom
    /// toolbar must use this, otherwise the toolbar ends up behind the taskbar.
    /// </summary>
    public static ScreenRect WorkArea
    {
        get
        {
            var rect = new NativeMethods.RECT();
            if (NativeMethods.SystemParametersInfoW(NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0))
                return new ScreenRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return Primary;
        }
    }
}

/// <summary>Identifies the foreground application, used by the auto-fold rules.</summary>
public static class ForegroundWindowService
{
    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public static string GetForegroundProcessName()
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return string.Empty;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return string.Empty;
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName;
        }
        catch
        {
            // The foreground window can vanish between the two calls; not an error.
            return string.Empty;
        }
    }
}

/// <summary>Per-user run-at-logon entry (no elevation required).</summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "InkCanvasWinUI";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue(ValueName, $"\"{executablePath}\"");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* registry may be locked down by policy; leave state unchanged */ }
    }
}