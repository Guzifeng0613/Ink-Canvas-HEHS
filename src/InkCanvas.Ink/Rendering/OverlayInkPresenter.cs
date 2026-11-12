using Microsoft.Graphics.Canvas;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using InkCanvas.Platform.Windows;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Renders the ink scene into a premultiplied-alpha Win2D target and pushes it to a
/// <see cref="LayeredInkWindow"/>. This is the transparent screen-annotation path.
/// </summary>
public sealed class OverlayInkPresenter : IDisposable
{
    private readonly LayeredInkWindow _window;
    private readonly InkController _controller;
    private readonly CanvasDevice _device;
    private CanvasRenderTarget _target;
    private bool _rendering;
    private bool _pending;
    private bool _attached;

    public OverlayInkPresenter(LayeredInkWindow window, InkController controller)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _device = CanvasDevice.GetSharedDevice();
        _target = CreateTarget();
        Scene = new InkSceneRenderer();
    }

    public InkSceneRenderer Scene { get; }

    /// <summary>Frames actually pushed to the screen; useful for diagnostics.</summary>
    public long PresentedFrames { get; private set; }

    private CanvasRenderTarget CreateTarget() => new(
        _device, _window.Width, _window.Height, 96,
        DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _window.PointerDown += OnDown;
        _window.PointerMove += OnMove;
        _window.PointerUp += OnUp;
        _controller.Invalidated += RequestRender;
        _controller.Document.Changed += OnDocumentChanged;
        RequestRender();
    }

    public void Detach()
    {
        if (!_attached) return;
        _attached = false;
        _window.PointerDown -= OnDown;
        _window.PointerMove -= OnMove;
        _window.PointerUp -= OnUp;
        _controller.Invalidated -= RequestRender;
        _controller.Document.Changed -= OnDocumentChanged;
    }

    private void OnDown(OverlayPointer p) => _controller.OnPointerDown(p);
    private void OnMove(OverlayPointer p) => _controller.OnPointerMove(p);
    private void OnUp(OverlayPointer p) => _controller.OnPointerUp(p);

    private void OnDocumentChanged()
    {
        // Strokes may have been removed; drop their cached geometry.
        Scene.Strokes.InvalidateAll();
        RequestRender();
    }

    /// <summary>
    /// Coalesces invalidations: if a render is already running, mark pending and let it
    /// loop once more rather than recursing through the drawing session.
    /// </summary>
    private void RequestRender()
    {
        if (_rendering) { _pending = true; return; }
        _rendering = true;
        try
        {
            do
            {
                _pending = false;
                RenderOnce();
            } while (_pending);
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RenderOnce()
    {
        if (_target is null || _window.PixelBuffer == IntPtr.Zero) return;

        using (var session = _target.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            Scene.Draw(session, _controller);
        }

        byte[] pixels = _target.GetPixelBytes();
        Marshal.Copy(pixels, 0, _window.PixelBuffer, pixels.Length);
        _window.Present();
        PresentedFrames++;
    }

    public void Dispose()
    {
        Detach();
        Scene.Dispose();
        _target?.Dispose();
        _target = null;
    }
}