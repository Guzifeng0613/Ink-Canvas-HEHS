namespace InkCanvas.AnalyticGeometry.Handwriting;

/// <summary>
/// Built-in offline mathematical symbol recognizer combining normalized point-cloud matching ($P-style)
/// with structural geometric features (aspect ratio, stroke count, direction, closure, corners).
/// </summary>
public static class MathSymbolClassifier
{
    private const int SampleCount = 24;

    private sealed record SymbolTemplate(string Symbol, int StrokeCount, InkSample[] Points, double PreferredAspect, double AspectWeight);

    private static readonly SymbolTemplate[] Templates = BuildTemplates();

    public static IReadOnlyList<(string Symbol, double Score)> Classify(IReadOnlyList<MathInkStroke> strokes)
    {
        if (strokes.Count == 0) return Array.Empty<(string, double)>();
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        void Vote(string symbol, double score)
        {
            if (!scores.TryGetValue(symbol, out double existing) || score > existing) scores[symbol] = score;
        }

        var bounds = InkBounds.Empty;
        foreach (var s in strokes) bounds = bounds.Union(s.Bounds);
        double aspect = bounds.Width / Math.Max(1, bounds.Height);

        // Fast geometric shortcuts for unambiguous structural shapes.
        if (strokes.Count == 1)
        {
            var s = strokes[0];
            if (s.IsDot)
            {
                Vote(".", 0.96); Vote("·", 0.72);
                return Rank(scores);
            }
            if (s.IsHorizontalLine)
            {
                Vote("-", 0.96); Vote("1", 0.35);
                return Rank(scores);
            }
            if (s.IsVerticalLine)
            {
                Vote("1", 0.88); Vote("l", 0.72); Vote("|", 0.65); Vote("i", 0.52);
                return Rank(scores);
            }
            if (IsRadicalShape(s))
            {
                Vote("√", 0.95);
            }
        }
        else if (strokes.Count == 2)
        {
            var a = strokes[0];
            var b = strokes[1];
            if (a.IsHorizontalLine && b.IsHorizontalLine)
            {
                double verticalGap = Math.Abs(a.Bounds.CenterY - b.Bounds.CenterY);
                double width = Math.Max(a.Bounds.Width, b.Bounds.Width);
                if (verticalGap <= Math.Max(26, width * 0.9))
                {
                    Vote("=", 0.96); Vote("≠", 0.40);
                    return Rank(scores);
                }
            }
            if ((a.IsHorizontalLine && b.IsVerticalLine) || (b.IsHorizontalLine && a.IsVerticalLine))
            {
                var h = a.IsHorizontalLine ? a : b;
                var v = a.IsVerticalLine ? a : b;
                double relY = (h.Bounds.CenterY - v.Bounds.Top) / Math.Max(1, v.Bounds.Height);
                if (relY is >= 0.25 and <= 0.75) { Vote("+", 0.94); Vote("t", 0.78); Vote("x", 0.55); }
                else if (relY < 0.35) { Vote("t", 0.86); Vote("7", 0.74); Vote("+", 0.68); }
                else { Vote("+", 0.82); Vote("t", 0.65); }
            }
            if (IsYTwoStroke(a, b))
            {
                Vote("y", 0.95); Vote("x", 0.58); Vote("4", 0.45);
            }
            else if (IsCrossShape(a, b))
            {
                Vote("x", 0.93); Vote("×", 0.84); Vote("+", 0.62);
            }
            if ((a.IsDot && b.IsVerticalLine && a.Bounds.CenterY < b.Bounds.Top + 6)
                || (b.IsDot && a.IsVerticalLine && b.Bounds.CenterY < a.Bounds.Top + 6))
            {
                Vote("i", 0.94); Vote("1", 0.45);
                return Rank(scores);
            }
            if (IsFourTwoStroke(a, b))
            {
                Vote("4", 0.91); Vote("+", 0.55); Vote("t", 0.45);
            }
        }
        else if (strokes.Count == 3)
        {
            if (IsDivisionShape(strokes)) { Vote("÷", 0.95); Vote("=", 0.42); return Rank(scores); }
            if (IsPiShape(strokes)) { Vote("π", 0.94); Vote("n", 0.45); return Rank(scores); }
            if (IsNotEqualShape(strokes)) { Vote("≠", 0.93); Vote("=", 0.50); return Rank(scores); }
        }

        // General point-cloud template matching for all strokes (1..3 strokes).
        var normalized = NormalizeCloud(strokes);
        foreach (var template in Templates)
        {
            if (template.StrokeCount != strokes.Count && !(strokes.Count == 1 && template.StrokeCount == 1))
                continue;
            double distance = CloudDistance(normalized, template.Points);
            double aspectPenalty = template.AspectWeight * Math.Abs(Math.Log((aspect + 0.15) / (template.PreferredAspect + 0.15)));
            double score = Math.Exp(-1.55 * distance - 0.35 * aspectPenalty);
            Vote(template.Symbol, Math.Clamp(score, 0.05, 0.97));
        }

        // Ensure fallback candidates for single-stroke curves.
        if (scores.Count == 0) Vote("x", 0.3);
        return Rank(scores);
    }

