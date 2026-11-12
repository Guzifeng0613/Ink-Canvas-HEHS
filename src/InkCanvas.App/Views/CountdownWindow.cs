using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Features.Tools;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>Classroom countdown timer.</summary>
public sealed class CountdownWindow : Window, IDisposable
{
    private readonly CountdownTimer _timer = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly TextBlock _display = new()
    {
        FontSize = 72,
        FontWeight = Microsoft.UI.Text.FontWeights.Light,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly Button _startPause = new() { Content = "开始", MinHeight = 44, MinWidth = 96 };

    public CountdownWindow()
    {
        Title = "倒计时";
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        _timer.Tick += remaining => _dispatcher.TryEnqueue(() => Render(remaining));
        _timer.Finished += () => _dispatcher.TryEnqueue(() =>
        {
            _display.Text = "00:00";
            _startPause.Content = "开始";
            Motion.Pulse(_display, 1.15f);
        });

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (int minutes in new[] { 1, 3, 5, 10, 15 })
        {
            int captured = minutes;
            var b = new Button { Content = $"{minutes} 分钟", MinHeight = 40 };
            b.Click += (_, _) =>
            {
                _timer.Start(TimeSpan.FromMinutes(captured));
                _startPause.Content = "暂停";
                Render(_timer.Remaining);
            };
            presets.Children.Add(b);
        }

        _startPause.Click += (_, _) =>
        {
            if (_timer.IsRunning) { _timer.Pause(); _startPause.Content = "继续"; }
            else { _timer.Resume(); _startPause.Content = "暂停"; }
        };

        var reset = new Button { Content = "重置", MinHeight = 44, MinWidth = 96 };
        reset.Click += (_, _) =>
        {
            _timer.Stop();
            _startPause.Content = "开始";
            _display.Text = "00:00";
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        actions.Children.Add(_startPause);
        actions.Children.Add(reset);

        var panel = new StackPanel { Spacing = 18, Padding = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_display);
        panel.Children.Add(presets);
        panel.Children.Add(actions);

        Content = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            Child = panel,
        };

        WindowChrome.MakeBorderless(this, topmost: true, resizable: false);
        WindowChrome.CenterOnPrimary(this, 460, 320);
        Render(TimeSpan.Zero);
        Closed += (_, _) => _timer.Dispose();
        Activated += (_, _) => Motion.PlayEntrance(panel, 14f);
    }

    private void Render(TimeSpan remaining)
    {
        _display.Text = remaining.TotalHours >= 1
            ? remaining.ToString(@"hh\:mm\:ss")
            : remaining.ToString(@"mm\:ss");
    }

    public void Dispose() => _timer.Dispose();
}