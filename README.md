# Timbratune — Gender Voice Analysis Tool

By **Reyfen**. Record a take and read your pitch, resonance, weight, and register/phrasing metrics on one dashboard,
live while you speak. Runs on Windows, Linux, Android and iOS from one C#/Avalonia code base.

Timbratune started as a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia) and is developed independently now.
The voice analysis is **pure C#** (`Reyfen.Timbratune.Acoustics`): the app needs no Praat, Python, ffmpeg or anything
else installed (see [Licensing and credits](#licensing-and-credits)).

## Setup

Prerequisites: the **.NET 10 SDK**. Windows, Linux and macOS use the same code; Android adds a workload (below).

```powershell
dotnet build Timbratune.slnx
dotnet test Timbratune.slnx
dotnet run --project src/Reyfen.Timbratune.Desktop
```

- **Linux needs:** an X11 or XWayland session, `libx11-6 libice6 libsm6 libfontconfig1`, PulseAudio or ALSA, and a
  colour-emoji font such as `fonts-noto-color-emoji`. These are standard on desktop distributions.
- **Praat oracle tests:** a few tests compare the analysis with real Praat and are skipped without it.
  `scripts/fetch-praat.ps1` puts Praat in `tools/praat/`, or set `TIMBRATUNE_PRAAT` to its path. Praat is used by
  tests only.

### Android

`src/Reyfen.Timbratune.Android` is not in `Timbratune.slnx`, so the solution builds without the workload. To build it:

1. `dotnet workload install android` (admin).
2. An Android SDK and a JDK 17–21. Installing Android Studio gives both (the SDK lands in `%LOCALAPPDATA%\Android\Sdk`).
   Or install them into user folders with this, which also accepts Google's SDK licences (run it from your own
   terminal):

```powershell
dotnet build src/Reyfen.Timbratune.Android -t:InstallAndroidDependencies -f net10.0-android `
  "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk" `
  -p:AcceptAndroidSDKLicenses=True
```

Then build a Debug APK and install it on a phone or emulator:

```powershell
./build.ps1 android debug
adb install -r publish\Timbratune-android-debug\Timbratune-v<version>-android.apk
```

`build.ps1` finds the SDK and JDK through `ANDROID_HOME` / `ANDROID_SDK_ROOT` and `JAVA_HOME` or in the usual install
folders, and lists every place it looked if one is missing. Without the project's signing key the APK is signed with
your machine's debug key, which is fine for development.

### iOS

`src/Reyfen.Timbratune.iOS` is built on macOS; see [`IOS.md`](IOS.md).

## Developing

Environment variables:
- `TIMBRATUNE_DATA_DIR`: use a throwaway data folder instead of your real takes (default `%APPDATA%\Timbratune\`,
  on Linux `~/.config/Timbratune/`).
- `TIMBRATUNE_FAKE_MIC=take.wav`: the recorder replays that file in real time instead of using the microphone.
- `TIMBRATUNE_PRAAT`: the path to Praat, for the oracle tests.

Import audio or `.tmbr` files without the UI:

```powershell
dotnet run --project src/Reyfen.Timbratune.Desktop -- --import take1.wav take2.mp3 --label "rainbow passage"
```

Where things are:
- [`REFACTOR-PLAN.md`](REFACTOR-PLAN.md): the architecture (feature modules, per-metric analysis) and the checklists
  for adding a feature or a metric.
- [`HANDOFF.md`](HANDOFF.md): history, measurements and the reasoning behind decisions; publishing and releases are
  in §11.
- [`NOISE-PLAN.md`](NOISE-PLAN.md): noise and microphone robustness.
- `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`: the papers behind each analysis component.

| Project | What it is |
|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Speech-acoustics algorithms (pitch, formants, intensity, HNR, jitter/shimmer, spectrum). No dependencies, trim/AOT safe |
| `src/Reyfen.Timbratune.Core` | Models, zones, metric modules, the analysis pipeline (full and live), storage, import and export |
| `src/Reyfen.Timbratune.Audio.*` | Recording and playback (SoundFlow), import decoders (Media Foundation, FFmpeg, Android) |
| `src/Reyfen.Timbratune` | Avalonia UI library, one folder per feature |
| `src/Reyfen.Timbratune.Desktop`, `.Android`, `.iOS` | Platform heads |
| `tests/` | Acoustics, Core, architecture and UI-library tests |
| `tools/` | Dev tools: live steadiness, analysis speed, noise bench |

## Licensing and credits

From version 0.3.1 on, Timbratune, including `Reyfen.Timbratune.Acoustics`, is **source-available** under the
[PolyForm Noncommercial License 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0) with the Timbratune
Additional Terms; the full text is in [`LICENSE.md`](LICENSE.md). In short:
- you may use, study, change and share it for any **noncommercial** purpose;
- it may not be used **commercially**, changed or not;
- neither it nor any fork may be published in an **app store or software marketplace** (Google Play, App Store,
  Microsoft Store, F-Droid, Flathub, Steam, …); sharing the source and builds from your own site or repository is fine;
- forks must use **their own name and icon**, not "Timbratune" or "Reyfen".

Versions 0.3.0 and older were published under the MIT License and stay under it. Code contributions are not accepted
at the moment; see [`CONTRIBUTING.md`](CONTRIBUTING.md). Third-party parts are listed in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

- **Fork of Euphonia:** the app started as a port of [Euphonia](https://github.com/Yuuzulight/Euphonia) (MIT). Parts
  of its metric definitions, zones, texts and data format come from there.
- **Inspired by Praat:** the analysis algorithms follow the methods that
  [Praat](https://github.com/praat/praat.github.io) (Boersma & Weenink) uses. They are written from scratch from the
  published papers, with defaults and conventions taken from the public Praat manual, so the numbers line up with Praat.
- **No Praat code:** no Praat source code was copied, translated or adapted into Timbratune. The papers behind each
  component are listed in `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`.
- **Praat itself (GPLv3):** it is never shipped with or linked into the app. The tests can optionally run the separate
  Praat program as an oracle to check the numbers.
- **Reference voices:** they come from VCTK (CC BY 4.0); see `src/Reyfen.Timbratune/Assets/reference/ATTRIBUTION.md`.
