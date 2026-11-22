using InkCanvas.Core.AiAssistant;
using InkCanvas.Controls.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;

namespace InkCanvas.Modules.AiAssistant;

public sealed partial class AssistantWindow
{
    private const double MaxThumbnailHeight = 220, MinRegionSize = 6, DragThreshold = 4;
    private readonly Image _thumbnail = new() { Stretch = Stretch.Fill }, _contextThumbnail = new() { Stretch = Stretch.UniformToFill };
    private readonly Border _thumbnailHost = new() { CornerRadius = new CornerRadius(8) };
    private readonly Canvas _selectionOverlay = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Canvas _annotationCanvas = new() { IsHitTestVisible = false };
    private readonly Rectangle _selectionRect = new() { StrokeThickness = 1.5, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly StackPanel _problemList = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _problemScroll = new() { HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 140 };
    private readonly Flyout _preview = new();
    private readonly Dictionary<string, BitmapImage> _problemImages = new();
    private readonly List<InMemoryRandomAccessStream> _problemStreams = new();
    private readonly DispatcherTimer _highlightTimer = new() { Interval = TimeSpan.FromSeconds(1.6) };
    private InMemoryRandomAccessStream _thumbStream;
    private string _thumbBase64 = "";
    private ProblemContext _problem;
    private AssistantRect _anchor;
    private Windows.Foundation.Point _thumbStart;
    private bool _thumbTracking, _thumbDragging, _previewOpen, _previewDirty;
    private int _thumbnailGeneration, _problemGeneration;
    private (string Id, int Order, string ImageBase64)[] _displayRegions = Array.Empty<(string, int, string)>();
    private readonly Brush _accentSolidBrush = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
    private static readonly Brush WhiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);

