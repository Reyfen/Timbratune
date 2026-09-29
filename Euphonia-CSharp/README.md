# Euphonia (C# / Avalonia)

A C# port of [`../Euphonia-TypeScript`](../Euphonia-TypeScript): record a take and read your pitch, resonance,
weight, and register/phrasing metrics on one dashboard. Same metrics, same zones, and the same `recordings.json`
data format as the Electron app. The look is close but not pixel-identical.

The voice analysis is **pure C#** (`Euphonia.Acoustics`). It needs no Praat, Python, ffmpeg or anything else
installed, so the same code can run on Windows, macOS, Linux, Android and iOS.

## Quick start (Windows)

Prerequisites: **.NET 10 SDK**.

```powershell
dotnet run --project src/Euphonia.Desktop
dotnet test
```

To build a folder you can launch without the SDK tools, publish it. The output folder is named per platform:

```powershell
dotnet publish src/Euphonia.Desktop -c Release -r win-x64 --self-contained false -o publish/Euphonia-win-x64
# → publish\Euphonia-win-x64\Euphonia.Desktop.exe  (reference voices included; needs the .NET 10 runtime)
```

Other useful commands:

```powershell
# analyze existing WAV files without the UI (like `uv run analyze.py clip.wav --label …`)
dotnet run --project src/Euphonia.Desktop -- --import take1.wav take2.wav --label "rainbow passage"

# use a throwaway data folder
$env:EUPHONIA_DATA_DIR = "C:\temp\euphonia-test"
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
| `src/Euphonia.Desktop` | Desktop head: `Program.cs`, `--import` | No |
| `tests/Euphonia.Acoustics.Tests` | Synthetic-signal tests, plus a component-by-component comparison with real Praat | — |
| `tests/Euphonia.Core.Tests` | Zones, JSON compatibility, store, statistics, **parity with analyze.py** | — |

### Dependencies

- **Avalonia 11.3** (+ Fluent theme, Inter font) is the UI.
- **CommunityToolkit.Mvvm** provides the MVVM source generators.
- **SoundFlow** handles recording and playback (WAV + the MP3 reference clips).
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
takes about 0.5 s.

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

## What's in v1 and what isn't

**Ported:**
- recording, with an optional label
- analysis
- the seven stat cards with zone bars
- the metric reference modal: take dots in lanes, VCTK reference ticks, and click-to-play
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
