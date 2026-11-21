using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Controls.Controls;

/// <summary>
/// Single source for the bundled icon font family (Fluent System Icons, MIT).
/// The style toggle (Settings.json <c>appearance.isIconRegularStyle</c>) switches between the
/// Filled and Regular faces of the SAME MIT font set, keeping icons identical across
/// Windows 10/11 and the HarmonyOS port. Both faces are always deployed; reads are cheap
/// and cached per style value so hot paths (every FontIcon) don't re-parse the settings.
/// </summary>
public static class IconFontStyle
{
    private const string FilledFace = "ms-appx:///Assets/FluentSystemIcons.ttf#FluentSystemIcons-Filled";
    private const string RegularFace = "ms-appx:///Assets/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular";

    private static FontFamily? _filled;
    private static FontFamily? _regular;
    private static int _appliedStyle = -1;
    private static Func<bool>? _regularRequested;

    /// <summary>Injects the settings probe (avoids a hard dependency on Features from Controls).</summary>
    public static void BindStyleSource(Func<bool> isRegularRequested)
    {
        _regularRequested = isRegularRequested;
        _appliedStyle = -1;
    }

    /// <summary>The font family for the CURRENT style; cached until the style value changes.</summary>
    public static FontFamily Font
    {
        get
        {
            var regular = _regularRequested?.Invoke() == true;
            if (regular)
            {
                if (_regular is null || _appliedStyle != 1)
                {
                    _regular = new FontFamily(RegularFace);
                    _appliedStyle = 1;
                }
                return _regular;
            }
            if (_filled is null || _appliedStyle != 0)
            {
                _filled = new FontFamily(FilledFace);
                _appliedStyle = 0;
            }
            return _filled;
        }
    }

    /// <summary>Family string for direct use where a <see cref="FontFamily"/> object isn't suitable.</summary>
    public static string Family => _regularRequested?.Invoke() == true ? RegularFace : FilledFace;
}