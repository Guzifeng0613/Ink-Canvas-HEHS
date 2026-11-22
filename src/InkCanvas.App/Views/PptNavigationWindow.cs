using System.Diagnostics;
using System.Runtime.InteropServices;
using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.Controls.Theming;
using InkCanvas.Features.Ppt;
using InkCanvas.Platform.Interop;
using InkCanvas.Platform.System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace InkCanvas.App.Views;

public enum PptNavigationPlacement { BottomLeft, BottomRight, SideLeft, SideRight }

/// <summary>The four WPF-style previous/page/next panels, with independent idle opacity.</summary>
public sealed class PptNavigationWindow : Window
{
    private const double S = 40.0 / 42.0;
    private readonly FloatingBarWindow _owner;
    private readonly int _theme;
    private readonly Border _card;
    private readonly Viewbox _scaled;
    private readonly Button _counter;
    private readonly TextBlock _counterText;
    private readonly FontIcon _previousIcon, _nextIcon;
    private readonly Rectangle _separator1, _separator2;
    private readonly DispatcherQueueTimer _idleTimer, _fadeTimer;
    private readonly Stopwatch _fadeClock = new();
    private Action _detachAnchor;
    private XamlRoot _trackedRoot;
    private PptSnapshot _snapshot;
    private bool _closed, _placing, _placeQueued;
    private double _opacity = 1, _fadeFrom = 1, _fadeTo = 1;

    public PptNavigationPlacement Placement { get; }
    internal double WindowOpacity => _opacity;
    internal double TargetOpacity => _fadeTo;
    private bool IsVertical => Placement is PptNavigationPlacement.SideLeft or PptNavigationPlacement.SideRight;

