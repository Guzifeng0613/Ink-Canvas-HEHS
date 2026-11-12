using InkCanvas.Controls.Animation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Controls.Controls;

/// <summary>
/// An icon button that meets the touch contract: at least a 40x40 hit target, no hover-only
/// affordance, a visible selected state, and Composition-driven press feedback.
/// </summary>
public sealed class TouchIconButton : Button
{
    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(TouchIconButton),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(TouchIconButton),
        new PropertyMetadata("", OnGlyphChanged));

    private readonly FontIcon _icon;

    public TouchIconButton()
    {
        MinWidth = 40;
        MinHeight = 40;
        Padding = new Thickness(8);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(0);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        _icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 20,
            // Apply the current value explicitly: assigning a value equal to the DP default
            // does not raise the change callback, which would leave the icon blank.
            Glyph = Glyph,
        };
        Content = _icon;

        PointerPressed += (_, _) => { if (Motion.AnimationsEnabled) Motion.Pulse(this, 0.92f); };
        Loaded += (_, _) =>
        {
            _icon.Glyph = Glyph;
            ApplyVisualState();
        };
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double IconSize
    {
        get => _icon.FontSize;
        set => _icon.FontSize = value;
    }

    /// <summary>Override for glyphs that live outside Segoe Fluent Icons (e.g. geometric shapes).</summary>
    public string IconFont
    {
        get => _icon.FontFamily.Source;
        set => _icon.FontFamily = new FontFamily(value);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TouchIconButton)d).ApplyVisualState();

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TouchIconButton)d)._icon.Glyph = e.NewValue as string ?? string.Empty;

    private void ApplyVisualState()
    {
        if (IsSelected)
        {
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcAccentSoftBrush"];
            _icon.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcAccentBrush"];
        }
        else
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _icon.Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcTextPrimaryBrush"];
        }
    }
}