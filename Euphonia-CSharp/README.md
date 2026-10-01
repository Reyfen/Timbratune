# Euphonia (C# / Avalonia)

A C# port of [`../Euphonia-TypeScript`](../Euphonia-TypeScript): record a take and read your pitch, resonance,
weight, and register/phrasing metrics on one dashboard. Same metrics, same zones, and the same `recordings.json`
data format as the Electron app. The look is close but not pixel-identical.

The voice analysis is **pure C#** (`Euphonia.Acoustics`). It needs no Praat, Python, ffmpeg or anything else
installed, so the same code can run on Windows, macOS, Linux, Android and iOS.

## Quick start

Prerequisites: **.NET 10 SDK**. Windows and Linux use the same code; Android adds a workload (see below).

```powershell
dotnet run --project src/Euphonia.Desktop
dotnet test
```

To build something you can run without the SDK, publish it. Each platform gets **one self-contained
executable**, with no .NET install needed on the target. The output folder is named per platform:

```powershell
dotnet publish src/Euphonia.Desktop -p:PublishProfile=win-x64     # → publish\Euphonia-win-x64\Euphonia-Desktop-v0.1.0-win-x64.exe  (~47 MB)
dotnet publish src/Euphonia.Desktop -p:PublishProfile=linux-x64   # → publish/Euphonia-linux-x64/Euphonia-Desktop-v0.1.0-linux-x64     (~47 MB)
```

Every published file is named `<app>-v<version>-<platform>`, with the version taken from `<Version>` in
`Directory.Build.props`. The app shows the same version in its footer.

```text
```

On Linux, package that binary for users. Run this on Linux or in WSL; it needs `dpkg-deb`, ImageMagick and
`appimagetool`:

```bash
scripts/package-linux.sh      # → …-v0.1.0-linux-x64.deb and …-v0.1.0-linux-x64.AppImage.tar.gz (replaces the binary)
```

- **`.deb` (~41 MB), for Mint, Ubuntu and Debian:** double-click it, then Install. Euphonia then appears in the app
  menu with its icon, and `euphonia` works in a terminal. There's no permission step.
- **`.AppImage.tar.gz` (~42 MB), for any distribution:** double-click it and choose Extract, then double-click the
  AppImage. It runs from a terminal too. The AppImage ships inside an archive because downloads and Windows drives drop
  a bare file's "run as program" flag; the archive keeps it.

The profiles are in `src/Euphonia.Desktop/Properties/PublishProfiles/`. The Linux build can be made from Windows.

- **What's bundled:** the native libraries (Skia, HarfBuzz, miniaudio) are inside the file. They are unpacked once
  to the .NET bundle cache on first start.
- **Linux needs:** an X11 or XWayland session, `libx11-6 libice6 libsm6 libfontconfig1`, PulseAudio or ALSA, and a
  colour-emoji font such as `fonts-noto-color-emoji`. These are standard on desktop distributions.

### Android

Prerequisites, once:
1. `dotnet workload install android` (admin).
2. Install the SDK and a JDK into user folders, which also accepts Google's SDK licences:

```powershell
dotnet build src/Euphonia.Android -t:InstallAndroidDependencies -f net10.0-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" `
  -p:AcceptAndroidSDKLicenses=True
