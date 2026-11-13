namespace InkCanvas.Core.Theming;

public enum AppTheme { Light, Dark }

/// <summary>
/// 1:1 port of <c>MW_AutoTheme.cs</c> → <c>GetMainWindowTheme</c>.
/// <para>
/// Outside the screen-annotation mode (<c>currentMode != 0</c>) the theme follows the board
/// colour choice, not the app theme; inside it, the requested app theme wins.
/// </para>
/// <para>
/// The WPF original swapped <c>ResourceDictionary</c> files and listened to
/// <c>SystemEvents.UserPreferenceChanged</c>. On WinUI 3 the same effect comes from
/// <c>FrameworkElement.RequestedTheme</c> (<c>Default</c> already follows the system), so only
/// the decision logic is ported here.
/// </para>
/// <para>
/// <b>未验证</b>：按要求跳过行为验证（无单元测试、无实机验证）。
/// </para>
/// </summary>
public static class ThemeResolver
{
    /// <param name="currentMode">0 = screen annotation, non-zero = board / slideshow.</param>
    /// <param name="usingWhiteboard">Canvas.UsingWhiteboard.</param>
    /// <param name="requestedThemeIsLight">Whether the resolved app theme is light.</param>
    public static AppTheme GetMainWindowTheme(int currentMode, bool usingWhiteboard, bool requestedThemeIsLight)
    {
        if (currentMode != 0) return usingWhiteboard ? AppTheme.Light : AppTheme.Dark;
        return requestedThemeIsLight ? AppTheme.Light : AppTheme.Dark;
    }

    /// <summary>1:1 <c>SetBoardTheme</c>: the board is light exactly when whiteboard mode is on.</summary>
    public static AppTheme GetBoardTheme(bool usingWhiteboard)
        => usingWhiteboard ? AppTheme.Light : AppTheme.Dark;
}