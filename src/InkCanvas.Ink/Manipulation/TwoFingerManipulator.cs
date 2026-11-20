using System.Numerics;

namespace InkCanvas.Ink.Manipulation;

/// <summary>
/// Two-finger pan / pinch-zoom / rotate, derived from the movement of a contact pair.
/// Pure math so it can be unit-tested without a digitizer.
/// </summary>
public sealed class TwoFingerManipulator
{
    private Vector2 _prevA, _prevB;
    private bool _hasPrevious;

    public bool AllowZoom { get; set; } = true;
    public bool AllowTranslate { get; set; } = true;
    public bool AllowRotation { get; set; }

    public float MinScale { get; set; } = 0.2f;
    public float MaxScale { get; set; } = 8f;

    /// <summary>
    /// Fraction of logarithmic zoom applied immediately. 1 = legacy 1:1 tracking.
    /// Reversing or subdividing the same pinch must not introduce scale drift.
    /// </summary>
    public float ZoomDamping { get; set; } = 1f;

    /// <summary>Contact-length dead zone in DIP below which zoom/rotate are ignored.</summary>
    public float ZoomDeadZone { get; set; } = 1f;

    public void Begin(Vector2 a, Vector2 b)
    {
        _prevA = a; _prevB = b; _hasPrevious = true;
    }

    public void Reset() => _hasPrevious = false;

    /// <summary>
    /// Accumulates the delta from the previous pair position into <paramref name="transform"/>.
    /// Returns false when there is nothing to apply.
    /// </summary>
    public bool Update(Vector2 a, Vector2 b, ref Matrix3x2 transform, Vector2 center, bool screenSpace = false)
    {
        if (!_hasPrevious)
        {
            Begin(a, b);
            return false;
        }

        var prevMid = (_prevA + _prevB) * 0.5f;
        var mid = (a + b) * 0.5f;
        var prevVec = _prevB - _prevA;
        var vec = b - a;

        float prevLen = prevVec.Length();
        float len = vec.Length();

        var delta = Matrix3x2.Identity;

        if (AllowTranslate)
            delta *= Matrix3x2.CreateTranslation(mid - prevMid);

        if (AllowZoom && prevLen > ZoomDeadZone && len > ZoomDeadZone)
        {
            float scale = len / prevLen;
            scale = MathF.Pow(scale, Math.Clamp(ZoomDamping, 0.05f, 1f));
            float currentScale = MathF.Sqrt(MathF.Abs(transform.M11 * transform.M22 - transform.M12 * transform.M21));
            scale = Math.Clamp(scale, MinScale / MathF.Max(currentScale, 1e-3f), MaxScale / MathF.Max(currentScale, 1e-3f));
            delta *= Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(center);
        }

        if (AllowRotation && prevLen > ZoomDeadZone && len > ZoomDeadZone)
        {
            float angle = MathF.Atan2(vec.Y, vec.X) - MathF.Atan2(prevVec.Y, prevVec.X);
            while (angle > MathF.PI) angle -= 2 * MathF.PI;
            while (angle < -MathF.PI) angle += 2 * MathF.PI;
            delta *= Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(center);
        }

        _prevA = a; _prevB = b;

        if (delta == Matrix3x2.Identity) return false;
        transform = screenSpace ? transform * delta : delta * transform;
        return true;
    }
}
