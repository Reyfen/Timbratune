# High-pitch plan: a formant ceiling chooser that works for high, soft voices, and the separation of pitch, size and weight

*Status (2026-10-18, third session):* the owner's take 24 (a high voice, then a low one of about 80 Hz with six resonances below 6 kHz) showed the plateau scoring hopping between two self-consistent readings at nearly every pause; the default is now `CeilingScoring.Steady` = `Plateau` plus a cost on changing the reading across a pause (§3, HANDOFF §8x), uncommitted, every §4 line as before (the women's tail and men's VTR F3 still open). The first three owner takes are fixtures with graph-line tests (§5). Left: C (more of the owner's takes: the low voice's true reading is the owner's call by ear) and D (a better formant estimate for voices above about 290 Hz).
*Base:* `dev/v0.4.0` after `dd0f4fc`.
*Owner's decisions (2026-10-18):* a **pitch-blind** chooser (no fix that reads F0 to pick the ceiling); **no reliability notes, fallbacks or hidden readings** in place of a fix ("find a proper solution, not avoid issues"); the owner's own takes are committed as fixtures and more can be recorded on request.

## How to use this document (for agents)

- **Read first:** `CLAUDE.md`; `RESONANCE-PLAN.md` (the ceiling path this plan corrects) and `HANDOFF.md` §8t, §8u (what was built and measured), §8v (the failure and this plan's first results); `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`; the research notes `C:\Projects\Reyfen\Timbratune-research\RESEARCH-ALGORITHMS.md` §2 (too many or too few poles), §3.4 (the high-voice artifact), §13 (the ceiling path).
- **The principle in §1 is a requirement, not advice.** Every change to the formant analysis is checked against it with the tests of §5 and the benches of §4.
- **Measure before claiming** (CLAUDE.md). The benches run from this repo (`tools/formant-check -dump` plus `tools/ceiling-bench/score.py`); the research clone is for reading, not for new results.
- **Acceptance lines are tails, not medians** (§4). v0.4.0 passed its acceptance at the median while a quarter of the women's high-pitch takes failed (§2).
- Commit only when the owner asks. If reality contradicts this plan, stop and ask the owner.

## 1. The principle: pitch, perceived size and weight are separate measurements

People who use Timbratune learn pitch, resonance (perceived size) and vocal weight **separately**, and they confuse them easily. The app's job is to tell them apart for the user, so each reading must be right on its own:

- **Pitch** (F0) is the rate of the vocal folds. It must not move the size or weight readings.
- **Perceived size** (the apparent vocal-tract length from F3–F4, shown in cm; F2 and F3 as secondary cues) is the resonator. It must not move with pitch (a user raising their pitch with the same tract must read the same size) nor with breathiness or weight.
- **Weight** (corrected H1*–A3*) is the glottal source's spectral tilt. It must not move with size, and its dependence on pitch is a known limitation to be reduced, not a feature.

Concretely, for the analysis:

1. **No measurement chooses its parameters from another metric.** The formant ceiling (the parameter that decides F2, F3 and size) is chosen from the formant tracks themselves, never from F0. A pitch-aware ceiling (drop 4500 Hz above 220 Hz, say) would make a man's practised high voice read smaller for a reason that is not his tract, and would hide the real problem: the chooser can't see a label slip at high pitch.
2. **Robustness is a property of each measurement.** A high pitch makes LPC harder (sparse harmonics, harmonic attraction); the formant analysis must handle that itself. A breathy voice makes HNR lower; the size reading must survive it. Noise reduction and gates are applied per metric (NOISE-PLAN), never globally.
3. **Separation is tested, not assumed.** `HighPitchTests`: the same synthetic tract at 120, 280 and 320 Hz reads the same ceiling and size; clear and breathy read the same size; light and heavy read the same size; a long tract keeps its size at a low and a high pitch; and the owner's real takes read like the Praat references. Every change to formants, size or weight runs these.
4. **Where a known coupling remains, it is written down with numbers**, in HANDOFF, and in the UI only as a documented limitation agreed with the owner, never as a substitute for a fix.

## 2. The failure (HANDOFF §8v, measured 2026-10-18)

The owner's first real v0.4.0 takes (`tests/Reyfen.Timbratune.Core.Tests/Fixtures/owner_take22.wav`, `owner_take23.wav`, `OWNER-TAKES.md`): median pitch 265 and 276 Hz (Praat and a cepstral check agree; 2 % of frames an octave below), median F1 about 300 Hz (F1/F0 1.1: the LPC sits on the first harmonic), HNR 14.5 and 8.3 dB.

| | Take 22: F2 / F3 | size | Take 23: F2 / F3 | size |
|---|---|---|---|---|
| v0.4.0 (ceiling path, smoothness) | 1660 / 2846 | 13.6 cm | **1319 / 2331** | **18.1 cm** |
| v0.3.0 (fixed 5500 Hz) | 1721 / 2932 | | 1560 / 2756 | |
| Praat, 5500 Hz, same vowel rows | 1725 / 2897 | 13.6 cm | 1658 / 2757 | 14.4 cm |
| Praat, 4500 Hz | 1233 / 2105 | 20.7 cm | 1178 / 1982 | 21.2 cm |

- **The Burg kernel is exact** (per-frame medians 3 / 20 / 28 Hz against Praat at the same ceiling). The failure is the **ceiling choice**: the path sits at 4500 Hz for 48 % of take 23's vowel rows (25 % of take 22's). In a 4500 Hz bracket a short tract has only four formants, so the fifth pole pair wedges between F2 and F3 and the labels slip down: "F3" is a spurious peak near 2000 Hz, "F4" is the real F3, and the size formula reads a 21 cm tract.
- **Why smoothness can't see it here:** per piece, log W(5500) − log W(4500) has a median of −0.03 (take 23) and −0.18 (take 22) with p10–p90 about ±3: a coin flip, the cheapest ceiling per piece scattered over all five (take 23: 94 / 56 / 54 / 37 / 19 pieces for 4500…6500). At 275 Hz the ten poles can sit on harmonics at every ceiling, and a harmonic-locked track is smooth even when mislabelled. With near ties, the switch cost of 12 commits the path to whichever ceiling a few strongly scoring pieces favour.
- **It was in the benches and the acceptance line hid it.** EARS women reading passage 1 in a high pitch (median F0 286 Hz, n 60): the accepted line was the median size change, +0.21 cm. The distribution: p90 **+4.2 cm**, 25 % of the women beyond +1 cm, 43 % within ±0.5 cm; the failing takes spend a median 74 % of their vowel rows at 4500 Hz against 0 % in the same women's regular reading, and their F3 drops from 2690 to 2251 Hz. No other bench has voices above 250 Hz (VTR: 4 of 516 sentences; trans/masc: none), and the unit-test fixtures top out at 222 Hz.
- The owner's takes are still more extreme than EARS (no EARS high-pitch take has F1 under 350 Hz; 14 of 71 above 250 Hz have F1/F0 under 1.5), so they are the first regression test (§5).

