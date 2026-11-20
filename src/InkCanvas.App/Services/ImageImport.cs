using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using InkCanvas.Core.Imaging;
using InkCanvas.Features.Board;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.UI.Xaml;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libwebp;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace InkCanvas.App.Services;

internal static class ImageImport
{
    private static readonly Lazy<bool> WebpReady = new(() =>
    {
        CodecManager.Configure(codecs => codecs.UseLibwebp());
        return true;
    });

    public static IReadOnlyList<string> SupportedExtensions => new[]
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".dib", ".gif", ".tif", ".tiff",
        ".ico", ".jxr", ".wdp", ".hdp", ".webp", ".svg", ".svgz",
    }.Concat(BitmapDecoder.GetDecoderInformationEnumerator().SelectMany(codec => codec.FileExtensions))
        .Where(extension => extension.StartsWith('.') && !extension.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase).ToArray();

    public static async Task<BoardImageAsset> PickAsync(Window owner, bool compress, Vector2 viewport, Matrix3x2 view)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var extension in SupportedExtensions)
            picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowChrome.GetHwnd(owner));
        var file = await picker.PickSingleFileAsync();
        return file is null ? null : await LoadAsync(file, compress, viewport, view);
    }

    public static async Task<BoardImageAsset> LoadAsync(StorageFile file, bool compress, Vector2 viewport, Matrix3x2 view)
    {
        try { return await LoadCoreAsync(file, compress, viewport, view); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x88982F8B))
        {
            throw new InvalidOperationException($"当前系统无法启用 {Path.GetExtension(file.Name)} 图像解码器。请安装对应的 Windows 图像扩展，或将图片转换为 PNG/JPEG 后插入。", ex);
        }
    }

    private static async Task<BoardImageAsset> LoadCoreAsync(StorageFile file, bool compress, Vector2 viewport, Matrix3x2 view)
    {
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 32 * 1024 * 1024) throw new InvalidOperationException("图片文件超过 32 MiB。");
        using var stream = await file.OpenReadAsync();
        string extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (extension is ".svg" or ".svgz")
        {
            using var managed = stream.AsStreamForRead();
            var svg = await Task.Run(() => SvgImage.Read(managed, extension == ".svgz"));
            using var document = CanvasSvgDocument.LoadFromXml(CanvasDevice.GetSharedDevice(), svg.Xml);
            return Place(file.Name, Encoding.UTF8.GetBytes(svg.Xml), SvgImage.ContentType, svg.Size, viewport, view);
        }
        if (extension == ".webp")
        {
            using var input = stream.AsStreamForRead();
            var decoded = await Task.Run(() =>
            {
                _ = WebpReady.Value;
                using var output = new MemoryStream();
                var settings = new ProcessImageSettings
                {
                    Width = compress ? 1920 : 0,
                    Height = compress ? 1080 : 0,
                    ResizeMode = CropScaleMode.Max,
                    DecoderOptions = new WebpDecoderOptions(0..1, AllowPlanar: false, UseBackgroundColor: false),
                };
                settings.TrySetEncoderFormat(ImageMimeTypes.Png);
                var result = MagicImageProcessor.ProcessImage(input, output, settings);
                return (Data: output.ToArray(), Size: new Vector2(result.Settings.Width, result.Settings.Height));
            });
            return Place(file.Name, decoded.Data, "image/png", decoded.Size, viewport, view);
        }
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint frameIndex = 0;
        if (extension == ".ico")
        {
            ulong largestArea = 0;
            for (uint index = 0; index < decoder.FrameCount; index++)
            {
                var candidate = await decoder.GetFrameAsync(index);
                ulong area = (ulong)candidate.PixelWidth * candidate.PixelHeight;
                if (area > largestArea) { largestArea = area; frameIndex = index; }
            }
        }
        var frame = await decoder.GetFrameAsync(frameIndex);
        float baseWidth = frame.OrientedPixelWidth, baseHeight = frame.OrientedPixelHeight;
        byte[] bytes;
        string contentType = decoder.DecoderInformation.CodecId == BitmapDecoder.JpegDecoderId ? "image/jpeg" : "image/png";
        bool downscale = compress && (baseWidth > 1920 || baseHeight > 1080);
        bool normalize = decoder.DecoderInformation.CodecId != BitmapDecoder.PngDecoderId
            && decoder.DecoderInformation.CodecId != BitmapDecoder.JpegDecoderId;
        if (downscale || normalize)
        {
            float scale = downscale ? Math.Min(1920f / baseWidth, 1080f / baseHeight) : 1;
            uint width = (uint)Math.Max(1, Math.Round(baseWidth * scale));
            uint height = (uint)Math.Max(1, Math.Round(baseHeight * scale));
            bool rotated = frame.OrientedPixelWidth != frame.PixelWidth || frame.OrientedPixelHeight != frame.PixelHeight;
            var pixels = await frame.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = rotated ? height : width, ScaledHeight = rotated ? width : height },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            using var compressed = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, compressed);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData());
            await encoder.FlushAsync();
            compressed.Seek(0);
            bytes = await ReadAsync(compressed);
            contentType = "image/png";
            baseWidth = width; baseHeight = height;
        }
        else
        {
            stream.Seek(0);
            bytes = await ReadAsync(stream);
        }
        return Place(file.Name, bytes, contentType, new Vector2(baseWidth, baseHeight), viewport, view);
    }

    internal static BoardImageAsset Place(string name, byte[] data, string contentType, Vector2 naturalSize, Vector2 viewport, Matrix3x2 view)
    {
        float baseWidth = naturalSize.X, baseHeight = naturalSize.Y;
        float factor = Math.Min(viewport.X / 2 / Math.Max(1, baseWidth), viewport.Y / 2 / Math.Max(1, baseHeight));
        float placedWidth = Math.Max(1, baseWidth * factor), placedHeight = Math.Max(1, baseHeight * factor);
        return new BoardImageAsset
        {
            Name = name, Data = data, ContentType = contentType,
            Width = placedWidth, Height = placedHeight,
            Left = (viewport.X - placedWidth) / 2, Top = (viewport.Y - placedHeight) / 2,
            Transform = Matrix3x2.Invert(view, out var inverse) ? BoardElementMath.Write(inverse) : null,
        };
    }

    private static async Task<byte[]> ReadAsync(IRandomAccessStream stream)
    {
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
