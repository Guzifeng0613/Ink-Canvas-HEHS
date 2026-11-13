using InkCanvas.Core.Ink;
using InkCanvas.Core.Replay;
using Xunit;

namespace InkCanvas.Core.Tests;

public class ReplayClockTests
{
    private static ReplayClock Clock(float ms = 1000f) => new(TimeSpan.FromMilliseconds(ms));

    [Fact]
    public void StartsPausedAtZero()
    {
        var c = Clock();
        Assert.False(c.IsPlaying);
        Assert.Equal(0f, c.ElapsedMs);
    }

    [Fact]
    public void AdvanceDoesNothingWhilePaused()
    {
        var c = Clock();
        c.Advance(500f);
        Assert.Equal(0f, c.ElapsedMs);
    }

    [Fact]
    public void AdvanceAccumulatesWhenPlaying()
    {
        var c = Clock();
        c.Play();
        c.Advance(100f);
        c.Advance(150f);
        Assert.Equal(250f, c.ElapsedMs, 3);
    }

    [Fact]
    public void SpeedScalesAdvance()
    {
        var c = Clock();
        c.Speed = 2f;
        c.Play();
        c.Advance(100f);
        Assert.Equal(200f, c.ElapsedMs, 3);
    }

    [Fact]
    public void StopsAtEndAndReportsComplete()
    {
        var c = Clock(300f);
        c.Play();
        c.Advance(1000f);
        Assert.Equal(300f, c.ElapsedMs, 3);
        Assert.False(c.IsPlaying);
        Assert.True(c.IsComplete);
    }

    [Fact]
    public void SeekClampsToRange()
    {
        var c = Clock(500f);
        c.Seek(-50f);
        Assert.Equal(0f, c.ElapsedMs);
        c.Seek(9999f);
        Assert.Equal(500f, c.ElapsedMs);
    }

    [Fact]
    public void ProgressIsFractionOfDuration()
    {
        var c = Clock(400f);
        c.Seek(100f);
        Assert.Equal(0.25f, c.Progress, 3);
    }

    [Fact]
    public void ToggleFlipsPlaying()
    {
        var c = Clock();
        c.Toggle();
        Assert.True(c.IsPlaying);
        c.Toggle();
        Assert.False(c.IsPlaying);
    }

    [Fact]
    public void PlayingAfterCompletionRestartsFromBeginning()
    {
        var c = Clock(200f);
        c.Play();
        c.Advance(500f);
        Assert.True(c.IsComplete);
        c.Play();
        Assert.Equal(0f, c.ElapsedMs);
        Assert.True(c.IsPlaying);
    }
}

/// <summary>Tests for the pre-existing replay session, which previously had no coverage.</summary>
public class InkReplaySessionTests
{
    private static InkStroke Stroke(int points, float width = 5f)
    {
        var s = new InkStroke(new StrokeAttributes { BaseWidth = width, TaperKind = TaperKind.None });
        for (int i = 0; i < points; i++) s.AddPoint(i * 3f, i * 2f);
        s.Complete();
        return s;
    }

    [Fact]
    public void NothingVisibleBeforeStart()
    {
        var session = new InkReplaySession(new[] { Stroke(10), Stroke(10) }, msPerPoint: 10f);
        Assert.Empty(session.VisibleAt(-1f));
    }

    [Fact]
    public void EverythingVisibleAtDuration()
    {
        var strokes = new[] { Stroke(10), Stroke(10) };
        var session = new InkReplaySession(strokes, msPerPoint: 10f);
        var visible = session.VisibleAt((float)session.Duration.TotalMilliseconds);
        Assert.Equal(2, visible.Count);
        Assert.Equal(10, visible[0].Count);
        Assert.Equal(10, visible[1].Count);
    }

    [Fact]
    public void VisibleCountNeverDecreases()
    {
        var session = new InkReplaySession(new[] { Stroke(20), Stroke(20) }, msPerPoint: 10f);
        int previous = 0;
        for (float t = 0; t <= 400f; t += 10f)
        {
            int total = session.VisibleAt(t).Sum(s => s.Count);
            Assert.True(total >= previous, $"visible point count dropped at t={t}");
            previous = total;
        }
    }

    [Fact]
    public void ReplayPreservesStoredWidths()
    {
        var session = new InkReplaySession(new[] { Stroke(10, 7f) }, msPerPoint: 10f);
        var partial = session.VisibleAt(50f).Single();
        Assert.All(partial.Points, p => Assert.Equal(7f, p.Width));
    }
}