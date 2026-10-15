# Noise and microphone robustness plan

*Status:* investigated and measured on 2026-10-15; nothing has been implemented in the app yet.
*Base:* `dev/v0.2.1` at `5807298`.
*Tools:* `tools/noise-bench/` (new, not committed yet).

Different users record with different microphones and in different places: wind, rumble, fans, hum, other people talking, echoey rooms. Today the same performance can get very different numbers depending on the mic and the room. This plan says what we found, what was measured, and what to build, in order.

## How to use this document (for agents)

- **Read first:**
  - `CLAUDE.md` for the ground rules: commit only when asked, no Praat code, cross-platform, never touch the user's takes or running app, measure before claiming.
  - `HANDOFF.md` §5 (live analysis, live = saved) and §6a (loudness gate) before touching analysis.
  - `REFACTOR-PLAN.md` §2–§3. It moves the analysis into tracks and modules, and its phases need identical results before and after each step. See §7 below on how the two plans fit together.
- **Order:** do the phases in §5 in order. Each phase ends green: build, tests, the bench numbers in its acceptance line, and the performance gates for anything on the live path.
- **Measure:** every phase has acceptance numbers from `tools/noise-bench`. Re-run the bench against the **C# implementation**, not the Python prototype, and record the results in `HANDOFF.md` (a new section, like §6a).
- **Algorithms:** implement from the papers in §6 and add them to `src/Reyfen.Timbratune.Acoustics/REFERENCES.md`. `tools/noise-bench/denoise.py` is a rough prototype showing the idea; don't port it line by line. Don't use native libraries (RNNoise, WebRTC, SpeexDSP and the like): the Acoustics project stays pure C#, zero-dependency and trim/AOT safe.
- **If reality contradicts this plan,** stop and ask the owner.

---

## 1. What started it: one performance, two microphones

The user recorded the same performance on two mics. The files are in the user's Downloads and are their own voice: **don't copy them into the repo**, and test only on copies outside it.

