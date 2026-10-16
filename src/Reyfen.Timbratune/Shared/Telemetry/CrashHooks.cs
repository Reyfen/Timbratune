using Avalonia.Threading;
using Reyfen.Timbratune.Core.Diagnostics;

namespace Reyfen.Timbratune.Shared.Telemetry;

/// <summary>
/// Sends unhandled errors (the UI thread's, other threads', unobserved tasks') to the crash reporter
/// and the log. With both off, the default, nothing is subscribed. It only observes: whether the
/// app goes on is decided as before.
/// </summary>
public static class CrashHooks
{
    private static bool _installed;

    public static void Install(ICrashReporter crashes, IAppLog log)
    {
        if (_installed || (!crashes.IsEnabled && !log.IsEnabled(AppLogLevel.Error))) return;
        _installed = true;
        void Note(Exception e, string area)
        {
            log.Log(AppLogLevel.Error, "unhandled", e, area);
            if (crashes.IsEnabled) crashes.Report(e, area);
        }
        Dispatcher.UIThread.UnhandledException += (_, e) => Note(e.Exception, "unhandled.ui");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error) Note(error, "unhandled");
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Note(e.Exception, "unhandled.task");
    }
}
