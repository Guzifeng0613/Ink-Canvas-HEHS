using InkCanvas.Core.Ink;

namespace InkCanvas.Core.Settings;

/// <summary>
/// Menu labels, palette colours, and control ranges transcribed from the WPF menu at
/// commit 8b2c95b. Keep persistence values in the owning settings model.
/// </summary>
public static class OriginalMenuCatalog
{
    public readonly record struct Swatch(string Name, InkColor DarkTheme, InkColor LightTheme);

    public static IReadOnlyList<Swatch> PenColors { get; } = Array.AsReadOnly(new[]
    {
        new Swatch("黑色", InkColor.Opaque(0, 0, 0), InkColor.Opaque(0, 0, 0)),
        new Swatch("白色", InkColor.Opaque(255, 255, 255), InkColor.Opaque(255, 255, 255)),
        new Swatch("红色", InkColor.Opaque(220, 38, 38), InkColor.Opaque(239, 68, 68)),
        new Swatch("粉色", InkColor.Opaque(147, 51, 234), InkColor.Opaque(236, 72, 153)),
        new Swatch("橙色", InkColor.Opaque(234, 88, 12), InkColor.Opaque(249, 115, 22)),
        new Swatch("黄色", InkColor.Opaque(234, 179, 8), InkColor.Opaque(250, 204, 21)),
        new Swatch("绿色", InkColor.Opaque(22, 163, 74), InkColor.Opaque(34, 197, 94)),
        new Swatch("青色", InkColor.Opaque(13, 148, 136), InkColor.Opaque(20, 184, 166)),
        new Swatch("蓝色", InkColor.Opaque(37, 99, 235), InkColor.Opaque(59, 130, 246)),
    });

    public static IReadOnlyList<Swatch> HighlighterColors { get; } = Array.AsReadOnly(new[]
    {
        new Swatch("荧光笔红色", InkColor.Opaque(220, 38, 38), InkColor.Opaque(220, 38, 38)),
        new Swatch("荧光笔橙色", InkColor.Opaque(234, 88, 12), InkColor.Opaque(234, 88, 12)),
        new Swatch("荧光笔黄色", InkColor.Opaque(234, 179, 8), InkColor.Opaque(234, 179, 8)),
        new Swatch("荧光笔青色", InkColor.Opaque(13, 148, 136), InkColor.Opaque(13, 148, 136)),
        new Swatch("荧光笔蓝色", InkColor.Opaque(37, 99, 235), InkColor.Opaque(37, 99, 235)),
        new Swatch("荧光笔紫色", InkColor.Opaque(147, 51, 234), InkColor.Opaque(147, 51, 234)),
    });

    public static IReadOnlyList<string> PenStyles { get; } = Array.AsReadOnly(new[]
    {
        "点集笔锋", "速率笔锋", "关闭笔锋", "平滑硬笔", "柔和压感", "自然笔锋", "书法笔锋",
    });

    public static IReadOnlyList<string> EraserSizes { get; } = Array.AsReadOnly(new[]
    {
        "小", "较小", "中（默认）", "较大", "大",
    });

    public static IReadOnlyList<string> EraserModes { get; } = Array.AsReadOnly(new[]
    {
        "图标切换", "面积擦", "线条擦",
    });

    public static IReadOnlyList<int> AutoDeleteDays { get; } = Array.AsReadOnly(new[]
    {
        1, 3, 5, 7, 15, 30, 60, 100, 365,
    });

    public static IReadOnlyList<string> ThemeOptions { get; } = Array.AsReadOnly(new[]
    {
        "浅色主题", "深色主题", "跟随系统",
    });

    public const double PenWidthMinimum = 1;
    public const double PenWidthMaximum = 30;
    public const double PenWidthStep = 1;
    public const double HighlighterAlphaMinimum = 20;
    public const double HighlighterAlphaMaximum = 240;
    public const double HighlighterAlphaStep = 20;

    public static double ToStoredPenWidth(double menuWidth) => menuWidth / 2;

    public static double ToMenuPenWidth(double storedWidth) => storedWidth * 2;
}
