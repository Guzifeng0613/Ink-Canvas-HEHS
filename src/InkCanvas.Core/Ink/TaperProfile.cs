namespace InkCanvas.Core.Ink;

public enum TaperKind
{
    /// <summary>Uniform width.</summary>
    None = 0,

    /// <summary>Nib taper: thin at the moving tip, settling to full width behind it.</summary>
    Tail = 1,

    /// <summary>Width driven by drawing speed (fast = thinner).</summary>
    Velocity = 2,

    /// <summary>
    /// Test-build feel profile: pressure and real elapsed time drive width, combined with a
    /// distance-based head/tail nib taper. Never enters the original three-mode vectors.
    /// </summary>
    Natural = 3,
}

/// <summary>
/// Computes per-point stroke width <b>incrementally and causally</b>.
/// <para>
/// This is the fix for the stroke-taper flicker. The WPF original computed the taper in
/// <c>StrokeCollected</c> — after pen-up — and replaced the whole point collection, so the
/// tail (or, in velocity mode, the entire stroke) visibly snapped to a new width on lift.
/// </para>
/// <para>
/// Contract every implementation must honour:
/// <list type="number">
/// <item>Only indices <c>&lt;= tipIndex</c> may be read or written — never a future point.</item>
/// <item>A point's width may only ever <b>increase</b> over successive calls. It settles
/// (freezes) once the tip has moved past the profile's window.</item>
/// <item>Completing a stroke changes nothing, so the committed geometry equals the last
/// live frame exactly.</item>
/// </list>
/// </para>
/// </summary>
public abstract class TaperProfile
{
    public abstract TaperKind Kind { get; }

    /// <param name="points">All points sampled so far; widths are updated in place.</param>
    /// <param name="tipIndex">Index of the point that just arrived.</param>
    /// <param name="baseWidth">Full stroke width.</param>
    public abstract void Apply(Span<InkPoint> points, int tipIndex, float baseWidth);

    /// <summary>Monotonic width assignment: never shrink an already-drawn point.</summary>
    protected static void Settle(ref float slot, float candidate)
    {
        if (candidate > slot) slot = candidate;
    }

    public static TaperProfile Create(TaperKind kind) => kind switch
    {
        TaperKind.None => new NoneTaper(),
        TaperKind.Velocity => new VelocityTaper(),
        TaperKind.Natural => new NaturalTaper(),
        _ => new TailTaper(),
    };
}

public sealed class NoneTaper : TaperProfile
{
    public override TaperKind Kind => TaperKind.None;

    public override void Apply(Span<InkPoint> points, int tipIndex, float baseWidth)
        => Settle(ref points[tipIndex].Width, baseWidth);
}

/// <summary>
/// Nib taper over a trailing window of <see cref="TailPoints"/> samples, plus a short
/// entry taper. Because the window slides with the tip, the only pixels whose width
/// changes frame-to-frame are the ones right at the tip — which reads as a pen nib,
/// not as a flicker.
/// </summary>
public sealed class TailTaper : TaperProfile
{
    public override TaperKind Kind => TaperKind.Tail;

    /// <summary>Trailing window length in samples. The original used 10.</summary>
    public int TailPoints { get; init; } = 10;

    /// <summary>Entry taper length in samples.</summary>
    public int HeadPoints { get; init; } = 4;

    /// <summary>Width ratio at the very tip. Original pressure 0.1 against 0.5 baseline.</summary>
    public float TipRatio { get; init; } = 0.2f;

    public override void Apply(Span<InkPoint> points, int tipIndex, float baseWidth)
    {
        int n = Math.Max(2, TailPoints);
        int start = Math.Max(0, tipIndex - n + 1);
        for (int j = start; j <= tipIndex; j++)
        {
            float distance = tipIndex - j;                       // 0 at the tip
            float t = distance / (float)(n - 1);                  // 0 at tip -> 1 at window edge
            float ratio = TipRatio + (1f - TipRatio) * t;         // thin at tip, full behind it
            Settle(ref points[j].Width, baseWidth * ratio);
        }

        if (tipIndex < HeadPoints)
        {
            float t = (tipIndex + 1) / (float)(HeadPoints + 1);
            float ratio = TipRatio + (1f - TipRatio) * t;
            Settle(ref points[tipIndex].Width, baseWidth * ratio);
        }
    }
}

/// <summary>
/// Speed-driven width, reproducing the original <c>InkStyle=1</c> curve but causally.
/// The original measured spread around point <c>i</c> using <c>i-1</c> and <c>i+1</c>;
/// referencing <c>i+1</c> is what forced it to recompute the whole stroke after pen-up.
/// Here we use the two <b>already available</b> segments ending at <c>i</c>, so each
/// width is decided once, on arrival, and never revisited.
/// </summary>
public sealed class VelocityTaper : TaperProfile
{
    public override TaperKind Kind => TaperKind.Velocity;

    public override void Apply(Span<InkPoint> points, int tipIndex, float baseWidth)
    {
        float pressure = 0.5f;
        if (tipIndex >= 2)
        {
            float d1 = Dist(points[tipIndex - 2], points[tipIndex - 1]);
            float d2 = Dist(points[tipIndex - 1], points[tipIndex]);
            float speed = (d1 + d2) / 20f;   // same scale as the original GetPointSpeed

            if (speed >= 0.25f)
                pressure = (float)(0.5 - 0.3 * (Math.Min(speed, 1.5f) - 0.3) / 1.2);
            else if (speed < 0.05f)
                pressure = (float)(0.5 + 0.4 * (0.05 - speed) / 0.05);
        }

        // Original pressure baseline is 0.5, so 0.5 maps to the full base width.
        float ratio = Math.Clamp(pressure / 0.5f, 0.15f, 1.8f);
        Settle(ref points[tipIndex].Width, baseWidth * ratio);
    }

