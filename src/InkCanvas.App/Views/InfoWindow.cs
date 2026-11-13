using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>
/// A scrollable text window used for both the operating guide and the change log.
/// Renders a small subset of Markdown (headings, bullets, plain paragraphs) — enough for
/// documentation without pulling in a Markdown dependency.
/// </summary>
public sealed class InfoWindow : Window
{
    public InfoWindow(string title, string markdown, int theme)
    {
        Title = title;

        var body = new StackPanel { Spacing = 8, Padding = new Thickness(24) };
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { body.Children.Add(new Border { Height = 6 }); continue; }

            if (line.StartsWith("### "))
                body.Children.Add(Heading(line[4..], 15, Microsoft.UI.Text.FontWeights.SemiBold));
            else if (line.StartsWith("## "))
                body.Children.Add(Heading(line[3..], 18, Microsoft.UI.Text.FontWeights.SemiBold));
            else if (line.StartsWith("# "))
                body.Children.Add(Heading(line[2..], 22, Microsoft.UI.Text.FontWeights.Bold));
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                body.Children.Add(Paragraph("•  " + line[2..]));
            else
                body.Children.Add(Paragraph(line));
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var close = new TouchIconButton { Glyph = Glyphs.Close };
        ToolTipService.SetToolTip(close, "关闭");
        close.Click += (_, _) => Close();

        var headerText = new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(headerText, 0);
        Grid.SetColumn(close, 1);
        headerGrid.Children.Add(headerText);
        headerGrid.Children.Add(close);

        var header = new Border
        {
            Padding = new Thickness(24, 14, 16, 10),
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = headerGrid,
        };

        var root = new Grid { Background = (Brush)Application.Current.Resources["IcSurfaceBrush"] };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        Grid.SetRow(scroll, 1);
        root.Children.Add(header);
        root.Children.Add(scroll);
        Content = root;

        WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
        WindowChrome.CenterOnPrimary(this, 720, 760);
        WindowChrome.ApplyTheme(this, theme);
        Activated += (_, _) => Motion.PlayEntrance(body, 16f);
    }

    private static TextBlock Heading(string text, double size, Windows.UI.Text.FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 2),
    };

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
    };

    public static InfoWindow OpenGuide(int theme) => new("操作指南", GuideText, theme);

    public static InfoWindow OpenChangeLog(int theme)
    {
        string text;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
            text = File.Exists(path) ? File.ReadAllText(path) : "未找到更新日志文件。";
        }
        catch
        {
            text = "无法读取更新日志。";
        }
        return new InfoWindow("更新日志", text, theme);
    }

    private const string GuideText = """
    # Ink Canvas · WinUI 3 操作指南

    ## 三种模式
    - 屏幕画笔：透明覆盖层铺满主屏，可在任意窗口上书写。点工具栏的锁形按钮进入「穿透模式」，鼠标/触摸会落到下方程序。
    - 画板：点显示器图标打开黑板/白板，支持多页、方格与横线纸。
    - 幻灯片：检测到 PowerPoint / WPS 放映后，工具栏自动出现翻页按钮。

    ## 工具
    - 画笔 / 荧光笔：点按钮弹出调色板，可选颜色、粗细与笔迹风格。
    - 笔迹风格：「笔锋」笔尖收细、「速度笔锋」快写变细、「均匀线条」等宽。
    - 橡皮擦：按笔画擦除；手掌/手背接触面积够大时自动当作区域橡皮。
    - 选择：点笔画选中，拖动内部移动、拖四角缩放、拖上方圆点旋转。选中后工具栏出现删除按钮。
    - 图形：直线、虚线、箭头、圆与椭圆、双曲线、抛物线、坐标系、圆柱圆锥长方体等。

    ## 触摸与手势
    - 所有可点目标不小于 40×40 像素。
    - 双指平移 / 缩放 / 旋转；在设置里可分别开关。
    - 勾选「双指手势作用于选区」后，有选区时手势只变换选区。
    - 笔优先：压感笔在书写时，手指不会画出墨迹。

    ## 快捷键
    - Ctrl+Z 撤销，Ctrl+Y 重做
    - Alt+1 画笔，Alt+E 橡皮
    - Ctrl+Shift+C 清空

    ## 其它功能
    - 倒计时、抽奖（可导入 txt/csv 名单、可不重复）
    - 截图保存到「图片\\InkCanvas」
    - 墨迹回放：按绘制顺序重放当前墨迹，可调速与拖动进度
    - 设置里可检查更新、开关边缘手势抑制、开机自启

    ## 已知限制
    - 旧版 WPF 的 .isf 墨迹存档无法读取，新格式为 .icink。
    - 设置文件 Settings.json 与旧版键名兼容，可直接沿用。
    """;
}