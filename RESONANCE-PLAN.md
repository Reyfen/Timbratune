# Resonance plan: the formant ceiling that follows the voice (R1b) and perceived size (R2)

*Status:* plan, 2026-10-17. Nothing is implemented yet.
*Base:* `dev/v0.4.0` at `4f47e74` (v0.3.0, the refactored architecture).
*Source:* the research in `C:\Projects\Reyfen\Timbratune-research` (tag `research-r1b`): `HANDOFF-RESEARCH.md`, `RESEARCH-ALGORITHMS.md` §2, §3, §8, §9, §12, §13, `research/COMPARISON.md`.

## How to use this document (for agents)

- **Read first:** `CLAUDE.md`; `HANDOFF.md` §5 (live = saved) and §8i–§8r (the gates and how they were measured); `REFACTOR-PLAN.md` §2, §3, §13, §14; then the research sections above.
- **The prototype is a reference, not a source.** It was written against the v0.2.1 layout (`LiveAnalyzer` owning the trackers, `RawAnalysisAssembler` choosing ceilings), and its live path breaks the hot-path rules of REFACTOR-PLAN §3 (LINQ, lambdas and arrays allocated on every update, dictionaries keyed by piece). Re-implement each piece from the papers (Weenink 2015; Reby & McComb 2003; Tukey 1977; Viterbi) and from the method described in RESEARCH-ALGORITHMS §12–§13, in the shapes of this repo. Use the prototype's numbers and tests as the target behaviour.
- **This version changes results on purpose.** So the REFACTOR-PLAN "identical results" gate does not apply to formants and weight. Instead each phase states which outputs may change, the golden files are rewritten once, on purpose, in the phase that changes them, and the diff is recorded in HANDOFF. Everything else (pitch, loudness, HNR, jitter, shimmer, phrases) must stay identical.
- **Order:** phases in order. Each ends green: build, `dotnet test Timbratune.slnx` (with Praat), its acceptance line, and a HANDOFF section (§8t onward). Commit only when the owner asks.
- **If reality contradicts this plan**, stop and ask the owner.

## Owner's decisions this plan follows (2026-10-17, RESEARCH-ALGORITHMS §9)

