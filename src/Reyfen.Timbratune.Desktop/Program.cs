using Avalonia;
using Reyfen.Timbratune.Audio.SoundFlow;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;

namespace Reyfen.Timbratune.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--import")
            return ImportAsync(args[1..]).GetAwaiter().GetResult();

        using var audio = new SoundFlowAudio();
        // TIMBRATUNE_FAKE_MIC=take.wav replays a file as if it were being spoken (developer aid).
        var fakeMic = Environment.GetEnvironmentVariable(Core.Audio.FileReplayRecorder.EnvVar);
        using var recorder = string.IsNullOrWhiteSpace(fakeMic) ? audio.CreateRecorder() : new Core.Audio.FileReplayRecorder(fakeMic);
        PlaybackService? playback = null;
        try
        {
            // Runs once Avalonia is up — PlaybackService owns a DispatcherTimer,
            // which must not be created before the UI thread is initialized.
            App.ServicesFactory = dialogs => new AppServices(
                Store: new RecordingStore(DataPaths.Default()),
                Engine: new AcousticsAnalysisEngine(),
                Recorder: recorder,
                Playback: playback = new PlaybackService(audio.CreatePlayer()),
                Dialogs: dialogs,
                ReferenceDir: Features.ReferenceVoices ? Path.Combine(AppContext.BaseDirectory, "reference") : null);

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            playback?.Dispose();
        }
    }

    /// <summary>
    /// <c>Reyfen.Timbratune.Desktop --import take.wav [more.wav…] [--label "…"]</c> —
    /// analyzes WAV files and adds them as takes, like
    /// <c>uv run analyze.py clip.wav --label …</c> did for the React app.
    /// Honors TIMBRATUNE_DATA_DIR.
    /// </summary>
    private static async Task<int> ImportAsync(string[] args)
    {
        var label = (string?)null;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--label" && i + 1 < args.Length) label = args[++i];
            else files.Add(args[i]);
        }
        if (files.Count == 0)
        {
            Console.Error.WriteLine("usage: Reyfen.Timbratune.Desktop --import <file.wav> [more.wav…] [--label \"text\"]");
            return 2;
        }

        var engine = new AcousticsAnalysisEngine();
        var store = new RecordingStore(DataPaths.Default());
        foreach (var file in files)
        {
            var result = await engine.AnalyzeAsync(file);
            var entry = result.Metrics;
            entry.Label = label ?? Path.GetFileNameWithoutExtension(file);
            entry.SourceFile = Path.GetFileName(file);
            var saved = store.Add(entry, result.Detail, file, result.Series);
            Console.WriteLine($"#{saved.Id} {entry.Label}: pitch ~{entry.Pitch.MeanHz} Hz, F2 {entry.Formants.F2Hz} Hz, " +
                              $"weight {entry.Weight?.H1a3cDb} dB, {entry.Register?.InRegisterPct}% in register");
        }
        Console.WriteLine($"saved to {store.Paths.Root}");
        return 0;
    }

    // Also used by the Avalonia XAML previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
