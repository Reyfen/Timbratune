# Timbratune: handoff notes

This file brings a new engineer or agent up to date on everything done so far: what the user asked for, what was decided and why, how the code is organised, what was measured, and what is still open.

Last updated 2026-10-07 (see §8d for the v0.2.0 Android performance work).

**Name:** the project is **Timbratune — Gender Voice Analysis Tool**, by **Reyfen**.
- **Before 2026-10-02 it was called Euphonia**, and it is a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia). Older parts of this file and of the transcript say "Euphonia" and "Euphonia-CSharp"; read those as Timbratune and the repo root (the code moved from `Timbratune/` to the root on 2026-10-06).
- See §8b for the rename itself.

The full conversation transcript, if you need exact wording, is at `C:\Users\mihai\.claude\projects\C--Projects-TEDinc-Euphonia\e751b8bb-a930-433a-b573-0bbfba026197.jsonl`. It is JSON Lines and very large; grep it rather than reading it whole.

---

## 1. Ground rules

- **Commit only when the user explicitly asks.** The main branch is `dev`; there is also a `main` branch. The user sometimes commits the work themselves; the recent commits "Add live update" and "Add graph stability" are theirs.
- **Licence constraint.** Praat's algorithms are used only as inspiration, the way a human programmer would read them. Code is written from the published papers; nothing is copied from Praat's GPLv3 source, and the result is MIT-compatible. In the user's words:

  > "Read the algorithms of Praat only as inspiration, as any human programmer would do. Do not copy it directly. Also, keep the papers based on it. I don't want to carry the GPL v3 license because there is no way for me to deeply integrate it and stuff like this. I take all the responsibility on myself."

  Praat's source lives in the optional `Praat` submodule (reference only). `Praat.exe` is used **by tests only** (the oracle), fetched with `scripts/fetch-praat.ps1`.
