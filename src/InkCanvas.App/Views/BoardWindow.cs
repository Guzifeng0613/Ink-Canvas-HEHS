using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Shapes;
using InkCanvas.Features.Board;
using InkCanvas.Ink;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using WinRT.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace InkCanvas.App.Views;

/// <summary>
/// WinUI board port: page navigation and tools dock to the lower edge, matching the upstream
/// WPF board's left/page, center/tools and right/actions layout.
/// </summary>
public sealed class BoardWindow : Window
{
    private readonly AppServices _services;
    private readonly BoardService _board;
    private readonly IShellActions _shell;
    private readonly InkSurface _surface = new();
    private readonly BoardImageLayer _images = new();
    private readonly CanvasControl _paper;
    private readonly Border _background;
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 32, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _pageCountLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 58, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _selectionLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private Button _previousPageButton;
    private Button _nextPageButton;
    private Button _addPageButton;
    private Button _deletePageButton;
    private readonly Border _selectionActions;
    private readonly Border _bottomBar;
    private readonly Grid _contentRoot;
    private readonly Grid _windowedContent;
    private readonly StackPanel _toolButtons;
    private InkController _boundController;
    private bool _isFullscreen;
    private float _viewScale = 1f;

    /// <summary>The controller currently bound to the board surface and its floating tools.</summary>
    public InkController CurrentController => _board.Current;

    /// <summary>Raised after navigation or loading has rebound this window to its current page.</summary>
    public event Action CurrentPageChanged;

    public BoardWindow(AppServices services, IShellActions shell = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _board = services.Board;
        _shell = shell;
        Title = "Ink Canvas 画板";

        _background = new Border();
        _paper = new CanvasControl { ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0) };
        _paper.Draw += OnDrawPaper;
        _images.SelectionChanged += OnImageSelectionChanged;

        var surfaceHost = new Grid();
        surfaceHost.Children.Add(_background);
        surfaceHost.Children.Add(_paper);
        surfaceHost.Children.Add(_surface);
        surfaceHost.Children.Add(_images);

