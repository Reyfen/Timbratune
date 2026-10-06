using System.Diagnostics;
using System.Globalization;
using System.Text;
using Reyfen.Timbratune.Core.Analysis;

namespace Reyfen.Timbratune.Core.Tests.PraatReference;

/// <summary>
/// Test oracle: runs analyze.praat (the Praat half of analyze.py) through the
/// real Praat binary and parses its stdout into the same <see cref="RawAnalysis"/>
/// the C# engine produces. The app itself no longer uses Praat.
/// </summary>
public sealed class PraatAnalysisEngine(string? praatPath) : IAnalysisEngine
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public string? PraatPath { get; } = praatPath;

    public bool IsAvailable => PraatPath is not null && File.Exists(PraatPath);

    public string? UnavailableReason => IsAvailable
        ? null
        : $"Praat wasn't found. Run scripts/fetch-praat.ps1, or set {PraatLocator.PraatEnvVar} to the Praat executable.";

    public async Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default) =>
        AnalysisPostProcessor.Process(await RunAsync(wavPath, cancellationToken), registerFloorHz);

    public async Task<RawAnalysis> RunAsync(string wavPath, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        var script = Path.Combine(AppContext.BaseDirectory, "PraatReference", "analyze.praat");
        var psi = new ProcessStartInfo(PraatPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "--run", "--no-pref-files", "--utf8", script, Path.GetFullPath(wavPath) })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new PraatException($"Praat exited {process.ExitCode}: {(await stderr).Trim()}");
        return Parse(await stdout);
    }

    /// <summary>
    /// Parses analyze.praat's stdout (tab-separated records, see the header of
    /// the script). Throws if the END marker is missing.
    /// </summary>
    public static RawAnalysis Parse(string stdout)
    {
        var summary = new Dictionary<string, double>(StringComparer.Ordinal);
        var formants = new List<RawAnalysis.FormantRow>();
        var weight = new List<RawAnalysis.WeightRow>();
        var ltas = new List<(double, double)>();
        var contour = new List<(double, double)>();
        var phrases = new List<(double, double)>();
        var complete = false;

        using var reader = new StringReader(stdout);
        while (reader.ReadLine() is { } line)
        {
            var f = line.TrimEnd('\r').Split('\t');
            double N(int i) => ParseNumber(f[i]);
            switch (f[0])
            {
                case "S" when f.Length >= 3: summary[f[1]] = N(2); break;
                case "F" when f.Length >= 6: formants.Add(new(N(1), N(2), N(3), N(4), N(5))); break;
                case "W" when f.Length >= 12: weight.Add(new(N(1), N(2), N(3), N(4), N(5), N(6), N(7), N(8), N(9), N(10), N(11))); break;
                case "L" when f.Length >= 3: ltas.Add((N(1), N(2))); break;
                case "C" when f.Length >= 3: contour.Add((N(1), N(2))); break;
                case "P" when f.Length >= 3: phrases.Add((N(1), N(2))); break;
                case "END": complete = true; break;
            }
        }
        if (!complete) throw new PraatException("Praat's output ended early (no END marker).");
        return new RawAnalysis
        {
            Summary = summary,
            Formants = formants,
            WeightFrames = weight,
            Ltas = ltas,
            Contour = contour,
            Sounding = phrases,
        };
    }

    /// <summary>Praat prints undefined as "--undefined--"; that (and anything unparsable) becomes NaN.</summary>
    public static double ParseNumber(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
}

public sealed class PraatException(string message) : Exception(message);