    public PptNavigationWindow(PptNavigationPlacement placement, IShellActions shell,
        int theme, FloatingBarWindow owner = null)
    {
        Placement = placement;
        _owner = owner;
        _theme = theme;
        Title = "PowerPoint 导航";
        _previousIcon = Icon(IsVertical ? "\uF2B7" : "\uF2AB");
        _nextIcon = Icon(IsVertical ? "\uF2A4" : "\uF2B1");
        _counterText = new TextBlock { Text = "0/0", FontSize = 9 * S, IsTextScaleFactorEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = IsVertical ? Orientation.Vertical : Orientation.Horizontal };
        row.Children.Add(Cell(_previousIcon, "上一页", shell.PptPrevious));
        var count = new Grid();
        _separator1 = new Rectangle { Width = IsVertical ? 25 * S : S, Height = IsVertical ? S : 25 * S };
        _separator2 = new Rectangle { Width = _separator1.Width, Height = _separator1.Height };
        if (IsVertical)
        {
            count.RowDefinitions.Add(new() { Height = new(S) });
            count.RowDefinitions.Add(new() { Height = new(36 * S) });
            count.RowDefinitions.Add(new() { Height = new(S) });
            Grid.SetRow(_counterText, 1); Grid.SetRow(_separator2, 2);
        }
        else
        {
            count.ColumnDefinitions.Add(new() { Width = new(S) });
            count.ColumnDefinitions.Add(new() { Width = new(36 * S) });
            count.ColumnDefinitions.Add(new() { Width = new(S) });
            Grid.SetColumn(_counterText, 1); Grid.SetColumn(_separator2, 2);
        }
        count.Children.Add(_separator1); count.Children.Add(_counterText); count.Children.Add(_separator2);
        _counter = Cell(count, "幻灯片页码", shell.PptOpenNavigation);
        _counter.Width = IsVertical ? 40 * S : 38 * S;
        _counter.Height = IsVertical ? 38 * S : 40 * S;
        row.Children.Add(_counter);
        row.Children.Add(Cell(_nextIcon, "下一页", shell.PptNext));
        _card = new Border { Child = row, BorderThickness = new(S), CornerRadius = new(6 * S) };
        _scaled = new Viewbox { Child = _card, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Content = _scaled;
        _idleTimer = DispatcherQueue.CreateTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(5);
        _idleTimer.IsRepeating = false;
        _idleTimer.Tick += (_, _) => FadeTo(.75);
        _fadeTimer = DispatcherQueue.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        _fadeTimer.Tick += (_, _) => AdvanceFade();
        _scaled.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => RecordInteraction()), true);
        _scaled.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => RecordInteraction()), true);
        _scaled.AddHandler(UIElement.PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => RecordInteraction()), true);
        _scaled.KeyDown += (_, _) => RecordInteraction();
        WindowChrome.MakeBorderless(this, topmost: true, noActivate: true);
        WindowChrome.ClipToSurfaces(this, () => new[] { _card });
        ApplyChrome();
        if (_owner is not null)
        {
            _owner.Bar.ThemeRefreshed += ApplyChrome;
            _detachAnchor = WindowChrome.TrackAnchor(this, WindowChrome.GetHwnd(_owner), Place);
        }
        _scaled.Loaded += (_, _) =>
        {
            _trackedRoot = _scaled.XamlRoot;
            if (_trackedRoot is not null) _trackedRoot.Changed += OnRootChanged;
            Place();
        };
        _card.SizeChanged += (_, _) => QueuePlace();
        Closed += (_, _) =>
        {
            _closed = true;
            _idleTimer.Stop(); _fadeTimer.Stop();
            _detachAnchor?.Invoke(); _detachAnchor = null;
            if (_owner is not null) _owner.Bar.ThemeRefreshed -= ApplyChrome;
            if (_trackedRoot is not null) _trackedRoot.Changed -= OnRootChanged;
        };
        Place();
    }

    private static FontIcon Icon(string glyph) => new()
    {
        Glyph = glyph, FontFamily = new(IconFontStyle.Family),
        FontSize = 20 * S, Width = 26 * S, Height = 26 * S, IsTextScaleFactorEnabled = false,
    };

    private Button Cell(UIElement content, string name, Action invoke)
    {
        var cell = new Button { Content = content, Width = 40 * S, Height = 40 * S, MinWidth = 0, MinHeight = 0,
            Padding = new(0), BorderThickness = new(0), CornerRadius = new(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        cell.Resources["ButtonBackgroundPointerOver"] = cell.Background;
        cell.Resources["ButtonBackgroundPressed"] = cell.Background;
        cell.Resources["ButtonBorderBrushPointerOver"] = cell.Background;
        cell.Resources["ButtonBorderBrushPressed"] = cell.Background;
        AutomationProperties.SetName(cell, name);
        ToolTipService.SetToolTip(cell, name);
        cell.Click += (_, _) => { RecordInteraction(); invoke(); };
        return cell;
    }

    private void ApplyChrome()
    {
        int theme = _owner?.Bar.Settings?.Appearance.Theme ?? _theme;
        if (theme == 0) theme = _owner?.Bar.Theme ?? 1;
        WindowChrome.ApplyTheme(this, theme);
        _card.Background = _owner?.Bar.ColorfulBackground == true ? PopupBrushes.FloatBarColorful() : PopupBrushes.FloatBarBackground(theme);
        _card.BorderBrush = PopupBrushes.FloatBarBorder(theme);
        var foreground = PopupBrushes.FloatBarForeground(theme);
        _previousIcon.Foreground = _nextIcon.Foreground = _counterText.Foreground = foreground;
        _separator1.Fill = _separator2.Fill = foreground;
    }

    public void Update(PptSnapshot snapshot, bool panelVisible, bool counterVisible)
    {
        _snapshot = snapshot;
        _counter.Visibility = counterVisible ? Visibility.Visible : Visibility.Collapsed;
        int position = snapshot?.ShowPosition > 0 ? snapshot.ShowPosition : snapshot?.Slide?.Index ?? 0;
        string text = $"{position}/{snapshot?.Presentation?.SlideCount ?? 0}";
        if (_counterText.Text != text) _counterText.Text = text;
        Place();
        if (panelVisible == AppWindow.IsVisible) return;
        if (panelVisible)
        {
            WindowChrome.ShowWithoutActivation(this);
            SetOpacity(1);
            RecordInteraction();
        }
        else
        {
            _idleTimer.Stop(); _fadeTimer.Stop();
            AppWindow.Hide();
        }
    }

    internal void RecordInteraction()
    {
        if (_closed || !AppWindow.IsVisible) return;
        FadeTo(1);
        _idleTimer.Stop(); _idleTimer.Start();
    }

    private void FadeTo(double target)
    {
        _fadeTimer.Stop();
        _fadeFrom = _opacity; _fadeTo = target;
        if (!Motion.AnimationsEnabled || Math.Abs(_opacity - target) < .001) { SetOpacity(target); return; }
        _fadeClock.Restart();
        _fadeTimer.Start();
    }

    private void AdvanceFade()
    {
        double progress = Math.Clamp(_fadeClock.Elapsed.TotalMilliseconds / Math.Max(1, Motion.Normal.TotalMilliseconds), 0, 1);
        double eased = progress * progress * (3 - 2 * progress);
        SetOpacity(_fadeFrom + (_fadeTo - _fadeFrom) * eased);
        if (progress >= 1) _fadeTimer.Stop();
    }

    private void SetOpacity(double opacity)
    {
        _opacity = opacity;
        var hwnd = WindowChrome.GetHwnd(this);
        NativeMethods.AddExStyle(hwnd, NativeMethods.WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(opacity * 255), 2 /* LWA_ALPHA */);
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => QueuePlace();
    private void QueuePlace()
    {
        if (_closed || _placeQueued) return;
        _placeQueued = true;
        DispatcherQueue.TryEnqueue(() => { _placeQueued = false; Place(); });
    }

    private void Place()
    {
        if (_closed || _placing) return;
        _placing = true;
        try
        {
            var monitor = ScreenService.FromWindow(_snapshot?.WindowHandle ?? WindowChrome.GetHwnd((Window)_owner ?? this));
            var area = _owner is not null ? _owner.GetPlacementArea()
                : _snapshot?.IsWindowedShow == true ? WindowChrome.GetClientBounds(_snapshot.WindowHandle).FitInside(monitor.WorkArea)
                : monitor.Bounds;
            WindowChrome.MoveToMonitor(this, monitor);
            double dpi = WindowChrome.GetDpiScale(this);
            _card.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var design = _card.DesiredSize;
            double scale = Math.Min(_owner?.EffectiveScale ?? 1, Math.Min(area.Width / (design.Width * dpi), area.Height / (design.Height * dpi)));
            _scaled.Width = design.Width * scale; _scaled.Height = design.Height * scale;
            ScreenBox toolbar = default;
            if (_owner?.AppWindow.IsVisible == true)
            {
                var origin = WindowChrome.GetClientPosition(_owner);
                var size = _owner.AppWindow.ClientSize;
                toolbar = new(origin.X, origin.Y, size.Width, size.Height);
            }
            int gap = (int)Math.Round((IsVertical ? 6 : Placement == PptNavigationPlacement.BottomLeft ? 2 : 0) * dpi);
            var placed = NavigationLayout.Place((NavigationCorner)Placement, new(area.X, area.Y, area.Width, area.Height),
                (int)Math.Ceiling(_scaled.Width * dpi), (int)Math.Ceiling(_scaled.Height * dpi), gap, toolbar);
            var current = WindowChrome.GetClientPosition(this);
            if (current.X != placed.X || current.Y != placed.Y || AppWindow.ClientSize.Width != placed.Width || AppWindow.ClientSize.Height != placed.Height)
                WindowChrome.PlaceClient(this, placed.X, placed.Y, placed.Width, placed.Height);
        }
        finally { _placing = false; }
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
}
