using InkCanvas.App.Services;
using InkCanvas.Features.Tools;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using InkCanvas.Controls.Theming;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace InkCanvas.App.Views;

internal sealed class NamesInputWindow : Window
{
    private readonly TextBox _box;
    private readonly TextBlock _status;
    private readonly string _path;
    private readonly DispatcherQueueTimer _keepAbove;
    private string _origin = "";
    private bool _allowClose, _confirming, _busy, _closed;
    internal string RosterText { get => _box.Text; set => _box.Text = value; }

    public NamesInputWindow(int theme, string dataDirectory = null)
    {
        Title = "课堂名单 - Ink Canvas";
        _path = Path.Combine(dataDirectory ?? LotteryWindow.DataDir, "Names.txt");
        var root = new Grid { Background = PopupBrushes.WindowSurface(theme), Padding = new Thickness(24), RowSpacing = 16 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "每行一人，也可粘贴表格或导入 TXT、CSV、TSV。表格优先读取“姓名”列，否则读取第一列。",
            FontSize = 18, TextWrapping = TextWrapping.Wrap });
        _box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_box, ScrollBarVisibility.Auto);
        Grid.SetRow(_box, 1); root.Children.Add(_box);
        _status = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        _box.TextChanged += (_, _) => _status.Text = $"当前识别到 {ClassroomRoster.Parse(_box.Text).Count} 人，相同姓名的不同记录分别参与。";
        Grid.SetRow(_status, 2); root.Children.Add(_status);
        var actions = new Grid { ColumnSpacing = 8 };
        for (int index = 0; index < 4; index++) actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var buttons = new[]
        {
            ClassroomControls.Button("导入文件", () => _ = ImportAsync()),
            ClassroomControls.Button("粘贴名单", () => _ = PasteAsync()),
            ClassroomControls.Button("保存使用", async () => { if (await SaveAsync()) CloseWithoutSaving(); }, primary: true),
            ClassroomControls.Button("关闭", Close),
        };
        for (int index = 0; index < buttons.Length; index++)
        {
            buttons[index].Padding = new Thickness(4); Grid.SetColumn(buttons[index], index); actions.Children.Add(buttons[index]);
        }
        Grid.SetRow(actions, 3); root.Children.Add(actions);
        Content = root;
        WindowChrome.MakeStandard(this, 680, 720);
        WindowChrome.ApplyTheme(this, theme);
        root.Loaded += async (_, _) =>
        {
            try
            {
                if (File.Exists(_path))
                {
                    string text = await File.ReadAllTextAsync(_path);
                    if (!_closed) _box.Text = text;
                }
            }
            catch { if (!_closed) _status.Text = "无法读取原名单，可在此输入后重新保存。"; }
            if (!_closed) _origin = _box.Text;
        };
        AppWindow.Closing += OnClosing;
        _keepAbove = DispatcherQueue.CreateTimer();
        _keepAbove.Interval = TimeSpan.FromMilliseconds(900);
        _keepAbove.Tick += (_, _) => WindowChrome.EnsureAbove(this);
        _keepAbove.Start();
        var minimum = new MinSizeHook(this, 540, 440);
        Closed += (_, _) => { _closed = true; _keepAbove.Stop(); minimum.Dispose(); };
    }

    private async Task ImportAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            foreach (string extension in new[] { ".txt", ".csv", ".tsv" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || _closed) return;
            string text = ClassroomRoster.Decode(await File.ReadAllBytesAsync(file.Path));
            if (!_closed) _box.Text = string.Join(Environment.NewLine, ClassroomRoster.Parse(text));
        }
        catch (Exception ex) { if (!_closed) _status.Text = $"导入失败，请检查文件。{ex.Message}"; }
        finally { _busy = false; }
    }

    private async Task PasteAsync()
    {
        try
        {
            var data = Clipboard.GetContent();
            if (data.Contains(StandardDataFormats.Text))
            {
                string text = await data.GetTextAsync();
                if (!_closed) _box.Text = string.Join(Environment.NewLine, ClassroomRoster.Parse(text));
            }
        }
        catch { if (!_closed) _status.Text = "暂时无法读取剪贴板，也可直接在输入框中粘贴。"; }
    }

    internal async Task<bool> SaveAsync()
    {
        if (_busy || _closed) return false;
        _busy = true;
        string original = _box.Text;
        _box.IsReadOnly = true;
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string normalized = string.Join(Environment.NewLine, ClassroomRoster.Parse(original));
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            await File.WriteAllTextAsync(temporary, normalized);
            File.Move(temporary, _path, overwrite: true);
            _origin = original;
            return true;
        }
        catch (Exception ex) { if (!_closed) _status.Text = $"保存失败，名单仍保留在输入框中。{ex.Message}"; return false; }
        finally
        {
            _busy = false;
            if (!_closed) _box.IsReadOnly = false;
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    internal void CloseWithoutSaving() { _allowClose = true; Close(); }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        if (_busy) { args.Cancel = true; return; }
        if (_box.Text == _origin) return;
        args.Cancel = true;
        if (_confirming || Content is not FrameworkElement root || root.XamlRoot is null) return;
        _confirming = true;
        try
        {
            var dialog = new ContentDialog { Title = "保存名单更改", Content = "是否保存当前名单？",
                PrimaryButtonText = "保存", SecondaryButtonText = "放弃更改", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = root.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary || result == ContentDialogResult.Primary && await SaveAsync()) CloseWithoutSaving();
        }
        finally { _confirming = false; }
    }
}
