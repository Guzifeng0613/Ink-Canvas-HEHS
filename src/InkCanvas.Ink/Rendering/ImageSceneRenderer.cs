using System.Numerics;
using InkCanvas.Core.Imaging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace InkCanvas.Ink.Rendering;

/// <summary>Decodes each image once and shares its bitmap or SVG document between frames and clones.</summary>
public sealed class ImageSceneRenderer : IDisposable
{
    private sealed class Entry
    {
        public byte[] Data;
        public CanvasBitmap Bitmap;
        public CanvasSvgDocument Vector;
        public Vector2 VectorSize;
        public string ContentType;
        public Task Loading;
    }

    private readonly Dictionary<byte[], Entry> _images = new();
    private bool _disposed;
    public int CachedBitmapCount => _images.Values.Count(entry => entry.Bitmap is not null || entry.Vector is not null);
    public int CachedVectorCount => _images.Values.Count(entry => entry.Vector is not null);
    public event Action Changed;

    public static IEnumerable<IImageSelectionElement> Images(InkController controller) =>
        controller.GetSelectableElements?.Invoke().OfType<IImageSelectionElement>()
            .Where(image => image.IsPresent && image.ImageData is { Length: > 0 }) ?? Enumerable.Empty<IImageSelectionElement>();

    public async Task PrepareAsync(CanvasDevice device, InkController controller)
    {
        var loads = Images(controller).Select(image => Get(device, image).Loading).Distinct().ToArray();
        await Task.WhenAll(loads);
    }

    public void Draw(CanvasDrawingSession session, InkController controller, Matrix3x2 view)
    {
        var images = Images(controller).ToArray();
        Retain(images);
        foreach (var image in images) Draw(session, image, view);
    }

    public void Draw(CanvasDrawingSession session, IImageSelectionElement image, Matrix3x2 view)
    {
        var entry = Get(session.Device, image);
        if (entry.Bitmap is null && entry.Vector is null) return;
        var previous = session.Transform;
        try
        {
            var frame = image.Frame;
            if (entry.Vector is not null)
            {
                session.Transform = Matrix3x2.CreateScale(frame.Bounds.Width / entry.VectorSize.X, frame.Bounds.Height / entry.VectorSize.Y)
                    * Matrix3x2.CreateTranslation(frame.Bounds.Left, frame.Bounds.Top) * frame.Transform * view;
                using var opacity = session.CreateLayer(Math.Clamp(image.Opacity, 0, 1));
                session.DrawSvg(entry.Vector, new Windows.Foundation.Size(entry.VectorSize.X, entry.VectorSize.Y));
                return;
            }
            session.Transform = frame.Transform * view;
            session.DrawImage(entry.Bitmap,
                new Windows.Foundation.Rect(frame.Bounds.Left, frame.Bounds.Top, frame.Bounds.Width, frame.Bounds.Height),
                new Windows.Foundation.Rect(0, 0, entry.Bitmap.Size.Width, entry.Bitmap.Size.Height),
                Math.Clamp(image.Opacity, 0, 1), CanvasImageInterpolation.Linear);
        }
        finally { session.Transform = previous; }
    }

    public void Retain(InkController controller) => Retain(Images(controller));

    private void Retain(IEnumerable<IImageSelectionElement> images)
    {
        var present = images.Select(image => image.ImageData).ToHashSet();
        foreach (var data in _images.Keys.Where(data => !present.Contains(data)).ToArray())
        {
            _images[data].Bitmap?.Dispose();
            _images[data].Vector?.Dispose();
            _images.Remove(data);
        }
    }

    private Entry Get(CanvasDevice device, IImageSelectionElement image)
    {
        if (_images.TryGetValue(image.ImageData, out var entry)) return entry;
        entry = new Entry { Data = image.ImageData, ContentType = image.ImageContentType };
        _images[image.ImageData] = entry;
        entry.Loading = LoadAsync(device, entry);
        return entry;
    }

    private async Task LoadAsync(CanvasDevice device, Entry entry)
    {
        try
        {
            if (string.Equals(entry.ContentType, SvgImage.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                using var data = new MemoryStream(entry.Data, writable: false);
                var image = SvgImage.Read(data);
                var vector = CanvasSvgDocument.LoadFromXml(device, image.Xml);
                if (!IsCurrent(entry)) { vector.Dispose(); return; }
                entry.Vector = vector;
                entry.VectorSize = image.Size;
                Changed?.Invoke();
                return;
            }
            using var managed = new MemoryStream(entry.Data, writable: false);
            using var stream = managed.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform(), ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(device, software);
            if (!IsCurrent(entry))
            {
                bitmap.Dispose();
                return;
            }
            entry.Bitmap = bitmap;
            Changed?.Invoke();
        }
        catch
        {
            // Failed images retain their entry until replaced, avoiding repeated decode attempts.
        }
    }

    private bool IsCurrent(Entry entry) => !_disposed && _images.TryGetValue(entry.Data, out var current) && ReferenceEquals(current, entry);

    public void Dispose()
    {
        _disposed = true;
        foreach (var entry in _images.Values)
        {
            entry.Bitmap?.Dispose();
            entry.Vector?.Dispose();
        }
        _images.Clear();
    }
}
