# Praat's F2 and F3 of a WAV on the app's 10 ms graph grid (t = k * 0.01 s), at fixed ceilings of 4500, 5000, 5500 and
# 6000 Hz: the per-moment references of HighPitchTests' graph-line tests (Fixtures/OWNER-TAKES.md).
# To Formant (burg): 5 formants, 25 ms window, 50 Hz pre-emphasis, a 1 ms time step; each grid moment is
# "Get value at time" (linear) between the two nearest Praat frames. Undefined values are written as NaN.
# Run: Praat.exe --run --no-pref-files --utf8 praat-tracks.praat <in.wav> > <out.tsv>
form Tracks
    sentence wav
endform
sound = Read from file: wav$
duration = Get total duration
selectObject: sound
f55 = To Formant (burg): 0.001, 5, 5500, 0.025, 50
selectObject: sound
f60 = To Formant (burg): 0.001, 5, 6000, 0.025, 50
selectObject: sound
f45 = To Formant (burg): 0.001, 5, 4500, 0.025, 50
selectObject: sound
f50 = To Formant (burg): 0.001, 5, 5000, 0.025, 50
writeInfoLine: "t", tab$, "f2_5500", tab$, "f3_5500", tab$, "f2_6000", tab$, "f3_6000", tab$, "f2_4500", tab$, "f3_4500", tab$, "f2_5000", tab$, "f3_5000"
n = floor(duration / 0.01)
for k from 0 to n
    t = k * 0.01
    selectObject: f55
    a2 = Get value at time: 2, t, "hertz", "linear"
    a3 = Get value at time: 3, t, "hertz", "linear"
    selectObject: f60
    b2 = Get value at time: 2, t, "hertz", "linear"
    b3 = Get value at time: 3, t, "hertz", "linear"
    selectObject: f45
    c2 = Get value at time: 2, t, "hertz", "linear"
    c3 = Get value at time: 3, t, "hertz", "linear"
    selectObject: f50
    d2 = Get value at time: 2, t, "hertz", "linear"
    d3 = Get value at time: 3, t, "hertz", "linear"
    line$ = fixed$(t, 2)
    for v from 1 to 8
        if v = 1
            x = a2
        elsif v = 2
            x = a3
        elsif v = 3
            x = b2
        elsif v = 4
            x = b3
        elsif v = 5
            x = c2
        elsif v = 6
            x = c3
        elsif v = 7
            x = d2
        else
            x = d3
        endif
        if x = undefined
            line$ = line$ + tab$ + "NaN"
        else
            line$ = line$ + tab$ + fixed$(x, 0)
        endif
    endfor
    appendInfoLine: line$
endfor
