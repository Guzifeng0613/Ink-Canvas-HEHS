using InkCanvas.Platform.Interop;
using System.Runtime.InteropServices;

namespace InkCanvas.Platform.Windows;

public enum OverlayPointerKind { Pen, Touch, Mouse }

public readonly record struct OverlayPointer(
    uint Id,
    float X,
    float Y,
    float Pressure,
    float ContactWidth,
    float ContactHeight,
    OverlayPointerKind Kind,
    uint TimeMs);

/// <summary>
/// A borderless, topmost, <b>per-pixel transparent</b> overlay that receives pen/touch input.
/// <para>
/// Why not a WinUI 3 window: a WinUI 3 XAML window always paints an opaque backdrop. We
/// measured five techniques (null backdrop + transparent root, <c>WS_EX_NOREDIRECTIONBITMAP</c>
/// applied both before and after XAML init, DWM blur-behind, and two accent policies) and all
/// produced pixel-identical output to an opaque control window. A Win32 layered window fed
/// with premultiplied-alpha pixels does work, including partial alpha, so the ink surface
/// lives here while every piece of chrome is a WinUI 3 window.
/// </para>
/// <para>
/// The window is created on the UI thread, so it shares the app's existing message pump.
/// </para>
/// </summary>
public sealed class LayeredInkWindow : IDisposable
{
    private const string ClassName = "InkCanvasOverlayWindow";
    private static bool _classRegistered;
    private static bool _mouseInPointerEnabled;
    private static bool _mouseAsPointer;
    private static readonly object _classLock = new();

    // Keep the delegate alive for the lifetime of the window; the native side holds a raw pointer.
    private readonly WndProcDelegate _wndProc;
    private IntPtr _hwnd;
    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private bool _disposed;

    private delegate IntPtr WndProcDelegate(IntPtr h, uint msg, IntPtr w, IntPtr l);

    public LayeredInkWindow(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
        _wndProc = WndProc;

        lock (_classLock)
        {
            if (!_mouseInPointerEnabled)
            {
                // Without this the mouse only produces WM_MOUSE* messages, never WM_POINTER*,
                // so a WndProc that handles pointer input alone cannot draw with a mouse.
                // Pen and touch already arrive as pointer messages; this brings the mouse in line.
                NativeMethods.EnableMouseInPointer(true);
                _mouseInPointerEnabled = true;
                _mouseAsPointer = NativeMethods.IsMouseInPointerEnabled();
            }

            if (!_classRegistered)
            {
                var wc = new NativeMethods.WNDCLASS
                {
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = Marshal.GetHINSTANCE(typeof(LayeredInkWindow).Module),
                    lpszClassName = ClassName,
                    hCursor = IntPtr.Zero,
                    hbrBackground = IntPtr.Zero,
                };
                if (NativeMethods.RegisterClassW(ref wc) == 0)
                    throw new InvalidOperationException($"RegisterClass failed: {Marshal.GetLastWin32Error()}");
                _classRegistered = true;
            }
        }

        // No WS_EX_TRANSPARENT initially: we need input while writing.
        int ex = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW
               | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE;
        _hwnd = NativeMethods.CreateWindowExW(ex, ClassName, "Ink Canvas Overlay",
            NativeMethods.WS_POPUP, x, y, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        _memDc = NativeMethods.CreateCompatibleDC(screen);
        var bi = new NativeMethods.BITMAPINFO
        {
            biSize = Marshal.SizeOf<NativeMethods.BITMAPINFO>(),
            biWidth = width,
            biHeight = -height,   // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };
        _dib = NativeMethods.CreateDIBSection(screen, ref bi, 0, out _bits, IntPtr.Zero, 0);
        _oldBitmap = NativeMethods.SelectObject(_memDc, _dib);
        NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        if (_bits == IntPtr.Zero)
            throw new InvalidOperationException("CreateDIBSection failed");
    }

    public IntPtr Handle => _hwnd;
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Writable premultiplied-BGRA surface, <c>Width*Height*4</c> bytes, top-down.</summary>
    public IntPtr PixelBuffer => _bits;

    public bool IsVisible { get; private set; }

    /// <summary>Diagnostics: how many input messages actually reached this window.</summary>
    public long PointerMessageCount { get; private set; }

    public long PointerDownCount { get; private set; }

    public long PointerMoveCount { get; private set; }

    public long PointerUpCount { get; private set; }

    public long MouseMessageCount { get; private set; }

    /// <summary>Whether the mouse is being delivered as WM_POINTER (vs WM_MOUSE fallback).</summary>
    public static bool MouseAsPointer => _mouseAsPointer;

    /// <summary>When true, clicks fall through to whatever is underneath (screen-pen idle mode).</summary>
    public bool ClickThrough
    {
        get => NativeMethods.HasExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        set
        {
            if (value) NativeMethods.AddExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
            else NativeMethods.RemoveExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        }
    }

    public bool Topmost
    {
        get => NativeMethods.HasExStyle(_hwnd, NativeMethods.WS_EX_TOPMOST);
        set => NativeMethods.SetWindowPos(_hwnd,
            value ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public event Action<OverlayPointer> PointerDown;
    public event Action<OverlayPointer> PointerMove;
    public event Action<OverlayPointer> PointerUp;

    /// <summary>Raised for WM_HOTKEY; the argument is the registration id.</summary>
    public event Action<int> HotkeyPressed;

    public void Show()
    {
        if (IsVisible) return;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        IsVisible = true;
    }

    public void Hide()
    {
        if (!IsVisible) return;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        IsVisible = false;
    }

    /// <summary>Push the current pixel buffer to the screen with per-pixel alpha.</summary>
    public void Present()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        var dst = new NativeMethods.POINT { X = X, Y = Y };
        var src = new NativeMethods.POINT { X = 0, Y = 0 };
        var size = new NativeMethods.SIZE { cx = Width, cy = Height };
        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = 0,               // AC_SRC_OVER
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,           // AC_SRC_ALPHA -> honour per-pixel alpha
        };
        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        NativeMethods.UpdateLayeredWindow(_hwnd, screen, ref dst, ref size, _memDc, ref src, 0,
            ref blend, NativeMethods.ULW_ALPHA);
        NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        if (!IsVisible) Show();
    }

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case NativeMethods.WM_POINTERDOWN:
                PointerMessageCount++; PointerDownCount++;
                Raise(PointerDown, w);
                return IntPtr.Zero;
            case NativeMethods.WM_POINTERUPDATE:
                PointerMessageCount++; PointerMoveCount++;
                Raise(PointerMove, w);
                return IntPtr.Zero;
            case NativeMethods.WM_POINTERUP:
            case NativeMethods.WM_POINTERCAPTURECHANGED:
                PointerMessageCount++; PointerUpCount++;
                Raise(PointerUp, w);
                return IntPtr.Zero;
            case NativeMethods.WM_HOTKEY:
                HotkeyPressed?.Invoke(w.ToInt32());
                return IntPtr.Zero;

            // Fallback for the case where mouse-in-pointer could not be enabled: without it the
            // mouse never produces pointer messages and drawing would silently do nothing.
            case NativeMethods.WM_LBUTTONDOWN when !_mouseAsPointer:
                RaiseMouse(PointerDown, l, mouseDown: true);
                return IntPtr.Zero;
            case NativeMethods.WM_MOUSEMOVE when !_mouseAsPointer:
                if ((w.ToInt64() & 0x0001) != 0) RaiseMouse(PointerMove, l, mouseDown: false);
                return IntPtr.Zero;
            case NativeMethods.WM_LBUTTONUP when !_mouseAsPointer:
                RaiseMouse(PointerUp, l, mouseDown: false);
                return IntPtr.Zero;
            case NativeMethods.WM_DESTROY:
                return IntPtr.Zero;
        }
        return NativeMethods.DefWindowProcW(h, msg, w, l);
    }

