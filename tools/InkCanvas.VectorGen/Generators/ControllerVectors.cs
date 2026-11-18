using System.Numerics;
using System.Text.Json;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Settings;
using InkCanvas.Core.Shapes;
using InkCanvas.Ink;
using InkCanvas.Platform.Input;
using InkCanvas.Platform.Windows;

/// <summary>
/// Phase B2/B3 controller vectors: driven by the real C# <see cref="InkController"/>.
/// The ArkTS side reproduces the same operation sequence via runControllerScenario.
/// </summary>
internal static class ControllerVectors
{
    public static void Write(string dir)
    {
        var cases = new List<object>();
        Add(cases, "pen-drag", PenDrag());
        Add(cases, "highlighter-tap", HighlighterTap());
        Add(cases, "mouse-draw", MouseDraw());
        Add(cases, "area-erase", AreaErase());
        Add(cases, "whole-stroke-erase", WholeStrokeErase());
        Add(cases, "palm-area-erase", PalmAreaErase());
        Add(cases, "shape-line", ShapeLine());
        Add(cases, "shape-cuboid-two-step", ShapeCuboidTwoStep());
        Add(cases, "shape-hyperbola-ask-remove", ShapeHyperbolaAskRemove());
        Add(cases, "selection-drag", SelectionDrag());
        Add(cases, "selection-clone", SelectionClone());
        Add(cases, "selection-delete", SelectionDelete());
        Add(cases, "two-finger-zoom-clamp", TwoFingerZoomClamp());
        Add(cases, "two-finger-selection-permove", TwoFingerSelectionPerMove());
        Add(cases, "two-finger-writing", TwoFingerWriting());
        Add(cases, "slide-flip-threshold", SlideFlipThreshold());
        Add(cases, "pan-tool", PanTool());
        Add(cases, "recognize-circle", RecognizeCircle());

        JsonWriter.Write(dir, "controller.json", new
        {
            id = "controller",
            entry = "harmony/ink/src/main/ets/InkController.ts",
            note = "C# InkController 驱动序列生成：落笔/移动/抬笔、高亮、面积擦、整笔擦、手掌擦、图形（单步/两步体/双曲线询问）、选区拖拽/克隆/删除、双指缩放钳制、双指选区每 move 一撤销单元、多指书写、翻页阈值、平移工具、墨迹转形状。期望由工作树 C# 实际驱动产生，ArkTS 由 runControllerScenario 复现同一操作序列。Lasso 擦/选区元素/TransformSelectionUndoable 未移植，不在向量内。",
            cases
        });
    }

    private static void Add(List<object> cases, string id, object scenario)
    {
        var json = JsonSerializer.Serialize(scenario);
        using var document = JsonDocument.Parse(json);
        var expected = ControllerScenarioRunner.Run(document.RootElement);
        cases.Add(new
        {
            id,
            fn = "runControllerScenario",
            input = new object[] { scenario },
            expected,
            tolerance = 1e-4
        });
    }

    // ---- scenario builders -------------------------------------------------

    private static object Scenario(object initial, object[] ops) => new { Initial = initial, Ops = ops };

    private static object Initial(
        int tool = 0,
        int shapeKind = 0,
        int eraserMode = 0,
        double eraserRadius = 16,
        double[]? view = null,
        double[]? center = null,
        bool multiTouch = true,
        bool palmErases = true,
        bool recognize = true,
        bool gesturesSelection = false,
        bool clone = false,
        bool sticky = false,
        int hyperbolaOption = 2,
        bool? ask = null,
        int pivot = 0,
        bool allowZoom = true,
        bool allowTranslate = true,
        bool allowRotation = false,
        object? attributes = null,
        object? palm = null) => new
    {
        Tool = tool,
        ShapeKind = shapeKind,
        EraserMode = eraserMode,
        EraserRadius = eraserRadius,
        ViewTransform = view ?? new double[] { 1, 0, 0, 1, 0, 0 },
        ViewCenter = center ?? new double[] { 960, 540 },
        MultiTouchEnabled = multiTouch,
        PalmErases = palmErases,
        RecognizeShapes = recognize,
        GesturesTargetSelection = gesturesSelection,
        CloneMode = clone,
        StickyShape = sticky,
        HyperbolaAsymptoteOption = hyperbolaOption,
        AskRemoveAsymptotes = ask,
        TransformCenterOption = pivot,
        AllowZoom = allowZoom,
        AllowTranslate = allowTranslate,
        AllowRotation = allowRotation,
        CurrentAttributes = attributes ?? Attrs(),
        Palm = palm ?? PalmSettings()
    };

