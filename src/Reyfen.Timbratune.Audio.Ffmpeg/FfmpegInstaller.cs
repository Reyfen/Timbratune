using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Platform;

namespace Reyfen.Timbratune.Audio.Ffmpeg;

/// <summary>One pinned FFmpeg download: an LGPL-only static build, repacked to just ffmpeg and its licence.</summary>
/// <param name="Rid">The .NET runtime identifier it runs on (linux-x64, linux-arm64, win-x64, win-arm64).</param>
public sealed record FfmpegBuild(string Rid, string Version, Uri Url, long Bytes, string Sha256);

/// <summary>
/// Downloads FFmpeg for Timbratune, only when the user asks (REFACTOR-PLAN §4.3):
/// <list type="bullet">
/// <item>the build for this OS and CPU is pinned in the app (URL, size, SHA-256) and fetched over HTTPS;</item>
/// <item>the hash is checked before anything is unpacked, and a mismatch deletes the file;</item>
/// <item>only <c>ffmpeg</c> and its licence files are unpacked, into
/// <c>&lt;tools&gt;/&lt;version&gt;/</c> (<c>$XDG_DATA_HOME/Timbratune/tools/ffmpeg</c> on Linux), apart from the takes;</item>
/// <item>no root access, nothing system-wide; the package manager's command is only shown.</item>
/// </list>
/// Offered unasked on Linux; on other desktops it is there for a setting to offer later.
/// </summary>
public sealed class FfmpegInstaller : IDecoderInstaller
{
    /// <summary>
    /// The pinned builds: BtbN FFmpeg-Builds' LGPL static builds of FFmpeg 9.0.2
    /// (autobuild-2026-10-15-13-03, build n9.0.2-28-g669f5ab083), repacked with
    /// <c>scripts/mirror-ffmpeg.ps1</c> and mirrored on Reyfen/github-releases-as-file-hosting.
    /// Updated with app releases.
    /// </summary>
    public static IReadOnlyList<FfmpegBuild> Pinned { get; } =
    [
        new("linux-x64", "9.0.2", new("https://github.com/Reyfen/github-releases-as-file-hosting/releases/download/ffmpeg-n9.0.2-28-g669f5ab083/ffmpeg-9.0.2-linux-x64.tar.gz"), 59628498, "d0eb7f1d6a187726dd06b5bcd0bf208b63dea5e3e68c1235bb13dedd8053c5f0"),
        new("linux-arm64", "9.0.2", new("https://github.com/Reyfen/github-releases-as-file-hosting/releases/download/ffmpeg-n9.0.2-28-g669f5ab083/ffmpeg-9.0.2-linux-arm64.tar.gz"), 53807066, "a80c52f6abf85aa2ac13a20fc3baaafef768d66761c9fae6c897989383151fa7"),
        new("win-x64", "9.0.2", new("https://github.com/Reyfen/github-releases-as-file-hosting/releases/download/ffmpeg-n9.0.2-28-g669f5ab083/ffmpeg-9.0.2-win-x64.tar.gz"), 55878971, "2007f9b1904b5af269adf3e09796887ebe36472e550469f4a7e5b080637fcb19"),
        new("win-arm64", "9.0.2", new("https://github.com/Reyfen/github-releases-as-file-hosting/releases/download/ffmpeg-n9.0.2-28-g669f5ab083/ffmpeg-9.0.2-win-arm64.tar.gz"), 38000404, "4d816964a098f30fe8da1f6bb80db13fa74907133bdf480f59b11bac8b94c991"),
    ];

    private static readonly Uri Licence = new("https://ffmpeg.org/legal.html");
    private static readonly Uri Source = new("https://ffmpeg.org/download.html#releases");

    private readonly IHttpDownloader _downloader;
    private readonly IProcessRunner _runner;
    private readonly string _root;
    private readonly FfmpegBuild? _build;

    /// <param name="root">The tools folder for FFmpeg (default: <see cref="DefaultRoot"/>).</param>
    /// <param name="builds">The pinned builds (default: <see cref="Pinned"/>; tests pass their own).</param>
    public FfmpegInstaller(IHttpDownloader downloader, IProcessRunner runner, string? root = null, IReadOnlyList<FfmpegBuild>? builds = null)
    {
        _downloader = downloader;
        _runner = runner;
        _root = root ?? DefaultRoot;
        _build = (builds ?? Pinned).FirstOrDefault(b => b.Rid == RuntimeInformation.RuntimeIdentifier || b.Rid == CurrentRid);
    }

