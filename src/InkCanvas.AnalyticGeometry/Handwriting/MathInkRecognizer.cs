using System.Text;
using InkCanvas.AnalyticGeometry.Algebra;

namespace InkCanvas.AnalyticGeometry.Handwriting;

/// <summary>
/// Built-in 2D mathematical handwriting recognition engine (similar to Windows Math Input Panel).
/// Groups multi-stroke symbols, classifies them offline, parses 2D spatial structures
/// (fractions, radicals, superscripts, subscripts), and outputs expression + MathML 3.0.
/// </summary>
public static class MathInkRecognizer
{
    private static readonly string[] KnownFunctions = { "sin", "cos", "tan", "asin", "acos", "atan", "ln", "log", "abs", "lim", "exp" };

    public static MathInkRecognitionResult Recognize(
        IReadOnlyList<MathInkStroke> strokes,
        IReadOnlyDictionary<int, string> symbolOverrides = null)
    {
        if (strokes is null || strokes.Count == 0)
            return new MathInkRecognitionResult("", null, Array.Empty<RecognizedSymbol>(), Array.Empty<string>());

        var groups = GroupStrokes(strokes);
        var recognized = new List<RecognizedSymbol>(groups.Count);
        foreach (var group in groups)
        {
            var ranked = MathSymbolClassifier.Classify(group);
            var bounds = InkBounds.Empty;
            var ids = new int[group.Count];
            for (int i = 0; i < group.Count; i++) { bounds = bounds.Union(group[i].Bounds); ids[i] = group[i].Id; }
            string best = ranked.Count > 0 ? ranked[0].Symbol : "?";
            double confidence = ranked.Count > 0 ? ranked[0].Score : 0.2;
            var alternatives = ranked.Select(item => item.Symbol).Distinct().ToList();

            if (symbolOverrides is not null)
            {
                foreach (int id in ids)
                {
                    if (symbolOverrides.TryGetValue(id, out string chosen) && !string.IsNullOrWhiteSpace(chosen))
                    {
                        best = chosen;
                        confidence = 1.0;
                        if (!alternatives.Contains(chosen)) alternatives.Insert(0, chosen);
                        else { alternatives.Remove(chosen); alternatives.Insert(0, chosen); }
                        break;
                    }
                }
            }
            recognized.Add(new RecognizedSymbol(best, confidence, bounds, ids, alternatives));
        }

        string primary = ParseSpatial(recognized);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary)) candidates.Add(primary);

        // Build alternative expression candidates by swapping the most ambiguous symbols.
        foreach (var entry in recognized
            .Select((sym, idx) => (Symbol: sym, Index: idx))
            .Where(item => item.Symbol.Alternatives.Count > 1 && item.Symbol.Confidence < 0.94)
            .OrderBy(item => item.Symbol.Confidence)
            .Take(3))
        {
            foreach (string alt in entry.Symbol.Alternatives.Skip(1).Take(2))
            {
                var mutated = recognized.ToList();
                mutated[entry.Index] = entry.Symbol with { Symbol = alt };
                string expr = ParseSpatial(mutated);
                if (!string.IsNullOrWhiteSpace(expr) && !candidates.Contains(expr))
                    candidates.Add(expr);
                if (candidates.Count >= 6) break;
            }
            if (candidates.Count >= 6) break;
        }

        string mathMl = null;
        if (!string.IsNullOrWhiteSpace(primary))
        {
            try { mathMl = MathMarkup.ToMathML(primary); }
            catch (FormatException) { mathMl = null; }
        }

        return new MathInkRecognitionResult(primary, mathMl, recognized, candidates);
    }

    private static List<List<MathInkStroke>> GroupStrokes(IReadOnlyList<MathInkStroke> strokes)
    {
        int n = strokes.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) parent[rb] = ra; }

        double medianHeight = Median(strokes.Where(s => !s.IsDot && !s.IsHorizontalLine).Select(s => s.Bounds.Height).DefaultIfEmpty(36));
        double medianWidth = Median(strokes.Where(s => !s.IsDot && !s.IsHorizontalLine).Select(s => s.Bounds.Width).DefaultIfEmpty(26));

        // Detect candidate long fraction bars first so they are NOT merged with numerator/denominator strokes.
        var fractionBars = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            var bar = strokes[i];
            if (!bar.IsHorizontalLine || bar.Bounds.Width < Math.Max(34, medianWidth * 1.15)) continue;
            bool hasAbove = false, hasBelow = false;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var other = strokes[j];
                if (other.Bounds.CenterX < bar.Bounds.Left - 8 || other.Bounds.CenterX > bar.Bounds.Right + 8) continue;
                if (other.Bounds.Bottom <= bar.Bounds.CenterY + 6 && bar.Bounds.CenterY - other.Bounds.Bottom < medianHeight * 2.2) hasAbove = true;
                if (other.Bounds.Top >= bar.Bounds.CenterY - 6 && other.Bounds.Top - bar.Bounds.CenterY < medianHeight * 2.2) hasBelow = true;
            }
            if (hasAbove && hasBelow) fractionBars.Add(i);
        }

        for (int i = 0; i < n; i++)
        {
            if (fractionBars.Contains(i)) continue;
            for (int j = i + 1; j < n; j++)
            {
                if (fractionBars.Contains(j)) continue;
                var a = strokes[i];
                var b = strokes[j];

                // 1. Direct intersection (e.g., +, x, t, 4, f, ≠).
                if (MathInkStroke.SegmentsIntersect(a, b, 7))
                {
                    // Avoid merging a radical √ with its radicand if they barely touch at the roof.
                    if (LooksLikeRadicalTouchingContent(a, b) || LooksLikeRadicalTouchingContent(b, a)) continue;
                    Union(i, j);
                    continue;
                }

                // 2. Parallel horizontal bars forming '='.
                if (a.IsHorizontalLine && b.IsHorizontalLine)
                {
                    double overlapX = Math.Min(a.Bounds.Right, b.Bounds.Right) - Math.Max(a.Bounds.Left, b.Bounds.Left);
                    double minW = Math.Min(a.Bounds.Width, b.Bounds.Width);
                    double gapY = Math.Abs(a.Bounds.CenterY - b.Bounds.CenterY);
                    if (overlapX >= minW * 0.5 && gapY <= Math.Max(26, Math.Max(a.Bounds.Width, b.Bounds.Width) * 0.85))
                    {
                        Union(i, j);
                        continue;
                    }
                }

                // 3. Dot above vertical stem ('i') or dots above/below a short horizontal bar ('÷').
                if (a.IsDot ^ b.IsDot)
                {
                    var dot = a.IsDot ? a : b;
                    var stem = a.IsDot ? b : a;
                    if (stem.IsVerticalLine && dot.Bounds.CenterX >= stem.Bounds.Left - 12 && dot.Bounds.CenterX <= stem.Bounds.Right + 12
                        && dot.Bounds.Bottom <= stem.Bounds.Top + 6 && stem.Bounds.Top - dot.Bounds.Bottom <= Math.Max(22, stem.Bounds.Height * 0.6))
                    {
                        Union(i, j);
                        continue;
                    }
                    if (stem.IsHorizontalLine && dot.Bounds.CenterX >= stem.Bounds.Left && dot.Bounds.CenterX <= stem.Bounds.Right
                        && Math.Abs(dot.Bounds.CenterY - stem.Bounds.CenterY) <= Math.Max(24, stem.Bounds.Width * 0.7))
                    {
                        Union(i, j);
                        continue;
                    }
                }

                // 4. 'π' top bar with two vertical legs underneath.
                if (a.IsHorizontalLine ^ b.IsHorizontalLine)
                {
                    var bar = a.IsHorizontalLine ? a : b;
                    var leg = a.IsHorizontalLine ? b : a;
                    if (leg.IsVerticalLine && leg.Bounds.CenterX >= bar.Bounds.Left - 4 && leg.Bounds.CenterX <= bar.Bounds.Right + 4
                        && leg.Bounds.Top >= bar.Bounds.Top - 8 && leg.Bounds.Top <= bar.Bounds.Bottom + 14)
                    {
                        Union(i, j);
                    }
                }
            }
        }

        var map = new Dictionary<int, List<MathInkStroke>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!map.TryGetValue(root, out var list)) map[root] = list = new List<MathInkStroke>();
            list.Add(strokes[i]);
        }
        return map.Values.ToList();
    }

    private static bool LooksLikeRadicalTouchingContent(MathInkStroke radical, MathInkStroke content)
    {
        if (radical.Bounds.Width < 24 || radical.Bounds.Height < 18) return false;
        return content.Bounds.Left > radical.Bounds.Left + radical.Bounds.Width * 0.25
            && content.Bounds.Right <= radical.Bounds.Right + 10
            && content.Bounds.Top >= radical.Bounds.Top - 4
            && content.Bounds.Bottom <= radical.Bounds.Bottom + 8;
    }

    private static string ParseSpatial(IReadOnlyList<RecognizedSymbol> items)
    {
        if (items.Count == 0) return "";
        var remaining = items.ToList();
        var tokens = new List<(InkBounds Bounds, string Text, bool IsOperator, bool IsAtom)>();

        double typicalHeight = Median(remaining
            .Where(s => s.Symbol is not ("-" or "=" or "." or "·"))
            .Select(s => s.Bounds.Height)
            .DefaultIfEmpty(36));

        // Step 1: Detect fractions (horizontal '-' bar with symbols above and below).
        var bars = remaining
            .Where(s => s.Symbol == "-" && s.Bounds.Width >= Math.Max(26, s.Bounds.Height * 2.2))
            .OrderByDescending(s => s.Bounds.Width)
            .ToArray();

        foreach (var bar in bars)
        {
            if (!remaining.Contains(bar)) continue;
            double padX = Math.Max(8, bar.Bounds.Width * 0.1);
            var above = remaining.Where(s => !ReferenceEquals(s, bar)
                && s.Bounds.CenterX >= bar.Bounds.Left - padX && s.Bounds.CenterX <= bar.Bounds.Right + padX
                && s.Bounds.Bottom <= bar.Bounds.CenterY + 6
                && bar.Bounds.CenterY - s.Bounds.Bottom <= Math.Max(typicalHeight * 2.6, bar.Bounds.Width * 1.4)).ToList();
            var below = remaining.Where(s => !ReferenceEquals(s, bar)
                && s.Bounds.CenterX >= bar.Bounds.Left - padX && s.Bounds.CenterX <= bar.Bounds.Right + padX
                && s.Bounds.Top >= bar.Bounds.CenterY - 6
                && s.Bounds.Top - bar.Bounds.CenterY <= Math.Max(typicalHeight * 2.6, bar.Bounds.Width * 1.4)).ToList();

            if (above.Count > 0 && below.Count > 0)
            {
                remaining.Remove(bar);
                foreach (var a in above) remaining.Remove(a);
                foreach (var b in below) remaining.Remove(b);
                var box = bar.Bounds;
                foreach (var a in above) box = box.Union(a.Bounds);
                foreach (var b in below) box = box.Union(b.Bounds);
                string num = ParseSpatial(above);
                string den = ParseSpatial(below);
                tokens.Add((box, $"({num})/({den})", false, true));
            }
        }

        // Step 2: Detect radicals ('√' enclosing symbols underneath its roof).
        var radicals = remaining.Where(s => s.Symbol == "√").OrderByDescending(s => s.Bounds.Width).ToArray();
        foreach (var rad in radicals)
        {
            if (!remaining.Contains(rad)) continue;
            var inside = remaining.Where(s => !ReferenceEquals(s, rad)
                && s.Bounds.CenterX >= rad.Bounds.Left + rad.Bounds.Width * 0.22
                && s.Bounds.CenterX <= rad.Bounds.Right + 10
                && s.Bounds.CenterY >= rad.Bounds.Top - 6
                && s.Bounds.CenterY <= rad.Bounds.Bottom + 12).ToList();
            if (inside.Count > 0)
            {
                remaining.Remove(rad);
                foreach (var item in inside) remaining.Remove(item);
                var box = rad.Bounds;
                foreach (var item in inside) box = box.Union(item.Bounds);
                string body = ParseSpatial(inside);
                tokens.Add((box, $"sqrt({body})", false, true));
            }
        }

        // Add remaining symbols as tokens.
        foreach (var sym in remaining)
        {
            string mapped = NormalizeSymbol(sym.Symbol);
            bool isOp = mapped is "+" or "-" or "*" or "/" or "=" or "<" or ">" or "<=" or ">=" or "!=" or "(" or ")" or "[" or "]";
            tokens.Add((sym.Bounds, mapped, isOp, !isOp || mapped == ")"));
        }

        tokens.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));

        // Step 3: Left-to-right assembly with superscript / subscript grouping.
        var output = new List<(string Text, bool IsOperator, bool IsAtom)>();
        int idx = 0;
        while (idx < tokens.Count)
        {
            var current = tokens[idx];
            idx++;

            if (current.IsAtom && !current.IsOperator && idx < tokens.Count)
            {
                // Check for superscript tokens to the upper-right of current base.
                var supTokens = new List<RecognizedSymbol>();
                var subTokens = new List<RecognizedSymbol>();
                double baseTop = current.Bounds.Top;
                double baseBottom = current.Bounds.Bottom;
                double baseHeight = Math.Max(14, current.Bounds.Height);
                double baseMidY = current.Bounds.CenterY;
                double cursorRight = current.Bounds.Right;

                while (idx < tokens.Count)
                {
                    var next = tokens[idx];
                    if (next.Bounds.Left - cursorRight > Math.Max(baseHeight * 1.4, 48)) break;
                    bool isSup = !next.IsOperator || next.Text is "-" or "+";
                    bool looksSup = isSup
                        && next.Bounds.CenterY < baseTop + baseHeight * 0.36
                        && next.Bounds.Bottom < baseMidY + baseHeight * 0.18
                        && next.Bounds.Height < baseHeight * 0.92;
                    bool looksSub = !next.IsOperator
                        && next.Bounds.CenterY > baseBottom - baseHeight * 0.34
                        && next.Bounds.Top > baseMidY - baseHeight * 0.12
                        && next.Bounds.Height < baseHeight * 0.85;

                    if (looksSup && subTokens.Count == 0)
                    {
                        supTokens.Add(new RecognizedSymbol(next.Text, 1, next.Bounds, Array.Empty<int>(), Array.Empty<string>()));
                        cursorRight = Math.Max(cursorRight, next.Bounds.Right);
                        idx++;
                    }
                    else if (looksSub && supTokens.Count == 0)
                    {
                        subTokens.Add(new RecognizedSymbol(next.Text, 1, next.Bounds, Array.Empty<int>(), Array.Empty<string>()));
                        cursorRight = Math.Max(cursorRight, next.Bounds.Right);
                        idx++;
                    }
                    else break;
                }

                string text = current.Text;
                if (supTokens.Count > 0)
                {
                    string sup = ParseSpatial(supTokens);
                    text = sup.Length == 1 ? $"{text}^{sup}" : $"{text}^({sup})";
                }
                else if (subTokens.Count > 0)
                {
                    string sub = ParseSpatial(subTokens);
                    text = $"{text}_{sub}";
                }
                output.Add((text, false, true));
            }
            else
            {
                output.Add((current.Text, current.IsOperator, current.IsAtom));
            }
        }

        // Step 4: Join tokens, recognize function names (sin, cos, tan, ln, log), and insert implicit multiplication.
        return AssembleExpression(output);
    }

    private static string AssembleExpression(List<(string Text, bool IsOperator, bool IsAtom)> tokens)
    {
        if (tokens.Count == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            // Try matching multi-letter function names like s-i-n, c-o-s, t-a-n, l-n, l-o-g.
            string matchedFunc = TryMatchFunction(tokens, i, out int consumed);
            if (matchedFunc is not null)
            {
                if (sb.Length > 0 && NeedsImplicitMultiply(sb[^1], matchedFunc[0])) sb.Append('*');
                sb.Append(matchedFunc);
                i += consumed - 1;
                if (i + 1 < tokens.Count && tokens[i + 1].Text != "(")
                {
                    sb.Append('(');
                    sb.Append(tokens[++i].Text);
                    sb.Append(')');
                }
                continue;
            }

            string part = tokens[i].Text;
            if (sb.Length > 0 && part.Length > 0 && NeedsImplicitMultiply(sb[^1], part[0]))
                sb.Append('*');
            sb.Append(part);
        }
        return sb.ToString();
    }

    private static string TryMatchFunction(List<(string Text, bool IsOperator, bool IsAtom)> tokens, int start, out int consumed)
    {
        consumed = 0;
        foreach (string fn in KnownFunctions)
        {
            if (start + fn.Length > tokens.Count) continue;
            bool match = true;
            for (int k = 0; k < fn.Length; k++)
            {
                if (tokens[start + k].Text.Length != 1 || char.ToLowerInvariant(tokens[start + k].Text[0]) != fn[k])
                { match = false; break; }
            }
            if (match) { consumed = fn.Length; return fn; }
        }
        return null;
    }

    private static bool NeedsImplicitMultiply(char prev, char next)
    {
        bool prevEndsAtom = char.IsAsciiDigit(prev) || prev == ')';
        bool nextStartsAtom = char.IsLetter(next) || next == '(';
        return prevEndsAtom && nextStartsAtom;
    }

    private static string NormalizeSymbol(string symbol) => symbol switch
    {
        "×" => "*",
        "÷" => "/",
        "−" => "-",
        "π" => "pi",
        "·" => "*",
        "≠" => "!=",
        "≤" => "<=",
        "≥" => ">=",
        _ => symbol,
    };

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 36 : sorted[sorted.Length / 2];
    }
}
