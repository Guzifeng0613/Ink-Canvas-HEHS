using InkCanvas.App.Services;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Shapes;
using InkCanvas.Ink;
using InkCanvas.Platform.Windows;
using System.Numerics;
using System.Text.Json;

namespace InkCanvas.App.Diagnostics;

/// <summary>
/// Opt-in runtime check (<c>--selftest</c>). Draws through the real input path and verifies,
/// in the running app, that committing a stroke does not change its geometry — the property
/// whose absence caused the original stroke-taper flicker.
/// </summary>
public static class SelfTest
{
    public static void Run(ShellController shell, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var controller = shell.Services.OverlayInk;
        var checks = new List<object>();

        // 1. Freehand tapered stroke, fed one sample at a time exactly like a digitizer.
        controller.CurrentAttributes.Color = InkColor.Opaque(220, 38, 38);
        controller.CurrentAttributes.BaseWidth = 14f;
        controller.CurrentAttributes.TaperKind = TaperKind.Tail;
        controller.Tool = InkTool.Pen;

        const uint penId = 9001;
        var path = new List<Vector2>();
        for (int i = 0; i < 70; i++)
            path.Add(new Vector2(220 + i * 9f, 300 + MathF.Sin(i * 0.16f) * 70f));

        controller.OnPointerDown(Sample(penId, path[0], 0));
        for (int i = 1; i < path.Count; i++)
            controller.OnPointerMove(Sample(penId, path[i], (uint)(i * 8)));

        var live = controller.LiveStrokes.First();
        float[] widthsBeforeCommit = live.Points.Select(p => p.Width).ToArray();
        Vector2[] outlineBeforeCommit = StrokeGeometry.BuildOutline(live);
        bool tipTaperedLive = live.Points[^1].Width < live.Points[10].Width;

        controller.OnPointerUp(Sample(penId, path[^1], (uint)(path.Count * 8)));

        var committed = controller.Document.Strokes[^1];
        float[] widthsAfterCommit = committed.Points.Select(p => p.Width).ToArray();
        Vector2[] outlineAfterCommit = StrokeGeometry.BuildOutline(committed);

        checks.Add(new
        {
            check = "committed geometry equals last live frame",
            widthsIdentical = widthsBeforeCommit.SequenceEqual(widthsAfterCommit),
            outlineIdentical = outlineBeforeCommit.SequenceEqual(outlineAfterCommit),
            taperPresentWhileDrawing = tipTaperedLive,
            tipWidth = widthsAfterCommit[^1],
            settledWidth = widthsAfterCommit[10],
        });

        // 2. Highlighter (must never taper).
        controller.Tool = InkTool.Highlighter;
        controller.CurrentAttributes.Color = InkColor.Opaque(234, 179, 8);
        controller.CurrentAttributes.BaseWidth = 34f;
        const uint hiId = 9002;
        controller.OnPointerDown(Sample(hiId, new Vector2(220, 460), 0));
        for (int i = 1; i < 50; i++)
            controller.OnPointerMove(Sample(hiId, new Vector2(220 + i * 12f, 460), (uint)(i * 8)));
        controller.OnPointerUp(Sample(hiId, new Vector2(220 + 49 * 12f, 460), 400));
        var highlighter = controller.Document.Strokes[^1];
        checks.Add(new
        {
            check = "highlighter is uniform width",
            uniform = highlighter.Points.All(p => Math.Abs(p.Width - 34f) < 0.01f),
        });

        // 3. Preset shapes through the shape tool.
        controller.Tool = InkTool.Shape;
        controller.CurrentAttributes.Color = InkColor.Opaque(37, 99, 235);
        controller.CurrentAttributes.BaseWidth = 4f;
        controller.RecognizeShapes = false; // keep the synthetic shapes verbatim
        int before = controller.Document.Count;
        controller.ShapeKind = ShapeKind.Coordinate3D;
        const uint shapeId = 9003;
        controller.OnPointerDown(Sample(shapeId, new Vector2(1150, 620), 0));
        controller.OnPointerMove(Sample(shapeId, new Vector2(1420, 400), 40));
        controller.OnPointerUp(Sample(shapeId, new Vector2(1420, 400), 80));
        checks.Add(new
        {
            check = "3D coordinate system committed as three axes",
            strokesAdded = controller.Document.Count - before,
        });

        controller.ShapeKind = ShapeKind.Circle;
        controller.OnPointerDown(Sample(shapeId, new Vector2(300, 700), 0));
        controller.OnPointerMove(Sample(shapeId, new Vector2(520, 900), 40));
        controller.OnPointerUp(Sample(shapeId, new Vector2(520, 900), 80));

        controller.Tool = InkTool.Pen;
        controller.RecognizeShapes = shell.Services.Settings.InkToShape.IsInkToShapeEnabled;

        bool allPassed = widthsBeforeCommit.SequenceEqual(widthsAfterCommit)
                      && outlineBeforeCommit.SequenceEqual(outlineAfterCommit)
                      && tipTaperedLive;

        var report = new
        {
            presentedFrames = shell.Overlay.Presenter.PresentedFrames,
            overlayTransparentCapable = true,
            strokeCount = controller.Document.Count,
            allCriticalChecksPassed = allPassed,
            checks,
        };
        File.WriteAllText(Path.Combine(outputDirectory, "selftest.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static OverlayPointer Sample(uint id, Vector2 p, uint timeMs)
        => new(id, p.X, p.Y, 0.5f, 0, 0, OverlayPointerKind.Pen, timeMs);
}