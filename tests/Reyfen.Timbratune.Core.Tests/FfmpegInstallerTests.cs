using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Reyfen.Timbratune.Audio.Ffmpeg;
using Reyfen.Timbratune.Core.Platform;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The FFmpeg download (REFACTOR-PLAN §4.3), offline: a fake server hands out a made-up archive.</summary>
public sealed class FfmpegInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-ffmpeg-" + Guid.NewGuid().ToString("N"));
    private static readonly string Program = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    private static readonly string Rid = RuntimeInformation.RuntimeIdentifier;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A tar.gz like the mirrored ones, plus things that must not be unpacked.</summary>
    private static byte[] Archive(string programText = "#!/bin/sh\necho ffmpeg\n")
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            void Add(string name, string text, TarEntryType type = TarEntryType.RegularFile)
            {
                var entry = new PaxTarEntry(type, name);
                if (type == TarEntryType.RegularFile) entry.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                tar.WriteEntry(entry);
            }
            Add(Program, programText);
            Add("LICENSE.txt", "LGPL");
            Add("README.txt", "source: ffmpeg.org");
            Add("ffprobe", "not wanted");
            Add("../../evil", "outside");
            Add("bin", "", TarEntryType.Directory);
        }
        return output.ToArray();
    }

    private static FfmpegBuild Pin(byte[] archive, string version = "9.0.2") =>
        new(Rid, version, new Uri("https://example.test/ffmpeg.tar.gz"), archive.Length, Convert.ToHexStringLower(SHA256.HashData(archive)));

    [Fact]
    public async Task ItDownloadsVerifiesAndUnpacksOnlyTheProgramAndItsLicence()
    {
        var archive = Archive();
        var downloader = new FakeDownloader(archive);
        var installer = new FfmpegInstaller(downloader, new NoFfmpegOnPath(), _root, [Pin(archive)]);
        Assert.True(installer.CanInstall);
        Assert.False(installer.IsInstalled);
        Assert.Equal(("FFmpeg", "9.0.2", (long)archive.Length), (installer.Describe().Name, installer.Describe().Version, installer.Describe().Bytes));

        var reports = new List<double>();
        await installer.InstallAsync(new Progress(reports), CancellationToken.None);

        var folder = Path.Combine(_root, "9.0.2");
        Assert.Equal(["LICENSE.txt", "README.txt", Program], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal([folder], Directory.GetDirectories(_root)); // no download or partial folder left
        Assert.False(File.Exists(Path.Combine(_root, "..", "evil")));
        Assert.Equal([Path.Combine(folder, Program)], installer.InstalledPrograms());
        Assert.True(installer.IsInstalled);
        Assert.False(installer.CanInstall);
        Assert.Equal(1.0, reports[^1]);
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(Path.Combine(folder, Program)).HasFlag(UnixFileMode.UserExecute));

        installer.Remove();
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task AFileThatIsntThePinnedOneIsDeletedUnopened()
    {
        var pinned = Archive();
        var tampered = Archive("#!/bin/sh\nrm -rf ~\n");
        var installer = new FfmpegInstaller(new FakeDownloader(tampered), new NoFfmpegOnPath(), _root, [Pin(pinned) with { Bytes = tampered.Length }]);
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(null, CancellationToken.None));
        Assert.Contains("SHA-256", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(_root)); // nothing unpacked, the download gone
        Assert.False(installer.IsInstalled);
    }

    [Fact]
    public async Task ANewPinnedVersionReplacesTheOldOne()
    {
        var archive = Archive();
        await new FfmpegInstaller(new FakeDownloader(archive), new NoFfmpegOnPath(), _root, [Pin(archive, "8.1")]).InstallAsync(null, CancellationToken.None);

        // After an app update the pin is newer: offered again even though an ffmpeg is there (the old download).
        var updated = new FfmpegInstaller(new FakeDownloader(archive), new NoFfmpegOnPath(), _root, [Pin(archive, "9.0.2")]);
        Assert.True(updated.CanInstall);
        Assert.Equal([Path.Combine(_root, "8.1", Program)], updated.InstalledPrograms()); // the old one keeps working meanwhile
        await updated.InstallAsync(null, CancellationToken.None);
        Assert.Equal([Path.Combine(_root, "9.0.2", Program)], updated.InstalledPrograms());
        Assert.False(Directory.Exists(Path.Combine(_root, "8.1")));
    }

    [Fact]
    public void NothingIsOfferedWithoutAPinOrWithAnFfmpegAlreadyThere()
    {
        var archive = Archive();
        Assert.False(new FfmpegInstaller(new FakeDownloader(archive), new NoFfmpegOnPath(), _root, []).CanInstall);
        Assert.False(new FfmpegInstaller(new FakeDownloader(archive), new FfmpegOnPath(), _root, [Pin(archive)]).CanInstall);
        Assert.Throws<PlatformNotSupportedException>(() => new FfmpegInstaller(new FakeDownloader(archive), new NoFfmpegOnPath(), _root, []).Describe());
        // Offered unasked only on Linux; elsewhere it waits for a setting.
        Assert.Equal(OperatingSystem.IsLinux(), new FfmpegInstaller(new FakeDownloader(archive), new NoFfmpegOnPath(), _root, [Pin(archive)]).IsOffered);
    }

    [Fact]
    public async Task ACancelledDownloadLeavesNothing()
    {
        var installer = new FfmpegInstaller(new FakeDownloader(Archive(), cancelHalfway: true), new NoFfmpegOnPath(), _root, [Pin(Archive())]);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => installer.InstallAsync(new CancelOnHalf(cancel), cancel.Token));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    private sealed class FakeDownloader(byte[] body, bool cancelHalfway = false) : IHttpDownloader
    {
        public async Task DownloadAsync(Uri url, Stream target, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            var half = body.Length / 2;
            await target.WriteAsync(body.AsMemory(0, half), cancellationToken);
            progress?.Report(0.5);
            if (cancelHalfway) cancellationToken.ThrowIfCancellationRequested();
            await target.WriteAsync(body.AsMemory(half), cancellationToken);
            progress?.Report(1);
        }
    }

    private sealed class NoFfmpegOnPath : IProcessRunner
    {
        public string? FindOnPath(string program) => null;
        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, Func<Stream, CancellationToken, Task> readOutput,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FfmpegOnPath : IProcessRunner
    {
        public string? FindOnPath(string program) => program == "ffmpeg" ? "/usr/bin/ffmpeg" : null;
        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, Func<Stream, CancellationToken, Task> readOutput,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Synchronous, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class Progress(List<double> reports) : IProgress<double>
    {
        public void Report(double value) => reports.Add(value);
    }

    private sealed class CancelOnHalf(CancellationTokenSource cancel) : IProgress<double>
    {
        public void Report(double value)
        {
            if (value >= 0.5) cancel.Cancel();
        }
    }
}
