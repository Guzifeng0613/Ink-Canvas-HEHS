using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using InkCanvas.Core.Ink;
using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using InkCanvas.Platform.Windows;
using Microsoft.Graphics.Canvas;

namespace InkCanvas.App.Diagnostics;

internal static class WritingPerformance
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        foreach (int background in new[] { 0, 240 })
        foreach (TaperKind style in new[] { TaperKind.Natural, TaperKind.Calligraphy, TaperKind.InkWash })
        {
            using var controller = new InkController { RecognizeShapes = false };
            for (int i = 0; i < background; i++)
            {
                var s = new InkStroke(new StrokeAttributes { BaseWidth = 3, TaperKind = TaperKind.None });
                for (int j = 0; j < 100; j++) s.AddPoint(20 + j * 7, 20 + i % 30 * 20 + MathF.Sin(j * .2f) * 10);
                s.Complete(); controller.Document.Add(s);
            }
            using var scene = new InkSceneRenderer();
            using var committed = new CommittedInkLayer();
            using var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), 1280, 720, 96);
            var sample = new OverlayPointer(9900, 20, 300, .5f, 0, 0, OverlayPointerKind.Pen, 0);
            controller.CurrentAttributes.TaperKind = style;
            controller.OnPointerDown(sample);
            var frames = new List<double>();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var total = Stopwatch.StartNew();
            for (int frame = 0; frame < 120; frame++)
            {
                for (int j = 0; j < 4; j++)
                {
                    int index = frame * 4 + j;
                    controller.OnPointerMove(sample with { X = 20 + index * 2.3f, Y = 300 + MathF.Sin(index * .12f) * 80, TimeMs = (uint)(index * 4) });
                }
                var watch = Stopwatch.StartNew();
                using (var draw = target.CreateDrawingSession())
                {
                    draw.Clear(Microsoft.UI.Colors.Transparent);
                    committed.Draw(draw, scene, controller, 1280, 720); scene.DrawLive(draw, controller);
                }
                // Flush each frame so the measurement includes actual raster work.
                target.GetPixelBytes(0, 0, 1, 1);
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }
            total.Stop(); frames.Sort();
            results.Add(new { brush = style.ToString(), backgroundStrokes = background, frames = frames.Count, points = controller.LiveStrokes.Single().Count,
                totalMs = total.Elapsed.TotalMilliseconds, medianMs = frames[frames.Count / 2], p95Ms = frames[(int)(frames.Count * .95)],
                allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated, geometryBuilds = scene.Strokes.GeometryBuildCount,
                committedStrokeDraws = committed.CommittedStrokeDraws, segmentBuilds = scene.Strokes.SegmentBuildCount });
        }
        File.WriteAllText(Path.Combine(directory, "writing.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
