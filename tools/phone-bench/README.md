# phone-bench

The phone A/B of HANDOFF §8d and §8i as scripts (Git Bash on Windows, or bash on Linux; `adb` from the SDK's `platform-tools`, override with `ADB=`).

**Etiquette:** never uninstall the owner's app (that deletes their takes) and never change phone settings; ask the owner to enable "Stay awake". Build both APKs with a **separate app id**, so the owner's installed Timbratune and its takes are never touched, and ask before installing on their phone.

**Build** each Release profiling APK (LLVM AOT; one build at a time):

```bash
dotnet publish src/Reyfen.Timbratune.Android -c Release -r android-arm64 -o <dir> -p:TimbratuneProfiling=true -p:ApplicationId=com.reyfen.timbratune.research -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>
```

**Run** six alternating pairs (about 13 minutes per phone), with `long.wav` from `scripts/make-long-wav.ps1`. Record and Stop button coordinates: Pixel 4a 273,662 and 348,543; Pixel 9 259,674 and 332,560.

```bash
bash tools/phone-bench/ab.sh <serial> com.reyfen.timbratune.research long.wav <out> 273 662 348 543 6 base=<a.apk> head=<b.apk>
py -I tools/phone-bench/analyze.py <out> base head
bash tools/phone-bench/clean.sh <serial> com.reyfen.timbratune.research
```

`run.sh` is one run (cold start, empty takes list, a 28 s recording sampled with `dumpsys SurfaceFlinger --latency` every 1.5 s, Stop, logcat probes and a screenshot). `analyze.py` prints each run, the medians per build and a shuffle test between the two builds. Single runs vary by up to 4 points of janky % on the 4a, so don't use fewer than 6 pairs.
