# Euphonia voice analysis (as used by Timbratune's parity tests) — the Praat half of the C# port of analyze.py.
#
# Every Praat command and parameter below mirrors a parselmouth call in
# Euphonia-TypeScript/analyze.py (analyze(), vowel_formants(),
# spectral_weight(), analyze_register(), segment_phrases()). Anything that is
# plain statistics there (medians, SDs, the F2 stability gate, the ceiling
# choice, the Iseli–Alwan correction, the LTAS fit, phrase stats) happens in
# C# — see AnalysisPostProcessor.cs — so this script only extracts raw data.
#
# Usage:
#   Praat --run --no-pref-files --utf8 analyze.praat <wavPath>
#
# Output goes to stdout (Praat 7 sandboxes file writes behind --FULL-TRUST,
# which this script deliberately doesn't need). One record per line, fields
# separated by tabs, first field = record type:
#   S  key value                        summary scalar
#   F  ceiling t f1 f2 f3               vowel-core candidate that passed the F1 gate
#   W  t f0 f1 f2 f3 b1 b2 b3 h1 a3 k3  spectral-weight frame
#   L  f db                             LTAS sample
#   C  t hz                             10 ms pitch track (hz = 0 where unvoiced)
#   P  start end                        "sounding" interval
#   END                                 last line; its absence means the run died
# Undefined numbers print as "--undefined--".

form Euphonia analysis
    sentence Wav_path
endform

pitchFloor = 75
pitchCeiling = 500
formantCeiling = 5500

writeInfo: ""

sound = Read from file: wav_path$
duration = Get total duration
fs = Get sampling frequency

# ======================================================================
# analyze(): pitch summary
# ======================================================================
selectObject: sound
pitch = To Pitch: 0.0, pitchFloor, pitchCeiling
meanF0 = Get mean: 0, 0, "Hertz"
minF0 = Get minimum: 0, 0, "Hertz", "Parabolic"
maxF0 = Get maximum: 0, 0, "Hertz", "Parabolic"
sdF0 = Get standard deviation: 0, 0, "Hertz"
medianF0 = Get quantile: 0, 0, 0.5, "Hertz"

# Voiced frames of the default-step track (pitch.selected_array > 0).
nPitch = Get number of frames
nVoiced = 0
for i to nPitch
    f = Get value in frame: i, "Hertz"
    if f <> undefined
        if f > 0
            nVoiced += 1
            voicedT[nVoiced] = Get time from frame number: i
            voicedF[nVoiced] = f
        endif
    endif
endfor

# ======================================================================
# vowel_formants(): loud gate (peak - 10 dB) with searchsorted lookup
# ======================================================================
selectObject: sound
intensity = To Intensity: pitchFloor, 0, "yes"
nInt = Get number of frames
intMax = -1e308
for j to nInt
    intT[j] = Get time from frame number: j
    intV[j] = Get value in frame: j
    if intV[j] <> undefined
        if intV[j] > intMax
            intMax = intV[j]
        endif
    endif
endfor
loudFloor = intMax - 10

# np.searchsorted(int_ts, t) (left), clamped to the last frame: the first
# intensity frame at or after t. Voiced times increase, so a moving pointer works.
nCand = 0
p = 1
for k to nVoiced
    t = voicedT[k]
    while p < nInt and intT[p] < t
        p += 1
    endwhile
    if nInt > 0
        v = intV[p]
        if v <> undefined
            if v >= loudFloor
                nCand += 1
                candT[nCand] = t
            endif
        endif
    endif
endfor

# Subsample to <= 300 frames: cand_ts[int(k * step)].
if nCand > 300
    step = nCand / 300
    for k from 0 to 299
        selT[k + 1] = candT[floor(k * step) + 1]
    endfor
    nSel = 300
else
    for k to nCand
        selT[k] = candT[k]
    endfor
    nSel = nCand
endif