## 3. Direction: a pitch-blind chooser

The candidate built here (`CeilingScoring`, `CeilingPathFinder.Instability`): each voiced piece's cost on a candidate ceiling is

    SmoothnessWeight × log W  +  StabilityWeight × log(instability + 10 Hz)

where the **instability** is how far that ceiling's F2 and F3 sit from the other candidates' over the piece (the median over the piece's frames of the mean of |ΔF2| and |ΔF3| per other candidate; then, by `StabilityReference`, the nearer neighbour, the mean of both neighbours, or the median over all the others). This is the ceiling sweep of manual practice: the right ceiling is on the **plateau** where the formants stop moving with the ceiling; a ceiling that squeezes an extra pole in (too low) or loses one (too high) differs from its neighbours by hundreds of Hz. It reads only formant tracks, never F0.

Why this and not the alternatives considered:
- *Drop 4500 Hz:* still leaves take 23 at 5000 Hz with F3 2677 Hz and 15.0 cm; a long tract needs 4500 Hz (men's files pick it rightly 40–50 % of the time on every bench); and it is a tract prior, not a fix.
- *Self-consistency of ΔF with the ceiling (ceiling ≈ 5·ΔF):* a label slip is self-consistent (4500 Hz gives ΔF 826 Hz, 5500 Hz gives 1216 Hz: both "4.5 formants in the bracket").
- *Harmonic-lock detection (poles near k·F0 with narrow bandwidths):* both the right and the wrong ceiling lock onto harmonics at 275 Hz; it reads F0; and it would start a pitch-dependent branch of the analysis.
- *Scoring the median-smoothed tracks:* hides the slips the criterion looks for (RESONANCE-PLAN F3).
- *Literature remedies for harmonic attraction* (WLP-AME, Alku et al. 2013; QCP-FB, Gowda, Airaksinen & Alku 2017; RESEARCH-ALGORITHMS §2.4): these improve the **formant estimates** at high F0 and are the next layer if the plateau criterion isn't enough (§6, D). They don't choose the ceiling.

What was built first (2026-10-18, "partial implementation"):
- `Core/Analysis/Ceilings/CeilingScoring.cs`: the record (`SmoothnessWeight`, `StabilityWeight`, `StabilityFloorHz`), `Smoothness` (v0.4.0) and the process-wide `Default` the providers read when made without one (left at `Smoothness`).
- `CeilingPathFinder(CeilingScoring?)`, `Instability(...)`, the cached stability term per piece and candidate (append-only, so the live finder reuses it).
- The scoring passes through `AcousticsAnalysisEngine.MeasureTracks`, `SavedTrackProvider.Measure`, `LiveTrackProvider` and `LiveAnalyzer` as an optional argument (null = `Default`), so tests never touch the static.
- `tools/formant-check -stability λ -smoothness w` sets `Default` for a dump run; `tools/ceiling-bench/score.py` scores dump directories (§4).
- `HighPitchTests` (§5) with `Candidate` = smoothness 1, stability 3, reference `AllOthersMedian` (§4.1); the app's default is unchanged (`TheAppsDefaultScoringMeetsTheHighPitchBar` is skipped with the reason).

What the second session built (HANDOFF §8w):
- **The default, `CeilingScoring.Plateau`:** log W + 3 × log(relative distance of F2–F4 from the nearer neighbouring ceiling + 30 ‰) + 2 × log(the same from the median of all the others). The nearer neighbour finds the plateau; the median keeps the choice from resting on one small, noisy distance between two neighbours. `FirstFormant`, `LastFormant`, `Relative` and `MedianStabilityWeight` are the record's new fields.
- **Why the plateau criterion alone can't finish the job:** on voices above about 290 Hz the low ceilings (4500–5500 Hz) form a second, wrong plateau (the labels all slip the same way), and a median over the other candidates favours the middle ceiling by construction. Every variant tried (HANDOFF §8w's table) leaves 3–10 % of the EARS women beyond +1 cm.
- **Live = saved formant frames:** the resampling to 2 × the ceiling now puts the new samples on the frame grid's origin and low-passes with a 20 Hz raised-cosine transition (`Resampler.ResampleAll`'s `gridOrigin` and `transitionHz`, `FormantAnalyzer.AnchoredTransitionHz`; Path only, Legacy and the Praat oracle tests unchanged). Live and saved now have the same ceiling path on 66 of 66 files (46 before); without this, any term that compares candidates frame by frame made live and saved disagree.