    private void BuildPreview()
    {
        _selectionRect.Stroke = _accentBrush; _selectionRect.Fill = _accentSoftBrush;
        _selectionOverlay.Children.Add(_selectionRect);
        _selectionOverlay.PointerPressed += OnThumbnailPointerPressed;
        _selectionOverlay.PointerMoved += OnThumbnailPointerMoved;
        _selectionOverlay.PointerReleased += OnThumbnailPointerReleased;
        _selectionOverlay.PointerCanceled += OnThumbnailPointerCanceled;
        _selectionOverlay.PointerCaptureLost += OnThumbnailPointerCanceled;
        var image = new Grid(); image.Children.Add(_thumbnail); image.Children.Add(_annotationCanvas); image.Children.Add(_selectionOverlay);
        _thumbnailHost.Child = image;
        _thumbnailHost.SizeChanged += (_, _) => ApplyAnnotations(_lastSession);
        _problemScroll.Content = _problemList;
        _highlightTimer.Tick += (_, _) => { _highlightTimer.Stop(); _selectionRect.Visibility = Visibility.Collapsed; };
        var content = new StackPanel { Width = 296, Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "当前题目选区", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(_thumbnailHost); content.Children.Add(_problemScroll);
        content.Children.Add(new TextBlock { Text = "回到画布框选后可追加题干或条件。单选区时，在预览中拖动矩形可局部纠错。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = _textSecondary });
        _preview.Content = content;
        _preview.Opened += (_, _) => { _previewOpen = true; RefreshProblemPreview(); UpdateThumbnailSize(); ApplyAnnotations(_lastSession); };
        _preview.Closed += (_, _) => { _previewOpen = false; _thumbTracking = _thumbDragging = false; _highlightTimer.Stop(); };
    }

    private void UpdateThumbnailSize()
    {
        _thumbnailHost.Height = _anchor.IsEmpty ? 160 : Math.Clamp(296 * _anchor.Height / _anchor.Width, 28, MaxThumbnailHeight);
    }

    /// <summary>设置缩略图对应的原选区锚点（屏幕 DIP），用于把框选换算回选区坐标。</summary>
    public void SetAnchor(AssistantRect anchor)
    {
        _anchor = anchor;
        UpdateThumbnailSize();
        ApplyAnnotations(_lastSession);
    }

    /// <summary>
    /// 渲染拼题选区列表。单选区保持原有大图与圈选纠错；多选区改为横向缩略图，
    /// 支持按顺序追加、删除与前后移动，重排后由插件服务重新请求。
    /// </summary>
    public void ApplyProblem(ProblemContext problem, string notice = null)
    {
        if (!string.IsNullOrWhiteSpace(notice)) ShowNotice(notice);

        _problem = problem;
        _contextTitle.Text = problem is { Count: > 0 } ? $"题目选区 ({problem.Count})" : "当前画布";
        var regions = ProblemContextPolicy.Ordered(problem).Select(region => (region.Id, region.Order, region.ImageBase64)).ToArray();
        if (_displayRegions.SequenceEqual(regions)) return;
        _displayRegions = regions;
        _previewDirty = true;
        if (_previewOpen) RefreshProblemPreview();
    }

    private void RefreshProblemPreview()
    {
        if (!_previewDirty) return;
        _previewDirty = false;
        _problemGeneration++;
        _problemImages.Clear();
        foreach (var stream in _problemStreams) stream.Dispose();
        _problemStreams.Clear();
        _problemList.Children.Clear();
        bool multiple = _problem is { Count: > 1 };
        _problemScroll.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
        _thumbnailHost.Visibility = !multiple && _thumbnail.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!multiple) return;
        foreach (var region in ProblemContextPolicy.Ordered(_problem)) _problemList.Children.Add(CreateProblemCard(region));
        _problemList.Children.Add(CreateAddRegionCard());
    }

    private FrameworkElement CreateProblemCard(ProblemRegion region)
    {
        var image = new Image
        {
            Height = 64,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (!string.IsNullOrEmpty(region.ImageBase64))
        {
            if (_problemImages.TryGetValue(region.ImageBase64, out BitmapImage cached))
            {
                image.Source = cached;
            }
            else
            {
                _ = LoadProblemImageAsync(region.ImageBase64, image);
            }
        }

        var badge = new Border
        {
            Background = _accentSolidBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 0, 0),
            Child = new TextBlock
            {
                Text = region.Order.ToString(),
                FontSize = 10,
                Foreground = WhiteBrush,
            },
        };

        var moveLeft = ProblemCardButton("◀", "前移", () => MoveRegionRequested?.Invoke(region.Id, -1));
        var moveRight = ProblemCardButton("▶", "后移", () => MoveRegionRequested?.Invoke(region.Id, 1));
        var remove = ProblemCardButton("✕", "移除此选区", () => RemoveRegionRequested?.Invoke(region.Id));
        var retry = ProblemCardButton("↻", "重新识别此选区",
            () => ReRecognizeRegionRequested?.Invoke(region.Id));
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        controls.Children.Add(moveLeft);
        controls.Children.Add(moveRight);
        controls.Children.Add(retry);
        controls.Children.Add(remove);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(image, 0);
        Grid.SetRow(badge, 0);
        Grid.SetRow(controls, 1);
        layout.Children.Add(image);
        layout.Children.Add(badge);
        layout.Children.Add(controls);

        return new Border
        {
            Width = 184,
            Background = _inputBgBrush,
            BorderBrush = _controlStrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            Child = layout,
        };
    }

    private FrameworkElement CreateAddRegionCard()
    {
        var content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = "＋",
            FontSize = 20,
            Foreground = _accentBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = "追加选区",
            FontSize = 10.5,
            Foreground = _textSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var button = new Button
        {
            Width = 184,
            Height = 112,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = _controlStrokeBrush,
            BorderThickness = new Thickness(1),
            Content = content,
        };
        ToolTipService.SetToolTip(button, "先回画布框选题干/图形/条件，再点此追加为同一题");
        button.Click += (_, _) => AddRegionRequested?.Invoke();
        return button;
    }

    private Button ProblemCardButton(string glyph, string tip, Action invoke)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                Foreground = _textSecondary,
            },
            Width = 40,
            Height = 40,
            MinWidth = 40,
            MinHeight = 40,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => invoke();
        return button;
    }

