using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using InkCanvas.Core.Ink;
using InkCanvas.Host.Contracts;
using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using InkCanvas.Pdf;
using InkCanvas.Platform.Windows;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace InkCanvas.Modules.Pdf;

/// <summary>Runs inside the real WinUI host, including native PDF rasterization and GPU ink.</summary>
public static class PdfDiagnostics
{
    public static async Task RunAsync(IDocumentWorkspaceHost host, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<object>(); PdfWindow window = null;
        try
        {
            string source = Path.Combine(directory, "classroom.pdf"), saved = Path.Combine(directory, "annotations.pdf");
            CreateFixture(source, 6);
            var inks = CreateInk();
            using (var file = await PdfAnnotationFile.OpenAsync(source))
            {
                Check("crop rotation and user unit metadata", file.Pages.Count == 6 && file.Pages[1].NormalizedRotation == 90 && file.Pages[4].UserUnit == 2);
                Check("document bookmarks", file.Bookmarks.Count == 1 && file.Bookmarks[0].PageIndex == 5);
                var pages = Enumerable.Range(0, 5).ToDictionary(index => index, _ => (IReadOnlyList<InkStroke>)inks);
                var saveTime = Stopwatch.StartNew();
                await file.SaveAsync(saved, pages);
                long initialSize = new FileInfo(saved).Length;
                for (int repeat = 0; repeat < 3; repeat++) await file.SaveAsync(saved, pages);
                Check("repeated saves do not accumulate appearances", new FileInfo(saved).Length <= initialSize + 1024, new { bytes = initialSize, saveMilliseconds = saveTime.ElapsedMilliseconds / 4 });
            }
            using (var file = await PdfAnnotationFile.OpenAsync(saved))
            {
                Check("all brush points restore exactly", Enumerable.Range(0, 5).All(index => file.GetInk(index).Count == inks.Count && file.GetInk(index).All(stroke => stroke.Points.SequenceEqual(inks.Single(original => original.Id == stroke.Id).Points))));
                Check("original text remains searchable", (await file.SearchAsync("classroom")).Single().PageIndex == 5);
                await using var raw = await PdfRasterizer.OpenAsync(saved, null, CancellationToken.None);
                var device = CanvasDevice.GetSharedDevice();
                for (int index = 0; index < 5; index++)
                {
                    var geometry = file.Pages[index];
                    var png = await raw.RenderAsync(index, geometry, new(0, 0, geometry.DipWidth, geometry.DipHeight), 1, CancellationToken.None);
                    await File.WriteAllBytesAsync(Path.Combine(directory, "pdf-" + index + ".png"), png);
                    using var native = await BitmapAsync(png, device);
                    using var controller = new InkController(); controller.Document.AddRange(inks);
                    using var reference = new CanvasRenderTarget(device, (float)native.Size.Width, (float)native.Size.Height, 96);
                    using var scene = new InkSceneRenderer();
                    using (var draw = reference.CreateDrawingSession())
                    {
                        draw.Clear(Microsoft.UI.Colors.White);
                        scene.DrawCommitted(draw, controller, Matrix3x2.CreateScale((float)native.Size.Width / geometry.DipWidth, (float)native.Size.Height / geometry.DipHeight));
                    }
                    await reference.SaveAsync(Path.Combine(directory, "ink-reference-" + index + ".png"), CanvasBitmapFileFormat.Png);
                    byte[] actual = native.GetPixelBytes(), expected = reference.GetPixelBytes();
                    long union = 0, intersection = 0, totalError = 0;
                    for (int p = 0; p < expected.Length; p += 4)
                    {
                        bool a = Math.Min(actual[p], Math.Min(actual[p + 1], actual[p + 2])) < 235;
                        bool b = Math.Min(expected[p], Math.Min(expected[p + 1], expected[p + 2])) < 235;
                        if (a || b) { union++; totalError += Math.Abs(actual[p] - expected[p]) + Math.Abs(actual[p + 1] - expected[p + 1]) + Math.Abs(actual[p + 2] - expected[p + 2]); }
                        if (a && b) intersection++;
                    }
                    double overlap = intersection / (double)Math.Max(1, union), error = totalError / (double)Math.Max(1, union * 3);
                    Check("vector appearance matches native ink page " + index, overlap > .995 && error < 1, new { overlap, meanChannelError = error, rotation = geometry.NormalizedRotation, geometry.UserUnit });
                }
            }
            var workspaceFile = await PdfAnnotationFile.OpenAsync(source);
            var rasterizer = await PdfRasterizer.OpenAsync(workspaceFile, CancellationToken.None);
            window = new PdfWindow(host, workspaceFile, rasterizer, () => Task.CompletedTask, () => Task.CompletedTask);
            host.Attach(window); window.Activate(); await Task.Delay(900);
            Check("native document workspace attaches", ReferenceEquals(host.Owner, window));
            var pen = window.Controller; pen.RecognizeShapes = false;
            var first = Vector2.Transform(new(140, 200), pen.ViewTransform);
            var input = new OverlayPointer(431, first.X, first.Y, .5f, 4, 4, OverlayPointerKind.Touch, 1000);
            pen.OnPointerDown(input);
            for (int i = 1; i <= 45; i++)
            {
                var point = Vector2.Transform(new(140 + i * 4, 200 + MathF.Sin(i / 5f) * 24), pen.ViewTransform);
                input = input with { X = point.X, Y = point.Y, TimeMs = (uint)(1000 + i * 6) }; pen.OnPointerMove(input);
            }
            pen.OnPointerUp(input); pen.FinishDotAnimations();
            Check("original touch ink controller commits PDF annotation", pen.Document.Count == 1 && window.IsDirty);
            var expectedPoints = pen.Document.Strokes[0].Points.ToArray();
            window.GoToPage(1); var second = window.Controller;
            window.GoToPage(0);
            Check("page changes restore independent ink and history", !ReferenceEquals(pen, second) && ReferenceEquals(pen, window.Controller) && pen.Document.Count == 1 && second.Document.Count == 0);
            Check("native window save", await window.SaveAsync() && !window.IsDirty);
            using (var restored = await PdfAnnotationFile.OpenAsync(source)) Check("workspace edits persist inside PDF", restored.GetInk(0).Single().Points.SequenceEqual(expectedPoints));
            await window.SearchAsync("classroom");
            for (int i = 0; i < 16; i++) { window.GoToPage(i % 6); await Task.Delay(40); }
            window.GoToPage(0); await Task.Delay(800);
            Check("preview cache remains bounded", window.PreviewCacheBytes <= 16 * 1024 * 1024, new { bytes = window.PreviewCacheBytes, workingSet = Process.GetCurrentProcess().WorkingSet64 });
            await ScreenshotAsync(window, Path.Combine(directory, "pdf-window.png"));
            Check("window closes cleanly", await window.CloseAsync()); window = null;
            CreateFixture(Path.Combine(directory, "many-pages.pdf"), 160);
            using (var large = await PdfAnnotationFile.OpenAsync(Path.Combine(directory, "many-pages.pdf")))
            {
                var watch = Stopwatch.StartNew(); await using var renderer = await PdfRasterizer.OpenAsync(large, CancellationToken.None);
                for (int index = 0; index < 30; index++) await renderer.PreviewAsync(index, large.Pages[index], CancellationToken.None);
                Check("large PDFs render on demand with bounded cache", renderer.CachedBytes <= 16 * 1024 * 1024, new { pages = large.Pages.Count, renderedPages = 30, milliseconds = watch.ElapsedMilliseconds, renderer.CachedBytes });
            }
            string fixtures = Path.GetFullPath(Path.Combine(directory, "..", "pdf-fixtures"));
            if (Directory.Exists(fixtures))
            foreach (string path in Directory.GetFiles(fixtures, "*.pdf"))
            {
                using var original = await PdfAnnotationFile.OpenAsync(path);
                await using var before = await PdfRasterizer.OpenAsync(original, CancellationToken.None);
                var preview = await before.PreviewAsync(0, original.Pages[0], CancellationToken.None);
                string output = Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-annotated.pdf");
                await original.SaveAsync(output, new Dictionary<int, IReadOnlyList<InkStroke>> { [0] = inks });
                using var reloaded = await PdfAnnotationFile.OpenAsync(output);
                await using var after = await PdfRasterizer.OpenAsync(reloaded, CancellationToken.None);
                var restored = await after.PreviewAsync(0, reloaded.Pages[0], CancellationToken.None);
                using var sourceBitmap = await BitmapAsync(preview, CanvasDevice.GetSharedDevice());
                using var restoredBitmap = await BitmapAsync(restored, CanvasDevice.GetSharedDevice());
                Check("external PDF original content preserved " + Path.GetFileName(path), reloaded.Pages.Count == original.Pages.Count && sourceBitmap.GetPixelBytes().SequenceEqual(restoredBitmap.GetPixelBytes()),
                    new { pages = original.Pages.Count, sourceBytes = new FileInfo(path).Length, savedBytes = new FileInfo(output).Length });
            }
        }
        catch (Exception ex) { checks.Add(new { check = "unhandled PDF regression error", pass = false, detail = ex.ToString() }); }
        finally
        {
            if (window is not null) { window.ForceClose(); await window.Cleanup; }
            await File.WriteAllTextAsync(Path.Combine(directory, "pdf.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Check(string check, bool pass, object detail = null) => checks.Add(new { check, pass, detail });
    }
    private static List<InkStroke> CreateInk()
    {
        var result = new List<InkStroke>();
        foreach (var nib in Enum.GetValues<PenNib>())
        foreach (var taper in new[] { TaperKind.None, TaperKind.Tail, TaperKind.Calligraphy })
        {
            float y = 150 + result.Count * 64;
            var stroke = new InkStroke(new StrokeAttributes { Color = InkColor.Red, BaseWidth = 15, Nib = nib, TaperKind = taper });
            for (int p = 0; p < 140; p++) stroke.AddPoint(90 + p * 3, y + MathF.Sin(p / 12f) * 16, .5f, 1000 + p * 5);
            stroke.Complete(); result.Add(stroke);
        }
        var marker = new InkStroke(new StrokeAttributes { Color = new InkColor(255, 255, 210, 0), BaseWidth = 36, IsHighlighter = true, HighlighterAlpha = 96, Nib = PenNib.Ellipse45 }) { LayerOrder = 2 };
        for (int p = 0; p < 180; p++) marker.AddPoint(100 + MathF.Sin(p / 30f) * 95 + p * 1.8f, 600 + MathF.Cos(p / 18f) * 22, .5f, 1000 + p * 4);
        marker.Complete(); result.Add(marker);
        var dot = new InkStroke(new StrokeAttributes { Color = InkColor.Black, BaseWidth = 20, TaperKind = TaperKind.None }); dot.AddPoint(540, 300); dot.Complete(); result.Add(dot);
        var under = new InkStroke(new StrokeAttributes { Color = InkColor.Black, BaseWidth = 14, TaperKind = TaperKind.None }) { LayerOrder = 1 };
        under.AddPoint(90, 600); under.AddPoint(550, 600); under.Complete(); result.Add(under);
        var longStroke = new InkStroke(new StrokeAttributes { Color = InkColor.Red, BaseWidth = 7, TaperKind = TaperKind.Velocity });
        for (int p = 0; p < 1100; p++) longStroke.AddPoint(100 + p * .4f, 700 + MathF.Sin(p / 22f) * 12, .5f, p * 4);
        longStroke.Complete(); result.Add(longStroke);
        return result;
    }
    private static void CreateFixture(string path, int count)
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (int p = 0; p < count; p++)
        {
            var page = document.AddPage(); page.Width = XUnit.FromPoint(600); page.Height = XUnit.FromPoint(800); page.Rotate = (p % 4) * 90;
            page.CropBox = new PdfRectangle(new XRect(20, 30, 560, 740));
            if (p == 4) page.Elements.SetReal("/UserUnit", 2);
            if (p == 5)
            {
                var font = new PdfDictionary(document); font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica"); document.Internals.AddObject(font);
                var fonts = new PdfDictionary(document); fonts.Elements["/F1"] = font.Reference;
                var resources = new PdfDictionary(document); resources.Elements["/Font"] = fonts; page.Elements["/Resources"] = resources;
                var content = new PdfDictionary(document); content.CreateStream(Encoding.ASCII.GetBytes("BT /F1 20 Tf 70 720 Td (InkCanvas classroom PDF) Tj ET")); document.Internals.AddObject(content); page.Elements["/Contents"] = content.Reference;
                document.Outlines.Add("Classroom", page, true);
            }
        }
        document.Save(path);
    }
    private static async Task<CanvasBitmap> BitmapAsync(byte[] bytes, CanvasDevice device)
    {
        using var memory = new InMemoryRandomAccessStream(); using var writer = new DataWriter(memory); writer.WriteBytes(bytes); await writer.StoreAsync(); writer.DetachStream(); memory.Seek(0);
        return await CanvasBitmap.LoadAsync(device, memory, 96);
    }
    private static async Task ScreenshotAsync(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(window.Content as FrameworkElement);
        var buffer = await bitmap.GetPixelsAsync(); var bytes = new byte[buffer.Length]; DataReader.FromBuffer(buffer).ReadBytes(bytes);
        using var memory = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, memory);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes); await encoder.FlushAsync();
        memory.Seek(0); using var output = File.Create(path); await memory.AsStreamForRead().CopyToAsync(output);
    }
}
