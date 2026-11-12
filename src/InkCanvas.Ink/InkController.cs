using InkCanvas.Core.History;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Recognition;
using InkCanvas.Core.Shapes;
using InkCanvas.Ink.Manipulation;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;
using System.Numerics;

namespace InkCanvas.Ink;

public enum InkTool { Pen, Highlighter, Eraser, Select, Shape }

/// <summary>
/// The single entry point the UI uses for ink: routes input, owns the document and history,
/// and raises <see cref="Invalidated"/> when a redraw is needed.
/// <para>
/// Commit is deliberately <b>atomic</b>: the stroke is completed, moved from the live layer
/// into the document, and recorded in history before a single invalidation is raised. There
/// is therefore no frame in which the stroke is missing from both layers or present in both,
/// and no frame in which its geometry is recomputed.
/// </para>
/// </summary>
public sealed class InkController : IDisposable
{
    private readonly Dictionary<uint, InkStroke> _live = new();
    private readonly Dictionary<uint, OverlayPointer> _touches = new();
    private readonly TwoFingerManipulator _manipulator = new();
    private readonly PalmRejector _palm = new();
    private readonly List<InkStroke> _recentForRecognition = new();
    private readonly Dictionary<uint, Vector2> _shapeDragStart = new();
    private List<InkStroke> _shapePreview = new();
    private Vector2 _lastEraserPoint;
    private bool _erasing;

    public InkDocument Document { get; } = new();

    public UndoStack History { get; } = new(200);

    public StrokeAttributes CurrentAttributes { get; } = new()
    {
        Color = InkColor.Black,
        BaseWidth = 2.5f,
        TaperKind = TaperKind.Tail,
    };

    public InkTool Tool { get; set; } = InkTool.Pen;

    public ShapeKind ShapeKind { get; set; } = ShapeKind.Line;

    public float EraserRadius { get; set; } = 16f;

    /// <summary>Palm/back-of-hand acts as an area eraser.</summary>
    public bool PalmErases { get; set; } = true;

    public bool RecognizeShapes { get; set; } = true;

    public bool MultiTouchEnabled { get; set; } = true;

    public Matrix3x2 ViewTransform { get; set; } = Matrix3x2.Identity;

    public Vector2 ViewCenter { get; set; } = new(960, 540);

    public IReadOnlyCollection<InkStroke> LiveStrokes => _live.Values;

    public IReadOnlyList<InkStroke> ShapePreview => _shapePreview;

    public List<InkStroke> Selection { get; } = new();

    public PalmRejector Palm => _palm;

    /// <summary>Raised once per logical change; the presenter redraws on this.</summary>
    public event Action Invalidated;

    public event Action SelectionChanged;

    public void ConfigureGestures(bool zoom, bool translate, bool rotate)
    {
        _manipulator.AllowZoom = zoom;
        _manipulator.AllowTranslate = translate;
        _manipulator.AllowRotation = rotate;
    }

    // ---- input ------------------------------------------------------------

    public void OnPointerDown(in OverlayPointer p)
    {
        _palm.OnDown(p);
        var world = ToWorld(p);

        if (p.Kind == OverlayPointerKind.Touch)
        {
            _touches[p.Id] = p;
            if (_touches.Count == 2 && MultiTouchEnabled)
            {
                // A second finger cancels any in-progress touch ink and starts a gesture.
                CancelTouchInk();
                var pair = _touches.Values.Take(2).ToArray();
                _manipulator.Begin(ToWorld(pair[0]), ToWorld(pair[1]));
                Invalidated?.Invoke();
                return;
            }
            if (PalmErases && _palm.ShouldErase(p))
            {
                BeginErase(world);
                return;
            }
            if (!_palm.ShouldDraw(p) && Tool is InkTool.Pen or InkTool.Highlighter) return;
        }

        switch (Tool)
        {
            case InkTool.Eraser:
                BeginErase(world);
                break;

            case InkTool.Select:
                SelectAt(world);
                break;

            case InkTool.Shape:
                _shapeDragStart[p.Id] = world;
                break;

            default:
                StartStroke(p, world);
                break;
        }
    }

