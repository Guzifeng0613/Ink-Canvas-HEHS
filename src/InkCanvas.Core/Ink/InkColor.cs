namespace InkCanvas.Core.Ink;

/// <summary>UI-framework-independent colour. Kept here so Core has no Windows UI dependency.</summary>
public readonly record struct InkColor(byte A, byte R, byte G, byte B)
{
    public static InkColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    public static InkColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    public uint PackedArgb => (uint)(A << 24 | R << 16 | G << 8 | B);

    public static InkColor FromPackedArgb(uint v) =>
        new((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    public InkColor WithAlpha(byte a) => new(a, R, G, B);

    public static readonly InkColor Black = Opaque(0, 0, 0);
    public static readonly InkColor White = Opaque(255, 255, 255);
    public static readonly InkColor Red = Opaque(220, 38, 38);
    public static readonly InkColor Transparent = new(0, 0, 0, 0);
}