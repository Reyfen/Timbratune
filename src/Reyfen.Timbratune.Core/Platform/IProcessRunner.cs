using System.Diagnostics;

namespace Reyfen.Timbratune.Core.Platform;

/// <summary>How a finished process ended: its exit code, and what it printed (stdout unless streamed, stderr).</summary>
public sealed record ProcessResult(int ExitCode, string Output, string Error);

/// <summary>
/// Runs other programs (FFmpeg). Only desktop heads register one: Android, iOS and the browser
/// can't start processes. A port, so what uses it can be tested without the program.
/// </summary>
public interface IProcessRunner
{
    /// <summary>The full path of <paramref name="program"/> on the PATH, or null.</summary>
    string? FindOnPath(string program);

    /// <summary>Runs to the end and returns what it printed.</summary>
    Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs, handing its stdout to <paramref name="readOutput"/> as it is written (the result's
    /// <see cref="ProcessResult.Output"/> is then empty). Cancelling kills the process.
    /// </summary>
    Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, Func<Stream, CancellationToken, Task> readOutput,
        CancellationToken cancellationToken = default);
}

/// <summary><see cref="IProcessRunner"/> over <see cref="Process"/>: no shell, no window, arguments passed as they are.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public string? FindOnPath(string program)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries).Prepend("")
            : [""];
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir.Trim('"'), program + ext);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) =>
        RunCoreAsync(program, arguments, null, cancellationToken);

    public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, Func<Stream, CancellationToken, Task> readOutput,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(program, arguments, readOutput, cancellationToken);

    private static async Task<ProcessResult> RunCoreAsync(string program, IReadOnlyList<string> arguments,
        Func<Stream, CancellationToken, Task>? readOutput, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true, // closed at once: nothing can wait for input
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"{program} didn't start.");
        process.StandardInput.Close();
        await using var kill = cancellationToken.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        var output = "";
        if (readOutput is null) output = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        else
        {
            try
            {
                await readOutput(process.StandardOutput.BaseStream, cancellationToken);
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw;
            }
        }
        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult(process.ExitCode, output, await error);
    }
}
