using System.Runtime.InteropServices;

namespace InkCanvas.Features.Ppt;

/// <summary>
/// PowerPoint / WPS Presentation control through COM.
/// <para>
/// Uses <c>dynamic</c> late binding instead of the Office PIAs the WPF build referenced, so
/// there is no interop assembly to ship and no Office version lock. <c>Marshal.GetActiveObject</c>
/// does not exist on modern .NET, so the running-object-table lookup is done via P/Invoke.
/// </para>
/// </summary>
public sealed class PptInteropService
{
    [DllImport("ole32.dll")] private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);
    [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, out IntPtr punk);

    private object _app;
    private string _progId;

    /// <summary>ProgIDs tried in order; WPS Presentation registers KWPP.Application.</summary>
    public string[] ProgIds { get; set; } = { "PowerPoint.Application", "KWPP.Application" };

    public bool IsAttached => _app is not null;

    public string AttachedProgId => _progId;

    public bool AllowWps { get; set; } = true;

    /// <summary>Try to bind to a running presentation app. Never throws.</summary>
    public bool TryAttach()
    {
        foreach (var progId in ProgIds)
        {
            if (!AllowWps && progId.StartsWith("KWPP", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (CLSIDFromProgID(progId, out var clsid) != 0) continue;
                if (GetActiveObject(ref clsid, IntPtr.Zero, out IntPtr punk) != 0 || punk == IntPtr.Zero) continue;
                var obj = Marshal.GetObjectForIUnknown(punk);
                Marshal.Release(punk);
                _app = obj;
                _progId = progId;
                return true;
            }
            catch
            {
                // Not running, or not registered — try the next candidate.
            }
        }
        _app = null;
        _progId = null;
        return false;
    }

    public void Detach()
    {
        _app = null;
        _progId = null;
    }

    private dynamic App => _app;

    /// <summary>True when a slideshow is currently running.</summary>
    public bool IsSlideShowRunning()
    {
        if (!IsAttached) return false;
        try
        {
            var windows = App.SlideShowWindows;
            return windows is not null && (int)windows.Count > 0;
        }
        catch { return false; }
    }

    public bool Next()
    {
        try
        {
            if (!IsSlideShowRunning()) return false;
            App.SlideShowWindows[1].View.Next();
            return true;
        }
        catch { return false; }
    }

    public bool Previous()
    {
        try
        {
            if (!IsSlideShowRunning()) return false;
            App.SlideShowWindows[1].View.Previous();
            return true;
        }
        catch { return false; }
    }

    public bool ExitSlideShow()
    {
        try
        {
            if (!IsSlideShowRunning()) return false;
            App.SlideShowWindows[1].Exit();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Current slide number within the running slideshow, or 0 when unavailable.</summary>
    public int CurrentSlideIndex()
    {
        try
        {
            if (!IsSlideShowRunning()) return 0;
            return (int)App.SlideShowWindows[1].View.CurrentShowPosition;
        }
        catch { return 0; }
    }

    public int SlideCount()
    {
        try
        {
            if (!IsSlideShowRunning()) return 0;
            return (int)App.SlideShowWindows[1].Presentation.Slides.Count;
        }
        catch { return 0; }
    }

    /// <summary>Polls for slideshow start/stop so the shell can switch modes.</summary>
    public bool PollSlideShowState(ref bool lastKnown)
    {
        bool now = IsSlideShowRunning();
        if (now == lastKnown) return false;
        lastKnown = now;
        return true;
    }
}