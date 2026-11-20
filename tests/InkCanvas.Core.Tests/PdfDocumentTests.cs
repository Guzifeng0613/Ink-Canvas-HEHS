using System.Numerics;
using InkCanvas.Core.Ink;
using InkCanvas.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using Xunit;

namespace InkCanvas.Core.Tests;

public sealed class PdfDocumentTests
{
    private static InkStroke Stroke(float x = 100, bool highlighter = false, PenNib nib = PenNib.Round)
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 15, Color = InkColor.Red, IsHighlighter = highlighter, HighlighterAlpha = 93, Nib = nib, TaperKind = TaperKind.Tail });
        for (int p = 0; p < 240; p++) stroke.AddPoint(x + p, 120 + MathF.Sin(p / 25f) * 40, .6f, 1000 + p * 4);
        stroke.Complete(); return stroke;
    }
    private static void CreateFixture(string path, int pages = 3, int rotation = 0, bool encrypted = false)
    {
        using var document = new PdfDocument(); document.Info.Title = "PDF annotation fixture";
        for (int i = 0; i < pages; i++)
        {
            var page = document.AddPage(); page.Width = XUnit.FromPoint(600); page.Height = XUnit.FromPoint(800); page.Rotate = rotation;
            var font = new PdfDictionary(document); font.Elements.SetName("/Type", "/Font"); font.Elements.SetName("/Subtype", "/Type1"); font.Elements.SetName("/BaseFont", "/Helvetica");
            document.Internals.AddObject(font);
            var fonts = new PdfDictionary(document); fonts.Elements["/F1"] = font.Reference;
            var resources = new PdfDictionary(document); resources.Elements["/Font"] = fonts; page.Elements["/Resources"] = resources;
            var content = new PdfDictionary(document); content.CreateStream(System.Text.Encoding.ASCII.GetBytes($"q 1 1 1 rg 0 0 600 800 re f Q BT /F1 20 Tf 40 720 Td (InkCanvas classroom page {i + 1}) Tj ET"));
            document.Internals.AddObject(content); page.Elements["/Contents"] = content.Reference;
        }
        var note = new PdfTextAnnotation { Contents = "Existing annotation", Rectangle = new PdfRectangle(new XRect(20, 20, 30, 30)) };
        document.Pages[0].Annotations.Add(note);
        document.Outlines.Add("Chapter one", document.Pages[0], true);
        if (encrypted) { document.SecuritySettings.UserPassword = "reader"; document.SecuritySettings.OwnerPassword = "owner"; }
        document.Save(path);
    }
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void PageTransformRoundTripsCropOffsetsRotationAndUserUnits(int rotation)
    {
        var geometry = new PdfPageGeometry(20, -10, 620, 790, rotation, 2);
        foreach (var point in new[] { Vector2.Zero, new Vector2(100, 200), new Vector2(geometry.DipWidth, geometry.DipHeight) })
            Assert.True(Vector2.Distance(point, geometry.FromPdf(geometry.ToPdf(point))) < .001f);
    }
    [Theory]
    [InlineData(false, PenNib.Round)] [InlineData(true, PenNib.Ellipse45)]
    public void EmbeddedInkRoundTripsEveryStoredCoordinateWidthAndBrush(bool highlighter, PenNib nib)
    {
        var original = Stroke(highlighter: highlighter, nib: nib);
        var loaded = Assert.Single(PdfInkCodec.Decode(PdfInkCodec.Encode(new[] { original })));
        Assert.Equal(original.Id, loaded.Id); Assert.Equal(original.Points, loaded.Points); Assert.Equal(original.Attributes.BaseWidth, loaded.Attributes.BaseWidth);
        Assert.Equal(original.Attributes.Nib, loaded.Attributes.Nib); Assert.Equal(original.Attributes.HighlighterAlpha, loaded.Attributes.HighlighterAlpha);
        Assert.Equal(StrokeGeometry.BuildOutline(original), StrokeGeometry.BuildOutline(loaded));
    }
    [Fact]
    public async Task SavingPreservesOriginalPagesTextBookmarksOtherAnnotationsAndEditableInk()
    {
        string root = Path.Combine(Path.GetTempPath(), "inkcanvas-pdf-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "source.pdf"), saved = Path.Combine(root, "saved.pdf"); CreateFixture(original);
            var stroke = Stroke(); using (var session = await PdfAnnotationFile.OpenAsync(original))
            {
                Assert.Equal(3, session.Pages.Count);
                await session.SaveAsync(saved, new Dictionary<int, IReadOnlyList<InkStroke>> { [0] = new[] { stroke } });
            }
            using (var document = PdfReader.Open(saved, PdfDocumentOpenMode.Modify))
            { Assert.Equal(3, document.PageCount); Assert.Single(document.Outlines); Assert.Equal(2, document.Pages[0].Annotations.Count); Assert.Equal("PDF annotation fixture", document.Info.Title); }
            using var reopened = await PdfAnnotationFile.OpenAsync(saved); Assert.Equal(stroke.Points, Assert.Single(reopened.GetInk(0)).Points);
            Assert.Single(await reopened.SearchAsync("classroom page 2"));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task RepeatedSavesReplaceOwnAppearanceAndDeletingInkRemovesItFromPdf()
    {
        string root = Path.Combine(Path.GetTempPath(), "inkcanvas-pdf-repeat-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.pdf"), saved = Path.Combine(root, "saved.pdf"); CreateFixture(source);
            using var session = await PdfAnnotationFile.OpenAsync(source); var pages = new Dictionary<int, IReadOnlyList<InkStroke>> { [1] = new[] { Stroke() } };
            await session.SaveAsync(saved, pages); long size = new FileInfo(saved).Length;
            for (int i = 0; i < 4; i++) await session.SaveAsync(saved, pages);
            Assert.True(new FileInfo(saved).Length < size + 1024);
            using (var reopened = await PdfAnnotationFile.OpenAsync(saved)) { Assert.Single(reopened.GetInk(1)); await reopened.SaveAsync(saved, new Dictionary<int, IReadOnlyList<InkStroke>>()); }
            using var final = await PdfAnnotationFile.OpenAsync(saved); Assert.Empty(final.GetInk(1));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task InvalidPasswordAndCancelledSaveDoNotReplaceOriginalFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "inkcanvas-pdf-secure-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "source.pdf"); CreateFixture(path, encrypted: true); byte[] original = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<PdfPasswordRequiredException>(() => PdfAnnotationFile.OpenAsync(path));
            using var session = await PdfAnnotationFile.OpenAsync(path, "owner"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.SaveAsync(path, new Dictionary<int, IReadOnlyList<InkStroke>>(), cancellation.Token));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("1-3,2,5", 5, 4, "0,1,2,4")]
    [InlineData(" 2，4-5 ", 5, 3, "1,3,4")]
    public void ImportRangesDeduplicateValidateCapacityAndUseZeroBasedPages(string input, int count, int maximum, string expected)
        => Assert.Equal(expected, string.Join(",", PdfPageRange.Parse(input, count, maximum)));

    [Theory]
    [InlineData("0")][InlineData("4-2")][InlineData("1-999999999")][InlineData("1-2-3")][InlineData("")][InlineData("1,2,3,4")]
    public void InvalidImportRangesDoNotExpandOrPartiallyImport(string input)
        => Assert.Throws<FormatException>(() => PdfPageRange.Parse(input, 10, 3));

    [Fact]
    public async Task ExternalFileChangesCannotBeOverwrittenByStaleWorkspace()
    {
        string path = Path.Combine(Path.GetTempPath(), "inkcanvas-pdf-conflict-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            CreateFixture(path);
            using var file = await PdfAnnotationFile.OpenAsync(path);
            File.AppendAllText(path, "\n% Changed by another editor\n");
            byte[] updated = File.ReadAllBytes(path);
            await Assert.ThrowsAsync<IOException>(() => file.SaveAsync(path, new Dictionary<int, IReadOnlyList<InkStroke>>()));
            Assert.Equal(updated, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UserPasswordRespectsReadOnlyPermissionAndOwnerCanAnnotate()
    {
        string path = Path.Combine(Path.GetTempPath(), "inkcanvas-pdf-permissions-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using (var pdf = new PdfDocument())
            {
                pdf.AddPage(); pdf.SecuritySettings.UserPassword = "reader"; pdf.SecuritySettings.OwnerPassword = "owner";
                pdf.SecuritySettings.PermitModifyDocument = false; pdf.SecuritySettings.PermitAnnotations = false; pdf.Save(path);
            }
            using var reader = await PdfAnnotationFile.OpenAsync(path, "reader"); Assert.True(reader.ReadOnly);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.SaveAsync(path, new Dictionary<int, IReadOnlyList<InkStroke>>()));
            using var owner = await PdfAnnotationFile.OpenAsync(path, "owner"); Assert.False(owner.ReadOnly);
        }
        finally { File.Delete(path); }
    }
}
