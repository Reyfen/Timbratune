# Timbratune — Gender Voice Analysis Tool

By **Reyfen**. Record a take and read your pitch, resonance, weight, and register/phrasing metrics on one dashboard,
live while you speak. Runs on Windows, Linux and Android from one C#/Avalonia code base.

**Timbratune is a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia)** (Electron/React with a Python and
Praat analysis). It started as a C# port with the same metrics, zones and `recordings.json` data format, and has grown
from there: live analysis, a native analysis engine, more platforms, one folder per take, import and export. The original is available as an optional
submodule in [`Euphonia-TypeScript`](Euphonia-TypeScript) (see [Optional submodules](#optional-submodules)).

The voice analysis is **pure C#** (`Reyfen.Timbratune.Acoustics`). It needs no Praat, Python, ffmpeg or anything else
installed. Its algorithms are **inspired by [Praat](https://github.com/praat/praat.github.io)**. They are written
independently from the published papers and the Praat manual; **no Praat source code was copied** (see
[Licensing and credits](#licensing-and-credits)).

## Quick start

Prerequisites: **.NET 10 SDK**. Windows and Linux use the same code; Android adds a workload (see below).

```powershell
dotnet run --project src/Reyfen.Timbratune.Desktop
dotnet test
```

To build something you can run without the SDK, publish it. Each platform gets **one self-contained
executable**, with no .NET install needed on the target. The output folder is named per platform:

```powershell
dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64     # → publish\Timbratune-win-x64\Timbratune-v0.2.0-win-x64.exe  (~47 MB)
dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=linux-x64   # → publish/Timbratune-linux-x64/Timbratune-v0.2.0-linux-x64     (~47 MB)
```

Every published file is named `<app>-v<version>-<platform>`, with the version taken from `<Version>` in
`Directory.Build.props`. The app shows the same version in its footer. `build.ps1` (below) makes all of them at once.

On Linux, package that binary for users. Run this on Linux or in WSL; it needs `dpkg-deb`, ImageMagick and
`appimagetool`:

```bash
scripts/package-linux.sh      # → …-v0.2.0-linux-x64.deb and …-v0.2.0-linux-x64.AppImage.tar.gz (replaces the binary)
```

- **`.deb` (~39 MB), for Mint, Ubuntu and Debian:** double-click it, then Install. Timbratune then appears in the app
  menu with its icon, and `timbratune` works in a terminal. There's no permission step.
- **`.AppImage.tar.gz` (~40 MB), for any distribution:** double-click it and choose Extract, then double-click the
  AppImage. It runs from a terminal too. The AppImage ships inside an archive because downloads and Windows drives drop
  a bare file's "run as program" flag; the archive keeps it.

The profiles are in `src/Reyfen.Timbratune.Desktop/Properties/PublishProfiles/`. The Linux build can be made from Windows.

- **What's bundled:** the native libraries (Skia, HarfBuzz, miniaudio) are inside the file. They are unpacked once
  to the .NET bundle cache on first start.
- **Linux needs:** an X11 or XWayland session, `libx11-6 libice6 libsm6 libfontconfig1`, PulseAudio or ALSA, and a
  colour-emoji font such as `fonts-noto-color-emoji`. These are standard on desktop distributions.

### All builds at once

`build.ps1` builds every published file into `publish/`, or only the platforms named: `./build.ps1 win android`.
It runs in Windows PowerShell 5.1 and in PowerShell 7 (`pwsh`, also on Linux); on Windows it can also be started
by double-click or right-click → Run with PowerShell (the window then stays open at the end). It needs the .NET 10
SDK or newer and says so if it's missing, and at the end lists the files of the platforms it built. On Windows the Linux packages are made in WSL (`Ubuntu-24.04`); on Linux
`scripts/package-linux.sh` runs directly (needs `dpkg-deb`, ImageMagick and appimagetool). The Android SDK and a
JDK 17–21 are found through `ANDROID_SDK` / `ANDROID_HOME` / `ANDROID_SDK_ROOT` and `ANDROID_JDK` / `JAVA_HOME`, or in
the usual install folders (Android Studio, Visual Studio, the setup below, Linux packages). If either is missing, the
script lists every place it looked.

### Android

Prerequisites, once:
1. `dotnet workload install android` (admin).
2. An Android SDK and a JDK 17–21. Installing Android Studio gives both (the SDK lands in `%LOCALAPPDATA%\Android\Sdk`).
   Or install them into user folders with this, which also accepts Google's SDK licences (run it from your own
   terminal):

```powershell
dotnet build src/Reyfen.Timbratune.Android -t:InstallAndroidDependencies -f net10.0-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" `
  -p:AcceptAndroidSDKLicenses=True
```

Build an APK with `./build.ps1 android`, which finds the SDK and JDK itself, or by hand. The Release build is
sideloadable and signed with the local debug key; a store release needs its own keystore:

```powershell
dotnet publish src/Reyfen.Timbratune.Android -c Release -f net10.0-android -o publish/Timbratune-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk"
# → publish\Timbratune-android\Timbratune-v0.2.0-android.apk  (~42 MB, arm64 + x86_64, LLVM AOT; Android 8.0+)
adb install -r publish\Timbratune-android\Timbratune-v0.2.0-android.apk
```

- **Solution:** `Reyfen.Timbratune.Android` is not in `Timbratune.slnx`, so the desktop solution and its tests build without
  the workload.
- **Microphone:** Android asks for microphone access the first time you press Record.
- **Takes:** they live in `Android/data/com.reyfen.timbratune/files/takes`, which a PC sees over USB.
- **Emulator:** created with `avdmanager create avd -n EuphoniaPixel -k "system-images;android-35;google_apis;x86_64"
  -d pixel_7`. It uses the Windows Hypervisor Platform. Its virtual microphone plays a steady 100 Hz tone, which
  Timbratune measures as 100.0 Hz.
- **Fake mic on Android** (Debug builds only): copy a WAV to `files/fake-mic.wav` in the app's private folder:
  `adb push take.wav /data/local/tmp/` then `adb shell run-as com.reyfen.timbratune cp /data/local/tmp/take.wav
  files/fake-mic.wav`.
- **Emoji:** the app carries a 56 KB subset of Noto Color Emoji (OFL), built by `scripts/make-emoji-font.py`. Android's
  own emoji font is COLRv1, which this Skia can't draw. Re-run the script after adding emoji to the UI.

Other useful commands:

```powershell
# import audio (.wav, .mp3, .flac) or .tmbr files without the UI (like `uv run analyze.py clip.wav --label …`)
dotnet run --project src/Reyfen.Timbratune.Desktop -- --import take1.wav take2.mp3 --label "rainbow passage"

# use a throwaway data folder
$env:TIMBRATUNE_DATA_DIR = "C:\temp\timbratune-test"

# turn the real reference voices (VCTK clips) back on in the metric comparison; off by default (Features.cs)
dotnet build -p:TimbratuneReferenceVoices=true
```

Takes are stored in `%APPDATA%\Timbratune\takes\` (on Linux `~/.config/Timbratune/takes/`; on Android
`Android/data/com.reyfen.timbratune/files/takes`, visible from a PC over USB). The footer link opens the folder.
Each take is one folder, found by listing the folder (there is no index), so takes can be copied in or deleted by hand:

```
takes/003 rainbow passage/
  take.wav        44.1 kHz mono PCM16, recorded directly (no ffmpeg step)
  take.json       label, note, date, metrics, audio format, analysis settings
  detail.json     10 ms pitch contour + phrases + register summary + trends
  series.json     per-frame lists (pitch, loudness, HNR, F1–F3, weight, jitter), used for export
```

Audio (`.wav`, `.mp3`, `.flac`) or `.tmbr` files dropped into `takes/` are imported on the next start; the 📥 import
button does the same from a file picker. Data folders from earlier versions (`recordings.json` + `audio/` +
`analysis/`, the Electron app's layout) are converted on first start; `recordings.json` is kept as
`recordings.json.migrated`. Takes from builds made before the rename, in `Euphonia-CSharp`, are moved over first.
This is deliberately separate from the original Electron app's `%APPDATA%\Euphonia`.

## Solution layout

| Project | What it is | Platform-specific? |
|---|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Speech-acoustics algorithms: pitch (autocorrelation / cross-correlation + Viterbi path), harmonicity, glottal pulses, jitter/shimmer, intensity, silence detection, Burg formants, spectrum, LTAS, WAV decoding. No dependencies, no native code, trim/AOT safe; usable on its own in other apps | No |
| `src/Reyfen.Timbratune.Core` | Models, zones and metric registry, JSON, `RecordingStore`, the analysis pipeline (`AcousticsAnalysisEngine` + `AnalysisPostProcessor`), WAV writer and waveform peaks | No |
| `src/Reyfen.Timbratune.Audio.SoundFlow` | Microphone capture and playback via [SoundFlow](https://github.com/LSXPrime/SoundFlow) (miniaudio) | No — ships natives for Win/macOS/Linux/Android/iOS |
| `src/Reyfen.Timbratune` | Avalonia UI: views, view models (CommunityToolkit.Mvvm), custom-drawn charts | No |
| `src/Reyfen.Timbratune.Desktop` | Desktop head (Windows, Linux, macOS): `Program.cs`, `--import`, single-file publish profiles | No |
| `src/Reyfen.Timbratune.Android` | Android head: `MainActivity` (services, microphone permission, emoji font) | Android only |
| `tests/Reyfen.Timbratune.Acoustics.Tests` | Synthetic-signal tests, plus a component-by-component comparison with real Praat | — |
| `tests/Reyfen.Timbratune.Core.Tests` | Zones, JSON compatibility, store, statistics, **parity with analyze.py** | — |

### Dependencies

- **Avalonia 11.3** (+ Fluent theme, Inter font) is the UI.
- **CommunityToolkit.Mvvm** provides the MVVM source generators.
- **SoundFlow** handles recording and playback.
- **xUnit** is used for tests.

## How the analysis works

`analyze.py` (parselmouth) is the reference. `AcousticsAnalysisEngine` repeats its measurement steps with
`Reyfen.Timbratune.Acoustics`, using the same settings:
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

- **`tests/Reyfen.Timbratune.Core.Tests/ParityTests`** compares every metric with `analyze.py`'s output for four VCTK clips.
  Everything matches to the rounded digit, except **jitter and shimmer**, which are within 4%. The fixtures were made
  with parselmouth's bundled Praat 6.1.38, whose pulse picking differs slightly from current Praat 7; the C# code
  follows Praat 7.
- **`tests/Reyfen.Timbratune.Acoustics.Tests/OracleTests`** compares each component frame by frame with real Praat 7 when it
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
  only used by tests: `scripts/fetch-praat.ps1` puts it in `tools/praat/`, or set `TIMBRATUNE_PRAAT`. Without it these
  tests are skipped.

## Live analysis while recording

While you speak, the take view fills in and keeps updating. `LiveAnalyzer` (`Reyfen.Timbratune.Core/Analysis`) receives
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

Live and full analysis share the same frame kernels (`Reyfen.Timbratune.Acoustics/Streaming`) and the same assembler
(`RawAnalysisAssembler`).
- **Trimming:** the saved WAV is trimmed by up to 10 ms so that its frame grid equals the live grid.
- **Exact match:** after trimming, the final live snapshot matches the saved analysis exactly for:
  - pitch statistics, contour, register and melody
  - phrases and landed endings
  - jitter and shimmer
- **Close match:** formants are within 11 Hz, HNR within 0.2 dB, weight within 0.3 dB and loudness within 0.03 dB
  (see `tests/Reyfen.Timbratune.Core.Tests/LiveAnalysisTests`).

**Delayed or approximate while live:**
- The last ~0.1–0.3 s of the pitch path can still be revised.
- Phrases (count, endings, onset/mid/offset) appear only after a pause.
- Jitter and shimmer appear once a voiced stretch ends.
- Formants, weight and phrase boundaries are relative to the loudest moment so far, so they can shift when you get
  louder.
- LTAS tilt is only computed at Stop.

Developer aid: `$env:TIMBRATUNE_FAKE_MIC = "take.wav"` makes the recorder replay that file in real time instead of
using the microphone.

## What's in v1 and what isn't

**Ported:**
- recording, with an optional label
- analysis
- the seven stat cards with zone bars
- the metric reference modal: take dots in lanes and click-to-play. The VCTK reference-voice ticks are switched off by default (`Features.ReferenceVoices`; build with `-p:TimbratuneReferenceVoices=true` to include them and their clips)
- resonance (F2/F3 gauges)
- register & phrasing: contour chart, stat tiles, drop-position bars and the tip
- **trends within the take** (this differs from the React app, whose trend charts plot one point per take across
  all recordings). The take is cut into equal slices of whole seconds (at most 10 points), and ten charts plot one
  point per slice: pitch, pitch variability, in-register melody, phrase endings, loudness, F2, F3, weight, clarity
  (HNR) and steadiness (jitter). They are stored as `trends` in the take's `detail.json`; takes analyzed before a
  measure existed are re-analyzed once in the background from their WAV.
- the take's title with when it was recorded, in the device's date and time format ("Latest take · 08-Oct-26 · 14:03")
- the recordings list: waveform player, rename in place, delete with confirmation, and a save menu: **save audio**
  (`.wav`), **export data** (`.tmbr`: a zip with the WAV and `take.json` holding the metrics, contour, trends and the
  per-frame lists with their frame spacing, for a later import) and **export PDF** (shown, not implemented yet)
- **import** of `.wav`, `.mp3`, `.flac` and `.tmbr` files (button beside Record, or drop them into the takes folder)
- the take switcher
- the cheat sheet
- light (blossom) and dark (dusk-plum) themes that follow the OS, with a toggle

**Platforms, verified 2026-10-01:**

| Platform | Where it ran | What was checked |
|---|---|---|
| Windows 10 x64 | single-file exe | starts; records with fake and real mic; live graphs; saves |
| Linux | Ubuntu 24.04 in WSL 2 (WSLg) | same as Windows; results identical to Windows to every digit |
| Android 15 | Pixel 7 emulator (x86_64) | Debug and Release; the microphone permission prompt; real AAudio capture (the virtual mic's 100 Hz tone measured 100.01 Hz); phone layout |

**Since then (0.2.0):**

| Platform | Where it ran | What was checked |
|---|---|---|
| Android | Pixel 9 (Android 17) and Pixel 4a, over USB | live recording speed and smoothness (Pixel 9: ~58 fps, 2–3 % slow frames), analysis time, scrolling |
| Android | Pixel 7 emulator, 2026-10-08 | phone layout of the take, live view, trends and cards in both themes; importing files dropped into the takes folder |
| Windows / Linux | single-file exe; Ubuntu 24.04 in WSL | `build.ps1` builds; the exe built on Linux runs on Windows; import, export, rename and the folder link |

**Not yet ported:**
- template and Gemini insights
- the settings screen: API key, delete-all
- PDF export (the menu item is there, greyed out)
- the other six themes
- the installer and auto-update
- migrating data from the Electron app (its takes are `.webm`, which would need a decoder)

**Quirks carried over on purpose** are marked `TODO(port)` in the code:
- `Zones.ZoneOf` puts values *below* the lowest zone into the **last** zone, as `zones.ts` does.
- The cheat sheet's F2 and weight guide numbers disagree with the zone tables, as `CheatSheet.tsx` does.

## Optional submodules

Two reference sources are git submodules that are **not** fetched by default; their `update = none` in `.gitmodules`
means a plain clone or `git submodule update --init` skips them. Nothing in the build or the tests needs them.

| Path | What | Fetch it with |
|---|---|---|
| `Euphonia-TypeScript` | the original [Euphonia](https://github.com/Yuuzulight/Euphonia) app (Timbratune's fork origin) | `git submodule update --init --checkout Euphonia-TypeScript` |
| `Praat` | [Praat](https://github.com/praat/praat.github.io)'s source (GPLv3, ~380 MB), for reading only | `git submodule update --init --checkout --depth 1 Praat` |

## Licensing and credits

All of Timbratune's code, including `Reyfen.Timbratune.Acoustics`, is under the MIT License, like the original
Euphonia.
- **Fork of Euphonia:** the app started as a port of [Euphonia](https://github.com/Yuuzulight/Euphonia). Its metric
  definitions, zones, texts and data format come from there.
- **Inspired by Praat:** the analysis algorithms follow the methods that
  [Praat](https://github.com/praat/praat.github.io) (Boersma & Weenink) uses. They are written from scratch from the
  published papers, with defaults and conventions taken from the public Praat manual, so the numbers line up with Praat.
- **No Praat code:** no Praat source code was copied, translated or adapted into Timbratune. The papers behind each
  component are listed in `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`.
- **Praat itself (GPLv3):** it is never shipped with or linked into the app. The tests can optionally run the separate
  Praat program as an oracle to check the numbers.
- **Reference voices:** they come from VCTK (CC BY 4.0); see `src/Reyfen.Timbratune/Assets/reference/ATTRIBUTION.md`.
