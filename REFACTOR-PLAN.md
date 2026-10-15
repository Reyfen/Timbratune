# Timbratune refactoring plan

*Status:* agreed 2026-10-15; P0 in progress (all but the phone baselines, HANDOFF §8i).
*Base:* `dev/v0.2.1` at `2b33f06`.
*Work branch:* `dev/v0.3.0`.

This is the target architecture and the order of work for splitting Timbratune into independent feature modules and per-metric analysis modules. It also prepares for wider audio import, opt-in analytics, and later accounts and a browser build.

## How to use this document (for agents)

- **Read first:**
  - `CLAUDE.md` for the ground rules: commit only when asked, no Praat code, cross-platform, never touch the user's takes or running app, measure before claiming.
  - The relevant `HANDOFF.md` section, before touching live analysis, smoothing or performance (§8, §8d).
- **Order of work:** do the phases in §9 in order. Each phase is small enough to review on its own, and ends green: build, tests, a UI check and the performance gates where they apply.
- **Progress:** tick the checklist in §9 as phases land. Record measured numbers in `HANDOFF.md`, not here.
- **Decisions:** if reality contradicts this plan (an API doesn't exist, a gate can't be met), stop and ask the owner. Don't silently change direction. Open questions are listed in §12.
- **Scope:**
  - *In this refactor:* §1–§5 and the "seams" in §6 and §7.
  - *Later versions:* the account and avatar modules (§6) and the browser head (§7).

---

## 0. Why

**Today**, every feature passes through a few classes:
- **UI** (`src/Reyfen.Timbratune`):
  - **`MainViewModel`** (377 lines) creates every other view model. It owns import, export, rename, delete, selection, the metric modal, the take dialog and the trend backfill.
  - **Views reach the parent view model** through `$parent[v:MainView].((vm:MainViewModel)DataContext)` (`MainView.axaml:38,39,72,73,91`) and an ancestor lookup in `RecordingCardView.axaml.cs:19-21`.
  - **No navigation, no DI.** The heads build an `AppServices` record by hand (`Services/IFileDialogs.cs:39-50`, `App.ServicesFactory`).
- **Analysis** (`src/Reyfen.Timbratune.Core/Analysis`): there is no per-metric abstraction. Adding one metric touches about 15–19 files, through parallel lists that must stay in step:
  - `RawAnalysisAssembler` uses a string-keyed summary dictionary.
  - `AnalysisPostProcessor` (470 lines) builds every metric.
  - `TakeViewModel` addresses its stat cards by index (`StatCards[0..6]`).
  - `LiveTimelinesViewModel` must keep `Timelines` and `Lines` in the same order.

**Goal:**
- Each feature lives in its own folder with a **View, ViewModel ("view controller"), Manager and Router**.
- Each metric is a self-contained **metric module**.
- Adding a feature or metric happens in one place and cannot silently break the others.

**Decisions already taken by the owner:**
- Features are **folders inside the existing UI project**, not separate projects.
- Platform adapters are separate projects.
- Composition uses **`Microsoft.Extensions.DependencyInjection`** (MS.DI).
- Analysis is split into **per-metric modules**.
- **No noticeable performance loss** is acceptable for the sake of architecture (§3).

**Invariants.** Tests enforce these; never break them:
- **Live and saved analysis stay in parity.** Pitch, jitter, shimmer and phrases match exactly; the rest within the tolerances in `LiveAnalysisTests`. On Stop the WAV is trimmed by ≤ 10 ms so the frame grids line up.
- **The `Recording` JSON shape and key order stay the same** (`CoreTests.WritesTheSameKeysAnalyzePyWrites`). Parity with `analyze.py` is checked by `ParityTests`.
- **The `RawAnalysis` contract stays the same.** The Praat oracle (`tests/.../PraatReference/PraatAnalysisEngine.cs`, `analyze.praat`) produces it.
- **These stay public:** `AcousticsAnalysisEngine.Measure` / `MeasureTracks` and the `LoudOutlierFraction` init property.
- **Acoustics:** zero dependencies, trim/AOT safe, MIT, no Praat code.
- **Data on disk:** the take folder layout (`takes/<take>/take.wav|take.json|detail.json|series.json`) and the old-layout conversion keep working byte for byte.

---

## 1. Target structure

`(later)` marks parts that are planned but not built in this refactor.

```
src/
  Reyfen.Timbratune.Acoustics/          unchanged: DSP kernels + Streaming/ live trackers
  Reyfen.Timbratune.Core/               no UI, no platform code
    Domain/                             Recording models, Zones, MetricId, value types (no I/O)
    Analysis/
      Pipeline/                         AnalysisPipeline (saved), LiveAnalysisPipeline (live), TrackView
      Tracks/                           shared measurements + derived tracks (§2.1)
      Metrics/                          IMetricModule, MetricRegistry, one folder per metric (§2.2)
      Compat/                           RawAnalysis + string-key adapter for the Praat oracle
      IAnalysisScheduler.cs             where live/saved analysis runs (§7 seam)
    Audio/                              IAudioRecorder, IAudioPlayer, WavFile/WavWriter
      Decoding/                         format catalog, sniffer, decoder ports, registry (§4)
    Storage/                            ITakeStorage + IDataStore ports (§7 seam), RecordingStore (repository
                                        over ITakeStorage), FileSystemTakeStorage (today's layout), DataPaths,
                                        TakeArchive
      Settings/                         SettingKey<T>, SettingScope, SettingsService, ISettingsStore, migrations (§5a)
  Reyfen.Timbratune/                    Avalonia UI library
    App/                                App.axaml(.cs), CompositionRoot (AddTimbratune), ViewLocator (hand-written map)
    Shell/                              ShellView (was MainView), ShellViewModel, overlay stack, back/Esc handling
    Shared/                             ports and state that features share; no feature code
      Navigation/                       IOverlayHost, IRouter base
      Messaging/                        IMessenger + message records (TakeSaved, TakeRenamed, TakeDeleted, ImportFinished…)
      State/                            IActiveTakeService, ITakeCatalog, ILiveSession, ISettings (§5a), IEntitlements (later)
      Platform/                         IFileDialogs, IFileSharer, IPermissions, IPlatformInfo, ISecureStore,
                                        IExternalBrowser, IProcessRunner, IHttpDownloader
      Analytics/                        IAnalytics, TakeAnalyzedEvent, NullAnalytics (the port only, §5)
      Controls/  Themes/  Diagnostics/
    Features/                           each: View(s), ViewModel(s), Manager, Router, Add<Feature>() registration
      Recording/                        record panel; RecordingManager owns recorder + live session + save
      LiveMonitor/                      live timelines; smoothing (RecentWindow) per snapshot
      TakeLibrary/                      take switcher + recordings card grid
      TakeDetails/                      stat cards, gauges, trends (also used for the live take)
      MetricCompare/                    metric comparison modal (takes + reference voices)
      TakeActions/                      per-take dialog: save/export/share, rename, delete
      Import/                           picker, dropped-folder scan, trend backfill, FFmpeg offer (§4)
      Analytics/                        MixpanelAnalytics + consent toggle (§5)
      CheatSheet/  About/               static content; footer (version, credits, data folder)
      Account/  Avatar/                 (later, §6)
  Reyfen.Timbratune.Audio.SoundFlow/    exists: recorder, player, miniaudio decoder provider
  Reyfen.Timbratune.Audio.MediaFoundation/   Windows decoder provider (§4)
  Reyfen.Timbratune.Audio.Android/      MediaCodec decoder provider (moved out of the head)
  Reyfen.Timbratune.Audio.Apple/        AVFoundation / afconvert providers (when iOS/macOS heads exist)
  Reyfen.Timbratune.Audio.Ffmpeg/       ffmpeg-process decoder provider + FfmpegInstaller
  Reyfen.Timbratune.Audio.Web/  Reyfen.Timbratune.Storage.Web/   (later, §7)
  Reyfen.Timbratune.Desktop/  Android/  Browser (later)   heads: register platform adapters, call AddTimbratune()
```