    private static object Attrs(
        uint color = 0xFF000000u,
        double width = 2.5,
        bool highlighter = false,
        int alpha = 96,
        int taper = 1) => new
    {
        ColorPacked = color,
        BaseWidth = width,
        IsHighlighter = highlighter,
        HighlighterAlpha = alpha,
        TaperKind = taper
    };

    private static object PalmSettings(
        bool advanced = false,
        bool specialScreen = false,
        bool quadIr = false,
        double multiplier = 0.25,
        int nibBounds = 10,
        int fingerBounds = 30,
        bool nibMode = false,
        bool forceEraser = false,
        bool forcePointEraser = true,
        bool pptGestureEligible = false,
        double palmThreshold = 30,
        bool penHasPriority = true)
    {
        object? advancedValue = advanced
            ? new
            {
                isSpecialScreen = specialScreen,
                isQuadIR = quadIr,
                touchMultiplier = multiplier,
                nibModeBoundsWidth = nibBounds,
                fingerModeBoundsWidth = fingerBounds,
                nibModeBoundsWidthThresholdValue = 2.5,
                fingerModeBoundsWidthThresholdValue = 2.5,
                nibModeBoundsWidthEraserSize = 0.8,
                fingerModeBoundsWidthEraserSize = 0.8
            }
            : null;
        return new
        {
            Advanced = advancedValue,
            NibMode = nibMode,
            ForceEraser = forceEraser,
            ForcePointEraser = forcePointEraser,
            PptGestureEligible = pptGestureEligible,
            PalmContactThreshold = palmThreshold,
            PenHasPriority = penHasPriority
        };
    }

    private static object Seed(object attributes, object[] points, bool completed = true) =>
        new { Attributes = attributes, Points = points, IsCompleted = completed };

    private static object Point(double x, double y, double width = 0, double pressure = 0.5, double time = 0) =>
        new { X = x, Y = y, Pressure = pressure, Width = width, TimeMs = time };

    private static object Pointer(
        int id,
        double x,
        double y,
        int kind = 0,
        double pressure = 0.5,
        int timeMs = 0,
        double contactWidth = 0,
        double contactHeight = 0,
        bool shift = false,
        bool control = false) => new
    {
        Id = id,
        X = x,
        Y = y,
        Pressure = pressure,
        ContactWidth = contactWidth,
        ContactHeight = contactHeight,
        Kind = kind,
        TimeMs = timeMs,
        Shift = shift,
        Control = control
    };

    private static object Down(object pointer) => new { Op = "down", Pointer = pointer };
    private static object Move(object pointer) => new { Op = "move", Pointer = pointer };
    private static object Up(object pointer) => new { Op = "up", Pointer = pointer };
    private static object Cancel(object pointer) => new { Op = "cancel", Pointer = pointer };
    private static object SeedOp(object stroke) => new { Op = "seed", Stroke = stroke };
    private static object Simple(string op) => new { Op = op };
    private static object ValueOp(string op, int value) => new { Op = op, Value = value };
    private static object FlagOp(string op, bool flag) => new { Op = op, Flag = flag };
    private static object ShapeOp(int kind, bool sticky = false) => new { Op = "selectShape", Value = kind, Flag = sticky };
    private static object SelectIndex(int index) => new { Op = "selectIndex", Value = index };
    private static object Gestures(bool zoom, bool translate, bool rotate) =>
        new { Op = "configureGestures", Zoom = zoom, Translate = translate, Rotate = rotate };

    private static object RectStroke(double width = 4) => Seed(
        Attrs(width: width, taper: 0),
        new object[]
        {
            Point(100, 100, width), Point(200, 100, width), Point(200, 160, width),
            Point(100, 160, width), Point(100, 100, width)
        });

