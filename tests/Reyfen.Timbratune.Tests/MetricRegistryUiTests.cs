using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Features.LiveMonitor;
using Reyfen.Timbratune.Features.TakeDetails;
using Reyfen.Timbratune.Shared;

namespace Reyfen.Timbratune.Tests;

/// <summary>
/// The UI follows the registry (REFACTOR-PLAN §2.4, the P7 proof): a metric added as one module and
/// one registration shows up in the stat cards, the trend charts, the live timelines and the saved
/// series, with no change to any view model.
/// </summary>
public sealed class MetricRegistryUiTests
{
    /// <summary>A made-up metric: the slice length as a trend, the intensity frames as a series.</summary>
    private sealed class DummyModule : IMetricModule
    {
        public static readonly MetricInfo Dummy = new()
        {
            Name = "dummy", Title = "Dummy metric", Unit = "dB", Zones = Zones.Loudness, Lo = 45, Hi = 78,
            Take = r => r.Intensity.MeanDb,
            Columns = MetricColumns.Trend,
            Card = new(_ => "a test metric"),
            Trend = new("seconds per slice", "Chart1"),
            Live = new("every intensity frame", LiveSmoothing.Frame),
            Chip = new("dummy"),
        };

        private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(Dummy);

        public string Name => "dummy";
        public TrackSet RequiredTracks => TrackSet.Intensity;
        public IReadOnlyList<MetricInfo> Metrics => All;

        public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns) =>
            columns[Dummy] = window.End - window.Start;

        public void BuildSeries(in TrackView tracks, SeriesLines lines)
        {
            var line = lines[Dummy];
            line.Clear();
            for (var i = 0; i < tracks.Intensity.Db.Count; i++) line.Add(new(tracks.Intensity.Grid.IndexToX(i), tracks.Intensity.Db[i]));
        }

        public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series) =>
            series["dummy"] = new FrameSeries { Unit = "dB", T = lines[Dummy].Select(p => p.T).ToList() };
    }

    private sealed class NoReferenceVoices : IFeatureFlags
    {
        public bool ReferenceVoices => false;
    }

    private static Sound Fixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Reyfen.Timbratune.Core.Tests", "Fixtures", "vctk_f294.wav");
        using var s = File.OpenRead(path);
        return WavDecoder.Decode(s);
    }

    [Fact]
    public void ADummyMetricShowsUpEverywhere()
    {
        var registry = new MetricRegistry([.. MetricRegistry.Default.Modules, new DummyModule()]);
        var dummy = registry.Id(DummyModule.Dummy);
        var sound = Fixture();

        // Live: the analysis fills its series, and the timelines draw it.
        var live = new LiveAnalyzer(sound.SamplingFrequency, registry: registry);
        live.Append(sound.Channel(0).ToArray());
        var snapshot = live.Update(final: true);
        Assert.NotEmpty(snapshot.Series[dummy]);
        var lines = LiveLine.From(registry);
        var timelines = new LiveTimelinesViewModel(lines);
        timelines.Apply(LiveTimelinesViewModel.Compute(snapshot, lines));
        var timeline = Assert.Single(timelines.Timelines, t => t.Title == "Dummy metric");
        Assert.NotEmpty(timeline.Line);

        // The take: a stat card and a trend chart with its column.
        var result = snapshot.Result!;
        Assert.All(result.Detail.Trends!.Points, p => Assert.True(p[dummy] > 0));
        var take = new TakeViewModel(result.Metrics, result.Detail, isLatest: true, new RelayCommand<MetricId>(_ => { }),
            new NoReferenceVoices(), registry);
        var card = Assert.Single(take.StatCards, c => c.Title == "Dummy metric");
        Assert.Equal(result.Metrics.Intensity.MeanDb, card.Value);
        var trend = Assert.Single(take.PhraseTrends, t => t.Title == "Dummy metric");
        Assert.All(trend.Points, p => Assert.NotNull(p.Y));

        // The saved series.
        var series = FrameSeriesBuilder.ForTake(SavedTrackProvider.Measure(sound, registry.RequiredTracks), registry);
        Assert.NotEmpty(series.Series["dummy"].T);

        // The app's own registry doesn't have it.
        Assert.Null(MetricRegistry.Default.Find("dummy"));
    }
}
