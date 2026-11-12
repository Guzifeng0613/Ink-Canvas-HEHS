using InkCanvas.Core.Ink;
using Microsoft.Graphics.Canvas;
using System.Numerics;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Draws a whole scene (committed ink, live ink, shape preview, selection) into any Win2D
/// drawing session. Shared by the transparent overlay and the WinUI 3 board surface so both
/// render identically.
/// </summary>
public sealed class InkSceneRenderer : IDisposable
{
    public StrokeRenderer Strokes { get; } = new();

    public bool ShowSelectionOutline { get; set; } = true;

    public void Draw(CanvasDrawingSession session, InkController controller)
    {
        if (controller is null) return;
        var view = controller.ViewTransform;

        foreach (var stroke in controller.Document.Strokes)
            Strokes.Draw(session, stroke, view);

        // Live strokes draw on top of the committed layer, in the same pass. Committing moves
        // a stroke from this loop to the one above between two frames, never within one.
        foreach (var stroke in controller.LiveStrokes)
            Strokes.Draw(session, stroke, view);

        foreach (var stroke in controller.ShapePreview)
            Strokes.Draw(session, stroke, view);

        if (ShowSelectionOutline && controller.Selection.Count > 0)
            DrawSelection(session, controller);
    }

    private static void DrawSelection(CanvasDrawingSession session, InkController controller)
    {
        var bounds = Rect.Empty;
        foreach (var s in controller.Selection) bounds = bounds.Union(s.Bounds);
        if (bounds.IsEmpty) return;

        Matrix3x2.Invert(controller.ViewTransform, out var inverse);
        var corners = new[]
        {
            Vector2.Transform(new Vector2(bounds.Left, bounds.Top), controller.ViewTransform),
            Vector2.Transform(new Vector2(bounds.Right, bounds.Top), controller.ViewTransform),
            Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), controller.ViewTransform),
            Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), controller.ViewTransform),
        };
        _ = inverse;

        var accent = Windows.UI.Color.FromArgb(255, 0, 120, 212);
        for (int i = 0; i < 4; i++)
            session.DrawLine(corners[i], corners[(i + 1) % 4], accent, 1.5f);
        foreach (var c in corners)
            session.FillCircle(c, 4f, accent);
    }

    /// <summary>Drop cached geometry for strokes that were mutated in place.</summary>
    public void InvalidateGeometry(IEnumerable<InkStroke> strokes)
    {
        foreach (var s in strokes) Strokes.Invalidate(s);
    }

    public void Dispose() => Strokes.Dispose();
}