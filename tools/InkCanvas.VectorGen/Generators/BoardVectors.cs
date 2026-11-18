using InkCanvas.Core.Ink;
using InkCanvas.Features.Board;
using System.Numerics;

internal static class BoardVectors
{
    public static void Write(string dir)
    {
        var cases = new List<object>();
        var rotation = Matrix3x2.CreateRotation(MathF.PI / 4);
        var rotation90 = Matrix3x2.CreateRotation(MathF.PI / 2);
        var translation = Matrix3x2.CreateTranslation(5, 6);

        cases.Add(new { id = "read-null", fn = "readTransformArray", input = new object[] { (double[]?)null! }, expected = M(Matrix3x2.Identity), tolerance = 1e-4 });
        cases.Add(new { id = "read-short", fn = "readTransformArray", input = new object[] { new double[] { 1, 2 } }, expected = M(Matrix3x2.Identity), tolerance = 1e-4 });
        cases.Add(new { id = "read-valid", fn = "readTransformArray", input = new object[] { M(rotation) }, expected = M(rotation), tolerance = 1e-4 });
        cases.Add(new { id = "append-rotation-translation", fn = "appendTransform", input = new object[] { M(rotation), M(translation) }, expected = M(rotation * translation), tolerance = 1e-4 });
        cases.Add(new { id = "same-null-null", fn = "sameTransform", input = new object[] { null!, null! }, expected = true, tolerance = 0 });
        cases.Add(new { id = "same-null-array", fn = "sameTransform", input = new object[] { null!, M(Matrix3x2.Identity) }, expected = false, tolerance = 0 });
        cases.Add(new { id = "same-equal", fn = "sameTransform", input = new object[] { M(rotation), M(rotation) }, expected = true, tolerance = 0 });
        cases.Add(new { id = "same-different", fn = "sameTransform", input = new object[] { M(rotation), M(translation) }, expected = false, tolerance = 0 });
        cases.Add(new { id = "bounds-rotation", fn = "transformedBounds", input = new object[] { 10.0, 20.0, 100.0, 50.0, M(rotation90) }, expected = Bounds(BoardElementMath.TransformedBounds(10, 20, 100, 50, F(M(rotation90)))), tolerance = 1e-4 });
        cases.Add(new { id = "render-rotation", fn = "renderMatrix", input = new object[] { 10.0, 20.0, M(rotation90) }, expected = M(BoardElementMath.RenderMatrix(10, 20, F(M(rotation90)))), tolerance = 1e-4 });

        JsonWriter.Write(dir, "board-element.json", new
        {
            id = "board-element",
            entry = "harmony/features/src/main/ets/board/BoardElementMath.ts",
            cases
        });
    }

    private static double[] M(Matrix3x2 m) => new double[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 };

    private static float[] F(double[] values) => values.Select(v => (float)v).ToArray();

    private static double[] Bounds(Rect r) => new double[] { r.Left, r.Top, r.Right, r.Bottom };
}
