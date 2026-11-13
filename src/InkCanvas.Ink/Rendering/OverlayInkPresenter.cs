using InkCanvas.Core.Imaging;
using InkCanvas.Core.Ink;
using Microsoft.Graphics.Canvas;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX;
using InkCanvas.Platform.Windows;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Renders the ink scene into a premultiplied-alpha surface and pushes it to a
/// <see cref="LayeredInkWindow"/>. This is the transparent screen-annotation path.
/// <para>
/// Performance: the committed layer is rendered and read back from the GPU only when the
/// document, selection or view actually changes. Per frame we memcpy that cache and read back
/// just the bounding box of the in-progress stroke, compositing it with
/// <see cref="PixelCompositing"/>. That turns an ~8 MB per-frame GPU readback at 1080p into a
/// few tens of KB while drawing.
/// </para>
/// </summary>
public sealed class OverlayInkPresenter : IDisposable
{
    private readonly LayeredInkWindow _window;
    private readonly InkController _controller;
    private readonly CanvasDevice _device;

    private CanvasRenderTarget _committedTarget;
    private byte[] _committedPixels;
    private byte[] _frame;
    private bool _committedValid;
    private Matrix3x2 _lastView;
    private Rect _lastSelectionBounds;
    private int _lastSelectionCount;

    private CanvasRenderTarget _liveTarget;
    private int _liveWidth, _liveHeight;

    private bool _rendering;
    private bool _pending;
    private bool _attached;

    public OverlayInkPresenter(LayeredInkWindow window, InkController controller)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _device = CanvasDevice.GetSharedDevice();

        _committedTarget = CreateTarget(window.Width, window.Height);
        _committedPixels = new byte[window.Width * window.Height * 4];
        _frame = new byte[_committedPixels.Length];
        Scene = new InkSceneRenderer();
    }

    public InkSceneRenderer Scene { get; }

    /// <summary>Frames actually pushed to the screen; useful for diagnostics.</summary>
    public long PresentedFrames { get; private set; }

    /// <summary>Total bytes read back from the GPU; lets the self-test prove the optimisation.</summary>
    public long GpuReadbackBytes { get; private set; }

    private CanvasRenderTarget CreateTarget(int width, int height) => new(
        _device, width, height, 96,
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
        _controller.SelectionChanged += InvalidateCommitted;
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
        _controller.SelectionChanged -= InvalidateCommitted;
    }

    private void OnDown(OverlayPointer p) => _controller.OnPointerDown(p);
    private void OnMove(OverlayPointer p) => _controller.OnPointerMove(p);
    private void OnUp(OverlayPointer p) => _controller.OnPointerUp(p);

    private void OnDocumentChanged()
    {
        Scene.Strokes.InvalidateAll();
        InvalidateCommitted();
    }

    private void InvalidateCommitted()
    {
        _committedValid = false;
        RequestRender();
    }

    /// <summary>
    /// Coalesces invalidations: if a render is already running, mark pending and let it loop
    /// once more rather than recursing through the drawing session.
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
        if (_committedTarget is null || _window.PixelBuffer == IntPtr.Zero) return;

        EnsureCommittedCache();
        Array.Copy(_committedPixels, _frame, _frame.Length);

        var region = LiveRegion();
        if (region.Width > 0 && region.Height > 0)
            CompositeLiveRegion(region);

        Marshal.Copy(_frame, 0, _window.PixelBuffer, _frame.Length);
        _window.Present();
        PresentedFrames++;
    }

    private void EnsureCommittedCache()
    {
        var selectionBounds = _controller.SelectionBounds;
        bool viewChanged = _lastView != _controller.ViewTransform;
        bool selectionChanged = _lastSelectionCount != _controller.Selection.Count
                             || _lastSelectionBounds != selectionBounds;

        if (_committedValid && !viewChanged && !selectionChanged) return;

        using (var session = _committedTarget.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            Scene.DrawCommitted(session, _controller);
        }
        _committedPixels = _committedTarget.GetPixelBytes();
        GpuReadbackBytes += _committedPixels.Length;

        _committedValid = true;
        _lastView = _controller.ViewTransform;
        _lastSelectionBounds = selectionBounds;
        _lastSelectionCount = _controller.Selection.Count;
    }

    /// <summary>Screen-space bounding box of everything still being drawn.</summary>
    private (int X, int Y, int Width, int Height) LiveRegion()
    {
        var world = Rect.Empty;
        foreach (var s in _controller.LiveStrokes) world = world.Union(s.Bounds);
        foreach (var s in _controller.ShapePreview) world = world.Union(s.Bounds);
        if (world.IsEmpty) return (0, 0, 0, 0);

        var view = _controller.ViewTransform;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in new[]
        {
            new Vector2(world.Left, world.Top), new Vector2(world.Right, world.Top),
            new Vector2(world.Right, world.Bottom), new Vector2(world.Left, world.Bottom),
        })
        {
            var v = Vector2.Transform(c, view);
            minX = MathF.Min(minX, v.X); minY = MathF.Min(minY, v.Y);
            maxX = MathF.Max(maxX, v.X); maxY = MathF.Max(maxY, v.Y);
        }

        int x = Math.Max(0, (int)MathF.Floor(minX) - 4);
        int y = Math.Max(0, (int)MathF.Floor(minY) - 4);
        int right = Math.Min(_window.Width, (int)MathF.Ceiling(maxX) + 4);
        int bottom = Math.Min(_window.Height, (int)MathF.Ceiling(maxY) + 4);
        return (x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private void CompositeLiveRegion((int X, int Y, int Width, int Height) region)
    {
        if (_liveTarget is null || _liveWidth != region.Width || _liveHeight != region.Height)
        {
            _liveTarget?.Dispose();
            _liveTarget = CreateTarget(region.Width, region.Height);
            _liveWidth = region.Width;
            _liveHeight = region.Height;
        }

        // Render as if on screen, then shift so the region's origin becomes (0,0).
        var transform = _controller.ViewTransform
                      * Matrix3x2.CreateTranslation(-region.X, -region.Y);

        using (var session = _liveTarget.CreateDrawingSession())
        {
            session.Clear(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            Scene.DrawLive(session, _controller, transform);
        }

        var pixels = _liveTarget.GetPixelBytes();
        GpuReadbackBytes += pixels.Length;
        PixelCompositing.BlendOver(_frame, _window.Width, _window.Height,
            region.X, region.Y, pixels, region.Width, region.Height);
    }

    public void Dispose()
    {
        Detach();
        Scene.Dispose();
        _committedTarget?.Dispose();
        _committedTarget = null;
        _liveTarget?.Dispose();
        _liveTarget = null;
    }
}