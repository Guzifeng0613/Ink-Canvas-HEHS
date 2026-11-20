using System.Numerics;
using System.Text.Json;
using InkCanvas.Core.History;
using InkCanvas.Core.Ink;
using InkCanvas.Core.ScreenTracking;
using InkCanvas.Core.Settings;
using InkCanvas.Features.Board;
using InkCanvas.Features.Tools;
using InkCanvas.Ink;
using InkCanvas.Platform.Windows;
using Xunit;

namespace InkCanvas.Core.Tests;

public sealed class ScreenTrackingTests
{
    private static ScreenViewSnapshot View(string key = "A", float scroll = 0, float zoom = 1) => new()
    {
        Application = TrackedApplication.Diagnostic, WindowHandle = 42, ContextKey = key,
        Viewport = new Rect(100, 200, 1100, 1000), Scale = zoom, ScrollPixels = new Vector2(0, scroll),
        HasScrollMetrics = true, HasZoomMetrics = true,
    };
    private static ExperimentalSettings Options() => new() { ScreenContentTracking = true, PreserveUnchangedRegions = false };
    private static InkStroke Stroke(float x = 200, float y = 300)
    {
        var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 10, TaperKind = TaperKind.None });
        stroke.AddPoint(x, y); stroke.AddPoint(x + 20, y + 5); stroke.Complete(); return stroke;
    }
    private static void Record(InkController controller, InkStroke stroke)
    {
        controller.Document.Add(stroke);
        controller.History.Push(new DelegateAction("ink", () => controller.Document.Add(stroke), () => controller.Document.Remove(stroke)));
    }
    private static void Near(Vector2 expected, Vector2 actual) => Assert.True(Vector2.Distance(expected, actual) < .01f, $"Expected {expected}, actual {actual}");

    [Fact]
    public void LegacySettingsKeepExperimentDisabledAndRoundTripIndependentOptions()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"canvas\":{\"InkWidth\":4}}");
        Assert.False(old.Experimental.ScreenContentTracking);
        old.Experimental.ScreenContentTracking = true; old.Experimental.FollowZoom = false;
        var saved = old.Clone(); Assert.True(saved.Experimental.ScreenContentTracking); Assert.False(saved.Experimental.FollowZoom);
        saved.ResetToRecommendation(); Assert.False(saved.Experimental.ScreenContentTracking);
        Assert.True(saved.Experimental.RememberPages); Assert.True(saved.Experimental.PreserveUnchangedRegions);
    }

    [Fact]
    public void PagesRestoreObjectIdentityImagesAndUndoRedo()
    {
        using var controller = new InkController(); var images = new BoardService(controller);
        var memory = new ScreenAnnotationMemory(controller, images); var options = Options();
        var a = Stroke(); Record(controller, a);
        var image = new BoardImageAsset { Data = new byte[] { 1 }, Width = 40, Height = 30 }; images.AddImage(image);
        Assert.True(memory.Apply(View(), options, Vector2.Zero, 1));
        Assert.True(memory.Apply(View("B"), options, Vector2.Zero, 1)); Assert.Empty(controller.Document.Strokes); Assert.Empty(images.CurrentImages);
        var b = Stroke(350); Record(controller, b);
        memory.Apply(View(), options, Vector2.Zero, 1);
        Assert.Same(a, Assert.Single(controller.Document.Strokes)); Assert.Same(image, Assert.Single(images.CurrentImages));
        controller.Undo(); Assert.Empty(images.CurrentImages); controller.Undo(); Assert.Empty(controller.Document.Strokes);
        memory.Apply(View("B"), options, Vector2.Zero, 1); Assert.Same(b, Assert.Single(controller.Document.Strokes));
        controller.Undo(); Assert.Empty(controller.Document.Strokes);
        memory.Apply(View(), options, Vector2.Zero, 1); controller.Redo(); Assert.Same(a, Assert.Single(controller.Document.Strokes));
        controller.Redo(); Assert.Same(image, Assert.Single(images.CurrentImages)); Assert.Equal(2, memory.RememberedPages);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(-1920, 80, 1.5f)]
    [InlineData(1920, -200, 2)]
    public void ScrollZoomAndWindowMoveFollowPhysicalPixelsWithoutChangingGeometry(float ox, float oy, float dpi)
    {
        using var controller = new InkController(); var images = new BoardService(controller);
        var memory = new ScreenAnnotationMemory(controller, images); var options = Options();
        var stroke = Stroke(); controller.Document.Add(stroke); var initial = stroke.Points.ToArray();
        var origin = new Vector2(ox, oy); var anchor = View(); memory.Apply(anchor, options, origin, dpi);
        var point = new Vector2(200, 300);
        var physical = point * dpi + origin;
        var content = physical - new Vector2(anchor.Viewport.Left, anchor.Viewport.Top);
        var current = View(scroll: 350, zoom: 1.5f) with { Viewport = new Rect(130, 240, 1130, 1040) };
        memory.Apply(current, options, origin, dpi);
        Near((Vector2.Transform(content, current.ContentToScreen()) - origin) / dpi, Vector2.Transform(point, controller.ViewTransform));
        Assert.Equal(initial, stroke.Points); Assert.Equal(10, stroke.Attributes.BaseWidth);
        memory.Apply(anchor, options, origin, dpi); Near(point, Vector2.Transform(point, controller.ViewTransform));
    }

    [Fact]
    public void ManualViewAdjustmentBecomesNewBaselineAndReanchorPreservesDisplayedPosition()
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller)); var options = Options();
        memory.Apply(View(), options, Vector2.Zero, 1); memory.Apply(View(scroll: 100), options, Vector2.Zero, 1);
        controller.ViewTransform *= Matrix3x2.CreateTranslation(60, 30);
        memory.Apply(View(scroll: 150), options, Vector2.Zero, 1); Near(new Vector2(60, -120), Vector2.Transform(Vector2.Zero, controller.ViewTransform));
        var before = controller.ViewTransform; memory.Reanchor(View(scroll: 700)); memory.Apply(View(scroll: 700), options, Vector2.Zero, 1);
        Assert.Equal(before, controller.ViewTransform);
        memory.Apply(View(scroll: 720), options, Vector2.Zero, 1); Near(new Vector2(60, -140), Vector2.Transform(Vector2.Zero, controller.ViewTransform));
    }

    [Theory]
    [InlineData(false, false, 1, 0)]
    [InlineData(true, false, 1, -200)]
    [InlineData(false, true, 2, -200)]
    public void FollowOptionsAreIndependent(bool scroll, bool zoom, float expectedScale, float expectedY)
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller));
        var options = Options(); options.FollowScroll = scroll; options.FollowZoom = zoom;
        memory.Apply(View(), options, Vector2.Zero, 1); memory.Apply(View(scroll: 200, zoom: 2), options, Vector2.Zero, 1);
        Assert.Equal(expectedScale, controller.ViewTransform.M11); Assert.Equal(expectedY, controller.ViewTransform.M32);
    }

    [Fact]
    public void ActivePointerDefersPageSwitchAndUnsupportedTargetRestoresIndependentDesktop()
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller)); var options = Options();
        memory.Apply(View(), options, Vector2.Zero, 1);
        var pointer = new OverlayPointer(4, 300, 300, .5f, 10, 0, OverlayPointerKind.Mouse, 1);
        controller.OnPointerDown(pointer); Assert.False(memory.Apply(View("B"), options, Vector2.Zero, 1)); Assert.False(memory.Leave()); Assert.Equal("A", memory.CurrentKey);
        controller.OnPointerUp(pointer); Assert.True(memory.Apply(View("B"), options, Vector2.Zero, 1)); Assert.Empty(controller.Document.Strokes);
        Assert.True(memory.Leave()); var desktop = Stroke(500); Record(controller, desktop);
        memory.Apply(View(), options, Vector2.Zero, 1); Assert.Single(controller.Document.Strokes); Assert.DoesNotContain(desktop, controller.Document.Strokes);
        memory.Leave(); Assert.Same(desktop, Assert.Single(controller.Document.Strokes));
    }

    [Fact]
    public void DisablingPageMemoryKeepsSingleWorkingSetAndInvalidSnapshotsDoNothing()
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller)); var options = Options();
        options.RememberPages = false; var stroke = Stroke(); controller.Document.Add(stroke);
        memory.Apply(View(), options, Vector2.Zero, 1); memory.Apply(View("B"), options, Vector2.Zero, 1);
        Assert.Same(stroke, Assert.Single(controller.Document.Strokes)); Assert.Equal(1, memory.RememberedPages);
        Assert.False(memory.Apply(View() with { Scale = float.NaN }, options, Vector2.Zero, 1)); Assert.Same(stroke, Assert.Single(controller.Document.Strokes));
    }

    [Theory]
    [InlineData(800, 25, 50, 1200)]
    [InlineData(800, 100, -1, 0)]
    [InlineData(800, 0, 20, 0)]
    [InlineData(800, 50, 100, 800)]
    public void ScrollPatternPercentagesMapToPhysicalContentOffset(float viewport, double view, double scroll, float expected) =>
        Assert.Equal(expected, ScreenViewSnapshot.ScrollOffset(viewport, view, scroll));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PixelClipHandlesViewportMasksAndDirtyTileOrigin(byte background)
    {
        var clip = new ScreenInkClip(new Rect(11.2f, 21.2f, 16.9f, 26.9f), new[] { new Rect(13.2f, 22.1f, 14.2f, 23.1f) });
        byte[] pixels = Enumerable.Repeat((byte)200, 8 * 8 * 4).ToArray(); clip.Apply(pixels, 8, 8, 10, 20, background);
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
        {
            bool visible = x >= 2 && x < 6 && y >= 2 && y < 6 && !(x >= 3 && x < 5 && y >= 2 && y < 4);
            Assert.All(pixels.Skip((y * 8 + x) * 4).Take(4), value => Assert.Equal(visible ? (byte)200 : background, value));
        }
        Assert.True(clip.Contains(new Vector2(15, 25))); Assert.False(clip.Contains(new Vector2(13.5f, 22.5f))); Assert.False(clip.Contains(Vector2.Zero));
        clip = new ScreenInkClip(new Rect(-10, -10, -2, -2), Array.Empty<Rect>()); clip.Apply(pixels, 8, 8, background: background);
        Assert.All(pixels, value => Assert.Equal(background, value));
    }

    [Fact]
    public void FixedRegionsRequireConfirmedScrollAndDisappearWhenRemoved()
    {
        var detector = new FixedScreenRegions(); var header = new ScreenRegionAnchor("header", new Rect(100, 200, 1100, 280));
        var a = View() with { Anchors = new[] { header } };
        Assert.Empty(detector.Update(null, a)); Assert.Empty(detector.Update(a, a));
        var b = a with { ScrollPixels = new Vector2(0, 100) }; Assert.Single(detector.Update(a, b));
        Assert.Empty(detector.Update(b, View("B"))); Assert.Empty(detector.Update(a, a with { Scale = 2 }));
        detector.Update(a, b); Assert.Empty(detector.Update(b, b with { Anchors = Array.Empty<ScreenRegionAnchor>() }));
    }

    [Fact]
    public void SameUrlVariantsIgnoreCountersAndScrollingAndRestoreEarlierContent()
    {
        var variants = new ContentPageVariants();
        string a = string.Concat(Enumerable.Range(1, 40).Select(i => $"语文课堂讨论章节{i}，学习古诗中的景物描写。"));
        string b = string.Concat(Enumerable.Range(1, 40).Select(i => $"数学应用题第{i}节，计算三角函数和立体几何的面积。"));
        string key = variants.Resolve("url", a, false); Assert.Equal(key, variants.Resolve("url", a + "访问人数100", false));
        for (int i = 0; i < 3; i++) Assert.Equal(key, variants.Resolve("url", b, true));
        Assert.Equal(key, variants.Resolve("url", b, false)); string other = variants.Resolve("url", b, false); Assert.NotEqual(key, other);
        variants.Resolve("url", a, false); Assert.Equal(key, variants.Resolve("url", a, false));
        Assert.Equal(key, variants.Resolve("url", "", false));
    }

    [Fact]
    public void MatchingParagraphRetainsInkButChangedRegionUsesSeparatePagesAndDeletionDoesNotResurrect()
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller));
        var options = Options(); options.PreserveUnchangedRegions = true;
        var paragraph = new ScreenRegionAnchor("same text", new Rect(100, 250, 600, 400));
        var a = View() with { StableContentRegions = new[] { paragraph } };
        var b = View("B") with { StableContentRegions = new[] { paragraph with { Bounds = new Rect(100, 450, 600, 600) } } };
        var shared = Stroke(); var specific = Stroke(700, 600); Record(controller, shared); Record(controller, specific);
        memory.Apply(a, options, Vector2.Zero, 1); memory.Apply(b, options, Vector2.Zero, 1);
        var copy = Assert.Single(controller.Document.Strokes); Assert.Equal(shared.Id, copy.Id); Assert.Equal(shared.Points[0].Y + 200, copy.Points[0].Y);
        Assert.False(controller.History.CanUndo);
        controller.Document.Remove(copy); memory.Apply(a, options, Vector2.Zero, 1);
        Assert.Same(specific, Assert.Single(controller.Document.Strokes));
        memory.Apply(b, options, Vector2.Zero, 1); Assert.Empty(controller.Document.Strokes);
    }

    [Fact]
    public void RepeatedOrDistortedParagraphsDoNotTransferInk()
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller));
        var options = Options(); options.PreserveUnchangedRegions = true;
        var paragraph = new ScreenRegionAnchor("duplicate", new Rect(100, 250, 600, 400));
        memory.Apply(View() with { StableContentRegions = new[] { paragraph, paragraph } }, options, Vector2.Zero, 1); controller.Document.Add(Stroke());
        memory.Apply(View("B") with { StableContentRegions = new[] { paragraph } }, options, Vector2.Zero, 1); Assert.Empty(controller.Document.Strokes);
        controller.Document.Add(Stroke()); memory.Apply(View("C") with { StableContentRegions = new[] { paragraph with { Bounds = new Rect(100, 250, 900, 400) } } }, options, Vector2.Zero, 1);
        Assert.Empty(controller.Document.Strokes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedDeletionSurvivesAnIntermediatePageWithoutThatParagraph(bool throughDesktop)
    {
        using var controller = new InkController(); var memory = new ScreenAnnotationMemory(controller, new BoardService(controller));
        var options = Options(); options.PreserveUnchangedRegions = true;
        var regions = new[] { new ScreenRegionAnchor("persistent paragraph", new Rect(100, 250, 600, 400)) };
        var a = View() with { StableContentRegions = regions }; var b = View("B") with { StableContentRegions = regions };
        memory.Apply(a, options, Vector2.Zero, 1); controller.Document.Add(Stroke()); memory.Apply(b, options, Vector2.Zero, 1);
        controller.Document.Clear();
        if (throughDesktop) memory.Leave(); else memory.Apply(View("C"), options, Vector2.Zero, 1);
        memory.Apply(a, options, Vector2.Zero, 1);
        Assert.Empty(controller.Document.Strokes);
    }
}