    private static float Dist(in InkPoint a, in InkPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// Test-build "first stroke feels alive" profile.
/// <para>
/// Difference from <see cref="TailTaper"/> / <see cref="VelocityTaper"/>:
/// <list type="bullet">
/// <item>Device pressure <b>does</b> modulate width (0.45× at p=0, 1.0× at p=0.5, 1.35× at p=1).</item>
/// <item>Speed uses <b>real elapsed milliseconds</b> (<see cref="InkPoint.TimeMs"/>), not a fixed
/// 20 ms assumption, so 120/240/360 Hz pens stop producing different widths for the same hand speed.</item>
/// <item>Head/tail nib taper windows are <b>distances</b> in DIP, so the nib length is the same
/// whether the stroke is drawn slowly or quickly.</item>
/// </list>
/// Causal and monotonic: every scale term for point <c>j</c> depends only on <c>j-1, j-2, j</c>,
/// and the window ratio only grows as the tip advances, so <see cref="Settle"/> still holds.
/// </para>
/// </summary>
public sealed class NaturalTaper : TaperProfile
{
    public override TaperKind Kind => TaperKind.Natural;

    /// <summary>Width ratio at the very tip.</summary>
    public float TipRatio { get; init; } = 0.28f;

    /// <summary>Width multiplier at zero pressure (p = 0).</summary>
    public float PressureMinScale { get; init; } = 0.45f;

    /// <summary>Width multiplier at full pressure (p = 1). p = 0.5 is neutral (1.0).</summary>
    public float PressureMaxScale { get; init; } = 1.35f;

    public float SlowSpeed { get; init; } = 0.15f;
    public float SlowScale { get; init; } = 1.20f;
    public float FastSpeed { get; init; } = 3.00f;
    public float FastScale { get; init; } = 0.55f;

    public float MinTotalScale { get; init; } = 0.35f;
    public float MaxTotalScale { get; init; } = 1.40f;

    public float HeadTaperWidthFactor { get; init; } = 0.8f;
    public float HeadTaperMin { get; init; } = 6f;
    public float HeadTaperMax { get; init; } = 20f;

    public float TailTaperWidthFactor { get; init; } = 1.5f;
    public float TailTaperMin { get; init; } = 12f;
    public float TailTaperMax { get; init; } = 40f;

    /// <summary>Segment duration assumed when the device reports no usable timestamps.</summary>
    public float FallbackSegmentMs { get; init; } = 10f;

    public override void Apply(Span<InkPoint> points, int tipIndex, float baseWidth)
    {
        float tailWindow = Math.Clamp(baseWidth * TailTaperWidthFactor, TailTaperMin, TailTaperMax);
        float headWindow = Math.Clamp(baseWidth * HeadTaperWidthFactor, HeadTaperMin, HeadTaperMax);

        float accumulated = 0f;
        for (int j = tipIndex; j >= 0; j--)
        {
            if (j < tipIndex)
            {
                accumulated += Dist(points[j], points[j + 1]);
                if (accumulated > tailWindow) break;
            }

            float t = Math.Clamp(accumulated / tailWindow, 0f, 1f);
            float ratio = TipRatio + (1f - TipRatio) * t;
            Settle(ref points[j].Width, baseWidth * ratio * PointScale(points, j));
        }

        // Entry nib: only while the whole stroke is still shorter than the head window.
        float arc = 0f;
        for (int j = 1; j <= tipIndex; j++)
        {
            arc += Dist(points[j - 1], points[j]);
            if (arc >= headWindow) break;
        }
        if (arc < headWindow)
        {
            float t = Math.Clamp(arc / headWindow, 0f, 1f);
            float ratio = TipRatio + (1f - TipRatio) * t;
            Settle(ref points[tipIndex].Width, baseWidth * ratio * PointScale(points, tipIndex));
        }
    }

    private float PointScale(Span<InkPoint> points, int index)
    {
        float pressure = points[index].Pressure;
        float pressureScale = pressure <= 0.5f
            ? PressureMinScale + (1f - PressureMinScale) * Math.Clamp(pressure / 0.5f, 0f, 1f)
            : 1f + (PressureMaxScale - 1f) * Math.Clamp((pressure - 0.5f) / 0.5f, 0f, 1f);

        float speed = SpeedAt(points, index);
        float speedScale;
        if (speed <= SlowSpeed)
            speedScale = SlowScale;
        else if (speed >= FastSpeed)
            speedScale = FastScale;
        else
            speedScale = SlowScale + (FastScale - SlowScale) * ((speed - SlowSpeed) / (FastSpeed - SlowSpeed));

        return Math.Clamp(pressureScale * speedScale, MinTotalScale, MaxTotalScale);
    }

    /// <summary>
    /// Average speed of the last two segments ending at <paramref name="index"/> in DIP/ms.
    /// Uses the device timestamps when present; falls back to the legacy 10 ms assumption.
    /// </summary>
    private float SpeedAt(Span<InkPoint> points, int index)
    {
        if (index < 1) return 0f;

        float d1 = Dist(points[index - 1], points[index]);
        float dt1 = DeltaMs(points[index - 1], points[index]);
        if (index < 2) return d1 / dt1;

        float d2 = Dist(points[index - 2], points[index - 1]);
        float dt2 = DeltaMs(points[index - 2], points[index - 1]);
        return (d1 + d2) / (dt1 + dt2);
    }

    private float DeltaMs(in InkPoint a, in InkPoint b)
    {
        float dt = b.TimeMs - a.TimeMs;
        if (!float.IsFinite(dt) || dt <= 0f) return FallbackSegmentMs;
        return Math.Clamp(dt, 2f, 40f);
    }

    private static float Dist(in InkPoint a, in InkPoint b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}