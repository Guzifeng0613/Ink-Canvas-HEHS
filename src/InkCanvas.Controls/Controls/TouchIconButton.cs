using InkCanvas.Controls.Animation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
    private readonly TextBlock _aiText;
    private Grid _customIcon;
    private Microsoft.UI.Xaml.Shapes.Path _customIconOutline;

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
            FontFamily = new FontFamily("ms-appx:///Assets/SegoeFluentIcons.ttf#Segoe Fluent Icons"),
            FontSize = 20,
            // Apply the current value explicitly: assigning a value equal to the DP default
            // does not raise the change callback, which would leave the icon blank.
            Glyph = Glyph,
        };
        _aiText = new TextBlock
        {
            Text = "AI",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,

        };
        Content = _icon;

        PointerPressed += (_, _) => { if (Motion.AnimationsEnabled) Motion.Pulse(this, 0.92f); };
        Loaded += (_, _) =>
        {
            UpdateContent();
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

    /// <summary>Use a native vector mouse-pointer pictogram instead of a font glyph.</summary>
    public void UseMousePointerIcon()
    {
        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(3, 2),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new PolyLineSegment
        {
            Points = new PointCollection
            {
                new Windows.Foundation.Point(3, 22),
                new Windows.Foundation.Point(8, 17),
                new Windows.Foundation.Point(12, 23),
                new Windows.Foundation.Point(15, 21),
                new Windows.Foundation.Point(11, 15),
                new Windows.Foundation.Point(19, 15),
                new Windows.Foundation.Point(3, 2),
            },
        });
        var geometry = new PathGeometry { Figures = new PathFigureCollection { figure } };
        _customIconOutline = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = geometry,
            Fill = new SolidColorBrush(Microsoft.UI.Colors.White),
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 24,
            Height = 24,
        };
        _customIcon = new Grid { Width = 24, Height = 24 };
        _customIcon.Children.Add(_customIconOutline);
        Content = _customIcon;
        ApplyVisualState();
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TouchIconButton)d).ApplyVisualState();

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TouchIconButton)d).UpdateContent();

    /// <summary>
    /// Swaps the button content for the current glyph. <see cref="Glyphs.Ai"/> is a sentinel:
    /// it renders as a literal "AI" label instead of a Segoe Fluent Icons codepoint.
    /// </summary>
    private void UpdateContent()
    {
        // A vector pictogram (pass-through toggle) owns the content; leave it untouched.
        if (_customIcon is not null) return;

        if (Glyph == Glyphs.Ai)
        {
            Content = _aiText;
            ApplyIconForeground();
            return;
        }

        _icon.Glyph = Glyph ?? string.Empty;
        Content = _icon;
    }

    private void ApplyIconForeground()
    {
        var brush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
            IsSelected ? "IcAccentBrush" : "IcTextPrimaryBrush"];
        _icon.Foreground = brush;
        _aiText.Foreground = brush;
    }

    private void ApplyVisualState()
    {
        if (IsSelected)
            Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcAccentSoftBrush"];
        else
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        ApplyIconForeground();

        if (_customIconOutline is not null)
            _customIconOutline.Stroke = IsSelected
                ? (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcAccentBrush"]
                : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["IcTextPrimaryBrush"];
    }
}