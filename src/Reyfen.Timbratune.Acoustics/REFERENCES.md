# References

Reyfen.Timbratune.Acoustics implements the speech-analysis steps Timbratune needs,
written from scratch from the published literature below. The algorithms are
**inspired by [Praat](https://github.com/praat/praat.github.io)** (Boersma & Weenink):
parameter defaults and the conventions for frame placement, interpolation and edge
cases follow the public **Praat manual** (<https://www.fon.hum.uva.nl/praat/manual/>),
so that results line up with Praat, whose numbers the original Euphonia's zones were
calibrated on.

**No Praat source code was copied, translated or adapted.** Praat's code was not
used as a basis for this module; only the papers and the manual's descriptions were.
Praat itself (GPLv3) is not part of Timbratune: it is never shipped or linked, and is
only run as an optional, separate test oracle to check the numbers. This module is
MIT licensed, like the rest of Timbratune.

| Component | Based on |
|---|---|
| `PitchAnalyzer` (autocorrelation & cross-correlation), `PitchPath`, `HarmonicityAnalyzer` | P. Boersma (1993), "Accurate short-term analysis of the fundamental frequency and the harmonics-to-noise ratio of a sampled sound", *Proc. Institute of Phonetic Sciences, Univ. of Amsterdam* 17: 97–110. Manual: *Sound: To Pitch (ac)…*, *Sound: To Harmonicity (cc)…*. |
| `SincInterpolator` | Boersma (1993), §3 (windowed sin(x)/x interpolation). |
| `PeakRefinement` / `Brent` | R. P. Brent (1973), *Algorithms for Minimization without Derivatives*, Prentice-Hall, ch. 5. |
| `Fft` | J. W. Cooley & J. W. Tukey (1965), "An algorithm for the machine calculation of complex Fourier series", *Math. Comp.* 19: 297–301. |
| `IntensityAnalyzer` | J. F. Kaiser (1974), "Nonrecursive digital filter design using the I₀-sinh window function", *Proc. IEEE ISCAS*. Manual: *Sound: To Intensity…*. |
| `Windows.BesselI0` | M. Abramowitz & I. A. Stegun (1964), *Handbook of Mathematical Functions*, formulas 9.8.1–9.8.2. |
| `Windows` (Hamming, Hanning) | F. J. Harris (1978), "On the use of windows for harmonic analysis with the discrete Fourier transform", *Proc. IEEE* 66: 51–83. |
| `SilenceDetector` | Manual: *Intensity: To TextGrid (silences)…*. |
| `PulseDetector` | Manual: *Sound & Pitch: To PointProcess (cc)*. |
| `VoiceReport` (jitter, shimmer) | Manual: *Voice 2. Jitter*, *Voice 3. Shimmer*; M. Farrús, J. Hernando & P. Ejarque (2007), "Jitter and shimmer measurements for speaker recognition", *Proc. Interspeech*. |
| `BurgLpc` | J. P. Burg (1975), *Maximum Entropy Spectral Analysis*, PhD thesis, Stanford; D. G. Childers (ed.) (1978), *Modern Spectrum Analysis*, IEEE Press; S. M. Kay & S. L. Marple (1981), "Spectrum analysis — a modern perspective", *Proc. IEEE* 69: 1380–1419. |
| `FormantAnalyzer` | J. D. Markel & A. H. Gray (1976), *Linear Prediction of Speech*, Springer. Manual: *Sound: To Formant (burg)…*. |
| `Polynomial.Roots` | O. Aberth (1973), "Iteration methods for finding all zeros of a polynomial simultaneously", *Math. Comp.* 27: 339–344; D. A. Bini (1996), "Numerical computation of polynomial zeros by means of Aberth's method", *Numer. Algorithms* 13: 179–200. |
| `Resampler` | Band-limited interpolation (Boersma 1993, §3) after an ideal low-pass in the frequency domain. |
| `Spectrum`, `Ltas` | Manual: *Sound: To Spectrum…*, *Sound: To Ltas…*. |
| `Stats` (compensated summation) | A. Neumaier (1974), "Rundungsfehleranalyse einiger Verfahren zur Summation endlicher Summen", *ZAMM* 54: 39–51. |
