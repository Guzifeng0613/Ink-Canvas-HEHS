using InkCanvas.App.Services;
using InkCanvas.Controls.Animation;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Shapes;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Features.Board;
using InkCanvas.Ink;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly TextBlock _pageCountLabel = new() { VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Opacity = 0.75 };
    private readonly TextBlock _selectionLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _pageListPanel = new() { Spacing = 4 };
    private Button _newPageButton;
    private Button _copyPageButton;
    private Button _deletePageButton;
    private Button _moveUpButton;
    private Button _moveDownButton;
    private Button _renamePageButton;
    private readonly Border _selectionActions;
    private readonly Border _bottomBar;
    private readonly Grid _contentRoot;
    private readonly Grid _windowedContent;
    private readonly StackPanel _toolButtons;
    private InkController _boundController;
    private bool _isFullscreen;
    private float _viewScale = 1f;
    private long _lastImageTapTicks;
    private Windows.Foundation.Point _lastImageTapPoint;
    private bool _editingAiCard;

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
        // Image views mark pointer presses as handled for dragging, so observe them at the layer
        // level to detect a double tap without touching the existing drag/delete interaction.
        _images.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnImageLayerPointerPressed), true);
        // Native gesture is registered in parallel so system-injected or synthesized double taps
        // still reach the same edit entry point.
        _images.AddHandler(UIElement.DoubleTappedEvent, new DoubleTappedEventHandler(OnImageDoubleTapped), true);

        var surfaceHost = new Grid();
        surfaceHost.Children.Add(_background);
        surfaceHost.Children.Add(_paper);
        surfaceHost.Children.Add(_surface);
        surfaceHost.Children.Add(_images);

        _toolButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var toolStrip = BuildToolStrip();
        var actionStrip = BuildActionStrip();
        var dock = new Grid { ColumnSpacing = 8 };
        dock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dock.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(toolStrip, 0);
        Grid.SetColumn(actionStrip, 1);
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

        var mainArea = new Grid();
        mainArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(surfaceHost, 0);
        mainArea.Children.Add(surfaceHost);
        Grid.SetRow(_selectionActions, 0);
        mainArea.Children.Add(_selectionActions);
        Grid.SetRow(_bottomBar, 1);
        mainArea.Children.Add(_bottomBar);

        var sidebar = BuildPageSidebar();
        _contentRoot = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
        _contentRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _contentRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidebar, 0);
        _contentRoot.Children.Add(sidebar);
        Grid.SetColumn(mainArea, 1);
        _contentRoot.Children.Add(mainArea);

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

    private Border BuildPageSidebar()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock { Text = "页面", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        _pageCountLabel.Text = "共 1 页";
        header.Children.Add(_pageCountLabel);

        var actions = new Grid { RowSpacing = 2, ColumnSpacing = 2, Margin = new Thickness(0, 6, 0, 6) };
        for (int c = 0; c < 3; c++)
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _newPageButton = Button("＋", "在当前页后新建页面", () => _board.InsertPageAfterCurrent(), 1);
        _copyPageButton = Button("⧉", "复制当前页", () => _board.CopyCurrentPage(), 1);
        _deletePageButton = Button("－", "删除当前页", DeleteCurrentPage, 1);
        _moveUpButton = Button("↑", "上移当前页", () => MoveCurrentPage(-1), 1);
        _moveDownButton = Button("↓", "下移当前页", () => MoveCurrentPage(1), 1);
        _renamePageButton = Button("✎", "重命名当前页", () => _ = RenameCurrentPageAsync(), 1);
        var actionButtons = new[]
        {
            (_newPageButton, 0, 0), (_copyPageButton, 0, 1), (_deletePageButton, 0, 2),
            (_moveUpButton, 1, 0), (_moveDownButton, 1, 1), (_renamePageButton, 1, 2),
        };
        foreach (var (button, row, column) in actionButtons)
        {
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(button, row);
            Grid.SetColumn(button, column);
            actions.Children.Add(button);
        }

        var scroll = new ScrollViewer
        {
            Content = _pageListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        panel.Children.Add(header);
        Grid.SetRow(actions, 1);
        panel.Children.Add(actions);
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);

        return new Border
        {
            Width = 176,
            Padding = new Thickness(6),
            Background = (Brush)Application.Current.Resources["IcSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = panel,
        };
    }

    private void RefreshPageList()
    {
        _pageListPanel.Children.Clear();
        for (int i = 0; i < _board.PageCount; i++)
            _pageListPanel.Children.Add(BuildPageListItem(_board.GetPageThumbnail(i)));
    }

    private UIElement BuildPageListItem(BoardPageThumbnail info)
    {
        bool current = info.Index == _board.CurrentIndex;
        bool dark = IsDarkBackground(info.Background);

        var thumb = new Border
        {
            Width = 42,
            Height = 30,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(PageBackgroundColor(info.Background)),
            BorderBrush = (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = info.StrokeCount > 0 ? info.StrokeCount.ToString() : string.Empty,
                Foreground = new SolidColorBrush(dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            },
        };

        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = $"第 {info.Index + 1} 页", FontSize = 11, Opacity = 0.7 });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(info.Name) ? "未命名" : info.Name,
            FontSize = 12,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(thumb);
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 48,
            Padding = new Thickness(6, 4, 6, 4),
            Background = current ? (Brush)Application.Current.Resources["IcAccentSoftBrush"] : (Brush)Application.Current.Resources["IcSurfaceAltBrush"],
            BorderBrush = current ? (Brush)Application.Current.Resources["IcAccentBrush"] : (Brush)Application.Current.Resources["IcStrokeBrush"],
            BorderThickness = current ? new Thickness(2) : new Thickness(1),
        };
        ToolTipService.SetToolTip(button, $"{info.Name}（{info.StrokeCount} 笔 / {info.ImageCount} 图）");
        int index = info.Index;
        button.Click += (_, _) => _board.GoTo(index);
        return button;
    }

    private void MoveCurrentPage(int delta)
    {
        int from = _board.CurrentIndex;
        _board.MovePage(from, from + delta);
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
        foreach (var style in new[] { ("点集笔锋", TaperKind.Tail), ("关闭笔锋", TaperKind.None), ("速率笔锋", TaperKind.Velocity) })
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
            ("黑板", BoardBackground.Black), ("黑板绿", BoardBackground.Green),
            ("护眼米黄", BoardBackground.Cream), ("纯白", BoardBackground.White),
            ("网格纸", BoardBackground.Grid), ("横线纸", BoardBackground.Lined),
            ("坐标系", BoardBackground.Coordinate),
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
        _services.Settings.Canvas.UsingWhiteboard = !IsDarkBackground(background);
        _services.SaveSettings();
        RefreshPageList();
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
        bool hasContent = _board.Current.Document.Count > 0 || _board.CurrentImages.Count > 0;
        if (_board.PageCount <= 1)
        {
            if (!await ConfirmAsync("清空当前页", "这是最后一页，将清空其全部墨迹和图片。继续吗？")) return;
        }
        else if (hasContent)
        {
            if (!await ConfirmAsync("删除当前页", "当前页含墨迹或图片，删除后无法从其他页恢复。继续吗？")) return;
        }
        else if (!await ConfirmAsync("删除当前页", "确定删除当前空白页吗？")) return;

        _board.RemoveCurrentPage();
        UpdatePageNavigation();
    }

    private async Task RenameCurrentPageAsync()
    {
        if (Content is not FrameworkElement root || root.XamlRoot is null) return;
        var input = new TextBox { Text = _board.PageName(_board.CurrentIndex), PlaceholderText = "页面名称" };
        var dialog = new ContentDialog
        {
            Title = "重命名页面",
            Content = input,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _board.RenamePage(_board.CurrentIndex, input.Text);
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
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                if (!ReferenceEquals(_contentRoot, Content))
                {
                    // The element may still be parented by the windowed chrome; detach it
                    // before handing it to Window.Content or the setter throws.
                    _windowedContent.Children.Remove(_contentRoot);
                    Content = null;
                    Content = _contentRoot;
                }
                _isFullscreen = true;
            }
            catch (Exception ex)
            {
                InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                    $"进入全屏失败：{ex}", InkCanvas.Core.Diagnostics.LogType.Error);
            }
        }
        else
        {
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                WindowChrome.MakeBorderless(this, topmost: false, resizable: true);
                if (!ReferenceEquals(_windowedContent, Content))
                {
                    Content = null;
                    if (!_windowedContent.Children.Contains(_contentRoot))
                    {
                        Grid.SetRow(_contentRoot, 1);
                        _windowedContent.Children.Add(_contentRoot);
                    }
                    Content = _windowedContent;
                }
                var work = Platform.System.ScreenService.WorkArea;
                WindowChrome.Place(this, work.X, work.Y, work.Width, work.Height);
                _isFullscreen = false;
            }
            catch (Exception ex)
            {
                InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                    $"退出全屏失败：{ex}", InkCanvas.Core.Diagnostics.LogType.Error);
            }
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
        _pageCountLabel.Text = $"共 {_board.PageCount} 页";
        if (_newPageButton is not null) _newPageButton.IsEnabled = _board.PageCount < BoardService.MaximumPages;
        if (_copyPageButton is not null) _copyPageButton.IsEnabled = _board.PageCount < BoardService.MaximumPages;
        if (_deletePageButton is not null) _deletePageButton.IsEnabled = _board.PageCount > 1;
        if (_moveUpButton is not null) _moveUpButton.IsEnabled = _board.CurrentIndex > 0;
        if (_moveDownButton is not null) _moveDownButton.IsEnabled = _board.CurrentIndex < _board.PageCount - 1;
        if (_renamePageButton is not null) _renamePageButton.IsEnabled = _board.PageCount >= 1;
        RefreshPageList();
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
        var background = _board.Background;
        bool dark = IsDarkBackground(background);
        _background.Background = new SolidColorBrush(PageBackgroundColor(background));
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
        bool dark = IsDarkBackground(_board.Background);
        if (dark && IsBlackLike(color)) return InkColor.Opaque(245, 245, 245);
        if (!dark && IsWhiteLike(color)) return InkColor.Opaque(20, 20, 20);
        return color;
    }

    private static bool IsDarkBackground(BoardBackground background)
        => background is BoardBackground.Black or BoardBackground.Green;

    private static Windows.UI.Color PageBackgroundColor(BoardBackground background) => background switch
    {
        BoardBackground.Black => (Windows.UI.Color)Application.Current.Resources["IcBoardBlackColor"],
        BoardBackground.Green => Windows.UI.Color.FromArgb(255, 0x1E, 0x3B, 0x2F),
        BoardBackground.Cream => Windows.UI.Color.FromArgb(255, 0xF5, 0xEF, 0xDC),
        _ => Windows.UI.Color.FromArgb(255, 0xFA, 0xFA, 0xFA),
    };

    private static bool IsBlackLike(InkColor color)
        => color.R <= 32 && color.G <= 32 && color.B <= 32;

    private static bool IsWhiteLike(InkColor color)
        => color.R >= 240 && color.G >= 240 && color.B >= 240;

    private void OnDrawPaper(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var background = _board.Background;
        bool dark = IsDarkBackground(background);
        float spacing = Math.Clamp(_board.GridSpacing, 12f, 160f);
        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        var line = dark
            ? Windows.UI.Color.FromArgb(70, 235, 235, 235)
            : Windows.UI.Color.FromArgb(55, 92, 113, 121);

        switch (background)
        {
            case BoardBackground.Grid:
                for (float y = spacing; y < height; y += spacing)
                    args.DrawingSession.DrawLine(new Vector2(0, y), new Vector2(width, y), line, 1f);
                for (float x = spacing; x < width; x += spacing)
                    args.DrawingSession.DrawLine(new Vector2(x, 0), new Vector2(x, height), line, 1f);
                break;

            case BoardBackground.Lined:
                for (float y = spacing; y < height; y += spacing)
                    args.DrawingSession.DrawLine(new Vector2(0, y), new Vector2(width, y), line, 1f);
                break;

            case BoardBackground.Coordinate:
                DrawCoordinateGrid(args, dark, spacing, width, height);
                break;
        }
    }

    private static void DrawCoordinateGrid(CanvasDrawEventArgs args, bool dark, float spacing, float width, float height)
    {
        var session = args.DrawingSession;
        var axis = dark
            ? Windows.UI.Color.FromArgb(200, 230, 230, 230)
            : Windows.UI.Color.FromArgb(190, 60, 80, 90);
        var tick = dark
            ? Windows.UI.Color.FromArgb(150, 220, 220, 220)
            : Windows.UI.Color.FromArgb(140, 60, 80, 90);

        float originX = width * 0.5f;
        float originY = height * 0.5f;
        const float tickHalf = 4f;

        session.DrawLine(new Vector2(0, originY), new Vector2(width, originY), axis, 1.6f);
        session.DrawLine(new Vector2(originX, 0), new Vector2(originX, height), axis, 1.6f);

        for (float x = originX + spacing; x < width; x += spacing)
            session.DrawLine(new Vector2(x, originY - tickHalf), new Vector2(x, originY + tickHalf), tick, 1f);
        for (float x = originX - spacing; x > 0; x -= spacing)
            session.DrawLine(new Vector2(x, originY - tickHalf), new Vector2(x, originY + tickHalf), tick, 1f);
        for (float y = originY + spacing; y < height; y += spacing)
            session.DrawLine(new Vector2(originX - tickHalf, y), new Vector2(originX + tickHalf, y), tick, 1f);
        for (float y = originY - spacing; y > 0; y -= spacing)
            session.DrawLine(new Vector2(originX - tickHalf, y), new Vector2(originX + tickHalf, y), tick, 1f);

        using var format = new Microsoft.Graphics.Canvas.Text.CanvasTextFormat { FontSize = 12 };
        session.DrawText("O", originX + 4, originY + 4, axis, format);
        session.DrawText("x", width - 18, originY + 4, axis, format);
        session.DrawText("y", originX + 4, 4, axis, format);
        session.DrawText($"单位：1 格 = {spacing:0} DIP", 8, height - 20, axis, format);
    }

    private void OnImageLayerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_images).Position;
        long now = Environment.TickCount64;
        bool isDoubleTap = now - _lastImageTapTicks <= 450
            && Math.Abs(point.X - _lastImageTapPoint.X) <= 6
            && Math.Abs(point.Y - _lastImageTapPoint.Y) <= 6;
        _lastImageTapTicks = isDoubleTap ? 0 : now;
        _lastImageTapPoint = point;
        if (!isDoubleTap) return;

        if (TryEditAiCardAt(point, "手动双击判定"))
            e.Handled = true;
    }

    private void OnImageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var point = e.GetPosition(_images);
        if (TryEditAiCardAt(point, "DoubleTapped"))
            e.Handled = true;
    }

    /// <summary>Shared edit entry point for both the manual tap heuristic and DoubleTapped.</summary>
    private bool TryEditAiCardAt(Windows.Foundation.Point point, string source)
    {
        var asset = HitTestImage((float)point.X, (float)point.Y);
        if (asset is null)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 卡片双击（{source}）：坐标 ({point.X:0.0},{point.Y:0.0}) 未命中任何资产",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            return false;
        }

        if (!string.Equals(asset.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 卡片双击（{source}）：命中资产 {asset.Name}，坐标 ({point.X:0.0},{point.Y:0.0})，ContentType={asset.ContentType} 非 image/png，忽略",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            return false;
        }

        var metadata = AiPasteMetadata.TryParse(asset.Metadata);
        if (metadata is null)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 卡片双击（{source}）：命中资产 {asset.Name}，坐标 ({point.X:0.0},{point.Y:0.0})，Metadata 为空或无法解析（长度 {asset.Metadata?.Length ?? 0}），忽略",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            return false;
        }

        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 卡片双击（{source}）：命中资产 {asset.Name}，坐标 ({point.X:0.0},{point.Y:0.0})，Kind={metadata.Kind}，进入编辑",
            InkCanvas.Core.Diagnostics.LogType.Event);
        _ = EditAiCardAsync(asset, metadata);
        return true;
    }

    private BoardImageAsset HitTestImage(float x, float y)
    {
        var images = _board.CurrentImages;
        for (int i = images.Count - 1; i >= 0; i--)
        {
            var image = images[i];
            if (x >= image.Left && x <= image.Left + image.Width
                && y >= image.Top && y <= image.Top + image.Height)
                return image;
        }
        return null;
    }

    private async Task EditAiCardAsync(BoardImageAsset asset, AiPasteMetadata metadata)
    {
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 卡片编辑：进入 EditAiCardAsync，资产 {asset.Name}，Kind={metadata.Kind}，已在编辑={_editingAiCard}",
            InkCanvas.Core.Diagnostics.LogType.Event);
        if (_editingAiCard)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                "AI 卡片编辑：已有编辑对话框，忽略重复请求",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            return;
        }
        if (Content is not FrameworkElement root || root.XamlRoot is null)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 卡片编辑：XamlRoot 为空（Content={Content?.GetType().Name ?? "null"}），无法打开对话框",
                InkCanvas.Core.Diagnostics.LogType.Error);
            return;
        }

        bool formula = metadata.Kind == AiPasteKind.Formula;
        string initial = formula
            ? metadata.Latex
            : metadata.Kind == AiPasteKind.Steps && metadata.Lines.Count > 0
                ? string.Join("\n", metadata.Lines)
                : metadata.Text;

        var editor = new TextBox
        {
            Text = initial,
            AcceptsReturn = !formula,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 360,
            MinHeight = formula ? 60 : 140,
        };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = formula ? "LaTeX 公式" : "卡片文本",
            Opacity = 0.7,
        });
        panel.Children.Add(editor);

        var dialog = new ContentDialog
        {
            Title = "编辑 AI 卡片",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
        };

        _editingAiCard = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            string text = editor.Text ?? "";
            if (formula && string.IsNullOrWhiteSpace(text))
            {
                await ShowMessageAsync("LaTeX 内容不能为空。");
                return;
            }

            var updated = new AiPasteMetadata
            {
                Version = metadata.Version > 0 ? metadata.Version : AiPasteMetadata.CurrentVersion,
                Kind = metadata.Kind,
                Annotations = metadata.Annotations.Select(points => new List<double>(points)).ToList(),
            };
            if (formula)
            {
                updated.Latex = text.Trim();
            }
            else
            {
                updated.Text = text;
                updated.Lines = SplitCardLines(text);
            }

            var lines = ContentLines(updated);
            var (estimatedWidth, estimatedHeight) = PasteCardLayout.Estimate(lines);
            int width = Math.Clamp((int)Math.Round(Math.Max(asset.Width, estimatedWidth)), 120, 640);
            int height = Math.Clamp((int)Math.Round(Math.Max(asset.Height, estimatedHeight)), 60, 480);
            byte[] png = await PasteCardRenderer.RenderPngAsync(asset.Name, lines, width, height);
            if (png is null || png.Length == 0)
            {
                await ShowMessageAsync("卡片重新渲染失败，原内容保持不变。");
                return;
            }

            if (!_board.UpdateImage(asset, png, updated.Serialize()))
                await ShowMessageAsync("卡片已不在当前页面，未更新。");
        }
        finally
        {
            _editingAiCard = false;
        }
    }

    private static List<string> ContentLines(AiPasteMetadata metadata)
    {
        if (metadata.Kind == AiPasteKind.Formula)
            return new List<string> { metadata.Latex };
        if (metadata.Lines.Count > 0) return metadata.Lines.ToList();
        return SplitCardLines(metadata.Text);
    }

    private static List<string> SplitCardLines(string text)
        => (text ?? "").Replace("\r", "").Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .ToList();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _board.PageChanged -= OnPageChanged;
        _board.BackgroundChanged -= RefreshBackground;
        _board.ImagesChanged -= OnImagesChanged;
        _images.SelectionChanged -= OnImageSelectionChanged;
        if (_boundController is not null) _boundController.SelectionChanged -= OnStrokeSelectionChanged;
    }
}
