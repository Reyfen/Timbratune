using System.Globalization;
using System.Runtime.CompilerServices;

namespace Reyfen.Timbratune.Core.Diagnostics;

public enum AppLogLevel { Debug, Info, Warning, Error }

/// <summary>
/// The app's log (REFACTOR-PLAN §5). Nothing is written by default (<see cref="NullAppLog"/>);
/// a crash reporter can later keep the recent lines as breadcrumbs for its reports, or send
/// them as logs. Messages are short and hold no voice data, labels or file names.
/// Write through <see cref="AppLogExtensions.Log(IAppLog, AppLogLevel, string, ref AppLogMessage)"/>:
/// with the level off, an interpolated message isn't even formatted (nothing is allocated).
/// </summary>
public interface IAppLog
{
    bool IsEnabled(AppLogLevel level);

    /// <param name="area">Where it happened, e.g. "import", "recording", "settings".</param>
    void Write(AppLogLevel level, string area, string message, Exception? error = null);
}

/// <summary>The default log: off at every level.</summary>
public sealed class NullAppLog : IAppLog
{
    public static readonly NullAppLog Instance = new();

    private NullAppLog()
    {
    }

    public bool IsEnabled(AppLogLevel level) => false;

    public void Write(AppLogLevel level, string area, string message, Exception? error = null)
    {
    }
}

public static class AppLogExtensions
{
    public static void Log(this IAppLog log, AppLogLevel level, string area, string message)
    {
        if (log.IsEnabled(level)) log.Write(level, area, message);
    }

    public static void Log(this IAppLog log, AppLogLevel level, string area, Exception error, string message)
    {
        if (log.IsEnabled(level)) log.Write(level, area, message, error);
    }

    public static void Log(this IAppLog log, AppLogLevel level, string area,
        [InterpolatedStringHandlerArgument("log", "level")] ref AppLogMessage message)
    {
        if (message.Enabled) log.Write(level, area, message.ToStringAndClear());
    }

    public static void Log(this IAppLog log, AppLogLevel level, string area, Exception error,
        [InterpolatedStringHandlerArgument("log", "level")] ref AppLogMessage message)
    {
        if (message.Enabled) log.Write(level, area, message.ToStringAndClear(), error);
    }
}

/// <summary>An interpolated log message, formatted (invariant culture) only when its level is on.</summary>
[InterpolatedStringHandler]
public ref struct AppLogMessage
{
    private DefaultInterpolatedStringHandler _text;

    public AppLogMessage(int literalLength, int formattedCount, IAppLog log, AppLogLevel level, out bool enabled)
    {
        Enabled = enabled = log.IsEnabled(level);
        _text = enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture) : default;
    }

    public bool Enabled { get; }

    public void AppendLiteral(string value) => _text.AppendLiteral(value);

    public void AppendFormatted<T>(T value) => _text.AppendFormatted(value);

    public void AppendFormatted<T>(T value, string? format) => _text.AppendFormatted(value, format);

    public void AppendFormatted(string? value) => _text.AppendFormatted(value);

    public string ToStringAndClear() => _text.ToStringAndClear();
}
