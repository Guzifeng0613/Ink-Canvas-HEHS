using InkCanvas.AnalyticGeometry;

internal static class AnalyticVectors
{
    public static void Write(string dir)
    {
        WriteExpressions(dir);
        WriteCompile(dir);
        WriteSamples(dir);
    }

    private static void WriteExpressions(string dir)
    {
        var cases = new List<object>
        {
            Norm("normalize-superscript-and-symbols", "x²−y×3÷2"),
            Norm("normalize-pi-theta", "π×θ"),
            Eval("quadratic-with-params", "a*x^2+b*x+c", 2, 0, 0, 1, -3, 2),
            Eval("implicit-multiplication", "2x", 3, 0, 0, 0, 0, 0),
            Eval("multi-letter-product", "xy", 3, 4, 0, 0, 0, 0),
            Eval("pi-and-e", "pi+e", 0, 0, 0, 0, 0, 0),
            Eval("unary-minus-and-power", "-x^2", 3, 0, 0, 0, 0, 0),
            Eval("functions", "sin(pi/2)+max(3,4)+atan2(1,1)", 0, 0, 0, 0, 0, 0),
            Eval("depth-and-variables", "((x+(a-b)))*t", 1, 0, 2, 5, 2, 0),
            Eval("exp-notation", "2e2", 0, 0, 0, 0, 0, 0),
            EvalError("error-unknown-name", "foo(x)", 0, 0, 0, 0, 0, 0),
            EvalError("error-unbalanced", "(x+1", 0, 0, 0, 0, 0, 0),
            EvalError("error-unsupported-char", "x@y", 0, 0, 0, 0, 0, 0),
            EvalError("error-too-long", "x" + new string('+', 2099) + "1", 0, 0, 0, 0, 0, 0)
        };
        JsonWriter.Write(dir, "math-expression.json", new
        {
            id = "math-expression",
            entry = "harmony/features/src/main/ets/analytic/MathExpression.ts",
            cases
        });
    }

    private static object Norm(string id, string value) => new
    {
        id,
        fn = "expressionNormalizeProbe",
        input = new object[] { value },
        expected = MathExpression.Normalize(value),
        tolerance = 0
    };

    private static object Eval(string id, string text, double x, double y, double t, double a, double b, double c)
    {
        var compiled = MathExpression.Compile(text);
        return new
        {
            id,
            fn = "expressionEvaluateProbe",
            input = new object[] { text, x, y, t, a, b, c },
            expected = compiled.Evaluate(x, y, t, a, b, c),
            tolerance = 1e-9
        };
    }

    private static object EvalError(string id, string text, double x, double y, double t, double a, double b, double c)
    {
        string message;
        try { MathExpression.Compile(text).Evaluate(x, y, t, a, b, c); message = ""; }
        catch (FormatException e) { message = "ERROR:" + e.Message.Split('，', '位')[0]; }
        return new
        {
            id,
            fn = "expressionEvaluateProbe",
            input = new object[] { text, x, y, t, a, b, c },
            expected = message,
            tolerance = 0
        };
    }

    private sealed record CaseDef(int Kind, string Formula, string Second, double XMin, double XMax,
        double YMin, double YMax, double TMin, double TMax, double A, double B, double C);

    private static readonly double Tau = Math.PI * 2;

