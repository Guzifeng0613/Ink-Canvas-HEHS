using InkCanvas.Core.History;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Input;
using InkCanvas.Core.Recognition;
using InkCanvas.Core.Selection;
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

    /// <summary>1:1 port of the original gesture pivot option. 未验证。</summary>
    public Core.Settings.MatrixTransformCenterPointOptions TransformCenterOption { get; set; }

    private Vector2 ResolvePivot(Vector2 gestureCenter)
        => MatrixTransformCenter.Resolve(TransformCenterOption, ViewCenter, gestureCenter,
            SelectionBounds.Center, isLoaded: true);

    public IReadOnlyCollection<InkStroke> LiveStrokes => _live.Values;

    public IReadOnlyList<InkStroke> ShapePreview => _shapePreview;

    public List<InkStroke> Selection { get; } = new();

    /// <summary>When true, two-finger gestures transform the selection instead of the view.</summary>
    public bool GesturesTargetSelection { get; set; }

    /// <summary>World-space bounds of the current selection, empty when nothing is selected.</summary>
    public Rect SelectionBounds
    {
        get
        {
            var r = Rect.Empty;
            foreach (var s in Selection) r = r.Union(s.Bounds);
            return r;
        }
    }

    // Selection drag state. Transforms are computed absolutely from a snapshot taken at
    // drag start, so a long drag never accumulates rounding error.
    private SelectionHandle _activeHandle = SelectionHandle.None;
    private Vector2 _dragFromView;
    private Rect _dragStartViewBounds;
    private Matrix3x2 _viewAtDragStart;
    private List<InkStroke> _dragSnapshot;

    // Marquee (rubber-band) selection state. Pure view/world geometry; no UI dependency.
    private bool _marqueeing;
    private Vector2 _marqueeStartView, _marqueeStartWorld, _marqueeCurrentView;

    /// <summary>Current marquee rectangle in view coordinates while dragging, otherwise null.</summary>
    public Rect? MarqueeViewRect
    {
        get
        {
            if (!_marqueeing) return null;
            var a = _marqueeStartView;
            var b = _marqueeCurrentView;
            return Rect.FromLTRB(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y),
                MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        }
    }

    public PalmRejector Palm => _palm;

    /// <summary>Raised once per logical change; the presenter redraws on this.</summary>
    public event Action Invalidated;

    public event Action SelectionChanged;

    /// <summary>一支笔画刚被提交到文档（用于板书录制）。</summary>
    public event Action<InkStroke> StrokeCommitted;

    /// <summary>整笔擦除移除的笔画（用于板书录制）。</summary>
    public event Action<IReadOnlyList<InkStroke>> StrokesErased;

    /// <summary>当前页被清空（用于板书录制）。</summary>
    public event Action Cleared;

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

            // 1:1 special-screen classification (QuadIR / TouchMultiplier / nib-vs-finger bounds).
            // 未验证：按要求跳过行为验证。
            if (_palm.Advanced is not null)
            {
                var decision = _palm.ClassifyContact(p);
                switch (decision.Intent)
                {
                    case TouchIntent.Ignore:
                        return;
                    case TouchIntent.GestureOnly:
                        _slideGestureStartX = p.X;
                        _slideGestureActive = true;
                        return;
                    case TouchIntent.EraseByPoint when PalmErases:
                        _gestureEraserRadius = (float)(decision.EraserDiameter / 2.0);
                        BeginErase(world);
                        return;
                    case TouchIntent.EraseByStroke when PalmErases:
                        _gestureEraserRadius = 2.5f;
                        BeginErase(world);
                        return;
                }
            }

            if (!_palm.ShouldDraw(p) && Tool is InkTool.Pen or InkTool.Highlighter) return;
        }

        switch (Tool)
        {
            case InkTool.Eraser:
                BeginErase(world);
                break;

            case InkTool.Select:
                BeginSelectionInteraction(p, world);
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
                if (GesturesTargetSelection && Selection.Count > 0)
                {
                    // Manipulate the selection rather than the whole view.
                    var identity = Matrix3x2.Identity;
                    if (_manipulator.Update(ToWorld(pair[0]), ToWorld(pair[1]), ref identity, SelectionBounds.Center))
                    {
                        TransformSelectionByGesture(identity);
                        Invalidated?.Invoke();
                    }
                    return;
                }

                var t = ViewTransform;
                var a = ToWorld(pair[0]);
                var b = ToWorld(pair[1]);
                if (_manipulator.Update(a, b, ref t, ResolvePivot((a + b) * 0.5f)))
                {
                    ViewTransform = t;
                    Invalidated?.Invoke();
                }
                return;
            }
        }

        if (_marqueeing)
        {
            _marqueeCurrentView = new Vector2(p.X, p.Y);
            Invalidated?.Invoke();
            return;
        }

        if (_activeHandle != SelectionHandle.None)
        {
            UpdateSelectionDrag(new Vector2(p.X, p.Y));
            return;
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

        // Finger slide control in a slideshow (original GestureOnly mode). 未验证。
        if (_slideGestureActive)
        {
            _slideGestureActive = false;
            float dx = p.X - _slideGestureStartX;
            if (MathF.Abs(dx) > 60f) SlideFlipRequested?.Invoke(dx < 0);
            return;
        }

        if (_marqueeing)
        {
            EndMarquee(world);
            return;
        }

        if (_activeHandle != SelectionHandle.None)
        {
            EndSelectionDrag();
            return;
        }

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

        foreach (var s in strokes) StrokeCommitted?.Invoke(s);
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

    /// <summary>Per-contact eraser size from the classifier; falls back to the configured size.</summary>
    private float? _gestureEraserRadius;

    private bool _slideGestureActive;
    private float _slideGestureStartX;

    /// <summary>Raised when a finger swipe should flip slides: true = next, false = previous.</summary>
    public event Action<bool> SlideFlipRequested;

    private void BeginErase(Vector2 world)
    {
        _erasing = true;
        _lastEraserPoint = world;
        _erasedThisGesture.Clear();
        EraseAlong(world, world);
    }

    private void EraseAlong(Vector2 from, Vector2 to)
    {
        float radius = _gestureEraserRadius ?? EraserRadius;
        // Sample along the sweep so a fast wipe cannot skip strokes.
        float dist = Vector2.Distance(from, to);
        int steps = Math.Max(1, (int)(dist / MathF.Max(4f, radius * 0.5f)));
        bool any = false;
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(from, to, steps == 0 ? 0 : (float)i / steps);
            var hits = Document.EraseAt(p, radius);
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
        _gestureEraserRadius = null;
        if (_erasedThisGesture.Count > 0)
        {
            var removed = _erasedThisGesture.ToList();
            History.Push(new DelegateAction("erase",
                () => Document.RemoveAll(removed),
                () => Document.AddRange(removed)));
            StrokesErased?.Invoke(removed);
            _erasedThisGesture.Clear();
        }
        Invalidated?.Invoke();
    }

    // ---- selection --------------------------------------------------------

    /// <summary>Axis-aligned selection box in view coordinates, where the handles are drawn.</summary>
    private Rect ViewBoundsOf(Rect worldBounds)
    {
        if (worldBounds.IsEmpty) return Rect.Empty;
        var corners = new[]
        {
            new Vector2(worldBounds.Left, worldBounds.Top),
            new Vector2(worldBounds.Right, worldBounds.Top),
            new Vector2(worldBounds.Right, worldBounds.Bottom),
            new Vector2(worldBounds.Left, worldBounds.Bottom),
        };
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            var v = Vector2.Transform(c, ViewTransform);
            minX = MathF.Min(minX, v.X); minY = MathF.Min(minY, v.Y);
            maxX = MathF.Max(maxX, v.X); maxY = MathF.Max(maxY, v.Y);
        }
        return Rect.FromLTRB(minX, minY, maxX, maxY);
    }

    private void BeginSelectionInteraction(in OverlayPointer p, Vector2 world)
    {
        var viewPoint = new Vector2(p.X, p.Y);
        var handle = Selection.Count > 0
            ? SelectionGeometry.HitTest(ViewBoundsOf(SelectionBounds), viewPoint)
            : SelectionHandle.None;

        if (handle == SelectionHandle.None)
        {
            var hits = Document.HitTest(world, 8f);
            if (hits.Count == 0)
            {
                // Nothing under the cursor: start a rubber-band marquee selection.
                _marqueeing = true;
                _marqueeStartView = viewPoint;
                _marqueeStartWorld = world;
                _marqueeCurrentView = viewPoint;
                Selection.Clear();
                SelectionChanged?.Invoke();
                Invalidated?.Invoke();
                return;
            }

            Selection.Clear();
            Selection.AddRange(hits);
            SelectionChanged?.Invoke();
            // Tapping a stroke selects it and immediately allows dragging it.
            handle = SelectionHandle.Move;
        }

        _activeHandle = handle;
        _dragFromView = viewPoint;
        _viewAtDragStart = ViewTransform;
        _dragStartViewBounds = ViewBoundsOf(SelectionBounds);
        _dragSnapshot = Selection.Select(s => s.Clone()).ToList();
        Invalidated?.Invoke();
    }

    private void EndMarquee(Vector2 endWorld)
    {
        _marqueeing = false;

        var a = _marqueeStartView;
        var b = _marqueeCurrentView;
        float vLeft = MathF.Min(a.X, b.X), vTop = MathF.Min(a.Y, b.Y);
        float vRight = MathF.Max(a.X, b.X), vBottom = MathF.Max(a.Y, b.Y);
        var viewRect = Rect.FromLTRB(vLeft, vTop, vRight, vBottom);

        Selection.Clear();
        if (viewRect.Width >= 6 && viewRect.Height >= 6)
        {
            float wLeft = MathF.Min(_marqueeStartWorld.X, endWorld.X);
            float wTop = MathF.Min(_marqueeStartWorld.Y, endWorld.Y);
            float wRight = MathF.Max(_marqueeStartWorld.X, endWorld.X);
            float wBottom = MathF.Max(_marqueeStartWorld.Y, endWorld.Y);
            var worldRect = Rect.FromLTRB(wLeft, wTop, wRight, wBottom);
            Selection.AddRange(Document.HitTest(worldRect));
        }

        SelectionChanged?.Invoke();
        Invalidated?.Invoke();
    }

    private void UpdateSelectionDrag(Vector2 viewPoint)
    {
        if (_dragSnapshot is null || Selection.Count != _dragSnapshot.Count) return;

        var viewDelta = SelectionGeometry.DragMatrix(_activeHandle, _dragStartViewBounds,
            _dragFromView, viewPoint, keepAspect: false);

        // Conjugate the view-space drag into world space: w' = w * V * D * V^-1.
        Matrix3x2.Invert(_viewAtDragStart, out var inverse);
        var worldDelta = _viewAtDragStart * viewDelta * inverse;

        ApplySnapshot(Selection, _dragSnapshot);
        foreach (var s in Selection) s.Transform(worldDelta);
        Document.Touch();
        Invalidated?.Invoke();
    }

    private void EndSelectionDrag()
    {
        var targets = Selection.ToList();
        var before = _dragSnapshot;
        var after = Selection.Select(s => s.Clone()).ToList();
        _activeHandle = SelectionHandle.None;
        _dragSnapshot = null;

        if (before is not null && before.Count == after.Count)
        {
            History.Push(new DelegateAction("transform selection",
                () => { ApplySnapshot(targets, after); Document.Touch(); Invalidated?.Invoke(); },
                () => { ApplySnapshot(targets, before); Document.Touch(); Invalidated?.Invoke(); }));
        }
        Document.Touch();
        Invalidated?.Invoke();
    }

    private static void ApplySnapshot(List<InkStroke> targets, List<InkStroke> snapshot)
    {
        for (int i = 0; i < targets.Count && i < snapshot.Count; i++)
        {
            targets[i].RestorePoints(snapshot[i].Points);
            targets[i].Attributes.BaseWidth = snapshot[i].Attributes.BaseWidth;
        }
    }

    /// <summary>Apply a gesture matrix to the selection, recording one undoable step.</summary>
    private void TransformSelectionByGesture(Matrix3x2 worldDelta)
    {
        var targets = Selection.ToList();
        var before = targets.Select(s => s.Clone()).ToList();
        foreach (var s in targets) s.Transform(worldDelta);
        var after = targets.Select(s => s.Clone()).ToList();
        History.Push(new DelegateAction("gesture selection",
            () => { ApplySnapshot(targets, after); Document.Touch(); Invalidated?.Invoke(); },
            () => { ApplySnapshot(targets, before); Document.Touch(); Invalidated?.Invoke(); }));
        Document.Touch();
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
        Cleared?.Invoke();
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