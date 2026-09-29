using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Pitch;

/// <summary>
/// Harmonics-to-noise ratio per frame in dB (Boersma 1993, §4): with r the
/// normalized correlation at the period, HNR = 10·log10(r / (1 − r)).
/// Silent (unvoiced) frames hold −200 dB and are ignored by <see cref="Mean"/>.
/// </summary>
public sealed class HarmonicityContour
{
    public const double Unvoiced = -200;

    internal HarmonicityContour(TimeGrid grid, double[] db)
    {
        Grid = grid;
        Db = db;
    }

    public TimeGrid Grid { get; }
    public IReadOnlyList<double> Db { get; }

    /// <summary>Mean HNR (dB) over the frames that are not <see cref="Unvoiced"/>; NaN if there are none.</summary>
    public double Mean()
    {
        var voiced = Db.Where(v => v != Unvoiced).ToArray();
        return voiced.Length == 0 ? double.NaN : Stats.Mean(voiced);
    }
}

public static class HarmonicityAnalyzer
{
    /// <summary>
    /// Cross-correlation harmonicity (Praat: Sound: To Harmonicity (cc)…):
    /// the strongest cross-correlation peak per frame, no path smoothing.
    /// </summary>
    /// <param name="timeStep">Seconds between frames, e.g. 0.01.</param>
    /// <param name="minimumPitch">Lowest periodicity searched for (Hz), e.g. 75.</param>
    /// <param name="silenceThreshold">Frames quieter than this fraction of the global peak count as silent, e.g. 0.1.</param>
    /// <param name="periodsPerWindow">Window length in periods of the minimum pitch, e.g. 1.0.</param>
    public static HarmonicityContour CrossCorrelation(Sound sound, double timeStep = 0.01, double minimumPitch = 75,
        double silenceThreshold = 0.1, double periodsPerWindow = 1.0)
    {
        var settings = new PitchSettings
        {
            MaxCandidates = 15,
            SilenceThreshold = silenceThreshold,
            VoicingThreshold = 0,
            OctaveCost = 0,
            OctaveJumpCost = 0,
            VoicedUnvoicedCost = 0,
        };
        var pitch = PitchAnalyzer.Analyze(sound, PitchAnalyzer.Method.CrossCorrelationAccurate, periodsPerWindow, timeStep,
            minimumPitch, 0.5 * sound.SamplingFrequency, settings);

        var db = new double[pitch.FrameCount];
        for (var i = 0; i < db.Length; i++)
        {
            var best = pitch.Frames[i].Best;
            if (best.Frequency == 0) db[i] = HarmonicityContour.Unvoiced;
            else
            {
                var r = best.Strength;
                db[i] = r <= 1e-15 ? -150 : r > 1 - 1e-15 ? 150 : 10 * Math.Log10(r / (1 - r));
            }
        }
        return new HarmonicityContour(pitch.Grid, db);
    }
}
