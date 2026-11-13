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

        // Re-fit whenever the bar's button set changes (selection button, PPT group, fold).
        _bar.LayoutUpdated += OnBarLayoutUpdated;

        Activated += (_, _) => Motion.PlayEntrance(_bar, 16f);
    }

    private double _bottomMargin = 100, _scalePercent = 100;

    private void PlaceBottomCentre(int width, int height)
    {
        // Work area so a zero bottom margin still clears the taskbar.
        var work = Platform.System.ScreenService.WorkArea;
        int x = work.X + (work.Width - width) / 2;
        int y = work.Y + work.Height - height - (int)Math.Max(0, _bottomMargin);
        WindowChrome.Place(this, x, y, width, height);
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
        _bottomMargin = bottomMargin;
        _scalePercent = scalePercent;
        double scale = Math.Clamp(scalePercent, 50, 200) / 100.0;
        _bar.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        _bar.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };

        var (naturalWidth, naturalHeight) = _bar.ComputeLayoutSize();
        _ = naturalWidth; _ = naturalHeight;

        // Deterministic sizing from the button count. 54px per button comfortably covers the
        // actual laid-out pitch; the bar stretches to fill, so any slack is inside the visible
        // rounded surface rather than an invisible click-swallowing strip.
        var (buttons, separators) = _bar.CountVisibleItems();
        const double chrome = 16 + 2 + 12;
        double rowWidth = buttons * 54.0 + separators * 11.0 + chrome;
        bool folded = _bar.IsFolded;

        int width = (int)Math.Ceiling(rowWidth * scale);
        int height = (int)Math.Ceiling((40 + chrome + (folded ? 44 : 0)) * scale);
        PlaceBottomCentre(width, height);
    }

    private int _lastButtons = -1, _lastSeparators = -1;
    private bool _lastFolded;

    private void OnBarLayoutUpdated(object sender, object e)
    {
        var (buttons, separators) = _bar.CountVisibleItems();
        bool folded = _bar.IsFolded;
        if (buttons == _lastButtons && separators == _lastSeparators && folded == _lastFolded) return;
        _lastButtons = buttons;
        _lastSeparators = separators;
        _lastFolded = folded;
        Reposition(_bottomMargin, _scalePercent);
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