using InkCanvas.Core.AiAssistant;
using InkCanvas.Features.AiAssistant;
using InkCanvas.Features.Board;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using Windows.Storage.Streams;

namespace InkCanvas.App.Views;

/// <summary>Per-page raster layer. Images remain in the same DIP coordinate space as board ink.</summary>
internal sealed class BoardImageLayer : Canvas
{
    private const string SelectionBorderTag = "selection-border";
    private const string ResizeHandleTag = "resize-handle";

    private readonly Dictionary<Guid, Grid> _views = new();
    private BoardService _board;
    private BoardImageAsset _selected;
    private Grid _dragTarget;
    private bool _resizing;
    private int _refreshVersion;
    private Windows.Foundation.Point _pointerStart;
    private BoardImageGeometry _geometryAtStart;

    public BoardImageLayer()
    {
        Background = null;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerReleased;
        PointerCaptureLost += OnPointerReleased;
    }

    public BoardImageAsset SelectedImage => _selected;

    public event Action SelectionChanged;

    public void SetPage(BoardService board)
    {
        _board = board;
        _selected = null;
        Refresh();
    }

    public void UpdateViewTransform(System.Numerics.Matrix3x2 transform)
    {
        RenderTransform = new MatrixTransform
        {
            Matrix = new Microsoft.UI.Xaml.Media.Matrix(
                transform.M11, transform.M12, transform.M21, transform.M22,
                transform.M31, transform.M32),
        };
    }

    public void Refresh()
    {
        var annotationLayers = new List<CanvasControl>();
        foreach (var view in _views.Values)
        {
            foreach (var child in view.Children)
            {
                if (child is CanvasControl layer) annotationLayers.Add(layer);
            }
        }

        Children.Clear();
        foreach (var layer in annotationLayers) layer.RemoveFromVisualTree();
        _views.Clear();
        if (_board is null) return;
        if (_selected is not null && !_board.CurrentImages.Contains(_selected))
        {
            _selected = null;
            SelectionChanged?.Invoke();
        }
        int version = ++_refreshVersion;
        foreach (var asset in _board.CurrentImages)
            _ = AddViewAsync(asset, version);
    }

    private async Task AddViewAsync(BoardImageAsset asset, int version)
    {
        try
        {
            var bitmap = await LoadBitmapAsync(asset.Data);
            if (version != _refreshVersion || !_board.CurrentImages.Contains(asset)) return;
            var image = new Image { Source = bitmap, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
            var border = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = SelectionBorderTag,
            };
            var handle = new Border
            {
                Width = 12,
                Height = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = (Brush)Application.Current.Resources["IcAccentBrush"],
                Visibility = Visibility.Collapsed,
                Tag = ResizeHandleTag,
            };
            var view = new Grid
            {
                Width = asset.Width,
                Height = asset.Height,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = asset,
            };
            view.Children.Add(image);

            // 图形释义矢量层：只读取资产元数据，不写入墨迹 Document、不与墨迹合并。
            var annotationLayer = BuildShapeAnnotationLayer(asset);
            if (annotationLayer is not null) view.Children.Add(annotationLayer);

            view.Children.Add(border);
            view.Children.Add(handle);
            view.PointerPressed += OnImagePointerPressed;
            view.PointerMoved += OnImagePointerMoved;
            view.PointerReleased += OnImagePointerReleased;
            view.PointerCanceled += OnImagePointerReleased;
            view.PointerCaptureLost += OnImagePointerReleased;
            Canvas.SetLeft(view, asset.Left);
            Canvas.SetTop(view, asset.Top);
            _views[asset.Id] = view;
            Children.Add(view);
            SetSelectedVisual(view, asset == _selected);
        }
        catch
        {
            // Keep the rest of the board usable if an embedded image is corrupt or unsupported.
        }
    }