    private void RaiseMouse(Action<OverlayPointer> handler, IntPtr lParam, bool mouseDown)
    {
        MouseMessageCount++;
        if (handler is null) return;
        if (mouseDown) NativeMethods.SetCapture(Handle);
        else NativeMethods.ReleaseCapture();
        int raw = lParam.ToInt32();
        short x = (short)(raw & 0xFFFF);
        short y = (short)((raw >> 16) & 0xFFFF);
        handler(new OverlayPointer(1, x, y, 0.5f, 0, 0, OverlayPointerKind.Mouse, (uint)Environment.TickCount));
    }

    private void Raise(Action<OverlayPointer> handler, IntPtr wParam)
    {
        if (handler is null) return;
        uint id = (uint)(wParam.ToInt64() & 0xFFFF);
        if (!NativeMethods.GetPointerInfo(id, out var info)) return;

        float pressure = 0.5f, cw = 0, ch = 0;
        var kind = OverlayPointerKind.Mouse;

        if (info.pointerType == NativeMethods.POINTER_INPUT_TYPE_PEN)
        {
            kind = OverlayPointerKind.Pen;
            if (NativeMethods.GetPointerPenInfo(id, out var pen))
            {
                // Pen pressure is reported 0..1024; 0 means "no pressure data".
                pressure = pen.pressure == 0 ? 0.5f : Math.Clamp(pen.pressure / 1024f, 0f, 1f);
            }
        }
        else if (info.pointerType == NativeMethods.POINTER_INPUT_TYPE_TOUCH)
        {
            kind = OverlayPointerKind.Touch;
            if (NativeMethods.GetPointerTouchInfo(id, out var touch))
            {
                cw = touch.rcContact.Right - touch.rcContact.Left;
                ch = touch.rcContact.Bottom - touch.rcContact.Top;
                pressure = touch.pressure == 0 ? 0.5f : Math.Clamp(touch.pressure / 1024f, 0f, 1f);
            }
        }

        var pt = info.ptPixelLocation;
        handler(new OverlayPointer(id, pt.X - X, pt.Y - Y, pressure, cw, ch, kind, info.dwTime));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_memDc != IntPtr.Zero)
        {
            NativeMethods.SelectObject(_memDc, _oldBitmap);
            NativeMethods.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero) { NativeMethods.DeleteObject(_dib); _dib = IntPtr.Zero; }
        if (_hwnd != IntPtr.Zero) { NativeMethods.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}