    private static object LineStroke(double y, double width = 4) => Seed(
        Attrs(width: width, taper: 0),
        new object[] { Point(0, y, width), Point(200, y, width) });

    // ---- cases -------------------------------------------------------------

    private static object PenDrag() => Scenario(
        Initial(),
        new object[]
        {
            Down(Pointer(1, 100, 100, timeMs: 0)),
            Move(Pointer(1, 120, 110, timeMs: 10)),
            Move(Pointer(1, 150, 130, timeMs: 20)),
            Move(Pointer(1, 190, 160, timeMs: 30)),
            Up(Pointer(1, 190, 160, timeMs: 40))
        });

    private static object HighlighterTap() => Scenario(
        Initial(tool: 1, attributes: Attrs(width: 20, alpha: 96, taper: 1)),
        new object[]
        {
            Down(Pointer(2, 50, 50, pressure: 0.7, timeMs: 0)),
            Up(Pointer(2, 50, 50, pressure: 0.7, timeMs: 10))
        });

    private static object MouseDraw() => Scenario(
        Initial(),
        new object[]
        {
            Down(Pointer(3, 10, 10, kind: 2, timeMs: 0)),
            Move(Pointer(3, 40, 40, kind: 2, timeMs: 10)),
            Up(Pointer(3, 40, 40, kind: 2, timeMs: 20))
        });

    private static object AreaErase() => Scenario(
        Initial(tool: 2, eraserMode: 0, eraserRadius: 10, attributes: Attrs(taper: 1)),
        new object[]
        {
            SeedOp(LineStroke(50)),
            SeedOp(LineStroke(100)),
            SeedOp(LineStroke(150)),
            Down(Pointer(4, 100, 100, kind: 0, timeMs: 0)),
            Move(Pointer(4, 120, 100, kind: 0, timeMs: 10)),
            Up(Pointer(4, 120, 100, kind: 0, timeMs: 20)),
            Simple("undo"),
            Simple("redo")
        });

    private static object WholeStrokeErase() => Scenario(
        Initial(tool: 2, eraserMode: 1, eraserRadius: 5),
        new object[]
        {
            SeedOp(LineStroke(50)),
            SeedOp(LineStroke(100)),
            Down(Pointer(5, 100, 50, timeMs: 0)),
            Move(Pointer(5, 120, 50, timeMs: 10)),
            Up(Pointer(5, 120, 50, timeMs: 20)),
            Simple("undo"),
            Simple("redo")
        });

    private static object PalmAreaErase() => Scenario(
        Initial(tool: 0, palmErases: true, eraserRadius: 10),
        new object[]
        {
            SeedOp(LineStroke(50)),
            Down(Pointer(6, 100, 50, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 0)),
            Move(Pointer(6, 120, 50, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 10)),
            Up(Pointer(6, 120, 50, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 20)),
            Simple("undo")
        });

    private static object ShapeLine() => Scenario(
        Initial(tool: 4, shapeKind: 0, attributes: Attrs(width: 3, taper: 1)),
        new object[]
        {
            Down(Pointer(7, 100, 100, timeMs: 0)),
            Move(Pointer(7, 200, 150, timeMs: 10)),
            Up(Pointer(7, 200, 150, timeMs: 20)),
            Simple("undo")
        });

    private static object ShapeCuboidTwoStep() => Scenario(
        Initial(tool: 4, shapeKind: 24, attributes: Attrs(width: 3, taper: 1)),
        new object[]
        {
            Down(Pointer(8, 100, 100, timeMs: 0)),
            Move(Pointer(8, 200, 180, timeMs: 10)),
            Up(Pointer(8, 200, 180, timeMs: 20)),
            Down(Pointer(8, 150, 120, timeMs: 30)),
            Move(Pointer(8, 150, 220, timeMs: 40)),
            Up(Pointer(8, 150, 220, timeMs: 50)),
            Simple("undo")
        });

    private static object ShapeHyperbolaAskRemove() => Scenario(
        Initial(tool: 4, shapeKind: 18, hyperbolaOption: 2, ask: true, attributes: Attrs(width: 2.5, taper: 1)),
        new object[]
        {
            Down(Pointer(9, 100, 100, timeMs: 0)),
            Move(Pointer(9, 200, 150, timeMs: 10)),
            Up(Pointer(9, 200, 150, timeMs: 20)),
            Down(Pointer(9, 150, 120, timeMs: 30)),
            Move(Pointer(9, 180, 160, timeMs: 40)),
            Up(Pointer(9, 180, 160, timeMs: 50)),
            Simple("undo")
        });

