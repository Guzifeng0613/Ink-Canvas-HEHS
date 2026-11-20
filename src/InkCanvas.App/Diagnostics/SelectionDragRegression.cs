using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using InkCanvas.Core.Ink;
using InkCanvas.Ink;
using InkCanvas.Ink.Rendering;
using InkCanvas.Platform.Windows;
using Microsoft.Graphics.Canvas;

namespace InkCanvas.App.Diagnostics;

internal static class SelectionDragRegression
{
    public static async Task Run(List<object> checks, string directory)
    {
        foreach (string mode in new[] { "move", "clone", "control-clone" })
        {
            using var controller = new InkController { RecognizeShapes = false,
                ViewTransform = Matrix3x2.CreateScale(1.25f) * Matrix3x2.CreateTranslation(.3f, .7f) };
            var strokes = Enumerable.Range(0, 240).Select(index =>
            {
                var stroke = new InkStroke(new StrokeAttributes { BaseWidth = 3, TaperKind = TaperKind.None,
                    IsHighlighter = index % 3 == 0, Color = new InkColor(255, (byte)(index % 2 == 0 ? 220 : 30), 70, 160) });
                for (int point = 0; point < 512; point++)
                    stroke.AddPoint(80 + index % 20 * 16 + point / 8f, 90 + index / 20 * 16 + MathF.Sin(point * .13f) * 8);
                stroke.Complete();
                return stroke;
            }).ToArray();
            controller.Document.AddRange(strokes);
            controller.Tool = InkTool.Select;
            controller.Selection.AddRange(strokes.Where((_, index) => index % 2 == 0));
            controller.CloneMode = mode == "clone";
            using var window = new LayeredInkWindow(0, 0, 1600, 900) { ClickThrough = true };
            using var presenter = new OverlayInkPresenter(window, controller);
            presenter.Scene.ShowSelectionOutline = false;
            presenter.Attach();
            using var referenceScene = new InkSceneRenderer { ShowSelectionOutline = false };
            using var reference = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), window.Width, window.Height, 96);
            var source = controller.SelectionFrame.Center(controller.ViewTransform);
            var pointer = new OverlayPointer(7718, source.X, source.Y, .5f, 0, 0, OverlayPointerKind.Mouse, 0,
                Control: mode == "control-clone");
            controller.OnPointerDown(pointer);
            controller.OnPointerMove(pointer with { X = source.X + 4, Y = source.Y + 2 });
            long bytesBefore = presenter.GpuReadbackBytes, buildsBefore = presenter.Scene.Strokes.GeometryBuildCount;
            long framesBefore = presenter.PresentedFrames;
            var elapsed = new List<double>();
            int mismatches = 0;
            for (int frame = 2; frame <= 60; frame++)
            {
                var timer = Stopwatch.StartNew();
                controller.OnPointerMove(pointer with { X = source.X + frame * 4, Y = source.Y + frame * 2 });
                elapsed.Add(timer.Elapsed.TotalMilliseconds);
                if (frame is 2 or 30 or 60)
                {
                    mismatches += CompareFrame();
                    var pixels = Pixels();
                    using var bitmap = CanvasBitmap.CreateFromBytes(CanvasDevice.GetSharedDevice(), pixels, window.Width, window.Height,
                        Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
                    await bitmap.SaveAsync(Path.Combine(directory, $"selection-{mode}-{frame}.png"), CanvasBitmapFileFormat.Png);
                }
            }
            long readback = presenter.GpuReadbackBytes - bytesBefore;
            long frames = presenter.PresentedFrames - framesBefore;
            long builds = presenter.Scene.Strokes.GeometryBuildCount - buildsBefore;
            long fullFrameReadback = (long)window.Width * window.Height * 4 * frames;
            controller.OnPointerUp(pointer with { X = source.X + 240, Y = source.Y + 120 });
            mismatches += CompareFrame();
            elapsed.Sort();
            checks.Add(new { check = "selection drag previews preserve ink stacking and reuse geometry", mode,
                passed = mismatches == 0 && builds == 0 && frames >= 59 && controller.History.UndoCount == 1 && readback < fullFrameReadback / 2,
                mismatches, geometryBuilds = builds, frames, medianMs = elapsed[elapsed.Count / 2],
                p95Ms = elapsed[(int)(elapsed.Count * .95)], gpuReadbackBytes = readback,
                fullFrameReadbackBytes = fullFrameReadback,
                regionReadback = readback < fullFrameReadback / 2 });

            byte[] Pixels()
            {
                var pixels = new byte[window.Width * window.Height * 4];
                Marshal.Copy(window.PixelBuffer, pixels, 0, pixels.Length);
                return pixels;
            }

            int CompareFrame()
            {
                using (var draw = reference.CreateDrawingSession())
                {
                    draw.Clear(Windows.UI.Color.FromArgb(1, 1, 1, 1));
                    var transform = controller.ViewTransform * Matrix3x2.CreateScale(window.DpiScale);
                    referenceScene.DrawCommitted(draw, controller, transform);
                    referenceScene.DrawLive(draw, controller, transform);
                }
                var actual = Pixels(); var expected = reference.GetPixelBytes();
                int outsideEdge = 0;
                for (int index = 0; index < actual.Length; index++)
                {
                    if (Math.Abs(actual[index] - expected[index]) <= 3) continue;
                    int x = index / 4 % window.Width, y = index / 4 / window.Width, channel = index % 4;
                    int low = 255, high = 0;
                    for (int yy = Math.Max(0, y - 1); yy <= Math.Min(window.Height - 1, y + 1); yy++)
                    for (int xx = Math.Max(0, x - 1); xx <= Math.Min(window.Width - 1, x + 1); xx++)
                    {
                        int value = expected[(yy * window.Width + xx) * 4 + channel];
                        low = Math.Min(low, value); high = Math.Max(high, value);
                    }
                    if (actual[index] < low - 3 || actual[index] > high + 3) outsideEdge++;
                }
                return outsideEdge;
            }
        }
    }
}