    public void OnPointerMove(in OverlayPointer p)
    {
        var world = ToWorld(p);

        if (p.Kind == OverlayPointerKind.Touch && _touches.ContainsKey(p.Id))
        {
            _touches[p.Id] = p;
            if (_touches.Count >= 2 && MultiTouchEnabled)
            {
                var pair = _touches.Values.Take(2).ToArray();
                var t = ViewTransform;
                if (_manipulator.Update(ToWorld(pair[0]), ToWorld(pair[1]), ref t, ViewCenter))
                {
                    ViewTransform = t;
                    Invalidated?.Invoke();
                }
                return;
            }
        }

        if (_erasing)
        {
            EraseAlong(_lastEraserPoint, world);
            _lastEraserPoint = world;
            return;
        }

        if (Tool == InkTool.Shape && _shapeDragStart.TryGetValue(p.Id, out var start))
        {
            _shapePreview = ShapeGenerator.Generate(ShapeKind, start, world, CurrentAttributes);
            Invalidated?.Invoke();
            return;
        }

        if (_live.TryGetValue(p.Id, out var stroke))
        {
            // Widths are settled here, as the point arrives — never after pen-up.
            stroke.AddPoint(world.X, world.Y, p.Pressure, p.TimeMs);
            Invalidated?.Invoke();
        }
    }

    public void OnPointerUp(in OverlayPointer p)
    {
        _palm.OnUp(p);
        _touches.Remove(p.Id);
        if (_touches.Count < 2) _manipulator.Reset();

        var world = ToWorld(p);

        if (_erasing)
        {
            EndErase();
            return;
        }

        if (Tool == InkTool.Shape && _shapeDragStart.Remove(p.Id, out var start))
        {
            var shapes = ShapeGenerator.Generate(ShapeKind, start, world, CurrentAttributes);
            _shapePreview = new List<InkStroke>();
            if (shapes.Count > 0) CommitStrokes(shapes, "shape");
            Invalidated?.Invoke();
            return;
        }

        if (_live.Remove(p.Id, out var stroke))
        {
            if (stroke.Count >= 2) CommitStrokes(new List<InkStroke> { stroke }, "ink");
            Invalidated?.Invoke();
        }
    }

    private void StartStroke(in OverlayPointer p, Vector2 world)
    {
        var attrs = CurrentAttributes.Clone();
        attrs.IsHighlighter = Tool == InkTool.Highlighter;
        var stroke = new InkStroke(attrs);
        stroke.AddPoint(world.X, world.Y, p.Pressure, p.TimeMs);
        _live[p.Id] = stroke;
        Invalidated?.Invoke();
    }

    /// <summary>
    /// Complete + move to document + record history, then invalidate exactly once.
    /// </summary>
    private void CommitStrokes(List<InkStroke> strokes, string name)
    {
        foreach (var s in strokes) s.Complete();
        Document.AddRange(strokes);
        History.Push(new DelegateAction(name,
            () => Document.AddRange(strokes),
            () => Document.RemoveAll(strokes)));

        if (RecognizeShapes && name == "ink") TryRecognize(strokes);
        Invalidated?.Invoke();
    }

    private void TryRecognize(List<InkStroke> committed)
    {
        _recentForRecognition.AddRange(committed);
        while (_recentForRecognition.Count > 4) _recentForRecognition.RemoveAt(0);
        _recentForRecognition.RemoveAll(s => !Document.Strokes.Contains(s));
        if (_recentForRecognition.Count == 0) return;

        var result = ShapeRecognizer.Recognize(_recentForRecognition);
        if (result.Shape is RecognizedShape.None or RecognizedShape.Triangle) return;

        List<InkStroke> replacement;
        var attrs = committed[0].Attributes;
        switch (result.Shape)
        {
            case RecognizedShape.Circle:
                replacement = ShapeGenerator.Generate(ShapeKind.Circle,
                    result.Center - new Vector2(result.RadiusX, result.RadiusY),
                    result.Center + new Vector2(result.RadiusX, result.RadiusY), attrs);
                break;
            case RecognizedShape.Ellipse:
                replacement = ShapeGenerator.Generate(ShapeKind.Ellipse,
                    result.Center - new Vector2(result.RadiusX, result.RadiusY),
                    result.Center + new Vector2(result.RadiusX, result.RadiusY), attrs);
                break;
            case RecognizedShape.Line:
                replacement = ShapeGenerator.Generate(ShapeKind.Line, result.Corners[0], result.Corners[1], attrs);
                break;
            case RecognizedShape.Rectangle:
                replacement = new List<InkStroke> { ShapeGenerator.Stroke(attrs, result.Corners.Concat(new[] { result.Corners[0] }).ToArray()) };
                break;
            default:
                return;
        }

        var removed = _recentForRecognition.ToList();
        Document.RemoveAll(removed);
        Document.AddRange(replacement);
        History.Push(new DelegateAction("recognize",
            () => { Document.RemoveAll(removed); Document.AddRange(replacement); },
            () => { Document.RemoveAll(replacement); Document.AddRange(removed); }));
        _recentForRecognition.Clear();
    }

