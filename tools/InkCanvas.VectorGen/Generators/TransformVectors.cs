using System.Numerics;
using InkCanvas.Core.Ink;
using InkCanvas.Core.Selection;
using InkCanvas.Core.Settings;
using InkCanvas.Ink.Manipulation;

internal static class TransformVectors
{
    public static void Write(string dir)
    {
        WriteMatrix(dir);
        WriteSelection(dir);
        WriteTwoFinger(dir);
        WritePivot(dir);
    }

    private static void WriteMatrix(string dir)
    {
        var cases = new List<object>();
        AddMatrix(cases, "mul-identity-translate", Matrix3x2.Identity, Matrix3x2.CreateTranslation(10, 20));
        AddMatrix(cases, "mul-rotate-scale", Matrix3x2.CreateRotation(MathF.PI / 6), Matrix3x2.CreateScale(2, 3));
        AddMatrix(cases, "mul-translate-rotate", Matrix3x2.CreateTranslation(5, -4), Matrix3x2.CreateRotation(-MathF.PI / 2));
        AddMatrix(cases, "mul-chain", Matrix3x2.CreateScale(2, 2) * Matrix3x2.CreateTranslation(3, 4), Matrix3x2.CreateRotation(0.7f));

        var points = new[] { (0f, 0f), (10f, 0f), (3f, 7f), (-5f, 2.5f) };
        foreach (var (fn, m) in new (string, Matrix3x2)[]
        {
            ("translate", Matrix3x2.CreateTranslation(12, -7)),
            ("rotate", Matrix3x2.CreateRotation(MathF.PI / 4)),
            ("scale", Matrix3x2.CreateScale(2.5f, 0.5f))
        })
        {
            foreach (var p in points)
            {
                var transformed = Vector2.Transform(new Vector2(p.Item1, p.Item2), m);
                cases.Add(new
                {
                    id = $"point-{fn}-{p.Item1}-{p.Item2}",
                    fn = "transformPoint",
                    input = new object[] { M(m), p.Item1, p.Item2 },
                    expected = new { X = transformed.X, Y = transformed.Y },
                    tolerance = 1e-4
                });
            }
        }

        foreach (var angle in new[] { 0f, MathF.PI / 6, -MathF.PI / 2, 2.5f })
        {
            cases.Add(new
            {
                id = $"rotation-{angle}",
                fn = "createRotation",
                input = new object[] { angle },
                expected = M(Matrix3x2.CreateRotation(angle)),
                tolerance = 1e-4
            });
        }

        foreach (var m in new[]
        {
            Matrix3x2.CreateTranslation(4, 9),
            Matrix3x2.CreateRotation(0.3f),
            Matrix3x2.CreateScale(3, 0.25f),
            Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(5, 6)
        })
        {
            Matrix3x2.Invert(m, out var inverted);
            cases.Add(new
            {
                id = "invert-" + string.Join("-", M(m).Select(v => v.ToString("0.###"))),
                fn = "invertMatrixValues",
                input = new object[] { M(m) },
                expected = M(inverted),
                tolerance = 1e-4
            });
        }

        JsonWriter.Write(dir, "matrix.json", new
        {
            id = "matrix",
            entry = "harmony/core/src/main/ets/math/Matrix3x2.ts",
            cases
        });
    }

    private static void AddMatrix(List<object> cases, string id, Matrix3x2 a, Matrix3x2 b)
    {
        cases.Add(new
        {
            id,
            fn = "matrixMultiply",
            input = new object[] { M(a), M(b) },
            expected = M(a * b),
            tolerance = 1e-4
        });
    }

