namespace InkCanvas.Core.Ink;

/// <summary>
/// One sampled pen/touch sample.
/// <para>
/// <see cref="Width"/> is stored, not derived at draw time. That is the structural
/// guarantee behind the stroke-taper flicker fix: rendering is a pure function of
/// stored data, so the committed stroke renders exactly like the last live frame.
/// </para>
/// </summary>
public struct InkPoint
{
    public float X;
    public float Y;

    /// <summary>Raw device pressure in [0,1]; 0.5 means "no pressure information".</summary>
    public float Pressure;

    /// <summary>Final rendered width in device-independent pixels. Frozen once settled.</summary>
    public float Width;

    /// <summary>Milliseconds since stroke start.</summary>
    public float TimeMs;

    public InkPoint(float x, float y, float pressure, float width, float timeMs)
    {
        X = x; Y = y; Pressure = pressure; Width = width; TimeMs = timeMs;
    }

    public readonly System.Numerics.Vector2 Position => new(X, Y);
}