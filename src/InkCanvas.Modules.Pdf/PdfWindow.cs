using System.Numerics;
using InkCanvas.Controls.Controls;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Selection;
using InkCanvas.Host.Contracts;
using InkCanvas.Ink;
using InkCanvas.Pdf;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Streams;
using Rect = Windows.Foundation.Rect;

namespace InkCanvas.Modules.Pdf;

/// <summary>A single-page virtualized PDF workspace. Only the current page owns GPU surfaces.</summary>
public sealed class PdfWindow : Window, IDocumentWorkspace
{
    private readonly IDocumentWorkspaceHost _host;
    private readonly PdfAnnotationFile _file;
    private readonly PdfRasterizer _rasterizer;
    private readonly Dictionary<int, InkController> _controllers = new();
    private readonly Grid _root = new(), _viewport = new(), _sidebar = new() { Width = 196 };
    private readonly CanvasControl _paper = new() { IsHitTestVisible = false, ClearColor = Microsoft.UI.Colors.Transparent };
    private readonly InkSurface _ink = new();
    private readonly Canvas _selectionHost = new() { IsHitTestVisible = true };
    private readonly Border _selectionBorder = new() { Padding = new Thickness(4), CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(242, 250, 250, 250)), Visibility = Visibility.Collapsed };
    private readonly SelectionToolbar _selection;
    private readonly NumberBox _pageNumber = new() { Minimum = 1, Value = 1, Width = 112, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 1 };
    private readonly TextBlock _pageCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
    private readonly TextBlock _zoom = new() { Width = 68, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 4, 12, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ListView _pages = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ListView _searchResults = new() { SelectionMode = ListViewSelectionMode.Single, Visibility = Visibility.Collapsed };
    private readonly ListView _bookmarks = new() { SelectionMode = ListViewSelectionMode.Single, Visibility = Visibility.Collapsed };
    private readonly AutoSuggestBox _search = new() { PlaceholderText = "搜索文档文字", QueryIcon = new SymbolIcon(Symbol.Find), Margin = new Thickness(8) };
    private readonly DispatcherQueueTimer _detailTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource _renderCancellation, _detailCancellation, _searchCancellation;
    private CanvasBitmap _preview, _detail;
    private Rect _detailRect;
    private int _pageIndex, _generation, _version, _savedVersion;
    private Matrix3x2 _lastView;
    private bool _closed, _allowClose, _closing, _saving, _pageChanging, _renderPending, _selectionPending, _viewHasLayout;
    private string _targetPath;
    private FitMode _fitMode = FitMode.Page;
    private enum FitMode { Free, Page, Width }

    public Window Window => this;
    public InkController Controller { get; private set; }
    public Core.Settings.GestureSettings Gestures { get; } = new() { IsEnableMultiTouchMode = false };
    public bool SupportsViewRotation => false;
    public bool CanEdit => !_file.ReadOnly;
    public event Action ControllerChanged;
    public event Action InputBegan;
    public int PageIndex => _pageIndex;
    public bool IsDirty => _version != _savedVersion;
    public long PreviewCacheBytes => _rasterizer.CachedBytes;
    public string FilePath => _targetPath;
    public Task Cleanup { get; private set; } = Task.CompletedTask;

    internal PdfWindow(IDocumentWorkspaceHost host, PdfAnnotationFile file, PdfRasterizer rasterizer,
        Func<Task> open, Func<Task> import)
    {
        _host = host; _file = file; _rasterizer = rasterizer; _targetPath = file.SourcePath;
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);

        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(8, 3, 8, 3) };
        commands.Children.Add(Command("页面", "\uE8A5", () => { _sidebar.Visibility = _sidebar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; }));
        commands.Children.Add(Command("打开", "\uE8E5", async () => await open()));
        var save = Command("保存", "\uE74E", async () => await SaveAsync()); save.IsEnabled = !file.ReadOnly; commands.Children.Add(save);
        var saveAs = Command("另存为", "\uE792", async () => await SaveAsync(true)); saveAs.IsEnabled = !file.ReadOnly; commands.Children.Add(saveAs);
        commands.Children.Add(Command("上一页", "\uE76B", () => GoToPage(_pageIndex - 1)));
        commands.Children.Add(_pageNumber); commands.Children.Add(_pageCount);
        commands.Children.Add(Command("下一页", "\uE76C", () => GoToPage(_pageIndex + 1)));
        commands.Children.Add(Command("缩小", "\uE71F", () => ZoomBy(.8f)));
        commands.Children.Add(_zoom);
        commands.Children.Add(Command("放大", "\uE8A3", () => ZoomBy(1.25f)));
        commands.Children.Add(Command("整页", "\uE9A6", () => Fit(FitMode.Page)));
        commands.Children.Add(Command("页宽", "\uE7EA", () => Fit(FitMode.Width)));
        commands.Children.Add(Command("阅读", "\uE7C9", () => { Controller.Tool = InkTool.Pan; }));
        commands.Children.Add(Command("导入画板", "\uE8B5", async () => await import()));
        var commandScroll = new ScrollViewer { Content = commands, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        _root.Children.Add(commandScroll);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1); _root.Children.Add(body);
        _sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _sidebar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _sidebar.Children.Add(_search);
        _pages.ItemsSource = Enumerable.Range(1, file.Pages.Count).Select(p => "第 " + p + " 页").ToArray();
        _pages.SelectedIndex = 0;
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        tabs.Children.Add(Command("页码", "\uE8A5", () => { _pages.Visibility = Visibility.Visible; _bookmarks.Visibility = _searchResults.Visibility = Visibility.Collapsed; }));
        tabs.Children.Add(Command("目录", "\uE8A4", () =>
        {
            _bookmarks.Visibility = Visibility.Visible; _pages.Visibility = _searchResults.Visibility = Visibility.Collapsed;
            if (_file.Bookmarks.Count == 0) SetStatus("此 PDF 没有目录书签");
        }));
        Grid.SetRow(tabs, 1); _sidebar.Children.Add(tabs);
        Grid.SetRow(_pages, 2); _sidebar.Children.Add(_pages);
        Grid.SetRow(_searchResults, 2); _sidebar.Children.Add(_searchResults);
        Grid.SetRow(_bookmarks, 2); _sidebar.Children.Add(_bookmarks);
        _bookmarks.ItemsSource = file.Bookmarks.Select(bookmark => new BookmarkItem(bookmark)).ToArray();
        _bookmarks.SelectionChanged += (_, _) => { if (_bookmarks.SelectedItem is BookmarkItem item) GoToPage(item.Bookmark.PageIndex); };
        body.Children.Add(_sidebar);
        Grid.SetColumn(_viewport, 1); body.Children.Add(_viewport);
        _viewport.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 75, 78, 84));
        _viewport.Children.Add(_paper); _viewport.Children.Add(_ink); _viewport.Children.Add(_selectionHost);
        // A Canvas does not consume input on its empty area. Only the compact toolbar is hit-testable.
        _selection = new SelectionToolbar(() => Controller, async () => await SaveAsync(true), () => _ = import(), "导入画板");
        _selectionBorder.Child = _selection; _selectionHost.Children.Add(_selectionBorder);
        _selection.ApplyTheme(1);
        _selection.LayoutChanged += QueueSelection;
        _selectionBorder.SizeChanged += (_, _) => QueueSelection();
        Grid.SetRow(_status, 2); _root.Children.Add(_status);
        Content = _root;
        _host.ConfigureWindow(this);
        _root.RequestedTheme = ElementTheme.Light;
        _detailTimer = DispatcherQueue.CreateTimer(); _detailTimer.Interval = TimeSpan.FromMilliseconds(170); _detailTimer.IsRepeating = false;
        _detailTimer.Tick += (_, _) => _ = RenderDetailAsync();
        _paper.Draw += DrawPage;
        _paper.CreateResources += (_, _) => { if (!_closed && _viewHasLayout) _ = RenderPageAsync(); };
        _viewport.SizeChanged += (_, _) =>
        {
            if (_closed || _viewport.ActualWidth < 10 || _viewport.ActualHeight < 10) return;
            _viewHasLayout = true;
            if (_fitMode != FitMode.Free) Fit(_fitMode);
            else OnInvalidated();
            if (_preview is null && !_renderPending) _ = RenderPageAsync();
        };
        _ink.IsTabStop = true;
        _ink.InputBegan += () => { _ink.Focus(FocusState.Pointer); InputBegan?.Invoke(); _selection.ClosePanels(); };
        _ink.PointerWheelChanged += Wheel;
        _pageNumber.ValueChanged += (_, _) => { if (!_pageChanging && double.IsFinite(_pageNumber.Value)) GoToPage((int)_pageNumber.Value - 1); };
        _pages.SelectionChanged += (_, _) => { if (!_pageChanging && _pages.SelectedIndex >= 0) GoToPage(_pages.SelectedIndex); };
        _search.QuerySubmitted += async (_, _) => await SearchAsync(_search.Text);
        _search.TextChanged += (_, _) => { if (string.IsNullOrEmpty(_search.Text)) { _searchCancellation?.Cancel(); _searchResults.Visibility = _bookmarks.Visibility = Visibility.Collapsed; _pages.Visibility = Visibility.Visible; } };
        _searchResults.SelectionChanged += (_, _) => { if (_searchResults.SelectedItem is SearchItem item) GoToPage(item.Hit.PageIndex); };
        _root.KeyDown += KeyDown;
        AppWindow.Closing += (sender, e) => { if (!_allowClose) { e.Cancel = true; _ = CloseAsync(); } };
        Closed += (_, _) =>
        {
            _closed = true; _lifetime.Cancel(); _renderCancellation?.Cancel(); _detailCancellation?.Cancel(); _searchCancellation?.Cancel(); _detailTimer.Stop();
            _host.Detach(this);
            if (Controller is not null) Unbind(Controller);
            _ink.Dispose(); _preview?.Dispose(); _detail?.Dispose(); _preview = _detail = null;
            _paper.RemoveFromVisualTree();
            foreach (var controller in _controllers.Values) controller.Dispose();
            _controllers.Clear();
            Cleanup = ReleaseAsync();
        };
        AutomationProperties.SetName(_pageNumber, "PDF 页码");
        AutomationProperties.SetName(_pages, "PDF 页面列表");
        AutomationProperties.SetName(_searchResults, "PDF 搜索结果");
        _pageNumber.Maximum = file.Pages.Count; _pageCount.Text = "/ " + file.Pages.Count;
        GoToPage(0);
        UpdateTitle();
        SetStatus(file.ReadOnly ? "此文件只允许查看" : file.HasSignatures ? "含数字签名，批注请另存为副本" : "使用主工具栏批注 · 双指平移和缩放 · 阅读模式支持单指拖动");
        if (file.UnrecognizedInkPages > 0) SetStatus("部分旧墨迹数据无法编辑，原批注外观已保留");
    }

    private static TouchIconButton Command(string label, string glyph, Action action)
    {
        var button = new TouchIconButton { Label = label, Glyph = glyph, MinWidth = 54, MinHeight = 54, Padding = new Thickness(6, 2, 6, 2) };
        AutomationProperties.SetName(button, label); ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => action();
        return button;
    }
    private void Configure(InkController controller)
    {
        _host.ConfigureController(controller);
        controller.MultiTouchEnabled = Gestures.IsEnableMultiTouchMode;
        controller.PanZoomEnabled = true;
        controller.ConfigureGestures(Gestures.IsEnableTwoFingerZoom, Gestures.IsEnableTwoFingerTranslate, false);
        controller.ConfigureViewScaleLimits(.02f, 8);
        controller.CurrentAttributes.Color = InkColor.Red;
        if (_file.ReadOnly) controller.Tool = InkTool.Pan;
    }
    public void ApplySettings()
    {
        foreach (var controller in _controllers.Values)
        {
            var color = controller.CurrentAttributes.Color;
            Configure(controller);
            controller.CurrentAttributes.Color = color;
        }
    }
    public void GoToPage(int index)
    {
        if (_closed || index < 0 || index >= _file.Pages.Count || Controller is not null && index == _pageIndex) return;
        _pageChanging = true;
        try
        {
            var previous = Controller;
            if (previous is not null)
            {
                if (previous.HasActiveInteraction) { _pageNumber.Value = _pageIndex + 1; _pages.SelectedIndex = _pageIndex; return; }
                previous.FinishDotAnimations(); previous.EndPlayback(); previous.ClearSelection(); Unbind(previous);
            }
            _pageIndex = index;
            if (!_controllers.TryGetValue(index, out var next))
            {
                next = new InkController(); Configure(next);
                next.Document.AddRange(_file.GetInk(index).Select(stroke => stroke.Clone()));
                next.Document.Changed += OnInkChanged;
                next.LayersChanged += OnInkChanged;
                _controllers[index] = next;
            }
            if (previous is not null)
            {
                next.CurrentAttributes.Color = previous.CurrentAttributes.Color;
                next.CurrentAttributes.TaperKind = previous.CurrentAttributes.TaperKind;
                next.CurrentAttributes.Nib = previous.CurrentAttributes.Nib;
                next.ConfigurePenWidths((float)_host.Settings.Canvas.InkWidth, (float)(_host.Settings.Canvas.HighlighterWidth ?? _host.Settings.Canvas.InkWidth));
                next.CurrentAttributes.HighlighterAlpha = previous.CurrentAttributes.HighlighterAlpha;
                next.EraserRadius = previous.EraserRadius; next.EraserMode = previous.EraserMode;
                next.ShapeKind = previous.ShapeKind; next.StickyShape = previous.StickyShape;
                next.Tool = previous.Tool;
            }
            Controller = next;
            Controller.Invalidated += OnInvalidated; Controller.SelectionChanged += QueueSelection;
            Controller.ToolChanged += OnToolChanged;
            _ink.Controller = next;
            var geometry = _file.Pages[index]; _ink.ContentBounds = new Core.Ink.Rect(0, 0, geometry.DipWidth, geometry.DipHeight);
            _pageNumber.Value = index + 1; _pages.SelectedIndex = index;
            _generation++; _renderCancellation?.Cancel();
            _preview?.Dispose(); _detail?.Dispose(); _preview = _detail = null;
            if (_viewHasLayout) Fit(_fitMode == FitMode.Free ? FitMode.Page : _fitMode);
            ControllerChanged?.Invoke(); QueueSelection(); _paper.Invalidate();
            if (_viewHasLayout) _ = RenderPageAsync();
        }
        finally { _pageChanging = false; }
    }
    private void Unbind(InkController controller)
    {
        controller.Invalidated -= OnInvalidated; controller.SelectionChanged -= QueueSelection;
        controller.ToolChanged -= OnToolChanged;
    }
    private void OnToolChanged()
    {
        if (_file.ReadOnly && Controller.Tool != InkTool.Pan) Controller.Tool = InkTool.Pan;
        QueueSelection();
    }
    private void OnInkChanged() { _version++; UpdateTitle(); }
    private void UpdateTitle() => Title = (IsDirty ? "● " : "") + Path.GetFileName(_targetPath) + " — Ink Canvas PDF";
    private void SetStatus(string text) { if (!_closed) _status.Text = text; }

    private void OnInvalidated()
    {
        if (_closed) return;
        if (_lastView != Controller.ViewTransform)
        {
            if (Controller.IsViewManipulating) _fitMode = FitMode.Free;
            _lastView = Controller.ViewTransform;
            _detailCancellation?.Cancel();
            _paper.Invalidate();
            _zoom.Text = (Math.Sqrt(Math.Abs(_lastView.GetDeterminant())) * 100).ToString("0") + "%";
            _detailTimer.Stop(); _detailTimer.Start();
        }
        QueueSelection();
    }
    private void Fit(FitMode mode)
    {
        _fitMode = mode;
        if (!_viewHasLayout || Controller is null) return;
        var page = _file.Pages[_pageIndex];
        float availableWidth = (float)Math.Max(20, _viewport.ActualWidth - 32), availableHeight = (float)Math.Max(20, _viewport.ActualHeight - 32);
        float scale = Math.Clamp(mode == FitMode.Width ? availableWidth / page.DipWidth : Math.Min(availableWidth / page.DipWidth, availableHeight / page.DipHeight), .02f, 8);
        Controller.ViewTransform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(
            ((float)_viewport.ActualWidth - page.DipWidth * scale) / 2,
            mode == FitMode.Width ? 16 : ((float)_viewport.ActualHeight - page.DipHeight * scale) / 2);
        Controller.NotifyElementsChanged();
    }
    private void ZoomBy(float factor, Vector2? anchor = null)
    {
        if (Controller.HasActiveInteraction) return;
        _fitMode = FitMode.Free;
        var pivot = anchor ?? new Vector2((float)_viewport.ActualWidth / 2, (float)_viewport.ActualHeight / 2);
        float current = MathF.Sqrt(MathF.Abs(Controller.ViewTransform.GetDeterminant()));
        factor = Math.Clamp(current * factor, .02f, 8) / current;
        Controller.ViewTransform *= Matrix3x2.CreateScale(factor, pivot);
        Controller.NotifyElementsChanged();
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (Controller.HasActiveInteraction) { e.Handled = true; return; }
        var point = e.GetCurrentPoint(_ink);
        if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) != 0)
            ZoomBy(MathF.Pow(1.15f, point.Properties.MouseWheelDelta / 120f), new((float)point.Position.X, (float)point.Position.Y));
        else
        {
            _fitMode = FitMode.Free;
            float delta = point.Properties.MouseWheelDelta / 120f * 70;
            Controller.ViewTransform *= Matrix3x2.CreateTranslation(point.Properties.IsHorizontalMouseWheel ? delta : 0, point.Properties.IsHorizontalMouseWheel ? 0 : delta);
            Controller.NotifyElementsChanged();
        }
        e.Handled = true;
    }
    private async void KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool control = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        bool shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (control && e.Key == Windows.System.VirtualKey.S) { e.Handled = true; await SaveAsync(shift); }
        else if (control && e.Key == Windows.System.VirtualKey.F) { e.Handled = true; _sidebar.Visibility = Visibility.Visible; _search.Focus(FocusState.Programmatic); }
        else if (control && e.Key == Windows.System.VirtualKey.Z) { e.Handled = true; Controller.Undo(); }
        else if (control && e.Key == Windows.System.VirtualKey.Y) { e.Handled = true; Controller.Redo(); }
        else if (e.OriginalSource is not TextBox && e.OriginalSource is not NumberBox)
        {
            if (e.Key == Windows.System.VirtualKey.PageDown) { e.Handled = true; GoToPage(_pageIndex + 1); }
            else if (e.Key == Windows.System.VirtualKey.PageUp) { e.Handled = true; GoToPage(_pageIndex - 1); }
            else if (e.Key == Windows.System.VirtualKey.Delete) { e.Handled = true; Controller.DeleteSelection(); }
            else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Controller.ClearSelection(); }
        }
    }

    private async Task RenderPageAsync()
    {
        if (_closed || !_viewHasLayout) return;
        int generation = ++_generation, index = _pageIndex;
        _renderCancellation?.Cancel(); _renderCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _renderCancellation = cancellation;
        _renderPending = true;
        try
        {
            var bytes = await _rasterizer.PreviewAsync(index, _file.Pages[index], cancellation.Token);
            var bitmap = await LoadBitmapAsync(bytes, cancellation.Token);
            if (_closed || generation != _generation) { bitmap.Dispose(); return; }
            _preview?.Dispose(); _preview = bitmap; _paper.Invalidate();
            _detailTimer.Stop(); _detailTimer.Start();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && generation == _generation) SetStatus("页面渲染失败 · " + ex.Message); }
        finally { if (generation == _generation) _renderPending = false; }
    }
    private async Task RenderDetailAsync()
    {
        if (_closed || !_viewHasLayout || _preview is null) return;
        var view = Controller.ViewTransform;
        if (Controller.IsViewManipulating) { _detailTimer.Start(); return; }
        if (!Matrix3x2.Invert(view, out var inverse)) return;
        int generation = _generation, index = _pageIndex;
        var page = _file.Pages[index];
        var left = Vector2.Transform(new(-32, -32), inverse);
        var right = Vector2.Transform(new((float)_viewport.ActualWidth + 32, (float)_viewport.ActualHeight + 32), inverse);
        float x = Math.Clamp(left.X, 0, page.DipWidth), y = Math.Clamp(left.Y, 0, page.DipHeight);
        float width = Math.Clamp(right.X, 0, page.DipWidth) - x, height = Math.Clamp(right.Y, 0, page.DipHeight) - y;
        if (width <= 0 || height <= 0) return;
        var source = new Rect(x, y, width, height);
        _detailCancellation?.Cancel(); _detailCancellation?.Dispose();
        _detailCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _detailCancellation.Token;
        try
        {
            var bytes = await _rasterizer.RenderAsync(index, page, source, Math.Sqrt(Math.Abs(view.GetDeterminant())) * (_root.XamlRoot?.RasterizationScale ?? 1), token);
            var bitmap = await LoadBitmapAsync(bytes, token);
            if (_closed || generation != _generation || view != Controller.ViewTransform) { bitmap.Dispose(); return; }
            _detail?.Dispose(); _detail = bitmap; _detailRect = source; _paper.Invalidate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed && generation == _generation) SetStatus("高清页面渲染失败 · " + ex.Message); }
    }
    private async Task<CanvasBitmap> LoadBitmapAsync(byte[] bytes, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        { writer.WriteBytes(bytes); await writer.StoreAsync().AsTask(token); writer.DetachStream(); }
        stream.Seek(0);
        return await CanvasBitmap.LoadAsync(_paper, stream, 96).AsTask(token);
    }
    private void DrawPage(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_closed || Controller is null) return;
        var page = _file.Pages[_pageIndex]; var draw = args.DrawingSession;
        draw.Transform = Controller.ViewTransform;
        var bounds = new Rect(0, 0, page.DipWidth, page.DipHeight);
        draw.FillRectangle(bounds, Microsoft.UI.Colors.White);
        if (_preview is not null) draw.DrawImage(_preview, bounds, _preview.Bounds);
        if (_detail is not null) draw.DrawImage(_detail, _detailRect, _detail.Bounds);
    }

    private void QueueSelection()
    {
        if (_closed || _selectionPending) return;
        _selectionPending = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => { _selectionPending = false; if (!_closed) UpdateSelection(); });
    }
    private void UpdateSelection()
    {
        if (!Controller.HasSelection || Controller.Tool != InkTool.Select || Controller.Playback is not null || Controller.IsMarqueeSelecting || Controller.IsRotatingSelection)
        { _selectionBorder.Visibility = Visibility.Collapsed; return; }
        _selection.Refresh();
        _selectionBorder.Visibility = Visibility.Visible;
        _selectionBorder.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _selectionBorder.DesiredSize;
        // Reuse the shared avoidance algorithm, with the main floating bar mapped into this viewport.
        double dpi = _root.XamlRoot?.RasterizationScale ?? 1;
        var origin = _viewport.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point());
        var client = AppWindow.ClientSize; var position = AppWindow.Position; var windowSize = AppWindow.Size;
        float screenX = position.X + (windowSize.Width - client.Width) / 2f + (float)(origin.X * dpi);
        float screenY = position.Y + windowSize.Height - client.Height - (windowSize.Width - client.Width) / 2f + (float)(origin.Y * dpi);
        var bar = _host.ToolbarScreenBounds;
        var obstacle = bar.IsEmpty ? Core.Ink.Rect.Empty : new Core.Ink.Rect((bar.Left - screenX) / (float)dpi, (bar.Top - screenY) / (float)dpi, (bar.Right - screenX) / (float)dpi, (bar.Bottom - screenY) / (float)dpi);
        float scale = (float)Math.Min(1, Math.Max(1, _viewport.ActualWidth - 16) / Math.Max(1, size.Width));
        _selectionBorder.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
        var place = FloatingToolbarLayout.Place(new Core.Ink.Rect(8, 8, Math.Max(9, (float)_viewport.ActualWidth - 8), Math.Max(9, (float)_viewport.ActualHeight - 8)),
            Controller.SelectionVisualBounds, obstacle, new((float)size.Width * scale, (float)size.Height * scale), 10);
        Canvas.SetLeft(_selectionBorder, place.Left); Canvas.SetTop(_selectionBorder, place.Top);
    }
    private sealed record SearchItem(PdfSearchHit Hit)
    {
        public override string ToString() => "第 " + (Hit.PageIndex + 1) + " 页\n" + Hit.Preview;
    }
    private sealed record BookmarkItem(PdfBookmark Bookmark)
    {
        public override string ToString() => new string(' ', Math.Min(6, Bookmark.Depth) * 2) + Bookmark.Title;
    }
    public async Task SearchAsync(string query)
    {
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _searchCancellation = cancellation;
        try
        {
            SetStatus("正在搜索文字…");
            var hits = await _file.SearchAsync(query, cancellation.Token);
            if (_closed || cancellation.IsCancellationRequested) return;
            _searchResults.ItemsSource = hits.Select(hit => new SearchItem(hit)).ToArray();
            _searchResults.Visibility = Visibility.Visible; _pages.Visibility = _bookmarks.Visibility = Visibility.Collapsed;
            SetStatus("找到 " + hits.Count + " 个匹配页面" + (hits.Count == 0 ? " · 扫描件需要先进行文字识别" : ""));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus("搜索失败 · " + ex.Message); }
    }

    public async Task<bool> SaveAsync(bool saveAs = false)
    {
        if (_closed || _saving || _file.ReadOnly) return false;
        if (_controllers.Values.Any(controller => controller.HasActiveInteraction)) { SetStatus("请先完成当前笔画或拖动，再保存"); return false; }
        _saving = true;
        try
        {
            _searchCancellation?.Cancel();
            string path = _targetPath;
            if (saveAs || _file.HasSignatures && path.Equals(_file.SourcePath, StringComparison.OrdinalIgnoreCase))
            {
                var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(path) + " 批注" };
                picker.FileTypeChoices.Add("PDF 文档", new List<string> { ".pdf" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var result = await picker.PickSaveFileAsync(); if (result is null) return false;
                path = result.Path;
            }
            foreach (var controller in _controllers.Values) controller.FinishDotAnimations();
            var pages = new Dictionary<int, IReadOnlyList<InkStroke>>();
            for (int page = 0; page < _file.Pages.Count; page++)
                pages[page] = (_controllers.TryGetValue(page, out var controller) ? controller.Document.Strokes : _file.GetInk(page)).Select(stroke => stroke.Clone()).ToArray();
            int version = _version;
            SetStatus("正在保存 PDF 矢量批注…");
            await _file.SaveAsync(path, pages, _lifetime.Token);
            _targetPath = path; _savedVersion = version; UpdateTitle();
            SetStatus("已保存 · " + Path.GetFileName(path) + " · " + (new FileInfo(path).Length / 1024d).ToString("N0") + " KB");
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { if (!_closed) await MessageAsync("保存 PDF 失败", ex.GetBaseException().Message); return false; }
        finally { _saving = false; }
    }
    private Task<ContentDialogResult> MessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { Title = title, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "关闭", XamlRoot = _root.XamlRoot };
        return dialog.ShowAsync().AsTask();
    }
    public async Task<bool> CloseAsync()
    {
        if (_closed) return true;
        if (_closing || _saving) return false;
        _closing = true;
        try
        {
            _searchCancellation?.Cancel();
            foreach (var controller in _controllers.Values) controller.FinishDotAnimations();
            if (IsDirty)
            {
                var dialog = new ContentDialog { Title = "保存 PDF 批注？", Content = Path.GetFileName(_targetPath) + " 有未保存的批注。",
                    PrimaryButtonText = "保存", SecondaryButtonText = "不保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary, XamlRoot = _root.XamlRoot };
                Activate();
                var answer = await dialog.ShowAsync();
                if (answer == ContentDialogResult.None) return false;
                if (answer == ContentDialogResult.Primary && !await SaveAsync()) return false;
            }
            _allowClose = true; Close(); await Cleanup; return true;
        }
        catch (Exception ex) { SetStatus("关闭失败 · " + ex.Message); return false; }
        finally { _closing = false; }
    }
    internal void ForceClose() { if (!_closed) { _allowClose = true; Close(); } }
    private async Task ReleaseAsync()
    {
        await _rasterizer.DisposeAsync();
        await Task.Run(_file.Dispose);
        _renderCancellation?.Dispose(); _detailCancellation?.Dispose(); _searchCancellation?.Dispose(); _lifetime.Dispose();
    }
}
