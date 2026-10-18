namespace Reyfen.Timbratune.Acoustics.Formants;

/// <summary>A stretch of frames [<see cref="From"/>, <see cref="To"/>) of one formant analysis.</summary>
public readonly record struct FramePiece(int From, int To)
{
    public int Length => To - From;
}

/// <summary>
/// How smooth a formant analysis is over a short piece (Weenink 2015, "Improved formant frequency
/// measurements of short segments", Proc. 18th ICPhS): each of F1–F3 is modelled by a sum of
/// Legendre polynomials over the piece, fitted by least squares with weights 1/B² (B the
/// formant's bandwidth). The criterion combines the misfit χ² per degree of freedom d with the
/// mean variance s²/k of the k fitted coefficients: W = (s²/k)^t · χ²/d. A formant ceiling that
/// is too low or too high makes a pole jump between formant numbers, which a smooth model can't
/// follow (large χ²), or gives wide, uncertain resonances (large s²); the ceiling whose analysis
/// has the lowest W over a piece fits that piece best.
/// </summary>
public static class FormantSmoothness
{
    /// <summary>The formants modelled (F1–F3) and the Legendre terms per formant (P0–P2).</summary>
    public const int ModelledFormants = 3, Terms = 3;

    /// <summary>The power t on the coefficient variances (Weenink 2015: 1.25).</summary>
    public const double VariancePower = 1.25;

    /// <summary>
    /// Pieces are up to <see cref="PieceFrames"/> frames long and start every <see cref="PieceHop"/>
    /// frames, within voiced stretches of at least <see cref="MinStretchFrames"/> frames.
    /// </summary>
    public const int PieceFrames = 16, PieceHop = 8, MinStretchFrames = 8;

    /// <summary>
    /// W for frames [<paramref name="from"/>, <paramref name="to"/>) of one analysis; +∞ when the
    /// piece is too short, a modelled formant is missing in too many frames, or the fit is singular.
    /// </summary>
    public static double Criterion(IFormantFrames frames, int from, int to) => Criterion(frames, from, to, 0);

    /// <summary>
    /// The same with every bandwidth taken as at least <paramref name="bandwidthFloor"/> Hz in the weights 1/B² and the
    /// residuals: a pole locked on one harmonic of a low, pressed voice reports a bandwidth of 30–50 Hz, narrower than any
    /// vocal-tract resonance, and would otherwise earn a certainty it hasn't got (HANDOFF §8y). 0 = off.
    /// </summary>
    public static double Criterion(IFormantFrames frames, int from, int to, double bandwidthFloor)
    {
        var n = to - from;
        if (n < Terms + 3) return double.PositiveInfinity;
        var t0 = frames.FrameTime(from);
        var halfSpan = 0.5 * Math.Max(frames.FrameTime(to - 1) - t0, 1e-9);

        Span<double> normal = stackalloc double[Terms * Terms];
        Span<double> rhs = stackalloc double[Terms];
        Span<double> basis = stackalloc double[Terms];
        Span<double> coefficients = stackalloc double[Terms];
        Span<double> inverseDiagonal = stackalloc double[Terms];
        double misfit = 0, variance = 0;
        var degreesOfFreedom = 0;
        for (var formant = 0; formant < ModelledFormants; formant++)
        {
            // Weighted normal equations Aᵀ W A c = Aᵀ W f over the frames that have this formant.
            normal.Clear();
            rhs.Clear();
            var points = 0;
            for (var i = from; i < to; i++)
            {
                if (!TryFormant(frames.FrameAt(i), formant, out var f, out var b)) continue;
                b = Math.Max(b, bandwidthFloor);
                Legendre((frames.FrameTime(i) - t0) / halfSpan - 1, basis);
                var w = 1 / (b * b);
                for (var p = 0; p < Terms; p++)
                {
                    rhs[p] += w * basis[p] * f;
                    for (var q = 0; q <= p; q++) normal[p * Terms + q] += w * basis[p] * basis[q];
                }
                points++;
            }
            if (points < Terms + 2 || !SolveSymmetric(normal, rhs, coefficients, inverseDiagonal)) return double.PositiveInfinity;
            for (var p = 0; p < Terms; p++) variance += inverseDiagonal[p];

            for (var i = from; i < to; i++)
            {
                if (!TryFormant(frames.FrameAt(i), formant, out var f, out var b)) continue;
                b = Math.Max(b, bandwidthFloor);
                Legendre((frames.FrameTime(i) - t0) / halfSpan - 1, basis);
                var model = 0.0;
                for (var p = 0; p < Terms; p++) model += coefficients[p] * basis[p];
                var residual = (f - model) / b;
                misfit += residual * residual;
            }
            degreesOfFreedom += points - Terms;
        }
        return Math.Pow(variance / (ModelledFormants * Terms), VariancePower) * (misfit / degreesOfFreedom);
    }

    /// <summary>
    /// Appends to <paramref name="pieces"/> the pieces of an analysis whose frame i is voiced when
    /// <paramref name="voiced"/>[i] is: in each voiced stretch of at least <see cref="MinStretchFrames"/>
    /// frames, pieces of up to <see cref="PieceFrames"/> frames every <see cref="PieceHop"/> frames,
    /// in time order (the last piece of a stretch may be shorter).
    /// </summary>
    public static void Pieces(ReadOnlySpan<bool> voiced, List<FramePiece> pieces)
    {
        var i = 0;
        while (i < voiced.Length)
        {
            if (!voiced[i])
            {
                i++;
                continue;
            }
            var end = i + 1;
            while (end < voiced.Length && voiced[end]) end++;
            if (end - i >= MinStretchFrames)
                for (var start = i; start + PieceHop <= end; start += PieceHop)
                    pieces.Add(new FramePiece(start, Math.Min(start + PieceFrames, end)));
            i = end;
        }
    }