    private static IEnumerable<(string Id, CaseDef Definition)> Definitions() => new[]
    {
        ("y-explicit", new CaseDef(0, "y=sin(x)", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("x-explicit", new CaseDef(0, "x=y^2", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("implicit-with-equals", new CaseDef(0, "x^2+y^2=25", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("implicit-without-equals", new CaseDef(0, "x^2+y^2-25", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("plain-expression-functiony", new CaseDef(0, "a*x^2+b*x+c", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("parametric-prefix", new CaseDef(1, "x=cos(t)", "y=sin(t)", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("polar-prefix", new CaseDef(2, "r=1+cos(t)", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("cartesian-rejects-t", new CaseDef(0, "y=sin(t)", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("parametric-rejects-xy", new CaseDef(1, "x=y", "y=x", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("range-must-increase", new CaseDef(0, "y=x", "", 10, -10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        ("two-equals-rejected", new CaseDef(0, "y=x=y", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
    };

    private static void WriteCompile(string dir)
    {
        var cases = new List<object>();
        foreach (var (id, def) in Definitions())
        {
            var definition = ToDefinition(def);
            try
            {
                var compiled = CompiledPlot.Compile(definition);
                var expected = new Dictionary<string, object> { ["Kind"] = (int)compiled.Kind, ["Variables"] = compiled.First.Variables.ToArray() };
                if (compiled.Second is not null) expected["SecondVariables"] = compiled.Second.Variables.ToArray();
                cases.Add(new { id, fn = "plotCompileProbe", input = InputOf(def), expected, tolerance = 0 });
            }
            catch (FormatException e)
            {
                cases.Add(new { id, fn = "plotCompileProbe", input = InputOf(def), expected = "ERROR:" + e.Message, tolerance = 0 });
            }
        }
        JsonWriter.Write(dir, "plot-compile.json", new
        {
            id = "plot-compile",
            entry = "harmony/features/src/main/ets/analytic/PlotCompiler.ts",
            cases
        });
    }

    private static void WriteSamples(string dir)
    {
        var samples = new (string Id, CaseDef Definition)[]
        {
            ("function-y-parabola", new CaseDef(0, "a*x^2+b*x+c", "", -10, 10, -7.5, 7.5, 0, Tau, 0.1, 0, 0)),
            ("function-y-line", new CaseDef(0, "y=x", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
            ("implicit-circle", new CaseDef(0, "x^2+y^2=25", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
            ("parametric-circle", new CaseDef(1, "x=5*cos(t)", "y=5*sin(t)", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
            ("polar-cardioid", new CaseDef(2, "r=1+cos(t)", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
            ("function-discontinuity-splits", new CaseDef(0, "y=1/x", "", -10, 10, -7.5, 7.5, 0, Tau, 1, 0, 0)),
        };
        var cases = new List<object>();
        foreach (var (id, def) in samples)
        {
            var definition = ToDefinition(def);
            var mesh = PlotSampler.Sample(CompiledPlot.Compile(definition), definition);
            var expected = new Dictionary<string, object> { ["Paths"] = mesh.Paths.Count };
            if (mesh.Paths.Count > 0)
            {
                expected["FirstPoint"] = new[] { mesh.Paths[0][0].X, mesh.Paths[0][0].Y };
                var last = mesh.Paths[^1];
                expected["LastPoint"] = new[] { last[^1].X, last[^1].Y };
            }
            expected["PointCount"] = mesh.Paths.Sum(path => path.Length);
            if (mesh.Limited) expected["Limited"] = true;
            cases.Add(new { id, fn = "plotSampleProbe", input = InputOf(def), expected, tolerance = 1e-6 });
        }
        JsonWriter.Write(dir, "plot-sampler.json", new
        {
            id = "plot-sampler",
            entry = "harmony/features/src/main/ets/analytic/PlotSampler.ts",
            cases
        });
    }

    private static PlotDefinition ToDefinition(CaseDef def) => new()
    {
        Kind = (PlotKind)def.Kind,
        Formula = def.Formula,
        SecondFormula = def.Second,
        XMin = def.XMin, XMax = def.XMax, YMin = def.YMin, YMax = def.YMax,
        TMin = def.TMin, TMax = def.TMax, A = def.A, B = def.B, C = def.C,
        IncludeAxes = true,
    };

    private static object[] InputOf(CaseDef def) => new object[]
    {
        def.Kind, def.Formula, def.Second,
        def.XMin, def.XMax, def.YMin, def.YMax, def.TMin, def.TMax, def.A, def.B, def.C
    };
}