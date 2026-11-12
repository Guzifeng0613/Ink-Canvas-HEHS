using InkCanvas.Core.Ink;
using InkCanvas.Ink;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.Controls.Controls;

/// <summary>
/// Pen colour / width / nib-style picker shown in a flyout from the floating bar.
/// Swatches are 40px so they are usable with a finger, not just a pen.
/// </summary>
public sealed class PenPalette : UserControl
{
    private static readonly (string Name, InkColor Color)[] Palette =
    {
        ("黑色", InkColor.Opaque(0, 0, 0)),
        ("白色", InkColor.Opaque(255, 255, 255)),
        ("红色", InkColor.Opaque(220, 38, 38)),
        ("橙色", InkColor.Opaque(234, 88, 12)),
        ("黄色", InkColor.Opaque(234, 179, 8)),
        ("绿色", InkColor.Opaque(22, 163, 74)),
        ("青色", InkColor.Opaque(6, 182, 212)),
        ("蓝色", InkColor.Opaque(37, 99, 235)),
        ("紫色", InkColor.Opaque(147, 51, 234)),
        ("洋红", InkColor.Opaque(219, 39, 119)),
    };

    private readonly Slider _width = new() { Minimum = 1, Maximum = 40, StepFrequency = 0.5, Width = 210 };
    private readonly ComboBox _taper = new() { Width = 210 };
    private readonly List<Border> _swatchBorders = new();
    private bool _suppress;

    public PenPalette()
    {
        var swatchHost = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        StackPanel row = null;
        for (int i = 0; i < Palette.Length; i++)
        {
            if (i % 5 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                swatchHost.Children.Add(row);
            }

            var (name, color) = Palette[i];
            var swatch = new Border
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(2),
                BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B)),
                Tag = color,
            };
            ToolTipService.SetToolTip(swatch, name);
            swatch.PointerPressed += (_, _) =>
            {
                if (Controller is null || _suppress) return;
                Controller.CurrentAttributes.Color = color;
                RefreshFromController();
            };
            _swatchBorders.Add(swatch);
            row.Children.Add(swatch);
        }

        _taper.Items.Add("笔锋（笔尖收细）");
        _taper.Items.Add("速度笔锋");
        _taper.Items.Add("均匀线条");
        _taper.SelectionChanged += (_, _) =>
        {
            if (Controller is null || _suppress) return;
            Controller.CurrentAttributes.TaperKind = _taper.SelectedIndex switch
            {
                1 => TaperKind.Velocity,
                2 => TaperKind.None,
                _ => TaperKind.Tail,
            };
        };

        _width.ValueChanged += (_, e) =>
        {
            if (Controller is null || _suppress) return;
            Controller.CurrentAttributes.BaseWidth = (float)e.NewValue;
        };

        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(12) };
        panel.Children.Add(Heading("颜色"));
        panel.Children.Add(swatchHost);
        panel.Children.Add(Heading("粗细"));
        panel.Children.Add(_width);
        panel.Children.Add(Heading("笔迹风格"));
        panel.Children.Add(_taper);

        var close = new Button { Content = "完成", HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 40 };
        close.Click += (_, _) => RequestClose?.Invoke();
        panel.Children.Add(close);

        Content = panel;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
    };

    public InkController Controller { get; set; }

    public Action RequestClose { get; set; }

    public void RefreshFromController()
    {
        if (Controller is null) return;
        _suppress = true;
        try
        {
            _width.Value = Controller.CurrentAttributes.BaseWidth;
            _taper.SelectedIndex = Controller.CurrentAttributes.TaperKind switch
            {
                TaperKind.Velocity => 1,
                TaperKind.None => 2,
                _ => 0,
            };
            var current = Controller.CurrentAttributes.Color;
            foreach (var b in _swatchBorders)
            {
                if (b.Tag is not InkColor c) continue;
                bool selected = c.R == current.R && c.G == current.G && c.B == current.B;
                b.BorderBrush = selected
                    ? (Brush)Application.Current.Resources["IcAccentBrush"]
                    : (Brush)Application.Current.Resources["IcStrokeBrush"];
                b.BorderThickness = new Thickness(selected ? 3 : 2);
            }
        }
        finally
        {
            _suppress = false;
        }
    }
}