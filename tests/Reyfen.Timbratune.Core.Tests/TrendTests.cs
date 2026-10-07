using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>
/// The "trends within this take" slices and the analysis progress reports.
/// </summary>
public sealed class TrendTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData(0.5, 1)]
    [InlineData(9.5, 1)]
    [InlineData(10.9, 1)]
    [InlineData(11, 2)]
    [InlineData(27.6, 3)]
    [InlineData(60, 6)]
    [InlineData(100, 10)]
    [InlineData(110, 12)]
    [InlineData(600, 60)]
    public void StepIsTheShortestRoundLengthGivingAtMostTenPoints(double duration, int step)
    {
        Assert.Equal(step, AnalysisPostProcessor.TrendStep(duration));
        Assert.True(Math.Floor(duration / step) <= AnalysisPostProcessor.TrendPointTarget);
    }

    [Theory]
    [MemberData(nameof(ParityTests.Clips), MemberType = typeof(ParityTests))]
    public async Task SlicesSitOnWholeSecondsAndCoverTheTake(string clip)
    {
        var detail = (await new AcousticsAnalysisEngine().AnalyzeAsync(Fixture(clip + ".wav"))).Detail;
        var trends = detail.Trends!;
        var step = AnalysisPostProcessor.TrendStep(detail.DurationS);
        Assert.Equal(step, trends.StepS);
        Assert.Equal(TakeTrends.CurrentVersion, trends.Version);
        Assert.Equal((int)Math.Floor(detail.DurationS / step), trends.Points.Count);

        for (var k = 0; k < trends.Points.Count; k++)
        {
            var p = trends.Points[k];
            Assert.Equal((k + 1) * step, p.T);
            Assert.InRange(p.T, p.Start, p.End);
            if (k > 0) Assert.Equal(trends.Points[k - 1].End, p.Start);
        }
        Assert.Equal(0, trends.Points[0].Start);
        Assert.Equal(detail.DurationS, trends.Points[^1].End, 3);

        // Every measure is present somewhere (slices that fall in a pause have none).
        Assert.True(trends.Points.Count(p => p.MeanHz is not null) > trends.Points.Count / 2);
        Assert.Contains(trends.Points, p => p.F2Hz is not null);
        Assert.Contains(trends.Points, p => p.F3Hz is not null);
        Assert.Contains(trends.Points, p => p.WeightDb is not null);
        Assert.Contains(trends.Points, p => p.HnrDb is not null);
        Assert.Contains(trends.Points, p => p.JitterPct is not null);
        Assert.Contains(trends.Points, p => p.PitchSdHz is not null);
        // Loudness covers every frame, silence included, so every slice has it.
        Assert.All(trends.Points, p => Assert.InRange(p.LoudnessDb!.Value, 20, 100));
    }

    [Fact]
    public async Task OlderTakesGetPitchTrendsFromTheContour()
    {
        var detail = (await new AcousticsAnalysisEngine().AnalyzeAsync(Fixture("vctk_f294.wav"))).Detail;
        var rebuilt = AnalysisPostProcessor.TimeTrends(detail);
        Assert.Equal(detail.Trends!.Points.Select(p => p.MeanHz), rebuilt.Points.Select(p => p.MeanHz));
        Assert.Equal(detail.Trends.Points.Select(p => p.MelodySt), rebuilt.Points.Select(p => p.MelodySt));
        Assert.All(rebuilt.Points, p => Assert.Null(p.F2Hz));
    }

    [Fact]
    public async Task ProgressOnlyGrowsAndEndsAtOne()
    {
        var reports = new List<double>();
        await new AcousticsAnalysisEngine().AnalyzeAsync(Fixture("vctk_f294.wav"), progress: new Recorder(reports));
        Assert.True(reports.Count > 20, $"only {reports.Count} reports");
        Assert.All(reports.Zip(reports.Skip(1)), p => Assert.True(p.Second > p.First));
        Assert.Equal(1, reports[^1], 9);
    }

    private sealed class Recorder(List<double> into) : IProgress<double>
    {
        public void Report(double value)
        {
            lock (into) into.Add(value);
        }
    }
}
