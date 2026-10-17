# Timbratune: handoff notes

This file brings a new engineer or agent up to date on everything done so far: what the user asked for, what was decided and why, how the code is organised, what was measured, and what is still open.

Last updated 2026-10-16. The newest work is §8i–§8s (refactoring P0: golden, architecture, bench and phone baselines; P1: dependency injection and shared ports; P2: storage, settings and scheduling seams; P3: UI feature modules; P4: import formats; P5: analysis tracks; P6: metric modules; P7: registry-driven UI; P8: telemetry groundwork; P9: final performance check; P10: documentation, the refactoring is done); §8d–§8h cover v0.2.0 (Android performance, export, one folder per take, import, `build.ps1`); §8 is the current state.

**Name:** the project is **Timbratune — Gender Voice Analysis Tool**, by **Reyfen**.
- **Before 2026-10-02 it was called Euphonia**, and it is a fork of [Euphonia](https://github.com/Yuuzulight/Euphonia). Older parts of this file and of the transcript say "Euphonia" and "Euphonia-CSharp"; read those as Timbratune and the repo root (the code moved from `Timbratune/` to the root on 2026-10-06).
- See §8b for the rename itself.

The full conversation transcript, if you need exact wording, is at `C:\Users\mihai\.claude\projects\C--Projects-TEDinc-Euphonia\e751b8bb-a930-433a-b573-0bbfba026197.jsonl`. It is JSON Lines and very large; grep it rather than reading it whole.

---

## 1. Ground rules

- **Commit only when the user explicitly asks.** The main branch is `main` (it was `dev` until 2026-10-10); version work is on `dev/<version>` branches, now `dev/v0.2.0`. Releases are tagged `v<version>`. The user sometimes commits the work themselves; the recent commits "Add live update" and "Add graph stability" are theirs.
- **Licence constraint.** Praat's algorithms are used only as inspiration, the way a human programmer would read them. Code is written from the published papers; nothing is copied from Praat's GPLv3 source, and the result is MIT-compatible. In the user's words:

  > "Read the algorithms of Praat only as inspiration, as any human programmer would do. Do not copy it directly. Also, keep the papers based on it. I don't want to carry the GPL v3 license because there is no way for me to deeply integrate it and stuff like this. I take all the responsibility on myself."

  Praat's source lives in the optional `Praat` submodule (reference only). `Praat.exe` is used **by tests only** (the oracle), fetched with `scripts/fetch-praat.ps1`.
- **Cross-platform.** Windows comes first, but macOS, Linux, Android and iOS must stay possible, so **no Windows-only dependencies**.
- **Don't touch the user's running app.** The user often has the published build open from `publish/Timbratune-win-x64`. It locks the folder, so `dotnet publish` fails. Check with `Get-Process | ? Path -like '*Timbratune*'` (the single-file exe's process is named `Timbratune-v<ver>-win-x64` (`Timbratune-Desktop-v<ver>-win-x64` for builds before 2026-10-08), Debug builds `Reyfen.Timbratune.Desktop`; the `Path` column tells you which build it is) and ask before closing it. Never kill it without asking.
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
4. Set up the git repo with `dev` as the main branch (renamed `main` on 2026-10-10), and commit only when asked.
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
12. **Rename to Timbratune** and the move to `C:\Projects\Reyfen\Timbratune` (§8b).
13. **Live numbers, time-slice trends, a real progress bar** (§8c), pitch contour bridging, loudness and pitch-variability trends.
14. **v0.2.0 branch: Android performance on real phones** (Pixel 9, Pixel 4a; §8d). The user wanted jank gone "completely" without degrading formants or weight.
15. **Save menu:** save audio, export data as `.tmbr` (zip of WAV + JSON with every graph's data and metadata, for a future import), PDF placeholder (§8e).
16. **No `recordings.json`:** one folder per take, a clickable folder link, auto-import of dropped files, the import button, Android data visible over USB (§8f).
17. **Cards and build scripts:** framed icon buttons, rename, plain "#N", import beside Record, `build.sh`, "Desktop-" dropped from artifact names, date and time beside the take title (§8g).
18. **One `build.ps1`** instead of `.bat` + `.sh`, a pre-emptive fix for "Android SDK directory could not be found", and finding why the user's builds didn't see the SDK (§8h).

## 4. Codebase map (repo root)

> Since the refactoring (2026-10-15) the UI library is organised by feature: `Features/<Name>/`, `Shared/`, `Shell/`, `App/` (§8j–§8l, and the CLAUDE.md layout table). The `ViewModels/…` and `Views/…` paths below are from before that; the classes kept their names, except `MainViewModel`/`MainView` → `Shell/ShellViewModel`/`ShellView` and `RecordingItemViewModel` → `Features/TakeLibrary/TakeCardViewModel` (its dialog part is `Features/TakeActions/TakeDialogViewModel`). `Analysis/LiveWorker.cs` is in Core now; `Analysis/RecentWindow.cs` is in `Features/LiveMonitor/`.

| Project | Role |
|---|---|
| `src/Reyfen.Timbratune.Acoustics` | Pure C#, MIT, no dependencies. See the component list below. |
| `src/Reyfen.Timbratune.Core` | Domain logic, independent of the UI. |
| `src/Reyfen.Timbratune.Audio.SoundFlow` | Recording and playback through SoundFlow 1.4.1 (miniaudio). The recorder raises `SamplesCaptured` after each WAV write. |
| `src/Reyfen.Timbratune` | Avalonia 12 UI library, using CommunityToolkit.Mvvm. |
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

**Re-measured 2026-10-15** (`long.wav`, 27.6 s, after fixing the tool, §8i). Weight, HNR, loudness and pitch are unchanged. F2 and F3 moved because the formant loudness gate changed in v0.2.1 (§6a); with that gate set back to analyze.py's (`LoudOutlierFraction` 0) the tool gives exactly the F2 and F3 rows above. With the current gate:

| Metric | Dot step, baseline → app | Tail change, baseline → app | Dot error, baseline → app |
|---|---|---|---|
| Pitch | 8.46 → 8.31 | 0.60 → 0.62 | 8.71 → 8.98 |
| Loudness (holds) | 6.65 | 1.99 | 16.48 |
| HNR | 4.00 → 3.83 | 1.48 → 1.53 | 8.90 → 9.54 |
| F2 | 5.03 → 4.54 | 2.18 → 2.12 | 48.21 → 46.44 |
| F3 | 0.69 → 0.73 | 0.34 → 0.35 | 4.48 → 4.26 |
| Weight | 1.66 → 1.51 | 0.70 → 0.72 | 11.48 → 10.70 |

With the new gate the trend no longer steadies F3's dot (0.69 → 0.73) but still makes it more accurate; for F2 it helps a little more than before.

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
- Formant quality filters on bandwidth (F1–F3 ≤ 300/400/500 Hz) or on the number of poles kept (≥ 4, ≥ 5): the F2 frame-to-frame step did not improve on the noisy takes, and they threw away 50–80 % of clean VCTK frames. Not shipped.

### 6a. Loudness gate and the "shaky formants" note (2026-10-14, v0.2.1)
Two takes (voice-take-8/9, noisy: 6–12 kHz hiss about as loud as the voice) showed broken or no formants. Findings, with the C# Burg matching Praat to 0.003 Hz on both, so the analysis itself was right:
- The vowel-core gate (voiced frames within 10 dB of the loudest frame) was set by one 50 ms click. Take 9: 3 loud frames, 0 formant rows. Now the reference is the loudest value after setting aside the loudest 1 % of intensity frames (`RawAnalysisAssembler.LoudOutlierFraction`; none below 100 frames). `AcousticsAnalysisEngine.LoudOutlierFraction = 0` gives analyze.py's gate; the parity tests use it. Live and saved share the code, so they still agree.
- Clean takes move a little too (vctk_f339: F1 628 → 597, F2 1251 → 1316, F3 2796 → 2671 Hz; the other three ≤ 15 Hz), so formant numbers of old takes are not exactly comparable with new ones.
- `Formants.UsablePct` (take.json `formants.usable_pct`): the share of the ≤ 300 sampled loud voiced moments with F1 in 250–1000 Hz. Clean speech 93–98 %, the noisy takes 58–61 %. Below 75 % the take view adds a "formants are shaky" note under the F1 line. Null under 30 moments and in older takes.

## 7. Developer workflow

```powershell
cd C:\Projects\Reyfen\Timbratune
dotnet build Timbratune.slnx
dotnet test Timbratune.slnx                     # 243 tests; the Praat oracle tests skip without Praat
dotnet run --project src/Reyfen.Timbratune.Desktop
./build.ps1 [win] [linux] [android]             # all published builds into publish/ (§8h)
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

Speed and allocation check (refactoring gate, §8i): `cd tools/analysis-bench; dotnet run -c Release -- -baseline baseline-p0.json`

Shell gotchas on this machine:
- Git Bash heredocs containing `'` inside a python `-c` can break. Write the script to a file and run it.
- PowerShell 5.1 has no `&&`.
- .NET file APIs in PowerShell resolve against the process's directory. Use absolute paths.

### Release notes

The user wants them short enough for a person to read at a glance (a longer first draft was sent back).
- **What to cover:** what changed for users since the previous release, from `git log` since the commit the last published build was made from (the 0.1.0 builds came from 2026-10-06 ~23:40, before `7fc6bfa`).
- **Format:** one `## Timbratune vX.Y.Z` heading, then about 6-8 bullets, one per area (speed, import/export, takes, graphs, phone fixes, builds). Each bullet is "Area: one plain sentence", with a number only where it helps (e.g. fps).
- **Style:** no bold or other emphasis markers, no internal names (classes, files, commits), no developer-only details unless they affect users.
- **Delivery:** in a fenced code block, so it can be copied as is.

Example (v0.2.0):

```markdown
## Timbratune v0.2.0

- Smoother on Android: live recording is fluid (Pixel 9: ~58 fps instead of 38), and analysis after Stop is about twice as fast.
- Import: add .wav, .mp3, .flac or .tmbr files with the new Import button, or drop them into the takes folder.
- Export: the save button now saves the audio or exports all of a take's data as a .tmbr file. PDF export is coming.
- Takes as folders: each take is its own folder, so you can copy or delete takes by hand. Existing takes convert automatically. Click the folder path in the footer to open it.
- Rename takes in place, and see when each take was recorded next to its title.
- Graphs: new loudness and pitch-variability trends, and pitch lines no longer break up when you jump quickly.
- Phone fixes: takes can be reached from a PC over USB, and text that was cut off or hidden on small screens now shows in full.
- Builds: one build script for Windows and Linux, and simpler file names (Timbratune-v0.2.0-win-x64.exe).
```

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
  - `AndroidApp : AvaloniaAndroidApplication<App>` (Avalonia 12 builds the app there, before any Activity) wires up the services, fonts and renderer; `MainActivity : AvaloniaMainActivity` handles the window (microphone permission, Vulkan surface watcher).
- **Toolchain:**
  - the android workload (36.1.69);
  - the SDK in `%LOCALAPPDATA%\Android\Sdk` and JDK 17 in `%LOCALAPPDATA%\Android\jdk`. Pass both via `-p:AndroidSdkDirectory=… -p:JavaSdkDirectory=…`. **Correction (2026-10-09, §8h):** these were installed from Claude's shell, so they only exist in Claude's redirected AppData and the user's own builds can't see them. The user's `JAVA_HOME` (Microsoft OpenJDK 17.0.6) does work with the tooling;
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

**Android item (resolved 2026-10-15 by Avalonia 12, which brings SkiaSharp 3.119 / HarfBuzzSharp 8.3):** SkiaSharp 2.88.9's `libSkiaSharp.so` isn't 16 KB page-aligned (build warning XA0141).
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

Branch `v0.2.0`, now `dev/v0.2.0` (from `dev`, now `main`): commits `82c5621`, `750b29a`, `2fa4420`, `b5d73f4`. The version is 0.2.0 (`Directory.Build.props`), with Android `ApplicationVersion` (versionCode) 2 and versionName 0.2.0.

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
- **v0.2.1, Vulkan and a replaced surface (2026-10-15).** With Vulkan the app went black in split screen (Pixel 9, Android 17, next to Discord).
  - **Cause, from logcat and decompiled Avalonia 11.3.22:**
    - Entering split screen (and coming back to the app at all) replaces the SurfaceView's `Surface`: there is a new native window, and the old one's buffer queue is abandoned.
    - Avalonia's `VulkanDisplay` keeps its `VkSurfaceKHR` from the old window. `EnsureSwapchainAvailable` sees a size mismatch and re-creates the swapchain on that dead surface every frame. Logcat shows `BufferQueue ... has been abandoned` and `NATIVE_WINDOW_MIN_UNDEQUEUED_BUFFERS query failed` at 60 Hz (4,476 in 75 s), and the pane stays black.
    - The surface is only rebuilt on `VK_ERROR_SURFACE_LOST_KHR`. `AndroidVulkanSurface.CreateSurface` itself reads the current window each time.
    - The same code is in Avalonia 12.1.4: the decompiled `Avalonia.Vulkan` differs only in composite-alpha selection and interfaces.
  - **What didn't help:**
    - refreshing layout and invalidating on `OnConfigurationChanged` / `OnMultiWindowModeChanged`;
    - a forced full repaint after resizing (nudging the root's opacity), tried on the theory that only damaged regions were redrawn (Uno #24865).
  - **Fix** (`MainActivity.WatchSurface`): an `ISurfaceHolderCallback` on Avalonia's SurfaceView. When its surface is created again (any replacement) and Vulkan is in use, the Activity is re-created; at most once a second. A new view builds a new Vulkan surface from the current window.
    - It takes about half a second and flashes. The process, recorder, view models and the main view itself (static `SingleViewLifetime`) carry on.
    - This happens on entering and leaving split screen via Recents, and on every return to the app (Home, lock screen).
    - Resizing the split, or dragging the divider to full screen, only changes the surface's size and needs nothing.
  - **Tried and dropped: skipping the re-creation when the app came back from a stop at the same size.** It usually worked, but once, unlocking in split screen, the display presented to the dead window for 3 s (183 errors) until a later resize.
  - **Before this:** a runtime renderer switch, `RendererSwitch.cs` (Vulkan full screen, OpenGL ES in multi-window, rebuilding Avalonia's internal compositor by reflection). It has been removed, together with the earlier refresh code.
  - `renderer.txt` (`vulkan`, `egl` or `software`) in the app's external files folder still picks the renderer, for tests.
  - **Measured** (Pixel 9, Release profiling build, 60 Hz):
    - Scrolling, 15 s: full-screen Vulkan 52.8–53.8 fps while drawing with 10–14 % janky frames; OpenGL ES about 62 % janky; in a split pane OpenGL ES 33.7 %.
    - Live recording: 2 % janky with Vulkan, 9–10 % with OpenGL ES.
  - **Checked on the Pixel 9 (debug build):**
    - entering and leaving split screen with Discord, also via Recents;
    - dragging the divider and swapping panes;
    - Home and back;
    - rotation;
    - lock and unlock;
    - a re-creation 40 s into a recording: the timer and live graphs carried on and it is one take.
  - **Not checked:** the Pixel 4a, freeform or desktop windows, and picture-in-picture.
  - **Avalonia 12 (2026-10-15, emulator API 35, Debug):**
    - **`Recreate()` replaced by a new instance.** Under 12 every re-creation (Home → back, entering split screen) left white status and navigation bars and pushed the page down by the status-bar height. `Recreate()` relaunches into the *preserved* window (the same `DecorView`, by id), whose new content frame was laid out inside the bars (`0,136–1080,2337`) while Avalonia, with edge-to-edge enforced from Android 15, still padded for them. `SetDecorFitsSystemWindows(false)` in `OnCreate` didn't help, and `OnAttachedToWindow` doesn't even run for a preserved window. `OnSurfaceReplaced` now detaches the view (`Content = null`), starts a new `MainActivity` (`NoAnimation`), finishes this one and drops the close transition. That gives a new window, edge-to-edge, with one Activity in the task. `App`'s `MainViewFactory` also detaches the view from its old parent first, against a "MainView already has a visual parent" crash seen on the Pixel 9.
    - **Checked:** cold start (6.2–6.4 s to first frame, Debug), takes list, colour emoji, the mic permission prompt, fake-mic recording with live graphs, rotation mid-recording (no re-creation: config changes are handled), Home → back mid-recording (one take, saved), stop & analyze, entering split screen via Recents, resizing the split, dragging it back to full screen, Back then relaunch, and the share sheet (`voice-take-4.wav`).
    - **Is the watcher still needed? Not decidable on the emulator.** With `OnSurfaceReplaced` disabled, the emulator showed no "abandoned" errors and no black pane after Home → back or in split screen. But the 11.3 build (HEAD `9c638a0`) with the watcher disabled behaves the same there, so the emulator never had the bug (its Vulkan is `vulkan.ranchu`/SwiftShader). It stays until the same test passes on the Pixel 9 (Android 17).
    - **Cosmetic, not fixed:** a take dialog's dimmed backdrop stops under the status bar (it follows the safe area), so the bar stays light above it.
  - **Avalonia 12 vs 11.3 on the Pixel 9 (2026-10-15, Release profiling builds, fake mic, 60 Hz, medians of 4 runs (12) and 6 runs (11.3, HEAD `9c638a0`), alternated):**
    - Scrolling (10 flings down and back): 58.0 vs 53.4 fps, frames over 25 ms **1.0 % vs 8.1 %**.
    - Live recording, 30 s: frames over 25 ms 3.2 % vs 4.1 %, frames over 50 ms 24 vs 22 (fps over the window 39.2 vs 39.5; it counts idle gaps, as the app only redraws on change). One live update 4.2 vs 4.8 ms (median).
    - Stop: analysis 1.72 vs 1.73 s.
    - **Live update rate:** 12 ran only ~197 updates per take against ~237. Its 100 ms `DispatcherTimer` fires a little early, so `RecordViewModel`'s "at most one update per 100 ms" check skipped every such tick (gaps of 200–250 ms: ~60 per take against ~20). The check now allows 90 ms. Not yet re-measured on the phone with that change.
    - Found on the phone: the "already has a visual parent" crash after Home → back (fixed, see above), and that the profiling build's `ApplyExperiments` hook called `MainViewFactory` again, which (with the detach) emptied the window: black screen. It now uses the Activity's own `Content`.
    - How: `adb shell dumpsys SurfaceFlinger --latency` on the `SurfaceView[…](BLAST)` layer, sampled every 1.5 s; the probes from `adb logcat -s Timbratune`. On Android 17 shell can't read the app's 0600 files (take.wav) in `Android/data`, and `run-as` can't reach shared storage, so a phone backup over adb only gets the JSON files.
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
- **XA5300 "Android SDK directory could not be found"** with a valid SDK: a long-running MSBuild node had cached a failed lookup. `dotnet build-server shutdown` clears it. `build.bat` then published Android with `--disable-build-servers` (`2fa4420`). The deeper cause turned out to be the SDK existing only in Claude's redirected AppData (§8h).

## 8e. Save menu and the .tmbr export (2026-10-08)

The 💾 button on each "All recordings" card now opens a menu with three choices. It was first a `MenuFlyout`; since 2026-10-14 it is the take dialog, see below.

**Take dialogs (2026-10-14):** save / export (💾, with a cancel button), rename (click the take's name; there's no ✏️ button any more) and delete (🗑️) open one dialog over the whole page, with the same scrim and card as the metric modal. They used to be a flyout menu and inline rows in the card.
- **View:** `Views/TakeDialogView.axaml`, shown by `MainView` while `MainViewModel.TakeDialog` is set. Each card keeps its own state (`IsSaveMenuOpen`, `IsRenaming`, `IsConfirmingDelete`, together `IsDialogOpen`), and `MainViewModel.OnItemChanged` keeps one dialog open at a time.
- **Share on Android:** where the platform gives a share sheet (`AppServices.Sharer`, an `IFileSharer`; only Android sets one), the card shows a share button instead of 💾. There are two buttons and `CanSave` / `CanShare` pick one; the share sign is three linked dots in the accent colour, drawn in `RecordingCardView.axaml`, with no icon font or emoji.
  - The same dialog then reads "share", and its choices hand the WAV (`audio/wav`) or the `.tmbr` (`application/octet-stream`) to the share sheet instead of the save picker. `MainViewModel.ExportDataAsync(take, audio, share)` builds the `.tmbr` either way.
  - Android side, `AndroidFileSharer.cs`: the file is written to `cache/share/` and handed out as a `content://` URI by `ShareFileProvider` (an AndroidX `FileProvider`, declared by attribute so both manifests get it; paths in `Resources/xml/share_paths.xml`), with read permission granted through `ClipData`. Then `Intent.CreateChooser`.
  - Shared files are kept an hour (the receiving app may still be uploading), then deleted on the next share.
  - Checked on the Pixel 9: the sheet opens with `voice-take-N.tmbr` / `.wav`. Sending to an app wasn't tried.
- **Current Activity:** `MainActivity.s_current` is what the share sheet and the microphone request use. The services are built once per process, and they used to hold the first Activity, which can be re-created when its surface is replaced (§8d).
- **The name:** it is a `Button.label-link` (plain text, accent and underlined on hover), so clicking it renames without selecting the take. `RecordingCardView.OnCardClicked` skips buttons; the rest of the card still selects it.
- **Closing:** the background (scrim, or the scroller over it outside the card: `OnBackgroundPressed`), ✕, cancel, Esc, and Android's back button (`MainView.OnBackRequested`, which also closes the metric modal) close it as cancel.
- **Styles:** the save choices are `Button.choice` in `Styles.axaml`; "coming soon" is disabled and faded.
- **Rename focus:** the name box is focused with the name selected, which brings up a phone's keyboard. This is triggered by the take's `IsRenaming`, not by the box's visibility: with no dialog open the view has no DataContext, so the `IsVisible` binding falls back to true and never changes. Focus is retried every 50 ms (up to 10 times) until the box is laid out.
- **On-screen keyboard:** `TopLevel.InputPane` gives the scroller around the card a bottom margin, and the card scrolls when what's left is too short (landscape).
- **Checked on the Pixel 9 (debug build):**
  - save / export opens and Back closes it;
  - rename with focus, keyboard and Enter;
  - delete of a throwaway take;
  - export data opens the system picker.
- **File pickers** (`StorageProviderDialogs`):
  - The TopLevel is now looked up on every call. It was cached, which would break after the Activity is re-created (§8d).
  - On Android the extension is taken off the suggested name, because the picker adds it again (it showed `voice-take-15.tmbr.tmbr`).

The choices:
- **save audio (.wav):** the WAV copy as before (`RecordingItemViewModel.SaveCopyAsync`).
- **export data (.tmbr):** `RecordingItemViewModel.ExportDataAsync` → `MainViewModel.ExportDataAsync`. The card shows "preparing the export…", then "exported to <name>" or the error.
- **export PDF · coming soon:** disabled on purpose, in faint ink. Not implemented.

**File dialogs:** `IFileDialogs.SaveAsync(title, suggestedName, typeName, extension, mimeType, write)` is the general "save a new file" call. `SaveCopyAsync` is built on it. The `.tmbr` type uses MIME `application/octet-stream`, so Android's SAF picker shouldn't rename it to `.zip`. On the Pixel 9 the picker suggests `voice-take-<id>.tmbr` (after the double-extension fix); saving through it hasn't been tried yet.

**Per-frame lists are now kept with each take** (the user chose this over computing them on each export):
- The full analysis (`AcousticsAnalysisEngine.AnalyzeAsync`) builds `AnalysisResult.Series` (`Models/TakeSeries`) from its tracks with `Analysis/FrameSeriesBuilder`.
  - That is the same code the live graphs use: `LiveAnalyzer.BuildSeries` now calls it. Live output is unchanged (all live lines identical to `b5d73f4`).
  - Series: `pitch` (every 10 ms frame, null = unvoiced, equal to the saved contour), `loudness` (every intensity frame, 0.8/75 s), `hnr` (voiced 10 ms frames), `f1`/`f2`/`f3` (loud voiced frames with F1 in 250–1000 Hz, 5500 Hz ceiling), `weight` (corrected H1*–A3* on every measurable voiced frame, not the ≤ 250 subsample the metric uses), `jitter` (% per voiced stretch, at its end).
  - Each series is `{unit, step_s, description, t[], values[]}`, with explicit times, so an importer needs no grid maths.
- **Storage (superseded by §8f, now `series.json` in the take's folder):** `RecordingStore` writes them to `analysis/<id>.series.json`, apart from `<id>.json`, so loading the dashboard isn't slowed.
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

`TakeArchive.Read` / `Extract` return the `TakeExport` and refuse other zips and files from a newer exporter version; import uses them (§8f). Bump `exporter_version` when a field changes meaning or disappears.

**Tests:** `ExportTests` (4) cover the series grid and values, the store keeping and deleting them, the archive round trip (WAV byte-identical, metadata, lists equal) and refusing a foreign zip.

**Emoji:** the menu adds 🎵 📦 📄, so `scripts/make-emoji-font.py` was re-run (WSL, Ubuntu's Noto Color Emoji).

## 8f. One folder per take, folder link, import (2026-10-08)

The user asked to drop `recordings.json`, so takes can be added and removed by hand, the folder line to become a link, and import of audio and `.tmbr` files. Their choices: one folder per take, auto-import of files dropped into the folder, WAV + MP3 + FLAC + `.tmbr`, and on Android the USB-visible app folder.

**Layout** (`DataPaths`, `RecordingStore`):
- `takes/NNN label/` holds `take.wav`, `take.json`, `detail.json` and `series.json`.
- `take.json` is the `.tmbr` export's `TakeExport` shape without `detail`/`series` (they're in their own files): take info including `source_file`, audio format, analysis settings and metrics. It's small, so startup stays fast: listing 36 takes takes ~5 ms.
- **No index:** `Load()` lists `takes/*/take.json`.
  - A folder without `take.json` isn't a take, which is why `take.json` is written last.
  - An unreadable `take.json` is reported in the load error line (`Store.Unreadable`).
  - A duplicate or missing id (a folder copied from another device) gets the next free id, written back to its `take.json`.
- The folder name is cosmetic: id and safe label (≤ 40 chars) when created. The id is in `take.json`.
- `Recording.Audio` / `Detail` hold the folder-relative paths in memory, so `Paths.Resolve` works as before. `FolderOf(recording)` gives the folder, and `Delete` removes the whole folder (only ever directly inside `takes/`).

**Converting the old layout:** `recordings.json` + `audio/` + `analysis/` is converted take by take on the first `Load()`.
- Files are moved; ids are kept unless already used.
- `recordings.json` is then renamed `recordings.json.migrated`, and the empty `audio/` and `analysis/` folders are removed.
- Tried on a copy of the user's real data: 36/36 takes, labels, dates and metrics identical, 0.9 s once.
- **Note:** the published 0.2.0 build from before this change no longer sees takes after the conversion. If it records again it writes a new `recordings.json`, which the next start converts too (ids that clash get new ones).

**Import** (`Storage/TakeImporter.cs`):
- **Audio:** WAV is analyzed as is. MP3 / FLAC (or a WAV the decoder can't read) is first decoded to a 44.1 kHz mono PCM16 WAV by `SoundFlowAudio.DecodeToWav` (miniaudio, streamed; `IAudioDecoder` in Core, `AppServices.Decoder`). Label = file name, date = the file's last-write date, `source_file` kept.
  - A FLAC of a fixture gives exactly the WAV's metrics.
- **`.tmbr`:** unpacked as it is (`TakeArchive.Extract` → `RecordingStore.AddImported`), with a new id and no analysis.
- **Import button** ("📥 import" beside "All recordings"): `IFileDialogs.OpenFilesAsync` (multi-select; `PickedFile` with a stream, because Android gives no path). The file is copied to `<root>/.import/`, imported, then the copy is deleted. A status line under the header shows "importing 2 of 3 · name · 45%", then the result.
- **Dropped files:** `MainViewModel.StartAsync` (called by `App`) loads, cleans `.import/`, then imports audio and `.tmbr` files lying directly in `takes/`.
  - A WAV or `.tmbr` is then deleted (its contents are in the new folder).
  - An MP3 / FLAC original is moved into the take's folder as `source.mp3` / `source.flac`.
- CLI `--import` uses the same importer, so it now also takes mp3, flac and tmbr.

**Folder link:** the footer's "your takes live in …" is a `HyperlinkButton` opening `takes/` through Avalonia's `Launcher.LaunchDirectoryInfoAsync` (`IFileDialogs.OpenFolderAsync`). Checked on Windows: Explorer opens the folder.
- **Android:** `MainActivity.DataFolder()` now uses `GetExternalFilesDir(null)` (`Android/data/com.reyfen.timbratune/files`), which a PC sees over USB. Like private storage, it's removed on uninstall.
  - Takes in private storage are moved there once (`DataPaths.MoveContents`: file by file across volumes, a `.moving` marker for interrupted moves, never merging two folders that both have takes).
  - Android can't open that folder in a file manager, so `AppServices.DataFolderHint` replaces the link with a text saying where it is.
  - **Not yet tried on a phone.**

**Tests:** 119 pass.
- New store tests: folders, hand-copied and hand-removed folders, unreadable `take.json`, converting the old layout, never merging data folders.
- `ImportTests` (4): audio, `.tmbr` round trip without analysis, dropped files, no-decoder limits.

**Emoji:** 📥 added; the font was regenerated.

## 8g. Card buttons, rename, build.sh, phone layout fixes (2026-10-08)

- **`build.sh`** (Linux counterpart of `build.bat`, same `win` / `linux` / `android` options). Tested in WSL `Ubuntu-24.04` as `tester`, with the .NET 10 SDK installed to `~/.dotnet` (dotnet-install.sh, no sudo, `.bashrc` untouched), on a copy of the tree in `~/tt` so Linux builds don't touch the Windows `obj/` folders.
  - The `.exe` and the `.deb` / AppImage built there run: the AppImage on Ubuntu and the exe on Windows both gave the same import results as before.
  - Android isn't installed there; the script says which piece is missing.
  - **Both scripts check for the .NET 10 SDK first** and say so with the download link.
  - Through `wsl.exe … bash -c '…'`, `$?` must be written `\$?`, or it's expanded before the command reaches bash.
- **Take cards** (`RecordingCardView.axaml`):
  - "#N" is plain accent text (the pink number circle looked like a button; `Border.num` removed).
  - Delete and save are square framed `Button.icon` buttons (34 px, radius 8). The header row reads #N [name] date [🗑️ delete]. The ✏️ rename button was removed on 2026-10-14; the name itself renames (§8e).
  - The label wraps to two lines before it's cut short (phones).
- **Rename:** first a text box in the header; since 2026-10-14 a dialog (§8e). Enter saves, Escape cancels.
  - `RecordingStore.Rename` rewrites `take.json` and renames the folder to "NNN new label".
  - The folder keeps its old name while its audio is playing or if the move fails; the name in `take.json` is what counts.
  - `MainViewModel.RenameAsync` reloads after renaming.
- **Import** moved beside Record (`Button.outline`, a framed pill); its status line is now in the record panel and is cleared when a recording starts.
- **Text boxes** keep the app's input colour when hovered or focused (Fluent made them black in the dark theme).
- **Phone layout, checked on the `EuphoniaPixel` emulator** (Debug APK, fake mic, light and dark themes):
  - The "analyzing… 42% · about 4 s left" row ran off the right edge; it's now a `WrapPanel`.
  - The contour chart's "register floor" label was drawn under the pitch lines; it's now drawn last, on a card-coloured plate.
  - Nothing else was missing or misaligned in the take view, live view, trends, cards or footer.
  - Auto-import of files dropped into `Android/data/com.reyfen.timbratune/files/takes` works there.
- **Emoji:** ✏ ✓ added; the font was regenerated.
- **Tests:** 120 pass, including a new rename test.
- **Take title with date and time:** the "💗 #N · label · date" bubble under the hero is gone. The take's title reads "Latest take · <date time>" / "Take #N · <date time>" (`TakeViewModel.When`: `RecordedAt` in the device's "g" format, at 14 px in soft ink, like the bubble's date).
  - Takes now keep `recorded_at` (ISO 8601 with offset) in `take.json` / `Recording.RecordedAt`: set on Stop, on import (the file's time) and carried by `.tmbr` files.
  - Older takes use their `take.wav` time when it falls on the take's date (moving files keeps their time), otherwise only the date shows.
  - The empty label box says *untitled take* in italics.
- **Artifact names** lost "Desktop-": `Timbratune-v<ver>-win-x64.exe`, `Timbratune-v<ver>-linux-x64.deb` / `.AppImage.tar.gz`, `Timbratune-v<ver>-android.apk` (csproj target, `package-linux.sh`, both build scripts). Inside the .deb the binary is `/opt/timbratune/Timbratune`; the command is still `timbratune`. (The build scripts became one `build.ps1`, §8h.)

## 8h. One build script: build.ps1 (2026-10-08)

- **`build.ps1` replaces `build.bat` and `build.sh`.** One script for Windows PowerShell 5.1 and PowerShell 7, the latter on Linux too.
  - It keeps both earlier scripts' behaviour: platform arguments, the .NET 10 SDK check, the running-app check (Windows), Linux packing in WSL or directly, and Android SDK/JDK lookup.
  - Keep it ASCII-only: Windows PowerShell reads a BOM-less script as ANSI.
  - **Progress (2026-10-14):** dotnet runs through `Invoke-Dotnet`, which prints "still building, mm:ss" every 15 s and, for Android, how many assemblies the LLVM AOT has finished per ABI. A Release Android publish is ~3m45s of which ~3m10 is single-threaded `mono-aot-cross --llvm` for arm64 (x64 ~40 s, in parallel), with no output at `-v quiet`.
  - **`debug` option (2026-10-14):** `./build.ps1 android debug` (or just `debug`) publishes `-c Debug -p:EmbedAssembliesIntoApk=true` into `publish/Timbratune-android-debug/`: ~1m15s, 69 MB APK, no LLVM (the csproj enables it for Release only), so slower at runtime. For the agent's UI and smoke tests; use Release for the user's installs and any measurement. `win`/`linux` listed with it still build Release.
  - Started from Explorer (double-click with PowerShell as the .ps1 handler, which the user set, or "Run with PowerShell") it waits for Enter at the end. That's detected by the parent process being `explorer`; matching the command line missed double-clicks.
- **XA5300 prevention**, before the Android build, after the user hit it twice in a day:
  - `dotnet build-server shutdown`;
  - `ANDROID_HOME` / `ANDROID_SDK_ROOT` / `AndroidSdkDirectory` / `JavaSdkDirectory` / `JAVA_HOME` set in the environment for every MSBuild process;
  - the `-p:` properties;
  - `--disable-build-servers`.
- **The final list shows only what this run built:** the expected files (`Timbratune-v<Version>-…`) of the platforms that succeeded. A file-time filter missed up-to-date builds, e.g. an unchanged APK.
- **Android SDK lookup:** an SDK counts if it has `platform-tools`, `build-tools` or `platforms`. When none is found, every place is printed with the reason ("not set", "doesn't exist", not an SDK, access error); the JDK the same way. The search order is below.
  - **Cause of "no Android SDK" / XA5300 on the user's builds (2026-10-09):** the SDK and JDK installed on 2026-10-01 were installed from Claude's shell. The Claude desktop app is an MSIX package, so they went to `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Local\Android\{Sdk,jdk}` (4.9 + 0.3 GB). Claude's shells see them at `%LOCALAPPDATA%\Android`; Explorer and the user's own builds don't.
    - The intermittent XA5300 was MSBuild nodes started on one side being reused by the other.
    - The user chose not to move them. Instead `build.ps1` looks where real installs go and reports each place:
      - `ANDROID_SDK` / `ANDROID_HOME` / `ANDROID_SDK_ROOT`;
      - `%LOCALAPPDATA%\Android\Sdk` (Android Studio, README);
      - `Program Files (x86)\Android\android-sdk` (Visual Studio), `Program Files\Android\android-sdk`, `C:\Android\Sdk`;
      - Linux: `~/Android/Sdk`, `~/Library/Android/sdk`, `/usr/lib/android-sdk`, `/opt/android-sdk`.
    - JDK: `ANDROID_JDK`, `JAVA_HOME`, `%LOCALAPPDATA%\Android\jdk`, Android Studio's `jbr`, `Program Files\Microsoft\jdk-*`, `Eclipse Adoptium\jdk-*`, `/usr/lib/jvm/*`. Each is checked for `javac` and a Java version of 17-21 (from its `release` file).
    - The user's `JAVA_HOME` (Microsoft OpenJDK 17.0.6) qualifies, so on the user's side only the SDK is missing. They can install it with Android Studio or the README command run from their own terminal.
- **Tested:**
  - Windows PowerShell 5.1, all three platforms, 262 s;
  - WSL Ubuntu with PowerShell 7.6.6 in `~/.powershell` (tester, no sudo): win + linux built, android stopped with the workload message;
  - an unknown platform and a missing `dotnet` both give a message and exit 1.
- **The take title's date and time** now has a dot between them: "08-Oct-26 · 14:03" (the device's short date and short time).

## 8i. Refactoring P0: the safety net (2026-10-15)

Branch `dev/v0.3.0`, phase P0 of `REFACTOR-PLAN.md` §9. No app code changed; this adds what the later phases are checked against. The base for every comparison is `4da741a` (the analysis code is unchanged since `07db82d`).

- **Golden snapshots** (`tests/Reyfen.Timbratune.Core.Tests/GoldenTests.cs`, files in `Golden/`). For each VCTK fixture:
  - the saved analysis as the store writes it: `<clip>.take.json` (the `Recording`), `.detail.json`, `.series.json`;
  - the final live snapshot (`.live.json`: metrics, detail and the six live series), recorded the way the app does: irregular chunks, an update per 100 ms of audio, a full update every 10th, then the final update on the aligned length.
  - Saved values must be identical; live values may move by at most 1e-7. Keys, their order and array lengths must match exactly.
  - `TIMBRATUNE_UPDATE_GOLDEN=1` rewrites the files. Do that only on purpose, and say why in the commit.
  - The files were made on Windows x64. Another OS's `libm` could change last digits; if the WSL run in P9 shows that, loosen the saved comparison for that case rather than regenerating.
  - **Linux (2026-10-16): one live series has its own tolerance, `$.series.weight.v` at 1e-4 dB** (`LiveWeightTolerance`). WSL Ubuntu 24.04 failed `vctk_m311.live.json` at `weight.v[0]`, 12.7885677067799 expected, 12.788565828856093 got (1.9e-6), also on `c6a3160`, so not a refactoring regression. Measured:
    - The cause is `libm`. Over 200,000 arguments, Windows' CRT and glibc give results 1 ulp apart for 3.1 % of `Math.Sin`, 3.1 % of `Cos`, 3.6 % of `Log10`, 0.5 % of `Exp` and 0.2 % of `Pow(x, 2)` calls (never more than 1 ulp). The decoded WAV and every other input are identical.
    - Saved results are identical on Linux (all `take`/`detail`/`series.json` files), because the saved series are rounded. So are the live metrics and detail. The unrounded live series differ only in the last bits: loudness ≤ 3e-16 relative, HNR ≤ 8e-13, F2/F3 ≤ 9e-14, jitter ≤ 4e-13.
    - Weight is amplified because it is the one live output that uses F0 unrounded. F0 comes from Brent's maximization of the sinc-interpolated autocorrelation (`PeakRefinement.SincMaximum`), which stops within about √ε·x of the peak. A 1-ulp change in the interpolant can move it inside that tolerance. Of 1,124 weight frames on the four clips, F0 is identical on 124, ≤ 1.2e-10 relative on 999, and 3.8e-8 on one (m311 at 0.83 s; formants, bandwidths, H1 and A3 match to ≤ 4e-13). The Iseli–Alwan corrections give |dW / d ln F0| up to 630 dB on these clips (median 50–90), so that frame's weight moves 1.9e-6 dB. Every other weight value is within 2.1e-8.
    - The bound is 630 dB × 4e-8 ≈ 2.5e-5 dB, so the tolerance is 1e-4 dB, which is far below the 0.01 dB a saved take stores. Everything else keeps 1e-7 (live) and 0 (saved), including `weight.t`.
    - I didn't make the computation platform-stable. That would need our own `Sin`/`Cos`/`Log10`/`Exp`/`Pow` throughout pitch, FFT, windows and formants (the sinc interpolant is a hot loop). It would also change saved results that Praat oracle tests check, and it would buy bit-identity for a value shown to 0.1 dB. Tightening Brent doesn't help: below √ε·x the interpolant is flat to the last bit.
    - If a later platform (Android's bionic, ARM64, macOS) breaks a saved file, it would most likely be a 0.01-dB weight or 0.1-Hz F0 rounding flip near a half step. Check that before regenerating.
- **Architecture tests** (`tests/Reyfen.Timbratune.Architecture.Tests`, in the solution). A small IL scanner (`TypeReferences.cs`, reflection only) collects every type a type uses, from its signatures and method bodies; lambdas and async methods are covered through their nested compiler-generated types. Rules:
  - Acoustics references only the .NET runtime; Core only Acoustics and the runtime.
  - Acoustics, Core and the UI library reference nothing native-only (SoundFlow, Avalonia.Desktop/Android/Native/Win32/X11, Mono.Android, the `Audio.*` adapters, the heads).
  - Under `Reyfen.Timbratune.Features.<X>`: no references to another feature. Outside `Features`, only `Reyfen.Timbratune.App` and `.Shell` may use features. Feature `*Manager`s never use Avalonia. Feature `*ViewModel`s never use `RecordingStore`, `File`, `Directory`, `FileInfo`, `DirectoryInfo` or `FileStream`.
  - No `$parent[…]…DataContext` bindings in `src/Reyfen.Timbratune/Features/**/*.axaml`.
  - Live analysis (Acoustics, Core, `Reyfen.Timbratune.Analysis`, `Features.LiveMonitor`, types named `Live*`) never uses analytics.
  - The feature rules have nothing to check until P3 adds `Features/`. Positive controls (the scanner sees `VoiceReport` inside `LiveAnalyzer.Update`, and `RecordingStore` in `MainViewModel`'s async methods) keep them from passing by seeing nothing.
  - P1 must rename the `Features` class (`Features.cs`) and move `App` out of the namespace `Reyfen.Timbratune` before folders with those names exist; the tests refer to `Reyfen.Timbratune.App` by type.
- **`tools/analysis-bench`** (dev tool, not in the solution; Stopwatch and `GC.GetTotalAllocatedBytes(precise)`, no package). Without a WAV it builds the 27.6 s `long.wav` of `make-long-wav.ps1` from the fixtures. It measures the full analysis (`AnalyzeAsync`, all cores), then replays the take through `LiveAnalyzer` the way the app does: 10 ms chunks, an update every 100 ms (a full one every 10th), on one thread limited to 2 cores like `LiveWorker`, plus `LiveTimelinesViewModel.Compute` on each snapshot. It also prints SHA-256 hashes of the saved and live results. `-types` lists live allocations by type (an `AllocationTick` listener); `-out` writes the numbers; `-baseline` checks the gate.
  - **Allocations aren't exactly repeatable:** `ArrayPool.Shared` keeps per-thread caches, so a run's allocations depend on which pool threads the loops land on. The full analysis varies 109–185 MB between runs, and its median by about 4 % between sessions. Live allocation is steady (median 8.857–8.858 MB/s in three sessions). The gate therefore allows +5 % on the full analysis' allocations and +1 % on the live ones. Time medians also move by about ±4 % between sessions on this machine, so re-run once before calling a miss.
  - Run: `cd tools/analysis-bench; dotnet run -c Release -- -baseline baseline-p0.json`.
  - **A stored baseline drifts; gate with an A/B in one session.** At P1 (no analysis code changed) the same `86944bb` code measured 168 MB full-analysis allocation and 8.93–9.01 MB/s live, against 154 MB and 8.86 MB/s for the P0 baseline a few hours earlier, and `live.compute` 295–299 ms against 292 ms. Comparing with `baseline-p0.json` then shows false misses. For the gate: check out the base commit in a temporary worktree (`git worktree add --detach <tmp> <base>`), build its bench in Release, and run old and new alternately, at least twice each, with `-out`; compare those medians. `baseline-p0.json` stays as a rough reference only.

**Baselines** (this machine: 24 cores, .NET 10.0.11, x64, Release; 27.6 s `long.wav`; medians of 10 runs; stored in `tools/analysis-bench/baseline-p0.json`):

| Measure | Baseline |
|---|---|
| Full analysis | 356 ms; 154 MB allocated |
| Live update, quick / full (per tick) | 9.6 / 9.7 ms |
| Live updates, whole take (276 ticks + final) | 2,814 ms |
| Live allocation (updates) | 8.86 MB per second of audio |
| `LiveTimelinesViewModel.Compute`, whole take | 292 ms; 1.96 MB/s |
| Result hashes | saved `504d74118fb5a222`, live `3d166b64c72ee99b` |

Top live allocations by type (sampled): `double[]` 186 MB, `TimedValue[]` 36 MB, `(double, double)[]` 23 MB, `double?[]` 22 MB, `PitchCandidate[]` 17 MB per replay.

**`tools/live-steadiness`** on `long.wav` (% of each graph's range: dot step / tail change / dot error, app settings): pitch 8.31 / 0.62 / 8.98, loudness 6.65 / 1.99 / 16.48, HNR 3.83 / 1.53 / 9.54, F2 4.54 / 2.12 / 46.44, F3 0.73 / 0.35 / 4.26, weight 1.51 / 0.72 / 10.70 (baseline variant in §5.6). The first P0 run gave loudness 24.44 and about 0 for HNR, F2, F3 and weight because the tool was broken, not the app: it kept every tick's snapshot and computed the lines after the last update, but since `b5d73f4` `LiveAnalyzer` refills two sets of series lists in turn, so all stored snapshots showed the last two updates' data. Pitch was unaffected (its frames are new on every update). The tool now computes each tick's lines right after its update, as the app does, and checks its "app" lines against `LiveTimelinesViewModel.Compute` on every tick (it prints a warning if they differ). It reproduces §5.6's numbers exactly when the formant gate is set back to the pre-§6a one. Checked tick by tick against the original tool on `82c5621` (the commit before the double buffering): with the old gate, the fixed tool on today's code and on `b5d73f4` gives the same 2,030 dots, bit-identical in time and value, in both variants.

**Phone baselines** (2026-10-16), for both of the owner's phones:
- **Setup:** Release profiling builds (LLVM AOT) of `a835972` (P0: the app before the refactoring) and `7810f0c` (after P4), installed in turn over the phone's own build (same signing key, versionCode 3): the 4a (Android 13) had 0.2.0, the Pixel 9 (Android 17, 60 Hz) a 0.2.1 debug build. Each run: a cold start with an empty takes list, a 28 s fake-mic recording of `long.wav`, then Stop. Probes on, `SurfaceFlinger --latency` sampled every 1.5 s while recording. Six runs per build, alternating; scripts `phone-run.sh` / `phone-analyze.py` in the session scratchpad (the method of §8d).
- **Results** (medians of 6; janky = frame intervals over 25 ms):

| Phone, build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|
| 4a, P0 `a835972` (**baseline**) | 52.4 | 9.0 (6.8–10.8) | 17 | 4.58 s (3.88 s) | 222 | 46.6 / 59.2 ms |
| 4a, P4 `7810f0c` | 52.2 | 9.9 (9.2–11.1) | 18 | 4.52 s (3.80 s) | 234 | 44.3 / 59.0 ms |
| Pixel 9, P0 `a835972` (**baseline**) | 39.5 | 3.3 (2.7–4.0) | 20.5 | 2.02 s (1.62 s) | 261 | 42.0 / 49.6 ms |
| Pixel 9, P4 `7810f0c` | 39.4 | 3.8 (3.3–4.2) | 21.5 | 2.01 s (1.67 s) | 262.5 | 42.7 / 49.3 ms |

- **Reading:**
  - The difference after P1–P4 is inside the baselines' run-to-run spread on both phones. Janky % is a little higher on both (+0.9 on the 4a, +0.6 on the Pixel 9), not significant with 6 runs each (Mann–Whitney p ≈ 0.2 on the Pixel 9); worth watching in P5–P7.
  - **Pixel 9 fps is "over the window"** (about 39): the app only redraws on change, so idle gaps count, and every run has 16–17 gaps over 100 ms. The janky share includes them. The plan's earlier gate (≥ 57.5 fps, ≤ 3.1 %) was fps while drawing, measured under Avalonia 11.3 (§8d); the gate is now this baseline, as an A/B.
  - On the Pixel 9 both builds run about the same number of live updates (261 vs 262); the 4a's P0 build skipped more early ticks.
  - P3's loop keeps closer to one update per 100 ms (234 against 222 per take: P0's `DispatcherTimer` skipped early ticks, §8d), so a little more work per take, and has fewer UI-thread stalls (25 against 30.5 `ui.stall` > 32 ms), as the smoothing moved off the UI thread.
  - The slow frames are mostly single missed vsyncs (2-frame gaps), spread over the whole take.
  - For P5–P7, compare against the P0 rows with an A/B like this one, not against the numbers alone: single runs range by up to 4 points of janky % (4a) and 1.3 points (Pixel 9).
  - **Android 17's layer list** reads `RequestedLayerState{<id> SurfaceView[…](BLAST)#n parentId=…}`; `--latency` needs the name with the leading id (`<id> SurfaceView[…](BLAST)#n`), and the newest such layer is the current launch's.
  - **Run one driver at a time.** A first Pixel 9 attempt left earlier background drivers alive when the session ended; two of them drove the phone at once and their files mixed (two runs with the same logcat). Those runs were discarded and all twelve redone.
- **The user's 0.2.0 takes** are in the app's private storage on this phone. A 0.3.0 build moves them to shared storage on its first normal start (`AndroidApp.DataFolder`). The profiling build used to do that before redirecting a fake-mic session to `profiling-data`; it now skips the lookup when the fake mic is there (`AndroidApp.OnCreate`, profiling builds only), and the same patch was applied to the P0 build measured. The 4a was left on the `7810f0c` profiling build with the fake mic and test takes removed, so its next normal start does the 0.3.0 move. The Pixel 9's takes were already in shared storage in the current layout (20 takes, untouched); it was also left on the `7810f0c` profiling build, cleaned up the same way.

**Tests:** 148 pass with Praat (17 architecture, 57 acoustics, 74 core).

## 8j. Refactoring P1: dependency injection and shared ports (2026-10-15)

Phase P1 of `REFACTOR-PLAN.md`. No user-visible change.

- **Composition** (`src/Reyfen.Timbratune/App/`):
  - `CompositionRoot.AddTimbratune()` registers Core (`IAnalysisEngine`, `RecordingStore`, `TakeImporter`), Shared (`IMessenger` = a `WeakReferenceMessenger`, `IOverlayHost`, `IFeatureFlags`, defaults for `IPermissions`, `IThreadPriority` and `ReferenceVoiceFolder`, `StorageProviderDialogs` as `IFileDialogs`, `PlaybackService`) and the UI (`ViewLocator`, `MainViewModel`). All singletons, constructor injection, `TryAdd` so a head's own registration wins.
  - A head must add `DataPaths`, `IAudioRecorder`, `IAudioPlayer` and `IPlatformInfo`; it may add `IAudioDecoder`, `IFileSharer` (an optional constructor parameter of `MainViewModel`) and its own `IPermissions`/`IThreadPriority`/`ReferenceVoiceFolder`. `CompositionTests` checks both cases with `ValidateOnBuild`.
  - `App` is now `App/TimbratuneApp` (namespace `Reyfen.Timbratune.App`; a class can't share that name). It takes the container in its constructor; the parameterless one is for the XAML previewer. Services are resolved in `OnFrameworkInitializationCompleted`, after the UI thread exists (`PlaybackService` owns a `DispatcherTimer`), so nothing is created earlier than before.
  - `ViewLocator`: a hand-written type → view map, added to the app's data templates; it also builds the root view on Android.
  - Removed: `App.ServicesFactory`, the `AppServices` record, `Features.cs`.
- **Heads:**
  - **Desktop** (`Program.cs`): one `ServiceProvider` (validated on build) for the window and for `--import`, which now resolves `RecordingStore` and `TakeImporter` from it. It is disposed on exit, newest first: player and recorder before `SoundFlowAudio`, as the old `using`s did.
  - **Android** (`AndroidApp`): the container is built in `OnCreate`, and `CreateAppBuilder` (a protected virtual of `AvaloniaAndroidApplication<T>`, checked in its metadata: it was `Configure<TApp>().UseAndroid()`) is overridden to construct `TimbratuneApp` with it. The old `_audio`/`_recorder` fields are singletons now. `AndroidPlatform.cs` holds `AndroidPermissions` (through `MainActivity.Current`) and `AndroidThreadPriority`. `MainActivity.Current` stays.
- **`Features.cs` split:** `Shared/AppInfo` (version, credits, links, `CompareHint(bool)`), `Shared/FeatureFlags` (`IFeatureFlags`, `BuildFeatureFlags` with the build-time `ReferenceVoicesBuilt`), `Shared/Platform/IPlatformInfo` + the `PlatformInfo` record (`IsMobile`, `IsBrowser`, `SupportsThreads`, `CanOpenDataFolder`, `DataFolderHint`, `CanShare`, `CanRunProcesses`). The view models that `MainViewModel` still creates get the flag passed in (`TakeViewModel` and `RecordViewModel` take `IFeatureFlags`, `MetricModalViewModel` a bool); `TakeView` binds `CompareHint` instead of `x:Static`. `RecordViewModel` takes `IPermissions` and `IThreadPriority` instead of two optional delegates; the desktop defaults ask nothing and leave the priority alone, as the null delegates did.
- **Shared ports:** `Platform/` (`IFileDialogs`, `PickedFile`, `IFileSharer`, `StorageProviderDialogs` moved from `Services/`; new `IPlatformInfo`, `IPermissions`, `IThreadPriority`, `ReferenceVoiceFolder`), `Navigation/` (`IOverlayHost` + `OverlayHost`, a stack with move-to-front, and `RouterBase`, which disposes what it closes), `Messaging/Messages.cs` (`TakeSaved`, `TakeRenamed`, `TakeDeleted`, `ImportFinished`, `TakeAnalysisUpdated`; ids and values only). Navigation and messages are first used in P3.
- **Not done in P1, on purpose** (noted in the plan):
  - `Shared/State` (`IActiveTakeService`, `ITakeCatalog`, `ILiveSession`) comes with the P3 steps that move that state out of `MainViewModel` and `RecordViewModel`, so the interfaces are shaped by their implementations. `ISettings` comes in P2.
  - `ISecureStore`, `IExternalBrowser`, `IProcessRunner`, `IHttpDownloader` come with their first consumer (P4, later).
  - `StorageProviderDialogs` still checks `OperatingSystem.IsAndroid()` for the Android picker's extension quirk; it's adapter code, so it stays.
- **New test project** `tests/Reyfen.Timbratune.Tests` (UI library without a UI): `CompositionTests` (5) and `OverlayHostTests` (5). **Tests:** 158 pass with Praat (10 UI, 17 architecture, 57 acoustics, 74 core).
- **Checked:**
  - Desktop (Debug, fake mic, throwaway data folder): start, the `--import` CLI (same values as the golden file), record with live graphs, stop & analyze, the metric modal.
  - Android (Debug APK on the `EuphoniaPixel` emulator, fake mic): start, recording with live graphs (permission and thread priority through DI), stop & analyze (take saved and shown).
  - The Android profiling configuration (`-p:TimbratuneProfiling=true`) compiles.
  - The bench: hashes identical, and an A/B against `86944bb` in the same session shows no difference (see the §8i note on drift).

## 8k. Refactoring P2: storage, settings and scheduling seams (2026-10-15)

Phase P2 of `REFACTOR-PLAN.md`. The only user-visible change: the theme toggle and the live-window choice are now remembered between sessions.

- **`IAnalysisScheduler`** (`Core/Analysis/IAnalysisScheduler.cs`): where the analyses run. `NativeAnalysisScheduler(liveCores, lowerThreadPriority)` does exactly what the app did before:
  - live updates on one `LiveWorker` (moved from the UI project into Core), made on first use, at the platform's lower priority, with loops limited to `LiveCores` (2); with 0 cores, on the thread pool;
  - saved analyses on the thread pool.
  - The profiling switches `fullcpu` and `lowprioN` now pick the cores in `CompositionRoot.LiveCores()`. `RecordViewModel` asks the scheduler instead of keeping its own worker, and skips the 90 ms throttle when `LiveCores` is 0, as before. `AcousticsAnalysisEngine` takes an optional scheduler; the parameterless constructor (tests, tools, the CLI) uses the thread pool.
- **Stream entry points:**
  - `AcousticsAnalysisEngine.AnalyzeAsync(Stream …)` and `AnalyzeAsync(Sound …)` next to the path one; all three run the same private `Analyze`, and `SeamTests` checks that they give identical JSON.
  - `IAnalysisEngine` has a default `AnalyzeAsync(Stream …)` that copies to a temporary file, for path-only engines such as the Praat reference.
  - `WavWriter(Stream, …, leaveOpen)` and `WavWriter.Truncate(Stream, n)` write the same bytes as the file versions (tested).
  - The recorders and the Stop flow still use paths (see the plan's note).
- **Storage ports** (`Core/Storage/ITakeStorage.cs`):
  - `ITakeStorage`: named takes holding named files. It lists, creates (with "(2)" suffixes), renames, deletes, reads and writes atomically, copies or moves a local file in, gives a file's write time, and a local path where there is one. The file-system exceptions are the contract: locked means `IOException`.
  - `IDataStore`: the app's own files (settings, later tools), with atomic writes.
  - `FileSystemTakeStorage` and `FileSystemDataStore` implement them; names must be one path segment, so nothing can leave the folder.
  - `RecordingStore` is now the repository over `ITakeStorage`. Its public API and behaviour are unchanged (`RecordingStore(DataPaths)` still works; `RecordingStore(ITakeStorage, DataPaths)` is new), and the store tests pass unmodified.
  - A take is found from its paths (`takes/<take>/<file>`, anything else is refused). New `MoveIntoTake` is used for an import's original MP3/FLAC.
  - `TakeArchive.Describe` can read the WAV format from a stream opener.
  - **Byte check:** the old (`e110baf`) and new `--import` of the same two fixtures wrote identical `take.wav`, `detail.json` and `series.json`; `take.json` differs only in `exported_at`.
- **Settings** (`Core/Storage/Settings/`, REFACTOR-PLAN §5a):
  - `SettingKey<T>(Id, Default, Scope, JsonTypeInfo<T>)` and `SettingScope { Device, Synced }`; `SettingsJson` is the source-generated context for the plain value types. `ISettings` has `Get`, `Set` and `Changed`.
  - `SettingsService` loads both documents once (`{ "schemaVersion": 1, "values": {…} }`, one per scope), keeps values in memory, and saves 0.5 s after the last change on a timer. It also saves on dispose: the container is disposed when the desktop app exits; Android relies on the timer.
  - Unknown keys are written back unchanged. A damaged file becomes `.bak`, and the app starts from the defaults. A value of another shape reads as the default. Migrations run on load, and a newer app's document keeps its version.
  - Ids that look like secrets (token, secret, password, apikey) and duplicate ids are refused.
  - `DataStoreSettingsStore` writes indented JSON through `IDataStore`.
  - **Where:** desktop, the data folder's root (`settings.device.json` next to `takes/`, so `TIMBRATUNE_DATA_DIR` moves it too); Android, the app's private `FilesDir` (not the USB-visible folder with the takes).
  - **The owner's decision (2026-10-15): every setting is `Device` for now**; which ones sync is decided with accounts. `CompositionTests` checks that every registered key is `Device`.
  - **Keys today** (in `ViewModels/AppSettings.cs` until the features exist):
    - `shell.theme`: "system", "light" or "dark"; applied in `TimbratuneApp` before the window is made, set by the toggle.
    - `recording.liveWindowSeconds`: 0 means the whole take; an unknown value means the whole take.
  - Registered with `services.AddSetting(key)`. An architecture rule says features use `ISettings` only, never the settings store or `IDataStore`.
- **Tests:** 184 pass with Praat (14 UI, 18 architecture, 57 acoustics, 95 core). New: `SeamTests` (11), `SettingsTests` (10), 4 composition tests and 1 architecture rule.
- **Checked:**
  - **A/B bench** against `e110baf` in one session, alternating, 2 runs each: same numbers within noise, identical saved and live hashes.
  - **Desktop:** theme toggle → `settings.device.json` → restart in light; a stored 30 s window comes back as "last 30 s"; record (fake mic) and stop & analyze.
  - **Android emulator (Debug):** the theme kept across a force-stop, the file in `files/`, record and stop & analyze (take saved).
- **A slip during the checks:** one restarted desktop instance lacked `TIMBRATUNE_FAKE_MIC` (each tool call is a new shell), so Record captured ~26 s of the real microphone. It was killed before Stop, nothing was analyzed or saved, and the temporary WAV was deleted. CLAUDE.md now says to set the variable in the same command as every launch.

## 8l. Refactoring P3: UI feature modules (2026-10-15)

Phase P3 of `REFACTOR-PLAN.md`. No user-visible change except the two small ones listed at the end.

**Layout** (`src/Reyfen.Timbratune`):
- `Features/<Name>/`: each has its View(s), ViewModel(s), a Manager where there are use cases, a Router where it opens overlays, and `Add<Name>Feature()`:
  - `CheatSheet`: the view only.
  - `About`: the footer (takes folder, credits).
  - `TakeActions`: the take dialog (`TakeDialogViewModel`), `TakeActionsManager` (save or share audio, .tmbr export with the series backfill, rename, delete), `TakeActionsRouter`.
  - `MetricCompare`: `MetricModalViewModel`, `MetricCompareManager` (reference voices, audio paths), `MetricCompareRouter`.
  - `Import`: `ImportViewModel` (button) with `ImportStatusViewModel` (line), and `ImportManager` (picked and dropped files, the trend backfill).
  - `TakeLibrary`: switcher and cards, `TakeCardViewModel`, `TakeLibraryManager` (audio path, peaks).
  - `TakeDetails`: `TakeDetailsViewModel` (the take in focus and the live take), `TakeViewModel`, `TakeDetailsManager`.
  - `Record`: `RecordViewModel` (panel), `RecordingManager`, which is the `ILiveSession`.
  - `LiveMonitor`: `LiveMonitorViewModel`, `LiveTimelinesViewModel`, `RecentWindow`, the window picker.
- **`Shell/`:** `ShellViewModel` and `ShellView` (were `MainViewModel` and `MainView`) only place the features. The Shell fills the record panel's slots (`IdleActions` = Import button, `WindowPicker` = LiveMonitor, `Footer` = import status), holds the overlay stack, the theme toggle and start-up. `MainWindow` hosts `ShellView`.
- **`Shared/`:**
  - `State/`: `ITakeCatalog`/`TakeCatalog` (the takes; reloaded by whichever manager changed them), `IActiveTakeService`/`ActiveTakeService` (picked take, or the newest; a vanished pick falls back), `ILiveSession`.
  - `Navigation/`: `IOverlayHost`/`OverlayHost` (now disposes what it closes, so Esc, back and routers all clean up), `RouterBase`, `ITakeActionsLauncher`, `IMetricCompareLauncher`.
  - `Messaging/`: new `TakeActionStatus` (a card's export or rename status), `TakeTrendsOutdated`, `RecordingStarted`.
  - `ThrottledProgress`.
- **Deleted:** `ViewModels/`, `Views/` and `Analysis/`. `LiveWorker` moved to Core in P2. The bridge-gap constant moved to `ContourChart` (a shared control mustn't read a feature).
- **Namespace clash:** the Recording feature is `Features/Record`: a namespace `Features.Recording` hides the `Recording` model type in every sibling feature (C# resolves enclosing namespaces before `using`s).

**How the old couplings were cut:**
- **Cross-feature openers:** the `$parent[v:MainView]` bindings (import button and status, switcher chips) and the card's ancestor lookup became slots, the card's own `SelectCommand`, and launchers. A card opens its dialog through `ITakeActionsLauncher`; a stat card opens the comparison through `IMetricCompareLauncher`.
- **Status and backfill through messages:** a card's export and rename status arrives as `TakeActionStatus`. TakeDetails asks for a trend backfill with `TakeTrendsOutdated`, and Import answers with `TakeAnalysisUpdated`.
- **Overlays:** `Modal`, `TakeDialog` and `CloseModalCommand` became one overlay stack shown through the `ViewLocator`. Esc and Android's back close its front. One dialog of a kind at a time; reloading the takes closes a take dialog, as before.

**The live path, now in `RecordingManager`** (it was `RecordViewModel`'s timer):
- **Loop:** one update starts at most per 100 ms tick on `IAnalysisScheduler.RunLiveAsync`. After a slow update the next waits for the next tick, as the `DispatcherTimer` did. The loop runs off the UI thread (`ConfigureAwait(false)`) instead of hopping through it each tick.
- **Full updates:** about once a second, and every tick until the first result, as before.
- **Snapshots:** `SnapshotReady` is raised inside the live work item, on the live thread. LiveMonitor smooths there (`live.compute`) and posts only `Apply` (`live.apply`); TakeDetails posts the live take's update (`live.takeview`). The final snapshot is published on a pool thread, inside the final update's work item, so its smoothing is now off the UI thread as well.
- **Profiling switches:** `liveslow`, `slowcharts` and `nolivetake` keep working, in the manager, LiveMonitor and TakeDetails respectively.
- **Stop:** trim, final update, analyze and save happen in the manager. The panel goes idle, then `FinishAsync` reloads the takes, selects the new one, sends `TakeSaved` and ends the live views, in the old order.

**Tests:** 189 pass with Praat (19 UI, 18 architecture, 57 acoustics, 95 core).
- `RecordingManagerTests` replays `vctk_f294` in real time through the real manager (about 10 s): 99–100 snapshots for 9.8 s, about 11 with a result, live ticks on the "Live analysis" thread and the final one on a pool thread, monotonic progress. The take is saved, announced and selected, and the final live contour equals the saved one frame for frame.
- `TakeStateTests` covers the catalog and selection.
- The architecture rules now have features to check; they skip Avalonia's generated `CompiledAvaloniaXaml` loaders, which reference every view.

**Checked:**
- **A/B bench** against `859e98f`, alternating, 2 runs each: same within noise, identical hashes. The bench drives `LiveAnalyzer` directly, so the new loop's real-world cost is for the phone gates (P9).
- **Desktop** (fake mic, test data folder):
  - the metric comparison through the overlay stack, closed with ✕;
  - a card's delete dialog: cancel, then a real delete; the list reloads and the selection is kept;
  - selecting a card;
  - recording the whole 27.6 s `long.wav` with live graphs and the live take's cards, Stop with the progress bar over the frozen live view, the new take selected and shown.
- **Android emulator (Debug):** start, the comparison opened by tapping a stat card and closed with the back key (the app stays), record with live graphs, Stop, take saved.

**User-visible differences** (small, on purpose):
- "couldn't open the folder here · it's …" now shows under the folder link in the footer, not in the import line (that line belongs to Import now).
- The live-window choice is stored as `liveMonitor.windowSeconds`. The unreleased P2 id `recording.liveWindowSeconds` is not read; it stays in the file as an unknown key.

## 8m. Refactoring P4: import formats (2026-10-16)

Phase P4 of `REFACTOR-PLAN.md` (§4): import reads almost any audio format, on every platform, and the picker offers only what the device can read.

**Decoding layer** (`Core/Audio/Decoding`):
- `AudioFormatCatalog`: the known formats (wav, aiff, mp3, flac, ogg, opus, aac, m4a, mp4, 3gp, amr, caf, webm, mkv, wma) with extensions, MIME types and Apple type identifiers.
- `FormatSniffer`: the format from the first 64 KB (after any ID3 tags, however large), never from the extension. MP3 and ADTS need two consecutive frame headers. The extension only picks within the MP4 family.
- `IAudioDecoderProvider` (id, priority, `ProbeAsync`, `Create`) and `IAudioDecoder` (PCM at the file's own rate and channels into an `IPcmSink`).
- `AnalysisWavSink`: mono at 44.1 kHz PCM16. Audio at 44.1 kHz streams through; other rates are collected and resampled once at the end (Acoustics `Resampler`).
- `DecoderRegistry`: probes every provider once in the background (first asked at start-up by the dropped-file scan), sniffs, tries the providers that read the format by priority, and rewrites the target for each attempt. `RefreshAsync` re-probes and raises `CapabilitiesChanged`.
- `IDecoderInstaller`, `UnsupportedAudioException` (unknown format, or none of this device's decoders reads it).

**Providers:**

| Priority | Provider | Where | Reads |
|---|---|---|---|
| 1 | `ManagedDecoderProvider` (Core) | everywhere | WAV (incl. RF64), AIFF/AIFC |
| 2 | `MiniaudioDecoderProvider` (`Audio.SoundFlow`) | native heads | MP3, FLAC, WAV; still asks miniaudio for 44.1 kHz mono, so these imports are unchanged |
| 3 | `MediaFoundationDecoderProvider` (`Audio.MediaFoundation`) | Windows | here: M4A, ALAC, AAC, 3GP, MP4, WMA, AMR, WebM, MKV (with AAC/Opus), MP3, FLAC |
| 4 | `MediaCodecDecoderProvider` (`Audio.Android`) | Android | by `MediaCodecList`: M4A, MP4, AAC, 3GP, AMR, Ogg, Opus, WebM, MKV, MP3, FLAC |
| 6 | `FfmpegDecoderProvider` (`Audio.Ffmpeg`) | desktop | by `ffmpeg -demuxers` / `-decoders`: everything in the catalog |

- **Media Foundation:** source-generated COM interop (`[GeneratedComInterface]`), Source Reader to float at the native rate. A file whose extension doesn't match its content gets a temporary copy with the right one (the reader picks its parser by extension). The type change AAC decoders report on their first sample is accepted. The probe decodes an embedded 0.1 s sine clip per format (~0.5 s, background): Store codecs (Web Media Extensions is installed here) don't show in the registry and, here, aren't usable from a desktop app, so Ogg/Opus/CAF fall to FFmpeg.
- **MediaCodec:** synchronous API, first audio track, 16-bit or float output.
- **FFmpeg:** `ffmpeg -i <file> -map 0:a:0 -c:a pcm_f32le -f wav -`, the piped WAV read to its end (placeholder sizes). Found on the PATH, or the downloaded copy first. Its own error line comes through ("ffmpeg: …").

**Import** (`Core/Storage/TakeImportService.cs`, was `TakeImporter`):
- A RIFF WAV the analysis reads is stored as it is (unchanged).
- Everything else is decoded to `take.wav`, and the original is kept as `source.<ext>`: moved for dropped and picked files, copied for the CLI. Before, only dropped MP3/FLAC kept theirs.
- **Bug fixed:** `WavDecoder` read compressed WAVE data (ADPCM, µ-law…) as 8-bit PCM and analyzed noise. It now refuses it, so miniaudio decodes it (pitch 228 Hz instead of 485 on the test file). RF64/BW64 are read.
- The desktop CLI reports an unreadable file and carries on (exit code 1) instead of crashing.

**Picker and dropped files** (`Features/Import`):
- `ImportFilters`: "Audio and Timbratune takes" first, then one group per decodable format, then `.tmbr`, never "All files". Picked files are no longer filtered by extension; their content decides, with a reason when it can't be read (some Android file managers ignore the types).
- `IFileDialogs.OpenFilesAsync` takes `FileTypeFilter`s (extensions, MIME types, Apple type identifiers).
- Dropped audio files nothing here decodes stay in the takes folder, mentioned once in the import line at start.

**FFmpeg download** (§4.3; owner's decisions of 2026-10-15):
- **Build:** BtbN FFmpeg-Builds `autobuild-2026-10-15-13-03`, FFmpeg n9.0.2-28-g669f5ab083, LGPL static, for linux-x64/arm64 and win-x64/arm64. `scripts/mirror-ffmpeg.ps1` checks BtbN's `checksums.sha256` and repacks each to `ffmpeg-9.0.2-<rid>.tar.gz` with only `ffmpeg`, `LICENSE.txt` and a README (36–57 MB instead of 117–171 MB). The owner hosts them on Reyfen/github-releases-as-file-hosting, release `ffmpeg-n9.0.2-28-g669f5ab083`. URL, size and SHA-256 are pinned in `FfmpegInstaller.Pinned`; the uploads were checked against them.
- **Installer:** download over HTTPS (`IHttpDownloader`), hash and size checked before anything is unpacked (a mismatch deletes the file), only the program and licence/readme files unpacked by .NET's `TarReader`, flat, into `<LocalApplicationData>/Timbratune/tools/ffmpeg/<version>/` (`~/.local/share/…` on Linux), marked executable, older versions removed. No root, nothing system-wide. Flatpak: unavailable.
- **Offer** (`FfmpegOfferViewModel`, an overlay through `ImportRouter`): shown on Linux when no ffmpeg is found, before the picker, and after a batch with a file in a known but undecodable format. Download (progress, cancel), use my package manager (the command for apt/dnf/pacman/zypper to copy; "done" re-probes), not now (remembered: `import.ffmpegOfferDeclined`). On Windows the installer is registered but `IsOffered` is false: hidden until the settings page offers it.
- **Licence:** the offer names FFmpeg's LGPL with links to the licence and source; the About line says "some imports decoded by FFmpeg (LGPL)" whenever an ffmpeg is in use. `package-linux.sh` recommends `ffmpeg`.

**Tests:** 239, on Windows and on Linux (19 UI, 19 architecture, 57 acoustics, 141 core).
- New: `DecodingTests` (sniffer on 20 fixtures, renamed and non-seekable; big ID3 tags; AIFF and RF64 sample-exact; sink pass-through, downmix and resampling; registry priority, fallback and probing), `MediaFoundationTests` and `FfmpegTests` (pass with a note where unavailable; ALAC is sample-exact through both), `FfmpegInstallerTests` (offline: hash check before unpacking, nothing else unpacked, update, cancel), `ImportFiltersTests`, and new import tests.
- New architecture rule: features reach decoders only through `DecoderRegistry` / `IDecoderInstaller`.
- `RecordingManagerTests` (P3): the upper bound on snapshots now uses the measured recording time. The live loop ticks until Stop, 0.4 s past the clip, so 102 ticks plus the final snapshot is right; it failed on an idle Linux run with 103.
- Fixtures: `Fixtures/formats/`, half a second of `vctk_f339` in each format (FFmpeg 9.0). The probe clips are synthetic sines.

**Checked:**
- **Byte identity:** the same MP3 (44.1 kHz mono and 48 kHz stereo), FLAC and WAV files imported with the P3 CLI and the P4 CLI give identical `take.wav` hashes. 16-bit AIFF, RF64 and ALAC (through Media Foundation and FFmpeg) give the same `take.wav` as the FLAC.
- **Windows:** Media Foundation and FFmpeg imports through the CLI and the published single-file exe. In the app, dropped files (M4A/WMA imported, Opus left with the info line before FFmpeg was on the PATH) and the picker's type list.
- **Android emulator (Debug):** dropped M4A, 3GP, AMR, Ogg, Opus, WebM and MP4 imported through MediaCodec (pitch within 0.6 Hz of the desktop's), WMA left with the info line, and the system picker greys out WMA.
- **Linux (WSL):** the offer, the package-manager path, a failed download (404 before the upload: a clear message, nothing left), and the real download. It took ~2 s, unpacked only the three files and marked `ffmpeg` executable. Then the picker listed the new formats, dropped Ogg and M4A files imported, and the About line appeared. With the copy in place, nine formats import through the CLI.
- **Bench:** not run, since P4 doesn't touch the analysis. The golden snapshots pass unchanged on Windows.

**Linux golden test:** `GoldenTests.FinalLiveSnapshotMatchesTheGoldenFile(vctk_m311)` failed on Linux by 1.9e-6 in one live weight value, also on the P3 commit (not from P4); a separate session measured the cause and gave that one series its own tolerance (§8i).

## 8n. Refactoring P5: analysis tracks (2026-10-16)

Phase P5 of `REFACTOR-PLAN.md` (§2.1): the shared measurements of a take become tracks, computed once per update in fixed stages, for the saved and the live analysis alike. No user-visible change; every result is identical.

**Layout** (`src/Reyfen.Timbratune.Core/Analysis/Tracks/`):
- `TrackSet`: flags for pitch, intensity, harmonicity, formants (both ceilings), LTAS, pulses, voiced frames, sounding, the loud-vowel gate, and the derived vowel formant rows and weight frames. `WithDependencies()` adds what a track is computed from. Presets: `Live` (what a quick live update needs), `LiveFull` (plus the derived tracks), `All` (plus the LTAS).
- `TrackView`: a `readonly struct` with one typed field per track, plus the samples, the weight-row function and the `DerivedTracks`. `ToFrameTracks()` gives today's `FrameTracks` (it measures the whole-take jitter and shimmer from the pulses, as before).
- `DerivedTracks`: stage 2 (voiced frames, sounding stretches, the gate) and stage 3 (≤ 300 gated times with a plausible F1 at both ceilings; weight rows of ≤ 250 voiced frames). Live, one instance is refilled; the voiced frames, the gate and its sort buffer are reused lists.
- `SavedTrackProvider.Measure(sound, set, loudOutlierFraction, progress)`: stage 1 is the former `MeasureTracks` `Parallel.Invoke`, same kernels, settings, `Timing` names and progress weights; stage 2 runs the pulses beside voiced frames, sounding and gate; stage 3 the derived tracks.
- `LiveTrackProvider`: owns the live trackers (pitch, harmonicity, intensity, both formant ceilings), the pulse tracker and the weight-row memo that `LiveAnalyzer` held. Every update runs all four stage-1 trackers under `Parallelism.Options` (their state must keep up with the signal); the `TrackSet` decides stages 2–3. Same `live.*` probe names.

**On top of it, same signatures:**
- `AcousticsAnalysisEngine.MeasureTracks(sound, progress)` = `SavedTrackProvider.Measure(…).ToFrameTracks()`; a new overload takes the gate's outlier share, which the engine now passes in instead of `with { LoudOutlierFraction = … }` afterwards.
- `LiveAnalyzer.Update(final, length, full)` asks the provider for `TrackSet.Live` (quick) or `TrackSet.All` (full and final).
- `FrameTracks` gained `Derived` and `StretchJitter`. `RawAnalysisAssembler.Assemble` and `FrameSeriesBuilder.Build` use the derived tracks when present, and derive them as before when not, or when the gate was made with another outlier share (a `with { LoudOutlierFraction = … }` copy). So a live full update no longer computes the voiced frames and the gate twice (assembler and series), and the full analysis no longer twice either.

**Tests:** 243 with Praat (19 UI, 19 architecture, 57 acoustics, 145 core). New `TrackTests`: dependency closure, a partial set measures only what's asked, the derived tracks equal what the assembler derives (and a copy with another gate doesn't reuse it), quick live updates leave out the derived tracks and only the final one has the LTAS. The architecture test's positive control now looks in `LiveTrackProvider` and `TrackView`.

**Gates** (REFACTOR-PLAN §3):
1. **Identical results:** the golden snapshots (saved identical, live within 1e-7) and the analyze.py parity tests pass unchanged; the bench's saved and live result hashes are the same as P4's in every run.
2. **Bench, A/B against P4 (`dee1e42`)**, alternating, 3 runs of 10 each, the 27.6 s `long.wav`, medians:

| Measure | P4 | P5 | Change |
|---|---|---|---|
| Full analysis | 362.3 ms | 359.8 ms | −0.7 % |
| Full analysis allocation | 166.8 MB | 159.9 MB | −4.1 % |
| Live update, quick / full (per tick) | 9.62 / 9.72 ms | 9.62 / 9.67 ms | 0 / −0.5 % |
| Live updates, whole take | 2,825.5 ms | 2,825.3 ms | 0 % |
| Live allocation | 8.886 MB/s | 8.825 MB/s | −0.7 % |

   (`live.compute` 292.8 → 296.2 ms: not touched by P5, within run-to-run noise.)
3. **Phones, A/B against the P0 baseline build** (Release profiling builds, 6 alternating fake-mic runs each, method of §8i), medians:

| Phone | Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) |
|---|---|---|---|---|---|
| Pixel 9 | P0 `a835972` | 39.5 | 3.31 (3.00–3.62) | 20 | 2.08 s (1.67 s) |
| Pixel 9 | P5 | 39.4 | 3.31 (2.74–3.64) | 18.5 | 1.99 s (1.64 s) |
| Pixel 4a | P0 `a835972` | 51.9 | 9.63 (8.80–10.36) | 20.5 | 4.59 s (3.90 s) |
| Pixel 4a | P5 | 52.0 | 9.71 (8.58–10.34) | 22 | 4.54 s (3.83 s) |

   - Pixel 9: identical janky share, slightly fewer slow frames and a slightly faster Stop. It came up "offline" over USB until the adb server was restarted; the battery stayed at 21–22 % with Battery Saver off (it switches on at 20 % there; the driver now checks it before every run and stops if it is on).
   - Pixel 4a: within the baseline's spread (+0.08 points janky, a slightly faster Stop); a few more live updates per take (228.5 against 223.5, P3's loop) and fewer UI stalls (25.5 against 32). Battery 64 %, Battery Saver off.
   - Logged live-update counts vary between runs (194–271 on the Pixel 9) for both builds: logcat drops probe lines under load. The frame times come from SurfaceFlinger and aren't affected.
   - The driver now takes a lock, so a second one can't run against the same phone (§8i).
   - Both phones were left on the P5 profiling build, fake mic and test takes removed.

## 8o. Refactoring P6: metric modules (2026-10-16)

Phase P6 of `REFACTOR-PLAN.md` (§2.2): each metric family is one module, from the tracks to the saved take. No user-visible change; every result is identical.

**Layout** (`src/Reyfen.Timbratune.Core/Analysis/Metrics/`):
- `IMetricModule` (default methods, so a module implements only what it has), `MetricContext`, `DetailInputs`, `TrendWindow`, `RawAnalysisBuilder`; `MetricRegistry` (`Default`: Pitch, Resonance, Weight, Loudness, Clarity, Steadiness, Phrasing).
- One folder per module:

| Module | Raw values (`Measure`) | Take section (`Summarize`) | Phrase / trend columns | Series |
|---|---|---|---|---|
| `Pitch` | pitch mean, median, min, max, SD; the 10 ms contour | `Pitch` | mean Hz / mean Hz, pitch SD | pitch |
| `Resonance` | vowel-core formant rows, frames tried | `Formants` (F2 gate, ceiling choice, medians, usable share) | F2 / F2, F3 | f1, f2, f3 |
| `Weight` | weight frames, LTAS | `Weight` (Iseli–Alwan H1*–A3*, raw H1–A3, tilt) | corrected weight / weight | weight |
| `Loudness` | intensity mean, min, max; intensity frames | `Intensity` | – / energy average | loudness |
| `Clarity` | HNR mean; voiced harmonicity frames | `VoiceQuality.HnrDb` | – / mean HNR | hnr |
| `Steadiness` | jitter, shimmer; pulses | `VoiceQuality.JitterPct/ShimmerPct` | – / jitter of the slice's pulses | jitter |
| `Phrasing` | sounding stretches | `Register` and the detail (`AnalyzeRegister`: contour, phrases) | melody, ending / melody, endings | – |

- The metric-specific helpers moved with their module (`VowelFormants`, `UsableFormantPct`, `UnreliableFormantsBelowPct` → Resonance; `SpectralWeight`, `CorrectedH1A3`, `IseliCorrection` → Weight; `AnalyzeRegister` → Phrasing). `AnalysisPostProcessor` keeps `Process` (glue), the trend and phrase loops, `TrendStep`, the app-wide constants (register floor, semitone reference) and the Python-semantics statistics helpers. `RawAnalysisAssembler` keeps `Assemble` (glue) and the track-stage helpers. `FrameSeriesBuilder` keeps `Build`/`ForTake` (glue; `series.json` keeps its key order whatever the module order) and the shared `Series` writer.

**How it was done:** step 0 removed `FrameTracks` (the pipeline takes `TrackView`) and put everything into a temporary `LegacyMetrics` module, which made the three pipeline classes glue over the registry in one go. Each later step moved one metric out of `LegacyMetrics` into its folder, and the golden, parity and all other tests passed after every step. The Phrasing step deleted `LegacyMetrics`.

**Interface, adapted from the plan:** `Summarize` and the phrase/trend columns read `RawAnalysis`, not `TrackView`. The Praat reference engine (tests) produces `RawAnalysis` from Praat's own numbers and has no tracks; `RawAnalysis` is thus the compatibility layer the plan's `Compat/RawAnalysisAdapter` would have been, and `AnalysisPostProcessor.Process(RawAnalysis)` still serves both engines. Modules hold no state (one instance serves the saved and live analysis at once). The public `TimeTrends(detail, …)` and `PhraseBreakdown(detail, …)` keep their signatures (older takes' trends are rebuilt through them): they hand each frame list to the module that owns it.

**Side effect, measured:** the whole-take jitter, shimmer and HNR mean were computed on every live tick (in P5's `ToFrameTracks`) though only full updates use them; now `Measure` runs on full updates only.

**Not done here, for P7:** `MetricId` ints and `Presentation`; `TrendSlice` and `PhraseMetrics` as `double?[]` by metric index (they stay typed; modules set their own properties); `LiveSeries` shaped by the registry (modules fill the same pooled lists); appending only new frames to the live series (rebuilt in full each update, as before).

**Tests:** 243 with Praat. The architecture positive control looks for `VoiceReport` in the metric modules' namespace; two Core tests call `ResonanceModule.VowelFormants`/`UsableFormantPct`.

**Gates:**
1. **Identical results:** golden, parity and bench hashes unchanged after every step.
2. **Bench, A/B against P5 (`d645b26`)**, alternating, 6 runs of 10 each (two rounds of 3, the second started with P6), medians:

| Measure | P5 | P6 | Change |
|---|---|---|---|
| Full analysis | 348.2 ms | 352.0 ms | +1.1 % (ranges 334–357 / 341–364) |
| Full analysis allocation | 168.0 MB | 168.0 MB | 0 % |
| Live update, quick / full (per tick) | 9.58 / 9.55 ms | 8.88 / 9.60 ms | −7.4 / +0.5 % |
| Live updates, whole take | 2,813.6 ms | 2,618.5 ms | −6.9 % |
| Live allocation | 8.871 MB/s | 8.620 MB/s | −2.8 % |
| `live.compute` (not touched) | 295.2 ms | 296.0 ms | +0.3 % |

3. **Phones, A/B against the P0 baseline build:**

| Phone | Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|---|
| Pixel 4a | P0 `a835972` | 51.95 | 10.23 (9.80–10.78) | 21.5 | 4.63 s (3.94 s) | 219.5 | 43.9 / 66.4 ms |
| Pixel 4a | P6 | 52.47 | 9.12 (7.86–9.73) | 21 | 4.54 s (3.85 s) | 233.5 | 41.5 / 50.6 ms |

   - Pixel 4a (Release profiling builds, 6 alternating fake-mic runs each, battery 64 %, Battery Saver off): better than the baseline, with janky ranges that don't overlap; more live updates per take, each faster, and full updates a quarter quicker (the whole-take jitter, shimmer and HNR mean no longer run on quick ticks).
   - **Pixel 9: checked in P7,** as the phone schedule in REFACTOR-PLAN §3 sets (owner, 2026-10-16): its P7 A/B against the P0 build covers P6 and P7 together; on a miss, the P6 build is measured too.
   - The 4a was left on the P6 profiling build, fake mic and test takes removed (its 0.2.0 takes still untouched in private storage).

## 8p. Refactoring P7: registry-driven UI (2026-10-16)

Phase P7 of `REFACTOR-PLAN.md` (§2.4): the UI is built from the metric registry. Saved files and results are unchanged; the order of some cards, live timelines and chips changed.

**Metric descriptions** (`Core/Analysis/Metrics/MetricInfo.cs`):
- A module lists what it shows as `MetricInfo`s (`IMetricModule.Metrics`, numbered with `MetricInfo.Numbered`): `Name` (stable), `Title`, `Unit`, `Zones`, `Lo`/`Hi`, `Take` (the take's value), `Columns` (trend / phrase) and `ColumnKey` (today's JSON name), and the optional presentations `Card` (the line under the value), `Compare` (reference accessor, gendered, blurb), `Trend` (caption, colour, floor-dependent caption or bands, a highlight band, `MeasuredSinceVersion` for "measuring…"), `Live` (caption, `SmoothingSpec`, hold, or `Points` built from the contour) and `Chip`.
- The quantities: Pitch → mean, range, variability; Phrasing → melody, endings; Loudness; Resonance → F1, F2, F3; Weight; Clarity (HNR); Steadiness (jitter).
- `MetricRegistry` numbers every module's metrics: `MetricId(Index, Name)`, `Metrics` (id, info, module), `Id(info)`, `Find(name)`, `this[id]`. Order: Pitch, Phrasing, Loudness, Resonance, Weight, Clarity, Steadiness. It is registered in DI (`AddTimbratune`).
- `SmoothingSpec`/`WindowStatistic` moved to `Core/Domain` (modules carry their live smoothing; the shared ones are `LiveSmoothing.Frame/Vowel/Movement`); `TrendPoint`, `ScaleTick`, `TickKind` moved to `Core/Domain/ChartPoints.cs`. `Domain/Metrics.cs` (`MetricKey`, `MetricDef`) is deleted.

**Columns and series by metric:**
- `TrendSlice` and `PhraseMetrics` hold `double?[] Values` by `MetricId.Index` (`slice[id]`). Modules write `columns[metric]` (`MetricValues`: the module's offset in the registry plus the metric's slot, no lookup).
- `Json/MetricColumnsJson.cs` (the compatibility layer): the trend and phrase columns are saved under `MetricInfo.Key`, today's v2 names first in today's order, then newer metrics; unknown names are skipped on read. detail.json is byte-identical (golden tests); a take saved by the dummy build loads in the normal build.
- `FrameSeriesBuffers.Lines` is one pooled list per metric; modules fill `lines[metric]` (`SeriesLines`). F1 is collected only for a saved take (`ForSavedTake`). `LiveSeries` is a view of those lists by `MetricId` or `MetricInfo` (two instances, one per buffer set: no allocation per update). `LiveAnalyzer` takes an optional registry.

**UI:**
- `TakeViewModel`: stat cards = metrics with a `Card`, trend charts = metrics with a `Trend`, both in registry order; the F2/F3 gauges read the resonance metrics. Cards open the comparison by `MetricId` (`IMetricCompareLauncher.Open(MetricId)`).
- `LiveTimelinesViewModel`: `LiveLine.From(registry)` gives the timelines; `Compute(snapshot, lines, window)` smooths each metric's points with its own spec, so a chart and its line can't get out of step (the positional `Lines` array is gone).
- Chips (`TakeCardViewModel.Chips`) and `MetricModalViewModel` (`MetricInfo` + `Comparison`) read the same descriptions.
- Architecture rule `FeatureViewModelsDontDependOnControls` (positive control: a `Controls.ZoneBar` field in a view model fails it).

**Order on screen** (registry order; the trend charts are as before):

| Surface | Before | Now |
|---|---|---|
| Stat cards | pitch, range, loudness, variability, HNR, jitter, weight | pitch, range, variability, loudness, weight, HNR, jitter |
| Live timelines | loudness, variability, HNR, F2, F3, weight, melody, jitter | variability, melody, loudness, F2, F3, weight, HNR, jitter |
| Chips | pitch, range, F2, loudness, variability, HNR | pitch, range, variability, loudness, F2, HNR |

**Proof:** a throwaway `Metrics/Dummy/DummyModule.cs` (the loudest intensity frame) plus one registration showed up in the live timelines, the live and saved stat cards, the trend charts, the take card's chips, detail.json (`dummy_peak` after the v2 columns) and series.json (`dummy` after the known keys); then it was removed. `MetricRegistryUiTests` keeps the proof with a test registry (cards, trends, live timelines, saved series).

**Not done:** appending only new frames to the live series (§2.2's `UpdateLiveSeries(fromFrame)`); they are refilled in full each update, as before.

**Tests:** Core 145, architecture 20, UI library 23. `tools/live-steadiness` checks its lines against `Compute` by metric (no mismatch).

**Gates:**
1. **Identical results:** golden, parity and bench hashes unchanged (`428cdc1801047068` / `34e24c60fa4cab42`).
2. **Bench, A/B against P6 (`1fb534d`)**, alternating, 6 runs of 10 each, medians:

| Measure | P6 | P7 | Change |
|---|---|---|---|
| Full analysis | 328.0 ms | 328.5 ms | +0.1 % (ranges 318–337 / 322–339) |
| Full analysis allocation | 146.5 MB | 144.7 MB | −1.2 % |
| Live update, quick / full (per tick) | 8.84 / 9.52 ms | 8.79 / 9.42 ms | −0.6 / −1.0 % |
| Live updates, whole take | 2,584.7 ms | 2,581.9 ms | −0.1 % |
| Live allocation | 8.480 MB/s | 8.482 MB/s | 0 % |
| `live.compute`, whole take | 294.5 ms | 293.8 ms | −0.2 % |
| `live.compute` allocation | 1.961 MB/s | 1.958 MB/s | −0.2 % (after sizing the live melody list exactly; 1.971 before) |

3. **Emulator smoke test** (Debug APK, fake mic): start with 17 earlier takes, record with the live timelines and live cards, Stop and save, an older take's cards, chips and trends.
4. **Phones, A/B against the P0 baseline build:**

| Phone | Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|---|
| Pixel 4a | P0 `a835972` | 52.08 | 9.79 (9.18–10.23) | 21 | 4.59 s (3.91 s) | 219 | 44.3 / 61.3 ms |
| Pixel 4a | P7 | 51.64 | 10.88 (9.35–11.89) | 20 | 4.53 s (3.83 s) | 227 | 41.4 / 57.7 ms |
| Pixel 4a | P7, second A/B | 51.39 | 10.90 (9.97–11.85) | 26.5 | 4.62 s (3.92 s) | 230.5 | 41.7 / 54.8 ms |
| Pixel 4a | P7 with the P0 live order (test only) | 51.81 | 9.89 (8.04–12.74) | 24.5 | 4.61 s (3.91 s) | 230.5 | 41.9 / 56.2 ms |

   - Pixel 4a (Release profiling builds, 6 alternating fake-mic runs per build in each A/B, battery 97–100 %, Battery Saver off): the analysis is as fast or faster (more live updates, each quicker; Stop to saved equal), but janky frames rose by about 1 point. A second A/B against a P7 build whose live timelines keep the P0 order (Loudness first; not committed) traced it to the screen, not the code: during the run the phone shows the pitch chart and the first live timeline, which was Loudness in P0 and is Pitch variability in P7's registry order, and that chart costs more to draw. With the P0 order P7 measured 9.89 %, like P0.
   - **Owner's decision (2026-10-16): keep the registry order everywhere** (one order for every surface), accepting about 1 point of janky frames on the 4a while recording. Making the Pitch variability chart cheaper to draw is a possible later optimisation.
   - **Pixel 9** (same method; three A/Bs, battery 52–53 % and charging, Battery Saver off). Its A/B against P0 covers P6 and P7 (REFACTOR-PLAN §3); P7 measured a little higher, so P6 was measured too, as the plan asks, and the P0-order build once more:

| A/B | Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|---|
| 1 | P0 `a835972` | 39.58 | 3.27 (3.00–3.64) | 18.5 | 2.04 s (1.67 s) | 251.5 | 42.9 / 50.9 ms |
| 1 | P7 | 39.39 | 3.59 (3.18–3.74) | 19 | 2.00 s (1.65 s) | 257 | 39.8 / 56.0 ms |
| 2 | P7 | 39.55 | 3.50 (3.27–3.83) | 20 | 2.02 s (1.64 s) | 259.5 | 39.4 / 53.4 ms |
| 2 | P7 with the P0 live order (test only) | 39.53 | 3.45 (2.99–3.97) | 19.5 | 2.05 s (1.68 s) | 259 | 40.7 / 48.2 ms |
| 3 | P0 `a835972` | 39.59 | 3.31 (3.09–3.62) | 20.5 | 2.02 s (1.62 s) | 258.5 | 43.0 / 49.3 ms |
| 3 | P6 `1fb534d` | 39.58 | 3.23 (2.99–3.55) | 20 | 2.06 s (1.68 s) | 257 | 41.0 / 50.4 ms |

   - P6 matches P0 on the Pixel 9. P7 is about 0.2–0.3 points of janky frames above P0 in both of its A/Bs (about 3 more late frames in a 28 s take), with equal fps, Stop to saved well inside the 2.7 s limit, and faster live updates. Unlike on the 4a, the live order makes no difference here (the taller screen shows more charts at once), so this is P7's own cost, not yet located. Pooled (12 runs each): 3.27 % vs 3.54 % median, +8 % relative; a shuffle test puts the chance of a gap this size by luck at about 2 %.
   - **Owner's decision (2026-10-16): accepted** (too small to notice; the analysis itself got faster); recheck it in P9.
   - The 4a was left on the P7 profiling build (the last run), its 0.2.0 takes untouched in private storage; the fake mic and the runs' test takes stayed on it (it was unplugged before they could be removed) until P9 removed them (§8r).

## 8q. Refactoring P8: telemetry groundwork (2026-10-16)

Phase P8 of `REFACTOR-PLAN.md`, rescoped by the owner: analytics is a future concept, so this phase is the design (REFACTOR-PLAN §5, rewritten) plus groundwork. Nothing is sent: no Mixpanel or Sentry package, no network code, no UI. No user-visible change.

**Decisions (owner, 2026-10-16):**
- Mixpanel for usage analytics; Sentry for crash and error reports (suggested for its free plan: 5,000 errors, 5 GB logs a month, EU hosting; GlitchTip as a fallback with the same SDK).
- Eventually: performance data, errors, and anonymous user data: age range, voice target (the perceived gender the user trains towards), country, supporter status.
- Rollout: everyone who ever donated, plus a limited share of the others.
- EU hosting recommended for both, to be confirmed when the projects are created.

**Built:**
- `Core/Diagnostics/AppLog.cs`: `IAppLog`, `NullAppLog`, and `Log(level, area, $"…")` through an interpolated-string handler (nothing formatted or allocated when the level is off; invariant culture).
- Log points: `DecoderRegistry` (decoder chosen, failed, probe failed), `SettingsService` (save failed), `RecordingManager` (started, saved with analysis time and Stop to saved, Stop failed, live update failed), `ImportManager` (import done or failed, FFmpeg installed, backfill failed). Core services and the managers take the log, analytics and crash reporter as optional constructor arguments, so existing callers and tests are unchanged.
- `Shared/Telemetry/`: `IAnalytics` + `TakeAnalyzedEvent` + `NullAnalytics`, `AnalyticsPayload` (rounded durations and zone names only), `ICrashReporter` + `NullCrashReporter`, `CrashReportLimiter` (each kind once, at most 5 per session), `CrashHooks` (installed at start; subscribes nothing while everything is off), `TelemetrySettings` (`telemetry.analytics`, `telemetry.crashReports`, `telemetry.installId`, all device-scoped), `TelemetryConsent`, `TelemetryRollout` (FNV-1a bucket of the install id, 0–9999) and `RolloutRates` (default 25 % for analytics and for crash reports).
- `Shared/State/IEntitlements` (`HasEverDonated`) with `NoEntitlements`.
- `AddTimbratune` registers the do-nothing defaults, `NoEntitlements`, `RolloutRates.Default`, `TelemetryConsent` and the three settings.
- Architecture rule renamed and widened: `LiveAnalysisNeverReferencesTelemetry` (analytics, crash reports, Mixpanel, Sentry), with a positive control (`TheTelemetryRuleSeesTheCallSites`). Checked that it bites: a throwaway `ICrashReporter` reference in `LiveTimelinesViewModel` failed it, then was removed.

**Tests:** Acoustics 57, Core 145, architecture 21, UI library 33 (`TelemetryTests`: defaults off; 0 bytes allocated by the call sites when off; log levels and invariant culture; stable bucket; the rollout's rate, monotonic and donors; no sending without consent; install id lifecycle; limiter; payload holds zone names only).

**Gates:** build and all tests green; desktop start with a throwaway data folder. No performance gate: nothing on the analysis path changed (the live loop only logs in its error path, and the log is off).

**Next, when the adapters are added (REFACTOR-PLAN §5 "Later"):** the Mixpanel and Sentry adapters (EU), the consent switches in About, the remote rates file, the user properties, README and privacy text.

## 8r. Refactoring P9: final performance check (2026-10-16)

Phase P9 of `REFACTOR-PLAN.md`: every gate of §3 for the whole refactoring, P8 (`4d67e23`) against P0 (`a835972`), plus the Linux build.

**1. Identical results:** golden and parity tests pass; the bench's saved and live hashes are the same for both builds (`428cdc1801047068` / `34e24c60fa4cab42`).

**2. Bench, A/B against P0**, alternating, 6 runs of 10 each, medians. A first set was disturbed by other load on the PC (full analysis 338–963 ms in both builds), so it was run again on a quiet machine:

| Measure | P0 | P8 | Change |
|---|---|---|---|
| Full analysis | 341.8 ms | 335.9 ms | −1.7 % (ranges 331–369 / 330–353) |
| Full analysis allocation | 152.5 MB | 152.4 MB | 0 % |
| Live update, quick / full (per tick) | 9.85 / 10.16 ms | 8.88 / 9.45 ms | −9.8 / −7.0 % |
| Live updates, whole take | 2,793.8 ms | 2,587.7 ms | −7.4 % |
| Live allocation | 8.839 MB/s | 8.485 MB/s | −4.0 % |
| `live.compute`, whole take | 297.0 ms | 295.6 ms | −0.5 % |
| `live.compute` allocation | 1.961 MB/s | 1.958 MB/s | −0.2 % |

Within budget (≤ +3 % time, no more allocations): the refactored analysis is a little faster, as §3 expected (weight computed once).

**3. Phones** (Release profiling builds, LLVM AOT, A/B against the P0 build, method of §8i):
- **Pixel 9** (battery 55–58 %, USB, Battery Saver off; 6 runs per build, one P8 run dropped because the phone disconnected from USB during it):

| Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|
| P0 `a835972` | 39.54 | 3.23 (2.80–3.44) | 18.5 | 2.01 s (1.62 s) | 257 | 42.8 / 52.4 ms |
| P8 `4d67e23` | 39.47 | 3.55 (3.35–3.74) | 19 | 2.03 s (1.61 s) | 231 | 40.2 / 52.5 ms |

  - The P7 cost found in §8p is confirmed: +0.3 points of janky frames (shuffle test p ≈ 0.005), about 3 more late frames in a 28 s take. Frames per second, frames over 50 ms and Stop to saved (well inside the 2.7 s limit) are unchanged, and live updates are faster. Code review didn't locate it (the live cards update in place as before; the live order makes no difference on this phone, §8p).
  - **Owner's decision (2026-10-16): accepted as a known cost of P7.**
  - Cleaned up afterwards: the fake mic and the profiling takes removed, the owner's 20 takes untouched; the phone is left on the P0 profiling build.
- **Pixel 4a** (battery 84–99 %, USB, Battery Saver off; 6 runs per build). The first P0 run is dropped: Google Play Protect asked on the phone whether to send the newly installed build for a check, and the prompt covered the app while that run recorded (6 frames). The owner answered it; later installs didn't ask again.

| Build | fps | janky % (range) | > 50 ms | Stop to saved (analysis) | live updates | update median / full |
|---|---|---|---|---|---|---|
| P0 `a835972` (5 runs) | 51.90 | 9.83 (8.59–10.48) | 18 | 4.76 s (3.97 s) | 219 | 44.1 / 70.8 ms |
| P8 `4d67e23` | 50.99 | 10.89 (9.98–11.99) | 25.5 | 4.96 s (4.25 s) | 221.5 | 42.3 / 58.8 ms |

  - Janky frames +1.0 point (shuffle test p ≈ 0.02) and fps −0.9 (p ≈ 0.01): the live-order cost of P7 (§8p: +1.1 points, traced to the first live timeline the phone shows), which the owner kept.
  - Stop to saved, the analysis time and frames over 50 ms differ within noise (p 0.17–0.35; the bench shows the analysis itself 1.7 % faster). Live updates are quicker, full ones markedly (58.8 against 70.8 ms).
  - Cleaned up afterwards: the fake mic and the profiling takes removed (also P7's leftovers); the owner's 0.2.0 takes are untouched in private storage; the phone is left on the P0 profiling build.

**Verdict:** every gate passes or is a known cost the owner accepted (Pixel 9 +0.3 points and Pixel 4a +1 point of janky frames, both from P7). The refactoring keeps the results identical and the analysis as fast or faster.

**4. Linux** (WSL `Ubuntu-24.04`, `~/tt` copy): all tests pass (Acoustics 57, Core 145, architecture 21, UI 33). `build.ps1 linux` builds the `.deb` and the AppImage; the AppImage (run with `APPIMAGE_EXTRACT_AND_RUN=1`, no FUSE in WSL) starts, records with the fake mic with the live timelines, and Stop saves the take with its cards.

## 8s. Refactoring P10: documentation; the refactoring is done (2026-10-16)

Phase P10 of `REFACTOR-PLAN.md` closes the refactoring of v0.3.0 (P0–P10, §8i–§8s). Docs only:
- `REFACTOR-PLAN.md`: marked done; §10 lists the key files as built; new §13 "How to add a feature" and §14 "How to add a metric", each step tied to the architecture test or gate that checks it.
- `CLAUDE.md`: the layout table names Core's `Domain/`, `Models/` and `Json/` and the shared entitlements; the plan is described as the architecture and its record, with the checklists.

**Where it ended up** (details in §8i–§8r):
- Features are independent folders composed by DI (`AddTimbratune`), checked by architecture tests; the heads only add platform adapters.
- The analysis is computed once per update as shared tracks, then one module per metric family; the metric registry drives every surface (cards, trend charts, live timelines, chips, comparison) and the saved columns.
- Import reads almost any audio format through a decoder registry (managed, miniaudio, Media Foundation, Android MediaCodec, FFmpeg on demand).
- Settings persist per device; telemetry is designed and off (§5).
- Results are identical to v0.2.x; the analysis is a little faster (bench: full −1.7 %, live updates −7.4 %). Known costs accepted by the owner: janky frames while recording +0.3 points on the Pixel 9 and +1 point on the Pixel 4a, both from P7 (§8p, §8r).

**Next (later versions):** the telemetry adapters (Mixpanel, Sentry; §5), accounts and the avatar (§6) with settings sync (§5a.3), the browser head (§7).

## 8t. v0.4.0: resonance plan F0, baselines and the phone bench (2026-10-17)

Version 0.4.0 ports the research formant ceiling that follows the voice (R1b) and perceived size (R2) from `C:\Projects\Reyfen\Timbratune-research` (tag `research-r1b`), phase by phase: `RESONANCE-PLAN.md`.

- **Desktop baselines of v0.3.0** (`4f47e74`'s code; this machine, 24 cores, .NET 10.0.11, Release, `long.wav`, 10 runs): `tools/analysis-bench/baseline-v030.json`. Full analysis 339.6 ms (143.3 MB), live update quick / full 8.98 / 9.57 ms per tick, whole take 2,602 ms, live 8.50 MB/s. `tools/live-steadiness` gives the numbers of §8i unchanged (F2 4.54 / 2.12 / 46.44, F3 0.73 / 0.35 / 4.26, weight 1.51 / 0.72 / 10.70).
- **`tools/phone-bench`:** the phone A/B of §8d/§8i as scripts in the repo (`ab.sh`, `run.sh`, `analyze.py` with the shuffle test, `clean.sh`), for any package. Builds for it use the separate app id `com.reyfen.timbratune.research`, so the owner's installed app and its takes are never touched.
- **Android builds from a long path fail:** the Mono AOT compiler can't open assemblies under the session scratchpad (`Can not open image …linked\Microsoft.Extensions.DependencyInjection.Abstractions.dll`). Build from a short path (e.g. a worktree at `C:\Projects\Reyfen	t-base`).

**F1, the Acoustics kernels** (not used by the app yet; every output identical, goldens unchanged):
- `FormantAnalyzer.Burg` / `BurgAll` / `BurgResampled` take an optional `gridOrigin`: frames at origin + k × step wherever the whole window fits, instead of centred in the sound. `LiveFormantTracker` takes the same origin (frame times snapped to the grid), and has `Frames()` and an incrementally median-smoothed `SmoothedTrack()`.
- `Formants/FormantSmoothness.cs`: `IFormantFrames`, `FormantSmoothness.Criterion` (Weenink 2015: Legendre P0–P2 fits of F1–F3 weighted 1/B², by Cholesky; W = (s²/k)^1.25 · χ²/d), `Pieces` (16 frames every 8 in voiced stretches of ≥ 8), `FormantMedian` (5-frame running median; a frame keeps its own bandwidth).
- Live vs saved frames on the shared grid fall at the same times; their F3 differs by a median 2.4–3.2 Hz (VCTK f294 at 4500 Hz, m311 at 6500 Hz), because each live block is resampled on its own and 2 × ceiling doesn't divide 44.1 kHz. Exact equality would need the resampler to take a sample-grid offset.

**F2, the ceiling path** (`Core/Analysis/Ceilings/`, not wired in yet; `Formants` would clash with the model type):
- `FormantCeilingMethod { Path, Legacy }`, `FormantCeilings` (candidates 4500–6500 Hz; Legacy 5500/5000; grid origin 0 for the path, centred for Legacy; fallback 5500 Hz).
- `CeilingPathFinder.Find`: voicing per formant frame from the nearest pitch frame, pieces, log W per piece and candidate (cached per candidate by piece start and end, so a finder reused on growing live frames rescores only new or grown pieces; scored in parallel only when more than 64 are missing), unscorable entries = the piece's worst + 0.5, Viterbi with switch cost 12 (2 across a ≥ 0.25 s pause) and the live end margin of 4. `CeilingPath` (refillable segments, dominant, lowest, highest, switches) and `SwitchedFormantTrack`.
- Tests (`CeilingPathTests`): decoding (short stretch no switch, long one switches, a pause makes it cheaper, the end margin); synthetic tube voices with F1–F7 get 5500 Hz for ΔF 1000 Hz and 6500 Hz for ΔF 1207 Hz with F3 within 0.4 %; a long tract, a pause, then a short one switches 5500 → 6500 Hz; silence gives the fallback; a reused finder on growing frames equals a fresh one.

**Android, the share provider:** its authority was the fixed `com.reyfen.timbratune.fileprovider`, so a build with another app id couldn't be installed next to the app (`INSTALL_FAILED_CONFLICTING_PROVIDER`). It is now `${applicationId}.fileprovider`, and the sharer asks for `PackageName + ".fileprovider"`.

**F3, the saved analysis on the path** (results change on purpose; the goldens were rewritten once):
- `TrackSet.FormantPath` (stage 2, beside the pulses; needs pitch and the candidates; the vowel rows and weight frames depend on it). `TrackView` has `FormantMethod`, `FormantCeilings`, `FormantCandidates` (as measured), `Formants` (what every metric reads) and `CeilingPath`; `Formants5500` / `Formants5000` are gone.
- `SavedTrackProvider.Measure(…, formantMethod)`: the 5 candidates on grid origin 0, the path scored on the candidates **as measured** (scoring median-smoothed frames hides the label slips the criterion looks for: f294 then came out at 4500 Hz), then the used candidates median-smoothed and read along the path (`SwitchedFormantTrack`). Weight rows read the same track, so each moment's weight uses its own ceiling.
- `ResonanceModule`: one track, so the F2 jump gate runs over the rows in time order with no 5500/5000 choice; usable share over all rows; summary keys `formant_ceiling(_lowest/_highest/_switches)`. `Recording.Formants` gains `CeilingHz`, `CeilingLowestHz`, `CeilingHighestHz`, `CeilingSwitches` (written only when set). The `.tmbr` settings carry `formant_method` and the candidates.
- `AcousticsAnalysisEngine.FormantMethod` (default `Path`); `ParityTests` pin `Legacy` (analyze.py's two ceilings) and pass unchanged. `LiveAnalysisTests` compare with a Legacy saved analysis until F4.
- **Checked against the research engine:** `r1-check -choices Path -dump` (research clone) gives, on all four fixtures, exactly the new golden take values: f294 557.5 / 1543.5 / 2759.8 Hz, weight 4.83 dB, 5500 → 5000 Hz at 8.05 s; f339 592.1 / 1312.4 / 3024.8, 5.82 dB, 6000 → 5000 Hz at 4.83 s; m311 415.9 / 1398.4 / 2376.5, 9.26 dB, 4500 Hz; m345 432.8 / 1353.0 / 2378.2, 13.56 dB, 4500 Hz.
- **Golden diff** (v0.3.0 → F3): pitch, loudness, HNR, jitter, shimmer, phrases and the live files identical. Take F1/F2/F3: f294 511/1406/2635 → 558/1543/2760, f339 597/1316/2671 → 592/1312/3025, m311 419/1452/2478 → 416/1398/2376, m345 439/1396/2523 → 433/1353/2378; corrected weight 5.50 → 4.83, 6.78 → 5.82, 12.05 → 9.26, 13.50 → 13.56 dB; the F1–F3 series have 1–6 more points (the gate reads another track); the phrase and trend F2/F3/weight columns move with them.

**F4, the live analysis on the path:**
- `LiveTrackProvider(…, formantMethod)`: one `LiveFormantTracker` per candidate on grid origin 0 (one segment and resample per block for all of them). Each update finds the whole path again over the frames as measured (`CeilingPathFinder` keeps the piece scores, so only new or grown pieces are scored), with the previous update's last ceiling as the end to keep (margin 4; none on the final update), then reads the used candidates' incrementally median-smoothed tracks along it. The path, the finder, the frame views and the `SwitchedFormantTrack` are fields refilled per update. Weight rows are cached per (ceiling, time), so a moment that changes ceiling is measured again. `LiveAnalyzer(…, formantMethod)` passes it on (default `Path`); probe `live.formants.path`.
- `LiveAnalysisTests`: live vs saved on the fixtures with the same main ceiling on both sides (checked exactly); F1 ≤ 2.7 Hz, F2 ≤ 6.8 Hz, F3 ≤ 7.6 Hz except vctk_f339 at 31.5 Hz (saved 3007, live 3038 Hz: the research build gives the same numbers, with the same path and no switch: per-frame differences from the per-block resampling), weight ≤ 0.34 dB. The F3 bound went 15 → 35 Hz (the research had 45). New `LiveAndSavedChangeCeilingAtTheSameMoment`: m311 then f294 spliced; saved and live both go 4500 → 5500 Hz at 10.53 s (the voice changes at 10.4 s). Near the end they differ (saved 5500 → 5000 at 18.4 s, live 5500 → 4500 at 16.2 s): a near tie inside one voice, the R1b limit of RESEARCH-ALGORITHMS §13.6.
- The live goldens were rewritten (formants, weight and their phrase/trend columns only).
- **Cost (first look, `tools/analysis-bench -probes`, 3 runs, this machine):** full analysis 341 → 481 ms (alloc 167 → 200 MB), live updates over the take 2,576 → 3,464 ms, p95 per tick 17.7 → 33.7 ms, live 8.55 → 10.83 MB/s. The path is not the cost (saved 6 ms, live 22 ms per take): it is resampling and Burg at five ceilings, whose sampling rates add up to 55 kHz instead of 21 kHz (saved `analysis.formants` 245 → 386 ms, of which resampling 206 → 292 ms; `live.formants` 521 → 1,376 ms per take, ×2.6). The research's estimate (×1.1 live) was too low. Owner's decision 3: all five ceilings first, then the phones (F11).
- `tools/analysis-bench`: `-probes` prints each `Timing` probe's time per run, and a p95 row per tick.
- `RecordingManagerTests.RecordingFollowsTheMicrophoneLiveAndSavesTheTake` failed once while all four test projects ran at once (Debug), and passed three times alone: it expects at least 2 live snapshots per second of a real-time recording, and five ceilings make a Debug live update heavier. Watch it.

**F7, old takes keep both (storage; the display waits for the owner):**
- `take.json` `formants.method`: "path" for takes measured with the ceiling path; absent for older takes (5500/5000 Hz). `FormantCeilings.MethodOf(take)` reads it.
- **The trend backfill no longer mixes methods.** It re-analyses an old take's audio to fill its trends and series; with the path as the default it would have put path formants beside the take's 5500/5000 Hz cards. `IAnalysisEngine.WithFormantMethod` (the acoustics engine returns a copy with that method; the Praat reference returns itself), and `ImportManager.BackfillTrendsAsync` analyses with the take's own method.
- `RecordingStore.SaveReanalysis` / `LoadReanalysis` / `HasReanalysis`: a take's re-analysis is kept beside it as `take.reanalysed.json`, `detail.reanalysed.json`, `series.reanalysed.json` (take.json last, so its presence means complete; it carries the take's id, label, note and dates). The take's own files are never written. `Core/Storage/TakeReanalysis`: `Needs(take)` (an older method, no re-analysis yet, audio present) and `ReanalyseAsync(take)` (today's method).
- Not wired into the app yet: when to run it and how the two sets of numbers are shown is the owner's open decision 1 (options: a toggle on the take page, the new numbers with the original as a footnote, comparisons and trends on one method only).
- Tests (`ReanalysisTests`): an old take's four files stay byte-identical while the three re-analysis files appear beside them, the store still lists the take with its old numbers, the re-analysis equals a fresh analysis; a take measured today needs none; `WithFormantMethod(Legacy)` equals a Legacy engine; an old take's `.tmbr` still imports with its numbers (and then needs a re-analysis). Goldens: only `formants.method` added.
- `tools/phone-bench/ab.sh` installs with `-g`: the first run of a newly installed build otherwise stops at the microphone prompt (and the following runs find no app window).
- `RecordingManagerTests`: the snapshot floor is now one per second (see F4: a Debug build with five ceilings, under the parallel test load, made 17 in 9.8 s against the old floor of 18).

**F5, accuracy through this repo's engine:**
- `tools/formant-check` (new, dev tool): `-dump <dir> [-method Path|Legacy]` writes each file's saved analysis in the research tool's dump format (`m` cards, `c` path segments, `s` graph formants every 6.25 ms, plus `p` with F4 and `w` weight frames); `-live` records each file live as the app does and compares it with the saved analysis of the trimmed audio. `WeightModule.CorrectedH1A3` is public for it.
- **Identical to the research engine:** dumps of all six research lists (`vtr` 516, `vtr_noisy` 770, `mix_vtr` 226, `mix_noisy` 146, `mix_ears` 425, `tmasc` 242 files: 2,325) have the same cards, the same ceiling path and the same graph formants, to the last digit written, as the research build's `r1-check -dump -choices Path` (`Timbratune-research-data\cmp_<list>\Path` against `…\v040\cmp_<list>\Path`). So every R1b number in RESEARCH-ALGORITHMS §13.3 and `research/COMPARISON.md` holds for this port as it is: mixed M/F takes women F3 RMSE 188 Hz, men 152 Hz; single voices women F2/F3 148/174 Hz, men 105/156 Hz.
- **Live vs saved, the 42 EARS readings** (`formant-check -live -list research/lists/hyst_train.txt`): median |ΔF1, ΔF2, ΔF3| 2.4, 14.9, 10.0 Hz, p90 13.9, 52.2, 70.7 Hz, weight 0.16 dB (p90 0.55); the same main ceiling in 32 of 42 (the rest near ties). The research build: 2.5, 13, 9.5 Hz, p90 14, 51, 69 Hz.

**F6, the microphone picker:**
- `Core/Audio`: `AudioInputDevice(Name, IsDefault)` and `IInputDeviceSelection` (`InputDevices()`, asked again on every call; `InputDeviceName`, null = the default; `ActiveInputDevice`). The SoundFlow recorder implements it (`UpdateAudioDevicesInfo`, `InitializeCaptureDevice(chosen)`; a chosen device that isn't connected records from the default). The fake-mic replay doesn't, so it has no picker.
- `Features/Record/MicrophonePickerViewModel` + `MicrophonePickerView` on the record panel (while idle, after "live graphs show"): "system default (…)" and each device; the list is made off the UI thread after startup and again whenever the dropdown opens. Remembered by name in `record.microphone` (`SettingScope.Device`, "" = default); a remembered device that isn't connected stays selected as "… (not connected)" with a note under the picker. Hidden with one device and nothing remembered. `RecordingManager.InputDevices` hands the recorder's selection to the picker.
- Tests (`MicrophonePickerTests`): the choice reaches the recorder and the setting and comes back next session; a missing device stays chosen with the note, and is plain again once plugged in; one device or the fake mic: hidden. `CompositionTests` resolve it.
- Not checked here: recording through a non-default device (this PC has one input device; recording would use the owner's real microphone). Android: SoundFlow lists what AAudio reports; with one device the picker stays hidden.

**F8, perceived size (plan R2): evaluated, then ported.**
- **The measure:** per vowel-core moment, ΔF from F3 and F4 (least squares of Fi = (2i − 1)/2 · ΔF, Reby & McComb 2003), the take's median ΔF, and the apparent vocal-tract length VTL = 35000 / 2ΔF cm. The vowel-core rows now carry F4. Over **all** vowel-core rows: leaving out the F2 jump gate's rows made high voices' size less steady (EARS high-pitch women +0.30 against +0.21 cm, ICC 0.91 against 0.92). Only with the ceiling path; older (Legacy) takes have none.
- **Evaluation** (research clone `research/py/v040_size.py`, `results_v040_size.txt`; `formant-check -dump` of `ears` 749, `tmasc` 242, `mix_ears` 425, `mix_vtr` 226 and `vtr` 516 files, `z` and `r` lines), against R2's acceptance (RESEARCH-ALGORITHMS §8):

| Line | Acceptance | Result (v0.4.0 engine) | Legacy |
|---|---|---|---|
| EARS take-to-take ICC of VTL (3 regular passages, 103 speakers) | ≥ 0.9 | **0.92** (women 0.79, men 0.74; F3 0.88) | 0.92 (F3 0.67) |
| EARS high pitch − regular, women's VTL (n 60) | ±0.3 cm | **+0.21 cm** [IQR −0.38, +1.00]; men −0.93 cm (the raised larynx) | +0.49 cm |
| OSF trans/masc: our VTL vs the authors' (Johnson 2020) | r ≥ 0.75 | **0.78** (n 240) | 0.76 |
| OSF: F0 + size explain listeners' % "man" | R² ≥ 0.80 | **0.83** (F0 alone 0.76; F0 + F3 0.81) | |
| Mixed-voice takes: each part vs the same voice alone | within 0.3 cm | median **0.13 cm** (EARS voice changes, 340 parts) and **0.17 cm** (M/F splices, 811 parts); 73 % and 68 % of parts within 0.3 cm, p90 0.66 / 0.73 cm | |
| Live ≈ saved | | ≤ 0.22 cm on the fixtures (`LiveAnalysisTests`, bound 0.3 cm) | |

  The mixed-voice line holds at the median, not for every part: a part's rows in a mixed take are a share of the take's ≤ 300 subsampled rows, so short parts carry sampling noise.
- **Zones** (`Zones.Size`) from the norms through this engine, per-speaker medians: EARS women 15.68 cm (p90 16.77), men 18.48 (p10 17.32); VTR women 15.46 (p90 16.56), men 18.65 (p10 17.31). Smaller < 16.6 cm ≤ neutral < 17.4 cm ≤ larger (13–21 cm shown). Research clone `research/py/v040_size_norms.py`.
- **Port** (REFACTOR-PLAN §14): `ResonanceModule.Size` ("Perceived size", cm), listed after F1 and before F2, so it is the first resonance card, trend chart and live timeline; a trend column (no phrase column), a live timeline (vowel smoothing), the `size` series (`TakeSeries.CurrentVersion` 2), a chip, the comparison blurb, colour `Chart11` (new, light and dark). Wording: the size listeners hear, never body size (RESEARCH-ALGORITHMS §3.2). `take.json` `formants.delta_f_hz`, `formants.vtl_cm`. `TakeTrends.CurrentVersion` stays 2: a backfill can't add size to an older take (it re-measures with the take's own Legacy method), so bumping it would only re-analyse every old take for nothing.
- F2's comparison blurb now says it depends on the text; F1's card line says which ceiling(s) the take used ("formant ceiling followed the voice: 4500–5500 Hz, 1 switch, mostly 5500 Hz", "formant ceiling 5500 Hz", or "measured at a fixed 5500/5000 Hz formant ceiling (before v0.4.0)").
- **Choices made without the owner, to confirm:** size shown in cm (not ΔF in Hz); the zone borders above; size before F2 everywhere; the reference voices have no size yet (their comparison row shows none). On the phones the first live timelines cost the most to draw (§8p): size adds one (F11 measures it).

**F9, the desktop UI check and docs:**
- Release build, fake mic = the four-voice splice (`Timbratune-research-data\mixed_voices.wav`: man, woman, man, woman), throwaway data folder, `scripts/screenshot.ps1 -ProcId`. Footer v0.4.0; no microphone picker with the fake mic. Live: the new "Perceived size" timeline follows each voice (about 19–20 cm for the men, 15.5–16 cm for the women, in the zone colours), F2/F3 as before on the path. Saved take: a "Perceived size" card (16.96 cm, neutral, for the four voices together), and the Resonance section now leads with a "Perceived size" gauge (its own captions: smaller / lighter … larger / deeper; counted in the resonance summary), F2 "brightness cue · depends on the words", F3, then under F1: "Formant ceiling followed the voice: 4500–5500 Hz, 5 switches, mostly 4500 Hz." The size comparison dialog shows your takes; the reference voices have no size yet.
- Fixed on the way: F1 had been given a card line for the ceiling note, which made it a stat card of its own; the note is now in the Resonance section instead. `GaugeViewModel` shows the metric's unit and takes its end captions.
- Docs: CLAUDE.md (the plan, `Analysis/Ceilings/`, the re-analysis files, `tools/formant-check`, `tools/phone-bench`), REFACTOR-PLAN §10.
- Not done here: the emulator smoke test; the Pixel 4a runs of F11 record and save takes through the whole app instead.

**F10, performance on Windows** (`tools/analysis-bench -runs 10 -probes`, the same bench program in both: v0.3.0 `4f47e74` in a worktree against `771344b`; 6 rounds alternating, medians of the round medians; this machine, 24 cores, Release, `long.wav` 27.6 s, nothing else running):

| Measure | v0.3.0 | v0.4.0 | Change |
|---|---|---|---|
| Full analysis | 333.4 ms | 489.2 ms | +46.7 % |
| Full analysis allocation | 155.1 MB | 211.0 MB | +36.0 % |
| Live update, quick / full (median per tick) | 8.82 / 9.48 ms | 8.97 / 9.53 ms | +1.6 / +0.5 % |
| Live update, p95 per tick | 17.5 ms | 34.0 ms | +94 % |
| Live updates, whole take | 2,573 ms | 3,463 ms | +34.6 % |
| Live allocation | 8.52 MB/s | 10.86 MB/s | +27.5 % |
| `live.compute` (drawing data), whole take | 294.5 ms | 320.6 ms | +8.8 % (the size timeline) |

Probes per run: `analysis.formants` 239.6 → 389.6 ms (resampling 201.4 → 298.8, Burg frames 38.3 → 90.8), `analysis.formants.path` 5.5 ms; `live.formants` 524.5 → 1,382.0 ms, `live.formants.path` 22.0 ms; everything else unchanged (HNR, pitch, series, assembly). Rounds were consistent (full analysis: v0.3.0 320–341, v0.4.0 478–498 ms). The median live tick is unchanged; the formant blocks (every 0.5 s of audio) make the slow ticks twice as slow. The cost is the 3 extra ceilings' resampling and Burg frames (their rates add up to 55 kHz instead of 21 kHz), not the path. The research's estimate (saved ×1.2–1.3, live ×1.1) was too low. Above the refactoring's +3 % budget, which doesn't apply to a version that measures more on purpose; whether it is acceptable is the owner's call with the phone numbers (F11).

**F11, performance on the Pixel 4a** (Release profiling builds, LLVM AOT, both under the separate app id `com.reyfen.timbratune.research`, so the owner's app and takes were never touched; v0.3.0 from the `C:\Projects\Reyfen\tt-base` worktree with only the share-provider fix applied, v0.4.0 from `771344b`; 6 alternating pairs of cold-start 28 s fake-mic recordings of `long.wav`, `tools/phone-bench`; medians, shuffle test p):

| Build | fps | janky % | frames > 50 ms | live updates | update median / full | UI stalls | Stop to saved (analysis) |
|---|---|---|---|---|---|---|---|
| v0.3.0 | 52.0 | 10.4 (9.2–11.4) | 17.5 | 227.5 | 40.3 / 58.6 ms | 28 | 4.70 s (3.99 s) |
| v0.4.0 | 47.7 | 12.6 (11.5–14.0) | 32 | 193 | 48.9 / 75.4 ms | 32.5 | 6.43 s (5.70 s) |

- Every difference is significant (p ≈ 0.013 for janky %, fps, Stop and the update median). The live analysis keeps up (no backlog: a slow update makes the next one wait for the next 100 ms tick, so there are 15 % fewer of them), but frames are lost while recording (−4.3 fps, +2.2 points janky, almost twice the frames over 50 ms), and Stop to saved takes 1.7 s longer (+37 %). `live.formants.path` 0.4 ms per update. The size timeline adds drawing too (desktop `live.compute` +8.8 %).
- Conditions: battery at 17 % throughout (charging over USB but not gaining), Battery Saver off; the builds alternated, so both saw the same.
- Not measured: the Pixel 9 (not connected).
- **This is the owner's decision** (decision 3: measure the five ceilings first). The options in RESEARCH-ALGORITHMS §12.4: after the first seconds, track only the chosen ceiling and its neighbours live (3 instead of 5), or score the extra ceilings on every second block; the saved analysis could also skip ceilings no voiced piece can use. None is built.
- The phone was cleaned afterwards (fake mic, flags, profiling takes); the research-id app is still installed beside the owner's app: removing it is the owner's choice (it holds nothing of theirs).
- Android permission note: `ab.sh` installs with `-g`, so the test app has the microphone permission without a prompt.

**F12, Linux:** in WSL `Ubuntu-24.04` (user `tester`, a fresh export of `771344b` in `~/tt-v040`, beside the older `~/tt`): all 280 tests pass, goldens included; `build.ps1 linux` builds `Timbratune-v0.4.0-linux-x64.deb` and the AppImage. The AppImage wasn't started this time.

**Optimisations without losing quality** (after F11; owner: "other optimizations which will not reduce the actual quality"):
1. **Live, exact:** each live formant block resamples and analyses a 1 s segment but keeps only its middle 0.5 s. `Burg` / `BurgAll` take a `frameRange` (fixed grid only), so only the kept frames are analysed (frame times still from the whole grid's index), and `Resampler.ResampleAll` an `outputRange`, so only the samples those frames' windows read are interpolated (one more for the pre-emphasis). Bench result hashes unchanged (`3b704bbb…` / `118ee975…`); `live.formants` 1,382 → 965 ms per take.
2. **Half-rate low-pass (path candidates only):** after the brick-wall low-pass every candidate's signal is band-limited to ≤ 6.5 kHz, so the inverse transform can be half as long: `Resampler` `halfLength` / `LowPassedHalf` gives every second sample of the same low-passed signal (the kept bins, halved for the 1/n), and the sinc interpolation reads that 22.05 kHz signal. Used when every rate is below a quarter of the input's (`FormantCeilings.HalfRateLowPass`: the path; Legacy keeps analyze.py's full-rate path, so the parity tests are untouched; 16 kHz input such as VTR doesn't qualify and is unchanged).
   - Signal level (`ResamplerTests`): the difference to the full-length resampling is 120.6–121.3 dB below the signal at all five rates on two fixtures, about 25 dB under a 16-bit recording's own quantisation.
   - Results: VTR (16 kHz) identical; EARS (48 kHz, 749 files) 97.7 % of graph frames identical to 0.1 Hz, p99 0.1 Hz, one take's near-tie switch flipped (its F2 card 9.9 Hz); trans/masc cards within 0.1 Hz, size within 0.01 cm. Goldens moved by ≤ 0.01 Hz (saved cards) and ≤ 0.04 Hz (live points).
- **Bench now** (this machine, 5 runs): full analysis 489 → 384 ms (`analysis.formants.resample` 299 → 203 ms), live updates over the take 3,463 → 2,978 ms, p95 tick 34.0 → 20.9 ms, `live.formants` 1,382 → 901 ms (v0.3.0: 341 ms, 2,573 ms, 17.5 ms, 525 ms).

## 8. Current state, at the time of writing

**Branches (2026-10-10):** the user renamed them on GitHub: `dev` became `main` (everything up to `bc1ce48`, tagged `v0.1.0`), and `v0.2.0` became `dev/v0.2.0` (§8d–§8h, up to `8e88ea6`; not merged into `main` yet). The local branches were renamed to match and track `origin/main` and `origin/dev/v0.2.0`. The old `v0.2.0` branch name clashed with a `v0.2.0` tag; `dev/…` names can't coexist with a branch called `dev`. Commit `9c752e6`'s subject starts with a stray UTF-8 BOM from PowerShell 5.1's `Out-File`; write commit messages without a BOM.

**Tests:** 121/121 pass.

**Publish:** `./build.ps1 [win] [linux] [android]` builds into `publish/` (it replaced `build.bat` and `build.sh`, §8h). The 0.2.0 builds there were made from `9c752e6`'s code plus §8h. `publish/` also still holds older `Timbratune-Desktop-v…` files, which the user can delete.

**Android SDK on the user's side:** missing (§8h). Their builds need a real install (Android Studio, or the README command run from their own terminal). Claude's shells still see the hidden copy, so Claude's Android builds keep working.

**Phones:** the Pixel 9 has a profiling build of `b5d73f4`'s code (probes on, no flags file, no fake mic). The Pixel 4a has an older build without the jank fixes. Neither has the one-folder-per-take build yet; the emulator does (Debug).

**WSL test setup:** in `Ubuntu-24.04` the user `tester` has the .NET 10 SDK in `~/.dotnet` and PowerShell 7.6.6 in `~/.powershell`, neither on PATH by default. A copy of the tree is in `~/tt` for Linux build tests.

## 9. Possible next steps (none requested yet)
- Dim the provisional part of each line: the unsettled tail and the "now" extension.
- Loudness calibration or a fixed silence threshold, to stabilize formants, weight and phrases early in a take.
- Choose the formant ceiling once per take, or with hysteresis, to avoid live re-choices.
- Report `tools/live-steadiness` numbers in a test with bounds, so steadiness regressions get caught.
- Finish the Mint desktop VM check (§8a).
- Fix Android's 16 KB page alignment (§8a).
- Use a release keystore and an AAB for the Play Store.
- Re-measure the Pixel 4a with the jank fixes, and push the remaining 2–3 % janky frames on the Pixel 9 down (§8d).
- Implement the PDF export (§8e), and use `TakeArchive.Read` for previews or a richer import.
- Install a real Android SDK on the user's side, and optionally clear the hidden copy in Claude's AppData (§8h).
- On a phone: the `.tmbr` export and import through Android's file pickers, the move to the USB-visible folder, and the series cost after Stop (§8e, §8f).
- Live charts in the light theme have pale zone bands; their contrast could be improved.
- Port to macOS and iOS: same pattern, SoundFlow has natives for both.
