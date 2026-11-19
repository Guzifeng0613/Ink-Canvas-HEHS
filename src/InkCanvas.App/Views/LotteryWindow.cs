using InkCanvas.App.Services;
using InkCanvas.Controls.Controls;
using InkCanvas.Controls.Theming;
using InkCanvas.Features.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace InkCanvas.App.Views;

/// <summary>Large results on the left and classroom touch controls on the right.</summary>
public sealed class LotteryWindow : Window
{
    internal static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InkCanvas");
    private readonly LotteryService _lottery;
    private readonly bool _autoClose;
    private readonly string _dataDirectory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MinSizeHook _minSize;
    private readonly Grid _root = new();
    private Grid _workspace;
    private FrameworkElement _results;
    private Grid _controls, _quickRow;
    private StackPanel _countSection;
    private TextBlock _countTitle;
    private Grid _secondaryActions;
    private TouchIconButton _optionsButton;
    private TextBlock _out1, _out2, _out3, _countLabel, _summary, _history, _notice;
    private TouchIconButton _minusButton, _plusButton, _rollButton, _importButton, _resetRound, _copy;
    private CheckBox _noRepeat;
    private NumberBox _poolInput;
    private readonly List<TouchIconButton> _quickCounts = new();
    private int _totalCount = 1;
    private bool _rolling, _closed, _syncing;
    private NamesInputWindow _namesInput;
    private IReadOnlyList<string> _lastResult = Array.Empty<string>();
    internal IReadOnlyList<string> LastResult => _lastResult;
    internal event Action OptionsChanged;

    public LotteryWindow(LotteryService lottery, bool autoClose = false, int theme = 0, string dataDirectory = null)
    {
        _lottery = lottery;
        _autoClose = autoClose;
        _dataDirectory = dataDirectory ?? DataDir;
        Title = "抽奖 / 课堂点名 - Ink Canvas";
        BuildUi();
        Content = _root;
        WindowChrome.MakeStandard(this, 1100, 700);
        WindowChrome.ApplyTheme(this, theme);
        _minSize = new MinSizeHook(this, 600, 540);
        _root.ActualThemeChanged += (_, _) => ApplyPalette();
        _root.Loaded += async (_, _) =>
        {
            ApplyPalette(); ReloadNames();
            if (_autoClose)
            {
                try { await Task.Delay(100, _lifetime.Token); await RollAsync(); }
                catch (OperationCanceledException) { }
            }
        };
        Activated += (_, _) => { if (!_closed) RefreshSummary(); };
        Closed += (_, _) =>
        {
            _closed = true; _lifetime.Cancel(); _lifetime.Dispose();
            _minSize.Dispose(); _namesInput?.CloseWithoutSaving();
        };
        ApplyPalette(); RefreshSummary();
    }

