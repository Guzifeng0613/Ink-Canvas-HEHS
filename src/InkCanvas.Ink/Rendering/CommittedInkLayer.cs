using System.Numerics;
using InkCanvas.Core.Ink;
using Microsoft.Graphics.Canvas;

namespace InkCanvas.Ink.Rendering;

/// <summary>Visible history tiles. Translation reuses rasterized ink, including ink entering from outside the viewport.</summary>
public sealed class CommittedInkLayer : IDisposable
{
    private sealed class Run
    {
        public bool Moving;
        public IImageSelectionElement Image;
        public readonly List<InkStroke> Strokes = new();
        public readonly List<InkCanvas.Core.Ink.Rect> Bounds = new();
        public InkCanvas.Core.Ink.Rect Coverage = InkCanvas.Core.Ink.Rect.Empty;
        public CanvasRenderTarget SmallTarget;
        public Vector2 SmallOrigin;
    }
    private sealed class Tile
    {
        public CanvasRenderTarget Target;
        public int DrawnCount;
        public long LastUsed;
    }
    private readonly List<Run> _runs = new();
    private readonly Dictionary<(int Run, int X, int Y), Tile> _tiles = new();
    private readonly List<(InkStroke Stroke, long Version, uint Color, Matrix3x2 Nib)> _snapshot = new();
    private Matrix3x2 _linear;
    private Vector2 _rasterOffset;
    private InkPlayback _playback;
    private long _dotAnimationVersion;
    private IReadOnlyList<InkStroke> _preview;
    private InkController _owner;
    private float _dpi;
    private bool _dirty = true;
    private long _frame;
    private IReadOnlyList<DrawingObject> _objects = Array.Empty<DrawingObject>();
    private long _layerVersion;
    private const int MaxTiles = 48;
    private const long MaxSmallRasterBytes = 12 * 1024 * 1024;
    private long _smallRasterBytes;
    public long CommittedStrokeDraws { get; private set; }
    public long RebuildCount { get; private set; }
    public long RasterBytes => (long)_tiles.Count * 516 * 516 * 4 + _smallRasterBytes;
    public void Invalidate() => _dirty = true;

