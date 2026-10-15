# noise-bench

Measures how noise, rooms and microphones move Timbratune's take metrics, and which clean-up
chain brings them back closest to the clean take. Dev tool, not part of the solution.

```bash
python make_noisy.py <bench>/noisy ../../tests/Reyfen.Timbratune.Core.Tests/Fixtures/*.wav
python denoise.py <bench>/noisy <bench>/out            # every chain, or name some
dotnet run -c Release -- <bench>/out <bench>/results.csv
python score.py <bench>/results.csv hp50_lsa15         # optional: per-metric detail for one chain
```

Keep `<bench>` outside the repo (about 1.6 GB for 7 chains).

- `make_noisy.py`: 11 additive noises (white, pink, rumble, wind, 50/60 Hz hum, fan, babble,
  one competing talker, keyboard clicks, street) at SNR 30/20/10/5/0 dB, plus channel effects
  (two rooms, phone band, laptop mic, proximity effect, clipping, very low level). All synthetic
  and seeded; babble and talker come from the other fixtures.
- `denoise.py`: Python prototypes of the candidate chains (high-pass, Wiener, spectral
  subtraction, LSA, OM-LSA; whole-take or minimum-statistics noise estimate).
- `Program.cs`: runs the full analysis on every WAV, in parallel, and writes one CSV row each.
- `score.py`: error against the clean take (after a 50 Hz high-pass; see its docstring),
  divided by a per-metric tolerance and capped at 5; 0 = same as clean.
