using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Features.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace InkCanvas.App.Views;

/// <summary>Random picker, with an optional name list and no-repeat mode.</summary>
public sealed class LotteryWindow : Window
{
    private readonly LotteryService _lottery;
    private readonly TextBlock _result = new()
    {
        FontSize = 64,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        Text = "—",
    };
    private readonly TextBlock _status = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 12,
    };

    public LotteryWindow(LotteryService lottery)
    {
        _lottery = lottery;
        Title = "抽奖";

        var pick = new Button { Content = "抽取", MinHeight = 48, MinWidth = 120 };
        pick.Click += (_, _) =>
        {
            var value = _lottery.Pick();
            _result.Text = value ?? "已抽完";
            Motion.Pulse(_result, 1.18f);
            UpdateStatus();
        };

        var reset = new Button { Content = "重置", MinHeight = 48, MinWidth = 96 };
        reset.Click += (_, _) => { _lottery.Reset(); _result.Text = "—"; UpdateStatus(); };

        var import = new Button { Content = "导入名单", MinHeight = 48, MinWidth = 120 };
        import.Click += async (_, _) => await ImportNamesAsync();

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        actions.Children.Add(pick);
        actions.Children.Add(reset);
        actions.Children.Add(import);

        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(24), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_result);
        panel.Children.Add(_status);
        panel.Children.Add(actions);

        Content = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            Child = panel,
        };

        WindowChrome.MakeBorderless(this, topmost: true);
        WindowChrome.CenterOnPrimary(this, 480, 300);
        UpdateStatus();
        Activated += (_, _) => Motion.PlayEntrance(panel, 14f);
    }

    private void UpdateStatus()
    {
        string pool = _lottery.Names.Count > 0 ? $"名单 {_lottery.Names.Count} 人" : $"人数 {_lottery.PeopleCount}";
        string repeat = _lottery.NoRepeat ? $"，已抽 {_lottery.Used.Count}" : "，可重复";
        _status.Text = pool + repeat;
    }

    private async Task ImportNamesAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".csv");
        var hwnd = WindowChrome.GetHwnd(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var lines = await Windows.Storage.FileIO.ReadLinesAsync(file);
        _lottery.LoadNames(lines.SelectMany(l => l.Split(',', '\t', ';')));
        _result.Text = "—";
        UpdateStatus();
    }
}