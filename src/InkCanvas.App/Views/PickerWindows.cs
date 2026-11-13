using InkCanvas.App.Services;
using InkCanvas.Controls.Controls;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// Pen colour / width picker in its own window. A WPF-style transient menu is deliberately
/// represented as a compact independent popup window: WinUI 3 Flyout is clipped by the
/// short floating-bar host.
/// </summary>
public sealed class PenPaletteWindow : Window
{
    private readonly InkController _controller;
    private readonly PenPalette _palette;
    private readonly int _theme;
    private int _barX, _barY;
    private bool _hasBarPosition;
    private XamlRoot _trackedRoot;

    public PenPaletteWindow(InkController controller, bool highlighter, int theme)
    {
        _controller = controller;
        _theme = theme;
        Title = highlighter ? "荧光笔设置" : "画笔设置";

        _palette = new PenPalette { Controller = controller };
        _palette.RequestClose = Close;

        var root = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            Child = _palette,
        };
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: true);
        WindowChrome.ApplyTheme(this, theme);
        WindowChrome.PlaceDip(this, 100, 100, 292, 500);
        _palette.RefreshFromController();
        root.Loaded += (_, _) => AttachDpiTracking();
    }

    public Action<double> InkWidthChanged { get => _palette.InkWidthChanged; set => _palette.InkWidthChanged = value; }
    public Action<int> InkStyleChanged { get => _palette.InkStyleChanged; set => _palette.InkStyleChanged = value; }
    public Action<int> InkAlphaChanged { get => _palette.InkAlphaChanged; set => _palette.InkAlphaChanged = value; }
    public Action AttributesChanged { get => _palette.AttributesChanged; set => _palette.AttributesChanged = value; }
    public int HighlighterAlpha { get => _palette.HighlighterAlpha; set => _palette.HighlighterAlpha = value; }
    public bool LightColorScheme { get => _palette.LightColorScheme; set => _palette.LightColorScheme = value; }

    public void RefreshFromController() => _palette.RefreshFromController();

    private void AttachDpiTracking()
    {
        _trackedRoot = (Content as FrameworkElement)?.XamlRoot;
        if (_trackedRoot is not null) _trackedRoot.Changed += (_, _) =>
        {
            if (_hasBarPosition) PlaceAboveBar(_barX, _barY);
        };
    }

    /// <summary>Anchor over the floating bar and clamp to the current work area.</summary>
    public void PlaceAboveBar(int barX, int barY)
    {
        _barX = barX; _barY = barY; _hasBarPosition = true;
        var work = Platform.System.ScreenService.WorkArea;
        double dpi = WindowChrome.GetDpiScale(this);
        int w = (int)Math.Ceiling(292 * dpi), h = (int)Math.Ceiling(500 * dpi);
        int gap = (int)Math.Ceiling(8 * dpi);
        int x = Math.Clamp(barX + (int)Math.Ceiling(16 * dpi), work.X, work.X + work.Width - w);
        int y = Math.Max(work.Y, barY - h - gap);
        WindowChrome.Place(this, x, y, w, h);
    }
}

/// <summary>Shape catalogue popup; one window so every item stays reachable at any DPI.</summary>
public sealed class ShapePickerWindow : Window
{
    private int _barX, _barY;
    private bool _hasBarPosition;
    private XamlRoot _trackedRoot;

    public ShapePickerWindow(InkController controller, Action onPicked, int theme)
    {
        Title = "选择图形";

        var list = new StackPanel { Spacing = 4, Padding = new Thickness(10) };
        foreach (var (kind, label) in FloatingBar.ShapeCatalog)
        {
            var captured = kind;
            var button = new Button
            {
                Content = label,
                MinHeight = 40,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            button.Click += (_, _) =>
            {
                controller.ShapeKind = captured;
                controller.Tool = InkTool.Shape;
                onPicked?.Invoke();
                Close();
            };
            list.Children.Add(button);
        }

        var close = new Button { Content = "关闭", MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
        close.Click += (_, _) => Close();
        list.Children.Add(close);

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var root = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            Child = scroll,
        };
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: true);
        WindowChrome.ApplyTheme(this, theme);
        WindowChrome.PlaceDip(this, 100, 100, 280, 590);
        root.Loaded += (_, _) =>
        {
            _trackedRoot = root.XamlRoot;
            if (_trackedRoot is not null) _trackedRoot.Changed += (_, _) =>
            {
                if (_hasBarPosition) PlaceAboveBar(_barX, _barY);
            };
        };
    }

    public void PlaceAboveBar(int barX, int barY)
    {
        _barX = barX; _barY = barY; _hasBarPosition = true;
        var work = Platform.System.ScreenService.WorkArea;
        double dpi = WindowChrome.GetDpiScale(this);
        int w = (int)Math.Ceiling(280 * dpi), h = (int)Math.Ceiling(590 * dpi);
        int gap = (int)Math.Ceiling(8 * dpi);
        int x = Math.Clamp(barX + (int)Math.Ceiling(210 * dpi), work.X, work.X + work.Width - w);
        int y = Math.Max(work.Y, barY - h - gap);
        WindowChrome.Place(this, x, y, w, h);
    }
}