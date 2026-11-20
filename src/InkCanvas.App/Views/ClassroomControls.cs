using InkCanvas.Controls.Controls;
using InkCanvas.Controls.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace InkCanvas.App.Views;

internal static class ClassroomControls
{
    internal static TouchIconButton Button(string text, Action click = null, bool primary = false)
    {
        var button = new TouchIconButton
        {
            Content = text, FontSize = 18, MinHeight = 56, MinWidth = 56,
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 8, 12, 8),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(primary ? 0 : 1),
            SelectionBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(button, text);
        if (click is not null) button.Click += (_, _) => click();
        void Apply()
        {
            int theme = button.ActualTheme == ElementTheme.Dark ? 2 : 1;
            var foreground = primary ? new SolidColorBrush(Microsoft.UI.Colors.White) : PopupBrushes.Foreground(theme);
            button.Background = primary ? PopupBrushes.Accent() : PopupBrushes.Surface(theme);
            button.Foreground = foreground;
            button.BorderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128));
            button.Resources["ButtonForegroundPointerOver"] = foreground;
            button.Resources["ButtonForegroundPressed"] = foreground;
            button.Resources["ButtonBackgroundPointerOver"] = primary ? PopupBrushes.BadgeBackground(theme) : PopupBrushes.RingTrack(theme);
            button.Resources["ButtonBackgroundPressed"] = primary ? PopupBrushes.Accent() : PopupBrushes.RingTrack(theme);
        }
        button.Loaded += (_, _) => Apply();
        button.ActualThemeChanged += (_, _) => Apply();
        Apply();
        return button;
    }
}
