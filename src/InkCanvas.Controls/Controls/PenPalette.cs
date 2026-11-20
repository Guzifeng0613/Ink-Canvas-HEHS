using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Controls.Controls;

/// <summary>WPF-parity pen and highlighter palette, sized to fit its host and scrollable at high DPI.</summary>
public sealed class PenPalette : UserControl
{
    private readonly Slider _width = new()
    {
        Minimum = OriginalMenuCatalog.PenWidthMinimum,
        Maximum = OriginalMenuCatalog.PenWidthMaximum,
        StepFrequency = OriginalMenuCatalog.PenWidthStep,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Slider _alpha = new()
    {
        Minimum = OriginalMenuCatalog.HighlighterAlphaMinimum,
        Maximum = OriginalMenuCatalog.HighlighterAlphaMaximum,
        StepFrequency = OriginalMenuCatalog.HighlighterAlphaStep,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly TextBlock _widthValue = new() { MinWidth = 48, TextAlignment = TextAlignment.Right };
    private readonly TextBlock _alphaValue = new() { Text = "140", MinWidth = 48, TextAlignment = TextAlignment.Right };
    private readonly ComboBox _style = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _alphaAvailability = new()
    {
        Text = "透明度尚未接入笔迹模型，因此不可调整。",
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
    };
    private readonly List<(Button Button, OriginalMenuCatalog.Swatch Swatch)> _swatches = new();
    private bool _suppress;
    private Action<int> _inkAlphaChanged;
    private int _highlighterAlpha = 140;

    public PenPalette()
    {
        foreach (string option in OriginalMenuCatalog.PenStyles) _style.Items.Add(option);
        _style.SelectedIndex = 0;
        _alpha.Value = _highlighterAlpha;
        Accessible(_style, "笔迹风格", "点集笔锋、速率笔锋、关闭笔锋、自然笔锋");
        _style.SelectionChanged += (_, _) =>
        {
            if (Controller is null || _suppress || _style.SelectedIndex < 0) return;
            Controller.CurrentAttributes.TaperKind = _style.SelectedIndex switch
            {
                1 => TaperKind.Velocity,
                2 => TaperKind.None,
                3 => TaperKind.Natural,
                _ => TaperKind.Tail,
            };
            InkStyleChanged?.Invoke(_style.SelectedIndex);
            AttributesChanged?.Invoke();
        };

        _width.ValueChanged += (_, e) =>
        {
            if (Controller is null || _suppress) return;
            double snapped = Snap(e.NewValue, OriginalMenuCatalog.PenWidthMinimum,
                OriginalMenuCatalog.PenWidthStep);
            if (Math.Abs(snapped - e.NewValue) > 0.001)
            {
                _width.Value = snapped;
                return;
            }

            double stored = OriginalMenuCatalog.ToStoredPenWidth(snapped);
            _widthValue.Text = stored.ToString("0.##");
            Controller.CurrentAttributes.BaseWidth = (float)stored;
            InkWidthChanged?.Invoke(stored);
            AttributesChanged?.Invoke();
        };

        _alpha.ValueChanged += (_, e) =>
        {
            if (Controller is null || _suppress || InkAlphaChanged is null) return;
            int snapped = (int)Snap(e.NewValue, OriginalMenuCatalog.HighlighterAlphaMinimum,
                OriginalMenuCatalog.HighlighterAlphaStep);
            if (Math.Abs(snapped - e.NewValue) > 0.001)
            {
                _alpha.Value = snapped;
                return;
            }
            _highlighterAlpha = snapped;
            _alphaValue.Text = snapped.ToString();
            InkAlphaChanged.Invoke(snapped);
            AttributesChanged?.Invoke();
        };

        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(12) };
        panel.Children.Add(Heading("颜色"));
        panel.Children.Add(SwatchGrid(OriginalMenuCatalog.PenColors));
        panel.Children.Add(Heading("荧光笔颜色"));
        panel.Children.Add(SwatchGrid(OriginalMenuCatalog.HighlighterColors));
        panel.Children.Add(Heading("粗细（原菜单刻度）"));
        panel.Children.Add(SliderWithValue(_width, _widthValue, "画笔粗细，范围 1 到 30，步长 1"));
        panel.Children.Add(Heading("笔迹风格"));
        panel.Children.Add(_style);
        panel.Children.Add(Heading("荧光笔透明度"));
        panel.Children.Add(SliderWithValue(_alpha, _alphaValue, "荧光笔透明度，范围 20 到 240，步长 20"));
        panel.Children.Add(_alphaAvailability);

        var close = new Button
        {
            Content = "完成",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40,
        };
        Accessible(close, "完成并关闭调色盘");
        close.Click += (_, _) => RequestClose?.Invoke();
        panel.Children.Add(close);

        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        UpdateAlphaAvailability();
    }

    private Grid SwatchGrid(IReadOnlyList<OriginalMenuCatalog.Swatch> colors)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int rows = (colors.Count + 2) / 3;
        for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < colors.Count; i++)
        {
            var swatch = colors[i];
            var button = new Button
            {
                Width = 44,
                Height = 44,
                MinWidth = 44,
                MinHeight = 44,
                Padding = new Thickness(4),
                Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(ToWindowsColor(GetSwatchColor(swatch))),
                BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(22),
                Tag = swatch,
            };
            Accessible(button, swatch.Name, "选择此墨迹颜色");
            button.Click += (_, _) =>
            {
                if (Controller is null || _suppress) return;
                Controller.CurrentAttributes.Color = GetSwatchColor(swatch);
                AttributesChanged?.Invoke();
                RefreshFromController();
            };
            _swatches.Add((button, swatch));
            Grid.SetColumn(button, i % 3);
            Grid.SetRow(button, i / 3);
            grid.Children.Add(button);
        }
        return grid;
    }

    private static UIElement SliderWithValue(Slider slider, TextBlock readout, string name)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Accessible(slider, name);
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(readout, 1);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    private InkColor GetSwatchColor(OriginalMenuCatalog.Swatch swatch) =>
        _lightColorScheme ? swatch.LightTheme : swatch.DarkTheme;

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Windows.UI.Color ToWindowsColor(InkColor color) =>
        Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

    private static double Snap(double value, double minimum, double step) =>
        minimum + Math.Round((value - minimum) / step, MidpointRounding.AwayFromZero) * step;

    private static void Accessible(DependencyObject control, string name, string helpText = null)
    {
        AutomationProperties.SetName(control, name);
        if (!string.IsNullOrWhiteSpace(helpText)) AutomationProperties.SetHelpText(control, helpText);
    }

    public InkController Controller { get; set; }

    public Action RequestClose { get; set; }

    /// <summary>Optional persistence callbacks; persisted width is stored in Canvas.InkWidth units.</summary>
    public Action<double> InkWidthChanged { get; set; }
    public Action<int> InkStyleChanged { get; set; }
    public Action<int> InkAlphaChanged
    {
        get => _inkAlphaChanged;
        set
        {
            _inkAlphaChanged = value;
            _suppress = true;
            try
            {
                if (_inkAlphaChanged is not null)
                {
                    _alpha.Value = Math.Clamp(_highlighterAlpha, OriginalMenuCatalog.HighlighterAlphaMinimum,
                        OriginalMenuCatalog.HighlighterAlphaMaximum);
                    _alphaValue.Text = _highlighterAlpha.ToString();
                }
            }
            finally
            {
                _suppress = false;
            }
            UpdateAlphaAvailability();
        }
    }

    public int HighlighterAlpha
    {
        get => _highlighterAlpha;
        set
        {
            _highlighterAlpha = (int)Snap(Math.Clamp(value,
                (int)OriginalMenuCatalog.HighlighterAlphaMinimum,
                (int)OriginalMenuCatalog.HighlighterAlphaMaximum),
                OriginalMenuCatalog.HighlighterAlphaMinimum, OriginalMenuCatalog.HighlighterAlphaStep);
            if (InkAlphaChanged is null) return;
            _suppress = true;
            try
            {
                _alpha.Value = _highlighterAlpha;
                _alphaValue.Text = _highlighterAlpha.ToString();
            }
            finally { _suppress = false; }
        }
    }

    /// <summary>Raised after a color, width, style or alpha selection is applied.</summary>
    public Action AttributesChanged { get; set; }

    /// <summary>Controls which of the original theme-specific nine pen swatches is used.</summary>
    public bool LightColorScheme
    {
        get => _lightColorScheme;
        set
        {
            if (_lightColorScheme == value) return;
            _lightColorScheme = value;
            RefreshFromController();
        }
    }

    private bool _lightColorScheme;

    public void RefreshFromController()
    {
        if (Controller is null) return;
        _suppress = true;
        try
        {
            _width.Value = Math.Clamp(OriginalMenuCatalog.ToMenuPenWidth(Controller.CurrentAttributes.BaseWidth),
                OriginalMenuCatalog.PenWidthMinimum, OriginalMenuCatalog.PenWidthMaximum);
            _widthValue.Text = Controller.CurrentAttributes.BaseWidth.ToString("0.##");
            _style.SelectedIndex = Controller.CurrentAttributes.TaperKind switch
            {
                TaperKind.Velocity => 1,
                TaperKind.None => 2,
                TaperKind.Natural => 3,
                _ => 0,
            };
            if (InkAlphaChanged is not null)
            {
                _alpha.Value = Math.Clamp(HighlighterAlpha,
                    OriginalMenuCatalog.HighlighterAlphaMinimum,
                    OriginalMenuCatalog.HighlighterAlphaMaximum);
                _alphaValue.Text = HighlighterAlpha.ToString();
            }
            var current = Controller.CurrentAttributes.Color;
            foreach (var (button, swatch) in _swatches)
            {
                InkColor candidate = GetSwatchColor(swatch);
                button.Background = new SolidColorBrush(ToWindowsColor(candidate));
                bool selected = candidate.R == current.R && candidate.G == current.G && candidate.B == current.B;
                button.BorderBrush = selected
                    ? (Brush)Application.Current.Resources["IcAccentBrush"]
                    : (Brush)Application.Current.Resources["IcStrokeBrush"];
                button.BorderThickness = new Thickness(selected ? 3 : 2);
            }
        }
        finally
        {
            _suppress = false;
            UpdateAlphaAvailability();
        }
    }

    private void UpdateAlphaAvailability()
    {
        bool wired = InkAlphaChanged is not null;
        _alpha.IsEnabled = wired;
        _alphaAvailability.Text = wired
            ? "原菜单范围 20–240，步长 20。"
            : "透明度尚未接入笔迹模型，因此不可调整。";
        _alphaAvailability.Visibility = wired ? Visibility.Collapsed : Visibility.Visible;
        Accessible(_alpha, "荧光笔透明度", wired
            ? "原菜单范围 20 到 240，步长 20"
            : "暂不可用：荧光笔透明度尚未接入笔迹模型");
    }
}
