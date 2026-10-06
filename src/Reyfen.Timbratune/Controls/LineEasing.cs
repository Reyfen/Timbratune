using System.Diagnostics;
using Avalonia.Controls;
using Reyfen.Timbratune.Core.Analysis;

namespace Reyfen.Timbratune.Controls;

/// <summary>
/// Display-only easing for the live lines: when a chart gets a new line, the drawn line
/// glides from what was on screen to the new values over <see cref="EaseSeconds"/>, so
/// values that arrive in bursts or get corrected move smoothly instead of jumping.
/// NaN values are line breaks and are never eased.
/// </summary>
public sealed class LineEasing(Control owner)
{
    /// <summary>How long the drawn line takes to reach newly arrived values.</summary>
    public const double EaseSeconds = 0.25;
    /// <summary>A newly appended tail grows out of the old line's end when it starts within this (s).</summary>
    private const double ContinueSeconds = 0.5;

    private IReadOnlyList<TimedValue>? _from;
    private long _start;
    private bool _frameRequested;

    /// <summary>Call when the target line changes; <paramref name="old"/> is the previous target.</summary>
    public void Retarget(IReadOnlyList<TimedValue>? old)
    {
        // Start from what is on screen now (possibly mid-glide), not from the old target.
        _from = old is null ? null : Blend(_from, old, Progress());
        _start = Stopwatch.GetTimestamp();
        RequestFrame();
    }

    /// <summary>What to draw now for the target line.</summary>
    public IReadOnlyList<TimedValue> Current(IReadOnlyList<TimedValue> target) => Blend(_from, target, Progress());

    private double Progress() =>
        _from is null ? 1 : Math.Clamp(Stopwatch.GetElapsedTime(_start).TotalSeconds / EaseSeconds, 0, 1);

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(owner) is not { } top) return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            owner.InvalidateVisual();
            if (Progress() < 1) RequestFrame();
            else _from = null;
        });
    }

    /// <summary>The line <paramref name="to"/>, with each value moved <paramref name="p"/> of the way from <paramref name="from"/>'s value at that time.</summary>
    private static IReadOnlyList<TimedValue> Blend(IReadOnlyList<TimedValue>? from, IReadOnlyList<TimedValue> to, double p)
    {
        if (from is not { Count: > 0 } || p >= 1) return to;
        var e = 1 - Math.Pow(1 - p, 3); // ease-out
        var result = new TimedValue[to.Count];
        for (var i = 0; i < to.Count; i++)
        {
            var v = to[i].Value;
            if (!double.IsNaN(v) && ValueAt(from, to[i].T) is { } old) v = old + (v - old) * e;
            result[i] = new TimedValue(to[i].T, v);
        }
        return result;
    }

    /// <summary>The drawn value at time t: interpolated inside a stretch, or the stretch's end value just after it.</summary>
    private static double? ValueAt(IReadOnlyList<TimedValue> line, double t)
    {
        int lo = 0, hi = line.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (line[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        // lo = first index with T >= t
        if (lo < line.Count && line[lo].T == t) return double.IsNaN(line[lo].Value) ? null : line[lo].Value;
        var left = lo - 1;
        if (left < 0 || double.IsNaN(line[left].Value)) return null;
        if (lo < line.Count && !double.IsNaN(line[lo].Value))
        {
            var a = line[left];
            var b = line[lo];
            return a.Value + (b.Value - a.Value) * (t - a.T) / (b.T - a.T);
        }
        // Past the end of the line: new data continuing it grows out of its last value.
        return lo == line.Count && t - line[left].T <= ContinueSeconds ? line[left].Value : null;
    }
}
