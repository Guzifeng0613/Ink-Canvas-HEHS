using InkCanvas.Platform.Windows;

namespace InkCanvas.Platform.Input;

/// <summary>
/// Decides whether a touch contact is a palm/back-of-hand rather than a fingertip,
/// using the contact rectangle the digitizer reports.
/// <para>
/// Deliberately has <b>no visual side effect</b>. The WPF original dimmed the whole ink
/// layer to <c>Opacity = 0.1</c> while a palm was detected and restored it afterwards,
/// which produced a visible flash across all existing ink. Rejection here only affects
/// how input is routed.
/// </para>
/// </summary>
public sealed class PalmRejector
{
    /// <summary>Contact diameter (px) above which a touch is treated as a palm.</summary>
    public float PalmContactThreshold { get; set; } = 30f;

    /// <summary>A pen in range suppresses touch drawing entirely (hardware palm rejection).</summary>
    public bool PenHasPriority { get; set; } = true;

    private uint? _activePenId;
    private readonly HashSet<uint> _palms = new();

    public bool IsPenActive => _activePenId.HasValue;

    public IReadOnlyCollection<uint> ActivePalms => _palms;

    public void OnDown(in OverlayPointer p)
    {
        if (p.Kind == OverlayPointerKind.Pen) _activePenId = p.Id;
        else if (p.Kind == OverlayPointerKind.Touch && IsPalmContact(p)) _palms.Add(p.Id);
    }

    public void OnUp(in OverlayPointer p)
    {
        if (_activePenId == p.Id) _activePenId = null;
        _palms.Remove(p.Id);
    }

    public bool IsPalmContact(in OverlayPointer p)
        => MathF.Max(p.ContactWidth, p.ContactHeight) >= PalmContactThreshold;

    /// <summary>True when this contact should be allowed to draw.</summary>
    public bool ShouldDraw(in OverlayPointer p)
    {
        if (p.Kind == OverlayPointerKind.Pen) return true;
        if (_palms.Contains(p.Id)) return false;
        if (PenHasPriority && IsPenActive) return false;
        return true;
    }

    /// <summary>True when this contact should act as an area eraser (palm wipe).</summary>
    public bool ShouldErase(in OverlayPointer p) => p.Kind == OverlayPointerKind.Touch && _palms.Contains(p.Id);

    public void Reset()
    {
        _activePenId = null;
        _palms.Clear();
    }
}