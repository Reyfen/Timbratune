# Oracle dump for Reyfen.Timbratune.Acoustics tests: runs each Praat command that
# Timbratune uses on one WAV and prints its frame-level results, so the C#
# implementation can be compared component by component.
#
#   Praat --run --no-pref-files --utf8 dump.praat <wav>
#
# Lines: <tag> TAB values…  (numbers at full precision, "--undefined--" for undefined)

form Oracle dump
    sentence Wav_path
endform

writeInfo: ""
sound = Read from file: wav_path$
dur = Get total duration
fs = Get sampling frequency
ns = Get number of samples
appendInfoLine: "sound", tab$, dur, tab$, fs, tab$, ns

# ---------- pitch (ac) ----------
selectObject: sound
pitch = To Pitch: 0.0, 75, 500
n = Get number of frames
t1 = Get time from frame number: 1
dt = Get time step
appendInfoLine: "pitchgrid", tab$, n, tab$, t1, tab$, dt
for i to n
    v = Get value in frame: i, "Hertz"
    appendInfoLine: "pitch", tab$, i, tab$, v
endfor
m = Get mean: 0, 0, "Hertz"
s = Get standard deviation: 0, 0, "Hertz"
q = Get quantile: 0, 0, 0.5, "Hertz"
lo = Get minimum: 0, 0, "Hertz", "Parabolic"
hi = Get maximum: 0, 0, "Hertz", "Parabolic"
appendInfoLine: "pitchstats", tab$, m, tab$, s, tab$, q, tab$, lo, tab$, hi

# ---------- intensity ----------
selectObject: sound
intensity = To Intensity: 75, 0, "yes"
n = Get number of frames
t1 = Get time from frame number: 1
dt = Get time step
appendInfoLine: "intgrid", tab$, n, tab$, t1, tab$, dt
for i to n
    v = Get value in frame: i
    appendInfoLine: "int", tab$, i, tab$, v
endfor
m = Get mean: 0, 0, "energy"
lo = Get minimum: 0, 0, "Parabolic"
hi = Get maximum: 0, 0, "Parabolic"
appendInfoLine: "intstats", tab$, m, tab$, lo, tab$, hi

# ---------- silences ----------
tg = To TextGrid (silences): -25.0, 0.1, 0.05, "silent", "sounding"
n = Get number of intervals: 1
for i to n
    a = Get start time of interval: 1, i
    b = Get end time of interval: 1, i
    lab$ = Get label of interval: 1, i
    appendInfoLine: "interval", tab$, a, tab$, b, tab$, lab$
endfor

# ---------- harmonicity ----------
selectObject: sound
harm = To Harmonicity (cc): 0.01, 75, 0.1, 1.0
n = Get number of frames
for i to n
    v = Get value in frame: i
    appendInfoLine: "hnr", tab$, i, tab$, v
endfor
m = Get mean: 0, 0
appendInfoLine: "hnrmean", tab$, m

# ---------- pulses, jitter, shimmer ----------
selectObject: sound
pp = To PointProcess (periodic, cc): 75, 500
n = Get number of points
for i to n
    v = Get time from index: i
    appendInfoLine: "pulse", tab$, v
endfor
jitter = Get jitter (local): 0, 0, 0.0001, 0.02, 1.3
selectObject: sound, pp
shimmer = Get shimmer (local): 0, 0, 0.0001, 0.02, 1.3, 1.6
appendInfoLine: "voice", tab$, jitter, tab$, shimmer

# ---------- formants ----------
for c to 2
    if c = 1
        ceiling = 5500
    else
        ceiling = 5000
    endif
    selectObject: sound
    formant = To Formant (burg): 0.0, 5, ceiling, 0.025, 50
    n = Get number of frames
    t1 = Get time from frame number: 1
    dt = Get time step
    appendInfoLine: "fmtgrid", tab$, ceiling, tab$, n, tab$, t1, tab$, dt
    for i to n
        nf = Get number of formants: i
        t = Get time from frame number: i
        line$ = "fmt" + tab$ + string$(ceiling) + tab$ + string$(i) + tab$ + string$(nf)
        for k to nf
            f = Get value at time: k, t, "Hertz", "Linear"
            b = Get bandwidth at time: k, t, "Hertz", "Linear"
            line$ = line$ + tab$ + string$(f) + tab$ + string$(b)
        endfor
        appendInfoLine: line$
    endfor
    removeObject: formant
endfor

# ---------- extract part + spectrum ----------
selectObject: sound
part = Extract part: dur * 0.4, dur * 0.4 + 0.03, "Hamming", 1, "no"
spec = To Spectrum: "yes"
n = Get number of bins
df = Get bin width
appendInfoLine: "specgrid", tab$, n, tab$, df
for i to n
    re = Get real value in bin: i
    im = Get imaginary value in bin: i
    appendInfoLine: "spec", tab$, i, tab$, re, tab$, im
endfor

# ---------- ltas ----------
selectObject: sound
ltas = To Ltas: 100
for i to 50
    v = Get value at frequency: i * 100, "Linear"
    appendInfoLine: "ltas", tab$, i * 100, tab$, v
endfor
appendInfoLine: "END"
