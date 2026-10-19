# Formants plan: a formant estimate better than any version so far, on a ground truth of our own

*Status:* plan, 2026-10-19; **phase A built the same day** in the research clone (RESEARCH-ALGORITHMS §14): the glide truth tool validated on synthetic glides (every F1–F4 within 7 Hz), the synthetic voices through the measured microphones, the consensus oracle, and the harness with every baseline on every set (`research/results_fbench_*.txt`); the first prototype (P1) is in the table (§15 there). The base is `7dffa2a` on `dev/v0.4.0` (the `Steady` ceiling chooser, the owner's takes and the rainbow set as tests, HANDOFF §8v–§8z). Waiting on the owner's glides (§8).
*The owner's decisions (2026-10-19):* the aim is accuracy better than any previous version and better than Praat, not a match to v0.3.0; **formants first, weight after**; **prototype several ways and measure before choosing**; **ground truth is the critical issue** (the owner can't label their own recordings by eye, and so far "right" has meant "Praat at a ceiling we chose"); stability across devices is a goal, but **high-quality devices come first**; **no machine learning in the app's algorithm** (a learned model may make labels and references, never readings); pitch, size and weight stay separate measurements (HIGH-PITCH-PLAN §1).

## How to use this document (for agents)

- Read first: `CLAUDE.md`; HIGH-PITCH-PLAN.md (§1 the principle, §6 what was tried); HANDOFF §8t–§8z (every measurement behind this plan); `tests/…/Fixtures/OWNER-TAKES.md`; the research clone's `RESEARCH-ALGORITHMS.md` §2 (poles and ceilings), §2.3 (the first dynamic-programming attempt and why it locked), §13.
- Research and prototypes go in the research clone (`C:\Projects\Reyfen\Timbratune-research`), in Python where that is faster; only the winner is ported into `Acoustics` in C# (pure, allocation-free, AOT-safe). Praat and any ML tool are **oracles and labelling aids only**: nothing from them goes into the app (`CLAUDE.md`: no Praat code; the owner: no ML in the final algorithm). The app's estimator is a signal-processing method a person can read and explain.
- Every claim gets its number from the harness of §4.5, on every truth set of §3, in the priority order of §2. Do not tune a weight and re-run one bench: that is the loop of §1.
- Commit only when the owner asks. When reality contradicts the plan, stop and ask.

## 1. Where we are, and why the last iterations looped

Four versions, one design: a 10-pole Burg LPC fit below each of five ceilings, the Nth pole below the ceiling called Fn, and a **chooser** that picks which ceiling to believe.

| Version | The chooser | Fixed | Broke or left |
|---|---|---|---|
| v0.3.0 | 5500 or 5000 Hz per take, by the steadier F2 | the owner's phone takes read 13.6 / 14.3 cm, consistent piece to piece | 43 % of the EARS women reading high read > 1 cm bigger; F3 RMSE 367 Hz on VTR women; the choice flips with the microphone (up to 2.9 cm) |
| v0.4.0 | Weenink smoothness, a Viterbi path over pieces (§8t) | VTR women's F3 367 → 214 Hz; EARS tail 43 → 25 % | the owner's takes: 4500 Hz on a 270 Hz voice, "F3" at 2000 Hz, 18 cm (§8v); the lines "all over the place" piece to piece |
| Plateau (§8w) | + stability of F2–F4 across neighbouring ceilings | the owner's takes 13.5 / 14.4 cm; EARS tail 7 %; live = saved (a resampling fix found on the way) | 4 EARS women above 290 Hz on a wrong plateau; no plateau on PC-microphone takes; take 24's low voice hopping between two readings |
| Steady (§8x) | + continuity of the reading across pauses | take 24 holds one reading | it holds the wrong one (20 cm); take 25's feminine pieces at 5000 Hz where 5500 was right; 40 variants tried (§8w–§8y), 3–10 % tail, none better |

Why that is a loop, not a path:

1. **A chooser cannot be better than its candidates.** The candidates are five fits of the same kind, and each is right only when exactly five resonances lie below its ceiling. A voice with six below 6 kHz (take 24's low voice), or with harmonics so sparse that every ceiling's poles sit on them (270–340 Hz), or a microphone that moves the poles (§8z) has no right candidate. The 40 variants moved the choice between wrong candidates.
2. **"Correct" was circular.** The reference for the owner's voice was Praat's Burg at a ceiling we judged right by its plateau, which is the same LPC with the same failure modes. On lab corpora (VTR, Hillenbrand) the truth is hand-labelled and the chooser is fine there; the owner's voice never had a truth.
3. **The acceptance lines hid the failures:** v0.4.0 passed its line as a median while a quarter of the women failed; then tails were added, but on corpora with no voice like the owner's, no PC microphones, no headsets.
4. **The microphone was never in the model.** The rainbow set (§8z): one performance, four microphones, sizes 15.2–18.7 cm, F2/F3 lines 300–1300 Hz apart piece by piece; Praat at a fixed ceiling moves as much. The measured responses (§5) say why.
5. **Each iteration tuned one knob and re-ran one bench.** That is a local search in a space that does not contain the answer.

Rules that follow, kept in this plan: a truth of our own before any new estimator; several estimators of different kinds, compared on one table, before any is ported; the microphone in the test set from the start; acceptance as tails and as agreement across takes and devices; no reading of pitch in the formant estimate (the principle), but F0 may be *removed* from a spectrum, which is the opposite of coupling (§4.2, for the owner to confirm).

## 2. Goals and acceptance, in priority order

**Priority 1: correct on high-quality recordings** (a USB or built-in PC microphone, the phone's own microphone: the rainbow set's `usb` and `relay`, the owner's phone takes). Lines, all to be met by the estimator that replaces the chooser:

| Truth | Line | Today (`Steady`) |
|---|---|---|
| VTR hand-corrected tracks (516 sentences), the app's vowel rows | F3 RMSE ≤ 150 Hz women and men; gross (> 15 %) ≤ 3 %; F2 RMSE ≤ 130 Hz | 202 / 174 Hz; 5.5 / 4.6 %; F2 173 / 142 Hz |
| Hillenbrand steady vowels (men, women, boys, girls) | gross F1 / F2 / F3 ≤ 1 / 2 / 2 % in every group | Weenink per speaker: 0.4 / 0.7 / 2.0 men, 0.7 / 1.7 / 4.2 women, girls 3.5 / 3.9 / 3.1 |
| The owner's glide truth (§3.2) | F2 within 60 Hz, F3 within 80 Hz, F4 within 120 Hz of the envelope peaks on each vowel; size within 0.5 cm of the glides' F3–F4 | no truth yet |
| The owner's takes 22–25 and the rainbow set on `usb` / `relay` | HighPitchTests and RainbowTests green: one size per voice regardless of pitch (within 1 cm), lines steady piece to piece (F3 piece SD ≤ 150 Hz), the two agreeing microphones within 0.7 cm | 25 red |
| EARS (749 readings) | women reading high beyond +1 cm bigger ≤ 5 %, p90 ≤ +0.5 cm; take-to-take ICC ≥ 0.9 | 7 %, +0.93 cm; 0.94 |
| Human labellers (VTR inter-labeller RMS: 78 / 100 / 111 Hz on F1 / F2 / F3) | the floor; an estimate within 1.5 × it is "as good as a person" | |

**Priority 2: stable across devices.** The headset (16 kHz hands-free) and the webcam within 1 cm of size, 250 Hz of F3, 300 Hz per 3 s piece of the reference (RainbowTests' lines). Secondary: met after priority 1, and possibly only in part for microphones that physically lose the information (a 16 kHz hands-free codec carries F4 at 3.5–4.5 kHz only at its edge; §5 says what to do then).

**Priority 3: weight** (§6), after the formants it depends on are right.

**Cost:** live ≈ saved as now; the Pixel 4a A/B (`tools/phone-bench`) within a few per cent of today's live update time.

## 3. Ground truth: the critical issue

### 3.1 What exists

| Set | Truth | Covers | Lacks |
|---|---|---|---|
| VTR-Formants (TIMIT, 516 sentences; `Voice-Analyzer-Dataset`) | hand-corrected F1–F3 tracks | connected speech, men and women, studio | high pitches (4 sentences above 250 Hz), any microphone variety |
| Hillenbrand 1995 | hand-measured F1–F4 of steady vowels, 139 speakers incl. children | high pitches (girls to 300+ Hz), the exact formant values | connected speech |
| EARS (749 readings), OSF trans/masc (242) | consistency only (the same speaker, high and regular; the authors' LPC size) | breadth of voices | no formant truth |
| The owner's takes 22–25, the rainbow set | Praat at a chosen ceiling; consistency across pitch and microphone | the voices the app is for, four microphones | **no formant truth** |

### 3.2 Truth for the owner's voice: vowel glides

The ambiguity at a high pitch is physics: one steady pitch samples the vocal-tract envelope only at its harmonics, 270–340 Hz apart, so a formant between two harmonics cannot be placed better than about ±F0/2 by any method. The way out is to **move the harmonics across the envelope**: a sustained vowel whose pitch slides slowly (say 150 → 330 Hz and back over 6–8 s) sweeps every harmonic through the formants, and the harmonic amplitudes over time trace the envelope densely (every 10–20 Hz). Read F1–F4 as the peaks of that traced envelope. This is independent of LPC, of any ceiling and of the number of poles; it is what the harmonic sweep in acoustic measurement does. It needs nothing but the owner's voice and the recording.

Protocol (the owner): five vowels (/a/ as in *father*, /i/ as in *see*, /u/ as in *too*, /ɛ/ as in *bed*, /ɔ/ as in *law*), each as a slow glide up and down once (6–8 s), at the "small" and the "large" setting, on the USB microphone and the phone at the same time (as the rainbow set was recorded). About 20 glides, 3 minutes. Also three steady vowels at the low voice (80–110 Hz): there the harmonics are dense enough to read directly. **Close to the microphone, in a quiet room, and loud enough:** the tool reads a formant only where its harmonics stand clear of the noise floor, and the synthetic validation (below) shows that at the rainbow set's microphone distance (the USB floor 31 dB under the voice) the F4 region of every vowel and the F3 of the dark vowels (/u/, /ɔ/) are buried, while a clean recording reads all four. The open vowels (/a/, /ɛ/) give F3 and F4 most surely; keep the tract still during the glide (a tract that drifts by 3 % reports an uncertainty of ±60–80 Hz instead of ±2).

Tool (`research/py/truth_glide.py` in the research clone; RESEARCH-ALGORITHMS §14.1 has the method): the pitch track, then per frame the amplitude and exact frequency of each harmonic k·F0 on a fine spectrum (a harmonic counts only where it stands clear of the noise floor, sits where k·F0 says and follows its neighbouring frames), accumulated as (frequency, amplitude) points across the glide, fitted as a per-frame gain plus one envelope (a robust local regression), the peaks picked on the envelope with a broad baseline removed (the source's tilt moves a peak by under 1 Hz, so no slope correction is needed for the position); output F1–F4 with an uncertainty from the rising and the falling half of the glide and each peak's width. **Validated** on 140 synthetic glides (five vowels, two tract sizes, a drifting tract, a breathy voice, drowned harmonics, each clean and through the four measured microphones): every F1–F4 within 7 Hz on every clean glide, a drifting tract flagged by its uncertainty, buried harmonics left out rather than misread. Through the USB floor at the rainbow set's distance F4 is never read: hence the protocol above.

What it gives: the owner's true F1–F4 per vowel and per setting, hence the owner's true size (F3–F4) at "small" and "large", pitch-independent by construction; the reference to settle whether the owner reads 14 or 17 cm, and whether "large" changed the tract at all. These numbers become fixtures (`Fixtures/glides/`, `OWNER-TAKES.md`) and the per-vowel truth for every prototype.

### 3.3 Truth for connected speech on the owner's own passages

- **Vowel identity per moment** from the known text: a forced aligner (Montreal Forced Aligner or WhisperX, both run on this PC) marks which vowel is spoken when in the Rainbow passage. With the owner's per-vowel formants from the glides, every vowel moment of the rainbow set gets an expected F1–F3 (the owner's own, not a population's), and a reading of F2 = 1100 Hz on an /i/ is simply wrong. This turns the 32 rainbow files into a labelled set at the precision of the glides, without hand-labelling.
- **A consensus oracle:** where three or four independent methods (Praat's Burg sweep, Praat's robust formants, a WORLD/CheapTrick envelope's peaks, a published deep tracker if one runs) agree within 5 %, that moment is truth; where they disagree, it is left out or labelled by hand on a narrow-band spectrogram. Agreement among methods of different kinds is worth more than any one of them.
- **Hand labelling** only for a small audited subset (a few seconds per voice), to check the two above.

### 3.4 Truth for the microphone problem: synthesis through measured channels

The rainbow set gives each device's response relative to the USB microphone (§5). Synthetic voices with known formants (the test synthesizer, or a Klatt-style one with a glottal model), passed through those measured responses and the measured noise floors, are an exact truth for channel robustness: the estimator's error per device on a known tract. Unlimited and free.

### 3.5 Tools on the owner's PC

The PC: 24 cores, 32 GB, an RTX 2060 (6 GB): enough to run every tool below, and to train or fine-tune a small labelling model in minutes if one is wanted. Present: Python 3.11, numpy, scipy, soundfile, librosa, Praat 7.0.02. To add as needed (verify code, licence and that it runs before relying on any): `pyworld` (WORLD: CheapTrick, an F0-adaptive spectral envelope made for high pitches; its peaks are formant candidates for the consensus oracle); `parselmouth` (Praat as a Python library, for the oracle scripts); DeepFormants (Dissen & Keshet 2016, a published deep formant tracker trained on VTR) as one more voice in the consensus oracle, if its code still runs; `torch` (a CUDA build for the 2060) only for that or for a labelling model of our own, trained on VTR, Hillenbrand and synthetic-through-channels audio to label the owner's passages; Montreal Forced Aligner or WhisperX (vowel timing from the text). **None of these goes into the app** (the owner's rule): they make truth and comparisons, and the app's estimator is judged against what they and the glides agree on.

## 4. Prototypes: several kinds, one table

Each prototype is a formant estimator (F1–F4 per 10 ms with a confidence) that reads no pitch, uses no learned model (the owner's rule: signal processing only, explainable), drops into the same place as today's candidates + chooser, and is scored by the harness of §4.5 on every truth set of §3. Build them in the research clone in Python (numpy; speed is not the point yet), the harness alongside.

### 4.1 P1: a tracker over pooled candidates (labelling instead of counting)

Pool the resonances of all five ceilings (and a 12-pole fit), merge near-duplicates, then label F1–F4 per frame by dynamic programming over time (Talkin 1987; Deng et al. 2006 on VTR; Mehta, Rudoy & Wolfe 2012): local cost from bandwidth and from a per-take speaker model (iterated: track, take the take's median F1–F4, re-track), transition cost from frequency change scaled by spectral change; a candidate may be skipped (an extra peak: nasal, subglottal, the 1000 and 2250 Hz peaks of take 24's low voice) and a formant may be missing. Fixes the labelling failures of §8v–§8y directly; the first DP attempt in the research locked wrong assignments because it had no per-take model and no skip moves (RESEARCH-ALGORITHMS §2.3). Cheapest to build (the Burg kernel stays), deterministic.

### 4.2 P2: formants from a spectral envelope, not from poles

Estimate the spectral envelope first, pick its peaks, then track them (P1's DP). Two envelopes:
- **pitch-free:** the true-envelope cepstral method (Röbel & Rodet 2005) with a fixed cepstral order, or a fixed-order smoothed log-spectrum: no F0 anywhere;
- **pitch-adaptive:** CheapTrick (Morise 2015; WORLD), which uses F0 to set the smoothing window so that the harmonic ripple is removed and the envelope between harmonics is interpolated. It reads F0 only to remove F0's imprint: the result is less pitch-dependent, not more. Whether that keeps HIGH-PITCH-PLAN §1 is the owner's call; the plan starts with the pitch-free variant and measures both.

Strength: no pole count, no ceiling, candidates come from the shape of the spectrum; made for high pitches. Weakness: merged formants (F1 and F2 of /u/), bandwidths, and the same harmonic ambiguity at a steady high pitch (the DP's continuity and the speaker model carry it).

### 4.3 P3: the channel out of the spectrum before the fit

Flatten each take's long-term spectral tilt (or the microphone class's measured response, §5) before Burg or before P2, pitch-blind, then P1. Expected to recover much of the headset's and webcam's readings (their losses above 2.5 kHz are smooth) but not the webcam's hole at 1.5–2 kHz. Also the first step of the weight plan (§6).

### 4.4 P4: a model-fitted envelope (analysis by synthesis)

Fit an all-pole model with a *variable* number of resonances (4–6 below 6 kHz, plus optionally one zero for nasality) to the averaged spectrum of each stationary stretch, choosing the number by a complexity penalty (BIC or a minimum-description-length rule): "five or six resonances below 6 kHz" decided by the evidence in the spectrum, not by a fixed pole count. This is the principled form of the ceiling sweep. Heavier than P1–P3 and the hardest to make robust; built only if P1–P3 leave the six-resonance case (take 24's low voice) unsolved. Not a learned model: a fit a person can read.

(A learned estimator in the app was considered and ruled out by the owner, 2026-10-19: machine learning may label and make references, §3.3–§3.5, never produce the app's readings.)

### 4.5 The harness and the decision

One script (`research/py/formants_bench.py`) runs every prototype on: VTR (RMSE, gross, spikes), Hillenbrand (gross per group), the owner's glides (per-vowel error), the rainbow set (vowel-aligned error against the owner's glide formants; cross-device spread per piece; the `usb`–`relay` agreement), EARS (tail, ICC), the owner's takes 22–25 (the HighPitchTests lines), and synthetic-through-channels (error per device); prints one table with the lines of §2 marked met or not. Also the cost (ms per second of audio in Python, as a guide) and whether the method can run live with a lag of under 0.5 s.

Decide by the table in priority order: the best on priority 1 wins; ties go to the simpler one; P3 is combined with the winner if it helps the devices. Expect P1 to be the quick win; P2 is the one most likely to fix high pitches outright; P4 is the reserve for the six-resonance case.

### 4.6 Porting the winner

Into `Acoustics/Formants` (a `FormantTracker` or an envelope estimator; pure C#, scratch buffers, AOT-safe), behind a new `FormantCeilingMethod` so the old path stays for comparison; `LiveAnalysisTests`' live = saved kept (a tracker that runs forward with a lag is fine); the benches and the Pixel 4a A/B; the goldens rewritten once; HANDOFF with the table.

## 5. Devices

Measured 2026-10-19 from the simultaneous rainbow recordings (the ratio of each device's long-term spectrum to the USB reference's over the loud frames, normalised at 0.3–1 kHz, mean over the eight performances; dB):

| Band (kHz) | 1–1.5 | 1.5–2 | 2–2.5 | 2.5–3 | 3–3.5 | 3.5–4 | 4–5 | 5–6 |
|---|---|---|---|---|---|---|---|---|
| relay (phone over the network) | −2 | −4 | −3 | −4 | −6 | −7 | −10 | −15 |
| headset (WH-1000XM5 hands-free, 16 kHz) | 0 | −2 | −3 | −4 | −6 | −9 | −11 | −14 |
| webcam (Venus USB2.0) | −5 | **−16** | −10 | −1 | −1 | **−13** | **−21** | −20 |

The phone and the headset are smooth losses above 2.5 kHz, 10–15 dB by 5 kHz: F3 and F4 are there, weakened, and an LPC fit below a 5–6.5 kHz ceiling spends its poles on the louder low band. The webcam has a 16 dB hole at 1.5–2 kHz, exactly where F2 lives, which is why its F2 reads about 1100 Hz on every file whatever the voice. Noise floors: the webcam's 22–27 dB under the voice, the others' 30–45 dB.

The plan, in the owner's priority: (1) the estimator is chosen for the USB and the phone; (2) P3's channel normalisation (and the winner's own robustness, measured on the synthetic-through-channels set of §3.4) is measured on the headset and the webcam with the same harness, and the secondary lines of §2 say how far it gets; (3) where a microphone physically removes the information (F4 at the edge of a 16 kHz hands-free codec; a hole across F2), the app says so rather than guessing: a recording-quality note from NOISE-PLAN's measures (band limit, noise floor), never a silent fallback. The rainbow set is the test; its cross-device lines are red until (2) is done, and that is the intended state.

## 6. Weight, after the formants

The weight (corrected H1*–A3*) depends on the formants (its correction) and on the channel (§8z: a 12 dB spread across microphones for one voice; the webcam puts the owner's heaviest take at 4.4 dB). Two things must happen first: correct F1/F3 (this plan), and a channel-free spectrum (P3, per-take long-term normalisation). Then: whether the corrected H1*–A3* on the normalised spectrum separates the owner's heavy and light takes on every microphone (target: within 2 dB across devices, the heavy take ≥ 3 dB above the light); alternatives measured beside it (H1*–H2*, CPP, a spectral-tilt slope over 0.3–3 kHz); the sign convention settled (the UI says higher = heavier and the owner's heavy takes read higher, the synthetic test from §8v calls a brighter source heavier and expects it lower: one of them is wrong); and the weight's own pitch dependence (RESEARCH-ALGORITHMS §4.4). Its own tests on the rainbow set and the owner's pairs. Not before the formants are done.

## 7. Sequence

| Phase | Work | Done when |
|---|---|---|
| A. Truth | the glide protocol recorded by the owner; `truth_glide.py` validated on synthetic glides (**done**), run on the owner; forced alignment of the rainbow set; the consensus oracle (**done**); synthetic-through-channels set (**done**); the harness (**done**) | `Fixtures/glides` with the owner's F1–F4 per vowel and setting; every rainbow moment carries a vowel and an expected F1–F3; the harness runs on all of it |
| B. Prototypes | P1 (**in the table**, four variants, RESEARCH-ALGORITHMS §15), P2 (both envelopes), P3 in the research clone, P4 only if needed; the harness table | one table; the winner chosen in priority order |
| C. Port | the winner into Acoustics; chooser retired or kept as a candidate source; live = saved; phone A/B; goldens | HighPitchTests and the `usb`/`relay` RainbowTests green; VTR and Hillenbrand lines met |
| D. Devices | P3 on the headset and the webcam; the quality note | the cross-device RainbowTests green or the note in place where the information is gone |
| E. Weight | §6 | its tests green on the rainbow set |

A is the critical path and starts now; B can start P1 in parallel on the existing truth (VTR, Hillenbrand) while A produces the owner's.

**Status (2026-10-20, 03:00):** A's tools are built and validated (the glide tool, the synthetic sets, the consensus oracle, the harness; RESEARCH-ALGORITHMS §14); what A still needs is the owner's glides (§8) and then the forced alignment of the rainbow passages (not started: it waits for the glides, which give it something to align to). The harness's first table (RESEARCH-ALGORITHMS §14.4; `research/results_fbench_fast.txt`, `results_fbench_corpora.txt`) holds Praat at 5500 and 5000 Hz, the numpy Burg, the app's three choosers (`Steady`, v0.4.0's smoothness, the 5500/5000 of v0.3.0) and the first tracker prototype (P1, four variants) on every set. What it changed in this plan's picture:

- **No estimator meets priority 1**, as expected; the nearest is today's `Steady` chooser, which is the best baseline on every hand-labelled corpus (VTR women's F3 RMSE 180 Hz against the line's 150, Hillenbrand men and women met, EARS women's p90 met) and on the synthetic voices through the USB floor (F3 gross 2.8 %, met). §1's "a chooser cannot beat its candidates" is true where the owner's voice is concerned (take 24's two readings, take 25, the USB rainbow files read at 16–17 cm where Praat at 5500 and the oracle read 14–15) and for children (Hillenbrand girls' F3 gross 6 %), not in general. The winner must keep the chooser's strength on the corpora and fix the owner's case; the lines of §2 already say so.
- **A fixed 5500 Hz** is right only where the voice fits it (v0.3.0's consistency on the owner was that); **v0.4.0's smoothness** is the best on VTR men and the worst on the owner and on EARS women, on every set at once.
- **P1** (a tracker over the pooled poles of six LPC fits) is the only estimator reading one size per voice on the owner (0.1 cm between take 24's voices, 0.6 on take 25) and meeting EARS women's two tail lines, with the steadiest lines; it fails men and long tracts (VTR men's F3 RMSE 480–525 Hz in every variant) because the pooled candidates are poor there: four of its six fits have five poles for six resonances. Its later variants (a vowel-free prior, F3/F4 from the take's formant spacing) are the best of any estimator on women and girls (Hillenbrand women's F2/F3 gross 0.5/0.9 %, EARS women's tails met) and change nothing for men. Better candidates (fits matched to the tract, or an envelope's peaks) are the next step for P1; P2 is the estimator the glide line (F3 within 80 Hz on 14–39 % of the synthetic glides for every LPC-based estimate) calls for.
- The consensus oracle must agree on F1–F3 together (agreement on one formant alone labelled an /i/'s F2 as "F3" in all three methods); it covers 10–15 % of a passage's frames.

## 8. What the owner provides

- The glides (§3.2): 20 vowel glides plus three steady low vowels, on the USB microphone and the phone at once; nothing else is needed for the owner's truth.
- Which device they and their users mostly record on (phone in the app, a headset, a laptop), to anchor priority 1.
- Later, more passages with the better labels they offered; and a ruling on §4.2's pitch-adaptive envelope.

## 9. Risks and open questions

- The glides may show that the owner's "large" and "small" settings don't change F3–F4 (the rainbow set suggests so, §8z): then the size the app shows is right to be the same, and the owner's sense of "large" lives in F1/pitch/weight; worth knowing before tuning anything.
- A tracker (P1) can still lock a wrong label across a whole take; the per-take model and the glide truth are the guard, and P2 / P4 the alternatives.
- The published deep trackers may not run or may be licensed in ways that forbid even oracle use; nothing in this plan depends on them.
- GitHub LFS bandwidth: every clone of the repo now pulls about 140 MB of fixtures; fine for the owner, worth a thought before a public release.