What the third session built (HANDOFF §8x), from the owner's take 24 (`OWNER-TAKES.md`):
- **The default, `CeilingScoring.Steady`:** `Plateau` plus a cost on a change of *reading* across a pause: a change of ceiling at a pause costs 2 (against 12 inside a stretch) because the voice may change there, which let every phrase of the low voice re-decide between its two readings. Now, across a pause, the transition from ceiling *a* on the last piece before to ceiling *b* on the first piece after costs `ContinuityWeight` (0.12) × how much further the levels of F3 and F4 (the tract's cues, not the vowel's; the mean of the middle half of the piece's frames) jump than on the best continuation available, less a 50 ‰ dead zone. The best continuation is free, so a real change of voice (every ceiling's reading jumps) costs nothing extra, and a slip between the two readings of one voice (300–600 ‰) costs about 40: three switch costs. Pitch-blind: it reads formant tracks only.
- **Tried and rejected, measured** (HANDOFF §8x has the table): the same cost on every transition (it rewards a wrong reading for being flat: EARS women beyond +1 cm 12 %, VTR men's F3 238 Hz); the cost in excess of the best continuation on every transition (holds the owner's take but feeds piece-to-piece wobble into the path: live = saved fell to 62 of 68 and `LiveAnalysisTests` failed on F2/F3 by tens of Hz); medians instead of trimmed means (a median steps when one frame moves); F2–F3 only (F4 is where the two readings differ most); a same-moment cost inside stretches (on the same frames a real change of voice looks like a label jump: the man + woman splice test's change was delayed 0.8–1.7 s and became a near tie); weights 0.24–0.5 across pauses (the same delay at the splice).
- **The cost is a hurdle, not a decision:** which reading a stretch gets is still decided by its own evidence (the plateau terms); the owner's low voice holds the 4500–5000 Hz reading from 15 s on and its first 3 s continue the high voice's reading across the 12 s pause. The spectrum's count supports the low reading (six resonances below 6 kHz); whether a listener hears the 20 cm tract it implies is the owner's call.

## 4. Benches and acceptance (tails)

Run from this repo. Lists in the research clone (`research/lists/ears.txt`, `vtr.txt`, `tmasc.txt`: paths into `C:\Projects\Reyfen\Voice-Analyzer-Dataset`), plus the owner's fixtures:

```powershell
dotnet build tools/formant-check -c Release
tools/formant-check/bin/Release/net10.0/formant-check.exe -dump <out>/stab1 -stability 1 -jobs 8 -list <list.txt>
python -I tools/ceiling-bench/score.py <out>/base <out>/stab1
```

Acceptance for a scoring to become the default (all lines, every group). The default since §8w in the last column:

| Bench | Line | v0.4.0 (smoothness) | `Plateau` (the default, §8w) |
|---|---|---|---|
| Owner's takes | ceiling 5500–6500 Hz; F3 within Praat's 5500–6500 Hz band; size within 0.5 cm of Praat at 5500 Hz; the two takes within 1 cm | take 23: 4500 Hz, 18.1 cm | met: 6000 / 5500 Hz, 13.5 / 14.4 cm |
| EARS women, high pitch minus regular (n 60) | median within ±0.3 cm **and** p90 ≤ +0.5 cm **and** ≤ 5 % beyond +1 cm | +0.21 / **+4.20** / **25 %** | −0.08 / **+0.93** / **7 %**: not met |
| EARS men, high pitch minus regular (n 43) | median within −1.2…0 cm (the raised larynx is real); ≤ 5 % beyond +1 cm | −0.93 / 5 % | met: −0.65 / 2 % |
| EARS take-to-take ICC of size, three regular passages | all ≥ 0.9; women and men each ≥ 0.75 | 0.92; 0.79 / 0.74 | met: 0.94; 0.84 / 0.82 |
| VTR F3 RMSE on the app's vowel rows vs truth (this scorer's gate) | women ≤ 214 Hz, men ≤ 171 Hz (no worse than v0.4.0); gross ≤ 6 % / 5 % | 214 / 171; 6.0 / 4.8 % | 202 / **174**; 5.5 / 4.6 %: men 3 Hz over |
| OSF trans/masc, our size vs the authors' | r ≥ 0.75 | 0.78 | met: 0.79 |
| Live ≈ saved (`LiveAnalysisTests`, `formant-check -live`) | unchanged bounds | same path on 46 of 66 files | met: same path on 66 of 66 (the splice test allows one candidate 2 s inside a voice, §8w) |
| Cost | the stability term adds no measurable time on the Pixel 4a (it is a few subtractions per frame) | | desktop: none measurable; Pixel 4a: §8w |

