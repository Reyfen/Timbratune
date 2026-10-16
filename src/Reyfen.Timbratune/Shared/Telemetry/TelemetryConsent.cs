using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Shared.Telemetry;

/// <summary>The user's telemetry choices. Device-scoped: consent is given per device (REFACTOR-PLAN §5a).</summary>
public static class TelemetrySettings
{
    /// <summary>Usage analytics opt-in; off by default.</summary>
    public static readonly SettingKey<bool> Analytics = new("telemetry.analytics", false, SettingScope.Device, SettingsJson.Default.Boolean);

    /// <summary>Crash and error reports opt-in; off by default.</summary>
    public static readonly SettingKey<bool> CrashReports = new("telemetry.crashReports", false, SettingScope.Device, SettingsJson.Default.Boolean);

    /// <summary>A random id for this install, created at the first opt-in and forgotten when both are turned off.</summary>
    public static readonly SettingKey<string> InstallId = new("telemetry.installId", "", SettingScope.Device, SettingsJson.Default.String);
}

/// <summary>
/// The share of installs that send, for those who never donated (donors always send once they
/// opted in). They keep the free plans' quotas (Mixpanel about 1M events and Sentry 5,000 errors
/// a month) for a representative sample. The rates here are the built-in defaults; a small
/// rates file read at most once a day may lower or raise them later (REFACTOR-PLAN §5).
/// </summary>
public sealed record RolloutRates(double Analytics, double CrashReports)
{
    public static readonly RolloutRates Default = new(Analytics: 0.25, CrashReports: 0.25);
}

/// <summary>
/// Who is in the rollout: everyone who ever donated, and a stable share of the others. The
/// share is a lottery on the install id (a bucket 0–9999 from a fixed hash), so an install is
/// either always in or always out at a given rate, and raising the rate only adds installs.
/// </summary>
public static class TelemetryRollout
{
    public const int Buckets = 10_000;

    /// <summary>The install's bucket, 0 ≤ b &lt; <see cref="Buckets"/>; the same on every run and platform (FNV-1a, not string.GetHashCode).</summary>
    public static int BucketOf(string installId)
    {
        var hash = 2166136261u;
        foreach (var c in installId)
        {
            hash = (hash ^ c) * 16777619u;
        }
        return (int)(hash % Buckets);
    }

    public static bool Includes(string installId, bool hasEverDonated, double rate) =>
        hasEverDonated || BucketOf(installId) < Math.Clamp(rate, 0, 1) * Buckets;
}

/// <summary>
/// Whether this install sends analytics and crash reports: only with the user's opt-in, and only
/// when it is in the rollout (<see cref="TelemetryRollout"/>). The adapters (later) are created
/// only when this says yes, and read it again when a choice changes.
/// </summary>
public sealed class TelemetryConsent(ISettings settings, IEntitlements entitlements, RolloutRates rates)
{
    public bool AnalyticsOn => settings.Get(TelemetrySettings.Analytics) && InRollout(rates.Analytics);

    public bool CrashReportsOn => settings.Get(TelemetrySettings.CrashReports) && InRollout(rates.CrashReports);

    /// <summary>This install's id; created on the first opt-in, empty while there is none.</summary>
    public string InstallId => settings.Get(TelemetrySettings.InstallId);

    public void SetAnalytics(bool on) => Set(TelemetrySettings.Analytics, on);

    public void SetCrashReports(bool on) => Set(TelemetrySettings.CrashReports, on);

    private void Set(SettingKey<bool> key, bool on)
    {
        settings.Set(key, on);
        var any = settings.Get(TelemetrySettings.Analytics) || settings.Get(TelemetrySettings.CrashReports);
        if (any && InstallId.Length == 0) settings.Set(TelemetrySettings.InstallId, Guid.NewGuid().ToString("N"));
        else if (!any && InstallId.Length > 0) settings.Set(TelemetrySettings.InstallId, "");
    }

    private bool InRollout(double rate) => InstallId is { Length: > 0 } id && TelemetryRollout.Includes(id, entitlements.HasEverDonated, rate);
}
