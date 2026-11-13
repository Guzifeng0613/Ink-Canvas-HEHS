using InkCanvas.Core.Ink;
using InkCanvas.Core.Selection;
using Microsoft.Graphics.Canvas;
using System.Numerics;

namespace InkCanvas.Ink.Rendering;

/// <summary>
/// Draws a whole scene (committed ink, live ink, shape preview, selection) into any Win2D
/// drawing session. Shared by the transparent overlay and the WinUI 3 board surface so both
/// render identically.
/// <para>
/// The committed/live split lets the overlay cache the expensive part and only re-read a
/// small live region from the GPU each frame.
/// </para>
/// </summary>
public sealed class InkSceneRenderer : IDisposable
{
    public StrokeRenderer Strokes { get; } = new();

    public bool ShowSelectionOutline { get; set; } = true;

    /// <summary>Everything that only changes when the document, selection or view changes.</summary>
    public void DrawCommitted(CanvasDrawingSession session, InkController controller)
    {
        if (controller is null) return;
        var view = controller.ViewTransform;
        foreach (var stroke in controller.Document.Strokes)
            Strokes.Draw(session, stroke, view);

        if (ShowSelectionOutline && controller.Selection.Count > 0)
            DrawSelection(session, controller);
    }

    /// <summary>The in-progress stroke and shape preview: the only part that moves every frame.</summary>
    /// <param name="transformOverride">
    /// Optional world-to-surface transform, used when rendering into a small region target
    /// whose origin is not the screen origin.
    /// </param>
    public void DrawLive(CanvasDrawingSession session, InkController controller, Matrix3x2? transformOverride = null)
    {
        if (controller is null) return;
        var view = transformOverride ?? controller.ViewTransform;
        foreach (var stroke in controller.LiveStrokes)
            Strokes.Draw(session, stroke, view);
        foreach (var stroke in controller.ShapePreview)
            Strokes.Draw(session, stroke, view);
    }

    public void Draw(CanvasDrawingSession session, InkController controller)
    {
        DrawCommitted(session, controller);
        DrawLive(session, controller);
    }

    private static void DrawSelection(CanvasDrawingSession session, InkController controller)
    {
        var bounds = controller.SelectionBounds;
        if (bounds.IsEmpty) return;

        var view = controller.ViewTransform;
        var corners = new[]
        {
            Vector2.Transform(new Vector2(bounds.Left, bounds.Top), view),
            Vector2.Transform(new Vector2(bounds.Right, bounds.Top), view),
            Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), view),
            Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), view),
        };

        var accent = Windows.UI.Color.FromArgb(255, 0, 120, 212);
        var fill = Windows.UI.Color.FromArgb(255, 255, 255, 255);

        for (int i = 0; i < 4; i++)
            session.DrawLine(corners[i], corners[(i + 1) % 4], accent, 1.5f);

        // Corner scale handles.
        foreach (var c in corners)
        {
            session.FillCircle(c, 6f, fill);
            session.DrawCircle(c, 6f, accent, 1.5f);
        }

        // Rotate handle, joined to the top edge so its purpose is obvious.
        var topMid = (corners[0] + corners[1]) * 0.5f;
        var rotate = Vector2.Transform(SelectionGeometry.RotateHandlePosition(bounds), view);
        session.DrawLine(topMid, rotate, accent, 1.5f);
        session.FillCircle(rotate, 7f, fill);
        session.DrawCircle(rotate, 7f, accent, 1.5f);
    }

    /// <summary>Drop cached geometry for strokes that were mutated in place.</summary>
    public void InvalidateGeometry(IEnumerable<InkStroke> strokes)
    {
        foreach (var s in strokes) Strokes.Invalidate(s);
    }

    public void Dispose() => Strokes.Dispose();
}