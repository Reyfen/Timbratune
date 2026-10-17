using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.App;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared.State;
using Reyfen.Timbratune.Shared.Telemetry;

namespace Reyfen.Timbratune.Tests;

/// <summary>REFACTOR-PLAN §5: telemetry is off by default and costs nothing then; consent, rollout and limits.</summary>
public sealed class TelemetryTests
{
    [Fact]
    public void TheAppStartsWithEverythingOff()
    {
        var services = new ServiceCollection();
        services.AddTimbratune();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "timbratune-telemetry-" + Guid.NewGuid().ToString("N"))));
        using var provider = services.BuildServiceProvider();
        Assert.Same(NullAnalytics.Instance, provider.GetRequiredService<IAnalytics>());
        Assert.Same(NullCrashReporter.Instance, provider.GetRequiredService<ICrashReporter>());
        Assert.Same(NullAppLog.Instance, provider.GetRequiredService<IAppLog>());
        Assert.False(provider.GetRequiredService<IEntitlements>().HasEverDonated);
        var consent = provider.GetRequiredService<TelemetryConsent>();
        Assert.False(consent.AnalyticsOn);
        Assert.False(consent.CrashReportsOn);
        Assert.Equal("", consent.InstallId);
    }

    [Fact]
    public void TheCallSitesAllocateNothingWhenOff()
    {
        IAnalytics analytics = NullAnalytics.Instance;
        ICrashReporter crashes = NullCrashReporter.Instance;
        IAppLog log = NullAppLog.Instance;
        var take = new Recording();
        var error = new InvalidOperationException();
        void CallSites(int i)
        {
            if (analytics.IsEnabled) analytics.TakeAnalyzed(new(TakeSource.Recorded, take, i));
            if (crashes.IsEnabled) crashes.Report(error, "test");
            log.Log(AppLogLevel.Info, "test", $"take {i} took {i * 0.5:0.0} s");
            log.Log(AppLogLevel.Warning, "test", error, $"failed after {i} tries");
        }
        CallSites(0); // JIT
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) CallSites(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ALogFormatsOnlyTheLevelsItKeepsInTheInvariantCulture()
    {
        var log = new ListLog(AppLogLevel.Warning);
        var formatted = new CountingValue();
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            log.Log(AppLogLevel.Info, "a", $"skipped {formatted}");
            log.Log(AppLogLevel.Warning, "b", $"kept {1.5:0.0} {formatted}");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
        Assert.Equal(1, formatted.Count);
        Assert.Equal([(AppLogLevel.Warning, "b", "kept 1.5 value")], log.Lines);
    }

    [Fact]
    public void TheRolloutBucketIsStable()
    {
        // FNV-1a, so it is the same on every run, platform and version (string.GetHashCode isn't).
        Assert.Equal(8325, TelemetryRollout.BucketOf("0123456789abcdef0123456789abcdef"));
    }

    [Fact]
    public void TheRolloutTakesTheRateAndDonorsAlways()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => Guid.NewGuid().ToString("N")).ToList();
        var quarter = ids.Where(id => TelemetryRollout.Includes(id, hasEverDonated: false, 0.25)).ToHashSet();
        Assert.InRange(quarter.Count / (double)ids.Count, 0.23, 0.27);
        // Raising the rate only adds installs.
        var half = ids.Where(id => TelemetryRollout.Includes(id, false, 0.5)).ToHashSet();
        Assert.Subset(half, quarter);
        Assert.InRange(half.Count / (double)ids.Count, 0.48, 0.52);
        Assert.DoesNotContain(ids, id => TelemetryRollout.Includes(id, false, 0));
        Assert.All(ids, id => Assert.True(TelemetryRollout.Includes(id, false, 1)));
        Assert.All(ids, id => Assert.True(TelemetryRollout.Includes(id, hasEverDonated: true, 0)));
    }

    [Fact]
    public void NothingIsSentWithoutConsentWhateverTheRollout()
    {
        var consent = new TelemetryConsent(new MemorySettings(), new Donor(true), new RolloutRates(1, 1));
        Assert.False(consent.AnalyticsOn);
        Assert.False(consent.CrashReportsOn);
    }

    [Fact]
    public void TheInstallIdComesWithTheFirstOptInAndGoesWithTheLast()
    {
        var settings = new MemorySettings();
        var consent = new TelemetryConsent(settings, NoEntitlements.Instance, new RolloutRates(1, 1));
        consent.SetCrashReports(true);
        var id = consent.InstallId;
        Assert.Equal(32, id.Length);
        Assert.True(consent.CrashReportsOn);
        Assert.False(consent.AnalyticsOn);
        consent.SetAnalytics(true);
        Assert.Equal(id, consent.InstallId);
        Assert.True(consent.AnalyticsOn);
        consent.SetCrashReports(false);
        Assert.Equal(id, consent.InstallId);
        consent.SetAnalytics(false);
        Assert.Equal("", consent.InstallId);
        Assert.False(consent.AnalyticsOn);
    }

    [Fact]
    public void OutsideTheRolloutOnlyDonorsSend()
    {
        var settings = new MemorySettings();
        var none = new TelemetryConsent(settings, NoEntitlements.Instance, new RolloutRates(0, 0));
        none.SetAnalytics(true);
        none.SetCrashReports(true);
        Assert.False(none.AnalyticsOn);
        Assert.False(none.CrashReportsOn);
        var donor = new TelemetryConsent(settings, new Donor(true), new RolloutRates(0, 0));
        Assert.True(donor.AnalyticsOn);
        Assert.True(donor.CrashReportsOn);
    }

    [Fact]
    public void TheLimiterSendsEachKindOnceAndAFewPerSession()
    {
        var inner = new ListReporter();
        var limiter = new CrashReportLimiter(inner, maxPerSession: 3);
        var first = Thrown(() => Fail("a"));
        limiter.Report(first, "x");
        limiter.Report(Thrown(() => Fail("a again")), "x"); // the same kind: same area, type and method
        limiter.Report(first, "y");
        limiter.Report(Thrown(() => throw new ArgumentException()), "x");
        limiter.Report(Thrown(() => throw new FormatException()), "x"); // over the limit
        Assert.Equal(["x", "y", "x"], inner.Areas);
        Assert.False(new CrashReportLimiter(NullCrashReporter.Instance).IsEnabled);
    }

    private static void Fail(string message) => throw new InvalidOperationException(message);

    [Fact]
    public void ATakesPayloadHoldsZoneNamesOnly()
    {
        var take = new Recording { Label = "my voice", Note = "private", SourceFile = "secret.wav", DurationS = 27.4 };
        take.Pitch.MeanHz = 190;
        take.Pitch.SdHz = 10;
        take.Intensity.MeanDb = 90; // above the last zone: counts as the last
        var zones = AnalyticsPayload.Zones(take, MetricRegistry.Default);
        Assert.Contains(("pitch", "fem"), zones);
        Assert.Contains(("pitch_sd", "flat"), zones);
        Assert.Contains(zones, z => z.Zone == "strong");
        Assert.DoesNotContain(zones, z => z.Metric == "pitch_range"); // no zones
        var names = MetricRegistry.Default.Metrics.Select(m => m.Info.Name).ToHashSet();
        Assert.All(zones, z => Assert.Contains(z.Metric, names));
        Assert.All(zones, z => Assert.DoesNotMatch("[0-9]", z.Zone));
        Assert.Equal(25, AnalyticsPayload.DurationBucket(take));
        Assert.Equal(2.1, AnalyticsPayload.ProcessingRounded(2.0634));
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }
        throw new InvalidOperationException("didn't throw");
    }

    private sealed class CountingValue
    {
        public int Count { get; private set; }

        public override string ToString()
        {
            Count++;
            return "value";
        }
    }

    private sealed class ListLog(AppLogLevel min) : IAppLog
    {
        public List<(AppLogLevel, string, string)> Lines { get; } = [];

        public bool IsEnabled(AppLogLevel level) => level >= min;

        public void Write(AppLogLevel level, string area, string message, Exception? error = null) => Lines.Add((level, area, message));
    }

    private sealed class ListReporter : ICrashReporter
    {
        public List<string> Areas { get; } = [];

        public bool IsEnabled => true;

        public void Report(Exception error, string area) => Areas.Add(area);
    }

    private sealed class Donor(bool donated) : IEntitlements
    {
        public bool HasEverDonated => donated;
    }

    private sealed class MemorySettings : ISettings
    {
        private readonly Dictionary<string, object?> _values = [];

        public event Action<ISettingKey>? Changed;

        public T Get<T>(SettingKey<T> key) => _values.TryGetValue(key.Id, out var v) ? (T)v! : key.Default;

        public void Set<T>(SettingKey<T> key, T value)
        {
            _values[key.Id] = value;
            Changed?.Invoke(key);
        }
    }
}