    private void BuildUi()
    {
        var page = new Grid { Margin = new Thickness(24, 16, 24, 20), RowSpacing = 16 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _summary = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _resetRound = ClassroomControls.Button("重置本轮", () =>
        {
            if (_rolling) return;
            _lottery.Reset(); _notice.Text = "已开始新一轮，全部人员重新参与抽选。"; RefreshSummary();
        });
        header.Children.Add(_summary); Grid.SetColumn(_resetRound, 1); header.Children.Add(_resetRound);
        page.Children.Add(header);

        _out1 = Output(); _out2 = Output(); _out3 = Output();
        _out1.Text = "准备抽选";
        _out2.Visibility = _out3.Visibility = Visibility.Collapsed;
        var outputs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40,
            Children = { _out1, _out2, _out3 } };
        _results = new Viewbox { Child = outputs, Margin = new Thickness(20), Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly };

        var controls = new Grid { RowSpacing = 16, ColumnSpacing = 16, VerticalAlignment = VerticalAlignment.Center };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        for (int row = 0; row < 4; row++) controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _countSection = new StackPanel { Spacing = 8 };
        _countTitle = new TextBlock { Text = "本次抽选人数", FontSize = 18 };
        _countSection.Children.Add(_countTitle);
        var count = new Grid { ColumnSpacing = 12 };
        count.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        count.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        count.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        _minusButton = ClassroomControls.Button("−", () => ChangeCount(_totalCount - 1));
        _plusButton = ClassroomControls.Button("＋", () => ChangeCount(_totalCount + 1));
        AutomationProperties.SetName(_minusButton, "减少人数"); AutomationProperties.SetName(_plusButton, "增加人数");
        _minusButton.Height = _plusButton.Height = 70; _minusButton.FontSize = _plusButton.FontSize = 32;
        _countLabel = new TextBlock { Text = "1", FontSize = 42, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        count.Children.Add(_minusButton); Grid.SetColumn(_countLabel, 1); count.Children.Add(_countLabel);
        Grid.SetColumn(_plusButton, 2); count.Children.Add(_plusButton); _countSection.Children.Add(count); controls.Children.Add(_countSection);
        var quick = new Grid { ColumnSpacing = 6 };
        foreach (int value in new[] { 1, 2, 3, 5, 10 })
        {
            int column = quick.ColumnDefinitions.Count;
            quick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = ClassroomControls.Button($"{value} 人", () => ChangeCount(value));
            button.FontSize = 16; button.Padding = new Thickness(2);
            Grid.SetColumn(button, column); quick.Children.Add(button); _quickCounts.Add(button);
        }
        _quickRow = quick; Grid.SetRow(quick, 1); controls.Children.Add(quick);
        _rollButton = ClassroomControls.Button("开始抽选", () => _ = RollAsync(), primary: true);
        _rollButton.Height = 76; _rollButton.FontSize = 28;
        AutomationProperties.SetName(_rollButton, "抽奖"); Grid.SetRow(_rollButton, 2); controls.Children.Add(_rollButton);
        _noRepeat = new CheckBox { Content = "本轮不重复", IsChecked = _lottery.NoRepeat, MinHeight = 48, FontSize = 18 };
        _noRepeat.Click += (_, _) =>
        {
            _lottery.NoRepeat = _noRepeat.IsChecked == true; OptionsChanged?.Invoke(); RefreshSummary();
        };
        _poolInput = new NumberBox { Header = "数字点名总人数", Value = _lottery.PeopleCount, Minimum = 1, Maximum = 9999,
            SmallChange = 1, LargeChange = 10, MinHeight = 56, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        _poolInput.ValueChanged += (_, args) =>
        {
            if (_syncing || _rolling || !double.IsFinite(args.NewValue)) return;
            _lottery.PeopleCount = Math.Clamp((int)Math.Round(args.NewValue), 1, 9999);
            OptionsChanged?.Invoke(); RefreshSummary();
        };
        _optionsButton = ClassroomControls.Button("抽选选项");
        _optionsButton.Flyout = new Flyout { Content = new StackPanel { Spacing = 16, Width = 280,
            Children = { _noRepeat, _poolInput } } };
        _importButton = ClassroomControls.Button("名单管理", OpenNamesInput);
        AutomationProperties.SetName(_importButton, "导入名单");
        _secondaryActions = new Grid { ColumnSpacing = 8 };
        _secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _secondaryActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _secondaryActions.Children.Add(_importButton); Grid.SetColumn(_optionsButton, 1); _secondaryActions.Children.Add(_optionsButton);
        Grid.SetRow(_secondaryActions, 3); controls.Children.Add(_secondaryActions);
        _controls = controls;
        _workspace = new Grid { ColumnSpacing = 24, RowSpacing = 16 };
        _workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        _workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _workspace.Children.Add(_results); _workspace.Children.Add(_controls);
        _workspace.SizeChanged += (_, _) => UpdateLayout();
        Grid.SetRow(_workspace, 1); page.Children.Add(_workspace);

        var footer = new Grid { ColumnSpacing = 16 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var information = new StackPanel { Spacing = 6 };
        _notice = new TextBlock { Text = "支持一次抽选多人，名单模式按名单中的每一行参与。", FontSize = 14, TextWrapping = TextWrapping.Wrap };
        _history = new TextBlock { FontSize = 14, MaxLines = 3, TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis };
        information.Children.Add(_notice); information.Children.Add(_history); footer.Children.Add(information);
        _copy = ClassroomControls.Button("复制结果", CopyResult);
        Grid.SetColumn(_copy, 1); footer.Children.Add(_copy);
        Grid.SetRow(footer, 2); page.Children.Add(footer);
        _root.Children.Add(page);
    }

    private static TextBlock Output() => new()
    {
        FontSize = 96, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private void UpdateLayout()
    {
        bool stacked = _workspace.ActualWidth < 780;
        _workspace.ColumnDefinitions[1].Width = new GridLength(stacked ? 0 : 320);
        Grid.SetRow(_controls, stacked ? 1 : 0); Grid.SetColumn(_controls, stacked ? 0 : 1);
        _controls.MaxWidth = stacked ? double.PositiveInfinity : 320;
        _controls.HorizontalAlignment = HorizontalAlignment.Stretch;
        _controls.ColumnDefinitions[1].Width = new GridLength(stacked ? 1 : 0, GridUnitType.Star);
        _countTitle.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        _quickRow.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(_rollButton, stacked ? 0 : 2); Grid.SetColumn(_rollButton, stacked ? 1 : 0);
        Grid.SetRow(_secondaryActions, stacked ? 1 : 3); Grid.SetColumnSpan(_secondaryActions, stacked ? 2 : 1);
    }

    private void ApplyPalette()
    {
        int theme = _root.ActualTheme == ElementTheme.Dark ? 2 : 1;
        _root.Background = PopupBrushes.WindowSurface(theme);
        foreach (var text in new[] { _out1, _out2, _out3, _countLabel, _summary, _history, _notice })
            text.Foreground = PopupBrushes.Foreground(theme);
    }

    internal void ChangeCount(int value)
    {
        if (_rolling) return;
        _totalCount = Math.Clamp(value, 1, _lottery.PoolSize);
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        _syncing = true;
        try
        {
            int remaining = _lottery.RemainingCount;
            _totalCount = Math.Clamp(_totalCount, 1, _lottery.PoolSize);
            _summary.Text = _lottery.NoRepeat ? $"共 {_lottery.PoolSize} 人 · 剩余 {remaining} 人" : $"共 {_lottery.PoolSize} 人参与抽选";
            _countLabel.Text = _totalCount.ToString();
            _poolInput.Value = _lottery.PeopleCount;
            _poolInput.IsEnabled = !_rolling && _lottery.Names.Count == 0;
            _noRepeat.IsChecked = _lottery.NoRepeat;
            _rollButton.IsEnabled = !_rolling && remaining > 0;
            _rollButton.Content = remaining == 0 ? "本轮已抽完" : "开始抽选";
            _minusButton.IsEnabled = !_rolling && _totalCount > 1;
            _plusButton.IsEnabled = !_rolling && _totalCount < _lottery.PoolSize;
            _importButton.IsEnabled = _resetRound.IsEnabled = _noRepeat.IsEnabled = _optionsButton.IsEnabled = !_rolling;
            foreach (var button in _quickCounts) button.IsEnabled = !_rolling;
            _copy.IsEnabled = !_rolling && _lastResult.Count > 0;
            _history.Text = string.Join("\n", _lottery.History.TakeLast(3).Reverse().Select(draw => $"{draw.Time:HH:mm}　{string.Join("、", draw.Names)}"));
        }
        finally { _syncing = false; }
    }

    private void ReloadNames()
    {
        try
        {
            string path = Path.Combine(_dataDirectory, "Names.txt");
            if (File.Exists(path))
            {
                var rules = File.Exists(Path.Combine(_dataDirectory, "Replace.txt"))
                    ? File.ReadAllLines(Path.Combine(_dataDirectory, "Replace.txt")) : Array.Empty<string>();
                var names = File.ReadAllLines(path).Select(raw =>
                {
                    string name = raw.Trim();
                    foreach (string rule in rules)
                    {
                        int split = rule.IndexOf("-->", StringComparison.Ordinal);
                        if (split >= 0 && name == rule[..split]) name = rule[(split + 3)..];
                    }
                    return name;
                }).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
                bool changed = !_lottery.Names.SequenceEqual(names);
                _lottery.LoadNames(names);
                if (changed)
                {
                    _lastResult = Array.Empty<string>();
                    _out1.Text = "准备抽选";
                    _out2.Visibility = _out3.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch { _notice.Text = "名单读取失败，保留当前名单。请在名单编辑中检查文件。"; }
        RefreshSummary();
    }

    private void OpenNamesInput()
    {
        if (_rolling || _closed) return;
        if (_namesInput is not null) { _namesInput.Activate(); return; }
        _root.IsHitTestVisible = false;
        var dialog = new NamesInputWindow(_root.ActualTheme == ElementTheme.Dark ? 2 : 1, _dataDirectory);
        _namesInput = dialog;
        dialog.Closed += (_, _) =>
        {
            _namesInput = null;
            if (_closed) return;
            _root.IsHitTestVisible = true; ReloadNames();
        };
        dialog.Activate(); WindowChrome.EnsureAbove(dialog);
    }

    internal async Task RollAsync()
    {
        if (_rolling || _closed || _lottery.RemainingCount == 0) return;
        _rolling = true; RefreshSummary();
        _out2.Visibility = _out3.Visibility = Visibility.Collapsed;
        var token = _lifetime.Token;
        try
        {
            for (int iteration = 0; iteration < 5; iteration++)
            {
                int index = Random.Shared.Next(_lottery.PoolSize);
                _out1.Text = _lottery.Names.Count > 0 ? _lottery.Names[index] : (index + 1).ToString();
                await Task.Delay(120, token);
            }
            _lastResult = _lottery.Draw(_totalCount);
            int columns = _lastResult.Count <= 5 ? 1 : _lastResult.Count <= 10 ? 2 : 3;
            int perColumn = (int)Math.Ceiling(_lastResult.Count / (double)columns);
            var labels = new[] { _out1, _out2, _out3 };
            for (int column = 0; column < labels.Length; column++)
            {
                labels[column].Text = string.Join("\n", _lastResult.Skip(column * perColumn).Take(perColumn));
                labels[column].Visibility = column < columns ? Visibility.Visible : Visibility.Collapsed;
            }
            _notice.Text = _lottery.RemainingCount == 0 ? "本轮已全部抽完，点击“重置本轮”可重新开始。"
                : $"本次抽选 {_lastResult.Count} 人。";
            if (_autoClose) { await Task.Delay(1500, token); if (!_closed) Close(); }
        }
        catch (OperationCanceledException) { }
        finally { _rolling = false; if (!_closed) RefreshSummary(); }
    }

    private void CopyResult()
    {
        if (_lastResult.Count == 0) return;
        try
        {
            var package = new DataPackage(); package.SetText(string.Join(Environment.NewLine, _lastResult));
            Clipboard.SetContent(package); _notice.Text = "已复制本次抽选结果。";
        }
        catch { _notice.Text = "暂时无法访问剪贴板，请重试。"; }
    }
}