    private static void WriteSelection(string dir)
    {
        var cases = new List<object>();
        var bounds = new Rect(0, 0, 100, 50);
        var hitPoints = new (float X, float Y)[]
        {
            (50, -28), // rotate
            (0, 0), (100, 0), (100, 50), (0, 50), // corners
            (50, 25), // move
            (200, 200), // none
            (50, 0), // inside edge -> move
            (12, 0) // near top-left within radius
        };
        foreach (var p in hitPoints)
        {
            cases.Add(new
            {
                id = $"hit-{p.X}-{p.Y}",
                fn = "selectionHitTest",
                input = new object[] { BoundsJson(bounds), p.X, p.Y },
                expected = (int)SelectionGeometry.HitTest(bounds, new Vector2(p.X, p.Y)),
                tolerance = 0
            });
        }

        cases.Add(new
        {
            id = "hit-empty-bounds",
            fn = "selectionHitTest",
            input = new object[] { BoundsJson(Rect.Empty), 0f, 0f },
            expected = 0,
            tolerance = 0
        });

        var drags = new (string Id, SelectionHandle Handle, Vector2 From, Vector2 To, bool KeepAspect)[]
        {
            ("drag-move", SelectionHandle.Move, new Vector2(50, 25), new Vector2(60, 30), false),
            ("drag-rotate", SelectionHandle.Rotate, new Vector2(50, 0), new Vector2(100, 25), false),
            ("drag-rotate-degenerate", SelectionHandle.Rotate, new Vector2(50, 25), new Vector2(60, 30), false),
            ("drag-tl", SelectionHandle.TopLeft, new Vector2(0, 0), new Vector2(-10, -8), false),
            ("drag-br", SelectionHandle.BottomRight, new Vector2(100, 50), new Vector2(140, 80), false),
            ("drag-top", SelectionHandle.Top, new Vector2(50, 0), new Vector2(50, -20), false),
            ("drag-right", SelectionHandle.Right, new Vector2(100, 25), new Vector2(140, 25), false),
            ("drag-tl-aspect", SelectionHandle.TopLeft, new Vector2(0, 0), new Vector2(-10, -4), true),
            ("drag-anchor", SelectionHandle.TopLeft, new Vector2(100, 50), new Vector2(80, 40), false),
            ("drag-none", SelectionHandle.None, new Vector2(10, 10), new Vector2(20, 20), false)
        };
        foreach (var d in drags)
        {
            cases.Add(new
            {
                id = d.Id,
                fn = "selectionDragMatrix",
                input = new object[]
                {
                    (int)d.Handle, BoundsJson(bounds),
                    d.From.X, d.From.Y, d.To.X, d.To.Y, d.KeepAspect
                },
                expected = M(SelectionGeometry.DragMatrix(d.Handle, bounds, d.From, d.To, d.KeepAspect)),
                tolerance = 1e-4
            });
        }

        var rotateHandle = SelectionGeometry.RotateHandlePosition(bounds);
        cases.Add(new
        {
            id = "rotate-handle-position",
            fn = "rotateHandlePosition",
            input = new object[] { BoundsJson(bounds) },
            expected = new { X = rotateHandle.X, Y = rotateHandle.Y },
            tolerance = 0
        });

        JsonWriter.Write(dir, "selection.json", new
        {
            id = "selection",
            entry = "harmony/core/src/main/ets/selection/SelectionGeometry.ts",
            cases
        });
    }

