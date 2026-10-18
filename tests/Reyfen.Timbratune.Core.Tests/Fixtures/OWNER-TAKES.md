# The owner's own takes (fixtures)

Real recordings of the owner's voice, committed as test fixtures on their decision (2026-10-18). They are the
first regression test for the formant ceiling and perceived size (HIGH-PITCH-PLAN.md): the research benches
(EARS, VTR, Hillenbrand, OSF trans/masc) contain no voice like this one, and v0.4.0's ceiling chooser failed on
it (HANDOFF §8v).

The owner can record more, over a wide range of pitch, size and weight, but can't control each aspect separately
or say where a take stands: they can only say what they were **trying** to achieve. So each take carries the
owner's intent as they stated it, and the measured values are filled in by the agent from the reference
measurements below (never from the app's own, possibly wrong, output). A take's expected values in the tests come
from these references, not from the app.

| File | Recorded | Owner's intent | Reference pitch (Praat ac / cepstral medians) | Reference F1 / F2 / F3 (Praat, 5500 Hz, app's vowel rows) | Reference size (F3–F4 at 5500 Hz) | HNR | Notes |
|---|---|---|---|---|---|---|---|
| `owner_take22.wav` | 2026-10-18 00:38, phone, 18.5 s | *(not stated; to fill in)* | 265 / 262 Hz | 323 / 1725 / 2897 Hz | 13.6 cm | 14.5 dB | v0.3.0 read 329 / 1721 / 2932; v0.4.0 308 / 1660 / 2846, 13.6 cm (25 % of the vowel rows at 4500 Hz); the plateau scoring (HANDOFF §8w) 13.5 cm, mostly 6000 Hz |
| `owner_take23.wav` | 2026-10-18 00:39, phone, 34.6 s | *(not stated; to fill in)* | 276 / 277 Hz | 316 / 1658 / 2757 Hz | 14.4 cm | 8.3 dB | v0.3.0 read 320 / 1560 / 2756; v0.4.0 304 / 1319 / 2331, **18.1 cm** (48 % of the vowel rows at 4500 Hz: the failure); the plateau scoring (HANDOFF §8w) 14.4 cm, mostly 5500 Hz |
| `owner_take24.wav` | 2026-10-18 11:28, phone (v0.4.0 plateau build), 92.4 s, trimmed here to the first 56 s (voice ends at 54 s) | *(not stated: a high voice for 12 s, then a low one; the owner saw the lines "jumping up and down")* | 0–12 s: 243 Hz (Praat ac; p10–p90 217–297); 12–54 s: 82 Hz (75–91) | high voice, Praat 5500 Hz per 3 s piece: F3 2737–2820 Hz; low voice: see the table below (two readings) | high voice about 14.5–15 cm; low voice 20–21 cm by the F3–F4 formula on the 4500–5000 Hz reading | 9.7 / 7.8 dB (Praat cc means, high / low) | the plateau scoring (§8w) hopped between the low voice's two readings at nearly every pause, 18 changes of ceiling in 54 s (F3 2176–2730 Hz, size 14–21 cm, piece to piece); `CeilingScoring.Steady` (HANDOFF §8x) holds the low voice's reading from 15 s on: F3 2180–2332 Hz, 20–21 cm. **The owner's verdict (2026-10-18): size must not depend on pitch; the low voice is the same instrument and must read the same size as the high one (about 14 cm: F3 ≈ 2750, F4 ≈ 4350, F5 ≈ 5800 Hz are present in both voices).** Not met yet: the tests still pin the 20 cm reading. Recorded at full scale: the WAV clips (peak 1.00) |
| `owner_take25.wav` | 2026-10-18 13:45, Windows PC microphone through the blind build, 33.5 s | feminine 0–15 s and 24–33 s (Praat 214–271 Hz by 3 s piece), masculine 15–24 s (82–133 Hz); the owner found the old analysis better in places and the new one in others | 217 / 214 / 255 / 257 / 227 ‖ 82 / 111 / 133 ‖ 217 / 271 / 243 Hz (Praat ac, 3 s medians) | no plateau anywhere in Praat's sweep: F3 at 5000 / 5500 / 6000 Hz differs by 250–300 Hz in every feminine piece (table below) | Praat 5500 Hz: 14.6–15.1 cm feminine, 15.3–16.3 masculine; 6000 Hz: 13.2–13.8 / 13.7–15.3 | 13.6 dB (app) | a different microphone from the phone: the loud spectrum is flat to 7 kHz (the phone takes fall 15 dB by 4–5 kHz) and the noise floor is about 10 dB higher at 4–7 kHz; `Steady` and `Plateau` agree except at 6–12 s; v0.3.0 and Praat at 5500 Hz agree by construction |

