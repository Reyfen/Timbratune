using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Features.Record;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Tests;

/// <summary>The microphone picker (RESONANCE-PLAN F6): the choice is remembered by name and handed to the recorder.</summary>
public sealed class MicrophonePickerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "timbratune-mic-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class FakeRecorder(params AudioInputDevice[] devices) : IAudioRecorder, IInputDeviceSelection
    {
        public AudioInputDevice[] Devices { get; set; } = devices;
        public IReadOnlyList<AudioInputDevice> InputDevices() => Devices;
        public string? InputDeviceName { get; set; }
        public string? ActiveInputDevice => null;
        public bool IsRecording => false;
        public int SampleRate => 44100;
        public event SamplesCapturedHandler? SamplesCaptured { add { } remove { } }
        public void Start() { }
        public Task<string> StopAsync() => Task.FromResult("");
        public void Cancel() { }
        public void Dispose() { }
    }

    private sealed class MemorySettings : ISettings
    {
        private readonly Dictionary<string, object?> _values = [];
        public event Action<ISettingKey>? Changed;
        public T Get<T>(SettingKey<T> key) => _values.TryGetValue(key.Id, out var v) ? (T)v! : key.Default;

        public void Set<T>(SettingKey<T> key, T value)
        {
            _values[key.Id] = value;
            Changed?.Invoke(key);
        }
    }

    private MicrophonePickerViewModel Picker(IAudioRecorder recorder, ISettings settings)
    {
        var store = new RecordingStore(new DataPaths(_root));
        var catalog = new TakeCatalog(store);
        var manager = new RecordingManager(recorder, new AcousticsAnalysisEngine(), store, new NativeAnalysisScheduler(1, null),
            new NoRuntimePermissions(), catalog, new ActiveTakeService(catalog), new WeakReferenceMessenger());
        var picker = new MicrophonePickerViewModel(manager, settings);
        picker.Refresh();
        return picker;
    }

    [Fact]
    public void AChoiceIsRememberedAndHandedToTheRecorder()
    {
        var recorder = new FakeRecorder(new("Built-in", true), new("USB mic", false));
        var settings = new MemorySettings();
        var picker = Picker(recorder, settings);
        Assert.True(picker.IsVisible);
        Assert.Equal(["system default (Built-in)", "Built-in", "USB mic"], picker.Options.Select(o => o.Label));
        Assert.Null(picker.Selected!.Name);

        picker.Selected = picker.Options.Single(o => o.Name == "USB mic");
        Assert.Equal("USB mic", recorder.InputDeviceName);
        Assert.Equal("USB mic", settings.Get(RecordSettings.Microphone));
        Assert.Null(picker.Note);

        // Next session: the remembered choice is selected and given to the recorder before any recording.
        var again = new FakeRecorder(new("Built-in", true), new("USB mic", false));
        var picker2 = Picker(again, settings);
        Assert.Equal("USB mic", again.InputDeviceName);
        Assert.Equal("USB mic", picker2.Selected!.Name);

        picker2.Selected = picker2.Options[0];
        Assert.Null(again.InputDeviceName);
        Assert.Equal("", settings.Get(RecordSettings.Microphone));
    }

    [Fact]
    public void AMissingMicrophoneStaysChosenAndTheDefaultRecordsMeanwhile()
    {
        var settings = new MemorySettings();
        settings.Set(RecordSettings.Microphone, "USB mic");
        var recorder = new FakeRecorder(new AudioInputDevice("Built-in", true));
        var picker = Picker(recorder, settings);
        Assert.True(picker.IsVisible); // a single device, but a remembered one to show
        Assert.Equal("USB mic (not connected)", picker.Selected!.Label);
        Assert.NotNull(picker.Note);
        Assert.Equal("USB mic", recorder.InputDeviceName); // the recorder falls back to the default itself

        recorder.Devices = [new("Built-in", true), new("USB mic", false)];
        picker.Refresh(); // plugged in, then the dropdown opened
        Assert.Equal("USB mic", picker.Selected!.Label);
        Assert.Null(picker.Note);
    }

    [Fact]
    public void WithOneMicrophoneOrTheFakeMicThereIsNothingToChoose()
    {
        Assert.False(Picker(new FakeRecorder(new AudioInputDevice("Built-in", true)), new MemorySettings()).IsVisible);
        using var replay = new FileReplayRecorder(Path.Combine(_root, "none.wav"));
        Assert.False(Picker(replay, new MemorySettings()).IsVisible);
    }
}
