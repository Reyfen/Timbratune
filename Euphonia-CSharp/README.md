# Euphonia (C# / Avalonia)

A C# port of [`../Euphonia-TypeScript`](../Euphonia-TypeScript): record a take, analyze it with Praat, and read your
pitch, resonance, weight, and register/phrasing metrics on one dashboard. Same metrics, same zones, and the same
`recordings.json` data format as the Electron app. The look is close but not pixel-identical.

## Quick start (Windows)

Prerequisites: **.NET 10 SDK**. Python, ffmpeg and Node are not needed.

```powershell
# 1. fetch Praat (the analysis engine) into tools/praat/ — ~52 MB from github.com/praat/praat
powershell -ExecutionPolicy Bypass -File scripts\fetch-praat.ps1

# 2. run
dotnet run --project src/Euphonia.Desktop

# tests (the parity tests run the real Praat against analyze.py's output)
dotnet test
```

To build a folder you can launch without the SDK tools, publish it. The output folder is named per platform:

```powershell
dotnet publish src/Euphonia.Desktop -c Release -r win-x64 --self-contained false -o publish/Euphonia-win-x64
# → publish\Euphonia-win-x64\Euphonia.Desktop.exe  (Praat + reference voices included; needs the .NET 10 runtime)
```

Other useful commands:

```powershell
# analyze existing WAV files without the UI (like `uv run analyze.py clip.wav --label …`)
dotnet run --project src/Euphonia.Desktop -- --import take1.wav take2.wav --label "rainbow passage"

# use a throwaway data folder / a different Praat
$env:EUPHONIA_DATA_DIR = "C:\temp\euphonia-test"
$env:EUPHONIA_PRAAT    = "C:\Tools\Praat.exe"
```

Takes are stored in `%APPDATA%\Euphonia-CSharp\`. This is deliberately separate from the Electron app's
`%APPDATA%\Euphonia`. The layout is the same:

```
recordings.json         index (sorted by id, pretty JSON — same schema analyze.py writes)
audio/NNN.wav           44.1 kHz mono PCM16, recorded directly (no ffmpeg step)
analysis/<id>.json      10 ms pitch contour + phrases for the register section
```

## Solution layout

| Project | What it is | Platform-specific? |
|---|---|---|
| `src/Euphonia.Core` | Models, zones and metric registry, JSON, `RecordingStore`, the analysis pipeline (`analyze.praat` + `AnalysisPostProcessor`), WAV writer and waveform peaks | No — plain .NET |
| `src/Euphonia.Audio.SoundFlow` | Microphone capture and playback via [SoundFlow](https://github.com/LSXPrime/SoundFlow) (miniaudio) | No — ships natives for Win/macOS/Linux/Android/iOS |
| `src/Euphonia` | Avalonia UI: views, view models (CommunityToolkit.Mvvm), custom-drawn charts | No |
| `src/Euphonia.Desktop` | Desktop head: `Program.cs`, `--import`, and it bundles `tools/praat` as `praat/` | Only the Praat binary differs per OS |
| `tests/Euphonia.Core.Tests` | xUnit: zones, JSON compatibility, store, statistics, **Praat parity** | — |

### Dependencies

- **Avalonia 11.3** (+ Fluent theme, Inter font) is the UI.
- **CommunityToolkit.Mvvm** provides the MVVM source generators.
- **SoundFlow** handles recording and playback (WAV + the MP3 reference clips).
- **Praat 7.x** is an external binary, not a NuGet package, used as the analysis engine.
- **xUnit** is used for tests.

## How the analysis works

`analyze.py` (parselmouth) is the reference. Its work is split in two:

1. **`src/Euphonia.Core/Analysis/analyze.praat`** runs every Praat command with the *same parameters*:
   - `To Pitch 0 75 500`
   - `To Formant (burg) 0 5 5500|5000 0.025 50`
   - `To Harmonicity (cc)`
   - `To PointProcess (periodic, cc)` for jitter and shimmer
   - `To Intensity`, `To Ltas`, and per-frame spectra for H1/A3
   - `To TextGrid (silences)`

   It prints raw values to stdout. Praat 7 sandboxes scripts that write files, so the script only reads the WAV and
   prints; no `--FULL-TRUST` is needed.
2. **`AnalysisPostProcessor.cs`** does the numpy/statistics half:
   - the F2 stability gate and the choice of formant ceiling
   - medians and standard deviations
   - the Iseli–Alwan correction for corrected H1*–A3*
   - the LTAS slope
   - phrase onset/offset stats, register %, and semitone SDs

**Parity:** `ParityTests` runs Praat on four VCTK clips and compares the results with `analyze.py`'s output for the
same WAVs, in `tests/…/Fixtures`. Every metric matches to the rounded digit, except **jitter and shimmer**, which are
within 4%. Parselmouth 0.4.7 bundles Praat 6.1.38 and we ship Praat 7.0, and the periodic point-process picking
differs slightly between those versions. The gap is far inside the zone widths.

Praat has no Android or iOS build. `IAnalysisEngine` is the seam where a future mobile engine plugs in, and the UI
doesn't change.

## What's in v1 and what isn't

**Ported:**
- recording, with an optional label
- analysis
- the seven stat cards with zone bars
- the metric reference modal: take dots in lanes, VCTK reference ticks, and click-to-play
- resonance (F2/F3 gauges)
- register & phrasing: contour chart, stat tiles, drop-position bars and the tip
- **trends within the take** (this differs from the React app, whose trend charts plot one point per take across
  all recordings). The take is split into phrases at the pauses (`To TextGrid (silences)`), and five charts plot one
  point per phrase: pitch, in-register melody, ending pitch, F2 and weight. The values are stored as
  `phrase_metrics` in `analysis/<id>.json`. Takes analyzed before this existed are re-analyzed once in the background
  from their WAV.
- the recordings list: waveform player, save-a-copy, delete with confirmation
- the take switcher
- the cheat sheet
- light (blossom) and dark (dusk-plum) themes that follow the OS, with a toggle

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

The code is under the MIT License, like the TypeScript repo. **Praat is GPLv3**. Any build that ships Praat next to
the app is a combined work under GPLv3, which is the same situation as the Electron installer and its bundled
parselmouth. `fetch-praat.ps1` saves the GPL text alongside the binary. The reference voices come from VCTK
(CC BY 4.0); see `src/Euphonia/Assets/reference/ATTRIBUTION.md`.