What makes this voice hard (HANDOFF §8v): median F1 about 300 Hz at a 270 Hz pitch (F1/F0 1.1, so the LPC sits on
the first harmonic; no EARS high-pitch take has F1 under 350 Hz), sparse harmonics, and a low HNR on take 23.

Reference values were measured with Praat 7.0.02 (`To Formant (burg)`, 5 formants, 25 ms, 50 Hz pre-emphasis) at a
fixed 5500 Hz, 6000 Hz and 6500 Hz: the three agree on F3 within 40 Hz (take 22) and 100 Hz (take 23) and the
v0.3.0 build agrees with them, while 4500 Hz gives "F3" near 2000 Hz on both (the extra-pole label slip). The size is
the app's own formula (ΔF from F3 and F4, VTL = 35000 / 2ΔF) on Praat's 5500 Hz tracks.

## Graph-line references: Praat per 3 s piece and per moment

The cards above are medians over a whole take, and a median can hide a line that drops by hundreds of Hz for a few
seconds at a time (v0.4.0 on take 23). `HighPitchTests`' graph-line tests check the saved F2 and F3 **series** (what
the take's graphs draw) against Praat on the same moments, at fixed ceilings of 5500 and 6000 Hz (the right bracket
for this voice; the right one moves between the two within a take).

