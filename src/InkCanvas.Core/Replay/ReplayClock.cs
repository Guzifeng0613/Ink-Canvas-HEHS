namespace InkCanvas.Core.Replay;

/// <summary>
/// Playback clock for ink replay. Pure timekeeping — the UI supplies real frame deltas, which
/// keeps this testable without a dispatcher timer.
/// </summary>
public sealed class ReplayClock
{
    private readonly float _durationMs;

    public ReplayClock(TimeSpan duration)
    {
        _durationMs = MathF.Max(0f, (float)duration.TotalMilliseconds);
    }

    /// <summary>Playback rate multiplier; 1 is real time.</summary>
    public float Speed { get; set; } = 1f;

    public bool IsPlaying { get; private set; }

    public float ElapsedMs { get; private set; }

    public float DurationMs => _durationMs;

    public bool IsComplete => ElapsedMs >= _durationMs;

    public float Progress => _durationMs <= 0 ? 1f : ElapsedMs / _durationMs;

    /// <summary>Starts playback, restarting from the beginning if it had finished.</summary>
    public void Play()
    {
        if (IsComplete) ElapsedMs = 0f;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(float ms) => ElapsedMs = Math.Clamp(ms, 0f, _durationMs);

    /// <summary>Advance by a real-time delta, scaled by <see cref="Speed"/>; auto-pauses at the end.</summary>
    public void Advance(float realDeltaMs)
    {
        if (!IsPlaying) return;
        ElapsedMs = MathF.Min(_durationMs, ElapsedMs + realDeltaMs * MathF.Max(0f, Speed));
        if (ElapsedMs >= _durationMs) IsPlaying = false;
    }
}