    private static IReadOnlyList<(string Symbol, double Score)> Rank(Dictionary<string, double> scores)
        => scores.OrderByDescending(pair => pair.Value).Take(6).Select(pair => (pair.Key, pair.Value)).ToArray();

    private static bool IsRadicalShape(MathInkStroke s)
    {
        if (s.Points.Count < 4 || s.Bounds.Width < 18 || s.Bounds.Height < 14) return false;
        // Classic √: starts mid-left, drops to bottom-left-ish valley, climbs steeply to top, then extends right across the roof.
        int valleyIndex = 0;
        double maxY = double.NegativeInfinity;
        for (int i = 0; i < s.Points.Count; i++)
        {
            if (s.Points[i].Y > maxY) { maxY = s.Points[i].Y; valleyIndex = i; }
        }
        if (valleyIndex == 0 || valleyIndex >= s.Points.Count - 2) return false;
        int peakIndex = valleyIndex;
        double minY = double.PositiveInfinity;
        for (int i = valleyIndex; i < s.Points.Count; i++)
        {
            if (s.Points[i].Y < minY) { minY = s.Points[i].Y; peakIndex = i; }
        }
        if (peakIndex <= valleyIndex) return false;
        double startRelX = (s.Start.X - s.Bounds.Left) / Math.Max(1, s.Bounds.Width);
        double valleyRelX = (s.Points[valleyIndex].X - s.Bounds.Left) / Math.Max(1, s.Bounds.Width);
        double endRelX = (s.End.X - s.Bounds.Left) / Math.Max(1, s.Bounds.Width);
        double endRelY = (s.End.Y - s.Bounds.Top) / Math.Max(1, s.Bounds.Height);
        double roofLength = s.End.X - s.Points[peakIndex].X;
        return startRelX < 0.35 && valleyRelX < 0.55 && endRelX > 0.78 && endRelY < 0.35 && roofLength > s.Bounds.Width * 0.30;
    }

    private static bool IsCrossShape(MathInkStroke a, MathInkStroke b)
    {
        if (!MathInkStroke.SegmentsIntersect(a, b, 10)) return false;
        double diagA = DiagonalRatio(a);
        double diagB = DiagonalRatio(b);
        if (diagA < 0.35 || diagB < 0.35) return false;
        double slopeA = (a.End.Y - a.Start.Y) * (a.End.X - a.Start.X);
        double slopeB = (b.End.Y - b.Start.Y) * (b.End.X - b.Start.X);
        return slopeA * slopeB < 0; // opposite diagonal slopes
    }

    private static bool IsYTwoStroke(MathInkStroke a, MathInkStroke b)
    {
        // Two-stroke y: one short down-right stroke meeting a longer down-left stroke extending below.
        var longer = a.Bounds.Height >= b.Bounds.Height ? a : b;
        var shorter = ReferenceEquals(longer, a) ? b : a;
        if (longer.Bounds.Height < shorter.Bounds.Height * 1.18) return false;
        if (shorter.Bounds.Bottom > longer.Bounds.Bottom - longer.Bounds.Height * 0.15) return false;
        double slopeLong = (longer.End.X - longer.Start.X) * (longer.End.Y - longer.Start.Y);
        double slopeShort = (shorter.End.X - shorter.Start.X) * (shorter.End.Y - shorter.Start.Y);
        return slopeLong < 0 && slopeShort > 0 && MathInkStroke.SegmentsIntersect(a, b, 12);
    }

    private static bool IsFourTwoStroke(MathInkStroke a, MathInkStroke b)
    {
        var vertical = a.IsVerticalLine ? a : b.IsVerticalLine ? b : null;
        if (vertical is null) return false;
        var other = ReferenceEquals(vertical, a) ? b : a;
        // Other stroke is an L-shape (goes down-left then right across the vertical).
        bool startsHigh = other.Start.Y < other.Bounds.CenterY;
        bool endsRight = other.End.X > other.Bounds.CenterX;
        return startsHigh && endsRight && !other.IsHorizontalLine && MathInkStroke.SegmentsIntersect(a, b, 12);
    }