    /// <summary><c>$XDG_DATA_HOME/Timbratune/tools/ffmpeg</c> (Linux), <c>%LOCALAPPDATA%\Timbratune\tools\ffmpeg</c> (Windows).</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "Timbratune", "tools", "ffmpeg");

    private static string CurrentRid =>
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    private static string ProgramName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Inside Flatpak the platform's FFmpeg extension is the way; nothing is downloaded.</summary>
    private static bool InFlatpak => Environment.GetEnvironmentVariable("FLATPAK_ID") is { Length: > 0 };

    public string ProviderId => FfmpegDecoderProvider.ProviderId;

    public bool IsOffered => OperatingSystem.IsLinux() && !InFlatpak;

    /// <summary>
    /// A pinned build fits this system and isn't here yet, and either nothing provides ffmpeg or an
    /// older download does (then this is the update).
    /// </summary>
    public bool CanInstall => _build is not null && !InFlatpak
                              && !File.Exists(Path.Combine(_root, _build.Version, ProgramName))
                              && (IsInstalled || _runner.FindOnPath("ffmpeg") is null);

    public bool IsInstalled => InstalledPrograms().Any();

    /// <summary>Downloaded copies, the pinned version first (an older one keeps working until the new one is fetched).</summary>
    public IEnumerable<string> InstalledPrograms()
    {
        if (!Directory.Exists(_root)) yield break;
        var versions = Directory.GetDirectories(_root).Where(d => !d.EndsWith(".partial", StringComparison.Ordinal))
            .OrderByDescending(d => Path.GetFileName(d) == _build?.Version).ThenByDescending(d => d, StringComparer.Ordinal);
        foreach (var dir in versions)
        {
            var program = Path.Combine(dir, ProgramName);
            if (File.Exists(program)) yield return program;
        }
    }

    public DecoderDownload Describe()
    {
        var build = _build ?? throw new PlatformNotSupportedException("No FFmpeg build is pinned for this system.");
        return new DecoderDownload("FFmpeg", build.Version, build.Bytes, "LGPL 2.1 or later", Licence, Source, build.Url);
    }

    public string? PackageManagerCommand
    {
        get
        {
            if (!OperatingSystem.IsLinux() || InFlatpak) return null;
            foreach (var (program, command) in new[]
                     {
                         ("apt", "sudo apt install ffmpeg"), ("dnf", "sudo dnf install ffmpeg-free"),
                         ("pacman", "sudo pacman -S ffmpeg"), ("zypper", "sudo zypper install ffmpeg"),
                     })
                if (_runner.FindOnPath(program) is not null) return command;
            return null;
        }
    }

    public async Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var build = _build ?? throw new PlatformNotSupportedException("No FFmpeg build is pinned for this system.");
        Directory.CreateDirectory(_root);
        var download = Path.Combine(_root, $".download-{Guid.NewGuid():N}");
        var partial = Path.Combine(_root, build.Version + ".partial");
        try
        {
            await using (var file = new FileStream(download, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await _downloader.DownloadAsync(build.Url, file, progress, cancellationToken);
                file.Position = 0;
                // Checked before anything is unpacked.
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
                if (file.Length != build.Bytes || !hash.Equals(build.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The FFmpeg download isn't the expected file (SHA-256 {hash}); nothing was installed.");
            }

            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
            Directory.CreateDirectory(partial);
            await ExtractAsync(download, partial, cancellationToken);
            var program = Path.Combine(partial, ProgramName);
            if (!File.Exists(program)) throw new InvalidDataException("The FFmpeg download has no ffmpeg in it.");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                              UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var target = Path.Combine(_root, build.Version);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(partial, target);
            // Older versions go once the pinned one is in place.
            foreach (var old in Directory.GetDirectories(_root).Where(d => d != target))
                try { Directory.Delete(old, recursive: true); } catch (IOException) { }
        }
        finally
        {
            try { File.Delete(download); } catch (IOException) { }
            try { if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true); } catch (IOException) { }
        }
    }

    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Unpacks only the program and its licence and readme files, flat (no paths from the archive are used).</summary>
    private static async Task ExtractAsync(string archive, string folder, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);
        while (await tar.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null) continue;
            var name = Path.GetFileName(entry.Name.Replace('\\', '/'));
            var wanted = name == ProgramName || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
                                             || name.StartsWith("README", StringComparison.OrdinalIgnoreCase);
            if (!wanted) continue;
            await using var output = File.Create(Path.Combine(folder, name));
            await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
    }
}