    private static bool TryFormant(ReadOnlySpan<FormantValue> frame, int formant, out double frequency, out double bandwidth)
    {
        if (formant >= frame.Length)
        {
            frequency = bandwidth = double.NaN;
            return false;
        }
        (frequency, bandwidth) = (frame[formant].Frequency, frame[formant].Bandwidth);
        return double.IsFinite(frequency) && bandwidth > 0;
    }

    /// <summary>The Legendre polynomials P0, P1, … at x ∈ [−1, 1] (Bonnet's recursion).</summary>
    private static void Legendre(double x, Span<double> p)
    {
        p[0] = 1;
        if (p.Length > 1) p[1] = x;
        for (var k = 2; k < p.Length; k++) p[k] = ((2 * k - 1) * x * p[k - 1] - (k - 1) * p[k - 2]) / k;
    }

    /// <summary>
    /// Solves M c = r for a symmetric positive definite M (its lower triangle given, row-major) by
    /// Cholesky factorisation, and returns the diagonal of M⁻¹ (the coefficient variances); false when
    /// M is not numerically positive definite.
    /// </summary>
    private static bool SolveSymmetric(Span<double> m, ReadOnlySpan<double> r, Span<double> c, Span<double> inverseDiagonal)
    {
        const int k = Terms;
        // In place: M = L Lᵀ, L in the lower triangle.
        var scale = 0.0;
        for (var i = 0; i < k; i++) scale = Math.Max(scale, m[i * k + i]);
        for (var j = 0; j < k; j++)
        {
            var d = m[j * k + j];
            for (var p = 0; p < j; p++) d -= m[j * k + p] * m[j * k + p];
            if (!(d > 1e-12 * scale)) return false;
            var l = Math.Sqrt(d);
            m[j * k + j] = l;
            for (var i = j + 1; i < k; i++)
            {
                var s = m[i * k + j];
                for (var p = 0; p < j; p++) s -= m[i * k + p] * m[j * k + p];
                m[i * k + j] = s / l;
            }
        }
        // L y = r, then Lᵀ c = y.
        for (var i = 0; i < k; i++)
        {
            var s = r[i];
            for (var p = 0; p < i; p++) s -= m[i * k + p] * c[p];
            c[i] = s / m[i * k + i];
        }
        for (var i = k - 1; i >= 0; i--)
        {
            var s = c[i];
            for (var p = i + 1; p < k; p++) s -= m[p * k + i] * c[p];
            c[i] = s / m[i * k + i];
        }
        // M⁻¹ = L⁻ᵀ L⁻¹, so (M⁻¹)ₑₑ is the squared length of column e of L⁻¹: solve L x = uₑ.
        Span<double> column = stackalloc double[k];
        for (var e = 0; e < k; e++)
        {
            var sum = 0.0;
            for (var i = e; i < k; i++)
            {
                var s = i == e ? 1.0 : 0.0;
                for (var p = e; p < i; p++) s -= m[i * k + p] * column[p];
                column[i] = s / m[i * k + i];
                sum += column[i] * column[i];
            }
            inverseDiagonal[e] = sum;
        }
        return true;
    }
}

/// <summary>
/// A running median (Tukey 1977) over <c>width</c> frames of each formant's frequency, centred and
/// clipped at the ends of the frames available. It removes label slips of a frame or two (a
/// spurious pole briefly taking a formant's number) and leaves steady or gliding tracks alone.
/// A frame keeps its own bandwidth where it has that formant, else takes its neighbours' median.
/// </summary>
public static class FormantMedian
{
    public const int DefaultWidth = 5;

    /// <summary>Smoothed frame <paramref name="index"/>, from frames [0, <paramref name="available"/>) only.</summary>
    public static FormantValue[] Frame(IFormantFrames frames, int index, int available, int width = DefaultWidth)
    {
        var from = Math.Max(0, index - width / 2);
        var to = Math.Min(available, index + width / 2 + 1);
        var most = 0;
        for (var i = from; i < to; i++) most = Math.Max(most, frames.FrameAt(i).Length);
        Span<double> frequencies = stackalloc double[width];
        Span<double> bandwidths = stackalloc double[width];
        Span<FormantValue> result = stackalloc FormantValue[most];
        var own = frames.FrameAt(index);
        var count = 0;
        for (; count < most; count++)
        {
            var n = 0;
            for (var i = from; i < to; i++)
            {
                var frame = frames.FrameAt(i);
                if (count >= frame.Length || !double.IsFinite(frame[count].Frequency)) continue;
                frequencies[n] = frame[count].Frequency;
                bandwidths[n] = frame[count].Bandwidth;
                n++;
            }
            // Formants are numbered from below: above the first one no neighbour has, none is known.
            if (n == 0) break;
            var bandwidth = count < own.Length ? own[count].Bandwidth : Median(bandwidths[..n]);
            result[count] = new FormantValue(Median(frequencies[..n]), bandwidth);
        }
        return result[..count].ToArray();
    }

    /// <summary>Every frame of <paramref name="contour"/> smoothed, on the same grid.</summary>
    public static FormantContour Smooth(FormantContour contour, int width = DefaultWidth)
    {
        var frames = new FormantValue[contour.FrameCount][];
        for (var i = 0; i < frames.Length; i++) frames[i] = Frame(contour, i, frames.Length, width);
        return new FormantContour(contour.Grid, frames);
    }

    private static double Median(Span<double> values)
    {
        values.Sort();
        var mid = values.Length / 2;
        return values.Length % 2 == 1 ? values[mid] : 0.5 * (values[mid - 1] + values[mid]);
    }
}
