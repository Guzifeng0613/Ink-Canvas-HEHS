using System.Numerics;
using InkCanvas.Core.Ink;
using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using Microsoft.Graphics.Canvas;

namespace InkCanvas.App.Diagnostics;

internal static class PlaybackRegression
{
    public static bool Run(List<object> checks)
    {
        using var controller = new InkController { RecognizeShapes = false };
        var first = new InkStroke(new StrokeAttributes { TaperKind = TaperKind.None, BaseWidth = 4 });
        var second = new InkStroke(first.Attributes.Clone());
        for (int i = 0; i < 100; i++) { first.AddPoint(20 + i, 50); second.AddPoint(20 + i, 100); }
        first.Complete(); second.Complete(); controller.Document.AddRange(new[] { first, second });
        controller.Selection.Add(first);
        var original = first.Points.ToArray(); var view = Matrix3x2.CreateScale(1.25f); controller.ViewTransform = view;
        int history = controller.History.UndoCount, changes = 0;
        controller.Document.Changed += () => changes++;
        bool started = controller.BeginPlayback();
        bool subset = controller.Playback.Includes(first) && !controller.Playback.Includes(second) && controller.Playback.VisibleStrokes.Count == 0;
        controller.SeekPlayback(600); int mid = controller.Playback.VisibleStrokes.Sum(s => s.Count);
        controller.SeekPlayback(120); bool seek = mid > controller.Playback.VisibleStrokes.Sum(s => s.Count);
        controller.EndPlayback();
        bool preserved = first.Points.SequenceEqual(original) && ReferenceEquals(controller.Document.Strokes[0], first)
            && controller.History.UndoCount == history && changes == 0 && controller.ViewTransform == view;
        controller.BeginPlayback(); controller.Tool = InkTool.Highlighter;
        bool toolStops = controller.Playback is null;
        controller.BeginPlayback(); controller.Document.Add(new InkStroke(new StrokeAttributes()));
        bool editStops = controller.Playback is null;
        bool passed = started && subset && seek && preserved && toolStops && editStops;
        checks.Add(new { check = "in-place playback preserves source, selection, view and undo", started, subset, seek, preserved, toolStops, editStops });
        return passed;
    }

    public static bool CheckCachedRendering(List<object> checks)
    {
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, 320, 240, 96);
        using var reference = new CanvasRenderTarget(device, 320, 240, 96);
        bool passed = true;
        foreach (bool highlighter in new[] { false, true })
        {
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 12, TaperKind = TaperKind.Tail, IsHighlighter = highlighter });
            using var incremental = new StrokeRenderer();
            for (int i = 0; i < 400; i++)
            {
                stroke.AddPoint(150 + MathF.Sin(i * .08f) * 100, 120 + MathF.Sin(i * .16f) * 70);
                if (i % 8 == 0) using (var draw = target.CreateDrawingSession())
                { draw.Clear(Microsoft.UI.Colors.Transparent); incremental.Draw(draw, stroke, Matrix3x2.Identity); }
            }
            using (var draw = target.CreateDrawingSession()) { draw.Clear(Microsoft.UI.Colors.Transparent); incremental.Draw(draw, stroke, Matrix3x2.Identity); }
            var before = target.GetPixelBytes();
            stroke.Complete();
            using var fresh = new StrokeRenderer();
            using (var draw = reference.CreateDrawingSession()) { draw.Clear(Microsoft.UI.Colors.Transparent); fresh.Draw(draw, stroke, Matrix3x2.Identity); }
            var expected = reference.GetPixelBytes();
            int differing = before.Zip(expected, (a, b) => Math.Abs(a - b) > 1 ? 1 : 0).Sum();
            bool equivalent = differing == 0;
            passed &= equivalent;
            checks.Add(new { check = "cached live chunks preserve raster and highlighter opacity", highlighter, equivalent, differing });
        }
        using (var controller = new InkController())
        using (var scene = new InkSceneRenderer())
        using (var cache = new CommittedInkLayer())
        {
            var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 6 }); stroke.AddPoint(20, 20); stroke.AddPoint(180, 100); stroke.Complete();
            controller.Document.Add(stroke);
            for (int i = 0; i < 20; i++) using (var draw = target.CreateDrawingSession())
            { draw.Clear(Microsoft.UI.Colors.Transparent); cache.Draw(draw, scene, controller, 320, 240); }
            bool reused = cache.CommittedStrokeDraws == 1;
            controller.BeginPlayback();
            using (var draw = target.CreateDrawingSession()) { draw.Clear(Microsoft.UI.Colors.Transparent); cache.Draw(draw, scene, controller, 320, 240); }
            bool hidden = target.GetPixelBytes().All(b => b == 0);
            controller.EndPlayback();
            using (var draw = target.CreateDrawingSession()) { draw.Clear(Microsoft.UI.Colors.Transparent); cache.Draw(draw, scene, controller, 320, 240); }
            bool restored = target.GetPixelBytes().Any(b => b > 0);
            passed &= reused && hidden && restored;
            checks.Add(new { check = "board history cache reused and restored after playback", reused, hidden, restored });
        }
        return passed;
    }
}