    private static void WriteTwoFinger(string dir)
    {
        var cases = new List<object>
        {
            TwoFingerCase("twofinger-translate", true, true, false, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 200f, 100f), (110f, 105f, 210f, 105f) }, 0f, 0f),
            TwoFingerCase("twofinger-zoom", true, false, false, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 110f, 100f), (94f, 100f, 116f, 100f) }, 100f, 100f),
            TwoFingerCase("twofinger-rotate", false, false, true, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 200f, 100f), (150f, 50f, 150f, 150f) }, 150f, 100f),
            TwoFingerCase("twofinger-all", true, true, true, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 200f, 100f), (105f, 108f, 215f, 120f), (110f, 115f, 230f, 140f) }, 150f, 100f),
            TwoFingerCase("twofinger-zoom-clamp-min", true, false, false, 0.2f, 8f,
                Matrix3x2.CreateScale(0.21f, 0.21f),
                new[] { (100f, 100f, 200f, 100f), (149f, 100f, 151f, 100f) }, 150f, 100f),
            TwoFingerCase("twofinger-zoom-clamp-max", true, false, false, 0.2f, 8f,
                Matrix3x2.CreateScale(7.9f, 7.9f),
                new[] { (149f, 100f, 151f, 100f), (100f, 100f, 200f, 100f) }, 150f, 100f),
            TwoFingerCase("twofinger-degenerate", true, true, true, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 100.5f, 100f), (100f, 100f, 100.5f, 100f) }, 100f, 100f),
            TwoFingerCase("twofinger-identity", true, true, false, 0.2f, 8f, Matrix3x2.Identity,
                new[] { (100f, 100f, 200f, 100f) }, 150f, 100f)
        };

        JsonWriter.Write(dir, "two-finger.json", new
        {
            id = "two-finger",
            entry = "harmony/core/src/main/ets/ink/TwoFingerManipulator.ts",
            cases
        });
    }

    private static object TwoFingerCase(string id, bool zoom, bool translate, bool rotate,
        float minScale, float maxScale, Matrix3x2 initial,
        (float AX, float AY, float BX, float BY)[] samples, float centerX, float centerY)
    {
        var manipulator = new TwoFingerManipulator
        {
            AllowZoom = zoom,
            AllowTranslate = translate,
            AllowRotation = rotate,
            MinScale = minScale,
            MaxScale = maxScale
        };
        var transform = initial;
        var changed = new List<bool>();
        foreach (var s in samples)
        {
            changed.Add(manipulator.Update(
                new Vector2(s.AX, s.AY), new Vector2(s.BX, s.BY),
                ref transform, new Vector2(centerX, centerY)));
        }
        return new
        {
            id,
            fn = "simulateTwoFinger",
            input = new object[]
            {
                new { allowZoom = zoom, allowTranslate = translate, allowRotation = rotate, minScale, maxScale },
                M(initial),
                centerX,
                centerY,
                samples.Select(s => new { AX = s.AX, AY = s.AY, BX = s.BX, BY = s.BY }).ToArray()
            },
            expected = new { changed, final = M(transform) },
            tolerance = 1e-4
        };
    }

    private static void WritePivot(string dir)
    {
        var cases = new List<object>();
        var options = new[]
        {
            MatrixTransformCenterPointOptions.CanvasCenterPoint,
            MatrixTransformCenterPointOptions.SelectedElementsCenterPoint,
            MatrixTransformCenterPointOptions.GestureOperationCenterPoint
        };
        foreach (var option in options)
        {
            foreach (var loaded in new[] { true, false })
            {
                var resolved = MatrixTransformCenter.Resolve(
                    option, new Vector2(10, 20), new Vector2(30, 40), new Vector2(50, 60), loaded);
                cases.Add(new
                {
                    id = $"pivot-{(int)option}-{loaded}",
                    fn = "resolvePivot",
                    input = new object[] { (int)option, 10f, 20f, 30f, 40f, 50f, 60f, loaded },
                    expected = new { X = resolved.X, Y = resolved.Y },
                    tolerance = 0
                });
            }
        }
        cases.Add(new
        {
            id = "pivot-unknown",
            fn = "resolvePivot",
            input = new object[] { 9, 10f, 20f, 30f, 40f, 50f, 60f, true },
            expected = new { X = 10f, Y = 20f },
            tolerance = 0
        });

        JsonWriter.Write(dir, "pivot.json", new
        {
            id = "pivot",
            entry = "harmony/core/src/main/ets/ink/MatrixTransformCenter.ts",
            cases
        });
    }

    private static object BoundsJson(Rect bounds) => new
    {
        Left = (double)bounds.Left,
        Top = (double)bounds.Top,
        Right = (double)bounds.Right,
        Bottom = (double)bounds.Bottom
    };

    private static double[] M(Matrix3x2 m) => new double[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 };
}