```

Build an APK. The Release build is sideloadable and signed with the local debug key; a store release needs its own
keystore:

```powershell
dotnet publish src/Euphonia.Android -c Release -f net10.0-android -o publish/Euphonia-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk"
# → publish\Euphonia-android\Euphonia-v0.1.0-android.apk  (~32 MB, arm64 + x86_64; Android 8.0+)
adb install -r publish\Euphonia-android\Euphonia-v0.1.0-android.apk
```

- **Solution:** `Euphonia.Android` is not in `Euphonia.slnx`, so the desktop solution and its tests build without
  the workload.
- **Microphone:** Android asks for microphone access the first time you press Record.
- **Takes:** they live in the app's private folder.
- **Emulator:** created with `avdmanager create avd -n EuphoniaPixel -k "system-images;android-35;google_apis;x86_64"
  -d pixel_7`. It uses the Windows Hypervisor Platform. Its virtual microphone plays a steady 100 Hz tone, which
  Euphonia measures as 100.0 Hz.
- **Fake mic on Android** (Debug builds only): copy a WAV to `files/fake-mic.wav` in the app's private folder:
  `adb push take.wav /data/local/tmp/` then `adb shell run-as app.euphonia cp /data/local/tmp/take.wav
  files/fake-mic.wav`.
- **Emoji:** the app carries a 54 KB subset of Noto Color Emoji (OFL), built by `scripts/make-emoji-font.py`. Android's
  own emoji font is COLRv1, which this Skia can't draw. Re-run the script after adding emoji to the UI.

Other useful commands:

```powershell
# analyze existing WAV files without the UI (like `uv run analyze.py clip.wav --label …`)
dotnet run --project src/Euphonia.Desktop -- --import take1.wav take2.wav --label "rainbow passage"

# use a throwaway data folder
$env:EUPHONIA_DATA_DIR = "C:\temp\euphonia-test"

