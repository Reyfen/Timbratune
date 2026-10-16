namespace Reyfen.Timbratune.Shared.Telemetry;

/// <summary>
/// Crash and error reports (REFACTOR-PLAN §5; Sentry, later). Off unless the user opted in and
/// this install is in the rollout (<see cref="TelemetryConsent"/>); the default is
/// <see cref="NullCrashReporter"/>. Reports carry the error and the recent log lines, never
/// audio, labels or file names. An adapter is wrapped in a <see cref="CrashReportLimiter"/>, so
/// a crash loop can't use up the month's quota.
/// </summary>
public interface ICrashReporter
{
    bool IsEnabled { get; }

    /// <param name="area">Where it happened, e.g. "recording", "import", "unhandled".</param>
    void Report(Exception error, string area);
}

/// <summary>The default: nothing is reported.</summary>
public sealed class NullCrashReporter : ICrashReporter
{
    public static readonly NullCrashReporter Instance = new();

    private NullCrashReporter()
    {
    }

    public bool IsEnabled => false;

    public void Report(Exception error, string area)
    {
    }
}

/// <summary>
/// Passes on at most <see cref="MaxPerSession"/> reports per app session, and each kind of error
/// (area, exception type and the method that threw) only once.
/// </summary>
public sealed class CrashReportLimiter(ICrashReporter inner, int maxPerSession = CrashReportLimiter.DefaultMaxPerSession) : ICrashReporter
{
    public const int DefaultMaxPerSession = 5;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private int _sent;

    public int MaxPerSession => maxPerSession;

    public bool IsEnabled => inner.IsEnabled;

    public void Report(Exception error, string area)
    {
        if (!inner.IsEnabled) return;
        var kind = $"{area}|{error.GetType().FullName}|{error.TargetSite?.DeclaringType?.FullName}.{error.TargetSite?.Name}";
        lock (_seen)
        {
            if (_sent >= maxPerSession || !_seen.Add(kind)) return;
            _sent++;
        }
        inner.Report(error, area);
    }
}
