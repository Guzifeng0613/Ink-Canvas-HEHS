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
        string[] basic = { "x", "y", "z", "t", "a", "b", "π", "e", "7", "8", "9", "+", "−", "(", ")", "=",
            "4", "5", "6", "×", "÷", "x²", "x³", "^", "1", "2", "3", "sin", "cos", "tan", "√", "abs",
            "0", ".", ",", "ln", "log", "←", "→", "⌫" };
        string[][] pages = { basic,
            new[] { "sin", "cos", "tan", "asin()", "acos()", "atan()", "sinh()", "cosh()", "√", "abs", "exp()", "ln", "log", "pi", "∞", "i", "<", ">", "≤", "≥", "&&", "°", "!", "%", "x", "y", "z", "u", "v", "t", "(", ")", "0", ".", ",", "{", "}", "←", "→", "⌫" },
            new[] { "Simplify()", "Expand()", "Factor()", "Numeric()", "Solve()", "Substitute()", "Derivative()", "Integral()", "Limit()", "Determinant()", "Inverse()", "Transpose()", "Rank()", "Real()", "Imaginary()", "abs", "x", "y", "z", "i", "=", "+", "−", "^", "1", "2", "3", "0", "[", "]", "{", "}", "(", ")", ",", ".", "/", "←", "→", "⌫" },
            new[] { "Point()", "Line()", "Segment()", "Circle()", "Polygon()", "Intersect()", "Midpoint()", "Vector()", "Parallel()", "Perpendicular()", "Rotate()", "Translate()", "Reflect()", "Dilate()", "Distance()", "Area()", "Curve()", "Surface()", "Plane()", "Sphere()", "Prism()", "Pyramid()", "Net()", "Revolve()", "x", "y", "z", "t", "a", "b", "pi", "^", "(", ")", ",", "0", "1", "←", "→", "⌫" } };
        var panel = new StackPanel { Spacing = 6 };
        var selector = new SegmentedSelector(new[] { "基本", "函数", "代数", "几何" }) { Height = 36 };
        var host = new ContentControl();
        void ShowPage()
        {
            var keys = pages[Math.Max(0, selector.SelectedIndex)];
            var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
            for (int column = 0; column < 8; column++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int row = 0; row < 5; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var button = new Button { Content = key.Replace("()", ""), MinWidth = 0, MinHeight = 44, Padding = new Thickness(2),
                    HorizontalAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false, FontSize = key.Length > 8 ? 10 : key.Length > 4 ? 12 : 17 };
                AutomationProperties.SetName(button, "公式键 " + key);
                button.Click += (_, _) => KeyPressed?.Invoke(key);
                Grid.SetColumn(button, i % 8); Grid.SetRow(button, i / 8); grid.Children.Add(button);
            }
            host.Content = grid;
        }
        selector.SelectionChanged += ShowPage;
        panel.Children.Add(selector); panel.Children.Add(host); selector.SelectedIndex = 0;
        Content = panel;
    }
    public void SendKey(string key) => KeyPressed?.Invoke(key);
}
