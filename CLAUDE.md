# Timbratune

**Timbratune — Gender Voice Analysis Tool**, by Reyfen. A C#/Avalonia (.NET 10) voice-training feedback app: record a take, see pitch, resonance (F1–F3), vocal weight, loudness, clarity (HNR), steadiness (jitter/shimmer) and phrasing, each placed in zones. It is a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia) (the original Electron app is the optional `Euphonia-TypeScript` submodule).

`HANDOFF.md` has the full history, measurements and reasoning behind decisions. Read the relevant section before changing live analysis or smoothing.

## Ground rules

- **Commit only when asked.** The main branch is `dev` (tracks `origin/dev` on https://github.com/Reyfen/Timbratune). Current work is on the `v0.2.0` branch (pushed to `origin/v0.2.0`, not merged into `dev` yet); check `git status` and HANDOFF §8 before starting. Write commit messages to a file without a BOM (PowerShell 5.1's `Out-File -Encoding utf8` adds one) and pass it with `git commit -F`.
- **No Praat code.** Praat's algorithms are inspiration only; implement from the published papers (see `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`). Nothing may be copied, translated or adapted from Praat's GPLv3 source; the project must stay MIT. `Praat.exe` is used by tests only, as an oracle.
- **Cross-platform.** Windows, Linux and Android ship; macOS and iOS must stay possible. No Windows-only dependencies.
- **Don't touch the user's running app.** The user often runs the published build from `publish/Timbratune-win-x64`, which locks that folder. Check `Get-Process | ? Path -like '*Timbratune*'` — the single-file exe runs as `Timbratune-v<ver>-win-x64`, Debug builds as `Reyfen.Timbratune.Desktop` — and ask before closing it. When driving the UI, always target your own instance by pid.
- **Never touch the user's real takes.** Their data is `%APPDATA%\Timbratune` (desktop) and the app folder on their phones. Test on copies or with `TIMBRATUNE_DATA_DIR`. Never uninstall the app on a phone without asking (that deletes its takes), and don't change phone or system settings; ask the user to.
- **Measure before claiming.** "Steadier" or "faster" claims need numbers (`tools/live-steadiness`, tests). Several plausible ideas measured worse; see HANDOFF §6.

## Layout

| Path | Role |
|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Pure C#, MIT, zero dependencies, trim/AOT safe: pitch (Boersma 1993), Burg LPC formants, intensity, HNR, pulses/jitter/shimmer, spectrum, resampler, WAV decoder, and live `Streaming/` trackers. |
| `src/Reyfen.Timbratune.Core` | UI-independent domain: `Analysis/AcousticsAnalysisEngine.cs` (full analysis), `Analysis/LiveAnalyzer.cs` (live), zones, metrics, storage (`RecordingStore`, `DataPaths`; one folder per take under `takes/`: `take.wav`, `take.json`, `detail.json`, `series.json`; no index), `.tmbr` export (`Storage/TakeArchive.cs`), import (`Storage/TakeImporter.cs`). |
| `src/Reyfen.Timbratune.Audio.SoundFlow` | Recording/playback via SoundFlow (miniaudio). |
| `src/Reyfen.Timbratune` | Avalonia UI library (CommunityToolkit.Mvvm). Key: `ViewModels/RecordViewModel.cs`, `ViewModels/LiveTimelinesViewModel.cs`, `ViewModels/TakeViewModel.cs`, `Analysis/RecentWindow.cs` (live smoothing/prediction), `Controls/TimelineChart.cs`, `Controls/ContourChart.cs`, `Themes/Styles.axaml`, `Features.cs` (feature flags, version, links). |
| `src/Reyfen.Timbratune.Desktop` | Desktop head (Windows/Linux/macOS); `--import file.wav|.mp3|.flac|.tmbr --label "…"`. Publish profiles in `Properties/PublishProfiles/`. |
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
./build.ps1 [win] [linux] [android]           # all published builds (default: all three); Windows PowerShell 5.1 or pwsh (Linux too)
```

Artifact names follow `<app>-v<Version>-<platform>` from `<Version>` in `Directory.Build.props`.

Environment variables:
- `TIMBRATUNE_DATA_DIR`: throwaway data folder (default `%APPDATA%\Timbratune\`; takes in its `takes/`). A folder in the old layout (`recordings.json`) is converted when the app opens it, so test on a copy of real data, never the user's folder.
- `TIMBRATUNE_FAKE_MIC=take.wav`: the recorder replays that file in real time instead of the mic.
- `TIMBRATUNE_PRAAT`: path to Praat for oracle tests.

UI check without a person: build `long.wav` with `scripts/make-long-wav.ps1`, set `TIMBRATUNE_FAKE_MIC` and `TIMBRATUNE_DATA_DIR`, start with `scripts/screenshot.ps1 -Exe … -Out a.png -Wait 7` (prints `pid=…`), then always pass `-ProcId <pid>`. PrintWindow doesn't capture popups; drive dropdowns with the keyboard.

Android build: `./build.ps1 android` finds the SDK and a JDK 17-21 itself (env vars, then the usual install folders) and lists where it looked if it can't. By hand, pass `-p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`. See HANDOFF §8a and §8h.
- **Emulator** (AVD `EuphoniaPixel`): start it headless with `emulator -avd EuphoniaPixel -no-window -no-snapshot -no-boot-anim -gpu swiftshader_indirect -memory 4096`, install a Debug APK built with `-p:EmbedAssembliesIntoApk=true`, and give it a fake mic with `adb shell run-as com.reyfen.timbratune cp /data/local/tmp/take.wav files/fake-mic.wav`. Screenshots: `adb exec-out screencap -p`. Takes live in `/sdcard/Android/data/com.reyfen.timbratune/files/takes`.
- **Real phones** (the user's Pixel 9 and Pixel 4a, over USB) for performance: the profiling build (`-p:TimbratuneProfiling=true`), its probes, switches and the frame-time measurement are in HANDOFF §8d. Use the SDK's own `platform-tools/adb.exe`.
- **Linux builds** are tested in WSL `Ubuntu-24.04` as user `tester` (.NET in `~/.dotnet`, PowerShell 7 in `~/.powershell`), on a copy of the tree in `~/tt`, not on `/mnt/c` (that would mix Linux and Windows `obj/` folders).

**Claude's AppData is not the user's.** The Claude desktop app is an MSIX package, so files Claude's shells create under `%LOCALAPPDATA%` / `%APPDATA%` go to `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\…`, invisible to the user's own programs. The Android SDK and JDK at `%LOCALAPPDATA%\Android\{Sdk,jdk}` exist only there: Claude's builds see them, the user's don't. Don't install tools or write user-facing data under AppData; reads of the user's real files there work.

## Shell gotchas (this machine)

- PowerShell 5.1: no `&&`; .NET file APIs resolve against the process directory, so use absolute paths.
- Git Bash rewrites `/mnt/...` arguments to `wsl.exe` and `adb`; set `MSYS_NO_PATHCONV=1`.
- Python one-liners with quotes in Git Bash heredocs break; write a script file instead.
- Through `wsl.exe … bash -c '…'`, escape `$` as `\$` (e.g. `\$?`), or it's expanded before the command reaches bash.
- `build.ps1` must stay ASCII-only: Windows PowerShell 5.1 reads a BOM-less script as ANSI.
