namespace InkCanvas.Controls.Controls;

/// <summary>
/// Icon codepoints, named so call sites never contain raw private-use characters.
/// Everything here is Segoe Fluent Icons / Segoe MDL2 Assets except <see cref="Shapes"/>,
/// which is a geometric character from Segoe UI Symbol (set <c>IconFont</c> when using it).
/// </summary>
public static class Glyphs
{
    public const string Pen = "";
    public const string Highlight = "";
    public const string Eraser = "";
    public const string Select = "";

    public const string Shapes = "△";
    public const string ShapesFont = "Segoe UI Symbol";

    /// <summary>Sentinel instead of an icon codepoint: hosts render the literal text "AI".</summary>
    public const string Ai = "AI";

    public const string Undo = "";
    public const string Redo = "";
    public const string Delete = "";
    public const string Add = "";

    public const string Settings = "";
    public const string Camera = "";
    public const string Stopwatch = "";
    public const string People = "";
    public const string Board = "";

    public const string ChevronUp = "";
    public const string ChevronDown = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";

    public const string Lock = "";
    public const string Unlock = "";
    public const string Close = "";
    public const string Exit = "";

    public const string Play = "";
    public const string Pause = "";
    public const string Restart = "";
    public const string Replay = "";
    public const string Help = "";
    public const string Info = "";
    public const string Download = "";
    public const string TimeMachine = "\uE81C";
}