### 1.1 The four parts of a feature module

| Part | Does | May depend on | Must not |
|---|---|---|---|
| **View** (`.axaml` + minimal code-behind) | layout and bindings; code-behind only for platform input (focus, `InputPane`, back key) | its ViewModel | use `$parent` view-model bindings, or reference other features |
| **ViewModel** ("view controller") | presentation state, commands, UI-thread marshalling | its Manager, its Router, `Shared/State` | do file I/O, call `RecordingStore` directly, or `new` another feature's view models |
| **Manager** | the feature's use cases (record/stop/save, rename/delete, export, import, backfill); async; UI-free | Core, `Shared/*` ports, `IMessenger` | reference `Avalonia.*` |
| **Router** | opens and closes overlays and other screens (`OpenMetric(id)`, `OpenTakeActions(takeId)`, `Close()`) on top of `IOverlayHost` | `Shared/Navigation` | contain business logic |

**Dependency rules** (enforced by the architecture tests in phase 0):
- `Features/X` never references `Features/Y`. Cross-feature contracts live in `Shared/`.
- Only `App/CompositionRoot` and `Shell` see all features.
- Managers never reference `Avalonia`.
- Core never references the UI project or any adapter project.
- Acoustics references nothing.

### 1.2 How modules talk

- **Selection:** `IActiveTakeService` replaces `MainViewModel.Active` and `UpdateActive` (`MainViewModel.cs:302-316`).
- **Take list:** `ITakeCatalog` (an observable list of takes, refreshed by managers) replaces `Items` and `ReloadAsync`.
- **Events:** messages via `IMessenger` (CommunityToolkit) replace:
  - the `_onRecorded` delegate;
  - the `PropertyChanged` subscriptions in `MainViewModel` (:31-34, :199, :223-235);
  - the delete/export/rename delegates in `RecordingItemViewModel` (:23-25).
- **Live:** `ILiveSession` (in `Shared/State`) is owned by `RecordingManager`.
  - Its `SnapshotReady` event fires **on the analysis thread**. LiveMonitor's smoothing (`LiveTimelinesViewModel.Compute` today) keeps running there, as it does now on `LiveWorker`.
  - Only the finished frame is marshalled to the UI thread.
  - TakeDetails shows the live take from the same snapshot.
- **Overlays:** the Shell holds one overlay stack. Routers push `MetricModalViewModel` and `TakeDialogViewModel`; Esc and Android back pop it. This replaces `Modal`, `TakeDialog` and `CloseModalCommand`.
- **Views:** `ViewLocator` is a **hand-written** view-model-type → view map. It uses no reflection, so it is AOT-safe and fast at startup.

### 1.3 Composition

- **Feature registration:** every feature has `services.Add<Feature>Feature()`. `AddTimbratune()` registers Core, Shared and all features.
- **Platform adapters:** each head registers its own.
  - **Desktop:** SoundFlow, `StorageProviderDialogs`, Media Foundation (Windows only), FFmpeg.
  - **Android:** SoundFlow, MediaCodec, `AndroidFileSharer`, microphone permission, thread priority.
- **What this removes:**
  - `App.ServicesFactory` and the `AppServices` record.
  - The Android statics `s_audio`/`s_recorder`, which become singletons registered by the Android head. `s_current` (the live Activity) stays in the head.
- **`--import` on desktop** (`Program.cs:49-77`) resolves `ImportManager` from the same container.
- **`Features.cs` is split:**
  - `AppInfo`: version, credits, links.
  - `IFeatureFlags`: `ReferenceVoices`.
  - `IPlatformInfo`: `IsMobile`, `IsBrowser`, `SupportsThreads`, `CanOpenDataFolder`, `CanShare`, `CanRunProcesses`. It replaces `Features.IsMobile` and the `OperatingSystem.Is*` checks in shared code.
- **DI style:** constructor injection only, no assembly scanning; singletons are resolved once.

---

## 2. Analysis: tracks and metric modules

Metrics share measurements: the pitch contour feeds pulses, the loud-vowel gate, formants and weight, and intensity feeds the gate and the sounding intervals. So the split has **two layers**.

### 2.1 Tracks (`Analysis/Tracks/`)

Tracks are shared measurements, computed once per update. Each has a **saved** provider (wrapping today's `MeasureTracks` code) and a **live** provider (wrapping today's `Live*Tracker`s). Both call the same Acoustics kernels, which keeps parity by construction.

