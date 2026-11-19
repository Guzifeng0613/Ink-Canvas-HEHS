using InkCanvas.Controls.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace InkCanvas.Modules.AnalyticGeometry;

public sealed class FormulaKeyboard : UserControl
{
    public event Action<string> KeyPressed;
    public FormulaKeyboard()
    {
        string[] keys = { "x", "y", "t", "a", "b", "c", "π", "e", "7", "8", "9", "+", "−", "(", ")", "=",
            "4", "5", "6", "×", "÷", "x²", "x³", "^", "1", "2", "3", "sin", "cos", "tan", "√", "abs",
            "0", ".", ",", "ln", "log", "←", "→", "⌫" };
        var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
        for (int column = 0; column < 8; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 5; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            var button = new Button { Content = key, MinWidth = 0, MinHeight = 44, Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = 17 };
            AutomationProperties.SetName(button, "公式键 " + key);
            button.Click += (_, _) => KeyPressed?.Invoke(key);
            Grid.SetColumn(button, i % 8); Grid.SetRow(button, i / 8); grid.Children.Add(button);
        }
        Content = grid;
    }
    public void SendKey(string key) => KeyPressed?.Invoke(key);
}
