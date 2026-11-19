using InkCanvas.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.Controls.Controls;

/// <summary>Compact pen-style choices sharing the palette's live stroke preview.</summary>
public sealed class PenStylePicker : UserControl
{
    private readonly SegmentedSelector _selector = new(OriginalMenuCatalog.PenStyles, columns: 3);
    private readonly List<TextBlock> _labels = new();
    public event Action SelectionChanged;

    public PenStylePicker()
    {
        AutomationProperties.SetName(this, "笔迹风格");
        string[] descriptions =
        {
            "起收笔较细，保留原有点集笔锋效果。",
            "沿用原有速率笔锋，粗细随采样点间距变化。",
            "保持固定线宽。",
            "动态去抖，粗细变化轻微，适合日常板书。",
            "按实际书写速度模拟压感，慢写较粗、快写较细。",
            "速度压感配合自然起笔和三次曲线收锋。",
            "更明显的粗细、转角和起收笔变化，可搭配斜椭圆笔尖。",
        };
        for (int index = 0; index < OriginalMenuCatalog.PenStyles.Count; index++)
        {
            var label = new TextBlock { Text = OriginalMenuCatalog.PenStyles[index], FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, IsTextScaleFactorEnabled = false };
            _selector.SetOptionContent(index, label);
            _selector.SetOptionHelp(index, descriptions[index]);
            _labels.Add(label);
        }
        _selector.SelectionChanged += () => SelectionChanged?.Invoke();
        Content = _selector;
        Update(1);
    }

    public int SelectedIndex { get => _selector.SelectedIndex; set => _selector.SelectedIndex = value; }

    public void Update(int theme)
    {
        var foreground = Theming.PopupBrushes.Foreground(theme);
        foreach (var label in _labels) label.Foreground = foreground;
    }
}