| Track | Built from today's |
|---|---|
| `PitchTrack` | `PitchAnalyzer.Autocorrelation` / `LivePitchTracker` |
| `IntensityTrack` | `IntensityAnalyzer` / `LiveIntensityTracker` |
| `HarmonicityTrack` | `HarmonicityAnalyzer.CrossCorrelation` / the live harmonicity tracker |
| `FormantTracks` | both ceilings with one shared resample (`BurgAll`, `LiveFormantTracker.UpdateAll`) |
| `LtasTrack` | `Ltas.FromSound` (live: only when final) |
| `PulseTrack` | `PulseDetector` / `LivePulseTracker`; needs pitch |
| `VoicedFrames`, `Sounding` | voiced frames from pitch; `SilenceDetector` on intensity (-25 dB) |
| `LoudVowelGate` | `LoudVoicedTimes` + `LoudReference`; needs pitch and intensity |
| `VowelFormantRows` *(derived)* | `VowelCoreFormants` + the F2 stability gate and ceiling choice from `VowelFormants` |
| `WeightFrames` *(derived)* | `WeightFrames` / `MeasureWeightRow` (memoised as today); needs pitch and formants |

The two derived tracks are tracks rather than module internals because several modules need them. Phrasing reads the kept F2 rows and the per-frame corrected weight; trends and series read them too. Computing them once also removes today's three re-derivations of weight and H1-A3 (in `Process`, `FrameSeriesBuilder` and the trends).

**Scheduling.** Tracks are computed **eagerly in fixed stages, never lazily on first use**, so today's parallelism is kept:
1. **Stage 1** is today's `Parallel.Invoke`: pitch, intensity, HNR, formants, LTAS. It still runs under `Parallelism.Options`.
2. **Stage 2:** pulses, voiced frames, sounding, gate.
3. **Stage 3:** the derived tracks.

Only the tracks that registered modules need are run (`TrackSet` flags). `TrackView` is a `readonly struct` with one typed field per track; there are no dictionary or `Type` lookups.

### 2.2 Metric modules (`Analysis/Metrics/<Name>/`)

```csharp
public interface IMetricModule
{
    MetricId Id { get; }                 // int ordinal assigned by MetricRegistry + stable string name
    TrackSet RequiredTracks { get; }     // bit flags
    // Hot path (~10×/s live): append only new frames into pooled, double-buffered buffers
    // the module owns. Must not allocate.
    void UpdateLiveSeries(in TrackView tracks, int fromFrame);
    // Cold path: the ~1 s "full" live update, Stop, and the saved analysis only.
    void Summarize(in TrackView tracks, RecordingBuilder recording);
    void Detail(in TrackView tracks, DetailBuilder detail);   // phrase metrics, trend column
    void WriteSeries(SeriesWriter series);                    // series.json entries
    MetricPresentation Presentation { get; }  // static UI data: title, unit, zones, lo/hi, blurb,
                                              // stat card, trend chart, live timeline + SmoothingSpec,
                                              // chip, reference-voice accessor, analytics zone name
}
```

| Module | Takes over from today's code |
|---|---|
| Pitch | the pitch part of `Process`, pitch trends, the `pitch` series |
| Resonance (F1–F3) | `VowelFormants` medians, `UsableFormantPct`, the f1/f2/f3 series, the F2/F3 trend columns |
| Weight | `SpectralWeight`, `CorrectedH1A3`, the weight series and trend |
| Loudness | intensity summary, loudness series and trend |
| Clarity | HNR summary, series and trend |
| Steadiness | jitter and shimmer (`VoiceReport`) from `PulseTrack`, the jitter series and trend |
| Phrasing | `AnalyzeRegister`, `PhraseBreakdown` (reads tracks only, never other modules) |

`MetricRegistry` holds the ordered module list, and that order is the single source of display order. **Modules never call each other**; they share data only through tracks.

### 2.3 Persisted shapes and compatibility

- **`Recording` stays a fixed POCO** with the same properties in the same order. Each module fills its own section through `RecordingBuilder`. Adding a persisted summary value still means adding one model property, on purpose, because `take.json` is a file-format contract.
- **`TrendSlice` and `PhraseMetrics`:** internally a `double?[]` indexed by metric ordinal. A serializer adapter writes today's v2 field names for the existing metrics and the module's string name for new ones, so the existing JSON is unchanged.
- **`TakeSeries` keys** come from each module's `WriteSeries`, and stay the same strings as today (`pitch`, `loudness`, `hnr`, `f1`–`f3`, `weight`, `jitter`).
- **`Compat/RawAnalysisAdapter`** keeps `RawAnalysis` and its string summary keys, mapping them to and from the tracks, so `PraatAnalysisEngine` and `analyze.praat` stay as they are. String keys exist only here.

### 2.4 The UI follows the registry

- **`TakeViewModel`** builds its stat cards and trend charts by iterating `MetricRegistry` and each module's `Presentation`. It looks up by `MetricId`, never by index.
- **LiveMonitor** builds its timelines and the matching smoothing lines from the same list. That removes the hidden ordering coupling between `Timelines` (:100-110) and `Lines` (:155-164).
- **Chips** (`RecordingItemViewModel` :49-54) and **`MetricModalViewModel`** read `Presentation` too.
- **`Domain/Metrics.cs` / `MetricDef`** become a thin view over the registry during the transition, then are deleted.
- **`ScaleTick` and `TickKind`** move out of `Controls/MetricScale.cs` into Domain, and the trend-point types out of `Controls`, so view models stop depending on controls.

---

## 3. Performance: no noticeable regression allowed

**Where time goes today** (HANDOFF §8):
- A live update takes about 50 ms on the Pixel 9 and 70 ms on the 4a (Release, LLVM AOT).
  - It runs on one low-priority `LiveWorker` thread, starting at most every 100 ms, with nested parallelism capped at 2 cores.
  - The full live update (`Assemble` + `Process`) runs about once a second.
- Stop-to-saved takes 2.4–2.7 s for a 30 s take on the Pixel 9.
- The time is almost all DSP kernels. Past jank came from **CPU contention and GC allocations** (live analysis was cut from 69 to 12 MB/s), not from call overhead.

**What the architecture adds:** about 7 modules × a few interface calls, about 10 times a second. That is microseconds against 50–70 ms (≪ 0.1 %). DI and messaging act only at startup and on user actions. **The real risk is undoing past optimisations**, so these rules are mandatory:

