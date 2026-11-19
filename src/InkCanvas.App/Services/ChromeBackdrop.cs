using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using InkCanvas.Platform.Interop;
using System.Runtime.InteropServices;

namespace InkCanvas.App.Services;

/// <summary>
/// Clear acrylic behind the XAML tint, with native window alpha when acrylic is unavailable.
/// High contrast retains full opacity. No custom compositor is attached to the XAML HWND.
/// </summary>
internal sealed class ChromeBackdrop : SystemBackdrop
{
    private readonly IntPtr _window;
    private readonly bool _enableAcrylic;
    private bool _connected, _ownsLayeredStyle;
    private int _appliedAlpha = -1;
    private DesktopAcrylicController _controller;
    private SystemBackdropConfiguration _configuration;
    private SystemBackdropConfiguration _defaults;
    private int _theme;
    private Windows.UI.Composition.Compositor _fallbackCompositor;
    private Windows.UI.Composition.CompositionColorBrush _fallback;

    public ChromeBackdrop(IntPtr window, bool enableAcrylic = true)
    {
        _window = window;
        _enableAcrylic = enableAcrylic;
    }

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    public string State => _controller?.State.ToString() ?? (_fallback is null ? "Disconnected" : "Fallback");

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // Initializes the configuration and subscribes to OS theme/high-contrast changes.
        base.OnTargetConnected(target, xamlRoot);
        _connected = true;
        _defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        _configuration = new SystemBackdropConfiguration();
        RefreshConfiguration();
        if (_enableAcrylic && DesktopAcrylicController.IsSupported())
        {
            try
            {
                _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
                _controller.SetSystemBackdropConfiguration(_configuration);
                ApplyColors();
                if (!_controller.AddSystemBackdropTarget(target)) ReleaseController(target);
                else _controller.StateChanged += OnStateChanged;
            }
            catch (COMException) { ReleaseController(target); }
        }
        if (_controller is null)
        {
            _fallbackCompositor = new Windows.UI.Composition.Compositor();
            _fallback = _fallbackCompositor.CreateColorBrush();
            target.SystemBackdrop = _fallback;
        }
        ApplyColors();
        DispatcherQueue.TryEnqueue(UpdateOpacity);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        _defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        RefreshConfiguration();
        ApplyColors();
        UpdateOpacity();
    }

    public void SetTheme(int theme)
    {
        _theme = theme;
        RefreshConfiguration();
        ApplyColors();
        UpdateOpacity();
    }

    private void ApplyColors()
    {
        if (_configuration is null) return;
        bool dark = _configuration.Theme == SystemBackdropTheme.Dark;
        var themeColor = dark ? Windows.UI.Color.FromArgb(255, 31, 31, 31) : Microsoft.UI.Colors.White;
        var color = _configuration.IsHighContrast ? _configuration.HighContrastBackgroundColor ?? themeColor : themeColor;
        if (_fallback is not null) _fallback.Color = color;
        if (_controller is null) return;
        _controller.FallbackColor = color;
        // Keep the tint color opaque and use the dedicated opacity properties to make
        // the material clear. Transparent black tints produce grey surfaces on some DWM paths.
        _controller.TintColor = color;
        _controller.TintOpacity = 0;
        _controller.LuminosityOpacity = 0;
    }

    private void OnStateChanged(ISystemBackdropController sender, object args)
        => DispatcherQueue.TryEnqueue(UpdateOpacity);

    private void ReleaseController(ICompositionSupportsSystemBackdrop target)
    {
        if (_controller is null) return;
        _controller.StateChanged -= OnStateChanged;
        _controller.RemoveSystemBackdropTarget(target);
        _controller.Dispose();
        _controller = null;
    }

    private void UpdateOpacity()
    {
        if (!_connected) return;
        // Native alpha keeps chrome translucent on machines where desktop acrylic falls back
        // to a solid brush. High contrast keeps full opacity for readability.
        int alpha = _configuration?.IsHighContrast == true || _controller?.State == SystemBackdropState.Active ? 255 : 224;
        if (alpha == _appliedAlpha) return;
        if (alpha < 255 && !NativeMethods.HasExStyle(_window, NativeMethods.WS_EX_LAYERED))
        {
            NativeMethods.AddExStyle(_window, NativeMethods.WS_EX_LAYERED);
            _ownsLayeredStyle = true;
        }
        if (NativeMethods.HasExStyle(_window, NativeMethods.WS_EX_LAYERED))
            if (!SetLayeredWindowAttributes(_window, 0, (byte)alpha, 2 /* LWA_ALPHA */)) return;
        if (alpha == 255 && _ownsLayeredStyle)
        {
            NativeMethods.RemoveExStyle(_window, NativeMethods.WS_EX_LAYERED);
            _ownsLayeredStyle = false;
        }
        _appliedAlpha = alpha;
    }

    private void RefreshConfiguration()
    {
        if (_configuration is null || _defaults is null) return;
        _configuration.Theme = _theme switch
        {
            1 => SystemBackdropTheme.Light,
            2 => SystemBackdropTheme.Dark,
            _ => _defaults.Theme == SystemBackdropTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
        };
        _configuration.IsHighContrast = _defaults.IsHighContrast;
        _configuration.HighContrastBackgroundColor = _defaults.HighContrastBackgroundColor;
        _configuration.IsInputActive = true;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _connected = false;
        if (_ownsLayeredStyle)
        {
            SetLayeredWindowAttributes(_window, 0, 255, 2);
            NativeMethods.RemoveExStyle(_window, NativeMethods.WS_EX_LAYERED);
            _ownsLayeredStyle = false;
        }
        _appliedAlpha = -1;
        ReleaseController(target);
        if (_fallback is not null)
        {
            target.SystemBackdrop = null;
            _fallback.Dispose();
            _fallback = null;
            _fallbackCompositor.Dispose();
            _fallbackCompositor = null;
        }
        _configuration = null;
        _defaults = null;
        base.OnTargetDisconnected(target);
    }
}
