# Timbratune

**Timbratune — Gender Voice Analysis Tool**, by Reyfen. A C#/Avalonia (.NET 10) voice-training feedback app: record a take, see pitch, resonance (F1–F3), vocal weight, loudness, clarity (HNR), steadiness (jitter/shimmer) and phrasing, each placed in zones. It is a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia) (the original Electron app is the optional `Euphonia-TypeScript` submodule).

`HANDOFF.md` has the full history, measurements and reasoning behind decisions. Read the relevant section before changing live analysis or smoothing.

## Ground rules

- **Commit only when asked.** The working branch is `dev` (tracks `origin/dev` on https://github.com/Reyfen/Timbratune).
- **No Praat code.** Praat's algorithms are inspiration only; implement from the published papers (see `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`). Nothing may be copied, translated or adapted from Praat's GPLv3 source; the project must stay MIT. `Praat.exe` is used by tests only, as an oracle.
- **Cross-platform.** Windows, Linux and Android ship; macOS and iOS must stay possible. No Windows-only dependencies.
- **Don't touch the user's running app.** The user often runs the published build from `publish/Timbratune-win-x64`, which locks that folder. Check `Get-Process Reyfen.Timbratune.Desktop` (look at `Path`) and ask before closing it. When driving the UI, always target your own instance by pid.
- **Measure before claiming.** "Steadier" or "faster" claims need numbers (`tools/live-steadiness`, tests). Several plausible ideas measured worse; see HANDOFF §6.

## Layout

| Path | Role |
|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Pure C#, MIT, zero dependencies, trim/AOT safe: pitch (Boersma 1993), Burg LPC formants, intensity, HNR, pulses/jitter/shimmer, spectrum, resampler, WAV decoder, and live `Streaming/` trackers. |
| `src/Reyfen.Timbratune.Core` | UI-independent domain: `Analysis/AcousticsAnalysisEngine.cs` (full analysis), `Analysis/LiveAnalyzer.cs` (live), zones, metrics, storage (`RecordingStore`, `DataPaths`). |
| `src/Reyfen.Timbratune.Audio.SoundFlow` | Recording/playback via SoundFlow (miniaudio). |
| `src/Reyfen.Timbratune` | Avalonia UI library (CommunityToolkit.Mvvm). Key: `ViewModels/RecordViewModel.cs`, `ViewModels/LiveTimelinesViewModel.cs`, `ViewModels/TakeViewModel.cs`, `Analysis/RecentWindow.cs` (live smoothing/prediction), `Controls/TimelineChart.cs`, `Controls/ContourChart.cs`, `Themes/Styles.axaml`, `Features.cs` (feature flags, version, links). |
| `src/Reyfen.Timbratune.Desktop` | Desktop head (Windows/Linux/macOS); `--import file.wav --label "…"`. Publish profiles in `Properties/PublishProfiles/`. |
| `src/Reyfen.Timbratune.Android` | Android head. **Not in `Timbratune.slnx`** so the solution builds without the Android workload. |
| `tests/` | Acoustics tests (synthetic, Praat oracle, streaming) and Core tests (parity with `analyze.py` on VCTK fixtures, live-vs-saved). |
| `tools/live-steadiness` | Dev tool measuring how steady and accurate the live dots are. |
| `scripts/` | `fetch-praat.ps1`, `make-long-wav.ps1`, `screenshot.ps1` (Windows UI checks), `linux-ui.sh`, `package-linux.sh`, `make-emoji-font.py` (re-run when adding emoji to the UI). |

Live analysis shares per-frame kernels with the full analysis, so live ≈ saved (exact for pitch, jitter, shimmer, phrases; small tolerances elsewhere). On Stop the WAV is trimmed by ≤ 10 ms so both frame grids line up. Don't break this; `LiveAnalysisTests` checks it.

## Commands

```powershell
dotnet build Timbratune.slnx
dotnet test Timbratune.slnx                      # Praat oracle tests skip without Praat
dotnet run --project src/Reyfen.Timbratune.Desktop
dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64   # single self-contained exe
```

Artifact names follow `<app>-v<Version>-<platform>` from `<Version>` in `Directory.Build.props`.

Environment variables:
- `TIMBRATUNE_DATA_DIR`: throwaway data folder (default `%APPDATA%\Timbratune\`).
- `TIMBRATUNE_FAKE_MIC=take.wav`: the recorder replays that file in real time instead of the mic.
- `TIMBRATUNE_PRAAT`: path to Praat for oracle tests.

UI check without a person: build `long.wav` with `scripts/make-long-wav.ps1`, set `TIMBRATUNE_FAKE_MIC` and `TIMBRATUNE_DATA_DIR`, start with `scripts/screenshot.ps1 -Exe … -Out a.png -Wait 7` (prints `pid=…`), then always pass `-ProcId <pid>`. PrintWindow doesn't capture popups; drive dropdowns with the keyboard.

Android build: pass `-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk -p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk` (the user's `JAVA_HOME` isn't usable). Emulator AVD: `EuphoniaPixel`. See HANDOFF §8a.

## Shell gotchas (this machine)

- PowerShell 5.1: no `&&`; .NET file APIs resolve against the process directory, so use absolute paths.
- Git Bash rewrites `/mnt/...` arguments to `wsl.exe` and `adb`; set `MSYS_NO_PATHCONV=1`.
- Python one-liners with quotes in Git Bash heredocs break; write a script file instead.