| Risk | Rule |
|---|---|
| the parallel stage silently becomes sequential | eager fixed stages under `Parallelism.Options`; same `LiveWorker`, priority and 2-core limit |
| new per-tick allocations (GC pauses, plus the Java GC bridge on Android) | `UpdateLiveSeries` writes into pooled, double-buffered buffers; no LINQ, closures, boxing or dictionaries on the hot path; `TrackView` is a struct |
| live ticks doing full-analysis work | separate hot (`UpdateLiveSeries`) and cold (`Summarize`/`Detail`) paths; keep "full about every 1 s" |
| lost caches | keep the memoised `WeightRow`, the pooled Burg/FFT/resampler scratch buffers, the shared formant FFT, and `RecentWindow` views and in-place polish |
| strings or dictionaries replacing typed fields | ints for metric ids, arrays for columns; string keys only in `Compat/` |
| slower Android startup | constructor injection, no scanning, hand-written `ViewLocator` |
| interface calls in per-sample loops | forbidden; kernels stay in Acoustics, untouched |

**Expected:** the same speed, or slightly faster, because weight is computed once.

**Acceptance gates.** Baselines are taken in phase 0. Check them after every analysis phase (P5–P7) and in P9:
1. **Identical results.** Run the old engine (base commit) and the new one over the same WAVs (the VCTK fixtures plus `long.wav`) and diff `take.json`, `detail.json` and `series.json`. Saved values must be identical; live lines may differ by at most 1e-7. This is the same method as the `b5d73f4` comparison in HANDOFF §8.
2. **`tools/analysis-bench`** (new in P0, Stopwatch plus `GC.GetAllocatedBytesForCurrentThread`, no new package). It measures:
   - the full analysis;
   - `LiveAnalyzer.Update` per tick, light and full;
   - live allocation in MB/s, with the `AllocationTick` listener (HANDOFF §8d).

   **Budget:** at most +3 % time, and **no more allocations than the baseline**.
3. **Phones** (Release profiling build, HANDOFF §8d):
   - Pixel 9: ≥ 57.5 fps and ≤ 3.1 % janky while recording; stop-to-saved ≤ 2.7 s.
   - Pixel 4a: re-measure against its own phase-0 baseline.
4. **On a miss:** fix it or revert that step before continuing.

---

## 4. Import: almost any audio format, on every platform

**Today:**
- `TakeImporter` hard-codes `.wav`, `.mp3` and `.flac` by extension (`TakeImporter.cs:15,24,92`).
- WAV goes through `WavDecoder`; the rest goes through `IAudioDecoder.DecodeToWav`, implemented by `SoundFlowAudio` (miniaudio). It streams the result to a 44.1 kHz mono PCM16 WAV.

### 4.1 Architecture (ports and adapters; the shared code knows no platform)

```
Core/Audio/Decoding/
  AudioFormat            record: Id, DisplayName, Extensions[], MimeTypes[], AppleUtis[], Signature
  AudioFormatCatalog     the one list of known formats: wav, aiff, mp3, flac, ogg-vorbis, opus, aac/m4a, alac,
                         wma, amr, caf, webm, mkv, mp4/mov audio…
  FormatSniffer          format from magic bytes via the catalog (RIFF/RF64, FORM, fLaC, ID3/MPEG sync,
                         OggS + codec header, ftyp brand, EBML, caff, ASF, #!AMR); never trusts the extension
  AudioSource            path or stream (Android picker files have no path) + name + MIME
  IAudioDecoderProvider  Id, Priority; ValueTask<FormatSet> ProbeAsync(ct) = what it decodes on this device now;
                         IAudioDecoder Create()  (heavy objects are created only here, only during an import)
  IAudioDecoder          Task DecodeAsync(AudioSource, IPcmSink, IProgress<double>?, ct)
                         decoders emit PCM at the file's own rate and channels
  AnalysisWavSink        the one place that converts: mono downmix + Acoustics Resampler → 44.1 kHz → WavWriter;
                         streaming; passes audio already at 44.1 kHz mono straight through
  IDecoderInstaller      optional per provider: CanInstall, Describe() (size, licence, source), InstallAsync
  DecoderRegistry        aggregates providers, caches the probed FormatSet, raises CapabilitiesChanged;
                         decode = sniff → providers that support it, by priority → on failure try the next
```

- **Migration:** the current `Core/Audio/IAudioDecoder.cs` (`DecodeToWav`) is replaced by this.
- **`TakeImporter`** becomes part of `Features/Import/ImportManager`, plus a Core `TakeImportService` that the desktop `--import` CLI also uses.
- **`Features/Import`** knows only `DecoderRegistry` and `IDecoderInstaller`.
- **Swapping or adding a decoder** is one registration line in a head (`services.AddFfmpegDecoder()` and so on); priorities can be overridden in settings.

| Priority | Provider (project) | Registered on | Formats |
|---|---|---|---|
| 1 | managed WAV/AIFF (Core) | all | WAV PCM 8/16/24/32, float, `WAVE_FORMAT_EXTENSIBLE`, RF64; AIFF/AIFC |
| 2 | miniaudio (`Audio.SoundFlow`) | all native heads | MP3, FLAC, WAV. Kept first for these, still asking miniaudio for 44.1 kHz mono, so **existing imports decode exactly as before** |
| 3 | Media Foundation (`Audio.MediaFoundation`, source-generated COM interop) | Desktop, only on Windows | AAC/M4A, ALAC, WMA, MP4/MOV/MKV audio; Opus/Vorbis/WebM when the store's Web Media Extensions are installed |
| 4 | MediaExtractor + MediaCodec (`Audio.Android`) | Android | AAC/M4A, 3GP/AMR, Vorbis, Opus, FLAC, MP3, WebM/MKV, audio of videos |
| 5 | AVFoundation / `afconvert` (`Audio.Apple`) | iOS / macOS | AAC, ALAC, CAF, AIFF, M4A, MP3 |
| 6 | ffmpeg process (`Audio.Ffmpeg`, through `IProcessRunner`) | Desktop, any OS | practically everything, if ffmpeg is installed or was downloaded (§4.3) |

**FFmpeg is not bundled:**
- it would add about 20–40 MB per platform;
- it brings LGPL obligations;
- the App Store makes it awkward;
- the OS decoders already cover Windows, Android and Apple.

