using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Euphonia.Core.Analysis;

/// <summary>
/// Runs the embedded analyze.praat through the Praat binary
/// (<c>Praat --run --no-pref-files --utf8 analyze.praat &lt;wav&gt;</c>), parses
/// its stdout and hands the raw values to <see cref="AnalysisPostProcessor"/>.
/// The script only reads the WAV and prints, so it runs inside Praat 7's
/// default sandbox (no --FULL-TRUST).
/// </summary>
public sealed class PraatAnalysisEngine : IAnalysisEngine
{
    private const string ScriptResource = "Euphonia.Core.Analysis.analyze.praat";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
    private static readonly Lazy<string> Script = new(LoadScript);

    public PraatAnalysisEngine(string? praatPath)
    {
        PraatPath = praatPath;
    }

    public string? PraatPath { get; }

    public bool IsAvailable => PraatPath is not null && File.Exists(PraatPath);

    public string? UnavailableReason => IsAvailable
        ? null
        : $"Praat wasn't found. Run scripts/fetch-praat.ps1, or set {PraatLocator.PraatEnvVar} to the Praat executable.";

    public async Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default)
    {
        var raw = await RunAsync(wavPath, cancellationToken);
        return AnalysisPostProcessor.Process(raw, registerFloorHz);
    }

    /// <summary>Runs Praat and returns the parsed raw output (exposed for parity tests).</summary>
    public async Task<PraatOutput> RunAsync(string wavPath, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        if (!File.Exists(wavPath)) throw new FileNotFoundException("Recording not found", wavPath);

        // Praat needs the script as a file; one private copy per run keeps
        // concurrent analyses independent.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"euphonia-analyze-{Guid.NewGuid():N}.praat");
        await File.WriteAllTextAsync(scriptPath, Script.Value, new UTF8Encoding(false), cancellationToken);
        try
        {
            var stdout = await RunPraatAsync(scriptPath, Path.GetFullPath(wavPath), cancellationToken);
            return PraatOutput.Parse(stdout);
        }
        finally
        {
            try { File.Delete(scriptPath); } catch (IOException) { }
        }
    }

    private async Task<string> RunPraatAsync(string scriptPath, string wavPath, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(PraatPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "--run", "--no-pref-files", "--utf8", scriptPath, wavPath })
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"Praat analysis took longer than {Timeout.TotalMinutes:0} minutes.");
        }

        var output = await stdout;
        var err = (await stderr).Trim();
        if (process.ExitCode != 0)
            throw new PraatException($"Praat exited {process.ExitCode}: {(err.Length > 0 ? err : output.Trim())}");
        return output;
    }

    internal static string LoadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ScriptResource)
            ?? throw new InvalidOperationException($"Embedded resource {ScriptResource} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

public sealed class PraatException(string message) : Exception(message);