    // ---- erasing ----------------------------------------------------------

    private readonly List<InkStroke> _erasedThisGesture = new();

    private void BeginErase(Vector2 world)
    {
        _erasing = true;
        _lastEraserPoint = world;
        _erasedThisGesture.Clear();
        EraseAlong(world, world);
    }

    private void EraseAlong(Vector2 from, Vector2 to)
    {
        // Sample along the sweep so a fast wipe cannot skip strokes.
        float dist = Vector2.Distance(from, to);
        int steps = Math.Max(1, (int)(dist / MathF.Max(4f, EraserRadius * 0.5f)));
        bool any = false;
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(from, to, steps == 0 ? 0 : (float)i / steps);
            var hits = Document.EraseAt(p, EraserRadius);
            if (hits.Count > 0)
            {
                _erasedThisGesture.AddRange(hits);
                any = true;
            }
        }
        if (any) Invalidated?.Invoke();
    }

    private void EndErase()
    {
        _erasing = false;
        if (_erasedThisGesture.Count > 0)
        {
            var removed = _erasedThisGesture.ToList();
            History.Push(new DelegateAction("erase",
                () => Document.RemoveAll(removed),
                () => Document.AddRange(removed)));
            _erasedThisGesture.Clear();
        }
        Invalidated?.Invoke();
    }

    // ---- selection --------------------------------------------------------

    private void SelectAt(Vector2 world)
    {
        var hits = Document.HitTest(world, 8f);
        Selection.Clear();
        Selection.AddRange(hits);
        SelectionChanged?.Invoke();
        Invalidated?.Invoke();
    }

    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        var removed = Selection.ToList();
        Document.RemoveAll(removed);
        History.Push(new DelegateAction("delete",
            () => Document.RemoveAll(removed),
            () => Document.AddRange(removed)));
        Selection.Clear();
        SelectionChanged?.Invoke();
        Invalidated?.Invoke();
    }

    public void TransformSelection(Matrix3x2 m)
    {
        if (Selection.Count == 0) return;
        InkDocument.Transform(Selection, m);
        Invalidated?.Invoke();
    }

    // ---- commands ---------------------------------------------------------

    public void Undo()
    {
        if (History.Undo()) Invalidated?.Invoke();
    }

    public void Redo()
    {
        if (History.Redo()) Invalidated?.Invoke();
    }

    public void Clear()
    {
        if (Document.Count == 0) return;
        var removed = Document.Strokes.ToList();
        Document.Clear();
        History.Push(new DelegateAction("clear",
            () => Document.RemoveAll(removed),
            () => Document.AddRange(removed)));
        Invalidated?.Invoke();
    }

    private void CancelTouchInk()
    {
        var touchIds = _live.Keys.Where(id => _touches.ContainsKey(id)).ToList();
        foreach (var id in touchIds) _live.Remove(id);
    }

    public Vector2 ToWorld(in OverlayPointer p)
    {
        Matrix3x2.Invert(ViewTransform, out var inverse);
        return Vector2.Transform(new Vector2(p.X, p.Y), inverse);
    }

    public void Dispose()
    {
        _live.Clear();
        _touches.Clear();
        Selection.Clear();
    }
}