    private static bool IsDivisionShape(IReadOnlyList<MathInkStroke> strokes)
    {
        var bar = strokes.FirstOrDefault(s => s.IsHorizontalLine);
        if (bar is null) return false;
        var dots = strokes.Where(s => !ReferenceEquals(s, bar)).ToArray();
        return dots.Length == 2 && dots.All(d => d.IsDot || (d.Bounds.Width < bar.Bounds.Width * 0.45 && d.Bounds.Height < bar.Bounds.Width * 0.45))
            && ((dots[0].Bounds.CenterY < bar.Bounds.Top && dots[1].Bounds.CenterY > bar.Bounds.Bottom)
                || (dots[1].Bounds.CenterY < bar.Bounds.Top && dots[0].Bounds.CenterY > bar.Bounds.Bottom));
    }

    private static bool IsPiShape(IReadOnlyList<MathInkStroke> strokes)
    {
        var bar = strokes.FirstOrDefault(s => s.IsHorizontalLine);
        if (bar is null) return false;
        var legs = strokes.Where(s => !ReferenceEquals(s, bar)).ToArray();
        return legs.Length == 2 && legs.All(l => l.Bounds.Height > l.Bounds.Width * 0.9 && l.Bounds.CenterY > bar.Bounds.CenterY);
    }

    private static bool IsNotEqualShape(IReadOnlyList<MathInkStroke> strokes)
    {
        var bars = strokes.Where(s => s.IsHorizontalLine).ToArray();
        if (bars.Length != 2) return false;
        var slash = strokes.First(s => !bars.Contains(s));
        return MathInkStroke.SegmentsIntersect(slash, bars[0], 10) || MathInkStroke.SegmentsIntersect(slash, bars[1], 10);
    }

    private static double DiagonalRatio(MathInkStroke s)
    {
        double dx = Math.Abs(s.End.X - s.Start.X);
        double dy = Math.Abs(s.End.Y - s.Start.Y);
        double max = Math.Max(dx, dy);
        return max < 1 ? 0 : Math.Min(dx, dy) / max;
    }

    private static InkSample[] NormalizeCloud(IReadOnlyList<MathInkStroke> strokes)
    {
        var all = new List<InkSample>(SampleCount);
        double totalLength = strokes.Sum(s => Math.Max(1e-3, s.PathLength));
        int remaining = SampleCount;
        for (int i = 0; i < strokes.Count; i++)
        {
            int count = i == strokes.Count - 1 ? remaining : Math.Max(2, (int)Math.Round(SampleCount * Math.Max(1e-3, strokes[i].PathLength) / totalLength));
            count = Math.Min(count, remaining);
            remaining -= count;
            all.AddRange(Resample(strokes[i].Points, count));
        }
        return NormalizePoints(all);
    }

    private static InkSample[] Resample(IReadOnlyList<InkSample> points, int n)
    {
        if (n <= 1 || points.Count == 1) return Enumerable.Repeat(points[0], Math.Max(1, n)).ToArray();
        double total = 0;
        for (int i = 1; i < points.Count; i++) total += Distance(points[i - 1], points[i]);
        if (total < 1e-6) return Enumerable.Repeat(points[0], n).ToArray();
        double interval = total / (n - 1);
        var result = new List<InkSample>(n) { points[0] };
        double accumulated = 0;
        var prev = points[0];
        for (int i = 1; i < points.Count && result.Count < n; i++)
        {
            var curr = points[i];
            double d = Distance(prev, curr);
            if (accumulated + d >= interval)
            {
                double t = (interval - accumulated) / Math.Max(1e-9, d);
                var q = new InkSample(prev.X + t * (curr.X - prev.X), prev.Y + t * (curr.Y - prev.Y));
                result.Add(q);
                prev = q;
                accumulated = 0;
                i--;
            }
            else
            {
                accumulated += d;
                prev = curr;
            }
        }
        while (result.Count < n) result.Add(points[^1]);
        return result.ToArray();
    }