Linux is the one platform without a universal system decoder, hence §4.3.

**For every decoder:**
- **Video files:** the first audio track is used.
- **The original is kept** as `source.<ext>` for any non-WAV import. Today that happens only for dropped MP3/FLAC files.
- **Factories:** decoders are registered as factories, so nothing is created when no import is running.

### 4.2 The picker shows only formats this device can decode

- **Probing:** after first paint, in the background, `DecoderRegistry` probes every provider once and caches the result.
  - **Static sets:** managed, miniaudio, Apple.
  - **Media Foundation:** `MFTEnumEx`.
  - **Android:** `MediaCodecList`.
  - **FFmpeg:** whether it is present, then `ffmpeg -hide_banner -decoders` / `-demuxers`.
- **Filters:** `ImportRouter.PickFilesAsync` builds Avalonia `FilePickerFileType`s from the probed `FormatSet` via `AudioFormatCatalog`:
  - `Patterns` for Windows/Linux;
  - `MimeTypes` for Android and Linux portals;
  - `AppleUniformTypeIdentifiers` for macOS/iOS;
  - one "All supported audio" group plus one group per format, and **never** "All files" / `*`.
  - `.tmbr` is listed as its own group.
- **Dropped files:** files dropped into the takes folder are filtered the same way. Unsupported ones are left alone and mentioned once in an info line.
- **Refresh:** `CapabilitiesChanged` (for example after installing FFmpeg) re-probes, so the next picker shows the new formats.
- **Android caveat:** some file managers ignore MIME filters, so the sniffer still rejects unsupported files with a clear message.

### 4.3 Linux: offer to download FFmpeg

`FfmpegInstaller : IDecoderInstaller` works through `IHttpDownloader` and `IProcessRunner`, so it can be tested.