    private static object SelectionDrag() => Scenario(
        Initial(tool: 3),
        new object[]
        {
            SeedOp(RectStroke()),
            Down(Pointer(10, 150, 100, kind: 2, timeMs: 0)),
            Up(Pointer(10, 150, 100, kind: 2, timeMs: 10)),
            Down(Pointer(10, 125, 130, kind: 2, timeMs: 20)),
            Move(Pointer(10, 145, 150, kind: 2, timeMs: 30)),
            Up(Pointer(10, 145, 150, kind: 2, timeMs: 40)),
            Simple("undo"),
            Simple("redo")
        });

    private static object SelectionClone() => Scenario(
        Initial(tool: 3, clone: true),
        new object[]
        {
            SeedOp(RectStroke()),
            Down(Pointer(11, 150, 100, kind: 2, timeMs: 0)),
            Up(Pointer(11, 150, 100, kind: 2, timeMs: 10)),
            Down(Pointer(11, 125, 130, kind: 2, timeMs: 20)),
            Move(Pointer(11, 145, 150, kind: 2, timeMs: 30)),
            Up(Pointer(11, 145, 150, kind: 2, timeMs: 40)),
            Simple("undo"),
            Simple("redo")
        });

    private static object SelectionDelete() => Scenario(
        Initial(tool: 3),
        new object[]
        {
            SeedOp(RectStroke()),
            Down(Pointer(12, 150, 100, kind: 2, timeMs: 0)),
            Up(Pointer(12, 150, 100, kind: 2, timeMs: 10)),
            Simple("deleteSelection"),
            Simple("undo"),
            Simple("redo")
        });

    private static object TwoFingerZoomClamp() => Scenario(
        Initial(tool: 0, multiTouch: false, center: new double[] { 0, 0 }, allowZoom: true, allowTranslate: false, allowRotation: false),
        new object[]
        {
            Down(Pointer(20, 100, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 0)),
            Down(Pointer(21, 200, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 10)),
            Move(Pointer(20, 80, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 20)),
            Move(Pointer(21, 220, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 30)),
            Move(Pointer(20, 30, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 40)),
            Move(Pointer(21, 270, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 50)),
            Move(Pointer(20, -850, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 60)),
            Move(Pointer(21, 850, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 70)),
            Move(Pointer(20, 99, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 80)),
            Move(Pointer(21, 101, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 90)),
            Up(Pointer(20, 99, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 100)),
            Up(Pointer(21, 101, 100, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 110))
        });

    private static object TwoFingerSelectionPerMove() => Scenario(
        Initial(tool: 0, multiTouch: false, gesturesSelection: true, center: new double[] { 0, 0 },
            allowZoom: true, allowTranslate: false, allowRotation: false),
        new object[]
        {
            SeedOp(RectStroke()),
            SelectIndex(0),
            Down(Pointer(30, 125, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 0)),
            Down(Pointer(31, 180, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 10)),
            Move(Pointer(30, 105, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 20)),
            Move(Pointer(31, 200, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 30)),
            Up(Pointer(30, 105, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 40)),
            Up(Pointer(31, 200, 130, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 50)),
            Simple("undo"),
            Simple("undo"),
            Simple("redo")
        });

    private static object TwoFingerWriting() => Scenario(
        Initial(tool: 0, multiTouch: true),
        new object[]
        {
            Down(Pointer(40, 10, 10, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 0)),
            Down(Pointer(41, 20, 20, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 5)),
            Move(Pointer(40, 30, 10, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 10)),
            Move(Pointer(41, 40, 20, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 15)),
            Up(Pointer(40, 30, 10, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 20)),
            Up(Pointer(41, 40, 20, kind: 1, contactWidth: 1, contactHeight: 1, timeMs: 25))
        });