    private static InkSample[] NormalizePoints(IReadOnlyList<InkSample> points)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        double scale = Math.Max(1e-4, Math.Max(maxX - minX, maxY - minY));
        double cx = (minX + maxX) * 0.5;
        double cy = (minY + maxY) * 0.5;
        var output = new InkSample[points.Count];
        for (int i = 0; i < points.Count; i++)
            output[i] = new InkSample((points[i].X - cx) / scale, (points[i].Y - cy) / scale);
        return output;
    }

    private static double CloudDistance(InkSample[] a, InkSample[] b)
    {
        // Symmetric greedy point-cloud distance + directional order tie-breaker.
        double d1 = GreedyMatch(a, b);
        double d2 = GreedyMatch(b, a);
        double ordered = 0;
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) ordered += Distance(a[i], b[i]);
        ordered /= Math.Max(1, n);
        return Math.Min((d1 + d2) * 0.5, ordered) * 0.7 + ordered * 0.3;
    }

    private static double GreedyMatch(InkSample[] a, InkSample[] b)
    {
        bool[] matched = new bool[b.Length];
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double best = double.PositiveInfinity;
            int bestIndex = -1;
            for (int j = 0; j < b.Length; j++)
            {
                if (matched[j]) continue;
                double d = Distance(a[i], b[j]);
                if (d < best) { best = d; bestIndex = j; }
            }
            if (bestIndex >= 0) matched[bestIndex] = true;
            sum += best;
        }
        return sum / Math.Max(1, a.Length);
    }

    private static double Distance(InkSample a, InkSample b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static SymbolTemplate[] BuildTemplates()
    {
        var list = new List<SymbolTemplate>();
        void Add(string symbol, double aspect, double aspectWeight, params (double X, double Y)[][] strokes)
        {
            var inkStrokes = strokes.Select((pts, idx) => new MathInkStroke(idx, pts.Select(p => new InkSample(p.X, p.Y)).ToArray())).ToArray();
            list.Add(new SymbolTemplate(symbol, strokes.Length, NormalizeCloud(inkStrokes), aspect, aspectWeight));
        }

        // Digits 0-9
        Add("0", 0.72, 0.5, new[] { (0.5, 0.0), (0.15, 0.15), (0.05, 0.5), (0.15, 0.85), (0.5, 1.0), (0.85, 0.85), (0.95, 0.5), (0.85, 0.15), (0.5, 0.0) });
        Add("1", 0.25, 0.7, new[] { (0.5, 0.0), (0.5, 1.0) });
        Add("1", 0.35, 0.6, new[] { (0.3, 0.2), (0.55, 0.0), (0.55, 1.0) });
        Add("2", 0.75, 0.4, new[] { (0.15, 0.25), (0.35, 0.05), (0.7, 0.05), (0.85, 0.25), (0.65, 0.55), (0.1, 0.95), (0.9, 0.95) });
        Add("3", 0.70, 0.4, new[] { (0.15, 0.15), (0.55, 0.02), (0.85, 0.2), (0.45, 0.48), (0.88, 0.72), (0.55, 0.98), (0.15, 0.85) });
        Add("4", 0.75, 0.4, new[] { (0.65, 0.0), (0.1, 0.65), (0.95, 0.65) }, new[] { (0.68, 0.0), (0.68, 1.0) });
        Add("4", 0.75, 0.4, new[] { (0.35, 0.0), (0.15, 0.6), (0.95, 0.6) }, new[] { (0.7, 0.15), (0.7, 1.0) });
        Add("5", 0.75, 0.4, new[] { (0.85, 0.05), (0.2, 0.05), (0.15, 0.45), (0.6, 0.4), (0.88, 0.65), (0.6, 0.95), (0.15, 0.88) });
        Add("5", 0.75, 0.4, new[] { (0.2, 0.05), (0.15, 0.45), (0.6, 0.42), (0.88, 0.68), (0.55, 0.96), (0.15, 0.88) }, new[] { (0.2, 0.05), (0.88, 0.05) });
        Add("6", 0.70, 0.4, new[] { (0.75, 0.05), (0.35, 0.25), (0.12, 0.65), (0.3, 0.95), (0.75, 0.92), (0.85, 0.65), (0.55, 0.48), (0.15, 0.62) });
        Add("7", 0.72, 0.4, new[] { (0.1, 0.08), (0.9, 0.08), (0.35, 0.98) });
        Add("8", 0.68, 0.4, new[] { (0.5, 0.05), (0.18, 0.22), (0.5, 0.48), (0.82, 0.75), (0.5, 0.96), (0.18, 0.75), (0.5, 0.48), (0.82, 0.22), (0.5, 0.05) });
        Add("9", 0.70, 0.4, new[] { (0.82, 0.28), (0.55, 0.05), (0.18, 0.22), (0.22, 0.48), (0.62, 0.50), (0.82, 0.28), (0.75, 0.98) });

        // Common math variables & letters
        Add("x", 0.9, 0.3, new[] { (0.1, 0.1), (0.9, 0.9) }, new[] { (0.9, 0.1), (0.1, 0.9) });
        Add("y", 0.75, 0.3, new[] { (0.1, 0.1), (0.5, 0.55) }, new[] { (0.9, 0.1), (0.25, 1.0) });
        Add("y", 0.72, 0.3, new[] { (0.15, 0.1), (0.2, 0.45), (0.55, 0.5), (0.85, 0.1), (0.8, 0.95), (0.45, 0.98) });
        Add("z", 0.85, 0.3, new[] { (0.1, 0.1), (0.9, 0.1), (0.1, 0.9), (0.9, 0.9) });
        Add("a", 0.85, 0.3, new[] { (0.78, 0.25), (0.45, 0.1), (0.12, 0.45), (0.25, 0.88), (0.7, 0.75), (0.82, 0.25), (0.85, 0.92) });
        Add("b", 0.65, 0.3, new[] { (0.2, 0.02), (0.2, 0.95), (0.65, 0.92), (0.85, 0.65), (0.6, 0.42), (0.2, 0.55) });
        Add("c", 0.75, 0.4, new[] { (0.85, 0.2), (0.45, 0.08), (0.12, 0.5), (0.45, 0.92), (0.85, 0.82) });
        Add("e", 0.80, 0.3, new[] { (0.15, 0.52), (0.85, 0.48), (0.72, 0.15), (0.3, 0.15), (0.12, 0.52), (0.32, 0.88), (0.85, 0.82) });
        Add("s", 0.68, 0.3, new[] { (0.82, 0.15), (0.45, 0.05), (0.18, 0.25), (0.5, 0.5), (0.82, 0.72), (0.48, 0.95), (0.15, 0.85) });
        Add("n", 0.85, 0.3, new[] { (0.15, 0.92), (0.18, 0.25), (0.52, 0.15), (0.82, 0.35), (0.85, 0.92) });
        Add("t", 0.62, 0.4, new[] { (0.45, 0.05), (0.45, 0.88), (0.75, 0.92) }, new[] { (0.15, 0.35), (0.85, 0.35) });
        Add("f", 0.55, 0.4, new[] { (0.82, 0.12), (0.55, 0.05), (0.45, 0.35), (0.42, 0.95) }, new[] { (0.15, 0.45), (0.82, 0.45) });

        // Parentheses, brackets, inequalities, integral, radical
        Add("(", 0.38, 0.6, new[] { (0.75, 0.05), (0.35, 0.3), (0.25, 0.5), (0.35, 0.7), (0.75, 0.95) });
        Add(")", 0.38, 0.6, new[] { (0.25, 0.05), (0.65, 0.3), (0.75, 0.5), (0.65, 0.7), (0.25, 0.95) });
        Add("<", 0.65, 0.4, new[] { (0.85, 0.15), (0.15, 0.5), (0.85, 0.85) });
        Add(">", 0.65, 0.4, new[] { (0.15, 0.15), (0.85, 0.5), (0.15, 0.85) });
        Add("+", 0.95, 0.3, new[] { (0.5, 0.1), (0.5, 0.9) }, new[] { (0.1, 0.5), (0.9, 0.5) });
        Add("=", 1.4, 0.4, new[] { (0.1, 0.3), (0.9, 0.3) }, new[] { (0.1, 0.7), (0.9, 0.7) });
        Add("√", 1.2, 0.3, new[] { (0.05, 0.55), (0.18, 0.55), (0.32, 0.95), (0.48, 0.08), (0.95, 0.08) });
        Add("∫", 0.38, 0.6, new[] { (0.82, 0.08), (0.55, 0.04), (0.48, 0.5), (0.42, 0.95), (0.18, 0.92) });
        Add("∞", 1.7, 0.5, new[] { (0.5, 0.5), (0.25, 0.2), (0.08, 0.5), (0.25, 0.8), (0.5, 0.5), (0.75, 0.2), (0.92, 0.5), (0.75, 0.8), (0.5, 0.5) });
        Add("π", 1.1, 0.3, new[] { (0.08, 0.18), (0.92, 0.18) }, new[] { (0.32, 0.2), (0.28, 0.92) }, new[] { (0.7, 0.2), (0.72, 0.92) });

        return list.ToArray();
    }
}