### 4.1 First results (2026-10-18; `HANDOFF` §8v has the full tables)

| Scoring (`CeilingScoring`) | Take 22 / 23: ceiling, size | EARS women high − regular: median / p90 / beyond +1 cm / within 0.5 cm | EARS men: median / beyond +1 cm | ICC all (women / men) | VTR F3 RMSE women / men | trans/masc r |
|---|---|---|---|---|---|---|
| v0.4.0: smoothness only | 5500, 13.6 / **4500, 18.1** | +0.21 / **+4.20** / **25 %** / 43 % | −0.93 / 5 % | 0.92 (0.79 / 0.74) | 214 / 171 | 0.78 |
| + stability 0.5, nearer neighbour | 6000, 13.6 / 5000, 15.1 | −0.05 / +3.06 / 17 % / 48 % | −0.68 / 5 % | 0.93 (0.82 / 0.77) | 206 / 167 | 0.78 |
| + stability 1, nearer neighbour | 5500, 13.5 / 5000, 15.0 | −0.05 / +1.19 / 13 % / 53 % | −0.63 / 2 % | 0.93 (0.81 / 0.79) | 206 / 168 | 0.79 |
| + stability 2, nearer neighbour | 6000, 13.6 / 5500, 14.8 | +0.05 / +1.06 / 12 % / 48 % | −0.51 / 2 % | 0.94 (0.83 / 0.78) | 200 / 172 | 0.79 |
| + stability 4, nearer neighbour | 6000, 13.6 / 5500, 14.8 | +0.02 / +0.83 / 7 % / 67 % | −0.44 / 2 % | 0.94 (0.82 / 0.79) | 205 / 179 | 0.78 |
| + stability 2, both neighbours | **4500**, 13.9 / 5500, 14.8 | −0.12 / +1.93 / 15 % / 42 % | −0.71 / 5 % | 0.93 (0.80 / 0.78) | 206 / 175 | 0.79 |
| + stability 2, all others (median) | 5500, 13.7 / 5500, 14.7 | −0.02 / +1.15 / 12 % / 53 % | −0.63 / 5 % | 0.94 (0.79 / 0.85) | 208 / 170 | 0.79 |
| **+ stability 3, all others (median)** (`HighPitchTests.Candidate`) | 5500, 13.6 / 5500, 14.6 | +0.04 / +1.01 / 12 % / 58 % | −0.49 / 2 % | 0.93 (0.81 / 0.84) | 207 / 179 | 0.79 |
| + stability 4, all others (median) | 5500, 13.6 / 5500, 14.6 | +0.12 / +1.25 / 15 % / 60 % | −0.51 / 0 % | 0.93 (0.81 / 0.83) | 208 / 184 | 0.78 |
| stability only (smoothness 0, stability 1) | 6000, 13.4 / 5500, 14.5 | −0.01 / +0.99 / 8 % / 62 % | −0.50 / 0 % | **0.89** (0.72 / 0.69) | 206 / **196** | **0.72** |

