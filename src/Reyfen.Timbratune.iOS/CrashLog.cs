using ObjCRuntime;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// Writes fatal .NET exceptions to Documents/crash.txt, which the Files app shows (On My iPhone ›
/// Timbratune). A Release build's own log lines are redacted ("&lt;private&gt;") in the device log
/// and the crash report has no managed stack, so this is how a tester can send the reason.
/// Newest first; the file is kept short.
/// </summary>
internal static class CrashLog
{
    private const int MaxBytes = 64 * 1024;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Unhandled exception", e.ExceptionObject);
        // A managed exception about to cross into Objective-C (an Avalonia or UIKit callback) ends the
        // process from native code; this sees it first.
        Runtime.MarshalManagedException += (_, e) => Write("Managed exception into native code", e.Exception);
    }

    public static void Write(string what, object? exception)
    {
        try
        {
            var path = Path.Combine(IosFolders.Documents, "crash.txt");
            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {what} · Timbratune {typeof(CrashLog).Assembly.GetName().Version}\n{exception}\n\n";
            var old = File.Exists(path) ? File.ReadAllText(path) : "";
            var text = entry + old;
            if (text.Length > MaxBytes) text = text[..MaxBytes];
            File.WriteAllText(path, text);
            Console.Error.WriteLine(entry);
        }
        catch (Exception)
        {
            // Nothing more can be done while crashing.
        }
    }
}