    private static object SlideFlipThreshold() => Scenario(
        Initial(tool: 0, palmErases: false,
            palm: PalmSettings(advanced: true, pptGestureEligible: true)),
        new object[]
        {
            Down(Pointer(50, 100, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 0)),
            Up(Pointer(50, 39, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 10)),
            Down(Pointer(50, 100, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 20)),
            Up(Pointer(50, 161, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 30)),
            Down(Pointer(50, 100, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 40)),
            Up(Pointer(50, 40, 100, kind: 1, contactWidth: 40, contactHeight: 40, timeMs: 50))
        });

    private static object PanTool() => Scenario(
        Initial(tool: 5),
        new object[]
        {
            Down(Pointer(60, 100, 100, kind: 2, timeMs: 0)),
            Move(Pointer(60, 130, 150, kind: 2, timeMs: 10)),
            Up(Pointer(60, 130, 150, kind: 2, timeMs: 20))
        });

    private static object RecognizeCircle()
    {
        var points = new List<object>();
        for (var i = 0; i < 16; i++)
        {
            var angle = 2 * Math.PI * i / 16.0;
            points.Add(Point(100 + 60 * Math.Cos(angle), 100 + 60 * Math.Sin(angle)));
        }
        var ops = new List<object>();
        ops.Add(Down(Pointer(70, 160, 100, timeMs: 0)));
        for (var i = 1; i < points.Count; i++)
        {
            // Rebuild positions without the Width/Pressure defaults that Point adds.
            var angle = 2 * Math.PI * i / 16.0;
            ops.Add(Move(Pointer(70, 100 + 60 * Math.Cos(angle), 100 + 60 * Math.Sin(angle), timeMs: i * 10)));
        }
        ops.Add(Up(Pointer(70, 100 + 60 * Math.Cos(2 * Math.PI * 15 / 16.0), 100 + 60 * Math.Sin(2 * Math.PI * 15 / 16.0), timeMs: 160)));
        ops.Add(Simple("undo"));
        ops.Add(Simple("undo"));
        ops.Add(Simple("redo"));
        return Scenario(Initial(tool: 0, recognize: true), ops.ToArray());
    }
}

/// <summary>
/// Interprets a controller.json scenario with the real C# controller.
/// Mirrors harmony/ink/src/main/ets/InkController.ts runControllerScenario.
/// </summary>
internal static class ControllerScenarioRunner
{
    private sealed class Counters
    {
        public int Inv;
        public int SelCh;
        public int ToolCh;
        public readonly List<bool> Slides = new();
    }

    public static object Run(JsonElement scenario)
    {
        using var controller = new InkController();
        var counters = new Counters();
        controller.Invalidated += () => counters.Inv++;
        controller.SelectionChanged += () => counters.SelCh++;
        controller.ToolChanged += () => counters.ToolCh++;
        controller.SlideFlipRequested += next => counters.Slides.Add(next);

        ApplyInitial(controller, scenario.GetProperty("Initial"));
        var trace = new List<object>();
        foreach (var op in scenario.GetProperty("Ops").EnumerateArray())
        {
            ApplyOp(controller, op);
            trace.Add(Snapshot(controller, counters));
        }
        return new { Trace = trace, Final = FinalSnapshot(controller), SlideFlips = counters.Slides };
    }

