namespace InkCanvas.Core.Ink;

public sealed class StrokeAttributes
{
    public InkColor Color { get; set; } = InkColor.Black;

    /// <summary>Full stroke width in device-independent pixels.</summary>
    public float BaseWidth { get; set; } = 3f;

    public bool IsHighlighter { get; set; }

    public TaperKind TaperKind { get; set; } = TaperKind.Tail;

    /// <summary>Highlighter ink is laid down translucent and never tapered.</summary>
    public TaperKind EffectiveTaper => IsHighlighter ? TaperKind.None : TaperKind;

    public InkColor EffectiveColor => IsHighlighter ? Color.WithAlpha(96) : Color;

    public StrokeAttributes Clone() => new()
    {
        Color = Color,
        BaseWidth = BaseWidth,
        IsHighlighter = IsHighlighter,
        TaperKind = TaperKind,
    };
}