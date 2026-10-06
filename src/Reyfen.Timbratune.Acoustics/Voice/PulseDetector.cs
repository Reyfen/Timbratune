using Reyfen.Timbratune.Acoustics.Pitch;

namespace Reyfen.Timbratune.Acoustics.Voice;

/// <summary>
/// Glottal pulse (period mark) detection by waveform matching, as described in
/// the Praat manual (Sound &amp; Pitch: To PointProcess (cc)): in every voiced
/// stretch, start at the absolute extremum nearest its middle, then walk
/// backwards and forwards one period at a time, placing each next mark where a
/// one-period window best cross-correlates with the window at the current mark.
/// </summary>
public static class PulseDetector
{
    /// <returns>Sorted pulse times in seconds.</returns>
    public static double[] PeriodicCrossCorrelation(Sound sound, PitchContour pitch)
    {
        var points = new SortedSet<double>();
        var globalPeak = AbsolutePeak(sound);
        var addedRight = -1e308;
        foreach (var (tleft, tright) in VoicedStretches(pitch))
            if (!DetectStretch(sound, pitch, tleft, tright, globalPeak, points, ref addedRight)) break;
        return [.. points];
    }

    /// <summary>Largest |sample| over all channels: the reference for the pulse-acceptance levels.</summary>
    public static double AbsolutePeak(Sound sound)
    {
        var peak = 0.0;
        for (var ch = 0; ch < sound.ChannelCount; ch++)
            foreach (var s in sound.Channel(ch)) peak = Math.Max(peak, Math.Abs(s));
        return peak;
    }

    /// <summary>The voiced stretches in order, each widened by half a frame on both sides.</summary>
    public static IEnumerable<(double Left, double Right)> VoicedStretches(PitchContour pitch)
    {
        var t = pitch.Grid.XMin;
        while (NextVoicedInterval(pitch, t, out var tleft, out var tright))
        {
            yield return (tleft, tright);
            t = tright;
        }
    }

    /// <summary>
    /// Places the pulses of one voiced stretch into <paramref name="points"/>.
    /// <paramref name="addedRight"/> carries the last pulse added past a stretch's end
    /// into the next stretch (so a short gap is not filled twice); start with −1e308.
    /// </summary>
    /// <returns>False if the stretch has no pitch in its middle (never for a genuine voiced stretch).</returns>
    public static bool DetectStretch(Sound sound, PitchContour pitch, double tleft, double tright, double globalPeak,
        SortedSet<double> points, ref double addedRight)
    {
        {
            var tmiddle = 0.5 * (tleft + tright);
            var f0Middle = pitch.ValueAtTime(tmiddle);
            if (double.IsNaN(f0Middle)) return false; // cannot happen for a voiced stretch; stay safe
            var tmax = FindExtremum(sound, tmiddle - 0.5 / f0Middle, tmiddle + 0.5 / f0Middle);
            points.Add(tmax);
            var tsave = tmax;

            // Backwards.
            while (true)
            {
                var f0 = pitch.ValueAtTime(tmax);
                if (double.IsNaN(f0)) break;
                var correlation = FindMaximumCorrelation(sound, tmax, 1.0 / f0, tmax - 1.25 / f0, tmax - 0.8 / f0, ref tmax, out var peak);
                if (correlation == -1.0) tmax -= 1.0 / f0; // no match: skip one period
                if (tmax < tleft)
                {
                    if (correlation > 0.7 && peak > 0.023333 * globalPeak && tmax - addedRight > 0.8 / f0) points.Add(tmax);
                    break;
                }
                if (correlation > 0.3 && (peak == 0.0 || peak > 0.01 * globalPeak) && tmax - addedRight > 0.8 / f0)
                    points.Add(tmax); // don't fill a short unvoiced gap twice
            }

            // Forwards.
            tmax = tsave;
            while (true)
            {
                var f0 = pitch.ValueAtTime(tmax);
                if (double.IsNaN(f0)) break;
                var correlation = FindMaximumCorrelation(sound, tmax, 1.0 / f0, tmax + 0.8 / f0, tmax + 1.25 / f0, ref tmax, out var peak);
                if (correlation == -1.0) tmax += 1.0 / f0;
                if (tmax > tright)
                {
                    if (correlation > 0.7 && peak > 0.023333 * globalPeak)
                    {
                        points.Add(tmax);
                        addedRight = tmax;
                    }
                    break;
                }
                if (correlation > 0.3 && (peak == 0.0 || peak > 0.01 * globalPeak))
                {
                    points.Add(tmax);
                    addedRight = tmax;
                }
            }
        }
        return true;
    }

    /// <summary>The first run of voiced frames starting at or after <paramref name="after"/>, widened by half a frame each side.</summary>
    private static bool NextVoicedInterval(PitchContour pitch, double after, out double tleft, out double tright)
    {
        tleft = tright = 0;
        var grid = pitch.Grid;
        var i = Math.Max(0, grid.HighIndex(after));
        if (i >= grid.Count) return false;
        while (i < grid.Count && !pitch.IsVoiced(i)) i++;
        if (i >= grid.Count) return false;
        var last = i;
        while (last + 1 < grid.Count && pitch.IsVoiced(last + 1)) last++;

        tleft = grid.IndexToX(i) - 0.5 * grid.Step;
        tright = grid.IndexToX(last) + 0.5 * grid.Step;
        if (tleft >= grid.XMax - 0.5 * grid.Step) return false;
        tleft = Math.Max(tleft, grid.XMin);
        tright = Math.Min(tright, grid.XMax);
        return tright > after;
    }

