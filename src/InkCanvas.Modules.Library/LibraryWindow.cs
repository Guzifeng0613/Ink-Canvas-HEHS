using InkCanvas.Controls.Controls;
using InkCanvas.Host.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace InkCanvas.Modules.Library;

internal enum LibraryMode { Browse, Open, Save }

internal sealed class LibraryWindow : Window
{
    private readonly IBoardLibraryHost _host;
    private readonly LibraryStore _store;
    private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Grid _root = new();
    private readonly ListView _locations = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly GridView _files = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    private readonly AutoSuggestBox _search = new() { PlaceholderText = "搜索文稿和文件夹", MinWidth = 190 };
    private readonly ToggleSwitch _recursive = new() { Header = "搜索子文件夹", IsOn = true };
    private readonly ComboBox _sort = new() { ItemsSource = new[] { "最近修改", "名称", "文件大小" }, SelectedIndex = 0, MinWidth = 120 };
    private readonly TextBlock _path = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _previewTitle = new() { FontSize = 20, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _previewDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Height = 220 };
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center, Text = "页面预览" };
    private readonly TextBox _name = new() { Header = "文稿名称", MinWidth = 240 };
    private readonly Button _action = new() { MinWidth = 96, MinHeight = 44 };
    private readonly ProgressRing _busy = new() { Width = 20, Height = 20 };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _listing, _pageLoad;
    private readonly Dictionary<SelectorItem, LibraryEntry> _containers = new();
    private readonly Dictionary<SelectorItem, CancellationTokenSource> _thumbnailLoads = new();
    private readonly IReadOnlyList<string> _extensions;
    private string _folder, _category = "文件夹";
    private int _page, _pageCount;
    private bool _closed, _loading, _committing, _ready;
    public LibraryMode Mode { get; }
    public Task<string> Result => _result.Task;

    public LibraryWindow(IBoardLibraryHost host, LibraryMode mode, string suggestedName, IReadOnlyList<string> extensions, LibraryStore store)
    {
        _host = host; Mode = mode; _extensions = extensions;
        _store = store;
        Title = mode == LibraryMode.Save ? "保存到文稿库" : "文稿库";
        _name.Text = System.IO.Path.GetFileNameWithoutExtension(suggestedName ?? "白板");
        _action.Content = mode == LibraryMode.Save ? "保存" : "打开";
        _action.IsEnabled = false;
        Build();
        Content = _root;
        host.ConfigureWindow(this);
        WindowFrame.Attach(this, _root).UseMica();
        _root.Loaded += async (_, _) => await RunAsync(InitializeAsync);
        Closed += (_, _) =>
        {
            _closed = true; _debounce.Stop(); _lifetime.Cancel(); _listing?.Cancel(); _pageLoad?.Cancel();
            foreach (var load in _thumbnailLoads.Values) load.Cancel();
            _thumbnailLoads.Clear(); _files.ItemsSource = null; _containers.Clear(); _preview.Source = null;
            _result.TrySetResult(null);
        };
    }

    private static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label, MinHeight = 44, Padding = new Thickness(12, 6, 12, 6) };
        button.Click += async (_, _) => await action();
        return button;
    }

    private void Build()
    {
        _root.ColumnDefinitions.Add(new() { Width = new GridLength(208) });
        _root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var sidebar = new Grid { Padding = new Thickness(12), RowSpacing = 12 };
        sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        sidebar.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        sidebar.Children.Add(new TextBlock { Text = "文稿", FontSize = 24, Margin = new Thickness(8) });
        sidebar.Children.Add(_locations); Grid.SetRow(_locations, 1);
        var add = Button("添加文件夹…", () => RunAsync(AddLocationAsync)); sidebar.Children.Add(add); Grid.SetRow(add, 2);
        _locations.SelectionChanged += (_, _) =>
        {
            if (_loading || _locations.SelectedItem is not LibraryLocation location) return;
            _category = location.Path is null ? location.Title : "文件夹";
            if (_category == "文件夹") _folder = location.Path;
            _ = RunAsync(RefreshAsync);
        };
        _locations.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid ColumnSpacing="10" ToolTipService.ToolTip="{Binding Path}">
                <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <FontIcon Glyph="{Binding Icon}" FontSize="16"/>
                <TextBlock Grid.Column="1" Text="{Binding Title}" TextTrimming="CharacterEllipsis"/>
              </Grid>
            </DataTemplate>
            """);
        _root.Children.Add(sidebar);
        var center = new Grid { Padding = new Thickness(12), RowSpacing = 12 };
        Grid.SetColumn(center, 1); _root.Children.Add(center);
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var navigation = new Grid { ColumnSpacing = 8 };
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); navigation.ColumnDefinitions.Add(new()); navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        navigation.Children.Add(Button("上一级", () => RunAsync(async () =>
        {
            var parent = Directory.GetParent(_folder); if (parent is null) return;
            _folder = parent.FullName; _category = "文件夹"; await RefreshAsync();
        })));
        navigation.Children.Add(_path); Grid.SetColumn(_path, 1);
        var refresh = Button("刷新", () => RunAsync(RefreshAsync)); navigation.Children.Add(refresh); Grid.SetColumn(refresh, 2);
        center.Children.Add(navigation);
        var filters = new Grid { ColumnSpacing = 8 };
        filters.ColumnDefinitions.Add(new()); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.Children.Add(_search); filters.Children.Add(_sort); Grid.SetColumn(_sort, 1); Grid.SetRow(filters, 1); center.Children.Add(filters);
        _search.TextChanged += (_, args) => { if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) { _debounce.Stop(); _debounce.Start(); } };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _ = RunAsync(RefreshAsync); };
        _sort.SelectionChanged += (_, _) => { if (!_loading && _folder is not null) _ = RunAsync(RefreshAsync); };
        _files.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid Width="204" Margin="4,4,4,12" RowSpacing="7">
                <Grid.RowDefinitions><RowDefinition Height="124"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                <Border CornerRadius="8" Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}" BorderThickness="1">
                  <Grid><FontIcon Glyph="{Binding Icon}" FontSize="32" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/><Image Source="{Binding Preview}" Stretch="Uniform"/></Grid>
                </Border>
                <TextBlock Grid.Row="1" Text="{Binding Name}" TextTrimming="CharacterEllipsis" FontWeight="SemiBold"/>
                <TextBlock Grid.Row="2" Text="{Binding Details}" FontSize="12" TextTrimming="CharacterEllipsis" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
              </Grid>
            </DataTemplate>
            """);
        _files.ContainerContentChanging += OnContainerChanged;
        _files.SelectionChanged += async (_, _) => await RunAsync(async () =>
        {
            var item = _files.SelectedItem as LibraryEntry;
            _action.IsEnabled = _ready && (Mode == LibraryMode.Save || item is { IsFolder: false });
            _previewTitle.Text = item?.Name ?? "选择文稿以预览"; _previewDetails.Text = item?.Details ?? "";
            _page = 0; _pageCount = 0; _preview.Source = null;
            await LoadPageAsync();
        });
        _files.ItemClick += async (_, args) =>
        {
            if (args.ClickedItem is LibraryEntry { IsFolder: true } folder) await RunAsync(async () =>
            { _folder = folder.Path; _category = "文件夹"; await RefreshAsync(); });
        };
        _files.DoubleTapped += async (_, _) => { if (Mode != LibraryMode.Save) await RunAsync(CommitAsync); };
        Grid.SetRow(_files, 2); center.Children.Add(_files);
        var details = new StackPanel { Padding = new Thickness(16), Spacing = 16 };
        details.Children.Add(_previewTitle); details.Children.Add(_preview); details.Children.Add(_previewDetails);
        var pages = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pages.Children.Add(Button("‹", () => RunAsync(async () => { if (_page > 0) { _page--; await LoadPageAsync(); } })));
        pages.Children.Add(_pageText);
        pages.Children.Add(Button("›", () => RunAsync(async () => { if (_page + 1 < _pageCount) { _page++; await LoadPageAsync(); } })));
        details.Children.Add(pages);
        details.Children.Add(Button("收藏 / 取消收藏", () => RunAsync(async () =>
        {
            if (_files.SelectedItem is not LibraryEntry { IsFolder: false } entry) return;
            if (!_store.State.Favorites.Remove(entry.Path)) _store.State.Favorites.Add(entry.Path);
            await _store.SaveAsync(); _status.Text = "已更新收藏";
            if (_category == "收藏") await RefreshAsync();
        })));
        details.Children.Add(Button("重命名…", () => RunAsync(RenameAsync)));
        if (_host.DocumentAi?.Available == true) details.Children.Add(Button("AI 命名与描述", () => RunAsync(async () =>
        {
            if (_files.SelectedItem is not LibraryEntry { IsFolder: false } entry) return;
            _status.Text = "正在生成图片并分析…";
            var result = await _host.DocumentAi.DescribeAsync(new(entry.Path), _lifetime.Token);
            _status.Text = "已命名为 " + result.Title;
            _previewDetails.Text = result.Description;
            RelocatePaths(result.OriginalPath, result.Path); await _store.SaveAsync(); await RefreshAsync();
        })));
        details.Children.Add(Button("移动到文件夹…", () => RunAsync(() => TransferAsync(false))));
        details.Children.Add(Button("制作副本…", () => RunAsync(() => TransferAsync(true))));
        details.Children.Add(Button("新建文件夹…", () => RunAsync(NewFolderAsync)));
        details.Children.Add(_recursive);
        _recursive.Toggled += (_, _) => { if (_folder is not null) _ = RunAsync(RefreshAsync); };
        var detailScroll = new ScrollViewer { Content = details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        bool detailsInDialog = false;
        Grid.SetColumn(detailScroll, 2); _root.Children.Add(detailScroll);
        navigation.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var previewButton = Button("详情", () => RunAsync(async () =>
        {
            if (detailsInDialog) return;
            detailsInDialog = true; _root.Children.Remove(detailScroll); detailScroll.Visibility = Visibility.Visible;
            var flyout = new Flyout { Content = detailScroll, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
            flyout.Closed += (_, _) =>
            {
                flyout.Content = null; detailsInDialog = false;
                detailScroll.ClearValue(FrameworkElement.WidthProperty); detailScroll.MaxHeight = double.PositiveInfinity;
                detailScroll.Visibility = _root.ActualWidth < 760 ? Visibility.Collapsed : Visibility.Visible;
                if (!_closed) _root.Children.Add(detailScroll);
            };
            detailScroll.Width = 280; detailScroll.MaxHeight = Math.Max(240, _root.ActualHeight - 100);
            flyout.ShowAt(navigation);
            await Task.CompletedTask;
        }));
        navigation.Children.Add(previewButton); Grid.SetColumn(previewButton, 3);
        var bottom = new Grid { Padding = new Thickness(16), ColumnSpacing = 12 };
        bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bottom.ColumnDefinitions.Add(new()); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bottom.Children.Add(_busy);
        var status = new StackPanel { Spacing = 6 }; status.Children.Add(_status);
        if (Mode == LibraryMode.Save) status.Children.Add(_name);
        bottom.Children.Add(status); Grid.SetColumn(status, 1);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(Button(Mode == LibraryMode.Save ? "其他位置…" : "浏览文件…", () => RunAsync(SystemPickerAsync)));
        if (Mode != LibraryMode.Browse) actions.Children.Add(Button("取消", () => { Close(); return Task.CompletedTask; }));
        _action.Click += async (_, _) => await RunAsync(CommitAsync); actions.Children.Add(_action);
        bottom.Children.Add(actions); Grid.SetColumn(actions, 2);
        Grid.SetRow(bottom, 1); Grid.SetColumnSpan(bottom, 3); _root.Children.Add(bottom);
        AutomationProperties.SetName(_files, "文稿与文件夹"); AutomationProperties.SetName(_locations, "文稿位置");
        AutomationProperties.SetName(_preview, "所选白板的页面预览");
        _root.SizeChanged += (_, _) =>
        {
            bool compact = _root.ActualWidth < 760;
            _root.ColumnDefinitions[0].Width = new GridLength(_root.ActualWidth < 1100 ? 160 : 208);
            _root.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(_root.ActualWidth < 1100 ? 240 : 280);
            if (!detailsInDialog) detailScroll.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            previewButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private async Task InitializeAsync()
    {
        _loading = true;
        await _store.LoadAsync(); if (_closed) return;
        string defaultFolder = _host.DefaultDirectory;
        Directory.CreateDirectory(defaultFolder);
        if (!_store.State.Locations.Contains(defaultFolder, StringComparer.OrdinalIgnoreCase)) _store.State.Locations.Insert(0, defaultFolder);
        _folder = Directory.Exists(_store.State.LastFolder) ? _store.State.LastFolder : defaultFolder;
        UpdateLocations(); _loading = false; await RefreshAsync();
        _ready = true;
        if (!_closed) _action.IsEnabled = Mode == LibraryMode.Save || _files.SelectedItem is LibraryEntry { IsFolder: false };
    }

    private void UpdateLocations() => _locations.ItemsSource = new[]
    {
        new LibraryLocation("最近使用", null, "\uE81C"), new LibraryLocation("收藏", null, "\uE734"),
    }.Concat(_store.State.Locations.Select(path => new LibraryLocation(
        System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path, path, "\uE8B7"))).ToArray();

    private async Task RefreshAsync()
    {
        _listing?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _listing = request;
        _busy.IsActive = true; _path.Text = _category == "文件夹" ? _folder : _category;
        try
        {
            var selected = _category == "收藏" ? _store.State.Favorites : _category == "最近使用" ? _store.State.Recent : null;
            var entries = await LibraryStore.ListAsync(_folder, selected?.ToArray(), _search.Text, _recursive.IsOn, request.Token);
            if (request.IsCancellationRequested || _closed) return;
            IEnumerable<LibraryEntry> ordered = _sort.SelectedIndex switch
            {
                1 => entries.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
                2 => entries.OrderByDescending(item => item.Length),
                _ => _category == "最近使用" ? entries.OrderBy(item => _store.State.Recent.FindIndex(path => path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
                    : entries.OrderByDescending(item => item.Modified),
            };
            _files.ItemsSource = ordered.OrderByDescending(item => item.IsFolder).ToArray();
            _status.Text = entries.Count == 0 ? "此处没有匹配的文稿，可更换文件夹或搜索条件。" : $"{entries.Count(item => !item.IsFolder)} 份文稿 · {entries.Count(item => item.IsFolder)} 个文件夹";
            _store.State.LastFolder = _folder; await _store.SaveAsync();
        }
        finally { if (ReferenceEquals(_listing, request)) { _listing = null; _busy.IsActive = false; } }
    }

    private void OnContainerChanged(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var container = args.ItemContainer;
        if (_thumbnailLoads.Remove(container, out var oldLoad)) oldLoad.Cancel();
        if (_containers.Remove(args.ItemContainer, out var previous)) previous.Preview = null;
        if (args.InRecycleQueue || args.Item is not LibraryEntry entry || entry.IsFolder) return;
        _containers[container] = entry;
        var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _thumbnailLoads[container] = load;
        _ = RunAsync(async () =>
        {
            try
            {
                var result = await _store.PreviewAsync(entry.Path, 0, load.Token);
                if (_closed || load.IsCancellationRequested || !_containers.TryGetValue(container, out var current) || !ReferenceEquals(current, entry)) return;
                var bitmap = await BitmapAsync(result.Png);
                if (!load.IsCancellationRequested) entry.Preview = bitmap;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { /* A bad file must not interrupt scrolling. Selected preview reports the error. */ }
            finally { if (_thumbnailLoads.TryGetValue(container, out var current) && ReferenceEquals(current, load)) _thumbnailLoads.Remove(container); load.Dispose(); }
        });
    }

    private async Task LoadPageAsync()
    {
        _pageLoad?.Cancel();
        if (_files.SelectedItem is not LibraryEntry { IsFolder: false } entry) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pageLoad = request;
        try
        {
            var result = await _store.PreviewAsync(entry.Path, _page, request.Token);
            var bitmap = await BitmapAsync(result.Png);
            if (request.IsCancellationRequested || _closed) return;
            _preview.Source = bitmap;
            var metadata = await InkCanvas.Features.Board.BoardService.ReadArchiveMetadataAsync(entry.Path, request.Token);
            if (!request.IsCancellationRequested && !_closed && metadata?.Description is { Length: > 0 } description) _previewDetails.Text = entry.Details + "\n\n" + description;
            if (result.Pages > 0) _pageCount = result.Pages;
            _pageText.Text = _pageCount > 0 ? $"{_page + 1} / {_pageCount}" : $"第 {_page + 1} 页";
        }
        finally { if (ReferenceEquals(_pageLoad, request)) _pageLoad = null; }
    }

    private static async Task<BitmapImage> BitmapAsync(byte[] png)
    {
        if (png is null) return null;
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(png); await writer.StoreAsync(); }
        stream.Seek(0); var bitmap = new BitmapImage { DecodePixelWidth = 768 }; await bitmap.SetSourceAsync(stream); return bitmap;
    }

    private async Task CommitAsync()
    {
        if (_committing || !_ready) return;
        string path;
        if (Mode == LibraryMode.Save)
        {
            path = LibraryStore.NamedPath(_folder, _name.Text, true);
            if (File.Exists(path) && !await ConfirmAsync("替换现有文稿？", System.IO.Path.GetFileName(path))) return;
        }
        else
        {
            if (_files.SelectedItem is not LibraryEntry { IsFolder: false } entry) return;
            path = entry.Path;
        }
        _committing = true; _action.IsEnabled = false;
        try { await CompleteAsync(path); }
        finally { _committing = false; if (!_closed) _action.IsEnabled = true; }
    }

    private async Task CompleteAsync(string path)
    {
        if (Mode == LibraryMode.Browse && !await _host.OpenBoardAsync(path)) return;
        _store.Remember(path); await _store.SaveAsync();
        if (_closed) return;
        _result.TrySetResult(path); Close();
    }

    private async Task SystemPickerAsync()
    {
        string path;
        if (Mode == LibraryMode.Save)
        {
            var picker = new FileSavePicker { SuggestedFileName = _name.Text };
            picker.FileTypeChoices.Add("Ink Canvas 白板", new[] { ".icink" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            path = (await picker.PickSaveFileAsync())?.Path;
        }
        else
        {
            var picker = new FileOpenPicker();
            foreach (string extension in _extensions ?? new[] { ".icink" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            path = (await picker.PickSingleFileAsync())?.Path;
        }
        if (path is not null && !_closed) await CompleteAsync(path);
    }

    private async Task AddLocationAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync(); if (folder is null || _closed) return;
        if (!_store.State.Locations.Contains(folder.Path, StringComparer.OrdinalIgnoreCase)) _store.State.Locations.Add(folder.Path);
        _folder = folder.Path; _category = "文件夹"; UpdateLocations(); await RefreshAsync();
    }

    private async Task<string> AskNameAsync(string title, string initial)
    {
        var input = new TextBox { Text = initial, MinWidth = 260 };
        var dialog = new ContentDialog { Title = title, Content = input, PrimaryButtonText = "确定", CloseButtonText = "取消", XamlRoot = _root.XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task NewFolderAsync()
    {
        string name = await AskNameAsync("新建文件夹", "新文件夹"); if (name is null) return;
        string path = LibraryStore.NamedPath(_folder, name, false);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("此名称已存在。");
        Directory.CreateDirectory(path); await RefreshAsync();
    }
    private async Task RenameAsync()
    {
        if (_files.SelectedItem is not LibraryEntry entry) return;
        string name = await AskNameAsync("重命名", entry.Name); if (name is null) return;
        string path = LibraryStore.NamedPath(System.IO.Path.GetDirectoryName(entry.Path), name, !entry.IsFolder);
        if (path == entry.Path) return;
        await Task.Run(() => { if (entry.IsFolder) Directory.Move(entry.Path, path); else File.Move(entry.Path, path); });
        RelocatePaths(entry.Path, path);
        UpdateLocations();
        await RefreshAsync();
    }
    private async Task TransferAsync(bool copy)
    {
        if (_files.SelectedItem is not LibraryEntry { IsFolder: false } entry) return;
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync(); if (folder is null || _closed) return;
        string destination = LibraryStore.NamedPath(folder.Path, entry.Name + (copy ? " 副本" : ""), true);
        if (destination.Equals(entry.Path, StringComparison.OrdinalIgnoreCase)) return;
        _busy.IsActive = true;
        try
        {
            if (copy)
            {
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var input = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                    { await input.CopyToAsync(output, _lifetime.Token); await output.FlushAsync(_lifetime.Token); }
                    _lifetime.Token.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, false);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else
            {
                await Task.Run(() => File.Move(entry.Path, destination, false), _lifetime.Token);
                RelocatePaths(entry.Path, destination);
            }
            _store.Remember(destination); await _store.SaveAsync();
            await RefreshAsync(); _status.Text = copy ? "已制作副本" : "已移动文稿";
        }
        finally { _busy.IsActive = false; }
    }
    private void RelocatePaths(string source, string destination)
    {
        _store.RelocatePaths(source, destination);
        _folder = _store.State.LastFolder ?? _folder;
    }
    internal void Relocate(string previous, string path) => _ = RunAsync(async () =>
    {
        RelocatePaths(previous, path); await _store.SaveAsync(); await RefreshAsync();
    });
    private async Task<bool> ConfirmAsync(string title, string message) => await new ContentDialog
    { Title = title, Content = message, PrimaryButtonText = "替换", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close, XamlRoot = _root.XamlRoot }.ShowAsync() == ContentDialogResult.Primary;

    private async Task RunAsync(Func<Task> action)
    {
        try { if (!_closed) await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) _status.Text = ex.GetBaseException().Message; }
    }
}