    private static void ApplyInitial(InkController controller, JsonElement initial)
    {
        if (initial.TryGetProperty("Tool", out var tool)) controller.Tool = (InkTool)tool.GetInt32();
        if (initial.TryGetProperty("ShapeKind", out var shapeKind)) controller.ShapeKind = (ShapeKind)shapeKind.GetInt32();
        if (initial.TryGetProperty("EraserMode", out var eraserMode)) controller.EraserMode = (InkEraserMode)eraserMode.GetInt32();
        if (initial.TryGetProperty("EraserRadius", out var eraserRadius)) controller.EraserRadius = eraserRadius.GetSingle();
        if (initial.TryGetProperty("ViewTransform", out var view))
        {
            controller.ViewTransform = new Matrix3x2(
                view[0].GetSingle(), view[1].GetSingle(), view[2].GetSingle(),
                view[3].GetSingle(), view[4].GetSingle(), view[5].GetSingle());
        }
        if (initial.TryGetProperty("ViewCenter", out var center))
            controller.ViewCenter = new Vector2(center[0].GetSingle(), center[1].GetSingle());
        if (initial.TryGetProperty("MultiTouchEnabled", out var multiTouch)) controller.MultiTouchEnabled = multiTouch.GetBoolean();
        if (initial.TryGetProperty("PalmErases", out var palmErases)) controller.PalmErases = palmErases.GetBoolean();
        if (initial.TryGetProperty("RecognizeShapes", out var recognize)) controller.RecognizeShapes = recognize.GetBoolean();
        if (initial.TryGetProperty("GesturesTargetSelection", out var gesturesSelection))
            controller.GesturesTargetSelection = gesturesSelection.GetBoolean();
        if (initial.TryGetProperty("CloneMode", out var clone)) controller.CloneMode = clone.GetBoolean();
        if (initial.TryGetProperty("StickyShape", out var sticky)) controller.StickyShape = sticky.GetBoolean();
        if (initial.TryGetProperty("HyperbolaAsymptoteOption", out var hyperbola))
            controller.HyperbolaAsymptoteOption = (OptionalOperation)hyperbola.GetInt32();
        if (initial.TryGetProperty("AskRemoveAsymptotes", out var ask))
        {
            if (ask.ValueKind == JsonValueKind.Null) controller.AskRemoveAsymptotes = null;
            else
            {
                var answer = ask.GetBoolean();
                controller.AskRemoveAsymptotes = () => Task.FromResult(answer);
            }
        }
        if (initial.TryGetProperty("TransformCenterOption", out var pivot))
            controller.TransformCenterOption = (MatrixTransformCenterPointOptions)pivot.GetInt32();

        var zoom = !initial.TryGetProperty("AllowZoom", out var zoomValue) || zoomValue.GetBoolean();
        var translate = !initial.TryGetProperty("AllowTranslate", out var translateValue) || translateValue.GetBoolean();
        var rotate = initial.TryGetProperty("AllowRotation", out var rotateValue) && rotateValue.GetBoolean();
        controller.ConfigureGestures(zoom, translate, rotate);

        if (initial.TryGetProperty("CurrentAttributes", out var attributes))
        {
            controller.CurrentAttributes.Color = InkColor.FromPackedArgb(attributes.GetProperty("ColorPacked").GetUInt32());
            controller.CurrentAttributes.BaseWidth = attributes.GetProperty("BaseWidth").GetSingle();
            controller.CurrentAttributes.IsHighlighter = attributes.GetProperty("IsHighlighter").GetBoolean();
            controller.CurrentAttributes.HighlighterAlpha = attributes.GetProperty("HighlighterAlpha").GetByte();
            controller.CurrentAttributes.TaperKind = (TaperKind)attributes.GetProperty("TaperKind").GetInt32();
        }

        if (initial.TryGetProperty("Palm", out var palm))
        {
            if (palm.TryGetProperty("Advanced", out var advanced))
            {
                if (advanced.ValueKind == JsonValueKind.Null) controller.Palm.Advanced = null;
                else
                {
                    controller.Palm.Advanced = new AdvancedSettings
                    {
                        IsSpecialScreen = advanced.GetProperty("isSpecialScreen").GetBoolean(),
                        IsQuadIR = advanced.GetProperty("isQuadIR").GetBoolean(),
                        TouchMultiplier = advanced.GetProperty("touchMultiplier").GetDouble(),
                        NibModeBoundsWidth = advanced.GetProperty("nibModeBoundsWidth").GetInt32(),
                        FingerModeBoundsWidth = advanced.GetProperty("fingerModeBoundsWidth").GetInt32(),
                        NibModeBoundsWidthThresholdValue = advanced.GetProperty("nibModeBoundsWidthThresholdValue").GetDouble(),
                        FingerModeBoundsWidthThresholdValue = advanced.GetProperty("fingerModeBoundsWidthThresholdValue").GetDouble(),
                        NibModeBoundsWidthEraserSize = advanced.GetProperty("nibModeBoundsWidthEraserSize").GetDouble(),
                        FingerModeBoundsWidthEraserSize = advanced.GetProperty("fingerModeBoundsWidthEraserSize").GetDouble()
                    };
                }
            }
            if (palm.TryGetProperty("NibMode", out var nibMode)) controller.Palm.NibMode = nibMode.GetBoolean();
            if (palm.TryGetProperty("ForceEraser", out var forceEraser)) controller.Palm.ForceEraser = forceEraser.GetBoolean();
            if (palm.TryGetProperty("ForcePointEraser", out var forcePointEraser))
                controller.Palm.ForcePointEraser = forcePointEraser.GetBoolean();
            if (palm.TryGetProperty("PptGestureEligible", out var ppt))
                controller.Palm.PptGestureEligible = ppt.GetBoolean();
            if (palm.TryGetProperty("PalmContactThreshold", out var threshold))
                controller.Palm.PalmContactThreshold = threshold.GetSingle();
            if (palm.TryGetProperty("PenHasPriority", out var priority))
                controller.Palm.PenHasPriority = priority.GetBoolean();
        }
    }