    private static CanvasControl BuildShapeAnnotationLayer(BoardImageAsset asset)
    {
        if (!string.Equals(asset.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)) return null;

        var metadata = AiPasteMetadata.TryParse(asset.Metadata);
        if (metadata is null || metadata.Kind != AiPasteKind.Shape) return null;
        if (metadata.Annotations is not { Count: > 0 }) return null;

        var lines = new List<List<ShapeAnnotationPoint>>();
        foreach (var flat in metadata.Annotations)
        {
            if (flat is null) continue;
            var normalized = ShapeAnnotationGeometry.Normalize(flat, asset.Width, asset.Height);
            if (normalized.Count >= 2) lines.Add(normalized);
        }

        if (lines.Count == 0) return null;

        var canvas = new CanvasControl
        {
            ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
            IsHitTestVisible = false,
            Width = asset.Width,
            Height = asset.Height,
            Tag = "shape-annotation-layer",
        };

        canvas.Draw += (sender, args) =>
        {
            float width = (float)(sender.ActualWidth > 0 ? sender.ActualWidth : sender.Width);
            float height = (float)(sender.ActualHeight > 0 ? sender.ActualHeight : sender.Height);
            if (width <= 0 || height <= 0) return;

            var accent = AccentColor();
            foreach (var line in lines)
            {
                var points = ShapeAnnotationGeometry.Project(line, 0, 0, width, height);
                for (int i = 1; i < points.Count; i++)
                {
                    args.DrawingSession.DrawLine(
                        new Vector2((float)points[i - 1].X, (float)points[i - 1].Y),
                        new Vector2((float)points[i].X, (float)points[i].Y),
                        accent,
                        2f);
                }
            }
        };
        canvas.SizeChanged += (_, _) => canvas.Invalidate();
        return canvas;
    }

    private static Windows.UI.Color AccentColor()
    {
        try
        {
            if (Application.Current?.Resources["IcAccentBrush"] is SolidColorBrush brush)
                return brush.Color;
        }
        catch
        {
            // Theme resources may be unavailable during teardown; fall back to black.
        }

        return Microsoft.UI.Colors.Black;
    }

    private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage> LoadBitmapAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private void OnImagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid view || view.Tag is not BoardImageAsset asset) return;
        Select(asset);
        _dragTarget = view;
        var local = e.GetCurrentPoint(view).Position;
        _resizing = local.X >= view.ActualWidth - 20 && local.Y >= view.ActualHeight - 20;
        _pointerStart = e.GetCurrentPoint(this).Position;
        _geometryAtStart = GetGeometry(asset);
        view.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnImagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, _dragTarget) || _dragTarget?.Tag is not BoardImageAsset asset) return;
        var point = e.GetCurrentPoint(this).Position;
        float dx = (float)(point.X - _pointerStart.X);
        float dy = (float)(point.Y - _pointerStart.Y);
        if (_resizing)
        {
            float aspect = _geometryAtStart.Width / Math.Max(1, _geometryAtStart.Height);
            float width = Math.Max(32, _geometryAtStart.Width + dx);
            float height = Math.Max(32, _geometryAtStart.Height + dy);
            if (MathF.Abs(dx) >= MathF.Abs(dy)) height = width / Math.Max(0.05f, aspect);
            else width = height * aspect;
            asset.Width = width;
            asset.Height = height;
            _dragTarget.Width = width;
            _dragTarget.Height = height;
            foreach (var child in _dragTarget.Children)
            {
                if (child is not CanvasControl layer) continue;
                layer.Width = width;
                layer.Height = height;
                layer.Invalidate();
            }
        }
        else
        {
            asset.Left = _geometryAtStart.Left + dx;
            asset.Top = _geometryAtStart.Top + dy;
            Canvas.SetLeft(_dragTarget, asset.Left);
            Canvas.SetTop(_dragTarget, asset.Top);
        }
        e.Handled = true;
    }

    private void OnImagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid view || !ReferenceEquals(view, _dragTarget) || view.Tag is not BoardImageAsset asset) return;
        var after = GetGeometry(asset);
        _dragTarget = null;
        _board.RecordImageTransform(asset, _geometryAtStart, after);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragTarget is null) return;
        _dragTarget = null;
    }

    public void DeleteSelected()
    {
        if (_selected is null) return;
        var selected = _selected;
        _selected = null;
        _board.RemoveImage(selected);
        SelectionChanged?.Invoke();
    }

    private void Select(BoardImageAsset asset)
    {
        if (_selected == asset) return;
        _selected = asset;
        foreach (var (id, view) in _views)
            SetSelectedVisual(view, id == asset.Id);
        SelectionChanged?.Invoke();
    }

    private static void SetSelectedVisual(Grid view, bool selected)
    {
        foreach (var child in view.Children)
        {
            if (child is Border border && string.Equals(border.Tag as string, SelectionBorderTag, StringComparison.Ordinal))
            {
                border.BorderBrush = selected
                    ? (Brush)Application.Current.Resources["IcAccentBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
            else if (child is Border handle && string.Equals(handle.Tag as string, ResizeHandleTag, StringComparison.Ordinal))
            {
                handle.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private static BoardImageGeometry GetGeometry(BoardImageAsset image)
        => new(image.Left, image.Top, image.Width, image.Height);
}
