using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Euphonia.Acoustics.Tests.Oracle;

/// <summary>
/// Runs the real Praat (tools/praat, fetched by scripts/fetch-praat.ps1) on a
/// fixture with Oracle/dump.praat and parses its output. Dev-only: tests
/// return early when Praat is absent.
/// </summary>
public sealed class PraatDump
{
    private static readonly ConcurrentDictionary<string, PraatDump> Cache = new();

    public required Dictionary<string, List<double[]>> Numeric { get; init; }
    public required List<(double Start, double End, string Label)> Intervals { get; init; }

    public List<double[]> this[string tag] => Numeric.TryGetValue(tag, out var rows) ? rows : [];

    public static string? PraatPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("EUPHONIA_PRAAT");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "praat", OperatingSystem.IsWindows() ? "Praat.exe" : "praat");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }
    }

    public static PraatDump? For(string wav) => PraatPath is { } praat ? Cache.GetOrAdd(wav, w => Run(praat, w)) : null;

    private static PraatDump Run(string praat, string wav)
    {
        var psi = new ProcessStartInfo(praat)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "--run", "--no-pref-files", "--utf8", Path.Combine(AppContext.BaseDirectory, "Oracle", "dump.praat"), wav })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("Praat failed: " + stderr.Result);

        var numeric = new Dictionary<string, List<double[]>>();
        var intervals = new List<(double, double, string)>();
        foreach (var line in stdout.Result.Split('\n'))
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length < 2) continue;
            if (f[0] == "interval")
            {
                intervals.Add((Num(f[1]), Num(f[2]), f.Length > 3 ? f[3] : ""));
                continue;
            }
            if (!numeric.TryGetValue(f[0], out var rows)) numeric[f[0]] = rows = [];
            rows.Add(f.Skip(1).Select(Num).ToArray());
        }
        return new PraatDump { Numeric = numeric, Intervals = intervals };
    }

    public static double Num(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
}