| | A: `voice-take-17.wav` | B: `voice-take-17 (2).wav` |
|---|---|---|
| Real format | 44.1 kHz 16-bit PCM | **WebM/Opus recorded by Chrome, named `.wav`** (the importer fails on it; it was converted with ffmpeg) |
| Level | peak −18 dBFS | peak −5 dBFS |
| Background | about 24 dB below the voice; strong rumble at 20–40 Hz (−14 dB of the file's energy) | Chrome noise suppression: quiet parts gated to near silence |
| Mean / median pitch | **140.4** / 102.2 Hz | **102.1** / 101.0 Hz |
| In register | **14.3 %** | **0.7 %** |
| Weight (H1–A3c) | **6.5 dB** | **16.0 dB** |
| HNR / shimmer | 8.8 dB / 14.5 % | 12.5 dB / 9.1 % |
| F2 | 1068 Hz | 1196 Hz |

The user says B is much closer to reality. Findings:

1. **Pitch is the same where both are voiced** (349 shared frames, median ratio 1.00). A's 14 % "in register" comes from 69 stray frames at 200–513 Hz, about 10–20 dB below the voice, where B has no voicing. The cause is low-frequency rumble plus a quiet input: the pitch tracker's silence threshold is 3 % of the file's global peak, and A's noise is about 3.5 % of its peak, so noise frames pass. **A 50 Hz high-pass alone removes all of them** (A becomes 102.3 Hz mean, 0.2 % in register).
2. **Weight was mostly noise, not the mic.** Noise reduction moved A's uncorrected H1–A3 from 30.9 to 36.0 dB (B: 36.2). Part of the remaining gap (F2, spectral tilt −7.7 vs −3.6 dB/kHz) is the mic's frequency response, which no noise filter can remove.
3. **HNR and shimmer** are limited by A's noise. They move about halfway toward B with noise reduction. B is also processed by Chrome, so it is not a true reference either.
4. **The VCTK test fixtures have the same rumble.** They carry −22 dB of their energy below 40 Hz, and the clean male takes have stray frames up to 488 Hz (m311 pitch SD 35 Hz → 15 Hz after a 50 Hz high-pass). This affects every user, not just noisy ones.

## 2. The noise bench (`tools/noise-bench/`)

See its `README.md`. In short:

- `make_noisy.py` makes 252 test files from the 4 VCTK fixtures:
  - **11 additive noises:** white, pink, rumble, wind (gusty, below 400 Hz), 50 Hz hum, 60 Hz hum, fan (pink + blade tone + whine), babble, one competing talker, keyboard clicks, street. Each at **SNR 30 / 20 / 10 / 5 / 0 dB**.
  - **7 channel effects:** room echo with RT60 0.3 s and 0.8 s, phone band 300–3400 Hz, laptop mic (no bass, presence bump), proximity effect (+8 dB below 250 Hz), clipping, very low level (−45 dBFS).
  - All synthetic and seeded, so runs repeat exactly.
- `denoise.py` contains the candidate clean-up chains (Python prototypes).
- `Program.cs` runs Timbratune's full analysis on every WAV in parallel and writes a CSV. About 130 s for 1,764 files on 24 cores.
- `score.py` compares each file's take metrics with its clean take:
  - The clean reference is the clean take after the 50 Hz high-pass, because of finding 4.
  - Each metric's error is divided by a tolerance and capped at 5. Tolerances: pitch mean 3 %, median 2 %, in-register 5 points, F1 6 %, F2/F3 5 %, HNR 2 dB, jitter 0.5 points, shimmer 2 points, weight 2 dB.
  - **Score** = mean over the metrics. 0 = same as clean, 1 = off by about one tolerance on average.

Generated audio is about 3 GB; keep it outside the repo.

## 3. Bench results (Python prototypes, 2026-10-15)

Score by noise type (mean over 4 voices × 5 SNRs; lower is better):

| Chain | All noise | Wind | Rumble | Fan | Hum 50 | Hum 60 | Pink | White | Keyboard | Babble | Talker | Street | Clean input |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| none | 1.05 | 0.72 | 1.07 | 1.16 | 1.03 | 0.90 | 0.93 | 1.02 | 0.53 | 1.63 | 1.26 | 1.35 | 0.22 |
| hp50 | 0.86 | **0.31** | 0.56 | 1.07 | 0.78 | 0.76 | 0.80 | 0.90 | 0.42 | 1.47 | 1.14 | 1.22 | **0.00** |
| **hp50 + LSA, floor −15 dB** | **0.65** | 0.32 | 0.39 | 0.56 | 0.43 | 0.48 | **0.40** | **0.32** | 0.38 | 1.43 | 1.13 | 1.30 | **0.00** |
| hp50 + LSA −20 | 0.67 | 0.32 | 0.41 | 0.55 | 0.42 | 0.49 | 0.49 | 0.40 | 0.38 | 1.43 | 1.14 | 1.31 | 0.00 |
| hp50 + LSA −25 / −30 | 0.67 | 0.32 | 0.41 | 0.56 | 0.42 | 0.51 | 0.50 | 0.42 | 0.38 | 1.44 | 1.14 | 1.31 | 0.00 |
| hp50 + LSA −20, minimum statistics (causal) | 0.68 | 0.34 | **0.37** | **0.47** | 0.56 | 0.64 | 0.44 | 0.39 | 0.41 | 1.45 | 1.15 | 1.30 | 0.12 |
| hp50 + spectral subtraction | 0.69 | 0.31 | 0.50 | 0.69 | 0.52 | 0.52 | 0.43 | 0.34 | 0.37 | 1.47 | 1.13 | 1.36 | 0.00 |
| hp50 + Wiener −25 | 0.71 | 0.34 | 0.44 | 0.57 | 0.45 | 0.52 | 0.61 | 0.59 | 0.42 | 1.43 | 1.14 | 1.31 | 0.00 |
| hp50 + OM-LSA −30 (simplified) | 0.77 | 0.35 | 0.45 | 0.72 | 0.56 | 0.62 | 0.69 | 0.75 | 0.46 | 1.46 | 1.14 | 1.25 | 0.00 |
| hp70 + LSA −20 | 0.65 | 0.32 | 0.41 | 0.44 | 0.40 | 0.46 | 0.45 | 0.38 | 0.41 | 1.44 | 1.13 | 1.31 | 0.08 |

By SNR, for hp50 + LSA −15: 30 dB 0.07, 20 dB 0.25, 10 dB 0.67, 5 dB 0.94, 0 dB 1.32 (none: 0.27 / 0.47 / 1.04 / 1.49 / 2.00).

Channel effects: no chain helps. Per-metric errors for hp50 + LSA −15:
- **Weight:** laptop mic 3.9, phone band 4.8, proximity 2.3 (in tolerances).
- **Shimmer / HNR under echo:** RT60 0.8 s gives shimmer 4.7 and HNR 3.2.
- **Pitch under babble / talker / street:** about 2.5–2.8. The tracker follows the other voice.

On the user's take pair, hp50 + LSA −15 gives:

| | Mean pitch | In register | Weight | HNR | Shimmer |
|---|---|---|---|---|---|
| A | 102.4 Hz | 0.2 % | 9.9 dB | 11.2 dB | 11.9 % |
| B (essentially unchanged) | 101.9 Hz | 0.7 % | 16.3 dB | 12.0 dB | 9.0 % |

Conclusions:
- **A 50 Hz high-pass should always be on.** It is free, it makes clean input better, and it is the biggest win for wind and rumble. A 70 Hz cut helps fan and hum a little but changes clean takes (0.08), and the pitch floor is 75 Hz, so stay at 50.
- **LSA is the noise reduction to build.** The floor between −15 and −30 dB barely matters; start at −15.
- **The causal noise tracker** (needed for live) costs 0.12 on clean input as prototyped. The real Martin (2001) algorithm, with its bias compensation, should do better. Tune it until clean input is ≤ 0.03.
- **Not fixable by filtering:** other voices, echo and mic response. They need the separate measures in phases N4–N6.
- **Caveat:** 4 voices, synthetic noise, take summaries only (not the live dots). The prototype OM-LSA's speech-presence estimate was crude, so the method itself isn't ruled out.

## 4. Design

### 4.1 Where the clean-up goes

Put a **signal-conditioning stage** in front of the analysis: high-pass, then noise reduction, then the existing tracks. Rules:

- **`take.wav` stays raw.** Only the analysis input is conditioned, so a take can be re-analysed with other settings later.
- **Live = saved must still hold** (`LiveAnalysisTests`, HANDOFF §5). So the saved analysis runs **the same causal, streaming conditioner from sample 0** as the live one, not a whole-take version.
  - That is why the causal noise tracker has to be good enough, even though the whole-take "quietest 10 %" estimate scored slightly better.
  - The on-stop trim (≤ 10 ms) and the frame-grid alignment have to keep working. The conditioner's delay (high-pass: none in frames; STFT: about one window, ≈ 23 ms at 44.1 kHz) must be compensated, so frame times stay the same.
- **One switch for the analyze.py parity tests.** The Core parity tests compare with `analyze.py` output. Add an engine option, like `LoudOutlierFraction`, that turns conditioning off, and use it in those tests. Don't regenerate the expected files.
- **Record the settings in `take.json`**, in the `analysis` block, for example `"conditioning": {"highpass_hz": 50, "noise_reduction": "lsa", "floor_db": -15, "noise_tracker": "minstat"}`.
  - Takes without it were analysed raw.
  - Note in HANDOFF that old and new numbers aren't exactly comparable, as §6a did.
- **Kernels go in `Reyfen.Timbratune.Acoustics`:**
  - `Filters/` for the biquad high-pass, with streaming state;
  - a streaming STFT noise suppressor with pooled buffers;
  - no allocations per tick, under the REFACTOR-PLAN §3 rules.

### 4.2 Pitch: stray voicing

Even after filtering, faint noise can be tracked as voice. In the shared pitch post-processing, drop a voiced segment only when **all three** hold:

- it is **short** (under 150 ms);
- it is **weak** (median loudness more than 10 dB below the vowel-core reference; reuse the HANDOFF §6a gate);
- it is **far** from the take's pitch (more than 0.7 octave from the median of the long segments).

Split voiced runs at jumps over 0.5 octave first. Because all three must hold, real slides and loud high notes stay.

Prototype result on take A without any filter: in-register 14.3 % → 0.2 %, B unchanged. The rule must be causal enough for live, or applied identically in both paths.

### 4.3 Recording quality, measured and shown

Per take, compute and save in `take.json`:
- estimated SNR (active speech level against the noise floor of the pauses);
- peak level;
- clipped sample share;
- low-frequency energy share;
- **gating detected**: pauses at digital silence, which means the system already applied noise suppression;
- bandwidth: the highest frequency with real energy (phone or Bluetooth band, Opus).

In the take view, show one plain-language note when something is off, like the existing "formants are shaky" note. Thresholds come from the bench: by SNR, about 10 dB is where even the best chain reaches about 0.7. Name the metrics affected (for example "HNR and shimmer are less reliable").

### 4.4 Recording-time help (improve the input, not only the analysis)

- **Room check:** before or at the start of recording, measure about 1–2 s of silence and report the background level, with advice ("move away from the fan", "fine").
- **Level coaching:** a quiet input (take A peaked at −18 dBFS) loses SNR that no filter recovers. Show when the voice is too quiet or clipping, and suggest moving closer.
- **Ask the OS for raw audio:** platform noise suppression differs per device, and that difference is part of why A and B disagree.
  - Check what SoundFlow/miniaudio requests today. On Android, find the AAudio input preset: `UNPROCESSED`, falling back to `VOICE_RECOGNITION`. Nothing in `src/` sets one explicitly.
  - Our own conditioner is the same everywhere.
- The room check can also seed the noise tracker, so the first second of a live take isn't spent learning the noise.

## 5. Phases and checklist

Each phase ends with: build, `dotnet test Timbratune.slnx` green, the bench run against the C# code with its numbers in HANDOFF, and the REFACTOR-PLAN §3 performance gates for anything that runs live (time +3 % at most, no new allocations; Pixel numbers with the Release profiling build).

- [ ] **N0. Bench in the repo.** Commit `tools/noise-bench` (when the owner asks).
  - Add a mode to `Program.cs` that runs the engine **with C# conditioning on raw files**, so the bench measures the real implementation.
  - Optionally score the take-17 pair locally, outside the repo.
- [ ] **N1. High-pass.**
  - 4th-order Butterworth at 50 Hz, causal, streaming, in Acoustics `Filters/`.
  - Applied in front of both saved and live analysis, with the parity-test switch and `conditioning` in `take.json`.
  - *Acceptance:*
    - bench, C# vs Python hp50, within ±0.02 per noise type;
    - clean = 0.00 against the hp50 reference;
    - take A: in-register ≤ 1 %, mean pitch within 2 Hz of B;
    - `LiveAnalysisTests` green; performance gates met.
- [ ] **N2. Stray-voicing cleanup** (§4.2).
  - Add it to the bench as a scored option: it runs inside the analysis, so add a flag to `Program.cs`.
  - *Acceptance:* the pitch columns improve or stay the same in every noise type; clean unchanged (≤ 0.01).
- [ ] **N3. Noise reduction.**
  - Streaming LSA (Ephraim & Malah 1985) with decision-directed a-priori SNR (1984). Gain floor −15 dB.
  - Noise PSD from minimum statistics (Martin 2001, with bias compensation), optionally seeded by the room check.
  - STFT: Hann window, about 23 ms (1024 samples at 44.1 kHz, scaled for other rates), hop ¼.
  - *Acceptance:*
    - all-noise score ≤ 0.68 (the Python causal version) and ideally ≤ 0.65;
    - clean ≤ 0.03;
    - no noise type worse than hp50 alone;
    - take A weight and HNR move toward B, B changes by ≤ 0.5 dB;
    - performance gates on both Pixels. If the 4a misses its budget, ask the owner (options: run it only in the saved analysis and accept live ≠ saved for those metrics, or a cheaper gain).
- [ ] **N4. Recording-quality measurements and note** (§4.3), with thresholds from the bench.
- [ ] **N5. Recording-time help** (§4.4): room check, level coaching, raw OS input.
- [ ] **N6. Later, separate investigations** (each needs its own bench extension first):
  - **Per-mic correction for weight and formants.** Save the input device name per take. Estimate each mic's response from the long-term average spectrum of the user's own takes, compared with their reference mic. Correcting each take on its own would erase the real changes weight is meant to show.
  - **Competing voices** (babble, talker): keep the pitch track on the main voice, using continuity and loudness; at least warn.
  - **Echo:** dereverberation, for example weighted prediction error (Nakatani et al. 2010), or at least a warning.
  - **Import:** detect the format from the file's contents, not its name (a WebM named `.wav` must give a clear message or be decoded). This belongs to REFACTOR-PLAN P4 (sniffer).

## 6. References to add to `REFERENCES.md`

- S. Butterworth (1930), "On the theory of filter amplifiers"; any DSP textbook for the bilinear-transform biquad design.
- Y. Ephraim, D. Malah (1984), "Speech enhancement using a minimum mean-square error short-time spectral amplitude estimator", *IEEE Trans. ASSP* 32(6). Decision-directed a-priori SNR.
- Y. Ephraim, D. Malah (1985), "Speech enhancement using a minimum mean-square error log-spectral amplitude estimator", *IEEE Trans. ASSP* 33(2). The LSA gain.
- R. Martin (2001), "Noise power spectral density estimation based on optimal smoothing and minimum statistics", *IEEE Trans. Speech and Audio Processing* 9(5).
- For comparison only:
  - M. Berouti, R. Schwartz, J. Makhoul (1979), "Enhancement of speech corrupted by acoustic noise", *ICASSP*;
  - I. Cohen, B. Berdugo (2001), "Speech enhancement for non-stationary noise environments", *Signal Processing* 81(11) (OM-LSA).

## 7. How this fits with REFACTOR-PLAN.md

The refactor (not started; work branch `dev/v0.3.0`) needs **identical results** before and after each analysis phase. This plan deliberately **changes** results. Don't mix them inside one phase.

- **Open decision for the owner:** do N1–N3 **before** the refactor's P0, so its baselines include the conditioner, or **after** P7, as a new track (`ConditionedSignal`, stage 0 before stage 1) in the new structure. Before is simpler and gets the fix to users sooner. After avoids implementing the hookup twice.
- Either way, the conditioner's kernels live in Acoustics and don't depend on the analysis structure.

## 8. Open questions for the owner

1. Order relative to the refactor (§7).
2. Should old takes be re-analysed with conditioning (a one-off action in the take view), or keep their numbers?
3. If N3 misses the Pixel 4a budget: should noise reduction apply only to the saved analysis?
4. Is a short room check before every recording acceptable, or only on demand?