    public void Draw(CanvasDrawingSession session, InkSceneRenderer scene, InkController controller,
        float width, float height, float dpi = 96, Matrix3x2? transformOverride = null,
        IReadOnlyList<DrawingObject> objectsOverride = null)
    {
        if (width <= 0 || height <= 0) return;
        var objects = objectsOverride ?? controller.DrawingObjects;
        if (objects.Count == 0) { Dispose(); return; }
        var view = transformOverride ?? controller.ViewTransform;
        var linear = view;
        linear.Translation = Vector2.Zero;
        float pixel = 96 / dpi;
        var rasterOffset = new Vector2(view.M31 - MathF.Floor(view.M31 / pixel) * pixel,
            view.M32 - MathF.Floor(view.M32 / pixel) * pixel);
        bool reuseGestureRaster = controller.IsViewManipulating && _owner == controller && _dpi == dpi
            && Matrix3x2.Invert(_linear, out var previousInverse) && CanReuseRaster(previousInverse * linear);
        var preview = controller.PreviewStrokes;
        bool changed = !ReferenceEquals(_owner, controller) || linear != _linear && !reuseGestureRaster || dpi != _dpi
            || !controller.IsViewManipulating && preview is null && rasterOffset != _rasterOffset
            || !ReferenceEquals(_playback, controller.Playback) || !ReferenceEquals(_preview, preview)
            || _dotAnimationVersion != controller.DotAnimationVersion || _layerVersion != controller.LayerVersion;
        bool orderChanged = !ReferenceEquals(_objects, objects) && !_objects.SequenceEqual(objects);
        if (_dirty || changed || orderChanged)
        {
            var strokes = objects.Where(item => item.Stroke is not null).Select(item => item.Stroke).ToArray();
            bool append = !changed && preview is null && _runs.Count == 1 && _runs[0].Image is null
                && objects.All(item => item.Stroke is not null) && strokes.Length > _snapshot.Count;
            for (int i = 0; append && i < _snapshot.Count; i++)
                append = ReferenceEquals(strokes[i], _snapshot[i].Stroke) && strokes[i].GeometryVersion == _snapshot[i].Version
                    && strokes[i].Attributes.EffectiveColor.PackedArgb == _snapshot[i].Color && strokes[i].Attributes.NibTransform == _snapshot[i].Nib;
            if (!append)
            {
                ClearTiles(); ClearRuns(); _linear = linear; _rasterOffset = rasterOffset; RebuildCount++;
            }
            for (int i = append ? _snapshot.Count : 0; i < objects.Count; i++)
            {
                var item = objects[i];
                if (item.Element is IImageSelectionElement { ImageData.Length: > 0 } image)
                {
                    _runs.Add(new Run { Image = image });
                    continue;
                }
                if (item.Stroke is not { } stroke) continue;
                if (controller.Playback?.Includes(stroke) == true || controller.IsAnimatingDot(stroke)) continue;
                bool moving = controller.IsPreviewStroke(stroke);
                if (_runs.Count == 0 || _runs[^1].Image is not null || _runs[^1].Moving != moving) _runs.Add(new Run { Moving = moving });
                _runs[^1].Strokes.Add(stroke);
                var bounds = TransformBounds(stroke.Bounds, _linear * Matrix3x2.CreateTranslation(_rasterOffset));
                _runs[^1].Bounds.Add(bounds);
                _runs[^1].Coverage = _runs[^1].Coverage.Union(bounds);
            }
            _snapshot.Clear();
            foreach (var stroke in strokes) _snapshot.Add((stroke, stroke.GeometryVersion, stroke.Attributes.EffectiveColor.PackedArgb, stroke.Attributes.NibTransform));
            _owner = controller; _preview = preview; _playback = controller.Playback; _dpi = dpi;
            _dotAnimationVersion = controller.DotAnimationVersion; _dirty = false;
            _objects = objects; _layerVersion = controller.LayerVersion;
        }
        _frame++;
        float side = 512 * pixel;
        for (int runIndex = 0; runIndex < _runs.Count; runIndex++)
        {
            var run = _runs[runIndex];
            if (run.Image is not null)
            {
                scene.Images.Draw(session, run.Image, view);
                continue;
            }
            var runView = run.Moving ? controller.SelectionPreviewTransform * view : view;
            var previewTransform = controller.SelectionPreviewTransform;
            bool translating = previewTransform.M11 == 1 && previewTransform.M22 == 1 && previewTransform.M12 == 0 && previewTransform.M21 == 0;
            // Drawing the runs in document order preserves translucent intersections and stacking.
            if (run.Moving && !translating)
            {
                foreach (var stroke in run.Strokes) scene.Strokes.Draw(session, stroke, runView);
                continue;
            }
            Matrix3x2.Invert(_linear * Matrix3x2.CreateTranslation(_rasterOffset), out var inverseRasterView);
            var rasterToView = linear == _linear ? Matrix3x2.CreateTranslation(runView.Translation - _rasterOffset) : inverseRasterView * runView;
            if (!Matrix3x2.Invert(rasterToView, out var inverseRaster)) continue;
            var visible = TransformBounds(new InkCanvas.Core.Ink.Rect(0, 0, width, height), inverseRaster);
            if (!run.Coverage.Intersects(visible)) continue;
            if (run.Strokes.Count <= 2 && preview is not null)
            {
                if (DrawSmallRun(session, scene, run, rasterToView, pixel, dpi)) continue;
                foreach (var stroke in run.Strokes) scene.Strokes.Draw(session, stroke, runView);
                continue;
            }
            int firstX = (int)MathF.Floor(visible.Left / side), firstY = (int)MathF.Floor(visible.Top / side);
            int lastX = (int)MathF.Ceiling(visible.Right / side), lastY = (int)MathF.Ceiling(visible.Bottom / side);
            var previousTransform = session.Transform;
            session.Transform = rasterToView * previousTransform;
            try
            {
                for (int y = firstY; y < lastY; y++)
                for (int x = firstX; x < lastX; x++)
                {
                    var bounds = new InkCanvas.Core.Ink.Rect(x * side - 2 * pixel, y * side - 2 * pixel,
                        (x + 1) * side + 2 * pixel, (y + 1) * side + 2 * pixel);
                    if (!run.Coverage.Intersects(bounds)) continue;
                    var key = (runIndex, x, y);
                    if (!_tiles.TryGetValue(key, out var tile))
                    {
                        if (_tiles.Count >= MaxTiles)
                        {
                            var oldest = _tiles.MinBy(pair => pair.Value.LastUsed);
                            oldest.Value.Target.Dispose(); _tiles.Remove(oldest.Key);
                        }
                        tile = new Tile { Target = new CanvasRenderTarget(session, side + 4 * pixel, side + 4 * pixel, dpi) };
                        _tiles.Add(key, tile);
                    }
                    tile.LastUsed = _frame;
                    if (tile.DrawnCount < run.Strokes.Count)
                    {
                        using var draw = tile.Target.CreateDrawingSession();
                        if (tile.DrawnCount == 0) draw.Clear(Microsoft.UI.Colors.Transparent);
                        var transform = _linear * Matrix3x2.CreateTranslation(_rasterOffset + new Vector2(-x * side + 2 * pixel, -y * side + 2 * pixel));
                        for (int i = tile.DrawnCount; i < run.Strokes.Count; i++)
                        {
                            if (!run.Bounds[i].Intersects(bounds)) continue;
                            scene.Strokes.Draw(draw, run.Strokes[i], transform); CommittedStrokeDraws++;
                        }
                        tile.DrawnCount = run.Strokes.Count;
                    }
                    session.DrawImage(tile.Target, new Windows.Foundation.Rect(x * side, y * side, side, side),
                        new Windows.Foundation.Rect(2 * pixel, 2 * pixel, side, side));
                }
            }
            finally { session.Transform = previousTransform; }
        }
    }

