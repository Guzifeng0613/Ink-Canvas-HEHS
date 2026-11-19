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
    private bool _connected, _ownsLayeredStyle;
    private int _appliedAlpha = -1;
    private DesktopAcrylicController _controller;
    private SystemBackdropConfiguration _configuration;
    private SystemBackdropConfiguration _defaults;
    private int _theme;

    public ChromeBackdrop(IntPtr window) => _window = window;

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

    public string State => _controller?.State.ToString() ?? "Disconnected";

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        _connected = true;
        _defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        _configuration = new SystemBackdropConfiguration();
        RefreshConfiguration();
        if (!DesktopAcrylicController.IsSupported()) { UpdateOpacity(); return; }
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintOpacity = 0,
            LuminosityOpacity = 0,
            TintColor = Microsoft.UI.Colors.Transparent,
        };
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.StateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateOpacity);
        ApplyColors();
        _controller.AddSystemBackdropTarget(target);
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
        if (_controller is null) return;
        // Acrylic fallback is a solid material. Transparent black there produces gray/black
        // chrome when transparency is disabled, during remote sessions or policy changes.
        bool dark = _configuration.Theme == SystemBackdropTheme.Dark;
        _controller.FallbackColor = dark ? Windows.UI.Color.FromArgb(255, 31, 31, 31) : Microsoft.UI.Colors.White;
        _controller.TintColor = Microsoft.UI.Colors.Transparent;
        _controller.TintOpacity = 0;
        _controller.LuminosityOpacity = 0;
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
        _configuration.Theme = _theme switch { 1 => SystemBackdropTheme.Light, 2 => SystemBackdropTheme.Dark, _ => _defaults.Theme };
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
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        _defaults = null;
    }
}