# measure(ceiling) for both ceilings. The stability gate and the choice of
# ceiling are done in C#; rows come out in time order.
formant5500 = 0
for c to 2
    if c = 1
        ceiling = formantCeiling
    else
        ceiling = 5000
    endif
    selectObject: sound
    formant = To Formant (burg): 0.0, 5, ceiling, 0.025, 50
    for k to nSel
        t = selT[k]
        f1 = Get value at time: 1, t, "Hertz", "Linear"
        f2 = Get value at time: 2, t, "Hertz", "Linear"
        f3 = Get value at time: 3, t, "Hertz", "Linear"
        ok = 1
        if f1 = undefined or f2 = undefined or f3 = undefined
            ok = 0
        elsif f1 <= 0 or f2 <= 0 or f3 <= 0
            ok = 0
        elsif f1 < 250 or f1 > 1000
            ok = 0
        endif
        if ok
            appendInfoLine: "F", tab$, ceiling, tab$, t, tab$, f1, tab$, f2, tab$, f3
        endif
    endfor
    if c = 1
        formant5500 = formant
    else
        removeObject: formant
    endif
endfor

# ======================================================================
# spectral_weight(): per voiced frame H1 / A3 + formants & bandwidths.
# Same To Pitch / To Formant (burg) parameters as above, so the objects are
# reused instead of recomputed.
# ======================================================================
procedure harmonicPeak: .target, .f0
    # _harmonic_db(): max dB over bins with target-half <= f <= target+half,
    # half = max(f0/2, 20). Any zero-power bin in the window makes numpy's max
    # NaN, which analyze.py treats as "no peak" — mirrored via .bad.
    .ok = 0
    .db = 0
    if .target > 0 and .f0 > 0
        .half = max(.f0 * 0.5, 20)
        .lo = .target - .half
        .hi = .target + .half
        .n = Get number of bins
        .dx = Get bin width
        .x1 = Get frequency from bin number: 1
        .iLo = max(1, floor((.lo - .x1) / .dx))
        .iHi = min(.n, ceiling((.hi - .x1) / .dx) + 2)
        .found = 0
        .bad = 0
        .best = -1e308
        for .i from .iLo to .iHi
            .f = .x1 + (.i - 1) * .dx
            if .f >= .lo and .f <= .hi
                .found = 1
                .re = Get real value in bin: .i
                .im = Get imaginary value in bin: .i
                .pw = .re * .re + .im * .im
                if .pw > 0
                    .v = 10 * log10(.pw)
                    if .v > .best
                        .best = .v
                    endif
                else
                    .bad = 1
                endif
            endif
        endfor
        if .found and not .bad
            .ok = 1
            .db = .best
        endif
    endif
endproc

if nVoiced > 250
    step = nVoiced / 250
    for k from 0 to 249
        idx = floor(k * step) + 1
        wT[k + 1] = voicedT[idx]
        wF[k + 1] = voicedF[idx]
    endfor
    nW = 250
else
    for k to nVoiced
        wT[k] = voicedT[k]
        wF[k] = voicedF[k]
    endfor
    nW = nVoiced
endif

for k to nW
    t = wT[k]
    f0 = wF[k]
    win = max(0.025, 3 / f0)
    t0 = t - win / 2
    t1 = t + win / 2
    if t0 >= 0 and t1 <= duration
        selectObject: formant5500
        fq1 = Get value at time: 1, t, "Hertz", "Linear"
        bw1 = Get bandwidth at time: 1, t, "Hertz", "Linear"
        fq2 = Get value at time: 2, t, "Hertz", "Linear"
        bw2 = Get bandwidth at time: 2, t, "Hertz", "Linear"
        fq3 = Get value at time: 3, t, "Hertz", "Linear"
        bw3 = Get bandwidth at time: 3, t, "Hertz", "Linear"
        ok = 1
        if fq1 = undefined or bw1 = undefined or fq2 = undefined or bw2 = undefined or fq3 = undefined or bw3 = undefined
            ok = 0
        elsif fq1 <= 0 or bw1 <= 0 or fq2 <= 0 or bw2 <= 0 or fq3 <= 0 or bw3 <= 0
            ok = 0
        endif
        if ok
            selectObject: sound
            part = Extract part: t0, t1, "Hamming", 1, "no"
            spec = To Spectrum: "yes"
            @harmonicPeak: f0, f0
            h1ok = harmonicPeak.ok
            h1 = harmonicPeak.db
            # k3 = max(1, round(F3 / f0)) — the harmonic nearest F3.
            k3 = max(1, round(fq3 / f0))
            @harmonicPeak: k3 * f0, f0
            a3ok = harmonicPeak.ok
            a3 = harmonicPeak.db
            removeObject: part, spec
            if h1ok and a3ok
                appendInfoLine: "W", tab$, t, tab$, f0, tab$, fq1, tab$, fq2, tab$, fq3, tab$, bw1, tab$, bw2, tab$, bw3, tab$, h1, tab$, a3, tab$, k3
            endif
        endif
    endif
