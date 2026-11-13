using System.Runtime.InteropServices;
using System.Text;

namespace InkCanvas.Platform.System;

/// <summary>
/// Temporarily disables Windows edge swipe gestures for a fullscreen window by setting the
/// shell property <c>System.EdgeGesture.DisableTouchWhenFullscreen</c>.
/// <para>
/// Ported from Ink Canvas Artistry's <c>EdgeGestureUtil</c>, which credits the
/// InkCanvasForClass project (https://github.com/kriastans/InkCanvasForClass).
/// The original set the value without calling <c>Commit</c>; the property store requires a
/// commit for the change to take effect, so this port commits.
/// </para>
/// </summary>
public static class EdgeGestureService
{
    private static readonly Guid DisableTouchWhenFullscreen = new("32CE38B2-2C9A-41B1-9BC5-B3784394AA44");
    private static readonly Guid IPropertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private const ushort VT_BOOL = 11;
    private const uint PropertyId = 2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public short boolVal;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr handle, ref Guid riid, out IPropertyStore store);

    /// <summary>Returns true when the property was applied. Never throws.</summary>
    public static bool SetEdgeGesturesDisabled(IntPtr hwnd, bool disabled)
    {
        if (hwnd == IntPtr.Zero) return false;
        IPropertyStore store = null;
        try
        {
            var iid = IPropertyStoreIid;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store is null) return false;

            var key = new PropertyKey { fmtid = DisableTouchWhenFullscreen, pid = PropertyId };
            // VARIANT_TRUE is -1, not 1.
            var value = new PropVariant { vt = VT_BOOL, boolVal = (short)(disabled ? -1 : 0) };
            store.SetValue(ref key, ref value);
            store.Commit();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (store is not null) Marshal.FinalReleaseComObject(store);
        }
    }
}

/// <summary>
/// Top-level window lookup by title. Replaces the original UI-Automation-based
/// <c>WinTabWindowsChecker</c>, which needed the WPF-only UIAutomation assemblies.
/// </summary>
public static class WindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

    /// <summary>Titles of visible top-level windows.</summary>
    public static List<string> VisibleWindowTitles()
    {
        var titles = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            if (sb.Length > 0) titles.Add(sb.ToString());
            return true;
        }, IntPtr.Zero);
        return titles;
    }

    /// <summary>True when a visible window's title matches, exactly or by substring.</summary>
    public static bool WindowExists(string title, bool matchFullName = true)
    {
        if (string.IsNullOrEmpty(title)) return false;
        foreach (var t in VisibleWindowTitles())
        {
            if (matchFullName ? t == title : t.Contains(title, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>True when a matching window exists but is minimised.</summary>
    public static bool IsWindowMinimised(string title)
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(512);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            if (sb.Length > 0 && sb.ToString().Contains(title, StringComparison.OrdinalIgnoreCase) && IsIconic(hwnd))
            {
                found = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}