1. **Old takes: keep both.** Original numbers stay; the re-analysed numbers are stored alongside. How they are shown is open: ask before building that UI (phase 7).
2. **Perceived size (R2): port with R1b, if it meets R2's acceptance.** Default: a main resonance number, F2 kept as a secondary cue.
3. **Live: all 5 ceilings, properly, then measure on the phones.** The 3-neighbour fallback is not built up front.
4. **Weight zones stay** for this version (R1b moves men's weight about 1.7 dB lighter, no zone changes in the test data). Hawks–Miller bandwidths and new zones are R3, a later version.

## Target design in this architecture

**Acoustics** (pure, MIT, no allocation per frame once warmed up):
- `Formants/FormantAnalyzer.cs`, `Streaming/LiveTrackers.cs`: an optional frame-grid origin, so saved and live frames sit at `origin + k·step` regardless of where the sound ends (today the live blocks have their own grid). `LiveFormantTracker` exposes its frames in time order and an incrementally median-smoothed track (only the last 2 frames provisional).
- New `Formants/FormantSmoothness.cs`: Weenink's smoothness criterion W of a piece of frames (F1–F3 fitted by 3 Legendre terms, bandwidth-weighted least squares), and a 5-frame running median. Scratch buffers passed in or pooled; no per-piece arrays.

**Core** (`Analysis/Formants/`, a new folder next to `Tracks/`):
- `FormantCeilingMethod { Legacy, Path }`, part of an analysis options object both providers take. `Legacy` (5500/5000 Hz, the F2-SD chooser, Praat's centred grid) stays for `ParityTests` and the Praat reference engine. `Path` is the default. No `Smoothest` and no `CeilingHysteresis`: R1b superseded them.
- `FormantCandidates`: 4500, 5000, 5500, 6000, 6500 Hz.
- `CeilingPathFinder`: voiced pieces (16 frames every 8, inside voiced stretches of ≥ 8 frames), log W per piece and ceiling, Viterbi decoding over pieces with switch cost 12, 2 across a ≥ 0.25 s pause, an unscored-piece penalty, and a live end margin of 4 (none on the final update). Decoding over `Span<double>` / `Span<int>` work buffers the caller owns.
- `CeilingPath` (segments, dominant, lowest, highest, switches) and `SwitchedFormantTrack : IFormantTrack` (each moment reads the median-smoothed track its segment names; a switch falls halfway between pieces).

**Tracks** (`Analysis/Tracks/`):
- `TrackSet.Formants` now means the candidate ceilings (one shared resample, as today with two). New derived flag `TrackSet.FormantPath` (needs pitch for voicing and the candidates), computed in stage 2.
- `TrackView`: `Formants5500` / `Formants5000` give way to `FormantCandidates` (the per-ceiling tracks), `Formants` (the switched, smoothed track every module reads) and `CeilingPath`. In `Legacy` the fields hold today's two tracks, so the Legacy code path is unchanged.
- `DerivedTracks.ComputeStage3`: vowel formant rows from the one track (Legacy: both ceilings as today); weight rows measured on each moment's own ceiling.
- `LiveTrackProvider`: 5 trackers through `LiveFormantTracker.UpdateAll` (one segment and resample per block); a piece-score cache per ceiling as an append-only array indexed by piece (frames are only appended, so a piece's W never changes); the path found again on every update; the weight-row memo keyed by (ceiling, time) so a re-chosen moment never reuses another ceiling's row. The series are cleared and refilled each update already, and `LiveTimelinesViewModel` re-smooths from the points, so the recent past redraws when a switch is recognised.

**Metrics:**
- `ResonanceModule`: with a path there is one track, so `VowelFormants` keeps the analyze.py F2 jump gate but no 5500/5000 choice; `UsablePct` counts on the chosen track; series on `t.Formants`. The F1 card line says which ceiling(s) the take used ("followed the voice: 4500–5500 Hz, 5 switches, mostly 4500 Hz", or the one ceiling).
- `WeightModule`: unchanged code; its rows come from the path's tracks.
- R2 (phase 8): perceived size as a new `MetricInfo` in `ResonanceModule` (§14: another quantity of an existing family), listed before F2.

**Saved shapes:** `Recording.Formants` gets nullable `CeilingHz` (dominant), `CeilingLowestHz`, `CeilingHighestHz`, `CeilingSwitches`; the Compat summary keys `formant_ceiling`, `formant_ceiling_lowest`, `formant_ceiling_highest`, `formant_ceiling_switches`; the `.tmbr` settings carry the method and the ceilings. A take records which formant method made it (phase 7), so old takes are recognisable.

## Phases

### F0. Branch and baselines
- `dev/v0.4.0`: first commit bumps `<Version>` in `Directory.Build.props` to 0.4.0 (and sets up tracking of `origin/dev/v0.4.0` when the owner pushes).
- Desktop baseline of v0.3.0 (`4f47e74`): `tools/analysis-bench -out baseline-v030.json` on a quiet machine, 10 runs; `tools/live-steadiness` on `long.wav`. Keep a Release build of `4f47e74` for the A/B in F10.
- Bring the phone A/B driver into the repo this time (`tools/phone-bench/`: run, frame-time sampling, analysis; the method of HANDOFF §8i, with the battery and lock checks). Until now it lived in session scratchpads.
- *Done when:* baseline files saved, driver committed and dry-run on the emulator.

### F1. Acoustics kernels
- Grid origin in `Burg` / `BurgAll` / `BurgResampled` and `LiveFormantTracker` (default: today's behaviour).
- `FormantSmoothness` (criterion W, piece scoring helpers) and the running median, live incremental median.
- Cite Weenink 2015, Escudero et al. 2009, Tukey 1977 in `REFERENCES.md`.
- *Tests (Acoustics):* a label slip raises W by > 100×; doubled bandwidths scale W by 2^(2t−2); too few frames → ∞; the median removes a one-frame slip and keeps a linear drift; the live incremental median equals the batch median; with an origin, live and saved frames coincide in time.
- *Done when:* tests green; no change in any app output (the new code is not used yet: goldens identical).

### F2. Ceiling path (Core, not wired in yet)
- `Analysis/Formants/`: candidates, `CeilingPathFinder`, `CeilingPath`, `SwitchedFormantTrack`, the method option.
- *Tests:* synthetic long- and short-tract vowels get a ceiling near 5·ΔF with F3 within 12 %; decoding: a short better stretch does not switch, a long one does, a pause lowers the cost, the end margin holds the last segment live but not on the final update; frame counts differing by one between ceilings are clipped.
- *Done when:* tests green, goldens identical.

### F3. Saved analysis on the path
- `SavedTrackProvider`: 5 candidates on grid origin 0 (Legacy: today's 2 and centred grid), stage-2 path, `TrackView` fields, stage 3 on the one track, weight per moment's ceiling.
- `ResonanceModule` / `WeightModule` / Compat / `Recording.Formants` / `TakeArchive` as in the target design.
- `ParityTests` pin `Legacy`; the Praat engine path still fills the same sections.
- Rewrite the goldens once (`TIMBRATUNE_UPDATE_GOLDEN=1`) and record in HANDOFF which values moved and by how much; pitch, loudness, HNR, jitter, shimmer and phrases must be byte-identical.
- *Done when:* tests green; the four fixtures' F1–F3 within the research build's values (`r1-check -dump` on the same files) to a few Hz.

### F4. Live analysis on the path
- `LiveTrackProvider` as in the target design; probes `live.formants` (5 trackers) and `live.formants.path`.
- Hot-path review against REFACTOR-PLAN §3: no LINQ, closures, boxing or dictionaries per tick; allocation per tick checked with `analysis-bench -types`.
- *Tests:* `LiveAnalysisTests` stays green; try tightening its F3 tolerance back toward 15 Hz now that the grids are shared, and keep the tightest that passes on all fixtures. New: a man + woman splice of two VCTK fixtures, live and saved have the same ceilings either side and switch at the same moment (±0.1 s). `TrackTests` cover the new flag and its dependencies.
- *Done when:* tests green; live vs saved on the 42 EARS readings (via F5's tool) at median |ΔF1, ΔF2, ΔF3| ≈ 2.5 / 13 / 9.5 Hz or better.

### F5. Accuracy, through this repo's engine
- `tools/formant-check` (new, written for `TrackView`, not the v0.2.1 tools): runs the saved and live analysis per file and dumps per-moment formants, ceilings, weight and (for F8) F4, in the format the research scripts read.
- Score with the scripts in the research clone (`mix_check.py`, `compare_score.py`, `path_check_summary.py`); scripts and results stay there.
- *Acceptance* (RESEARCH-ALGORITHMS §8 R1 and §13.3): Hillenbrand gross F2/F3 ≤ 4 % in every group; VTR women F3 RMSE ≤ 195 Hz, men ≤ 160 Hz, F3 spikes ≤ 1.5 %; EARS take-to-take ICC of F3 ≥ 0.9; EARS high-pitch women's VTL change within ±0.3 cm; mixed M/F takes women F3 ≈ 188 Hz, men ≈ 152 Hz.
- `tools/live-steadiness` for F2, F3 and weight, against F0's numbers.
- *Done when:* every line met and written into HANDOFF.

### F6. Microphone picker (independent of F1–F5; may run in parallel)
- `IAudioRecorder`: list input devices and record from a chosen one (SoundFlow: device info refresh + capture on the chosen device); the fake-mic recorder has none.
- `SettingKey<string>` `record.microphone` (`SettingScope.Device`), remembered by name; a missing device shows "(not connected)" with a note, and recording falls back to the default.
- UI in `Features/Record/RecordPanelView` + `RecordViewModel`; on Android, check what the platform offers before adding it there.
- *Tests:* setting round trip, a missing device falls back; `CompositionTests`.

### F7. Old takes: keep both
- Mark how a take was analysed: a small nullable `analysis` section on `Recording` (formant method; absent = v0.3.0 and older = Legacy).
- **The trend backfill must not mix methods.** Today `ImportManager.BackfillTrendsAsync` re-analyses old takes with the current engine and rewrites their trends and series. From this version that would put path formants next to Legacy card values. It re-analyses old takes with `Legacy`, or writes into the alongside copy.
- Store the re-analysed numbers alongside the originals, never over them. Proposal to confirm with the owner: a `reanalysis/` subfolder in the take's folder with its own `take.json`, `detail.json`, `series.json` (same formats, one loader), made in the background like the backfill.
- **Ask the owner before the UI** (decision 1 is open). Options to put to them: a toggle on the take page (original / re-analysed); the re-analysed numbers shown with the original in a footnote; comparisons and trends always on one method.
- *Done when:* the storage and the background job exist with tests (originals untouched byte for byte; a `.tmbr` of an old take still imports), and the owner has chosen the display, which is then built.

### F8. Perceived size (R2): evaluate, then port
1. **Evaluate first,** with F5's tool dumping F3 and F4 on the path: ΔF per frame = (2.5·F3 + 3.5·F4)/(2.5² + 3.5²), take median, VTL = 35000 / (2ΔF) cm.
   - *Acceptance* (RESEARCH-ALGORITHMS §8 R2): EARS take-to-take ICC of VTL ≥ 0.9; EARS high-pitch women's VTL change within ±0.3 cm; OSF trans/masc: F0 + size explain ≥ 0.80 of the "man" ratings, and our VTL vs the authors' r ≥ 0.75; in mixed-voice takes each voice's size within 0.3 cm of the same voice alone; live ≈ saved.
   - Report to the owner. On a miss: stop; R2 waits for a later version.
2. **Port** (REFACTOR-PLAN §14): F4 in the vowel formant rows; a `Size` `MetricInfo` in `ResonanceModule`, listed before F2; `Recording.Formants` nullable `VtlCm` (and `DeltaFHz`); zones from the EARS/Hillenbrand norms per sex (`Domain/Zones.cs`); trend column under its new name (bump `TakeTrends.CurrentVersion`, `MeasuredSinceVersion`; this triggers F7's backfill path, so F7 comes first); live timeline with `LiveSmoothing.Vowel`; comparison blurb; wording "perceived size", never body size (§3.2).
   - Ask the owner, with numbers: cm or Hz shown, the zone borders, and whether F2's card text changes to "depends on what you say".
   - The first live timelines cost the most to draw on the phones (HANDOFF §8p): a new timeline is measured in F11.
- *Done when:* acceptance met, owner's answers built, `MetricRegistryUiTests` and `CompositionTests` green, desktop UI check (§11 of REFACTOR-PLAN).

### F9. Docs and UI check
- HANDOFF §8t onward: what changed, the golden diff, the accuracy table, the decisions; CLAUDE.md layout line for `Analysis/Formants/` and the new tools; `REFACTOR-PLAN.md` §10 key files.
- Desktop UI check with the fake mic on `long.wav` and on the four-voice splice (`Timbratune-research-data\mixed_voices.wav`, copied into a throwaway data folder): live F2/F3 follow each voice; the take page shows the ceiling note and, after F8, size.
- Emulator smoke test with a Debug APK (record, Stop, take page, old take).

### F10. Performance, Windows
- `tools/analysis-bench` A/B, v0.3.0 (`4f47e74`) against this branch, alternating, 6 runs of 10 each, quiet machine, Release; `-types` for allocations per tick.
- Report: full analysis time and allocation; live update quick/full per tick (median and p95); live updates per take; live MB/s; `live.formants` and `live.formants.path`.
- *Expected from the research:* saved ×1.2–1.3, live p95 +1.7–1.8 ms, nothing measurable for the path itself. The "+3 %" refactoring budget doesn't apply (5 ceilings cost 3 extra Burg passes by design), but: no allocation per tick beyond the 3 extra trackers' own buffers, and the path and caches allocate nothing per tick once warmed up. Anything beyond the expected numbers is investigated before F11.

### F11. Performance, Android
- Release profiling builds (LLVM AOT, `-p:TimbratuneProfiling=true`) of v0.3.0 and of this branch, both with a **separate app id** (`-p:ApplicationId=com.reyfen.timbratune.research`) so the owner's installed app and takes are never touched. **Ask the owner before installing**, and ask them to enable "Stay awake"; never change phone settings, never uninstall the owner's app.
- Pixel 4a and Pixel 9, 6 alternating fake-mic runs per build per phone (`tools/phone-bench`, `long.wav`, cold start, 28 s, Stop), battery and Battery Saver checked before each run.
- Report: fps, janky %, frames > 50 ms, UI stalls, live update median and full, `live.formants`, `live.formants.path`, live updates per take, Stop to saved.
- *Gates:* Pixel 9 Stop to saved ≤ 2.7 s; the 4a keeps up in real time (no growing backlog of live updates); janky % compared with the v0.3.0 build with the shuffle test of §8r. A miss goes to the owner with the numbers; the 3-neighbour fallback (RESEARCH-ALGORITHMS §12.4) is built only if they choose it.
- Clean up: fake mic, flags file and profiling takes removed; the research-id app left or removed as the owner says.

### F12. Linux and release
- WSL `Ubuntu-24.04` on `~/tt`: all tests, `build.ps1 linux`, AppImage starts, records with the fake mic.
- Release notes (HANDOFF §7 "Release notes"): what changed for users ("high voices' F2/F3 were often read too low; old takes keep their numbers"), then a PR from `dev/v0.4.0` to `main` when the owner asks.

## Not in this version

R3 (Hawks–Miller bandwidths, weight zones re-derived from the norms and then the owner's recordings), R4 (pitch octave check), R5 (NOISE-PLAN N1–N3), R6 (WLP/QCP formants), a per-voice summary of mixed takes (RESEARCH-ALGORITHMS §13.6), the 3-neighbour live fallback, and the owner's own recordings (§9.1–9.4, kept outside the repos), which are the real test of R1b and R2 once they exist.
