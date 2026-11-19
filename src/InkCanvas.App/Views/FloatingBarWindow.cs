using InkCanvas.Controls.Controls;
using InkCanvas.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Borderless, topmost, semi-transparent WinUI 3 window hosting ONLY the floating bar row.
/// Draggable by its background. The 更多设置 / 手势选项 popups live in separate
/// <see cref="BarPanelWindow"/>s so opening them never resizes or shifts this row.
/// </summary>
public sealed class FloatingBarWindow : Window
{
    private readonly FloatingBar _bar = new();
    private readonly Viewbox _scaledBar;
    private readonly Border _root;
    private readonly List<FoldedBarHandleWindow> _foldHandles = new();
    private readonly Dictionary<BarPanelKind, BarPanelWindow> _panels = new();
    private bool _isFolded;
    private bool _userPlaced;
    private double _bottomMargin = 100, _scalePercent = 100;
    private bool _initialDpiPlacementDone;
    private double _lastDpiScale = 1;
    private XamlRoot _trackedXamlRoot;

    public FloatingBarWindow()
    {
        Title = "Ink Canvas 工具栏";
        _scaledBar = new Viewbox
        {
            Child = _bar,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        _root = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(0),
            Child = _scaledBar,
        };
        Content = _root;

        WindowChrome.MakeBorderless(this, topmost: true, noActivate: true);
        EnableTransparency(this);
        WindowChrome.ClipToSurfaces(this, () => _bar.ChromeSurfaces);
        WindowChrome.EnableDragMove(this, _root, () => { _userPlaced = true; ClosePanels(); }, fromAnywhere: true);
        AttachTapRouting();
        _root.Loaded += (_, _) =>
        {
            AttachDpiTracking();
            if (!_initialDpiPlacementDone)
            {
                _initialDpiPlacementDone = true;
                Reposition(_bottomMargin, _scalePercent);
                UpdateAcrylicTint(this, _bar.Theme);
            }
        };

        _bar.LayoutUpdated += OnBarLayoutUpdated;
        _bar.PanelRequested += OnPanelRequested;
        _bar.ThemeRefreshed += () => UpdateAcrylicTint(this, _bar.Theme);

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated) return;
            if (!_initialDpiPlacementDone)
            {
                _initialDpiPlacementDone = true;
                Reposition(_bottomMargin, _scalePercent);
                UpdateAcrylicTint(this, _bar.Theme);
            }
        };
        Closed += (_, _) =>
        {
            if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed -= OnXamlRootChanged;
            _bar.LayoutUpdated -= OnBarLayoutUpdated;
            _bar.PanelRequested -= OnPanelRequested;
            ClosePanels();
            CloseFoldHandles();
        };
    }

    internal double UserScale => Math.Clamp(_scalePercent, 50, 200) / 100.0;

    private void AttachDpiTracking()
    {
        if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed -= OnXamlRootChanged;
        _trackedXamlRoot = _root.XamlRoot;
        if (_trackedXamlRoot is not null) _trackedXamlRoot.Changed += OnXamlRootChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        double newScale = WindowChrome.GetDpiScale(this);
        if (Math.Abs(newScale - _lastDpiScale) < 0.001) return;
        _lastDpiScale = newScale;
        var (width, height, _) = GetPhysicalSize(newScale);
        var pos = WindowChrome.GetClientPosition(this);
        WindowChrome.PlaceClient(this, pos.X, pos.Y, width, height);
        WindowChrome.EnsureAbove(this);
        ReanchorPanels();
    }

    public FloatingBar Bar => _bar;

    public Windows.Graphics.RectInt32 GetToolAnchor(InkCanvas.Ink.InkTool tool)
        => WindowChrome.GetScreenBounds(this, _bar.GetToolButton(tool));

    public bool IsFolded => _isFolded;
    internal IReadOnlyList<FoldedBarHandleWindow> FoldHandles => _foldHandles;

    public void SetFolded(bool folded)
    {
        if (_isFolded == folded) return;
        _isFolded = folded;
        if (folded)
        {
            ClosePanels();
            CloseFoldHandles();
            AppWindow.Hide();
            var work = Platform.System.ScreenService.WorkArea;
            double dpi = WindowChrome.GetDpiScale(this);
            // WPF LeftSidePanel: 30x50 mostly off-screen (Margin -40). Keep a small sliver
            // (~14 DIP) protruding so the fold footprint stays tiny but clickable.
            int width = (int)Math.Ceiling(30 * dpi), height = (int)Math.Ceiling(50 * dpi);
            int protrude = (int)Math.Ceiling(14 * dpi);
            int y = work.Y + (work.Height - height) / 2;
            _foldHandles.Add(new FoldedBarHandleWindow(this, left: true, width, height, work.X - (width - protrude), y));
            _foldHandles.Add(new FoldedBarHandleWindow(this, left: false, width, height, work.X + work.Width - protrude, y));
            foreach (var handle in _foldHandles) handle.ShowHandle();
            _bar.SetFoldedVisual(true);
        }
        else
        {
            CloseFoldHandles();
            _bar.SetFoldedVisual(false);
            Reposition(_bottomMargin, _scalePercent);
            WindowChrome.ShowWithoutActivation(this);
        }
    }

    private void CloseFoldHandles()
    {
        foreach (var handle in _foldHandles.ToArray()) handle.Close();
        _foldHandles.Clear();
    }

    public void Configure(InkCanvas.Ink.InkController controller, IShellActions shell)
    {
        _bar.Controller = controller;
        _bar.Shell = shell;
        _bar.Build();
    }

    public void Reposition(double bottomMargin, double scalePercent, bool recenter = true)
    {
        _bottomMargin = bottomMargin;
        _scalePercent = scalePercent;
        if (recenter) _userPlaced = false;
        double dpiScale = WindowChrome.GetDpiScale(this);
        _lastDpiScale = dpiScale;
        var (widthPx, heightPx, _) = GetPhysicalSize(dpiScale);
        PlaceBottomCentre(widthPx, heightPx, dpiScale, keepPosition: !recenter && _initialDpiPlacementDone);
        WindowChrome.EnsureAbove(this);
        ReanchorPanels();
    }

    private (int Width, int Height, double DpiScale) GetPhysicalSize(double dpiScale)
    {
        double userScale = UserScale;
        var (widthDip, heightDip) = _bar.ComputeLayoutSize();
        // Viewbox measures the design at its natural size before scaling. RenderTransform
        // scales after layout, so small windows clip the bar before the transform is applied.
        _scaledBar.Width = widthDip * userScale;
        _scaledBar.Height = heightDip * userScale;
        int widthPx = (int)Math.Ceiling(widthDip * userScale * dpiScale);
        int heightPx = (int)Math.Ceiling(heightDip * userScale * dpiScale);
        return (widthPx, heightPx, dpiScale);
    }

    private void PlaceBottomCentre(int width, int height, double dpiScale, bool keepPosition = false)
    {
        var work = Platform.System.ScreenService.WorkArea;
        bool keep = keepPosition || _userPlaced || (_isFolded && _foldHandles.Count > 0);
        var current = WindowChrome.GetClientPosition(this);
        int x = keep ? current.X : work.X + (work.Width - width) / 2;
        int y = keep ? current.Y
            : work.Y + work.Height - height - (int)Math.Ceiling(Math.Max(0, _bottomMargin) * dpiScale);
        WindowChrome.PlaceClient(this, x, y, width, height);
    }

    private int _lastButtons = -1, _lastSeparators = -1;
    private bool _lastExpanded, _lastCollapsed;

    private void OnBarLayoutUpdated(object sender, object e)
    {
        if (!_initialDpiPlacementDone) return;
        var (buttons, separators) = _bar.CountVisibleItems();
        bool expanded = _bar.IsExpanded;
        bool collapsed = _bar.IsMainCollapsed;
        if (buttons == _lastButtons && separators == _lastSeparators && expanded == _lastExpanded && collapsed == _lastCollapsed) return;
        _lastButtons = buttons;
        _lastSeparators = separators;
        _lastExpanded = expanded;
        _lastCollapsed = collapsed;
        // Refit in place: keep the left/top edge so the primary buttons never shift; a smiley
        // collapse shrinks the window from the RIGHT only.
        double dpi = WindowChrome.GetDpiScale(this);
        var (w, h, _) = GetPhysicalSize(dpi);
        var position = WindowChrome.GetClientPosition(this);
        WindowChrome.PlaceClient(this, position.X, position.Y, w, h);
        WindowChrome.EnsureAbove(this);
        ReanchorPanels();
    }

    // ---- sub-popups (own windows, anchored above the bar) ----

    private void OnPanelRequested(BarPanelKind kind, bool open)
    {
        if (!open)
        {
            if (_panels.TryGetValue(kind, out var closing)) closing.Close();
            return;
        }
        foreach (var other in _panels.Where(pair => pair.Key != kind).Select(pair => pair.Value).ToArray()) other.Close();
        if (!_panels.TryGetValue(kind, out var panel))
        {
            panel = new BarPanelWindow(this, _bar, kind);
            _panels.Add(kind, panel);
        }
        var position = AppWindow.Position;
        panel.ShowAboveBar(position.X, position.Y, AppWindow.Size.Width);
    }

    internal void OnPanelClosed(BarPanelKind kind, BarPanelWindow window)
    {
        if (!_panels.TryGetValue(kind, out var current) || !ReferenceEquals(current, window)) return;
        _panels.Remove(kind);
        _bar.NotifyPanelClosed(kind);
    }

    internal BarPanelWindow GetOpenPanel(BarPanelKind kind) => _panels.GetValueOrDefault(kind);

    internal void EnsureHandlesAbove(IntPtr overlay = default)
    {
        foreach (var h in _foldHandles) WindowChrome.EnsureAbove(h, overlay);
    }

    internal void ClosePanels()
    {
        foreach (var panel in _panels.Values.ToArray()) panel.Close();
        _panels.Clear();
    }

    private void ReanchorPanels()
    {
        var position = AppWindow.Position;
        foreach (var panel in _panels.Values.ToArray())
            panel.PlaceAboveBar(position.X, position.Y, AppWindow.Size.Width);
    }

    // ---- tap routing for smiley / PPT plate ----

    private DependencyObject _tapSource;
    private Windows.Foundation.Point _tapPos;

    private void AttachTapRouting()
    {
        _root.PointerPressed += (_, e) =>
        {
            _tapSource = e.OriginalSource as DependencyObject;
            _tapPos = e.GetCurrentPoint(_root).Position;
        };
        _root.PointerReleased += (_, e) =>
        {
            var source = _tapSource;
            _tapSource = null;
            if (source is null) return;
            var p = e.GetCurrentPoint(_root).Position;
            if (Math.Abs(p.X - _tapPos.X) <= 10 && Math.Abs(p.Y - _tapPos.Y) <= 10)
                _bar.TryHandleTap(source);
        };
        _root.PointerCanceled += (_, _) => _tapSource = null;
    }

    /// <summary>Leave tint to the XAML brushes and keep the desktop acrylic clear on deactivation.</summary>
    public static void EnableTransparency(Window window)
    {
        window.SystemBackdrop = new ChromeBackdrop();
    }

    /// <summary>
    /// Theme tint is painted by the XAML surface; the backdrop stays clear in either theme.
    /// </summary>
    public static void UpdateAcrylicTint(Window window, int theme)
    {
        // ChromeBackdrop follows the XAML theme and paints no additional tint.
    }
}