    /// <summary>Time of the largest |sample| in [tmin, tmax] (channel 1 and 2 averaged), parabolically refined.</summary>
    private static double FindExtremum(Sound sound, double tmin, double tmax)
    {
        var grid = sound.Grid;
        var from = Math.Max(0, grid.LowIndex(tmin));
        var to = Math.Min(grid.Count - 1, grid.HighIndex(tmax));
        var n = to - from + 1;
        double Value(int i) => sound.ChannelCount > 1 ? 0.5 * (sound.Channel(0)[i] + sound.Channel(1)[i]) : sound.Channel(0)[i];

        double position; // 0-based fractional offset from `from`
        if (n <= 0) return 0.5 * (tmin + tmax);
        if (n == 1) position = 0;
        else if (n == 2)
        {
            double a = Math.Abs(Value(from)), b = Math.Abs(Value(from + 1));
            position = a > b ? 0 : a < b ? 1 : 0.5;
        }
        else
        {
            int iMin = 0, iMax = 0;
            double min = Value(from), max = min;
            for (var k = 1; k < n; k++)
            {
                var v = Value(from + k);
                if (v < min) { min = v; iMin = k; }
                if (v > max) { max = v; iMax = k; }
            }
            if (min == max) position = 0.5 * (n - 1);
            else
            {
                var extreme = Math.Abs(min) > Math.Abs(max) ? iMin : iMax;
                if (extreme == 0 || extreme == n - 1) position = extreme;
                else
                {
                    double left = Value(from + extreme - 1), mid = Value(from + extreme), right = Value(from + extreme + 1);
                    position = extreme + 0.5 * (right - left) / (2 * mid - left - right);
                }
            }
        }
        return grid.First + (from + position) * grid.Step;
    }

    /// <summary>
    /// The time in [tmin2, tmax2] whose one-window neighbourhood correlates best with the
    /// window around t1; returns the correlation (−1 if none found) and the peak |sample|
    /// of the matching window.
    /// </summary>
    private static double FindMaximumCorrelation(Sound sound, double t1, double windowLength, double tmin2, double tmax2,
        ref double tout, out double peak)
    {
        var grid = sound.Grid;
        var n = grid.Count;
        var half = 0.5 * windowLength;
        // 1-based sample numbers throughout, matching the time conversion below.
        var left1 = grid.NearestIndex(t1 - half) + 1;
        var right1 = grid.NearestIndex(t1 + half) + 1;
        var left2Min = grid.LowIndex(tmin2 - half) + 1;
        var left2Max = grid.HighIndex(tmax2 - half) + 1;

        peak = 0;
        var best = -1.0;
        double r1Best = double.NaN, r3Best = double.NaN, bestLeft = double.NaN;
        double r1 = 0, r2 = 0, r3 = 0;
        for (var left2 = left2Min; left2 <= left2Max; left2++)
        {
            double norm1 = 0, norm2 = 0, product = 0, localPeak = 0;
            for (var ch = 0; ch < sound.ChannelCount; ch++)
            {
                var z = sound.Channel(ch);
                for (int i1 = left1, i2 = left2; i1 <= right1; i1++, i2++)
                {
                    if (i1 < 1 || i1 > n || i2 < 1 || i2 > n) continue;
                    double a = z[i1 - 1], b = z[i2 - 1];
                    norm1 += a * a;
                    norm2 += b * b;
                    product += a * b;
                    localPeak = Math.Max(localPeak, Math.Abs(b));
                }
            }
            r1 = r2;
            r2 = r3;
            r3 = norm1 == 0 || norm2 == 0 ? 0 : product / Math.Sqrt(norm1 * norm2);
            if (r2 > best && r2 >= r1 && r2 >= r3)
            {
                r1Best = r1;
                best = r2;
                r3Best = r3;
                bestLeft = left2 - 1; // r2 belongs to the previous start
                peak = localPeak;
            }
        }

        if (best > -1.0)
        {
            var interpolatedHeight = double.NaN;
            var curvature = (best - r1Best) + (best - r3Best);
            if (curvature != 0)
            {
                var slope = 0.5 * (r3Best - r1Best);
                interpolatedHeight = best + 0.5 * slope * slope / curvature;
                bestLeft += slope / curvature;
            }
            var peakTime = t1 + (bestLeft - left1) * grid.Step;
            if (peakTime >= tmin2 && peakTime <= tmax2)
            {
                tout = peakTime;
                if (!double.IsNaN(interpolatedHeight)) best = interpolatedHeight;
            }
            else
            {
                // Fall back to the geometric middle of the search range (one period for 0.8–1.25 × T).
                var distance = Math.Sqrt((tmin2 - t1) * (tmax2 - t1));
                tout = tmin2 < t1 ? t1 - distance : t1 + distance;
                if (r3 > best) best = r3;
            }
        }
        return best;
    }
}
