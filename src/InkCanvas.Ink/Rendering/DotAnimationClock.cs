using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace InkCanvas.Ink.Rendering;

/// <summary>Requests frames only while confirmed taps are settling to their final diameter.</summary>
public sealed class DotAnimationClock : IDisposable
{
    private readonly InkController _controller;
    private readonly Action _render;
    private readonly DispatcherQueueTimer _timer;
    private long _lastTick;

    public DotAnimationClock(InkController controller, Action render)
    {
        _controller = controller;
        _render = render;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) =>
        {
            long tick = Stopwatch.GetTimestamp();
            float elapsed = (float)Stopwatch.GetElapsedTime(_lastTick, tick).TotalMilliseconds;
            _lastTick = tick;
            _controller.AdvanceDotAnimations(elapsed);
        };
        _controller.DotAnimationChanged += OnChanged;
    }

    private void OnChanged()
    {
        if (_controller.DotAnimations.Count == 0) _timer.Stop();
        else if (!_timer.IsRunning)
        {
            _lastTick = Stopwatch.GetTimestamp();
            _timer.Start();
        }
        _render();
    }

    public void Dispose()
    {
        _timer.Stop();
        _controller.DotAnimationChanged -= OnChanged;
        _controller.FinishDotAnimations();
    }
}