Praat at 5500 Hz reads the takes at 13.6 / 14.4 cm. Reading the table: the stability term is what fixes the owner's takes and the women's tail (every row with it halves the share beyond +1 cm or better); the smoothness term is what keeps men right (without it, men's VTR F3 and the ICC fall and the trans/masc correlation drops below the line). The women still beyond +1 cm with the candidate (p044, p006, p053, p083: F0 292–338 Hz) sit at 5500 Hz, so the remaining error is no longer the 4500 Hz slip but a slip at 5000–5500 Hz on voices above 290 Hz, or a wrong regular reading; HIGH-PITCH-PLAN §6 A says what to try next. The candidate keeps the long-tract synthetic tests and every other Core test green; the two owner-take tests and the separation tests pass with it (the default stays smoothness only, so `TheAppsDefaultScoringMeetsTheHighPitchBar` is skipped).

How to read it: the stability term is what fixes the owner's takes and the women's tail; the smoothness term is what keeps men right (stability alone drops men's VTR F3 accuracy, the ICC and the trans/masc line). Neither the nearer-neighbour nor the all-others reference meets the tail line yet. The women still failing with the candidate (p044, p006, p053, p083; F0 292–338 Hz) sit at 5500 Hz, not 4500: the remaining error is a slip one bracket lower on voices above 290 Hz (or a wrong regular reading to compare with), which §6 A addresses.

### 4.2 Second session (HANDOFF §8w has the tables)

The default against v0.4.0 and against smoothness on the new resampling (so the scoring's own effect shows):

| Scoring | Take 22 / 23: ceiling, size | EARS women high − regular: median / p90 / beyond +1 cm | EARS men: median / beyond +1 cm | ICC all (women / men) | VTR F3 RMSE women / men | trans/masc r |
|---|---|---|---|---|---|---|
| v0.4.0 (smoothness, centred resampling) | 5500, 13.6 / **4500, 18.1** | +0.21 / **+4.20** / **25 %** | −0.93 / 5 % | 0.92 (0.79 / 0.74) | 214 / 171 | 0.78 |
| smoothness, anchored resampling | 6000, 13.6 / **4500, 17.4** | +0.14 / **+4.00** / **25 %** | −0.68 / 5 % | 0.93 (0.79 / 0.79) | 217 / 167 | 0.77 |
| **`Plateau`, anchored resampling** | 6000, 13.5 / 5500, 14.4 | −0.08 / +0.93 / 7 % | −0.65 / 2 % | 0.94 (0.84 / 0.82) | 202 / 174 | 0.79 |
| **`Steady`** (the default since §8x: `Plateau` + continuity across pauses) | 6000, 13.4 / 5500, 14.4 | −0.08 / +0.94 / 8 % | −0.64 / 0 % | 0.94 (0.83 / 0.82) | 201 / 174 | 0.79 |

The women still beyond +1 cm: p006, p044, p053, p098 (F0 290–338 Hz). A Praat sweep puts their right ceiling at 6000–6500 Hz; at 4500–5500 Hz their labels all slip the same way, so a plateau criterion sees two plateaus and smoothness favours the low one. That is §6 D's job.

## 5. Tests

`tests/Reyfen.Timbratune.Core.Tests/HighPitchTests.cs` (run with `Candidate` = `CeilingScoring.Plateau`, the default; point `Candidate` at another scoring to measure a change):

1. `TheSameTractReadsTheSameSizeAtAnyPitch` (120, 280, 320 Hz): the owner's tract shape (F1 320, F2 1700, F3 2800, F4 4100 Hz…) must get a ceiling of 5500–6500 Hz, F3 within 8 % and size within 0.5 cm of the true value at every pitch, including where the first harmonic sits on F1.
2. `ABreathyVoiceReadsTheSameSizeAsAClearOne`: aspiration noise at −10 dB (HNR clearly lower) changes the size by < 0.5 cm.
3. `AHeavierVoiceReadsTheSameSizeAsALighterOne`: a brighter source (weight reads > 2 dB heavier) changes the size by < 0.5 cm.
4. `ALongTractReadsTheSameSizeAtAnyPitch` (100, 250 Hz): a man's tract keeps 4500–5500 Hz and its size when he speaks high.
5. `TheOwnersTakesReadLikePraatAtTheRightCeiling` and `TheOwnersTwoTakesAgreeOnSize`: the fixtures against the Praat references in `OWNER-TAKES.md`.
6. The owner's **graph lines**, not just the cards: the saved F2 and F3 series (what the take's graphs draw) against Praat on the same moments at fixed 5500 and 6000 Hz (`Fixtures/owner_take*.praat.tsv`, made by `tools/ceiling-bench/praat-tracks.praat`; per-piece table in `OWNER-TAKES.md`). Bounds measured 2026-10-18, take 22 / 23:
   - `TheOwnersGraphLinesFollowPraatPieceByPiece`: every 3 s piece's median F2 and F3 within 150 / 200 Hz of Praat's 5500–6000 Hz band there (pinned in `PraatPieces`). Worst piece outside the band: plateau F2 −72, F3 −140 Hz; Smoothness F2 −524, F3 −938 Hz (2 of 6 and 8 of 11 pieces fail).
   - `TheOwnersF3LineHoldsItsLevelAcrossTheTake`: the SD of the F3 piece medians under 150 Hz. Plateau 81 / 80 Hz; Smoothness 315 / 324 Hz (v0.4.0, centred resampling: 392 Hz on take 23); v0.3.0 69 Hz.
   - `FewOfTheOwnersF3PointsStrayFromPraat`: under 15 % of the F3 points more than 300 Hz from Praat at the nearer of 5500 / 6000 Hz (Praat's two readings themselves differ by more than that on 17 / 21 % of the points). Plateau 5.7 / 10.3 %; Smoothness 18.5 / 43.2 %.

   All three fail with `Candidate = CeilingScoring.Smoothness` and pass with the default.
7. **Take 24** (HANDOFF §8x; `OWNER-TAKES.md`): a high voice for 12 s, then a low one of about 80 Hz with two self-consistent readings. `TheOwnersGraphLinesFollowPraatPieceByPiece("owner_take24")`: the high voice's pieces against Praat at 5500–6000 Hz, the low voice's against 4500–5000 Hz (the reading the spectrum's count supports; the first 3 s and 12–15 s, where the low voice's own reading takes over, are listed but not checked). `TheOwnersTake24HoldsOneReadingPerVoice`: the SD of the F3 piece medians within the high voice (3–12 s) and within the low voice (15–33 s) under 100 Hz: `Plateau` 48 / 165 Hz, `Steady` 47 / 55 Hz. Both fail with `Candidate = CeilingScoring.Plateau`.
8. `TheAppsDefaultScoringMeetsTheHighPitchBar`: the default is the scoring these tests pass with, and the graph-line tests' series are the ones `AcousticsAnalysisEngine.AnalyzeAsync` saves.

`ResamplerTests.ABlockOnTheGridOriginResamplesToTheWholeTakesSamples` pins the live = saved resampling (a block within 110 dB of the whole take; measured 117–121 dB, the brick wall 64 dB). The goldens were rewritten once for the new default (§8w: only formant-, size- and weight-derived values moved).

## 6. Tasks for the next agent, in order

Status after the third session (HANDOFF §8x): the owner's take 24 is a fixture, the continuity-across-pauses term is the default (uncommitted; the goldens are unchanged by it). **The owner's verdict (HANDOFF §8y): size must not depend on pitch; take 24's low voice is the same instrument as the high one and must read about 14 cm. Pinned as acceptance tests, red until met (take 24's low voice at Praat's 6000 Hz reading and within 1.5 cm of the high voice; take 25's feminine pieces at 5500–6000 Hz). The chooser's levers are exhausted for this (§8y: continuity holds but can't decide; a bandwidth floor changes nothing; the 6000 Hz analysis of the low voice is the worse LPC fit, so no scoring prefers it). Next, D′: a labelling-free size estimate (the tube pattern fitted to the whole resonance set; the two highest resonances of the 4500 Hz analysis already give 14–16 cm on every piece of takes 24 and 25), designed on the research benches with ground truth, then carried into `ResonanceModule`, the series, the trends and `score.py`; then D.** The owner says the lab recordings are too clean and offers more of their own: ask for them (a steady low voice on a known text, the same text high, breathy and clear, with a phone and with a headset), classify each with the references (`OWNER-TAKES.md`), and pin them.

A. **Meet the tail line.** The candidate reads the owner's takes right and halves the women's failures, but 12 % of the EARS women are still beyond +1 cm (p90 +1.01 cm), now at 5500 Hz with pitches above 290 Hz. Look at those takes first (p044, p006, p053, p083: which bracket is right by a Praat sweep, and whether their *regular* reading is the one that is off). Then try: a higher weight; the instability over F1–F3 or F2–F4; relative (semitone) differences instead of Hz; the median over a longer window than the piece; comparing with both neighbours (sum) rather than the nearer one. Each variant: `HighPitchTests` and the §4 sweep, numbers into §4.1.
B. **Make it the default** once every §4 line holds: `CeilingScoring.Default = Candidate` (or the adopted weights), remove the Skip, rewrite the goldens once (`TIMBRATUNE_UPDATE_GOLDEN=1`; pitch, loudness, HNR, jitter, shimmer and phrases must stay byte-identical), record the golden diff in HANDOFF. The takes' `formants.method` stays "path" (same candidates, a better choice); write the scoring's name into `take.json` if the owner wants old v0.4.0 takes told apart (`TakeReanalysis` can re-measure them).
C. **More of the owner's takes.** Ask the owner for takes over the range they can produce (high and low pitch, "bigger" and "smaller", "lighter" and "heavier", clear and breathy), each with what they were trying to achieve. Classify each with the references (Praat at 5500–6500 Hz and the cepstral pitch check, `OWNER-TAKES.md`), not with the app, and pin them in `HighPitchTests`. The separation tests then run on real material: a take where only pitch was changed must keep its size; a take where only weight was changed must keep its size and pitch.
D. **If the plateau criterion isn't enough** for some takes (the formant estimates themselves pulled onto harmonics), the next layer is a better formant estimate at high F0, pitch-blind by construction: WLP-AME or QCP-FB (RESEARCH-ALGORITHMS §2.4, from the papers, never from Praat's code), measured on Hillenbrand's girls and EARS high-pitch before anything else.
E. **Live.** The live finder reuses the cached stability term; check `LiveAnalysisTests` and `formant-check -live` on the owner's takes (same ceiling path live and saved), and a phone A/B only if the desktop bench shows any cost.
F. **Docs.** HANDOFF section with the numbers; CLAUDE.md pointer; this plan's §4.1 and the OWNER-TAKES table.

## Not in this plan

Weight's own pitch dependence (RESEARCH-ALGORITHMS §4.4: at F0 above about 300 Hz the corrected H1*–A3* is noisier; H1*–H2* and CPP as the breathiness cues), the Hawks–Miller bandwidths (R3), noise reduction (NOISE-PLAN), and a per-voice summary of mixed takes. Each is a separate plan, under the same principle (§1).
