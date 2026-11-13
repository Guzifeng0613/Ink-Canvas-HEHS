using InkCanvas.Core.Ink;

namespace InkCanvas.Core.Replay;

/// <summary>
/// Ink replay ("time machine"): re-reveals committed strokes in drawing order.
/// The caller advances a clock and asks for the visible prefix; nothing here mutates the
/// source document, and stored widths are preserved so replay looks like the original drawing.
/// </summary>
public sealed class InkReplaySession
{
    private readonly List<InkStroke> _source;
    private readonly List<float> _cumulative;
    private readonly float _totalMs;

    public InkReplaySession(IEnumerable<InkStroke> strokes, float msPerPoint = 12f)
    {
        _source = strokes.ToList();
        _cumulative = new List<float>();
        float acc = 0;
        foreach (var s in _source)
        {
            acc += Math.Max(1, s.Count) * msPerPoint;
            _cumulative.Add(acc);
        }
        _totalMs = acc;
    }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(_totalMs);

    public int StrokeCount => _source.Count;

    public bool IsComplete(float elapsedMs) => elapsedMs >= _totalMs;

    /// <summary>Strokes visible at <paramref name="elapsedMs"/>, the last one possibly partial.</summary>
    public List<InkStroke> VisibleAt(float elapsedMs)
    {
        var result = new List<InkStroke>();
        for (int i = 0; i < _source.Count; i++)
        {
            float start = i == 0 ? 0 : _cumulative[i - 1];
            if (elapsedMs < start) break;
            float end = _cumulative[i];
            var src = _source[i];
            if (elapsedMs >= end)
            {
                result.Add(src);
                continue;
            }

            float fraction = (elapsedMs - start) / Math.Max(1f, end - start);
            int take = Math.Max(1, (int)(src.Count * fraction));
            var partial = new InkStroke(src.Attributes.Clone());
            partial.RestorePoints(src.Points.Take(take));
            result.Add(partial);
        }
        return result;
    }
}