        _toolButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var toolStrip = BuildToolStrip();
        var pageStrip = BuildPageStrip();
        var actionStrip = BuildActionStrip();
        var dock = new Grid { ColumnSpacing = 8 };
        dock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pageStrip, 0);
        Grid.SetColumn(toolStrip, 1);
        Grid.SetColumn(actionStrip, 2);
        dock.Children.Add(pageStrip);
        dock.Children.Add(toolStrip);
        dock.Children.Add(actionStrip);

        _selectionActions = BuildSelectionActions();
        _bottomBar = new Border
        {
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1, 1, 1, 0),
            Padding = new Thickness(8, 6, 8, 8),
            Child = dock,
        };

        _contentRoot = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        _contentRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _contentRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(surfaceHost, 0);
        Grid.SetRow(_bottomBar, 1);
        _contentRoot.Children.Add(surfaceHost);
        Grid.SetRow(_selectionActions, 0);
        _contentRoot.Children.Add(_selectionActions);
        _contentRoot.Children.Add(_bottomBar);

        WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
        _windowedContent = WindowChrome.AddTitleBar(this, Title, _contentRoot, closeButton: true);
        Content = _windowedContent;

        var work = Platform.System.ScreenService.WorkArea;
        WindowChrome.Place(this, work.X, work.Y, work.Width, work.Height);

        _board.PageChanged += OnPageChanged;
        _board.BackgroundChanged += RefreshBackground;
        _board.ImagesChanged += OnImagesChanged;
        Closed += OnClosed;

        BindPage();
        RefreshBackground();
        UpdatePageNavigation();
        Activated += (_, _) => Motion.PlayEntrance(_bottomBar, 20f);
    }

    private Border BuildPageStrip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _previousPageButton = Button("‹", "上一页", () => _board.PreviousPage(), 44);
        _nextPageButton = Button("›", "下一页", () => _board.NextPage(), 44);
        _addPageButton = Button("+", "在当前页后插入新页", () => _board.InsertPageAfterCurrent(), 44);
        _deletePageButton = Button("−", "删除当前页", DeleteCurrentPage, 44);
        row.Children.Add(_previousPageButton);
        row.Children.Add(_pageLabel);
        row.Children.Add(_nextPageButton);
        row.Children.Add(_pageCountLabel);
        row.Children.Add(_addPageButton);
        row.Children.Add(_deletePageButton);
        return Strip(row);
    }

    private Border BuildToolStrip()
    {
        var row = _toolButtons;
        row.Children.Add(ToolButton("选择", "选择并变换墨迹", InkTool.Select));
        var pen = ToolButton("画笔", "画笔与墨色设置", InkTool.Pen);
        pen.Flyout = BuildInkFlyout(highlighter: false);
        row.Children.Add(pen);
        var highlighter = ToolButton("荧光笔", "荧光笔与颜色设置", InkTool.Highlighter);
        highlighter.Flyout = BuildInkFlyout(highlighter: true);
        row.Children.Add(highlighter);
        var eraser = ToolButton("橡皮", "整笔擦除设置", InkTool.Eraser);
        eraser.Flyout = BuildEraserFlyout();
        row.Children.Add(eraser);
        var shapes = Button("形状", "选择几何图形工具", () => SetTool(InkTool.Shape));
        shapes.Tag = InkTool.Shape;
        shapes.Flyout = BuildShapeFlyout();
        row.Children.Add(shapes);
        _toolButtons.Children.Add(Button("撤销", "撤销当前页操作", () => _board.Current.Undo()));
        _toolButtons.Children.Add(Button("重做", "重做当前页操作", () => _board.Current.Redo()));
        return Strip(row);
    }

    private Border BuildActionStrip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var background = Button("底色", "背景、网格和页面缩放", () => { });
        background.Flyout = BuildBackgroundFlyout();
        row.Children.Add(background);
        row.Children.Add(Button("插图", "插入图片并在本页拖动或缩放", async () => await InsertImageAsync()));
        var file = Button("文件", "保存或打开画板文件", () => { });
        file.Flyout = BuildFileFlyout();
        row.Children.Add(file);
        row.Children.Add(Button("全屏", "切换全屏显示", ToggleFullscreen));
        row.Children.Add(Button("退出", "退出画板", ExitBoard));
        return Strip(row);
    }

    private Border BuildSelectionActions()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_selectionLabel);
        row.Children.Add(Button("删除所选", "删除选中的墨迹或图片", DeleteSelection));
        row.Children.Add(Button("完成", "收起选择工具", () => SetTool(InkTool.Pen)));
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8),
            Padding = new Thickness(8, 4, 8, 4),
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = row,
            Visibility = Visibility.Collapsed,
        };
    }

    private static Border Strip(UIElement child) => new()
    {
        Background = (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
        BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5),
        Padding = new Thickness(4, 2, 4, 2),
        Child = child,
    };

    private static Button Button(string text, string tooltip, Action onClick, double width = 58)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = width,
            MinHeight = 44,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private Button ToolButton(string text, string tooltip, InkTool tool)
    {
        var button = Button(text, tooltip, () => SetTool(tool), text.Length > 2 ? 74 : 60);
        button.Tag = tool;
        return button;
    }

    private void SetTool(InkTool tool)
    {
        _board.Current.Tool = tool;
        foreach (var button in _toolButtons.Children.OfType<Button>())
            button.Background = Equals(button.Tag, tool)
                ? (Brush)Application.Current.Resources["IcAccentSoftBrush"]
                : (Brush)Application.Current.Resources["IcSurfaceAltBrush"];
    }

    private MenuFlyout BuildInkFlyout(bool highlighter)
    {
        var colors = new (string name, InkColor color)[]
        {
            ("黑", InkColor.Opaque(25, 25, 25)), ("白", InkColor.Opaque(250, 250, 250)),
            ("红", InkColor.Opaque(220, 38, 38)), ("粉", InkColor.Opaque(219, 39, 119)),
            ("橙", InkColor.Opaque(234, 88, 12)), ("黄", InkColor.Opaque(202, 138, 4)),
            ("绿", InkColor.Opaque(22, 163, 74)), ("青", InkColor.Opaque(13, 148, 136)),
            ("蓝", InkColor.Opaque(37, 99, 235)), ("紫", InkColor.Opaque(147, 51, 234)),
        };
        return BuildPaletteFlyout(highlighter, colors);
    }


    private MenuFlyout BuildPaletteFlyout(bool highlighter, (string name, InkColor color)[] colors)
    {
        var flyout = new MenuFlyout();
        foreach (var (name, color) in colors)
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += (_, _) =>
            {
                _board.Current.CurrentAttributes.Color = ColorForCurrentBackground(color);
                SetTool(highlighter ? InkTool.Highlighter : InkTool.Pen);
            };
            flyout.Items.Add(item);
        }
        var thin = new MenuFlyoutSubItem { Text = "粗细" };
        foreach (var width in new[] { ("细 · 1.5 px", 1.5f), ("中 · 3 px", 3f), ("粗 · 6 px", 6f), ("特粗 · 10 px", 10f) })
        {
            var item = new MenuFlyoutItem { Text = width.Item1 };
            item.Click += (_, _) => _board.Current.CurrentAttributes.BaseWidth = width.Item2;
            thin.Items.Add(item);
        }
        flyout.Items.Add(thin);
        var taper = new MenuFlyoutSubItem { Text = "笔锋" };
        foreach (var style in new[] { ("点集笔锋", TaperKind.Tail), ("关闭笔锋", TaperKind.None), ("速率笔锋", TaperKind.Velocity), ("自然笔锋（测试）", TaperKind.Natural) })
        {
            var item = new MenuFlyoutItem { Text = style.Item1 };
            item.Click += (_, _) => _board.Current.CurrentAttributes.TaperKind = style.Item2;
            taper.Items.Add(item);
        }
        flyout.Items.Add(taper);
        return flyout;
    }

    private MenuFlyout BuildEraserFlyout()
    {
        var flyout = new MenuFlyout();
        foreach (var (name, radius) in new[] { ("细 · 8 px", 8f), ("中 · 16 px", 16f), ("粗 · 28 px", 28f), ("特粗 · 44 px", 44f) })
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += (_, _) => { _board.Current.EraserRadius = radius; SetTool(InkTool.Eraser); };
            flyout.Items.Add(item);
        }
        var info = new MenuFlyoutItem { Text = "当前引擎按整笔删除触及的墨迹", IsEnabled = false };
        flyout.Items.Add(info);
        var clear = new MenuFlyoutItem { Text = "清除当前页内容…" };
        clear.Click += async (_, _) => await ConfirmClearCurrentPageAsync();
        flyout.Items.Add(clear);
        return flyout;
    }

    private MenuFlyout BuildShapeFlyout()
    {
        var flyout = new MenuFlyout();
        foreach (var (label, shape) in new[]
        {
            ("直线", ShapeKind.Line), ("虚线", ShapeKind.DashedLine), ("点线", ShapeKind.DotLine),
            ("箭头", ShapeKind.Arrow), ("平行线", ShapeKind.ParallelLine),
            ("矩形", ShapeKind.Rectangle), ("圆角中心矩形", ShapeKind.RectangleFromCenter),
            ("椭圆", ShapeKind.Ellipse), ("圆", ShapeKind.Circle),
            ("坐标轴", ShapeKind.CoordinateBoth), ("三维坐标轴", ShapeKind.Coordinate3D),
            ("圆柱", ShapeKind.Cylinder), ("圆锥", ShapeKind.Cone), ("长方体", ShapeKind.Cuboid),
        })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) =>
            {
                _board.Current.ShapeKind = shape;
                SetTool(InkTool.Shape);
                flyout.Hide();
            };
            flyout.Items.Add(item);
        }
        return flyout;
    }

    private MenuFlyout BuildBackgroundFlyout()
    {
        var flyout = new MenuFlyout();
        foreach (var (label, background) in new[]
        {
            ("黑板", BoardBackground.Black), ("白板", BoardBackground.White),
            ("网格纸", BoardBackground.Grid), ("横线纸", BoardBackground.Lined),
        })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => SetBackground(background);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        foreach (var (label, scale) in new[] { ("缩小 · 80%", 0.8f), ("实际大小 · 100%", 1f), ("放大 · 125%", 1.25f), ("放大 · 150%", 1.5f) })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => SetViewScale(scale);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var spacing = new MenuFlyoutSubItem { Text = "网格间距" };
        foreach (var value in new[] { 24f, 32f, 40f, 56f, 72f })
        {
            var item = new MenuFlyoutItem { Text = $"{value:0} DIP" };
            item.Click += (_, _) => { _board.GridSpacing = value; _paper.Invalidate(); };
            spacing.Items.Add(item);
        }
        flyout.Items.Add(spacing);
        return flyout;
    }

    private MenuFlyout BuildFileFlyout()
    {
        var flyout = new MenuFlyout();
        var savePage = new MenuFlyoutItem { Text = "保存当前页…" };
        savePage.Click += async (_, _) => await SavePageAsync();
        var saveBoard = new MenuFlyoutItem { Text = "保存整个画板…" };
        saveBoard.Click += async (_, _) => await SaveBoardAsync();
        var openPage = new MenuFlyoutItem { Text = "打开到当前页…" };
        openPage.Click += async (_, _) => await OpenPageAsync();
        var openBoard = new MenuFlyoutItem { Text = "打开整个画板…" };
        openBoard.Click += async (_, _) => await OpenBoardAsync();
        flyout.Items.Add(savePage);
        flyout.Items.Add(saveBoard);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(openPage);
        flyout.Items.Add(openBoard);
        return flyout;
    }

    private void SetBackground(BoardBackground background)
    {
        _board.SetBackground(background);
        bool white = background != BoardBackground.Black;
        _services.Settings.Canvas.UsingWhiteboard = white;
        _services.SaveSettings();
    }

    private void SetViewScale(float scale)
    {
        scale = Math.Clamp(scale, 0.5f, 2f);
        if (_surface.ActualWidth <= 0 || _surface.ActualHeight <= 0) { _viewScale = scale; return; }
        float currentScale = ExtractScale(_board.Current.ViewTransform);
        float factor = scale / currentScale;
        var center = new Vector2((float)_surface.ActualWidth / 2, (float)_surface.ActualHeight / 2);
        var aroundCenter = Matrix3x2.CreateTranslation(-center)
            * Matrix3x2.CreateScale(factor)
            * Matrix3x2.CreateTranslation(center);
        _board.Current.ViewTransform *= aroundCenter;
        _viewScale = scale;
        _board.Current.Document.Touch();
        _images.UpdateViewTransform(_board.Current.ViewTransform);
        _paper.Invalidate();
    }

    private async Task InsertImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" })
                picker.FileTypeFilter.Add(extension);
            InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 32 * 1024 * 1024) { await ShowMessageAsync("图片文件超过 32 MiB，未插入。"); return; }
            var bytes = await ReadBytesAsync(file);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            float maxWidth = (float)Math.Max(240, _surface.ActualWidth * 0.55);
            float maxHeight = (float)Math.Max(180, _surface.ActualHeight * 0.55);
            float scale = Math.Min(1f, Math.Min(maxWidth / decoder.PixelWidth, maxHeight / decoder.PixelHeight));
            float width = Math.Max(1, decoder.PixelWidth * scale);
            float height = Math.Max(1, decoder.PixelHeight * scale);
            var asset = new BoardImageAsset
            {
                Name = file.Name,
                Data = bytes,
                ContentType = file.ContentType,
                Width = width,
                Height = height,
                Left = Math.Max(0, ((float)_surface.ActualWidth - width) / 2),
                Top = Math.Max(0, ((float)_surface.ActualHeight - height) / 2),
            };
            _board.AddImage(asset);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"图片插入失败：{ex.Message}");
        }
    }

    private async Task SavePageAsync()
    {
        var picker = CreateSavePicker("画板页面", ".icink", $"Page-{_board.CurrentIndex + 1}.icink");
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try { _board.SavePage(file.Path); }
        catch (Exception ex) { await ShowMessageAsync($"页面保存失败：{ex.Message}"); }
    }

    private async Task SaveBoardAsync()
    {
        var picker = CreateSavePicker("画板文件", ".icink", "InkCanvas-Board.icink");
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try { _board.SaveBoard(file.Path); }
        catch (Exception ex) { await ShowMessageAsync($"画板保存失败：{ex.Message}"); }
    }

    private async Task OpenPageAsync()
    {
        if ((_board.Current.Document.Count > 0 || _board.CurrentImages.Count > 0)
            && !await ConfirmAsync("替换当前页", "打开文件会替换当前页上的墨迹和图片。继续吗？")) return;
        var file = await PickInkFileAsync();
        if (file is null) return;
        try { _board.LoadPage(file.Path); }
        catch (Exception ex) { await ShowMessageAsync($"页面打开失败：{ex.Message}"); }
    }

    private async Task OpenBoardAsync()
    {
        if (_board.HasContent
            && !await ConfirmAsync("替换整个画板", "打开文件会替换当前画板的所有页面、墨迹和图片。继续吗？")) return;
        var file = await PickInkFileAsync();
        if (file is null) return;
        try { _board.LoadBoard(file.Path); }
        catch (Exception ex) { await ShowMessageAsync($"画板打开失败：{ex.Message}"); }
    }

    private FileSavePicker CreateSavePicker(string typeName, string extension, string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName) };
        picker.FileTypeChoices.Add(typeName, new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(this));
        return picker;
    }

    private async Task<StorageFile> PickInkFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".icink");
        InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(this));
        return await picker.PickSingleFileAsync();
    }

    private static async Task<byte[]> ReadBytesAsync(StorageFile file)
    {
        var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        int length = checked((int)buffer.Length);
        var bytes = new byte[length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task ShowMessageAsync(string message)
    {
        if (Content is not FrameworkElement root || root.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "知道了",
            XamlRoot = root.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private void DeleteSelection()
    {
        if (_images.SelectedImage is not null) _images.DeleteSelected();
        if (_board.Current.Selection.Count > 0) _board.Current.DeleteSelection();
    }

    private void DeleteCurrentPage() => _ = ConfirmDeleteCurrentPageAsync();

    private async Task ConfirmDeleteCurrentPageAsync()
    {
        if (!await ConfirmAsync("删除当前页", "删除当前页及其墨迹、图片？此操作无法从其他页恢复。")) return;
        _board.RemoveCurrentPage();
        UpdatePageNavigation();
    }

    private async Task ConfirmClearCurrentPageAsync()
    {
        if (!await ConfirmAsync("清除当前页", "清除当前页上的全部墨迹和图片？")) return;
        _board.Current.Clear();
        foreach (var image in _board.CurrentImages.ToList()) _board.RemoveImage(image);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Content is not FrameworkElement root || root.XamlRoot is null) return false;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            Content = _contentRoot;
            _isFullscreen = true;
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
            Content = _windowedContent;
            var work = Platform.System.ScreenService.WorkArea;
            WindowChrome.Place(this, work.X, work.Y, work.Width, work.Height);
            _isFullscreen = false;
        }
        UpdatePageNavigation();
    }

    private void ExitBoard()
    {
        if (_shell is not null) _shell.ToggleBoard();
        else Close();
    }

    private void BindPage()
    {
        if (_boundController is not null) _boundController.SelectionChanged -= OnStrokeSelectionChanged;
        _boundController = _board.Current;
        _boundController.SelectionChanged += OnStrokeSelectionChanged;
        if (!ReferenceEquals(_surface.Controller, _boundController))
            _surface.Controller = _boundController;
        _images.SetPage(_board);
        _images.UpdateViewTransform(_boundController.ViewTransform);
        _pageLabel.Text = $"{_board.CurrentIndex + 1} / {_board.PageCount}";
        _viewScale = ExtractScale(_boundController.ViewTransform);
        SetTool(_boundController.Tool);
        UpdatePageNavigation();
        UpdateSelectionActions();
        _paper.Invalidate();
    }

    private static float ExtractScale(Matrix3x2 transform)
        => Math.Clamp(MathF.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12), 0.5f, 2f);

    private void OnPageChanged()
    {
        _services.ApplySettings();
        BindPage();
        RefreshBackground();
        CurrentPageChanged?.Invoke();
    }

    private void UpdatePageNavigation()
    {
        _pageLabel.Text = $"{_board.CurrentIndex + 1}";
        _pageCountLabel.Text = $"共 {_board.PageCount} 页";
        _previousPageButton.IsEnabled = _board.CurrentIndex > 0;
        _nextPageButton.IsEnabled = _board.CurrentIndex < _board.PageCount - 1 || _board.PageCount < BoardService.MaximumPages;
        _addPageButton.IsEnabled = _board.PageCount < BoardService.MaximumPages;
        _deletePageButton.IsEnabled = _board.PageCount > 1;
    }

    private void OnStrokeSelectionChanged() => UpdateSelectionActions();

    private void OnImageSelectionChanged() => UpdateSelectionActions();

    private void UpdateSelectionActions()
    {
        int strokeCount = _board.Current.Selection.Count;
        int imageCount = _images.SelectedImage is null ? 0 : 1;
        _selectionActions.Visibility = strokeCount + imageCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        _selectionLabel.Text = strokeCount > 0 && imageCount > 0
            ? $"已选中 {strokeCount} 笔和图片"
            : imageCount > 0 ? "已选中图片" : $"已选中 {strokeCount} 笔";
    }

    private void OnImagesChanged()
    {
        _images.Refresh();
        _images.UpdateViewTransform(_board.Current.ViewTransform);
        UpdateSelectionActions();
    }

    private void RefreshBackground()
    {
        bool dark = _board.Background == BoardBackground.Black;
        var color = dark
            ? (Windows.UI.Color)Application.Current.Resources["IcBoardBlackColor"]
            : (Windows.UI.Color)Application.Current.Resources["IcBoardWhiteColor"];
        _background.Background = new SolidColorBrush(color);
        var boardTheme = Core.Theming.ThemeResolver.GetBoardTheme(!dark);
        WindowChrome.ApplyTheme(this, boardTheme == Core.Theming.AppTheme.Light ? 1 : 2);
        if (dark && IsBlackLike(_board.Current.CurrentAttributes.Color))
            _board.Current.CurrentAttributes.Color = InkColor.Opaque(245, 245, 245);
        else if (!dark && IsWhiteLike(_board.Current.CurrentAttributes.Color))
            _board.Current.CurrentAttributes.Color = InkColor.Opaque(20, 20, 20);
        _paper.Invalidate();
    }

    private InkColor ColorForCurrentBackground(InkColor color)
    {
        bool dark = _board.Background == BoardBackground.Black;
        if (dark && IsBlackLike(color)) return InkColor.Opaque(245, 245, 245);
        if (!dark && IsWhiteLike(color)) return InkColor.Opaque(20, 20, 20);
        return color;
    }

    private static bool IsBlackLike(InkColor color)
        => color.R <= 32 && color.G <= 32 && color.B <= 32;

    private static bool IsWhiteLike(InkColor color)
        => color.R >= 240 && color.G >= 240 && color.B >= 240;

    private void OnDrawPaper(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_board.Background is not (BoardBackground.Grid or BoardBackground.Lined)) return;
        float spacing = Math.Clamp(_board.GridSpacing, 12f, 160f);
        var line = Windows.UI.Color.FromArgb(55, 92, 113, 121);
        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        for (float y = spacing; y < height; y += spacing)
            args.DrawingSession.DrawLine(new Vector2(0, y), new Vector2(width, y), line, 1f);
        if (_board.Background == BoardBackground.Grid)
            for (float x = spacing; x < width; x += spacing)
                args.DrawingSession.DrawLine(new Vector2(x, 0), new Vector2(x, height), line, 1f);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _board.PageChanged -= OnPageChanged;
        _board.BackgroundChanged -= RefreshBackground;
        _board.ImagesChanged -= OnImagesChanged;
        _images.SelectionChanged -= OnImageSelectionChanged;
        if (_boundController is not null) _boundController.SelectionChanged -= OnStrokeSelectionChanged;
    }
}