# turn the real reference voices (VCTK clips) back on in the metric comparison; off by default (Features.cs)
dotnet build -p:EuphoniaReferenceVoices=true
```

Takes are stored in `%APPDATA%\Euphonia-CSharp\`. This is deliberately separate from the Electron app's
`%APPDATA%\Euphonia`. The layout is the same:

```
recordings.json         index (sorted by id, pretty JSON — same schema analyze.py writes)
audio/NNN.wav           44.1 kHz mono PCM16, recorded directly (no ffmpeg step)
analysis/<id>.json      10 ms pitch contour + phrases (+ per-phrase metrics) for the register and trends sections
```

## Solution layout

| Project | What it is | Platform-specific? |
|---|---|---|
| `src/Euphonia.Acoustics` | Speech-acoustics algorithms: pitch (autocorrelation / cross-correlation + Viterbi path), harmonicity, glottal pulses, jitter/shimmer, intensity, silence detection, Burg formants, spectrum, LTAS, WAV decoding. No dependencies, no native code, trim/AOT safe; usable on its own in other apps | No |
| `src/Euphonia.Core` | Models, zones and metric registry, JSON, `RecordingStore`, the analysis pipeline (`AcousticsAnalysisEngine` + `AnalysisPostProcessor`), WAV writer and waveform peaks | No |
| `src/Euphonia.Audio.SoundFlow` | Microphone capture and playback via [SoundFlow](https://github.com/LSXPrime/SoundFlow) (miniaudio) | No — ships natives for Win/macOS/Linux/Android/iOS |
| `src/Euphonia` | Avalonia UI: views, view models (CommunityToolkit.Mvvm), custom-drawn charts | No |
| `src/Euphonia.Desktop` | Desktop head (Windows, Linux, macOS): `Program.cs`, `--import`, single-file publish profiles | No |
| `src/Euphonia.Android` | Android head: `MainActivity` (services, microphone permission, emoji font) | Android only |
| `tests/Euphonia.Acoustics.Tests` | Synthetic-signal tests, plus a component-by-component comparison with real Praat | — |
| `tests/Euphonia.Core.Tests` | Zones, JSON compatibility, store, statistics, **parity with analyze.py** | — |

### Dependencies

- **Avalonia 11.3** (+ Fluent theme, Inter font) is the UI.
- **CommunityToolkit.Mvvm** provides the MVVM source generators.
- **SoundFlow** handles recording and playback.
- **xUnit** is used for tests.

## How the analysis works

`analyze.py` (parselmouth) is the reference. `AcousticsAnalysisEngine` repeats its measurement steps with
`Euphonia.Acoustics`, using the same settings:
- autocorrelation pitch 75–500 Hz
- Burg formants at 5500 and 5000 Hz ceilings
- cross-correlation harmonicity
- pulses for jitter and shimmer
- intensity and silence-based phrases
- a Hamming-windowed spectrum per voiced frame for H1/A3
- a 100 Hz LTAS

`AnalysisPostProcessor` then does the numpy/statistics half: the F2 gate, the ceiling choice, medians, the
Iseli–Alwan H1*–A3* correction, phrase and register statistics. Independent analyses run in parallel; a 10 s take
takes about 0.2 s (about 1.4 s on a single core).

### Verification

- **`tests/Euphonia.Core.Tests/ParityTests`** compares every metric with `analyze.py`'s output for four VCTK clips.
  Everything matches to the rounded digit, except **jitter and shimmer**, which are within 4%. The fixtures were made
  with parselmouth's bundled Praat 6.1.38, whose pulse picking differs slightly from current Praat 7; the C# code
  follows Praat 7.
- **`tests/Euphonia.Acoustics.Tests/OracleTests`** compares each component frame by frame with real Praat 7 when it
  is installed:

  | Component | Agreement with Praat 7 |
  |---|---|
  | F0 | < 1e-5 Hz, same voicing decisions |
  | intensity | < 1e-13 dB |
  | HNR | < 1e-10 dB |
  | pulses | all identical |
  | formants | < 0.001 Hz where there is signal |
  | spectrum / LTAS | machine precision |

  The same run also checks that the final metrics are identical to the Praat-backed engine. Praat is optional and
  only used by tests: `scripts/fetch-praat.ps1` puts it in `tools/praat/`, or set `EUPHONIA_PRAAT`. Without it these
  tests are skipped.

## Live analysis while recording

While you speak, the take view fills in and keeps updating. `LiveAnalyzer` (`Euphonia.Core/Analysis`) receives
microphone samples and processes every newly complete analysis frame (10 ms pitch/HNR frames). The UI refreshes
about 10× per second. Above the stat cards there are timelines for:
- pitch, loudness, pitch variability, HNR
- F2, F3, weight
- in-register melody and jitter

Each timeline is drawn over its metric's own zone bands, using the same zones and colours as the cards, so the
current dot shows which zone you're in. The time axis grows in 10 s steps. The "live graphs show" dropdown switches
it to a sliding window of the last 10, 30, 60 or 120 s. On Stop the live view freezes, the normal full analysis runs,
and the saved take replaces it.

Each line is smoothed over a window centred on its time: a median, an energy average for loudness, or an SD for
movement. Near "now" only the past is available, so the newest part of the line is a quick estimate that settles in
place as more audio arrives. A window never reaches across a pause.

Most to least certain, there are four layers:
1. **Full analysis** after Stop: the saved take.
2. **Live frame analysis:** the points.
3. **Smoothed live line:** its unsettled tail uses windows that are still missing future data.
4. **Prediction:** the tail leans toward 50% of the settled line's recent trend and carries the line to "now", so the
   current point is always shown. Loudness is the exception: it changes with every syllable and can't be predicted,
   so its newest value is simply held.

On top of that, the chart eases each redraw over 0.25 s. This affects only the display.

The live pitch graph uses the same pipeline:
- **Smoothing:** a short median (about 0.2 s) removes single-frame octave slips and softens path revisions. The line
  breaks at unvoiced gaps over 60 ms and at jumps over 3 semitones in 20 ms.
- **No trend prediction:** like loudness, intonation turns too fast for a trend to help, so the newest value is held.
- **Easing:** the same 0.25 s glide as the other charts.

Saved takes still draw the exact 10 ms contour. `tools/live-steadiness` measures all of this on any WAV
(`scripts/make-long-wav.ps1` builds a 27.6 s test take from the fixtures).

Live and full analysis share the same frame kernels (`Euphonia.Acoustics/Streaming`) and the same assembler
(`RawAnalysisAssembler`).
- **Trimming:** the saved WAV is trimmed by up to 10 ms so that its frame grid equals the live grid.
- **Exact match:** after trimming, the final live snapshot matches the saved analysis exactly for:
  - pitch statistics, contour, register and melody
  - phrases and landed endings
  - jitter and shimmer
- **Close match:** formants are within 11 Hz, HNR within 0.2 dB, weight within 0.3 dB and loudness within 0.03 dB
  (see `tests/Euphonia.Core.Tests/LiveAnalysisTests`).

**Delayed or approximate while live:**
- The last ~0.1–0.3 s of the pitch path can still be revised.
- Phrases (count, endings, onset/mid/offset) appear only after a pause.
- Jitter and shimmer appear once a voiced stretch ends.
- Formants, weight and phrase boundaries are relative to the loudest moment so far, so they can shift when you get
  louder.
- LTAS tilt is only computed at Stop.

Developer aid: `$env:EUPHONIA_FAKE_MIC = "take.wav"` makes the recorder replay that file in real time instead of
using the microphone.

## What's in v1 and what isn't

**Ported:**
- recording, with an optional label
- analysis
- the seven stat cards with zone bars
- the metric reference modal: take dots in lanes and click-to-play. The VCTK reference-voice ticks are switched off by default (`Features.ReferenceVoices`; build with `-p:EuphoniaReferenceVoices=true` to include them and their clips)
- resonance (F2/F3 gauges)
- register & phrasing: contour chart, stat tiles, drop-position bars and the tip
- **trends within the take** (this differs from the React app, whose trend charts plot one point per take across
  all recordings). The take is split into phrases at the pauses, and five charts plot one point per phrase: pitch,
  in-register melody, ending pitch, F2 and weight. The values are stored as `phrase_metrics` in
  `analysis/<id>.json`. Takes analyzed before this existed are re-analyzed once in the background from their WAV.
- the recordings list: waveform player, save-a-copy, delete with confirmation
- the take switcher
- the cheat sheet
- light (blossom) and dark (dusk-plum) themes that follow the OS, with a toggle

**Platforms, verified 2026-10-01:**

| Platform | Where it ran | What was checked |
|---|---|---|
| Windows 10 x64 | single-file exe | starts; records with fake and real mic; live graphs; saves |
| Linux | Ubuntu 24.04 in WSL 2 (WSLg) | same as Windows; results identical to Windows to every digit |
| Android 15 | Pixel 7 emulator (x86_64) | Debug and Release; the microphone permission prompt; real AAudio capture (the virtual mic's 100 Hz tone measured 100.01 Hz); phone layout |

**Not yet ported:**
- template and Gemini insights
- the settings screen: API key, export, delete-all
- the other six themes
- the installer and auto-update
- migrating data from the Electron app (its takes are `.webm`, which would need a decoder)

**Quirks carried over on purpose** are marked `TODO(port)` in the code:
- `Zones.ZoneOf` puts values *below* the lowest zone into the **last** zone, as `zones.ts` does.
- The cheat sheet's F2 and weight guide numbers disagree with the zone tables, as `CheatSheet.tsx` does.

## Licensing

All code, including `Euphonia.Acoustics`, is under the MIT License, like the TypeScript repo.
- **How `Euphonia.Acoustics` was written:** its algorithms are implemented from the published literature. Parameter
  defaults and conventions follow the Praat manual, so the numbers line up with Praat.
- **No Praat code:** it contains no Praat source code; see `src/Euphonia.Acoustics/REFERENCES.md` for the papers
  behind each component.
- **Praat itself (GPLv3):** it is not shipped with the app. It's only an optional test oracle.
- **Reference voices:** they come from VCTK (CC BY 4.0); see `src/Euphonia/Assets/reference/ATTRIBUTION.md`.