1. **When:** the offer appears when the user opens Import (or settings) and ffmpeg isn't found, or when a file is rejected for lack of a decoder. It reads: *"More formats (M4A, Opus, OGG, WMA, video…) need FFmpeg. Download it for Timbratune (about 30 MB)?"* Options: **Download** / **Use my package manager** / **Not now** (remembered). Nothing is downloaded without a click.
2. **Download:**
   - **What:** a pinned LGPL-only static build, for example BtbN FFmpeg-Builds `linux64-lgpl` / `linuxarm64-lgpl`.
   - **Pinning:** the URL, version and **SHA-256** per CPU architecture are compiled into the app and updated with releases. HTTPS, with progress and cancel.
   - **Verification:** the hash is checked **before** extraction. On a mismatch the file is deleted and the download fails.
   - **Where:** extracted with the system `tar` (.NET can't read `.tar.xz`) into `$XDG_DATA_HOME/Timbratune/tools/ffmpeg/<version>/`. Only `ffmpeg` and the licence files are kept, marked executable.
   - No root access and nothing system-wide. Takes and tools stay separate.
3. **Package manager:** detect `apt`, `dnf`, `pacman` or `zypper`, show the exact command to copy (for example `sudo apt install ffmpeg`), and re-probe on "Done". The app never runs `sudo`.
4. **Flatpak** (if ever shipped that way): use the `org.freedesktop.Platform.ffmpeg-full` extension; the installer reports itself unavailable.
5. **Licensing:** FFmpeg is a separate process, downloaded at the user's request, so Timbratune stays MIT. The prompt and About name FFmpeg's LGPL licence and link its source.
6. **Removal and updates:** "Remove FFmpeg" in settings removes it and re-probes. A new pinned version is offered the same way after an app update.
7. **Packages:** `package-linux.sh` lists ffmpeg as a *recommended* dependency.

---

## 5. Usage analytics (Mixpanel)

**Requirements from the owner:**
- Mixpanel.
- Only **final results after an analysis**, never live data.
- The bare minimum of information.
- Lightweight, with **zero allocations when disabled**.

**Structure:**
- **The port** is in `Shared/Analytics`, so other features' managers can call it without depending on `Features/Analytics`:
  - `IAnalytics { bool IsEnabled; void TakeAnalyzed(in TakeAnalyzedEvent e); }`
  - `TakeAnalyzedEvent`, a `readonly record struct`
  - `NullAnalytics.Instance`
- **The implementation** is in `Features/Analytics`:
  - `MixpanelAnalytics`: `HttpClient` POST to `/track`, or `api-eu.mixpanel.com`; no SDK.
  - A source-generated `System.Text.Json` context.
  - `AnalyticsSettings`: opt-in flag plus a random install id, both `Device`-scoped settings (§5a).
  - The consent toggle view/view model, in About.

**When disabled, nothing is allocated:**
- DI registers `NullAnalytics.Instance` when the user hasn't opted in, or when no token was compiled in (`-p:MixpanelToken=…`, so the repo holds no token).
- No `HttpClient`, JSON context, queue or subscription is created.
- **Call sites:** only `RecordingManager` (after the saved analysis on Stop) and `ImportManager` (after an import), as `if (_analytics.IsEnabled) _analytics.TakeAnalyzed(new(...));`.
- Live analysis never references analytics; an architecture test checks this.

**When enabled:**
- One event per finished take, sent fire-and-forget in the background.
- Dropped on failure: no retry queue, no persistence, no batching.
- Turning the toggle off swaps in `NullAnalytics` and disposes the client.

**Payload** (proposed minimum; the owner confirms, §12):
- **Event:** `take_analyzed`.
- **Identity:** `distinct_id` = random per-install GUID, plus `ip=0`.
- **App:** `app_version`, `platform`.
- **Take:** `duration_s` (rounded), `source` (recorded/imported).
- **Results:** a **zone name per metric** (from `Presentation`), not raw Hz/dB values.

**Privacy:**
- Voice results for gender-voice training are sensitive personal data. Analytics is therefore **opt-in, default off**.
- No labels, file names or audio, and not linked to accounts (§6).
- Document it in the README.

**Tests:**
- The `NullAnalytics` path allocates 0 bytes (`GC.GetAllocatedBytesForCurrentThread`).
- The serialised payload contains only whitelisted keys.

---

## 5a. Settings that persist between sessions (local now, cloud sync later)

**Today** nothing is saved between sessions: the theme toggle, live window choice and similar options reset on every start. **Goal:**
- typed settings that persist on every platform;
- each setting marked **device-only** or **synced**;
- synced settings later follow the user's account to other devices (§6).

### 5a.1 Declaring settings: each feature owns its own

```csharp
public enum SettingScope { Device, Synced }          // secrets are never settings: they go to ISecureStore (§6)

public sealed record SettingKey<T>(string Id, T Default, SettingScope Scope, JsonTypeInfo<T> Json);

// in Features/Recording/RecordingSettings.cs (example)
public static class RecordingSettings
{
    public static readonly SettingKey<string> LiveWindow =
        new("recording.liveWindow", "10s", SettingScope.Synced, SettingsJson.Default.String);
}
```

- **Ids** are namespaced by feature (`recording.*`, `import.*`, `shell.*`) and never reused.
- **Registration:** each feature registers its keys in `Add<Feature>Feature()` (`ISettingsRegistry`). There is no central settings class to edit, so features stay independent.
- **AOT:** values are serialised with source-generated `System.Text.Json` (`JsonTypeInfo<T>`), so it is trim/AOT safe.

**Which scope, as a starting point** (the owner confirms; §12):

| Scope | Examples |
|---|---|
| **Device** | window size/position, Android renderer, data folder, FFmpeg path / "not now" / installed version, decoder priorities, analytics opt-in and install id (consent is per device), last-seen version, performance flags |
| **Synced** | theme, live window length, display preferences (units, which cards are shown), cheat-sheet/tips dismissed, reference-voice choice |
| **Never a setting** | OAuth tokens and secrets (`ISecureStore`), takes and analysis (`ITakeStorage`), anything about the user's voice |

### 5a.2 The service

- **Port:** `Shared/State/ISettings`, with `T Get<T>(SettingKey<T>)`, `void Set<T>(SettingKey<T>, T)` and a `Changed` event carrying the key.
- **Reads** come from an in-memory snapshot loaded once at startup. Features read a setting once and subscribe; settings are never read on the analysis hot path.
- **Writes** update memory at once and are saved **debounced** (about 0.5 s) on a background task. The file is written atomically: a temp file, then a rename.
- **Format:** two JSON documents, each with a `schemaVersion`:
  - `settings.device.json`;
  - `settings.synced.json`, the local copy of the synced settings.
- **Upgrades:** migrations run on load.
- **Different app versions on different devices:** unknown keys are **kept and written back unchanged**, so an older app version never deletes a newer version's settings.
- **Corruption:** a damaged file falls back to defaults and is kept as `.bak`, so one bad file never stops the app from starting.

**Storage port:** `ISettingsStore`, which loads and saves one document by name. It sits on `IDataStore` (§7, phase P2):

| Platform | Location |
|---|---|
| Windows / Linux / macOS | the data folder (`%APPDATA%\Timbratune`, `$XDG_DATA_HOME/Timbratune`, `~/Library/Application Support/Timbratune`); `TIMBRATUNE_DATA_DIR` moves it with the takes |
| Android | the app's internal `FilesDir`, not the external folder where takes live |
| iOS | Application Support |
| Browser (later) | OPFS or `localStorage` |

### 5a.3 Cloud sync (later, together with accounts in §6)

- **What syncs:** only `Synced` keys, only when the user is signed in and has sync on. There is a "Sync settings" toggle, default **on** once signed in.
- **Port:** `ISettingsSyncProvider` in `Shared/Platform`, implemented in a separate adapter project. Pull and push the synced document, keyed by the account.
- **Recommended backend:** the **same small serverless backend as the Patreon token exchange** (§6). It stores one small JSON document per account id, so it works the same on Windows, Linux, Android, iOS, macOS and the browser, whatever the sign-in provider.
  - Platform services (iCloud key-value store, Google Play Saved Games) only cover some platforms. They could be added later as extra adapters behind the same port.
- **Offline first:** the local copy is always the source the app reads. Sync runs on start, on resume, and debounced after a change.
- **Merging:** each synced key stores `{value, updatedAt, deviceId}`; the newer `updatedAt` wins, and `deviceId` breaks ties. Removed keys leave a tombstone. This means two devices changing *different* settings never overwrite each other.
- **Sign-out** keeps the local copy and stops syncing.
- **Device restore:** Android Auto Backup rules include only the settings files. Takes stay excluded, given the size limit and privacy.
- **Privacy:** synced settings hold no voice data, labels or results. Syncing takes is a separate, later decision, not part of this design.

### 5a.4 Tests

- Round-trips, defaults and migrations.
- Unknown keys are preserved, and a corrupt file falls back to defaults.
- Debounced, atomic save (kill-during-write leaves the old file).
- Merge rules: per-key last-writer-wins, tombstones, ties.
- An architecture test: features use only `ISettings`, never files.
- A test that no `SettingKey` id contains "token" or "secret".

---

## 6. Later: accounts and the supporter avatar (seams now, modules in a later version)

**Purpose:**
- Optional sign-in with **Google Play** (Android), **Apple** (iOS/macOS), or **Patreon** (all platforms; the one that proves supporter status).
- It only unlocks a private, local avatar / character customisation as a thank-you. Nothing is shared with other people.
- The app stays fully usable without an account.

```
Shared/Platform/   IAuthProvider (Id, IsAvailable, SignInAsync, SignOutAsync → AuthResult(ProviderId, AccountId,
                   DisplayName?), no email), ISecureStore (Android Keystore, Keychain, DPAPI, libsecret),
                   IExternalBrowser (system-browser OAuth, loopback or custom-scheme redirect)
Shared/State/      IEntitlements (IsSupporter, tier), so Avatar never references Account
Features/Account/  AccountManager, EntitlementManager (offline cache with a refresh date), sign-in sheet + router
Features/Avatar/   AvatarManager (avatar.json in the data folder, never uploaded), AvatarCatalog (each item names
                   the entitlement that unlocks it), editor view + router, AvatarBadge in the Shell header
```

**Constraints:**
- **Patreon needs a tiny serverless backend** for the OAuth token exchange, because the client secret can't ship in the app. It returns only the tier. Apple sign-in off Apple platforms would need the same.
- **Entitlements work offline** after one check. Expiry is a grace period, and the core app is never locked.
- **Privacy:** keep only the provider account id and the entitlement. Not linked to analytics or takes.
- **Cost when unused:** providers are factories, so nothing is created until the sign-in sheet opens.
- **Dependencies:** prefer plain OAuth. Vendor SDKs (Play Games) stay in the head that needs them.

**Seams in this refactor:**
- `ISecureStore` and `IExternalBrowser` in `Shared/Platform`.
- An empty header slot in the Shell.
- The settings service with `Device`/`Synced` scopes (§5a). Cloud sync then only needs `ISettingsSyncProvider`.

---

## 7. Later: WebAssembly (browser) head (seams now, head in a later version)

A future head, `Reyfen.Timbratune.Browser` (`Avalonia.Browser`, `net10.0-browser`), served as a static site.

| Area | Browser problem | Plan |
|---|---|---|
| Microphone | no miniaudio; needs HTTPS and a user gesture | `Audio.Web`: `getUserMedia` + an `AudioWorklet` posting float PCM through `[JSImport]`/`[JSExport]`; `AudioContext({sampleRate: 44100})`, otherwise the Acoustics `Resampler`; implements `IAudioRecorder` |
| Playback | no miniaudio | `AudioContext` or an `<audio>` element over a blob URL; implements `IAudioPlayer` |
| Import | no native decoders or FFmpeg | `WebAudioDecoderProvider` (`decodeAudioData`; probe with `canPlayType`), plus the managed WAV/AIFF decoder. FFmpeg installer unavailable. |
| Takes storage | .NET's virtual file system is in memory only | `Storage.Web`: `ITakeStorage` over the Origin Private File System, plus `navigator.storage.persist()`; quota shown in About |
| Export/share/pickers | no paths | Avalonia's browser `StorageProvider`, with `<input type=file>` / download fallbacks (Firefox, Safari); Web Share API on mobile; no "open data folder" |
| Threads | single-threaded by default, so a 50 ms update would freeze the page | `IAnalysisScheduler` runs analysis in a **Web Worker** with its own .NET runtime, using transferable buffers; or .NET multithreaded WASM if stable with Avalonia then. Both need COOP/COEP. A spike decides. |
| Speed | interpreter 10–50× slower | WASM AOT + SIMD; if needed, a longer minimum interval between live updates (a scheduler setting, not a code fork) |
| Back | no hardware back | the overlay stack also listens to `popstate` |
| Hosting | GitHub Pages can't set COOP/COEP | Cloudflare Pages / Netlify with a headers file, or the `coi-serviceworker` shim; Brotli; loading screen (expect 10–30 MB) |

**Seams in this refactor.** These are worth doing anyway, for testability:
1. **Storage ports.** Core has about 160 direct `File`/`Directory`/`Path` calls (`RecordingStore`, `DataPaths`, `TakeImporter`). Move them behind:
   - `ITakeStorage`: list, open/create take blobs, rename, delete.
   - `IDataStore`: settings, tools.

   `FileSystemTakeStorage` keeps today's layout byte for byte.
2. **Streams instead of paths.** Add `AnalyzeAsync(Stream | Sound, …)`; the path overload stays. The recorder writes to a storage-provided `Stream`.
3. **`IAnalysisScheduler`** wraps `LiveWorker` and `Parallelism.Limit`. The native implementation is today's behaviour exactly.
4. **`IPlatformInfo`** (see §1.3).
5. **No native-only project** may be referenced from what a browser head would need. The architecture test checks this.

**The later browser phase:**
1. A spike: run the analysis on a VCTK fixture in a worker with AOT; time it; diff the JSON with the desktop output (expect identical values or last-digit `libm` differences); choose the threading model.
2. Add `Audio.Web` and `Storage.Web`.
3. Add a `build.ps1 web` target that writes `publish/Timbratune-web/`, plus a headers config.
4. A Playwright parity test in headless Chromium, plus manual checks in Chrome, Firefox, Safari (desktop and iOS) and Android Chrome.

---

## 8. Glossary

- **Head:** a platform entry project (Desktop, Android, later iOS and Browser). It registers adapters and starts the app.
- **Port / adapter:** an interface in Core or `Shared` / its platform implementation in an adapter project.
- **Track:** a shared measurement over time, computed once per update.
- **Metric module:** consumes tracks and produces one metric's summary, detail, series and presentation.
- **Hot path / cold path:** work done on every live tick / work done only on the ~1 s full update, on Stop, or in the saved analysis.
- **Saved analysis / live analysis:** `AcousticsAnalysisEngine` after Stop or import / `LiveAnalyzer` while recording.

---

## 9. Phases and checklist

Every phase ends with:
- `dotnet build Timbratune.slnx` and `dotnet test Timbratune.slnx` green, with Praat available through `TIMBRATUNE_PRAAT`;
- a desktop UI check with the fake mic;
- no user-visible behaviour change unless the phase says so.

Ask the owner before each commit.

- [ ] **P0. Safety net.**
  - [x] Golden snapshot tests: `take.json`, `detail.json`, `series.json` and the final live snapshot for each VCTK fixture.
  - [x] Reflection-based architecture tests (the rules in §1.1, §5 and §7).
  - [x] `tools/analysis-bench`.
  - Baselines, recorded in HANDOFF §8i:
    - [x] bench numbers (`tools/analysis-bench/baseline-p0.json`) and `tools/live-steadiness`;
    - [ ] Pixel 9 and 4a frame times (needs the owner's phones; before P5 at the latest).
- [ ] **P1. Infrastructure.**
  - MS.DI, `CompositionRoot`/`AddTimbratune()`, `ViewLocator`.
  - `Shared/Navigation`, `Messaging`, `State`, `Platform`.
  - `AppInfo` / `IFeatureFlags` / `IPlatformInfo`.
  - Heads move to DI. `MainViewModel` still exists but gets its services from DI.
- [ ] **P2. Storage and scheduling seams** (§7):
  - `ITakeStorage`/`IDataStore` + `FileSystemTakeStorage`;
  - the settings service (§5a: keys, scopes, file store, migrations; local only). The theme and live-window choice become the first persisted settings;
  - stream-based analysis entry points;
  - `IAnalysisScheduler`.

  Store tests and the migration test must pass unchanged.
- [ ] **P3. UI features, one per step:**
  - [ ] CheatSheet + About
  - [ ] TakeActions
  - [ ] MetricCompare
  - [ ] Import
  - [ ] TakeLibrary
  - [ ] TakeDetails
  - [ ] Recording + LiveMonitor + `ILiveSession`
  - [ ] Shell: `MainViewModel` becomes `ShellViewModel`; remove every `$parent[v:MainView]` binding and the ancestor lookup in `RecordingCardView`.
- [ ] **P4. Import formats** (§4):
  - [ ] Decoding ports + catalog + sniffer + `AnalysisWavSink`, with the managed and miniaudio providers, so behaviour is unchanged.
  - [ ] Probed picker filters.
  - [ ] Media Foundation.
  - [ ] MediaCodec.
  - [ ] FFmpeg provider.
  - [ ] FFmpeg installer (Linux).
- [ ] **P5. Analysis tracks** (§2.1): track providers and staging. `MeasureTracks` and `LiveAnalyzer.Update` are re-implemented on top, with the same signatures. Performance gates apply.
- [ ] **P6. Metric modules** (§2.2), one per step:
  - [ ] Loudness
  - [ ] Clarity
  - [ ] Steadiness
  - [ ] Pitch
  - [ ] Resonance
  - [ ] Weight
  - [ ] Phrasing

  Each step deletes its lines from `RawAnalysisAssembler`, `AnalysisPostProcessor` and `FrameSeriesBuilder`, which end as pipeline glue or disappear. Performance gates apply.
- [ ] **P7. Registry-driven UI** (§2.4). Delete `Metrics.cs` and the positional arrays. Proof: a throwaway dummy metric added in one folder plus one registration shows up in the cards, trends, live timeline and series, then is reverted.
- [ ] **P8. Analytics** (§5): port, Mixpanel adapter, consent toggle, two call sites, tests. Verify against a Mixpanel test project, only with the owner's token and opt-in.
- [ ] **P9. Final performance check** (§3, all gates, Release builds on both phones) and a Linux build in WSL `Ubuntu-24.04` (`~/tt` copy).
- [ ] **P10. Documentation:**
  - Update the CLAUDE.md "Layout" table and add a HANDOFF section.
  - Add "How to add a feature" and "How to add a metric" checklists at the end of this file.
  - Mark this plan done.

Later versions: accounts and avatar (§6) with cloud settings sync (§5a.3), and the browser head (§7).

---

## 10. Key files today

- **UI:**
  - `src/Reyfen.Timbratune/App.axaml.cs`
  - `src/Reyfen.Timbratune/Features.cs`
  - `src/Reyfen.Timbratune/Services/IFileDialogs.cs` (`AppServices`)
  - `src/Reyfen.Timbratune/ViewModels/{MainViewModel,RecordViewModel,TakeViewModel,LiveTimelinesViewModel,RecordingItemViewModel,MetricModalViewModel}.cs`
  - `src/Reyfen.Timbratune/Views/MainView.axaml`
  - `src/Reyfen.Timbratune/Analysis/{LiveWorker,RecentWindow}.cs`
- **Analysis:**
  - `src/Reyfen.Timbratune.Core/Analysis/{AcousticsAnalysisEngine,LiveAnalyzer,RawAnalysisAssembler,AnalysisPostProcessor,FrameSeriesBuilder,RawAnalysis}.cs`
  - `src/Reyfen.Timbratune.Core/Domain/{Metrics,Zones}.cs`
  - `src/Reyfen.Timbratune.Core/Models/{Recording,RecordingDetail,TakeSeries}.cs`
  - `src/Reyfen.Timbratune.Acoustics/Streaming/LiveTrackers.cs`
- **Storage and import:**
  - `src/Reyfen.Timbratune.Core/Storage/{RecordingStore,DataPaths,TakeImporter,TakeArchive}.cs`
  - `src/Reyfen.Timbratune.Core/Audio/IAudioDecoder.cs`
  - `src/Reyfen.Timbratune.Audio.SoundFlow/SoundFlowAudio.cs`
- **Heads:**
  - `src/Reyfen.Timbratune.Desktop/Program.cs`
  - `src/Reyfen.Timbratune.Android/MainActivity.cs`
- **Tests:**
  - `tests/Reyfen.Timbratune.Core.Tests/{ParityTests,LiveAnalysisTests,ExportTests,TrendTests,CoreTests,ImportTests}.cs`
  - `.../PraatReference/PraatAnalysisEngine.cs`
  - `tests/Reyfen.Timbratune.Acoustics.Tests/StreamingTests.cs`
- **Kept as they are** (only moved or wrapped):
  - the Acoustics kernels and live trackers
  - `RecentWindow`
  - `PlaybackService`
  - `StorageProviderDialogs`
  - `AndroidFileSharer`
  - `TakeArchive`

## 11. Verification

- **Automated:** `dotnet test Timbratune.slnx`, with Praat available, covering the golden snapshots, parity, `LiveMatchesTheSavedAnalysis`, the architecture tests, and the decoder, picker, installer and analytics tests.
- **Desktop UI:**
  - Build `long.wav` with `scripts/make-long-wav.ps1`, and set `TIMBRATUNE_FAKE_MIC` and a throwaway `TIMBRATUNE_DATA_DIR`.
  - Run `scripts/screenshot.ps1 -ProcId <pid>`.
  - Check record, live graphs, take detail, metric modal, take dialog (rename/export/delete) and import.
- **Android:**
  - Debug APK (`./build.ps1 android debug`) on the `EuphoniaPixel` emulator with the fake mic.
  - Release profiling build on the Pixel 9 and 4a for the gates.
- **Linux:** WSL `Ubuntu-24.04` as `tester` on `~/tt`, including import with and without ffmpeg and the download offer.

## 12. Open decisions (ask the owner when the phase is reached)

1. **P8 analytics:** the exact payload fields, zones vs raw values, and the EU or US Mixpanel endpoint.
2. **P4:** the exact pinned FFmpeg build and its hosting source; whether the installer is also offered on Windows/macOS.
3. **Later, accounts:** where the Patreon token-exchange backend is hosted.
4. **Later, browser:** Web Worker vs multithreaded WASM, decided by the spike, and the hosting provider.
5. **P2 settings:** confirm the device/synced split in §5a.1.
6. **Later, sync:** whether the avatar choice syncs (it is currently local only, §6), and whether takes ever sync (not planned).