endfor

# Legacy LTAS tilt: To Ltas 100 Hz, sampled at 100..5000 Hz; the fit is in C#.
selectObject: sound
ltas = To Ltas: 100
for i to 50
    freq = i * 100
    v = Get value at frequency: freq, "Linear"
    if v <> undefined
        appendInfoLine: "L", tab$, freq, tab$, v
    endif
endfor

# ======================================================================
# analyze(): voice quality + intensity summary
# ======================================================================
selectObject: sound
harmonicity = To Harmonicity (cc): 0.01, pitchFloor, 0.1, 1.0
hnr = Get mean: 0, 0

selectObject: sound
pointProcess = To PointProcess (periodic, cc): pitchFloor, pitchCeiling
jitter = Get jitter (local): 0, 0, 0.0001, 0.02, 1.3
selectObject: sound, pointProcess
shimmer = Get shimmer (local): 0, 0, 0.0001, 0.02, 1.3, 1.6

selectObject: intensity
meanInt = Get mean: 0, 0, "energy"
minInt = Get minimum: 0, 0, "Parabolic"
maxInt = Get maximum: 0, 0, "Parabolic"

# ======================================================================
# analyze_register(): full 10 ms contour + silence-based phrases
# ======================================================================
selectObject: sound
pitchR = To Pitch: 0.01, pitchFloor, pitchCeiling
nR = Get number of frames
for i to nR
    t = Get time from frame number: i
    v = Get value in frame: i, "Hertz"
    if v = undefined
        v = 0
    endif
    appendInfoLine: "C", tab$, t, tab$, v
endfor

# segment_phrases(): sound.to_intensity(minimum_pitch=75) is the same
# To Intensity 75, 0, yes as above, so that object is reused.
selectObject: intensity
textgrid = To TextGrid (silences): -25.0, 0.1, 0.05, "silent", "sounding"
nIntervals = Get number of intervals: 1
for i to nIntervals
    label$ = Get label of interval: 1, i
    if label$ = "sounding"
        t0 = Get start time of interval: 1, i
        t1 = Get end time of interval: 1, i
        appendInfoLine: "P", tab$, t0, tab$, t1
    endif
endfor

# ======================================================================
# summary scalars
# ======================================================================
appendInfoLine: "S", tab$, "duration", tab$, duration
appendInfoLine: "S", tab$, "sampling_frequency", tab$, fs
appendInfoLine: "S", tab$, "pitch_mean", tab$, meanF0
appendInfoLine: "S", tab$, "pitch_median", tab$, medianF0
appendInfoLine: "S", tab$, "pitch_min", tab$, minF0
appendInfoLine: "S", tab$, "pitch_max", tab$, maxF0
appendInfoLine: "S", tab$, "pitch_sd", tab$, sdF0
appendInfoLine: "S", tab$, "hnr", tab$, hnr
appendInfoLine: "S", tab$, "jitter_local", tab$, jitter
appendInfoLine: "S", tab$, "shimmer_local", tab$, shimmer
appendInfoLine: "S", tab$, "intensity_mean", tab$, meanInt
appendInfoLine: "S", tab$, "intensity_min", tab$, minInt
appendInfoLine: "S", tab$, "intensity_max", tab$, maxInt
appendInfoLine: "END"