    private static void ApplyOp(InkController controller, JsonElement op)
    {
        var name = op.GetProperty("Op").GetString();
        switch (name)
        {
            case "down":
                controller.OnPointerDown(MakePointer(op.GetProperty("Pointer")));
                break;
            case "move":
                controller.OnPointerMove(MakePointer(op.GetProperty("Pointer")));
                break;
            case "up":
                controller.OnPointerUp(MakePointer(op.GetProperty("Pointer")));
                break;
            case "cancel":
                controller.OnPointerCanceled(MakePointer(op.GetProperty("Pointer")));
                break;
            case "seed":
                controller.Document.Add(MakeSeed(op.GetProperty("Stroke")));
                break;
            case "undo":
                controller.Undo();
                break;
            case "redo":
                controller.Redo();
                break;
            case "clear":
                controller.Clear();
                break;
            case "deleteSelection":
                controller.DeleteSelection();
                break;
            case "clearSelection":
                controller.ClearSelection();
                break;
            case "selectAll":
                controller.SelectAll();
                break;
            case "selectIndex":
                controller.Selection.Add(controller.Document.Strokes[op.GetProperty("Value").GetInt32()]);
                break;
            case "setTool":
                controller.Tool = (InkTool)op.GetProperty("Value").GetInt32();
                break;
            case "setEraserMode":
                controller.EraserMode = (InkEraserMode)op.GetProperty("Value").GetInt32();
                break;
            case "setShapeKind":
                controller.ShapeKind = (ShapeKind)op.GetProperty("Value").GetInt32();
                break;
            case "selectShape":
                controller.SelectShape(
                    (ShapeKind)op.GetProperty("Value").GetInt32(),
                    op.TryGetProperty("Flag", out var sticky) && sticky.GetBoolean());
                break;
            case "setCloneMode":
                controller.CloneMode = op.GetProperty("Flag").GetBoolean();
                break;
            case "configureGestures":
                controller.ConfigureGestures(
                    op.TryGetProperty("Zoom", out var zoom) && zoom.GetBoolean(),
                    op.TryGetProperty("Translate", out var translate) && translate.GetBoolean(),
                    op.TryGetProperty("Rotate", out var rotate) && rotate.GetBoolean());
                break;
            default:
                throw new InvalidOperationException("controller scenario: unknown op " + name);
        }
    }

    private static OverlayPointer MakePointer(JsonElement pointer) => new(
        (uint)pointer.GetProperty("Id").GetInt32(),
        pointer.GetProperty("X").GetSingle(),
        pointer.GetProperty("Y").GetSingle(),
        pointer.TryGetProperty("Pressure", out var pressure) ? pressure.GetSingle() : 0.5f,
        pointer.TryGetProperty("ContactWidth", out var contactWidth) ? contactWidth.GetSingle() : 0f,
        pointer.TryGetProperty("ContactHeight", out var contactHeight) ? contactHeight.GetSingle() : 0f,
        (OverlayPointerKind)pointer.GetProperty("Kind").GetInt32(),
        (uint)(pointer.TryGetProperty("TimeMs", out var timeMs) ? timeMs.GetInt32() : 0),
        pointer.TryGetProperty("Shift", out var shift) && shift.GetBoolean(),
        pointer.TryGetProperty("Control", out var control) && control.GetBoolean());

