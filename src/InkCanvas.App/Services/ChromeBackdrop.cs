using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Services;

/// <summary>
/// A clear desktop acrylic behind the XAML surface. Chrome already paints its own translucent
/// tint, so adding the default acrylic tint again makes it effectively opaque. Tool windows
/// also remain translucent when focus belongs to the application being annotated.
/// </summary>
internal sealed class ChromeBackdrop : SystemBackdrop
{
    private DesktopAcrylicController _controller;
    private SystemBackdropConfiguration _configuration;
    private SystemBackdropConfiguration _defaults;

    public string State => _controller?.State.ToString() ?? "Disconnected";

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        _defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        _configuration = new SystemBackdropConfiguration();
        RefreshConfiguration();
        if (!DesktopAcrylicController.IsSupported()) return;
        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin,
            TintOpacity = 0,
            LuminosityOpacity = 0,
            TintColor = Microsoft.UI.Colors.Transparent,
            FallbackColor = Microsoft.UI.Colors.Transparent,
        };
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(target);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        _defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        RefreshConfiguration();
    }

    private void RefreshConfiguration()
    {
        if (_configuration is null || _defaults is null) return;
        _configuration.Theme = _defaults.Theme;
        _configuration.IsHighContrast = _defaults.IsHighContrast;
        _configuration.HighContrastBackgroundColor = _defaults.HighContrastBackgroundColor;
        _configuration.IsInputActive = true;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
        _configuration = null;
        _defaults = null;
    }
}
