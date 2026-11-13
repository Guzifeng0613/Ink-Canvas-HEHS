using InkCanvas.Platform.Interop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;
using WinRT.Interop;

namespace InkCanvas.App.Services;

/// <summary>
/// Window chrome helpers shared by every WinUI 3 window in the app. All chrome windows are
/// borderless and optionally topmost so they can sit over the transparent ink overlay.
/// </summary>
public static class WindowChrome
{
    public static IntPtr GetHwnd(Window window) => WindowNative.GetWindowHandle(window);

    public static void MakeBorderless(Window window, bool topmost = true, bool resizable = false)
    {
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = resizable;
            presenter.IsMaximizable = resizable;
            presenter.IsMinimizable = resizable;
            presenter.IsAlwaysOnTop = topmost;
        }
        // No system backdrop: these windows paint their own tokenised surface.
        window.SystemBackdrop = null;
        SetRoundedCorners(window);
    }

    public static void Place(Window window, int x, int y, int width, int height)
        => window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

    /// <summary>Current monitor scale factor (logical DIPs per physical pixel).</summary>
    public static double GetDpiScale(Window window)
    {
        try
        {
            uint dpi = NativeMethods.GetDpiForWindow(GetHwnd(window));
            return dpi is 0 ? 1.0 : dpi / 96.0;
        }
        catch { return 1.0; }
    }

    /// <summary>
    /// Place a window using XAML logical coordinates. AppWindow expects physical pixels,
    /// therefore convert using that window's own monitor DPI.
    /// </summary>
    public static void PlaceDip(Window window, double xDip, double yDip, double widthDip, double heightDip)
    {
        double scale = GetDpiScale(window);
        Place(window,
            (int)Math.Round(xDip * scale), (int)Math.Round(yDip * scale),
            (int)Math.Ceiling(widthDip * scale), (int)Math.Ceiling(heightDip * scale));
    }

    /// <summary>
    /// Adds a draggable title row to a window that the original displayed as a movable window.
    /// The returned Grid owns both title bar and content; pass it as Window.Content.
    /// </summary>
    public static Grid AddTitleBar(Window window, string title, FrameworkElement content, bool closeButton = true)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 0),
        };
        var header = new Grid
        {
            MinHeight = 36,
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["IcSurfaceBrush"],
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(titleText, 0);
        header.Children.Add(titleText);

        if (closeButton)
        {
            var close = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = new Microsoft.UI.Xaml.Controls.FontIcon
                {
                    Glyph = "",
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons"),
                    FontSize = 12,
                },
                MinWidth = 44,
                MinHeight = 36,
                Padding = new Thickness(0),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            close.Click += (_, _) => window.Close();
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
        }

        header.PointerPressed += (_, e) =>
        {
            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current is not null)
            {
                if (current is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return;
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessageW(GetHwnd(window), NativeMethods.WM_NCLBUTTONDOWN,
                new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            e.Handled = true;
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(content, 1);
        root.Children.Add(header);
        root.Children.Add(content);
        return root;
    }

    /// <summary>0 = follow system, 1 = light, 2 = dark.</summary>
    public static void ApplyTheme(Window window, int theme)
    {
        if (window.Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    /// <summary>
    /// Re-assert topmost without activating. Both the overlay and the chrome are topmost, so
    /// this keeps the chrome above the ink surface without stealing focus from a slideshow.
    /// </summary>
    public static void EnsureAbove(Window window)
    {
        var hwnd = GetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    public static void CenterOnPrimary(Window window, int widthDip, int heightDip)
    {
        double scale = GetDpiScale(window);
        var work = Platform.System.ScreenService.WorkArea;
        int widthPx = (int)Math.Ceiling(widthDip * scale);
        int heightPx = (int)Math.Ceiling(heightDip * scale);
        Place(window, work.X + (work.Width - widthPx) / 2,
                      work.Y + (work.Height - heightPx) / 2, widthPx, heightPx);
    }

    /// <summary>
    /// Ask DWM to round the window itself. Keep the DWM frame out of the XAML client area:
    /// extending a glass frame over an opaque WinUI surface produces a white halo around the
    /// toolbar. The normal DWM non-client shadow remains managed by the window manager.
    /// </summary>
    public static void SetRoundedCorners(Window window)
    {
        var hwnd = GetHwnd(window);
        if (hwnd == IntPtr.Zero) return;
        int preference = 2; // DWMWCP_ROUND
        int policy = 2;     // DWMNCRP_ENABLED
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no rectangular white halo
        DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        DwmSetWindowAttribute(hwnd, 2 /* DWMWA_NCRENDERING_POLICY */, ref policy, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR */, ref borderColor, sizeof(int));
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}