using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Euphonia.Core.Analysis;
using Euphonia.Core.Audio;
using Euphonia.Core.Models;
using Euphonia.Core.Storage;

namespace Euphonia.ViewModels;

public enum RecordState { Idle, Recording, Analyzing, Error }

/// <summary>
/// RecordButton.tsx: label → record → stop &amp; analyze → saved. The take is
/// captured as a WAV, analyzed with Praat, then added to the store (the
/// Electron createRecording IPC flow, minus the base64 round-trip).
/// </summary>
public sealed partial class RecordViewModel : ObservableObject
{
    private readonly IAudioRecorder _recorder;
    private readonly IAnalysisEngine _engine;
    private readonly RecordingStore _store;
    private readonly Func<Task> _onRecorded;
    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _elapsed = new();

    public RecordViewModel(IAudioRecorder recorder, IAnalysisEngine engine, RecordingStore store, Func<Task> onRecorded)
    {
        _recorder = recorder;
        _engine = engine;
        _store = store;
        _onRecorded = onRecorded;
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
            (_, _) => OnPropertyChanged(nameof(ElapsedText)));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsRecording), nameof(IsAnalyzing), nameof(IsError))]
    private RecordState _state = RecordState.Idle;

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string? _errorDetail;

    public bool IsIdle => State == RecordState.Idle;
    public bool IsRecording => State == RecordState.Recording;
    public bool IsAnalyzing => State == RecordState.Analyzing;
    public bool IsError => State == RecordState.Error;
    public string ElapsedText => $"{_elapsed.Elapsed:m\\:ss}";

    /// <summary>Shown under the panel when Praat is missing (recording still works, analysis won't).</summary>
    public string? EngineWarning => _engine.IsAvailable ? null : "⚠️ " + _engine.UnavailableReason;

    [RelayCommand]
    private void Start()
    {
        try
        {
            _recorder.Start();
            _elapsed.Restart();
            _clock.Start();
            State = RecordState.Recording;
        }
        catch (Exception ex)
        {
            Fail("couldn't open the microphone", ex);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _clock.Stop();
        _elapsed.Stop();
        State = RecordState.Analyzing;
        string? wav = null;
        try
        {
            wav = await _recorder.StopAsync();
            var result = await _engine.AnalyzeAsync(wav);
            var entry = result.Metrics;
            entry.Label = string.IsNullOrWhiteSpace(Label) ? "untitled take" : Label.Trim();
            entry.Note = "";
            entry.Date = DateTime.Now.ToString("yyyy-MM-dd");
            entry.SourceFile = Path.GetFileName(wav);
            var sourceWav = wav;
            await Task.Run(() => _store.Add(entry, result.Detail, sourceWav));

            Label = "";
            State = RecordState.Idle;
            await _onRecorded();
        }
        catch (Exception ex)
        {
            Fail("couldn't save that take", ex);
        }
        finally
        {
            if (wav is not null) try { File.Delete(wav); } catch (IOException) { }
        }
    }

    [RelayCommand]
    private void Retry()
    {
        ErrorDetail = null;
        State = RecordState.Idle;
    }

    private void Fail(string what, Exception ex)
    {
        _recorder.Cancel();
        _clock.Stop();
        ErrorDetail = $"{what} 🌧️ — {ex.Message}";
        State = RecordState.Error;
    }
}
