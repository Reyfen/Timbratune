using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.App;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.ViewModels;

namespace Reyfen.Timbratune.Tests;

/// <summary>
/// The container a head builds: <see cref="CompositionRoot.AddTimbratune"/> plus the services a
/// head must add. Validation builds every registration's dependency graph without creating
/// anything (view models and the player need Avalonia's UI thread).
/// </summary>
public sealed class CompositionTests
{
    private static ServiceCollection WithHeadServices(Action<ServiceCollection>? head = null)
    {
        var services = new ServiceCollection();
        services.AddTimbratune();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "timbratune-composition-" + Guid.NewGuid().ToString("N"))));
        services.AddSingleton<IPlatformInfo>(new PlatformInfo());
        services.AddSingleton<IAudioRecorder, NoRecorder>();
        services.AddSingleton<IAudioPlayer, NoPlayer>();
        head?.Invoke(services);
        return services;
    }

    [Fact]
    public void EveryRegistrationResolvesWithADesktopsServices()
    {
        using var provider = WithHeadServices().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.IsType<NoRuntimePermissions>(provider.GetRequiredService<IPermissions>());
        Assert.IsType<DefaultThreadPriority>(provider.GetRequiredService<IThreadPriority>());
        Assert.Null(provider.GetRequiredService<ReferenceVoiceFolder>().Path);
        Assert.Null(provider.GetService<IFileSharer>());
    }

    [Fact]
    public void AHeadsOwnRegistrationsWinOverTheDefaults()
    {
        var permissions = new AskingPermissions();
        using var provider = WithHeadServices(s =>
        {
            s.AddSingleton<IPermissions>(permissions);
            s.AddSingleton(new ReferenceVoiceFolder("refs"));
        }).BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Same(permissions, provider.GetRequiredService<IPermissions>());
        Assert.Equal("refs", provider.GetRequiredService<ReferenceVoiceFolder>().Path);
    }

    [Fact]
    public void SharedServicesAreSingletons()
    {
        using var provider = WithHeadServices().BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IOverlayHost>(), provider.GetRequiredService<IOverlayHost>());
        Assert.Same(provider.GetRequiredService<IMessenger>(), provider.GetRequiredService<IMessenger>());
        Assert.Same(provider.GetRequiredService<TakeImporter>(), provider.GetRequiredService<TakeImporter>());
        Assert.Same(provider.GetRequiredService<StorageProviderDialogs>(), provider.GetRequiredService<IFileDialogs>());
    }

    [Fact]
    public void WithoutAHeadsServicesTheContainerSaysWhatIsMissing()
    {
        var services = new ServiceCollection().AddTimbratune();
        var e = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }));
        Assert.Contains(e.InnerExceptions, x => x.Message.Contains(nameof(MainViewModel)));
    }

    [Fact]
    public void TheRegisteredSettingsAreValidAndDeviceOnly()
    {
        using var provider = WithHeadServices().BuildServiceProvider();
        var keys = provider.GetServices<ISettingKey>().ToList();
        Assert.Contains(keys, k => k.Id == ShellSettings.Theme.Id);
        Assert.Contains(keys, k => k.Id == RecordingSettings.LiveWindowSeconds.Id);
        // Every setting is device-only until sync exists (the owner's decision, 2026-10-15).
        Assert.All(keys, k => Assert.Equal(SettingScope.Device, k.Scope));
        // Building the service checks the ids (no clashes, nothing that looks like a secret).
        Assert.Equal("system", provider.GetRequiredService<ISettings>().Get(ShellSettings.Theme));
    }

    [Theory]
    [InlineData(0, "whole take")]
    [InlineData(30, "last 30 s")]
    [InlineData(45, "whole take")]
    public void TheStoredLiveWindowPicksItsChoice(double seconds, string label) =>
        Assert.Equal(label, RecordingSettings.OptionFor(seconds).Label);

    [Fact]
    public void FeatureFlagsFollowTheBuild() => Assert.Equal(BuildFeatureFlags.ReferenceVoicesBuilt, new BuildFeatureFlags().ReferenceVoices);

    private sealed class NoRecorder : IAudioRecorder
    {
        public bool IsRecording => false;
        public int SampleRate => 44100;
        public event SamplesCapturedHandler? SamplesCaptured { add { } remove { } }
        public void Start() => throw new NotSupportedException();
        public Task<string> StopAsync() => throw new NotSupportedException();
        public void Cancel() { }
        public void Dispose() { }
    }

    private sealed class NoPlayer : IAudioPlayer
    {
        public string? CurrentPath => null;
        public bool IsPlaying => false;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public float Volume { get; set; } = 1;
        public event EventHandler? PlaybackEnded { add { } remove { } }
        public void Play(string path) { }
        public void Pause() { }
        public void Resume() { }
        public void Stop() { }
        public void Seek(TimeSpan position) { }
        public void Dispose() { }
    }

    private sealed class AskingPermissions : IPermissions
    {
        public Task<bool> RequestMicrophoneAsync() => Task.FromResult(false);
    }
}