- **Cross-platform.** Windows comes first, but macOS, Linux, Android and iOS must stay possible, so **no Windows-only dependencies**.
- **Don't touch the user's running app.** The user often has the published build open from `publish/Timbratune-win-x64`. It locks the folder, so `dotnet publish` fails. Check with `Get-Process | ? Path -like '*Timbratune*'` (the single-file exe's process is named `Timbratune-Desktop-v<ver>-win-x64`, Debug builds `Reyfen.Timbratune.Desktop`; the `Path` column tells you which build it is) and ask before closing it. Never kill it without asking.
- **Measure before claiming.** Changes aimed at "steadier" or "faster" were verified with numbers. Some ideas measured worse and were dropped or limited (see §6). Keep doing this.

## 2. What the product is

Timbratune, originally Euphonia, is a voice-training feedback app. You record a take and see a dashboard with:
- pitch: average, range, variability, contour;
- resonance: F1–F3, "vocal size";
- vocal weight: H1*–A3*;
- loudness, clarity (HNR) and steadiness (jitter/shimmer);
- register and phrasing: % in register, phrase endings landed, per-phrase trends.

Each metric is placed in zones (masculine / neutral / feminine, and others).

- `Euphonia-TypeScript/` is the original Electron/React app. It used Python and parselmouth (`analyze.py`), which calls Praat.
- The repo root is the port. It is the active codebase.

## 3. History of requests, in order

1. Run the TypeScript app locally.
2. Port it to C#/Avalonia (now the repo root): .NET 10, core dashboard, same metrics, same zones, same `recordings.json` format.
3. Trends are per phrase within a single take, not across takes. The publish folder carries a platform suffix: `publish/Timbratune-win-x64`.
4. Set up the git repo with `dev` as the main branch, and commit only when asked.
5. Replace Praat.exe with a pure C# module, `Reyfen.Timbratune.Acoustics`: MIT, zero dependencies, trim/AOT safe. Praat is kept for tests only.
6. Performance questions: how long analysis takes, and an estimate for low-end Android. FFT and sinc optimizations were then added and verified to give identical results, only faster.
7. **Live analysis while recording.** Graphs build while you speak and freeze on Stop. The axis grows in 10 s steps. Values update per frame. Live must be verified against the full analysis ("Ideally, they should match perfectly").
   - Show everything possible live, and explicitly list what can't be (see §5.4).
   - Live ≈ final; the saved take is exact. On Stop the live view freezes, then hands over to the saved take.
   - The current zone is shown **on the graph itself**: zone bands behind the line, with the dot's position as the feedback, and **no pills**.
8. Remove the faint raw lines. Add a **"live graphs show" dropdown**: whole take (default), or the last 10/30/60/120 s as a sliding window. Make values fast first and corrected later, because the lines looked jittery.
9. Style the dropdown like the other controls, and make weight and the others less jittery. The user proposed a **4-layer model**:
   1. full audio analysis after Stop;
   2. high-quality runtime analysis;
   3. low-quality runtime analysis (smoothing with incomplete windows);
   4. simple prediction for the most recent part (about 50% of the recent trend).

   The current point should always be shown.
10. Export this context (this file), and give the **pitch graph** the same stabilization. Both are done; see §5.6.
11. **Platforms** (§8a):
    - a **single .exe** for Windows (self-contained, as the user chose);
    - a **Linux build**, validated in a VM: WSL 2 Ubuntu (automated) plus a full Linux Mint Hyper-V desktop VM (the user chose both);
    - an **Android version**, tested on the emulator.

    Also: **disable the reference voices** behind a feature flag ("maybe we will use it later").

## 4. Codebase map (repo root)

| Project | Role |
|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Pure C#, MIT, no dependencies. See the component list below. |
| `src/Reyfen.Timbratune.Core` | Domain logic, independent of the UI. |
| `src/Reyfen.Timbratune.Audio.SoundFlow` | Recording and playback through SoundFlow 1.4.1 (miniaudio). The recorder raises `SamplesCaptured` after each WAV write. |
| `src/Reyfen.Timbratune` | Avalonia 11.3 UI library, using CommunityToolkit.Mvvm. |
| `src/Reyfen.Timbratune.Desktop` | Desktop entry point (Windows, Linux, macOS); also provides `--import file.wav --label "…"`. Single-file publish profiles live in `Properties/PublishProfiles/`. |
| `src/Reyfen.Timbratune.Android` | Android head; see §8a. It is **not in `Timbratune.slnx`**, so the desktop solution and tests build without the Android workload. |
| `tests/Reyfen.Timbratune.Acoustics.Tests` | 50 tests: synthetic, Praat oracle (skipped without Praat), and streaming. |
| `tests/Reyfen.Timbratune.Core.Tests` | 36 tests: parity with `analyze.py` output on four VCTK fixtures, plus live-analysis tests. |
| `tools/live-steadiness` | Dev tool, not in the solution. Measures how steady and accurate the live dots are. |
| `scripts/` | Helper scripts; see the list below. |

**`scripts/`:**
- `fetch-praat.ps1`;
- `make-long-wav.ps1`: builds the 27.6 s test take;
- `screenshot.ps1`: Windows UI checks without a person;
- `linux-ui.sh`: the same for Linux/X11;
- `make-emoji-font.py`: builds the Android emoji subset.

**`Reyfen.Timbratune.Acoustics` components:**
- Boersma (1993) autocorrelation/cross-correlation pitch with a Viterbi path;
- Burg LPC formants with Aberth root finding;
- Kaiser-window intensity;
- harmonicity (HNR);
- glottal pulses, jitter and shimmer;
- spectrum and LTAS;
- a sinc resampler;
- a WAV decoder.

**`Reyfen.Timbratune.Core` main pieces:**
- **`Analysis/AcousticsAnalysisEngine.cs`:** the full analysis. It measures the frame tracks, then `RawAnalysisAssembler`, then `AnalysisPostProcessor`.
- **`Analysis/LiveAnalyzer.cs`:** the live analysis.
- **Other:** `Audio/WavFile.cs` (includes `ToPcm16` and `Truncate`), `Audio/FileReplayRecorder.cs`, domain `Zones` and `Metrics`, and storage (`RecordingStore`, `DataPaths`).

**`src/Reyfen.Timbratune` UI, key files:**
- **`ViewModels/RecordViewModel.cs`:** record, stop and analyze; runs the live timer at 100 ms; holds `LiveWindow`, the dropdown's current choice.
- **`ViewModels/LiveTimelinesViewModel.cs`:** one persistent instance per recording. The static `Compute(snapshot, window)` runs off the UI thread and returns a `Frame`; `Apply(frame)` updates the persistent `TimelineViewModel`s in place, so charts and their easing survive. It also holds the public `SmoothingSpec`s.
- **`ViewModels/TakeViewModel.cs`:** the take dashboard, also used for the live take.
- **`Analysis/RecentWindow.cs`:** the smoothing and prediction layer (`SmoothingSpec`, `RecentWindow.Smooth`).
- **`Controls/TimelineChart.cs`:** a live metric chart (zone bands, one bold line, the dot).
- **`Controls/ContourChart.cs`:** the pitch contour. For saved takes it draws the raw 10 ms frames. In live mode it gets `Zones`, `AxisStart`/`AxisDuration` and `LiveLine`, the smoothed line.
- **`Controls/LineEasing.cs`:** shared 0.25 s display easing.
- **Views:** `Views/MainView.axaml` (record panel with the dropdown, take switcher, the live panel and the take view), `Views/TakeView.axaml`, `Views/LiveTimelinesView.axaml`.
- **`Themes/Styles.axaml`:** includes `ComboBox.pill`, the dropdown style that matches the pill buttons.

## 5. Live analysis: how it works

### 5.1 Shared kernels, so live equals full
- **Pitch, HNR and intensity:** the full and live analyses call the same per-frame kernels: `PitchFrameAnalyzer`, `IntensityFrameAnalyzer` and `Harmonicity.CreateFrameAnalyzer`.
- **Deferred global-peak normalization:** frames store `LocalPeak`, and the path finder divides by the global peak at path time.
- **Path finder:** non-mutating (`PitchPath.ChooseIndices`), with an exact zero-cost shortcut for harmonicity.
- **Trackers** (`Reyfen.Timbratune.Acoustics/Streaming/`):
  - `LiveSignal` is a growable buffer with a running sum and peak.
  - The trackers are `LivePitchTracker`, `LiveIntensityTracker`, `LiveFormantTracker` and `LivePulseTracker`.
  - `LiveFormantTracker` re-analyses blocks with Burg, using a 0.25 s margin on each side and 0.5 s blocks.
  - `LivePulseTracker` analyses a voiced stretch once it has ended.
- **Live frame grid:** `x1 − 0.5dx + 0.5·window + k·dt`. The full grid is centred on the total duration, so on Stop the WAV is **trimmed by ≤ 10 ms** (`TimeGrid.AlignedLength`, `WavWriter.Truncate`) to make the two grids identical.
- **Quantization:** live samples are quantized exactly as the WAV stores them: `ToPcm16(s)/32768.0`.

### 5.2 Live vs saved, verified in `LiveAnalysisTests` on four fixtures

| Metric | Live vs saved |
|---|---|
| Pitch mean, median, SD, min, max; jitter; shimmer; in-register %; semitone SDs; contour (frame by frame); phrase count; phrases landed; tilt | exact |
| Formants | ≤ 11 Hz |
| HNR | ≤ 0.2 dB |
| Weight | ≤ 0.32 dB |
| Loudness mean and max | ≤ 0.03 dB |
| Loudness min | the silence floor, so not meaningful |

Cost: about 3 s of `Update` calls for 27.6 s of audio (Release, this machine). It keeps up in real time.

### 5.3 Update cadence
- Frames are 10 ms for pitch and HNR, about 10.7 ms for intensity and 6.25 ms for formants.
- The UI refreshes every 100 ms.
- Formants, and therefore F2, F3 and weight, arrive in **0.5 s blocks, about 0.5–0.75 s late**.

### 5.4 Delayed or approximate while live (the user asked for this list explicitly)

| Parameter | While recording |
|---|---|
| Pitch, last 0.1–0.3 s | Can still be revised by the Viterbi path |
| Phrases: count, endings landed, onset/mid/offset | Appear only after a pause of about 0.1 s |
| Jitter, shimmer | Appear only once a voiced stretch ends |
| Formants and weight | Approximate: relative to the loudest moment so far; the ceiling choice is re-made each update |
| Phrase boundaries | Approximate: relative to the loudest moment so far |
| LTAS tilt | Computed only at Stop (not shown in the UI) |

Nothing displayed is impossible live.

Workaround ideas, **not implemented**:
- dim the provisional tail and the open phrase;
- a "say *aaa* loudly" calibration at the start, so the loudest-moment reference is known;
- a fixed dB silence threshold while live.

### 5.5 Smoothing and prediction (layers 3 and 4) in `RecentWindow.Smooth`

**Stretches.** The points are split into stretches wherever neighbours are more than `MaxGap` apart. A window never crosses a pause, and stretches are separated by NaN markers.

**Per stretch.** A statistic is taken over a window **centred** on each grid time (`Step`, default 50 ms). The statistic is a median, an energy mean for loudness, or an SD for the "movement" metrics. It is valid only with at least `MinimumCount` points. After that comes a light centred mean over `PolishRadius` grid steps.

**Prediction** (`Predict`):
- **Settled point:** the last grid time whose window saw at least `SettledCoverage` (0.5) of its future half-width.
- **Blending the tail:** later values are blended toward `anchor + TrendDamping × slope × Δt`, where the slope is a least-squares fit over the last `TrendSeconds` of the settled line. The less future a window has, the more weight the prediction gets, down to `MinimumTrust` (0.15) for the window's own value.
- **Extending to "now":** the ongoing stretch is extended to the current time, so the current point is always shown.
- **Short stretches:** a stretch too short to have settled simply holds its newest value.

**Breaks.** `MaxStepRatio` breaks the line at implausible jumps; pitch uses 3 semitones per 20 ms, because octave slips flip the median between octaves.

**Display.** `LineEasing` glides every redraw over 0.25 s.

The specs are in `LiveTimelinesViewModel`:

| Spec | Used by | Statistic | Half-width (s) | Min count | MaxGap (s) | Notes |
|---|---|---|---|---|---|---|
| `LoudnessSmoothing` | loudness | EnergyMean | 0.5 | 5 | 0.3 | **no trend** (hold); trust 1 |
| `FrameSmoothing` | HNR | Median | 0.5 | 5 | 0.4 | trend 0.5 |
| `VowelSmoothing` | F2, F3, weight | Median | 1.0 | 6 | 0.8 | polish 4; trend 0.5 |
| `MovementSmoothing` | pitch variability (Hz SD), in-register melody (st SD) | StandardDeviation | 1.0 | 10 | 0.4 | |
| `JitterSmoothing` | jitter, one point per finished stretch | Median | 1.5 | 1 | 3.0 | polish 4 |
| `PitchSmoothing` | the live pitch contour | Median | 0.1 | 5 | 0.06 | step 0.02 s, polish 1, **no trend**, MaxStepRatio 2^(3/12) |

Axis:
- **Whole take:** the axis runs to the next multiple of 10 s: `AxisFor(e) = max(10, 10·ceil(e/10 − 1e-9))`.
- **Sliding window of W seconds:** the axis shows `[max(W, elapsed) − W, max(W, elapsed)]`.

Only the visible range, plus the window reaching into it, is smoothed.

### 5.6 Measurements behind these choices

These come from `tools/live-steadiness` on the 27.6 s take. Values are % of each graph's range, and lower is better.

**Trend prediction vs holding the newest value (the app's settings):**

| Metric | Dot step, baseline → app | Dot error, baseline → app |
|---|---|---|
| Weight | 1.66 → 1.51 | 11.5 → 10.7 |
| F2 | 4.72 → 4.25 | 48.9 → 47.5 |
| F3 | 0.69 → 0.66 | 5.2 → 5.0 |
| HNR | 4.00 → 3.83 | 8.9 → 9.5 |

The gains are real but small, about 5–10%.

**Loudness.** Every prediction variant was worse on both measures; the first version made the dot step 6.65 → 9.29 and the error 16.5 → 25.8. Loudness follows syllables, so it holds instead.

**Pitch:**
- The raw contour (the old live view) had a dot step of 10.0 and an error of 4.5 against the exact final contour.
- The smoothed line has a dot step of about 8.1, but an error of about 8.9, because smoothing departs from the raw contour by design.
- Trend prediction didn't help, so pitch holds.
- In screenshots, the smoothing clearly removes octave spikes and fragments.

**Formant block of 0.1 s instead of 0.5 s.** Values arrived sooner, but the total motion doubled (F2 dot step 4.7 → 8.2) and accuracy didn't improve. The block was kept at 0.5 s; `LiveAnalyzer(formantBlockSeconds:)` is now a parameter.

**What does help visually** is the 0.25 s display easing, which smooths the 0.5 s formant bursts. It doesn't change the numbers above.

F2's large "error" is real phonetics: F2 differs by about 700 Hz between /i/ and /a/, so a live dot about 1 s ahead of the formant data can't match the centred final line.

## 6. Ideas tried and rejected or limited
- Trailing-window means: jittery and lagging. Replaced by centred windows.
- Windows that cross pauses: produced lone dots carrying pre-pause values. Replaced by per-stretch windows.
- Trend prediction for loudness and pitch: measured worse.
- 0.1 s formant blocks: measured worse.
- Faint raw per-frame traces: removed at the user's request.

## 7. Developer workflow

```powershell
cd C:\Projects\Reyfen\Timbratune
dotnet build Timbratune.slnx
dotnet test Timbratune.slnx                     # 108 tests; the Praat oracle tests skip without Praat
dotnet run --project src/Reyfen.Timbratune.Desktop
dotnet publish src/Reyfen.Timbratune.Desktop -c Release -r win-x64 --self-contained false -o publish/Timbratune-win-x64
```

Environment variables:
- `TIMBRATUNE_DATA_DIR`: a throwaway data folder. The default is `%APPDATA%\Timbratune\`.
- `TIMBRATUNE_FAKE_MIC=take.wav`: the recorder replays that file in real time, in 10 ms blocks, instead of using the mic.
- `TIMBRATUNE_PRAAT`: the path to Praat, for the oracle tests.

UI check without a person:
1. `scripts/make-long-wav.ps1 -Out <tmp>\long.wav`
2. Set `TIMBRATUNE_FAKE_MIC` to that file and `TIMBRATUNE_DATA_DIR` to a temp folder.
3. `scripts/screenshot.ps1 -Exe src\Reyfen.Timbratune.Desktop\bin\Debug\net10.0\Reyfen.Timbratune.Desktop.exe -Out a.png -Wait 7`. It prints `pid=…`.
4. Click the record button. At 1216×939 with at least one take saved it is at (422, 254); with an empty data folder it is at y ≈ 185.
5. Wait, then run `-ProcId <pid> -Scroll 3` to reach the live panel.
6. Always pass `-ProcId` after starting, so the user's own copy of Timbratune is never touched.
7. PrintWindow doesn't capture popups; select dropdown items with the keyboard (Down, Enter via SendKeys).

Steadiness check: `cd tools/live-steadiness; dotnet run -c Release -- <tmp>\long.wav`

Shell gotchas on this machine:
- Git Bash heredocs containing `'` inside a python `-c` can break. Write the script to a file and run it.
- PowerShell 5.1 has no `&&`.
- .NET file APIs in PowerShell resolve against the process's directory. Use absolute paths.

## 8a. Platforms: Windows single exe, Linux, Android (2026-10-01)

**Reference voices:**
- They are off behind a feature flag, `src/Reyfen.Timbratune/Features.cs`.
- The MSBuild property `TimbratuneReferenceVoices` (default false) defines `REFERENCE_VOICES` and only then ships `Assets/reference/**`.
- When the flag is off:
  - the comparison pop-up shows your own takes only;
  - the hint text reads "tap to compare with your other takes";
  - `AppServices.ReferenceDir` is null.

**Version and file names:**
- The user's convention: every artifact is named `<app>-v<Version>-<platform>`, from `<Version>` in `Directory.Build.props` (now 0.1.0).
- Artifacts:
  - `Timbratune-Desktop-v0.1.0-win-x64.exe`
  - `Timbratune-Desktop-v0.1.0-linux-x64.deb`
  - `Timbratune-Desktop-v0.1.0-linux-x64.AppImage.tar.gz`
  - `Timbratune-v0.1.0-android.apk`
- How the names are produced:
  - Desktop: a post-publish target in `Reyfen.Timbratune.Desktop.csproj`.
  - Android: a post-publish target in `Reyfen.Timbratune.Android.csproj`, which also removes the unsigned APK and stray dlls.
  - Linux packages: `package-linux.sh`.
- The app shows the version in its footer, via `Features.VersionText` (the informational version without "+commit").

**Linux packages:** `scripts/package-linux.sh` runs in WSL; appimagetool is in `~/tools/appimagetool` there. It turns the binary into two packages:
- **`Timbratune-Desktop-linux-x64.deb`:** installs to `/opt/timbratune`, with `/usr/bin/timbratune`, a menu entry and an icon. This is the user's preferred option, because a downloaded AppImage needs "Allow executing" first, which the user found unintuitive. Verified: it installs, runs and removes in WSL.
- **`…AppImage.tar.gz`:** for other distributions. The AppImage is inside a tarball so its exec bit survives downloads, which the user asked for. Verified: it extracts as `rwxr-xr-x` and runs without `chmod`.

**Mint VM microphone: unfinished; the user stopped here.** Hyper-V's basic console has no audio.
- Host side, done: `Set-VMHost -EnableEnhancedSessionMode $true`, and the VM's transport is HvSocket.
- Guest side: `scripts/hyperv-mint-enhanced-session.sh` installs xrdp, `pipewire-module-xrdp` and XFCE, and switches only the first `port=` in xrdp.ini to vsock.
  - Changing every `port=` line was a bug that caused a blue screen after login. It is fixed in the script.
  - After running it: reboot, don't log in on the console (Mint auto-login must be off), reconnect, and enable remote audio playback and recording under Show Options.
- State reached: the XFCE remote desktop works and the app runs in it.
- Remaining problem: the mic fails with "FailedToOpenBackendDevice", most likely because the script also did `unset XDG_RUNTIME_DIR` in startwm.sh, which hides PipeWire. A fix script was offered (it deletes that line, then you log out and back in) but not confirmed.
- The user declined xrdp auto-login, because it would store the password in plain text.

**App bug seen:** a silent take shows "Loudness −300 dB · strong". −300 is the no-signal value, and `Zones.ZoneOf` puts values below the lowest zone into the last zone, a quirk carried over on purpose. Silent takes should say "no voice detected" instead. Not fixed yet.

**Windows single .exe:**
- `dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=win-x64` gives `publish/Timbratune-win-x64/Reyfen.Timbratune.Desktop.exe`, about 47 MB.
- It is self-contained and compressed, and bundles its native libraries. Symbol and doc files are excluded.
- `linux-x64.pubxml` is the same for Linux.

**Linux validation:**
- **Test environment:** WSL was updated to the Store version (3.0.1, which includes WSLg), and `Ubuntu-24.04` was installed.
  - The test user is `tester`.
  - Packages added for the app and tests: `libx11-6 libice6 libsm6 libfontconfig1 fonts-noto-color-emoji xdotool imagemagick pulseaudio-utils x11-utils fonttools`.
- **Driving the UI:** copy the binary to `~/timbratune/`, then drive it with `scripts/linux-ui.sh`.
- **Verified:**
  - the UI renders and the theme toggle works;
  - the fake-mic take gives live graphs, and the saved numbers are **identical to Windows**;
  - the **real mic** works (WSLg passes the Windows mic through PulseAudio);
  - takes save.

**Full desktop VM: NOT done yet.**
- The Linux Mint 22.3 ISO is downloaded and SHA-256 checked: `C:\VMs\iso\linuxmint-22.3-cinnamon-64bit.iso`.
- The VM script is ready: `C:\VMs\create-timbratune-mint.ps1`. It needs to run elevated and creates a Gen-2 VM with 4 CPUs, 6 GB, 40 GB, Default Switch, Secure Boot with the MS UEFI CA, booting from the DVD.
- The UAC prompt was cancelled the first time.
- Next steps: run it again, the user clicks through Mint's installer, then copy and run the Linux binary.

**Android:**
- **Project:** `src/Reyfen.Timbratune.Android`.
  - `net10.0-android`, minimum API 26, app id `com.reyfen.timbratune`, ABIs arm64 and x86_64.
  - `MainActivity : AvaloniaMainActivity<App>` wires up the services.
- **Toolchain:**
  - the android workload (36.1.69);
  - the SDK in `%LOCALAPPDATA%\Android\Sdk` and JDK 17 in `%LOCALAPPDATA%\Android\jdk`. Pass both via `-p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`, because the user's `JAVA_HOME` points at a Program Files JDK the tooling won't use;
  - the emulator AVD `EuphoniaPixel` (Pixel 7, API 35, google_apis x86_64). WHPX was already usable.
  - Boot it headless: `emulator -avd EuphoniaPixel -no-window -no-snapshot -no-boot-anim -gpu swiftshader_indirect -memory 4096`.
  - Use the SDK's own `platform-tools/adb.exe`.
- **Debug APK for adb install:** build with `-p:EmbedAssembliesIntoApk=true`. Otherwise fast deployment keeps the assemblies outside the APK.
- **Release:** `dotnet publish … -c Release -o publish/Timbratune-android`. It is signed with the debug key, which is fine for sideloading. It was renamed to `Timbratune-android.apk` (about 32 MB).
- **Verified on the emulator:**
  - the phone layout;
  - colour emoji;
  - the microphone permission prompt;
  - fake-mic live analysis;
  - Release with real AAudio capture: the emulator's virtual mic plays a 100 Hz tone, and the take measured 100.01 Hz;
  - takes save, about 5 s after Stop for a 12 s take in Release.

**Android gotchas found (each one is fixed in the code):**
1. **Recorder deadlock.**
   - Cause: `SoundFlowRecorder` held its lock while starting and stopping the miniaudio device, and the audio callback took the same lock. On AAudio that froze the UI thread (ANR, timer stuck at 0:00).
   - Fix: the callback now has its own `_write` lock, and the device is started and stopped without holding it.
   - This applies to all platforms; it was re-verified with the real mic on Windows and Linux.
2. **Emoji drew as boxes.**
   - Cause: Android's emoji font is COLRv1, which SkiaSharp 2.88 can't draw. The system font also isn't reachable by family name, and `IFontManagerImpl.TryCreateGlyphTypeface(Stream)` is hidden by Avalonia's reference assemblies.
   - Fix: the app carries a **54 KB CBDT subset** of Noto Color Emoji (OFL), `Assets/Fonts/TimbratuneEmoji.ttf`, built from Ubuntu's `fonts-noto-color-emoji` by `scripts/make-emoji-font.py`. It is registered as an `EmbeddedFontCollection` plus a `FontFallback`.
   - **Re-run the script when adding emoji to the UI.**
3. **`debug.mono.env` aborts the app.** On .NET for Android 36.1, any value there aborts the runtime (an off-by-one in monodroid). So the Android fake mic is a file instead: Debug builds use `files/fake-mic.wav` if it is present.
4. **Phone layout.**
   - Fixed-width `WrapPanel`s became `Controls/CardGrid`: equal columns of at least N px, stretched to fill the row. That gives one column on a phone, and the desktop now fills its rows exactly.
   - The recording row is now a `WrapPanel`.
   - The register drop bars are now a 3-column `UniformGrid`.
5. **Runtime microphone permission.** `AppServices.RequestMicrophone` is null on desktop. On Android, `RecordViewModel.StartAsync` awaits it before recording.

**Open Android item:** SkiaSharp 2.88.9's `libSkiaSharp.so` isn't 16 KB page-aligned (build warning XA0141).
- Android 15+ devices with 16 KB pages, and Google Play from late 2025, need that alignment.
- Fix: move the Android head to SkiaSharp/HarfBuzzSharp 3.x, which Avalonia 11.3 supports, or to a newer Avalonia.
- Not needed for sideloading on today's 4 KB devices.

**Automation gotchas:**
- **Git Bash path conversion:** it rewrites `/mnt/...` arguments to `wsl.exe` and `adb`. Set `MSYS_NO_PATHCONV=1`.
- **`pkill -f <name>`** also kills the `bash -c` that contains the name. `linux-ui.sh` uses the pattern `'[R]eyfen.Timbratune.Desktop|[T]imbratune-Desktop'`; the brackets stop it matching itself.
- **Click timing:** Release starts slower than Debug, so wait about 10 s before tapping.
- **Background focus:** Windows ignores `SetForegroundWindow` from a background process, and then the first click only activates the window. That looked like "first click does nothing". `screenshot.ps1` now taps Alt first.
- **Typing collisions:** the user sometimes uses the PC at the same time; their typing landed in the test window once.
- **The user's app:** `publish/Timbratune-win-x64` is locked while their copy runs. The new single exe was staged in `publish/Timbratune-win-x64.new/` to swap in once it's closed.

## 8b. Rename to Timbratune (2026-10-02)

**What changed:**

| What | Before | After |
|---|---|---|
| Folder | `Euphonia-CSharp/` | `Timbratune/`, then the repo root (2026-10-06) |
| Solution | `Euphonia.slnx` | `Timbratune.slnx` |
| Projects, assemblies and namespaces | `Euphonia.*` | `Reyfen.Timbratune.*`; folders `src/Reyfen.Timbratune.*`, `tests/Reyfen.Timbratune.*.Tests` |
| JSON helper | `EuphoniaJson` | `TimbratuneJson` |
| Environment variables | `EUPHONIA_*` | `TIMBRATUNE_*` (`DATA_DIR`, `FAKE_MIC`, `PRAAT`) |
| Build switch | `EuphoniaReferenceVoices` | `TimbratuneReferenceVoices` |
| Android app id | `app.euphonia` | `com.reyfen.timbratune`, a new app beside the old one |
| Linux package and command | `euphonia` | `timbratune` |
| Artifacts | `Euphonia-*` | `Timbratune-Desktop-v0.1.0-<rid>…`, `Timbratune-v0.1.0-android.apk` |

- **`Directory.Build.props`:** Authors and Company are Reyfen, the Product is Timbratune, and the Description is "Timbratune — Gender Voice Analysis Tool".
- **UI:** the window title is "Timbratune — Gender Voice Analysis Tool" and the header reads "Timbratune".
  - The footer adds "Timbratune v0.1.0 · by Reyfen · a fork of [Euphonia] · algorithms inspired by [Praat]".
  - The two names are `HyperlinkButton`s; the links are in `Features.cs`.

**Data:** `DataPaths.Default()` moves `<AppData>/Euphonia-CSharp` to `<AppData>/Timbratune` on first start, if only the old one exists. If the move fails, it keeps using the old folder.

**Kept as "Euphonia" on purpose:** anything describing the original app, e.g. "port of ContourChart.tsx", "the Electron app", "Euphonia-TypeScript/analyze.py". The emulator AVD is still named `EuphoniaPixel`.

**Credits:**
- The README credits the fork origin (https://github.com/Yuuzulight/Euphonia) and Praat (https://github.com/praat/praat.github.io).
- The wording is explicit that no Praat source code was copied, translated or adapted. This appears in the README "Licensing and credits" section and in `REFERENCES.md`.

**Submodules, optional by design:**
- **`Euphonia-TypeScript`:** points to `Yuuzulight/Euphonia` at `247598f`. The local copy was identical to upstream apart from build artifacts. It is checked out locally.
- **`Praat`:** points to `praat/praat.github.io` at `74cabcc`. It is not checked out.
- Both have `update = none` and `shallow = true` in `.gitmodules`, so a clone or `git submodule update --init` skips them. Fetch one with `git submodule update --init --checkout [--depth 1] <path>`.
- The `/Praat/` gitignore rule was removed.

**Backups:** the previous local folders, including `.venv`, `node_modules` and the 381 MB Praat source, were moved to `C:\Projects\TEDinc\_backup-before-submodules\`. The user can delete them.

**Location:** on 2026-10-02 the user moved the repo to `C:\Projects\Reyfen\Timbratune`; on 2026-10-06 the app moved from its `Timbratune/` subfolder to the repo root. The old copy at `C:\Projects\TEDinc\Euphonia` is left for the user to delete. Claude's project memory for the new path is in `C:\Users\mihai\.claude\projects\C--Projects-Reyfen-Timbratune\memory\`. The older transcripts stay under `...\C--Projects-TEDinc-Euphonia\`.

## 8c. Live numbers, time trends, real progress (2026-10-06)

- **Live numbers:** each live graph's card shows its current value (the dot's value) beside the title, e.g. "186 Hz" (`LiveTimelinesViewModel.ValueText`, style `TextBlock.live-value`).
- **Trends within this take** are now time slices instead of one point per phrase:
  - `AnalysisPostProcessor.TrendStep` picks the shortest round step from 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 30 … s that gives at most 10 points (27.6 s → 3 s → 3, 6, … 27).
  - `TimeTrends` stores them in `RecordingDetail.Trends`. Each slice covers the step around its point; the first starts at 0 and the last runs to the end.
  - The charts are pitch, melody, phrase endings, F2, **F3**, weight, **clarity (HNR)** and **steadiness (jitter)**. HNR and jitter per slice come from the new optional `FrameTracks.Harmonicity` / `Pulses`, carried through as `RawAnalysis.HnrFrames` / `Pulses`.
  - Older takes rebuild the pitch trends from the contour and are re-analyzed once in the background (`MainViewModel.BackfillTrendsAsync`).
  - `PhraseMetrics` is still computed and saved but no longer shown.
- **Analysis progress bar:**
  - The analyzers take an optional `Action<double>` progress callback: `PitchAnalyzer`, `HarmonicityAnalyzer`, `FormantAnalyzer.Burg`, `Resampler.Resample`, and `Fft` (per butterfly pass).
  - `StageProgress` weights the stages by measured CPU time. The formant low-pass FFT (2²¹ points) is the critical path.
  - Measured: reported progress tracks wall time to within about 10 points.
  - After Stop, the bar covers: last live update 15%, analysis 80%, save 5%. The text adds "about N s left" from 15% on.
- **Resampler:** the sinc interpolation now runs in parallel chunks. Results are identical (all tests pass); the median full analysis of the 27.6 s take went from 715 to 645 ms.
- Tests: 101 (`TrendTests` added).

## 8d. v0.2.0: Android performance on real phones (2026-10-07)

Branch `v0.2.0` (from `dev`): commits `82c5621`, `750b29a`, `2fa4420`, `b5d73f4`. The version is 0.2.0 (`Directory.Build.props`), with Android `ApplicationVersion` (versionCode) 2 and versionName 0.2.0.

The user tested 0.1.0 on a **Pixel 9** and a **Pixel 4a** and found live recording far too slow and janky ("It is not acceptable"). Everything below was measured on those two phones over USB (adb), not the emulator, which runs at desktop speed.

**Constraint from the user:** formants and weight must not be degraded: same algorithms, same 0.5 s formant blocks, same results. Charts and the reader may lag a little.

### What caused the jank (Pixel 9, 30 s fake-mic take)
1. **Rendering backend.** OpenGL ES gave 38 fps with 40 % janky frames (> 25 ms). Vulkan gave 50 fps with 11–12 % janky frames.
2. **CPU contention.** Live updates ran back to back on all 8 cores (about 50 ms each on the Pixel 9, 70 ms on the 4a), starving the UI and render threads.
3. **Garbage collection.** The live analysis allocated about 30 MB/s. Mono's 4 MB nursery then collected about 5×/s, plus about 0.5 full collections/s. Each collection stops the world and also triggers a ~10 ms Java collection through the GC bridge (logcat: "Explicit concurrent copying GC").
4. **UI-thread work:** chart easing redraws every frame (ContourChart ~2–3 ms, TimelineChart ~1.5 ms) and live-value text changes that re-laid out their cards.

The algorithm itself wasn't the problem: each update is fast enough, but it competed with the UI.

### Fixes, in order (each measured)
- **`82c5621`** (first round):
  - Release builds use **LLVM full AOT** (`EnableLLVM`, no profiled AOT). On the Pixel 4a the analysis after Stop went from 12.7 to 6.6 s for a 30 s take, and live updates from 258 to 106 ms. It costs about 10 MB of APK and a few minutes of build time.
  - **Mobile-only cheaper look** (set up in code in `App.Initialize`, not with `OnPlatform` in XAML, which crashed on desktop): a crisp card shadow instead of a blurred one, and a two-stop vertical page gradient. iOS should get the same when added.
  - **Cheaper FFT:** real input as a half-size complex FFT, with pooled buffers. Results are identical.
  - The **profiling build** (see below).
- **`750b29a`:** `AndroidPlatformOptions.RenderingMode` = Vulkan, then Egl, then Software.
- **`b5d73f4`** (jank round):
  - **Scheduling:** live updates run on one dedicated thread (`Analysis/LiveWorker.cs`) at Android background priority (`AppServices.LowerThreadPriority`). At most one update starts per 100 ms, and nested parallelism is capped at 2 cores through an ambient limit (`Acoustics/Numerics/Parallelism.cs`, `Parallelism.Limit(n)` / `Parallelism.Options`, used by every `Parallel.*` in the analysis). The analysis after Stop keeps all cores.
  - **Mono GC:** `Properties/GcEnvironment.txt` sets `MONO_GC_PARAMS=nursery-size=16m,major=marksweep-conc` (an `AndroidEnvironment` item; `-p:TimbratuneGcEnv=false` leaves the defaults). **32 MB makes the runtime fail at startup** (LinkageError).
  - **Allocations** (desktop live analysis 69 → 12 MB/s):
    - pooled Burg scratch buffers (`BurgLpc.Scratch`, `FormantAnalyzer.FrameScratch`);
    - one forward FFT shared by both formant ceilings (`FormantAnalyzer.BurgAll` = `ResampleForCeilings` + `BurgResampled`; `LiveFormantTracker.UpdateAll`);
    - pooled low-pass buffers in `Resampler`;
    - double-buffered live series lists in `LiveAnalyzer.BuildSeries`;
    - views instead of copies: the stitched formant track, `RecentWindow.From`;
    - in-place `RecentWindow.Polish`.
  - **Charts:** `LineEasing` blends into a reused buffer; cached pens and brushes; points closer than 0.75 px are thinned.
  - **Fixed-width live values** (`TextBlock.live-value`: Width 140, Height 28, right-aligned), so text changes don't re-lay out the card.

**Result, Pixel 9:** 50 fps / 11–12 % janky → **57.6–58.4 fps / 2.3–3.1 % janky**, 3–6 frames over 50 ms per 30 s, stop-to-saved 2.4–2.7 s. The intermediate steps were: low-priority 2-core analysis 54–56 fps; plus the GC settings 56.6–57.6 fps; plus the allocation work 57.5–58 fps.

**Results unchanged:** all 108 tests pass. An old-vs-new comparison against `bc1ce48` found all 118,536 saved-analysis values identical. The live lines are identical apart from last-digit rounding (max 3e-8 in HNR, F2, F3, weight and jitter).

### Profiling build and how to measure
- **Build:** `dotnet publish src/Reyfen.Timbratune.Android -c Release -r android-arm64 -o <dir> -p:TimbratuneProfiling=true -p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`, then `adb install -r <apk>`. Never uninstall: that deletes the user's takes.
- **What it adds** (`PROFILING` define). Regular builds keep the `Perf` calls, but with no sink and no flags they cost nothing measurable:
  - a profileable manifest (`Properties/AndroidManifest.Profiling.xml`);
  - `Diagnostics/Perf` probes to logcat (`adb logcat -s Timbratune`, lines `PERF <step> <ms>`);
  - a UI stall watch (`ui.stall` > 32 ms) and a GC watch.
- **Experiment switches:** words in `/sdcard/Android/data/com.reyfen.timbratune/files/perf-flags.txt`, read at startup:
  - probes and logging: `noprobes` (no logging);
  - rendering: `egl` (OpenGL instead of Vulkan), `overlay` (Avalonia frame-time overlay);
  - look: `noshadow`, `flatbg`, `grad2`, `bmpbg`;
  - live analysis: `fullcpu`, `lowprioN`;
  - live UI: `noease`, `slowcharts` (charts at most every 200 ms), `nolivetake` (no take-card refresh), `liveslow`;
  - FFT: `fftbench`.

  Delete the file afterwards.
- **Fake mic:** push a WAV to `files/fake-mic.wav` in the same folder. Takes then go to `files/profiling-data`, not the user's takes. Remove both afterwards.
- **Frame times:** `adb shell dumpsys SurfaceFlinger --latency '<layer>'` on the app's `SurfaceView(BLAST)` layer. Clear it with `--latency-clear` and sample every ~1.5 s while recording. Find the layer in `dumpsys SurfaceFlinger --list`; on Android 17 the entries look like `RequestedLayerState{<name> parentId=…}`.
- **Driving the phones:** `adb shell input tap` with the record and stop buttons' coordinates (Pixel 4a: record 273,662, stop 348,543). On the Pixel 9, swipes high on the page land in the label TextBox and glide-type into it, so swipe lower down.
- **Allocation by type, desktop:** an in-process `EventListener` on the runtime's `AllocationTick` events while replaying a take through `LiveAnalyzer`.
- **Phone etiquette:** the user enables "Stay awake" themselves; don't change phone settings.

### Remaining and ideas
- About 2–3 % janky frames remain on the Pixel 9. The next candidates are incremental contour and phrase tracking (live.contour still allocates about 41 MB per take, `LiveTimelinesViewModel.Compute` about 53 MB, live.formants about 34 MB) and caching the static chart layer (bands, axes, dividers).
- **Pixel 4a not re-measured** with `b5d73f4`; it still has a build without it.
- **XA5300 "Android SDK directory could not be found"** with a valid SDK: a long-running MSBuild node had cached a failed lookup. `dotnet build-server shutdown` clears it. `build.bat` now publishes Android with `--disable-build-servers` (`2fa4420`).

## 8e. Save menu and the .tmbr export (2026-10-08)

The 💾 button on each "All recordings" card now opens a menu (`RecordingCardView.axaml`, a `Button.Flyout` with a `MenuFlyout`, styled as `MenuFlyoutPresenter.save-menu` in `Styles.axaml`):
- **save audio (.wav):** the WAV copy as before (`RecordingItemViewModel.SaveCopyAsync`).
- **export data (.tmbr):** `RecordingItemViewModel.ExportDataAsync` → `MainViewModel.ExportDataAsync`. The card shows "preparing the export…", then "exported to <name>" or the error.
- **export PDF · coming soon:** disabled on purpose, in faint ink. Not implemented.

**File dialogs:** `IFileDialogs.SaveAsync(title, suggestedName, typeName, extension, mimeType, write)` is the general "save a new file" call. `SaveCopyAsync` is built on it. The `.tmbr` type uses MIME `application/octet-stream`, so Android's SAF picker shouldn't rename it to `.zip`. **Not yet checked on a phone.**

**Per-frame lists are now kept with each take** (the user chose this over computing them on each export):
- The full analysis (`AcousticsAnalysisEngine.AnalyzeAsync`) builds `AnalysisResult.Series` (`Models/TakeSeries`) from its tracks with `Analysis/FrameSeriesBuilder`.
  - That is the same code the live graphs use: `LiveAnalyzer.BuildSeries` now calls it. Live output is unchanged (all live lines identical to `b5d73f4`).
  - Series: `pitch` (every 10 ms frame, null = unvoiced, equal to the saved contour), `loudness` (every intensity frame, 0.8/75 s), `hnr` (voiced 10 ms frames), `f1`/`f2`/`f3` (loud voiced frames with F1 in 250–1000 Hz, 5500 Hz ceiling), `weight` (corrected H1*–A3* on every measurable voiced frame, not the ≤ 250 subsample the metric uses), `jitter` (% per voiced stretch, at its end).
  - Each series is `{unit, step_s, description, t[], values[]}`, with explicit times, so an importer needs no grid maths.
- **Storage:** `RecordingStore` writes them to `analysis/<id>.series.json`, apart from `<id>.json`, so loading the dashboard isn't slowed.
  - Writes are streamed and atomic (tmp, then rename). The file is deleted with the take.
  - Values are rounded: t to 1 ms, Hz to 0.1, dB to 0.01, % to 0.001.
  - They are saved after Stop, on `--import`, by the trends backfill, and on the first export of an older take.
- **Memory:** the lists exist only in the `AnalysisResult` until the store has written them. Nothing in the UI holds them; export reads them from the file, writes the archive and lets them go.
- **Cost, desktop, 39 s take:** +14 ms on a 546 ms analysis (2.5 %); series file 132 KB vs 51 KB for the detail file. The progress stages are weighted from this (measure 93.5, assemble 2, postprocess 1, series 2.5). Not yet measured on a phone.

**The .tmbr format** (`Storage/TakeArchive.cs`): a zip with
- `take.wav`: the stored WAV byte for byte (Fastest compression: 3.4 MB → 1.8 MB);
- `take.json`: `Models/TakeExport`, snake_case, source-generated (`TimbratuneJsonContext`):
  - `format`: "timbratune-take", `exporter_version` 1, `app_version` (`Features.Version`), `exported_at` (UTC);
  - `take`: id, label, note, date, duration_s;
  - `audio`: file, sample_rate, channels, bits_per_sample (read from the WAV's fmt chunk);
  - `analysis`: the settings behind the numbers: pitch floor/ceiling/step, intensity step, HNR step, formant ceiling/count/window/step, register floor, semitone reference, trend step;
  - `metrics`: the take's `Recording` numbers, without data-folder paths or the source file name;
  - `detail`: the saved `RecordingDetail` (contour, phrases, register summary, trends; older takes get trends rebuilt from the contour, as the take view does);
  - `series`: the per-frame lists.

`TakeArchive.Read` returns the `TakeExport` and refuses other zips. It is the starting point for a future import; nothing in the UI uses it yet. Bump `exporter_version` when a field changes meaning or disappears.

**Tests:** `ExportTests` (4) cover the series grid and values, the store keeping and deleting them, the archive round trip (WAV byte-identical, metadata, lists equal) and refusing a foreign zip.

**Emoji:** the menu adds 🎵 📦 📄, so `scripts/make-emoji-font.py` was re-run (WSL, Ubuntu's Noto Color Emoji).

## 8. Current state, at the time of writing

**Branches:** `dev` holds everything up to `bc1ce48`. `v0.2.0` adds the Android performance work of §8d and is pushed to `origin/v0.2.0`. The save menu and `.tmbr` export (§8e) are not committed yet.

**Tests:** 112/112 pass.

**Publish:** `build.bat [win] [linux] [android]` builds into `publish/`. All three 0.2.0 builds are there; the Android APK was built from `b5d73f4`'s code.

**Phones:** the Pixel 9 has a profiling build of `b5d73f4`'s code (probes on, no flags file, no fake mic). The Pixel 4a has an older build without the jank fixes.

## 9. Possible next steps (none requested yet)
- Dim the provisional part of each line: the unsettled tail and the "now" extension.
- Loudness calibration or a fixed silence threshold, to stabilize formants, weight and phrases early in a take.
- Choose the formant ceiling once per take, or with hysteresis, to avoid live re-choices.
- Report `tools/live-steadiness` numbers in a test with bounds, so steadiness regressions get caught.
- Finish the Mint desktop VM check (§8a).
- Fix Android's 16 KB page alignment (§8a).
- Use a release keystore and an AAB for the Play Store.
- Re-measure the Pixel 4a with the jank fixes, and push the remaining 2–3 % janky frames on the Pixel 9 down (§8d).
- Import `.tmbr` files (`TakeArchive.Read`) and show them without re-analysis; implement the PDF export (§8e).
- Check the `.tmbr` export through Android's file picker, and the series cost after Stop, on a phone (§8e).
- Live charts in the light theme have pale zone bands; their contrast could be improved.
- Port to macOS and iOS: same pattern, SoundFlow has natives for both.
