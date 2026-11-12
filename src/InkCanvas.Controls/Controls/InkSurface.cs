using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using InkCanvas.Platform.Windows;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace InkCanvas.Controls.Controls;

/// <summary>
/// A WinUI 3 ink surface for opaque contexts (the blackboard/whiteboard window).
/// <para>
/// Uses the same <see cref="InkController"/> and <see cref="InkSceneRenderer"/> as the
/// transparent overlay, so board ink and screen ink behave identically — including the
/// live taper. The transparent screen-pen overlay cannot use this control because a WinUI 3
/// window is always opaque; it uses <see cref="OverlayInkPresenter"/> instead.
/// </para>
/// </summary>
public sealed class InkSurface : UserControl
{
    private readonly CanvasControl _canvas;
    private InkController _controller;
    private InkSceneRenderer _scene;

    public InkSurface()
    {
        _canvas = new CanvasControl
        {
            ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
        };
        _canvas.Draw += OnDraw;
        Content = _canvas;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCanceled;

        ManipulationMode = ManipulationModes.None; // gestures are handled by InkController
    }

    public InkController Controller
    {
        get => _controller;
        set
        {
            if (_controller is not null) _controller.Invalidated -= OnInvalidated;
            _controller = value;
            _scene?.Dispose();
            _scene = new InkSceneRenderer();
            if (_controller is not null)
            {
                _controller.Invalidated += OnInvalidated;
                _controller.Document.Changed += OnDocumentChanged;
            }
            _canvas.Invalidate();
        }
    }

    /// <summary>Board background colour; transparent when the host paints it.</summary>
    public Windows.UI.Color ClearColor
    {
        get => _canvas.ClearColor;
        set => _canvas.ClearColor = value;
    }

    private void OnDocumentChanged()
    {
        _scene?.Strokes.InvalidateAll();
        _canvas.Invalidate();
    }

    private void OnInvalidated() => _canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_controller is null || _scene is null) return;
        _controller.ViewCenter = new System.Numerics.Vector2(
            (float)ActualWidth / 2, (float)ActualHeight / 2);
        _scene.Draw(args.DrawingSession, _controller);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is null) return;
        CapturePointer(e.Pointer);
        _controller.OnPointerDown(Translate(e));
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is null) return;
        _controller.OnPointerMove(Translate(e));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is null) return;
        _controller.OnPointerUp(Translate(e));
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_controller is null) return;
        _controller.OnPointerUp(Translate(e));
    }

    private OverlayPointer Translate(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var props = point.Properties;
        var kind = point.PointerDeviceType switch
        {
            Microsoft.UI.Input.PointerDeviceType.Pen => OverlayPointerKind.Pen,
            Microsoft.UI.Input.PointerDeviceType.Touch => OverlayPointerKind.Touch,
            _ => OverlayPointerKind.Mouse,
        };
        var contact = props.ContactRect;
        return new OverlayPointer(
            e.Pointer.PointerId,
            (float)point.Position.X,
            (float)point.Position.Y,
            props.Pressure <= 0 ? 0.5f : props.Pressure,
            (float)contact.Width,
            (float)contact.Height,
            kind,
            (uint)point.Timestamp / 1000);
    }
}