using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Borderless, topmost WinUI 3 window hosting the floating bar. Draggable by its background.
/// </summary>
public sealed class FloatingBarWindow : Window
{
    private readonly FloatingBar _bar = new();
    private readonly Border _root;
    private bool _dragging;
    private Windows.Foundation.Point _dragStart;
    private Windows.Graphics.PointInt32 _windowStart;

    public FloatingBarWindow()
    {
        Title = "Ink Canvas 工具栏";
        _root = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(6),
            Child = _bar,
        };
        Content = _root;

        WindowChrome.MakeBorderless(this, topmost: true);

        _root.PointerPressed += OnDragStart;
        _root.PointerMoved += OnDragMove;
        _root.PointerReleased += OnDragEnd;
        _root.PointerCaptureLost += (_, _) => _dragging = false;

        Activated += (_, _) => Motion.PlayEntrance(_bar, 16f);
    }

    public FloatingBar Bar => _bar;

    public void Configure(InkCanvas.Ink.InkController controller, IShellActions shell)
    {
        _bar.Controller = controller;
        _bar.Shell = shell;
        _bar.Build();
    }

    /// <summary>
    /// Size the window to the bar's measured content and place it bottom-centre. Sizing to
    /// content matters: any leftover window area is transparent but still hit-testable.
    /// </summary>
    public void Reposition(double bottomMargin, double scalePercent)
    {
        double scale = Math.Clamp(scalePercent, 50, 200) / 100.0;
        _bar.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        _bar.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };

        _bar.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = _bar.DesiredSize;
        int width = Math.Max(120, (int)Math.Ceiling(desired.Width * scale) + 16);
        int height = Math.Max(56, (int)Math.Ceiling(desired.Height * scale) + 16);

        var screen = Platform.System.ScreenService.Primary;
        int x = screen.X + (screen.Width - width) / 2;
        int y = screen.Y + screen.Height - height - (int)Math.Max(0, bottomMargin);
        WindowChrome.Place(this, x, y, width, height);
    }

    private void OnDragStart(object sender, PointerRoutedEventArgs e)
    {
        // Only drag from empty bar background, never from a button.
        if (e.OriginalSource is not Border && e.OriginalSource is not StackPanel && e.OriginalSource is not Grid) return;
        _dragging = true;
        _dragStart = e.GetCurrentPoint(null).Position;
        _windowStart = this.AppWindow.Position;
        _root.CapturePointer(e.Pointer);
    }

    private void OnDragMove(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var now = e.GetCurrentPoint(null).Position;
        int dx = (int)(now.X - _dragStart.X);
        int dy = (int)(now.Y - _dragStart.Y);
        this.AppWindow.Move(new Windows.Graphics.PointInt32(_windowStart.X + dx, _windowStart.Y + dy));
    }

    private void OnDragEnd(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        _root.ReleasePointerCapture(e.Pointer);
    }
}