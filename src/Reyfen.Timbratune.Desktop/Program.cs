using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.App;
using Reyfen.Timbratune.Audio.Ffmpeg;
using Reyfen.Timbratune.Audio.MediaFoundation;
using Reyfen.Timbratune.Audio.SoundFlow;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Platform;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Disposed on exit, newest first: player, recorder, then the audio engine.
        using var services = BuildServices();
        if (args.Length > 0 && args[0] == "--import")
            return ImportAsync(services, args[1..]).GetAwaiter().GetResult();
        return BuildAvaloniaApp(() => new TimbratuneApp(services)).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The app's services plus the desktop's adapters. Nothing is created until first asked for.</summary>
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection().AddTimbratune();
        services.AddSingleton(_ => DataPaths.Default());
        services.AddSingleton<IPlatformInfo>(new PlatformInfo());
        services.AddSingleton(_ => new SoundFlowAudio());
        services.AddSingleton<IAudioDecoderProvider>(sp => new MiniaudioDecoderProvider(sp.GetRequiredService<SoundFlowAudio>));
        if (OperatingSystem.IsWindows()) services.AddSingleton<IAudioDecoderProvider, MediaFoundationDecoderProvider>();
        // FFmpeg: an ffmpeg on the PATH, or one downloaded at the user's request (offered on Linux).
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IHttpDownloader, HttpClientDownloader>();
        services.AddSingleton(sp => new FfmpegInstaller(sp.GetRequiredService<IHttpDownloader>(), sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton<IDecoderInstaller>(sp => sp.GetRequiredService<FfmpegInstaller>());
        services.AddSingleton<IAudioDecoderProvider>(sp => new FfmpegDecoderProvider(sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<FfmpegInstaller>().InstalledPrograms));
        // TIMBRATUNE_FAKE_MIC=take.wav replays a file as if it were being spoken (developer aid).
        services.AddSingleton<IAudioRecorder>(sp =>
            Environment.GetEnvironmentVariable(FileReplayRecorder.EnvVar) is { } fakeMic && !string.IsNullOrWhiteSpace(fakeMic)
                ? new FileReplayRecorder(fakeMic)
                : sp.GetRequiredService<SoundFlowAudio>().CreateRecorder());
        services.AddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<SoundFlowAudio>().CreatePlayer());
        if (new BuildFeatureFlags().ReferenceVoices) // registered after AddTimbratune, so this one wins
            services.AddSingleton(new ReferenceVoiceFolder(Path.Combine(AppContext.BaseDirectory, "reference")));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>
    /// <c>Reyfen.Timbratune.Desktop --import take.wav [more.mp3 / .flac / .m4a / .tmbr…] [--label "…"]</c> —
    /// analyzes audio files (or unpacks .tmbr files) and adds them as takes, like
    /// <c>uv run analyze.py clip.wav --label …</c> did for the React app.
    /// Honors TIMBRATUNE_DATA_DIR.
    /// </summary>
    private static async Task<int> ImportAsync(IServiceProvider services, string[] args)
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
            Console.Error.WriteLine("usage: Reyfen.Timbratune.Desktop --import <audio file|.tmbr> [more…] [--label \"text\"]");
            return 2;
        }

        var store = services.GetRequiredService<RecordingStore>();
        store.Load(); // converts an older data folder first
        var importer = services.GetRequiredService<TakeImportService>();
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                var entry = await importer.ImportAsync(file, label: label);
                Console.WriteLine($"#{entry.Id} {entry.Label}: pitch ~{entry.Pitch.MeanHz} Hz, F2 {entry.Formants.F2Hz} Hz, " +
                                  $"weight {entry.Weight?.H1a3cDb} dB, {entry.Register?.InRegisterPct}% in register");
            }
            catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException or FormatException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"{file}: {e.Message}");
                failed++;
            }
        }
        Console.WriteLine($"saved to {store.Paths.Root}");
        return failed == 0 ? 0 : 1;
    }

    // Also used by the Avalonia XAML previewer (with the parameterless app: no services).
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new TimbratuneApp());

    private static AppBuilder BuildAvaloniaApp(Func<TimbratuneApp> app) =>
        AppBuilder.Configure(app)
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
