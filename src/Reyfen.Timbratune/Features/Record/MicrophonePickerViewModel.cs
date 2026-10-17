using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage.Settings;

namespace Reyfen.Timbratune.Features.Record;

/// <summary>A microphone choice: a device by name, or the system default (<see cref="Name"/> null).</summary>
public sealed record MicrophoneOption(string? Name, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The microphone picker on the record panel: the input devices the recorder can use, the system
/// default first. The choice is remembered by name (<see cref="RecordSettings.Microphone"/>); one that
/// isn't connected stays selected, marked, and recording uses the default meanwhile. Shown only when the
/// recorder can choose (not with the fake-mic replay) and there is something to choose.
/// </summary>
public sealed partial class MicrophonePickerViewModel : ObservableObject
{
    private readonly IInputDeviceSelection? _devices;
    private readonly ISettings _settings;
    private bool _refreshing;

    public MicrophonePickerViewModel(RecordingManager manager, ISettings settings)
    {
        _devices = manager.InputDevices;
        _settings = settings;
        var remembered = settings.Get(RecordSettings.Microphone);
        if (_devices is not null) _devices.InputDeviceName = remembered.Length > 0 ? remembered : null;
        // Listing the devices asks the audio system: off the UI thread, after startup.
        if (_devices is not null) _ = Task.Run(() => _devices.InputDevices()).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Dispatcher.UIThread.Post(() => Fill(t.Result));
        }, TaskScheduler.Default);
    }

    public ObservableCollection<MicrophoneOption> Options { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Note))]
    private MicrophoneOption? _selected;

    [ObservableProperty] private bool _isVisible;

    /// <summary>Under the picker when the remembered microphone isn't connected.</summary>
    public string? Note => Selected is { Name: { } name } && !_connected.Contains(name)
        ? "that microphone isn't connected · takes record from the system default until it is"
        : null;

    private readonly HashSet<string> _connected = [];

    /// <summary>Lists the devices again (when the dropdown opens: one may have been plugged in).</summary>
    public void Refresh()
    {
        if (_devices is null) return;
        try
        {
            Fill(_devices.InputDevices());
        }
        catch (Exception)
        {
            // Keep the last list: the audio system may be busy.
        }
    }

    private void Fill(IReadOnlyList<AudioInputDevice> devices)
    {
        _refreshing = true;
        try
        {
            _connected.Clear();
            foreach (var d in devices) _connected.Add(d.Name);
            var defaultName = devices.FirstOrDefault(d => d.IsDefault)?.Name;
            var options = new List<MicrophoneOption> { new(null, defaultName is null ? "system default" : $"system default ({defaultName})") };
            options.AddRange(devices.Select(d => new MicrophoneOption(d.Name, d.Name)));
            var remembered = _settings.Get(RecordSettings.Microphone);
            if (remembered.Length > 0 && !_connected.Contains(remembered)) options.Add(new(remembered, $"{remembered} (not connected)"));
            Options.Clear();
            foreach (var o in options) Options.Add(o);
            Selected = options.FirstOrDefault(o => o.Name == (remembered.Length > 0 ? remembered : null)) ?? options[0];
            IsVisible = devices.Count > 1 || remembered.Length > 0;
        }
        finally
        {
            _refreshing = false;
        }
        OnPropertyChanged(nameof(Note));
    }

    partial void OnSelectedChanged(MicrophoneOption? value)
    {
        if (_refreshing || value is null || _devices is null) return;
        _devices.InputDeviceName = value.Name;
        _settings.Set(RecordSettings.Microphone, value.Name ?? "");
    }
}

/// <summary>The record panel's settings.</summary>
public static class RecordSettings
{
    /// <summary>The microphone to record from, by name; "" = the system default. Device-only (a device's own microphones).</summary>
    public static readonly SettingKey<string> Microphone = new("record.microphone", "", SettingScope.Device, SettingsJson.Default.String);
}
