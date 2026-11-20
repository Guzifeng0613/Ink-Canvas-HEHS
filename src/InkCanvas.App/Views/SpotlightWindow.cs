using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Features.Classroom;
using InkCanvas.Platform.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace InkCanvas.App.Views;

/// <summary>
/// 聚光灯（WinUI 3 全屏透明窗口）。用 <see cref="Path"/> + <see cref="GeometryGroup"/>
/// （EvenOdd）在整块半透明黑色遮罩上挖出圆形/矩形亮区；支持拖动移动亮区、滚轮缩放、
/// 右键菜单/顶部工具条切换形状与遮罩透明度、Esc 退出。
/// <para>全屏时覆盖书写层，关闭后恢复——聚光灯与批注互斥由使用者决定。</para>
/// </summary>
public sealed class SpotlightWindow : Window
{
    private readonly Grid _root = new();
    private readonly Path _mask = new();
    private readonly Grid _hit = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };

    private SpotlightShape _shape = SpotlightShape.Circle;
    private byte _maskAlpha = 0xC0;
    private double _centerX;
    private double _centerY;
    private double _radius = 120;
    private bool _initialized;
    private bool _dragging;

    public SpotlightWindow()
    {
        Title = "聚光灯";

        _mask.IsHitTestVisible = false;

        _root.Children.Add(_mask);
        _root.Children.Add(_hit);
        _root.Children.Add(BuildToolbar());

        _hit.PointerPressed += OnPointerPressed;
        _hit.PointerMoved += OnPointerMoved;
        _hit.PointerReleased += OnPointerReleased;
        _hit.PointerWheelChanged += OnPointerWheelChanged;
        _hit.ContextFlyout = BuildContextMenu();

        _root.SizeChanged += (_, e) =>
        {
            if (!_initialized && e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                _centerX = e.NewSize.Width / 2.0;
                _centerY = e.NewSize.Height / 2.0;
                _radius = Math.Min(e.NewSize.Width, e.NewSize.Height) * 0.22;
                _initialized = true;
            }
            UpdateGeometry();
        };

        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, e) => { Close(); e.Handled = true; };
        _root.KeyboardAccelerators.Add(escape);

        Content = _root;

        WindowChrome.MakeBorderless(this, topmost: true, resizable: false);
        var work = ScreenService.WorkArea;
        WindowChrome.Place(this, work.X, work.Y, work.Width, work.Height);

        UpdateGeometry();
        Activated += (_, _) => Motion.PlayEntrance(_root, 0f);
    }

    private FrameworkElement BuildToolbar()
    {
        var circle = new Button { Content = "圆形", MinHeight = 32, MinWidth = 56, Margin = new Thickness(2) };
        circle.Click += (_, _) => { _shape = SpotlightShape.Circle; UpdateGeometry(); };

        var rect = new Button { Content = "矩形", MinHeight = 32, MinWidth = 56, Margin = new Thickness(2) };
        rect.Click += (_, _) => { _shape = SpotlightShape.Rect; UpdateGeometry(); };

        var dimmer = new Button { Content = "更暗", MinHeight = 32, MinWidth = 56, Margin = new Thickness(2) };
        dimmer.Click += (_, _) => AdjustMask(24);

        var lighter = new Button { Content = "更亮", MinHeight = 32, MinWidth = 56, Margin = new Thickness(2) };
        lighter.Click += (_, _) => AdjustMask(-24);

        var exit = new Button { Content = "退出", MinHeight = 32, MinWidth = 56, Margin = new Thickness(2) };
        exit.Click += (_, _) => Close();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.Add(circle);
        row.Children.Add(rect);
        row.Children.Add(dimmer);
        row.Children.Add(lighter);
        row.Children.Add(exit);

        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = row,
        };
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();
        var circle = new MenuFlyoutItem { Text = "圆形亮区" };
        circle.Click += (_, _) => { _shape = SpotlightShape.Circle; UpdateGeometry(); };
        var rect = new MenuFlyoutItem { Text = "矩形亮区" };
        rect.Click += (_, _) => { _shape = SpotlightShape.Rect; UpdateGeometry(); };
        var darker = new MenuFlyoutItem { Text = "遮罩更暗" };
        darker.Click += (_, _) => AdjustMask(24);
        var brighter = new MenuFlyoutItem { Text = "遮罩更亮" };
        brighter.Click += (_, _) => AdjustMask(-24);
        var exit = new MenuFlyoutItem { Text = "退出聚光灯" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(circle);
        menu.Items.Add(rect);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(darker);
        menu.Items.Add(brighter);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void AdjustMask(int delta)
    {
        _maskAlpha = (byte)Math.Clamp(_maskAlpha + delta, 0x30, 0xEE);
        UpdateGeometry();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_hit);
        if (!point.Properties.IsLeftButtonPressed) return;
        _dragging = true;
        MoveCenterTo(point.Position);
        _hit.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        MoveCenterTo(e.GetCurrentPoint(_hit).Position);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        _hit.ReleasePointerCapture(e.Pointer);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(_hit).Properties.MouseWheelDelta;
        _radius = SpotlightGeometry.ClampRadius(_radius + (delta > 0 ? 20 : -20),
            _root.ActualWidth, _root.ActualHeight);
        UpdateGeometry();
        e.Handled = true;
    }

    private void MoveCenterTo(Point position)
    {
        var (x, y) = SpotlightGeometry.ClampCenter(
            position.X, position.Y, _root.ActualWidth, _root.ActualHeight, _radius);
        _centerX = x;
        _centerY = y;
        UpdateGeometry();
    }

    private void UpdateGeometry()
    {
        double width = _root.ActualWidth;
        double height = _root.ActualHeight;
        if (width <= 0 || height <= 0) return;

        _radius = SpotlightGeometry.ClampRadius(_radius, width, height);
        var (cx, cy) = SpotlightGeometry.ClampCenter(_centerX, _centerY, width, height, _radius);
        _centerX = cx;
        _centerY = cy;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, width, height) });
        if (_shape == SpotlightShape.Circle)
        {
            group.Children.Add(new EllipseGeometry
            {
                Center = new Point(cx, cy),
                RadiusX = _radius,
                RadiusY = _radius,
            });
        }
        else
        {
            group.Children.Add(new RectangleGeometry
            {
                Rect = new Rect(cx - _radius, cy - _radius, _radius * 2, _radius * 2),
            });
        }

        _mask.Data = group;
        _mask.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(_maskAlpha, 0, 0, 0));
    }
}