using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.App.Services;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private double _bottomMargin = 100, _scalePercent = 100;
    private bool _initialDpiPlacementDone;
    private double _lastDpiScale = 1;
    private XamlRoot _trackedXamlRoot;

    public FloatingBarWindow()
    {
        Title = "Ink Canvas 工具栏";
        _root = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            // No padding: a WinUI 3 window paints an opaque backdrop, so any inset around the
            // bar shows up as a white frame. The bar must fill the whole client area.
            Padding = new Thickness(0),
            Child = _bar,
        };
        Content = _root;

        WindowChrome.MakeBorderless(this, topmost: true);

        _root.PointerPressed += OnDragStart;
        _root.Loaded += (_, _) => AttachDpiTracking();

        // Re-fit whenever the bar's button set changes (selection button, PPT group, fold).
        _bar.LayoutUpdated += OnBarLayoutUpdated;

        Activated += (_, _) =>
        {
            Motion.PlayEntrance(_bar, 16f);
            if (!_initialDpiPlacementDone)
            {
                // On first activation the HWND is now associated with its actual monitor; query DPI
                // at that point and recompute the physical-pixel size from the DIP layout.
                _initialDpiPlacementDone = true;
                Reposition(_bottomMargin, _scalePercent);
            }
        };
        Closed += (_, _) =>
        {
            if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed -= OnXamlRootChanged;
            _bar.LayoutUpdated -= OnBarLayoutUpdated;
        };
    }

    private void AttachDpiTracking()
    {
        if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed -= OnXamlRootChanged;
        _trackedXamlRoot = _root.XamlRoot;
        if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed += OnXamlRootChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        // Per-monitor DPI changes update XamlRoot.RasterizationScale; AppWindow stays in pixels.
        // Keep the user's dragged location, only resize around it. Calling Reposition here would
        // unexpectedly snap a bar that had been moved to another monitor back to primary-center.
        double newScale = WindowChrome.GetDpiScale(this);
        if (Math.Abs(newScale - _lastDpiScale) < 0.001) return;
        _lastDpiScale = newScale;
        var (width, height, _) = GetPhysicalSize(newScale);
        var pos = AppWindow.Position;
        WindowChrome.Place(this, pos.X, pos.Y, width, height);
    }

    private void PlaceBottomCentre(int width, int height, double dpiScale)
    {
        // Work area is physical pixels; the configured margin is a logical DIP value.
        var work = Platform.System.ScreenService.WorkArea;
        int x = work.X + (work.Width - width) / 2;
        int y = work.Y + work.Height - height - (int)Math.Ceiling(Math.Max(0, _bottomMargin) * dpiScale);
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
        double dpiScale = WindowChrome.GetDpiScale(this);
        _lastDpiScale = dpiScale;
        var (widthPx, heightPx, _) = GetPhysicalSize(dpiScale);
        PlaceBottomCentre(widthPx, heightPx, dpiScale);
    }

    private (int Width, int Height, double DpiScale) GetPhysicalSize(double dpiScale)
    {
        double userScale = Math.Clamp(_scalePercent, 50, 200) / 100.0;
        _bar.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
        _bar.RenderTransform = new ScaleTransform { ScaleX = userScale, ScaleY = userScale };

        // AppWindow is physical pixels; WinUI layout and FontIcon sizes are DIPs.
        var (buttons, separators) = _bar.CountVisibleItems();
        const double chromeDip = 16;
        double rowDip = buttons * 54.0 + separators * 11.0 + chromeDip;
        bool folded = _bar.IsFolded;
        int widthPx = (int)Math.Ceiling(rowDip * userScale * dpiScale);
        int heightPx = (int)Math.Ceiling((40 + chromeDip + (folded ? 44 : 0)) * userScale * dpiScale);
        return (widthPx, heightPx, dpiScale);
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
        // Never start a native window drag from an interactive control.
        DependencyObject current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is ButtonBase) return;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        // Use Windows' native non-client caption drag loop. The old pointer-delta loop mixed
        // screen DIPs with AppWindow physical pixels and issued a Move on every pointer event,
        // which jittered on high-DPI displays and fought the XAML pointer-capture loop.
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessageW(WindowChrome.GetHwnd(this), NativeMethods.WM_NCLBUTTONDOWN,
            new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
        e.Handled = true;
    }
}