    // Alternating selected and stationary handwriting produces many one-stroke runs.
    // Tight rasters keep those runs in document order without rebuilding their paths per frame
    // or allocating a full 512-pixel tile for each small character.
    private bool DrawSmallRun(CanvasDrawingSession session, InkSceneRenderer scene, Run run,
        Matrix3x2 rasterToView, float pixel, float dpi)
    {
        if (run.SmallTarget is null)
        {
            var origin = new Vector2(MathF.Floor(run.Coverage.Left / pixel) - 2,
                MathF.Floor(run.Coverage.Top / pixel) - 2) * pixel;
            int width = (int)MathF.Ceiling((run.Coverage.Right - origin.X) / pixel) + 2;
            int height = (int)MathF.Ceiling((run.Coverage.Bottom - origin.Y) / pixel) + 2;
            long bytes = (long)width * height * 4;
            if (width <= 0 || height <= 0 || width > 512 || height > 512 || _smallRasterBytes + bytes > MaxSmallRasterBytes) return false;
            run.SmallTarget = new CanvasRenderTarget(session, width * pixel, height * pixel, dpi);
            run.SmallOrigin = origin;
            _smallRasterBytes += bytes;
            using var draw = run.SmallTarget.CreateDrawingSession();
            draw.Clear(Microsoft.UI.Colors.Transparent);
            var transform = _linear * Matrix3x2.CreateTranslation(_rasterOffset - origin);
            foreach (var stroke in run.Strokes)
            {
                scene.Strokes.Draw(draw, stroke, transform);
                CommittedStrokeDraws++;
            }
        }
        var previous = session.Transform;
        try
        {
            session.Transform = rasterToView * previous;
            session.DrawImage(run.SmallTarget, run.SmallOrigin);
        }
        finally { session.Transform = previous; }
        return true;
    }

    private static bool CanReuseRaster(Matrix3x2 delta)
    {
        float scale = MathF.Sqrt(MathF.Abs(delta.GetDeterminant()));
        return scale is >= .75f and <= 1.33f && MathF.Abs(MathF.Atan2(delta.M12, delta.M11)) <= .2f;
    }

    public static InkCanvas.Core.Ink.Rect TransformBounds(InkCanvas.Core.Ink.Rect bounds, Matrix3x2 transform)
    {
        if (bounds.IsEmpty) return bounds;
        var center = Vector2.Transform(bounds.Center, transform);
        float x = (MathF.Abs(transform.M11) * bounds.Width + MathF.Abs(transform.M21) * bounds.Height) / 2;
        float y = (MathF.Abs(transform.M12) * bounds.Width + MathF.Abs(transform.M22) * bounds.Height) / 2;
        return InkCanvas.Core.Ink.Rect.FromLTRB(center.X - x, center.Y - y, center.X + x, center.Y + y);
    }
    private void ClearTiles()
    {
        foreach (var tile in _tiles.Values) tile.Target.Dispose();
        _tiles.Clear();
    }
    private void ClearRuns()
    {
        foreach (var run in _runs) run.SmallTarget?.Dispose();
        _runs.Clear(); _smallRasterBytes = 0;
    }
    public void Dispose()
    {
        ClearTiles(); ClearRuns(); _snapshot.Clear(); _objects = Array.Empty<DrawingObject>();
        _owner = null; _preview = null; _playback = null; _dirty = true;
    }
}
