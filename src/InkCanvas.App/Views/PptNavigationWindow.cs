using InkCanvas.App.Services;
using InkCanvas.Controls.Controls;
using InkCanvas.Features.Ppt;
using InkCanvas.Platform.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

public enum PptNavigationPlacement { BottomLeft, BottomRight, SideLeft, SideRight }

/// <summary>
/// PPT slideshow navigation panel, corresponding to the four WPF controls
/// <c>PPTNavigationBottomLeft/Right</c> and <c>PPTNavigationSidesLeft/Right</c>.
/// It is an independent WinUI window so it can sit over a slideshow without being clipped by
/// the bottom toolbar's window bounds.
/// </summary>
public sealed class PptNavigationWindow : Window
{
    private readonly PptNavigationPlacement _placement;
    private readonly IShellActions _shell;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _counter;
    private readonly TextBlock _counterText;
    private XamlRoot _trackedRoot;

    public PptNavigationWindow(PptNavigationPlacement placement, IShellActions shell, int theme)
    {
        _placement = placement;
        _shell = shell;
        bool vertical = placement is PptNavigationPlacement.SideLeft or PptNavigationPlacement.SideRight;
        Title = "PowerPoint 导航";

        _previous = NavButton(vertical ? Glyphs.ChevronUp : Glyphs.ChevronLeft,
            vertical ? "上一页" : "上一页", () => _shell.PptPrevious());
        _next = NavButton(vertical ? Glyphs.ChevronDown : Glyphs.ChevronRight,
            "下一页", () => _shell.PptNext());
        _counterText = new TextBlock
        {
            Text = "0 / 0",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _counter = new Button
        {
            Content = _counterText,
            MinWidth = vertical ? 44 : 52,
            MinHeight = vertical ? 48 : 40,
            Padding = new Thickness(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(_counter, "打开 PowerPoint 幻灯片导航窗格并切换到鼠标模式");
        _counter.Click += (_, _) => _shell.PptOpenNavigation();

        var panel = new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = 2,
        };
        panel.Children.Add(_previous);
        panel.Children.Add(_counter);
        panel.Children.Add(_next);

        var root = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)Application.Current.Resources["IcBarCorner"],
            Padding = new Thickness(2),
            Child = panel,
        };
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: true);
        WindowChrome.ApplyTheme(this, theme);
        NativeMethods.AddExStyle(WindowChrome.GetHwnd(this), NativeMethods.WS_EX_NOACTIVATE);
        Closed += (_, _) =>
        {
            if (_trackedRoot is not null) _trackedRoot.Changed -= OnRootChanged;
        };
        Activated += (_, _) => TrackRoot();
        root.Loaded += (_, _) => TrackRoot();
        PlaceOnWorkArea();
        SetVisible(false);
    }

    private static Button NavButton(string glyph, string name, Action invoke)
    {
        var b = new Button
        {
            Content = new FontIcon
            {
                Glyph = glyph,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons"),
                FontSize = 16,
            },
            MinWidth = 44,
            MinHeight = 44,
            Padding = new Thickness(6),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(b, name);
        b.Click += (_, _) => invoke();
        return b;
    }

    public void Update(PptSnapshot snapshot, bool panelVisible, bool counterVisible)
    {
        _counter.Visibility = counterVisible ? Visibility.Visible : Visibility.Collapsed;
        int position = snapshot?.ShowPosition > 0 ? snapshot.ShowPosition : snapshot?.Slide?.Index ?? 0;
        int count = snapshot?.Presentation?.SlideCount ?? 0;
        _counterText.Text = $"{position} / {count}";
        PlaceOnWorkArea();
        SetVisible(panelVisible);
    }

    private void SetVisible(bool visible)
    {
        if (visible) NativeMethods.ShowWindow(WindowChrome.GetHwnd(this), NativeMethods.SW_SHOWNOACTIVATE);
        else NativeMethods.ShowWindow(WindowChrome.GetHwnd(this), NativeMethods.SW_HIDE);
    }

    private void TrackRoot()
    {
        if (_trackedRoot is not null) _trackedRoot.Changed -= OnRootChanged;
        _trackedRoot = (Content as FrameworkElement)?.XamlRoot;
        if (_trackedRoot is not null) _trackedRoot.Changed += OnRootChanged;
        PlaceOnWorkArea();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => PlaceOnWorkArea();

    private void PlaceOnWorkArea()
    {
        var work = Platform.System.ScreenService.WorkArea;
        double dpi = WindowChrome.GetDpiScale(this);
        bool vertical = _placement is PptNavigationPlacement.SideLeft or PptNavigationPlacement.SideRight;
        int width = (int)Math.Ceiling((vertical ? 52 : 160) * dpi);
        int height = (int)Math.Ceiling((vertical ? 160 : 52) * dpi);
        int gap = (int)Math.Ceiling(20 * dpi);
        var position = AppWindow.Position;
        int x = _placement switch
        {
            PptNavigationPlacement.SideRight => work.X + work.Width - width - gap,
            PptNavigationPlacement.BottomRight => work.X + work.Width - width - gap,
            _ => work.X + gap,
        };
        int y = _placement switch
        {
            PptNavigationPlacement.SideLeft or PptNavigationPlacement.SideRight => work.Y + (work.Height - height) / 2,
            _ => work.Y + work.Height - height - gap,
        };
        if (position.X != x || position.Y != y || AppWindow.Size.Width != width || AppWindow.Size.Height != height)
            WindowChrome.Place(this, x, y, width, height);
    }
}