- **Per moment:** `owner_take2*.praat.tsv` hold Praat's F2 and F3 at 5500 and 6000 Hz (and, since take 24, 4500 and
  5000 Hz) on every 10 ms row of the take (the graph's grid, t = k × 0.01 s), made by `tools/ceiling-bench/praat-tracks.praat`:
  Praat 7.0.02, `To Formant (burg)`, 5 formants, 25 ms window, 50 Hz pre-emphasis, 1 ms time step, each row read with
  `Get value at time` (linear).

  ```powershell
  tools/praat/Praat.exe --run --no-pref-files --utf8 tools/ceiling-bench/praat-tracks.praat <take>.wav > <take>.praat.tsv
  ```

  Praat's own two readings differ by more than 300 Hz on 17 % (take 22) and 21 % (take 23) of the graph points, so the
  per-point test compares with the nearer of the two.
- **Per piece** (pinned in `HighPitchTests.PraatPieces`): Praat's medians from those rows over each 3 s piece's graph
  points (the app's F3 points with the plateau scoring; pieces with fewer than 50 points are left out), next to what
  the app's lines read there, measured 2026-10-18:

| Take | Piece | Points | Praat 5500 Hz: F2 / F3 | Praat 6000 Hz: F2 / F3 | Plateau (default): F2 / F3 | Smoothness (v0.4.0's choice): F2 / F3 |
|---|---|---|---|---|---|---|
| 22 | 0–3 s | 62 | 1701 / 2779 | 1753 / 2821 | 1771 / 2831 | 1603 / **2229** |
| 22 | 3–6 s | 82 | 1858 / 2978 | 1852 / 2989 | 1859 / 2993 | 1872 / 2993 |
| 22 | 6–9 s | 101 | 1994 / 2910 | 1994 / 2918 | 2022 / 2919 | 1956 / 2909 |
| 22 | 9–12 s | 160 | 1652 / 2880 | 1719 / 2878 | 1597 / 2738 | **1412 / 2301** |
| 22 | 12–15 s | 115 | 1691 / 2893 | 1908 / 2912 | 1913 / 2926 | 1913 / 2926 |
| 22 | 15–18 s | 109 | 1573 / 2881 | 1567 / 2898 | 1535 / 2890 | 1535 / 2890 |
| 23 | 0–3 s | 70 | 1616 / 2707 | 1652 / 2755 | 1638 / 2728 | **1196 / 1967** |
| 23 | 3–6 s | 118 | 1835 / 2807 | 1820 / 2835 | 1747 / 2796 | **1466 / 2357** |
| 23 | 6–9 s | 124 | 1597 / 2784 | 1663 / 2897 | 1570 / 2725 | **1073 / 1846** |
| 23 | 9–12 s | 103 | 1837 / 2729 | 2031 / 2976 | 1781 / 2678 | **1383 / 2494** |
| 23 | 12–15 s | 136 | 1582 / 2774 | 1655 / 2863 | 1592 / 2771 | 1450 / **2141** |
| 23 | 15–18 s | 108 | 1784 / 2842 | 1838 / 2905 | 1804 / 2831 | 1778 / 2788 |
| 23 | 18–21 s | 94 | 1982 / 2854 | 2015 / 2958 | 1999 / 2837 | 1997 / 2819 |
| 23 | 21–24 s | 131 | 1209 / 2670 | 1393 / 2776 | 1218 / 2667 | **965 / 2402** |
| 23 | 24–27 s | 89 | 1566 / 2796 | 1589 / 2842 | 1570 / 2729 | **1303 / 2264** |
| 23 | 27–30 s | 63 | 1581 / 2849 | 1566 / 2911 | 1562 / 2937 | **1283 / 1933** |
| 23 | 30–33 s | 136 | 1674 / 2667 | 1751 / 2746 | 1670 / 2674 | 1670 / 2662 |

Bold: outside Praat's 5500–6000 Hz band by more than the tests allow (F2 150 Hz, F3 200 Hz). The F3 line's spread
(SD of the piece medians), take 22 / 23: plateau 81 / 80 Hz, Smoothness 315 / 324 Hz (v0.4.0 with its centred
resampling 392 Hz on take 23, v0.3.0 69 Hz). F3 points more than 300 Hz from Praat at the nearer of 5500 / 6000 Hz:
plateau 5.7 / 10.3 %, Smoothness 18.5 / 43.2 %. The app's numbers are the current tree's (anchored resampling,
HANDOFF §8w); they are what the tests' bounds were set against, not references.

### Take 24: the low voice's two readings

The low voice (12–54 s, about 82 Hz) is not like takes 22 and 23. Its long-window spectrum (an averaged 4096-point
FFT over each stretch, after pre-emphasis; an LPC-free look at the envelope) shows the same peaks in every stretch:
about **550, 950–1130, 2150–2350, 2640–3025 Hz, then nothing until 4000–4500 Hz (25–30 dB down) and 5300–5850 Hz**.
Six resonances below 6 kHz. A five-formant analysis can hold the first five at 4500 and 5000 Hz (F1 550, F2 1000,
F3 2250, F4 2750, F5 4400: the **low reading**), while at 6000 and 6500 Hz one pole pair has to serve the 5800 Hz
peak and the 1000 Hz one is lost: F1 700, F2 2250, F3 2800, F4 4300–5400 (the **high reading**). 5500 Hz gives
either, piece by piece. Both readings are self-consistent plateaus, so the plateau scoring's choice rested on which
one 5500 Hz happened to give, and the path hopped between them at nearly every pause (a change of ceiling across a
pause costs 2 against 12 inside a stretch, so every phrase re-decided). With `Steady`, a change of *reading* across a
pause costs by how much further F3 and F4 jump than they would on the best continuation (HANDOFF §8x), so the low voice
keeps its reading from 15 s on; its first 3 s continue the high voice's reading across the 12 s pause, until its own
evidence turns. The two readings differ by about 500 Hz in F3 and 6 cm in
size (the F3–F4 formula reads the 2250/2750 Hz pair as a 20 cm tract); which one a listener hears is the owner's call,
by ear and in the blind build against v0.3.0 (fixed 5500 Hz, which also mixed the two: F3 2403–2869 Hz by piece).
The tests pin the low voice to the **low reading** (Praat at 4500–5000 Hz), the one the spectrum's count supports;
if the owner decides the other is right, `HighPitchTests.PraatPieces["owner_take24"]` changes with that decision.
The high voice (0–12 s, 243 Hz) is checked at 5500–6000 Hz like takes 22 and 23.

Per 3 s piece, Praat's medians over the app's graph points (`Steady`'s) at four ceilings, and what the app's lines read
(F3, Hz), measured 2026-10-18:

| Piece | Points | Praat F3 @ 4500 / 5000 / 5500 / 6000 | Praat F2 @ 4500 / 5000 / 5500 / 6000 | Plateau (§8w): F2 / F3 / size | `Steady`: F2 / F3 / size | v0.3.0 (5500 Hz fixed): F3 |
|---|---|---|---|---|---|---|
| 0–3 s | 99 | 2280 / 2448 / 2762 / 2858 | 1551 / 1765 / 1958 / 2165 | 1744 / 2488 / 16.9 | 1744 / 2488 / 16.9 | 2731 |
| 3–6 s | 103 | 2152 / 2518 / 2827 / 2806 | 1484 / 1584 / 1682 / 1692 | 1719 / 2808 / 14.6 | 1719 / 2808 / 14.6 | 2838 |
| 6–9 s | 137 | 2225 / 2567 / 2812 / 2899 | 1533 / 1678 / 1776 / 1962 | 1701 / 2696 / 18.0 | 1701 / 2696 / 18.0 | 2801 |
| 9–12 s | 151 | 2072 / 2547 / 2737 / 2810 | 1524 / 1635 / 1686 / 1722 | 1695 / 2782 / 15.2 | 1674 / 2728 / 15.7 | 2734 |
| 12–15 s | 173 | 2174 / 2266 / 2691 / 2839 | 1257 / 1423 / 1806 / 2025 | 1580 / 2505 / 18.8 | 1580 / 2505 / 18.8 | 2687 |
| 15–18 s | 102 | 2280 / 2355 / 2660 / 2704 | 1094 / 1348 / 2166 / 2258 | **2033 / 2665** / 15.0 | 1259 / 2332 / 21.1 | 2659 |
| 18–21 s | 80 | 2192 / 2274 / 2585 / 2899 | 1354 / 1528 / 1792 / 1917 | **1750 / 2585** / 17.4 | 1332 / 2180 / 20.8 | 2546 |
| 21–24 s | 172 | 2332 / 2369 / 2425 / 2854 | 1020 / 1041 / 1032 / 2374 | 1022 / 2328 / 20.6 | 1022 / 2328 / 20.6 | 2444 |
| 27–30 s | 68 | 2280 / 2422 / 2804 / 2958 | 950 / 1158 / 2059 / 2165 | **2055 / 2794** / 15.0 | 968 / 2291 / 20.0 | 2804 |
| 30–33 s | 73 | 2193 / 2291 / 2411 / 2759 | 1394 / 1577 / 1751 / 1942 | **1734** / 2413 / 18.0 | 1590 / 2264 / 19.5 | 2403 |

Bold: outside the piece's band (5500–6000 Hz for 3–12 s, 4500–5000 Hz from 15 s) by more than the tests allow. The first
3 s (no plateau in Praat's sweep either) and 12–15 s (where the low voice's own reading takes over from the high
voice's, carried across the pause) are listed, not checked. The SD of the F3 piece medians, high voice (3–12 s) / low
voice (15–33 s): plateau 48 / 165 Hz, `Steady` 47 / 55 Hz (`TheOwnersTake24HoldsOneReadingPerVoice`).
`owner_take24.praat.tsv` holds Praat's F2 and F3 at 4500, 5000, 5500 and 6000 Hz on every 10 ms row (the tsv of
takes 22 and 23 now have those columns too).

### Take 25: the same voice feminine and masculine, through a PC microphone

Per 3 s piece, what the app's lines read with `Steady`, with `Plateau`, with v0.3.0's fixed 5500 Hz, and Praat's medians
over the same graph points at four ceilings (F3 Hz; size cm by the app's F3–F4 formula on Praat's tracks). Measured
2026-10-18:

| Piece (voice) | `Steady`: F2 / F3 / size | `Plateau` | v0.3.0: F2 / F3 | Praat F3 @ 4500 / 5000 / 5500 / 6000 | Praat size @ 4500 / 5000 / 5500 / 6000 |
|---|---|---|---|---|---|
| 0–3 (fem.) | 1566 / 2846 / 15.4 | same | 1556 / 2814 | 2629 / 2826 / 2816 / 3246 | 17.0 / 15.6 / 15.1 / 13.4 |
| 3–6 (fem.) | 1563 / **2703** / 16.1 | same | 1689 / 2980 | 2587 / 2719 / 2973 / 3264 | 17.3 / 16.1 / 14.6 / 13.2 |
| 6–9 (fem.) | 1393 / **2617** / 17.0 | 1414 / 2677 / 16.6 | 1596 / 2935 | 2492 / 2658 / 2951 / 3166 | 18.5 / 16.7 / 15.1 / 13.3 |
| 9–12 (fem.) | 1415 / 2820 / 15.8 | 1509 / 2861 / 15.4 | 1754 / 2972 | 2484 / 2627 / 2988 / 3180 | 18.4 / 17.5 / 14.7 / 13.8 |
| 12–15 (fem.) | 1727 / 3127 / 13.5 | 1735 / 3137 / 13.4 | 1707 / 3133 | 2053 / 2493 / 3121 / 3226 | 20.3 / 17.6 / 13.5 / 13.4 |
| 15–18 (masc.) | 1305 / **2387** / 19.4 | same | 1418 / 2594 | 2258 / 2399 / 2600 / 2821 | 20.8 / 19.2 / 16.3 / 15.3 |
| 18–21 (masc.) | 1642 / 2516 / 15.4 | same | 1688 / 2526 | 2066 / 2463 / 2527 / 2519 | 21.2 / 15.9 / 15.3 / 14.8 |
| 21–24 (masc.) | 1409 / **2563** / 17.5 | same | 1471 / 2842 | 2367 / 2575 / 2809 / 3043 | 19.7 / 17.6 / 16.1 / 13.7 |
| 24–27 (fem.) | 1282 / 2972 / 15.1 | same | 1277 / 2978 | 2428 / 2867 / 2974 / 3170 | 18.1 / 16.4 / 15.1 / 13.4 |
| 27–30 (fem.) | 1387 / 3058 / 14.9 | same | 1378 / 3065 | 2708 / 2920 / 3099 / 3296 | 17.1 / 15.6 / 14.9 / 12.8 |

Bold: the pieces where the new chooser went to 5000 Hz (feminine, 3–9 s) or 4500 Hz (masculine, 15–18 and 21–24 s) and
read F3 200–300 Hz below v0.3.0's 5500 Hz. Praat's sweep gives no plateau on this recording, so Praat can't say which
ceiling is right here; by the owner's principle (size independent of pitch, the same instrument), the feminine pieces
should read like each other (0–3, 12–15 and 24–30 s: 13.5–15.4 cm on both analyses), which makes the 5000 Hz pieces at
3–9 s (16–17 cm) the new chooser's error, and the masculine stretch is the owner's call. No test pins this take yet
(`owner_take25.praat.tsv` holds the per-moment references); the pin is the owner's decision.

## The rainbow set (2026-10-18 evening): eight performances, four microphones each

`Fixtures/rainbow/<performance>_<device>.wav`, 32 files, 124 MB, brought in through the app's own import conversion
(`AnalysisWavSink`: 44.1 kHz mono 16-bit; the relay's two identical channels downmixed, the headset's 16 kHz
upsampled). The text is the Rainbow passage (below; the passage the EARS bench reads too). The owner asked for all of
it to be tests, now and in the repo; `RainbowTests` are those tests. No file clips. The cis woman and the cis man agreed
to their recordings being in this public repository (the owner, 2026-10-19).

The owner recorded each performance on four devices at once, so the four files of one performance are **one voice at
one moment**, and any difference between their readings is the microphone's:

| Device | File suffix | What it is | Signal |
|---|---|---|---|
| USB audio device | `_usb` | a USB microphone on the PC; **the reference** | 48 kHz; loud spectrum to 8 kHz; noise about 30 dB under the voice |
| AudioRelay virtual mic | `_relay` | a phone's microphone relayed to the PC over the network | 48 kHz stereo, both channels identical; agrees with the reference within 0.7 cm of size on every performance |
| Sony WH-1000XM5, hands-free | `_headset` | a Bluetooth headset in its call profile | **16 kHz** (the hands-free codec); noise 32–45 dB under |
| Venus USB2.0 camera | `_cam` | a webcam's microphone | 48 kHz but quiet (−33 dB RMS) with the noise only 22–27 dB under the voice; the spectrum falls off early (99.9 % of the loud energy below about 3 kHz on most files) |

The performances, with the owner's stated intent in the file name (the owner: "labeling can be a bit off"; the cis
woman "a bit more awkward with tiny errors, as a speaker was reading text for the second time"):

| Performance | Who / intent | Praat pitch, reference mic (p10–p90) | Praat HNR (cc, mean) | Length |
|---|---|---|---|---|
| `owner_extrahigh_small_light` | the owner: extra-high pitch, small, light | 314 Hz (294–341) | 21.7 dB | 45 s |
| `owner_high_small_light` | the owner: high, small, light | 224 Hz (201–253) | 13.6 dB | 46 s |
| `owner_high_small_light_breathy` | the same, breathy | 237 Hz (216–262) | 10.2 dB | 49 s |
| `owner_high_large_light` | the owner: high, large, light | 201 Hz (182–235) | 12.3 dB | 44 s |
| `owner_high_large_heavy` | the owner: high, large, heavy | 177 Hz (160–194) | 13.2 dB | 46 s |
| `owner_low_small_heavy` | the owner: low, small, heavy | 99 Hz (84–120) | 11.6 dB | 41 s |
| `cisfem` | a cis woman (another speaker) | 189 Hz (154–259) | 11.7 dB | 58 s |
| `cismale` | a cis man (another speaker) | 95 Hz (80–120) | 8.9 dB | 39 s |

The app's pitch medians agree with Praat's to the Hz on all 32 files. Note what the intents did to the pitch: the
owner's "high" spans 177–238 Hz, lower the "larger" or "heavier" the intent (large light 201, large heavy 177): making
the voice bigger or heavier lowered it, which is the confusion between the three the app is meant to untangle.

**What the app reads (`CeilingScoring.Steady`, 2026-10-19), size in cm by microphone, and Praat's sweep on the
reference microphone** (median F2 / F3 / F4 over the app's vowel rows at each fixed ceiling, with the size the F3–F4
formula gives; the right ceiling for a voice is where these stop moving, and on these recordings they never do):

| Performance | usb | relay | headset | cam | Praat 4500 | 5000 | 5500 | 6000 | 6500 |
|---|---|---|---|---|---|---|---|---|---|
| owner extra-high small light | 16.4 | 16.2 | 16.6 | 16.9 | 1191/2224/3219 → 19.4 | 1276/2451/3729 → 16.9 | 1345/2721/4033 → 15.5 | 1633/3125/4332 → 14.2 | 1884/3476/4828 → 12.8 |
| owner high small light | 17.0 | 17.5 | 15.6 | 17.2 | 1498/2646/3405 → 17.5 | 1515/2641/3456 → 17.5 | 1819/2918/4105 → 14.9 | 2078/3123/4533 → 13.8 | 2431/3474/5013 → 12.5 |
| owner high small light breathy | 17.1 | 16.8 | 16.1 | 17.3 | 1577/2621/3449 → 17.3 | 1581/2633/3516 → 17.1 | 1851/2925/3990 → 15.4 | 2192/3172/4507 → 13.8 | 2365/3509/5024 → 12.5 |
| owner high large light | 17.2 | 17.0 | 15.8 | 17.3 | 1535/2623/3487 → 17.2 | 1459/2599/3455 → 17.5 | 1908/3021/4210 → 14.6 | 2233/3246/4469 → 13.8 | 2513/3889/5338 → 11.5 |
| owner high large heavy | 15.3 | 15.2 | 18.7 | 17.7 | 1106/2358/3107 → 19.4 | 1574/2859/3594 → 16.5 | 2047/3062/3919 → 15.2 | 2532/3350/5157 → 12.3 | 2698/3610/5475 → 11.4 |
| owner low small heavy | 16.9 | 16.3 | 19.9 | 18.1 | 1228/2240/2909 → 20.5 | 1415/2505/3194 → 18.6 | 1653/2736/3741 → 16.3 | 1860/2939/4531 → 13.9 | 2076/3153/4921 → 13.0 |
| cis woman | 15.5 | 15.0 | 15.2 | 16.0 | 1435/2478/3485 → 17.6 | 1561/2757/3924 → 15.7 | 1590/2810/4024 → 15.4 | 1647/2937/4275 → 14.5 | 1785/3208/4521 → 13.6 |
| cis man | 19.0 | 18.4 | 19.6 | 18.1 | 1283/2218/3118 → 19.8 | 1400/2339/3279 → 18.5 | 1568/2683/3960 → 15.7 | 1794/2975/4327 → 14.6 | 1883/3084/4558 → 13.9 |

What the set shows (HANDOFF §8z has the per-piece tables):

- **The microphone changes the formants, and so the size, by up to 3.6 cm.** The reference and the relay agree
  (0.1–0.7 cm); the headset reads the owner's heavy and low voices 3–3.5 cm bigger, the camera 1–2 cm bigger, and
  the camera's F2 card is about 1100 Hz on every file whatever the voice. Piece by piece, the F2 and F3 lines of one
  performance differ between microphones by 300–1300 Hz. Praat at a fixed ceiling shows the same microphone effect
  (its F2 at 5500 Hz on the camera is 1100–1300 Hz against 1600–2100 on the reference), so this is the LPC analysis
  itself following the microphone's spectrum, not the chooser. v0.3.0's fixed choice is as microphone-dependent
  (reference against relay up to 2.9 cm). An app whose users record on phones and headsets cannot leave this as it is:
  `RainbowTests` pin it (size within 1 cm, F3 within 250 Hz, pieces within 300 Hz, weight within 3 dB across the
  four microphones), red until met.
- **Pitch is microphone-independent** within 1–5 Hz, except the camera on the two cis speakers (+5 %: the noisy
  signal voices other frames, and Praat reads the same), which the test records as red.
- **Weight (corrected H1*–A3*) follows the microphone by up to 12 dB** (the owner's heavy voice: 4.4 dB on the camera,
  16.3 on the reference): a spectral tilt read without any microphone correction measures the microphone's tilt as
  much as the voice's. On the two agreeing microphones the owner's heavy intents do read heavier (by 5–10 dB).
- **The owner's rules hold on the agreeing microphones:** the three "small" performances at 314, 224 and 99 Hz read
  within 1.3 cm of each other (16.2–17.5 cm), the breathy take reads a lower HNR (13.6 → 10.2 dB) and the same size,
  the cis man reads 2–4 cm larger than the cis woman on every microphone.
- **The "large" intent is not acoustically larger.** On the reference, Praat at 5500 Hz reads the large-light take's
  F3/F4 at 3021/4210 Hz against the small-light take's 2918/4105: slightly higher, not lower. What changed was the
  pitch (224 → 201 Hz) and F1. Not asserted.
- **No plateau anywhere** in Praat's sweep on these PC recordings (F3 climbs 200–300 Hz per 500 Hz of ceiling on
  every file, as on take 25), and the choice of ceiling alone moves the size by 2–3 cm. The chooser sits at 5000 Hz
  for most of the owner's feminine takes on the reference (60–83 % of the vowel rows), where v0.3.0's 5500 Hz read
  the same takes 2 cm smaller (high small light: 14.8 against 17.0 cm), in line with the owner's phone takes 22/23
  (13.5–14.4 cm).
- **Against the owner's earlier takes:** the same voice read 13.5–14.4 cm from the phone (takes 22/23) and
  16–17.5 cm here from the PC microphones; which is the owner's size is not decidable from LPC at a chosen ceiling.
  A reference that does not depend on the ceiling is needed (sustained vowels, hand-read harmonics).

The passage (`text.txt` as recorded):

> When the sunlight strikes raindrops in the air, they act as a prism and form a rainbow. The rainbow is a division of
> white light into many beautiful colors. These take the shape of a long round arch, with its path high above, and its
> two ends apparently beyond the horizon. There is, according to legend, a boiling pot of gold at one end. People look,
> but no one ever finds it. When a person looks for something beyond their reach, their friends say they are looking for
> the pot of gold at the end of the rainbow. Throughout the centuries people have explained the rainbow in various
> ways. Some have accepted it as a miracle without physical explanation

References were measured 2026-10-19 with Praat 7.0.02: pitch `To Pitch (ac)` 60–600 Hz (median, p10, p90), HNR
`To Harmonicity (cc)` mean, formants `To Formant (burg)` 5 formants, 25 ms, 50 Hz pre-emphasis at 4500–6500 Hz on the
app's vowel rows. Per-moment Praat tracks (`praat-tracks.praat`) are not kept in the repo for this set (8 MB more);
regenerate them when a per-point test needs them.

**Adding a take:** copy the WAV here as `owner_<n>.wav` (mono 44.1 kHz 16-bit, what the app records), add a row with
the owner's stated intent, measure the references with Praat at 5500–6500 Hz and the cepstral pitch check
(`tools/ceiling-bench`), write its per-moment Praat rows with `praat-tracks.praat`, and pin the expected ranges and
piece medians in `HighPitchTests`. The repository is public: only the owner
decides what is committed.