    private async Task LoadProblemImageAsync(string base64, Image target)
    {
        int generation = _problemGeneration;
        InMemoryRandomAccessStream stream = null;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = 360 };
            await bitmap.SetSourceAsync(stream);
            if (_closed || generation != _problemGeneration) return;
            _problemImages[base64] = bitmap;
            _problemStreams.Add(stream);
            stream = null;
            target.Source = bitmap;
        }
        catch (Exception)
        {
            // 单个缩略图损坏不影响其他选区与正文。
        }
        finally { stream?.Dispose(); }
    }

    /// <summary>
    /// 把解释步骤涉及的顶点/边/角绘制为缩略图上的高亮层。定位不可靠时不绘制，
    /// 只保留文字解释，避免误导学生。
    /// </summary>
    private void ApplyAnnotations(AssistantSession session)
    {
        _annotationCanvas.Children.Clear();
        if (session is null) return;

        AnnotationLayer layer = AnnotationLayerPolicy.Build(session, _anchor);
        AnnotationLayerPolicy.SyncVisibility(layer, session);
        if (!_annotationsVisible) AnnotationLayerPolicy.SetAllVisible(layer, false);

        double width = _annotationCanvas.ActualWidth;
        double height = _annotationCanvas.ActualHeight;
        if (width <= 0) width = _thumbnailHost.ActualWidth;
        if (height <= 0) height = _thumbnailHost.ActualHeight;
        if (!layer.CanLocate || width <= 0 || height <= 0) return;

        foreach (AnnotationHighlightItem item in layer.Items)
        {
            if (!item.Visible) continue;

            List<ShapeAnnotationPoint> projected = ShapeAnnotationGeometry.Project(
                item.Points, 0, 0, width, height);
            if (projected.Count < 2) continue;

            var line = new Polyline
            {
                Stroke = _accentBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            foreach (ShapeAnnotationPoint point in projected)
                line.Points.Add(new Windows.Foundation.Point(point.X, point.Y));
            _annotationCanvas.Children.Add(line);
        }
    }

    private void OnThumbnailPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 圈选按下：enabled={RegionSelectionEnabled} anchorEmpty={_anchor.IsEmpty}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        if (!RegionSelectionEnabled || _anchor.IsEmpty) return;

        _thumbStart = e.GetCurrentPoint(_selectionOverlay).Position;
        _thumbTracking = true;
        _thumbDragging = false;
        _selectionOverlay.CapturePointer(e.Pointer);
        _highlightTimer.Stop();
        _selectionRect.Visibility = Visibility.Collapsed;
    }

    private void OnThumbnailPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_thumbTracking) return;

        Windows.Foundation.Point current = e.GetCurrentPoint(_selectionOverlay).Position;
        double dx = current.X - _thumbStart.X;
        double dy = current.Y - _thumbStart.Y;
        if (!_thumbDragging && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold) return;

        if (!_thumbDragging)
        {
            _thumbDragging = true;
            _selectionOverlay.CapturePointer(e.Pointer);
        }

        // 只有真正开始画框才吞掉指针事件，避免轻触时阻塞 ScrollViewer 滚动。
        e.Handled = true;
        UpdateSelectionVisual(_thumbStart, current);
    }

    private void OnThumbnailPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_thumbTracking) return;

        _thumbTracking = false;
        if (!_thumbDragging)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        _thumbDragging = false;
        Windows.Foundation.Point current = e.GetCurrentPoint(_selectionOverlay).Position;
        try
        {
            _selectionOverlay.ReleasePointerCapture(e.Pointer);
        }
        catch (Exception)
        {
            // 指针可能已被系统释放。
        }

        (double left, double top, double right, double bottom) = Normalize(_thumbStart, current);
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选松手：rect=({left:0.0},{top:0.0})-({right:0.0},{bottom:0.0})，"
            + $"anchor=({_anchor.Left:0.0},{_anchor.Top:0.0})-({_anchor.Right:0.0},{_anchor.Bottom:0.0})，"
            + $"RegionSelectionEnabled={RegionSelectionEnabled}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        if (right - left < MinRegionSize || bottom - top < MinRegionSize)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateSelectionVisual(_thumbStart, current);
        AssistantRect region = MapToAnchor(left, top, right, bottom);
        if (region.IsEmpty)
        {
            InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
                $"AI 缩略图圈选：映射后区域为空（缩略图 {_selectionOverlay.ActualWidth:0.0}x{_selectionOverlay.ActualHeight:0.0}），忽略",
                InkCanvas.Core.Diagnostics.LogType.Trace);
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选：发送 RegionSelected，region=({region.Left:0.0},{region.Top:0.0})-({region.Right:0.0},{region.Bottom:0.0})",
            InkCanvas.Core.Diagnostics.LogType.Event);
        _highlightTimer.Start();
        RegionSelected?.Invoke(region);
    }

    private void OnThumbnailPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        InkCanvas.Core.Diagnostics.FileLog.WriteLogToFile(
            $"AI 缩略图圈选取消：tracking={_thumbTracking} dragging={_thumbDragging}",
            InkCanvas.Core.Diagnostics.LogType.Trace);
        _thumbTracking = false;
        _thumbDragging = false;
        _selectionRect.Visibility = Visibility.Collapsed;
    }

    private void UpdateSelectionVisual(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        (double left, double top, double right, double bottom) = Normalize(start, end);
        Canvas.SetLeft(_selectionRect, left);
        Canvas.SetTop(_selectionRect, top);
        _selectionRect.Width = Math.Max(0, right - left);
        _selectionRect.Height = Math.Max(0, bottom - top);
        _selectionRect.Visibility = Visibility.Visible;
    }

    private static (double Left, double Top, double Right, double Bottom) Normalize(
        Windows.Foundation.Point start,
        Windows.Foundation.Point end)
    {
        return (
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y));
    }

    private AssistantRect MapToAnchor(double left, double top, double right, double bottom)
    {
        double width = _selectionOverlay.ActualWidth;
        double height = _selectionOverlay.ActualHeight;
        if (width <= 0 || height <= 0 || _anchor.IsEmpty) return default;

        double scaleX = _anchor.Width / width;
        double scaleY = _anchor.Height / height;

        return new AssistantRect(
            _anchor.Left + left * scaleX,
            _anchor.Top + top * scaleY,
            _anchor.Left + right * scaleX,
            _anchor.Top + bottom * scaleY);
    }

    private async Task LoadThumbnailAsync(string base64)
    {
        int generation = ++_thumbnailGeneration;
        _thumbBase64 = base64;
        InMemoryRandomAccessStream stream = null;
        try
        {
            byte[] bytes = Convert.FromBase64String(base64);
            stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = 640 };
            await bitmap.SetSourceAsync(stream);
            if (_closed || generation != _thumbnailGeneration) return;
            _thumbStream?.Dispose();
            _thumbStream = stream;
            stream = null;
            _thumbnail.Source = bitmap;
            _contextThumbnail.Source = bitmap;
            _thumbnailHost.Visibility = _problem is { Count: > 1 }
                ? Visibility.Collapsed
                : Visibility.Visible;
            _thumbBase64 = base64;
            UpdateThumbnailSize();
        }
        catch (Exception)
        {
            // 截图可能是空或损坏的 base64；正文仍应正常显示。
        }
        finally { stream?.Dispose(); }
    }

}