    private static InkStroke MakeSeed(JsonElement seed)
    {
        var attributesJson = seed.GetProperty("Attributes");
        var attributes = new StrokeAttributes
        {
            Color = InkColor.FromPackedArgb(attributesJson.GetProperty("ColorPacked").GetUInt32()),
            BaseWidth = attributesJson.GetProperty("BaseWidth").GetSingle(),
            IsHighlighter = attributesJson.GetProperty("IsHighlighter").GetBoolean(),
            HighlighterAlpha = attributesJson.GetProperty("HighlighterAlpha").GetByte(),
            TaperKind = (TaperKind)attributesJson.GetProperty("TaperKind").GetInt32()
        };
        var stroke = new InkStroke(attributes);
        var points = seed.GetProperty("Points").EnumerateArray().Select(p => new InkPoint(
            p.GetProperty("X").GetSingle(),
            p.GetProperty("Y").GetSingle(),
            p.TryGetProperty("Pressure", out var pressure) ? pressure.GetSingle() : 0.5f,
            p.TryGetProperty("Width", out var width) ? width.GetSingle() : 0f,
            p.TryGetProperty("TimeMs", out var timeMs) ? timeMs.GetSingle() : 0f)).ToArray();
        stroke.RestorePoints(points);
        if (seed.TryGetProperty("IsCompleted", out var completed) && completed.GetBoolean()) stroke.Complete();
        return stroke;
    }

    private static object Snapshot(InkController controller, Counters counters)
    {
        var strokes = controller.Document.Strokes;
        var doc = strokes.Select(s => s.Count).ToArray();
        var docFirst = strokes
            .Select(s => s.Count == 0 ? Array.Empty<double>() : new double[] { s.Points[0].X, s.Points[0].Y })
            .ToArray();
        return new
        {
            Doc = doc,
            DocFirst = docFirst,
            Live = controller.LiveStrokes.Count,
            Preview = controller.ShapePreview.Count,
            Sel = SelectionIndices(controller),
            Undo = controller.History.UndoCount,
            Redo = controller.History.RedoCount,
            Tool = (int)controller.Tool,
            View = Arr(controller.ViewTransform),
            Inv = counters.Inv,
            SelCh = counters.SelCh,
            ToolCh = counters.ToolCh,
            Slides = counters.Slides.Count
        };
    }

    private static object FinalSnapshot(InkController controller)
    {
        var strokes = controller.Document.Strokes.ToArray();
        var frame = controller.SelectionFrame;
        var bounds = controller.SelectionBounds;
        return new
        {
            Strokes = strokes.Select(s => new
            {
                Points = s.Points.Select(p => new double[]
                {
                    p.X, p.Y, p.Pressure, p.Width, p.TimeMs
                }).ToArray(),
                BaseWidth = (double)s.Attributes.BaseWidth,
                ColorPacked = s.Attributes.Color.PackedArgb,
                IsHighlighter = s.Attributes.IsHighlighter,
                HighlighterAlpha = (int)s.Attributes.HighlighterAlpha,
                TaperKind = (int)s.Attributes.TaperKind,
                IsRecognizedShape = s.IsRecognizedShape,
                IsCompleted = s.IsCompleted
            }).ToArray(),
            Sel = SelectionIndices(controller),
            Tool = (int)controller.Tool,
            EraserMode = (int)controller.EraserMode,
            CloneMode = controller.CloneMode,
            View = Arr(controller.ViewTransform),
            ViewCenter = new double[] { controller.ViewCenter.X, controller.ViewCenter.Y },
            Undo = controller.History.UndoCount,
            Redo = controller.History.RedoCount,
            SelectionBounds = new double[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
            SelectionFrame = new
            {
                Bounds = new double[]
                {
                    frame.Bounds.Left, frame.Bounds.Top, frame.Bounds.Right, frame.Bounds.Bottom
                },
                Transform = Arr(frame.Transform)
            }
        };
    }

    private static int[] SelectionIndices(InkController controller)
    {
        var strokes = controller.Document.Strokes.ToList();
        return controller.Selection.Select(s => strokes.IndexOf(s)).ToArray();
    }

    private static double[] Arr(Matrix3x2 m) =>
        new double[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 };
}
