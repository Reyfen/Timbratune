"""Candidate clean-up chains for the noise bench (Python prototypes; the winner gets a C#
implementation from the papers, see src/Reyfen.Timbratune.Acoustics/REFERENCES.md).

usage: python denoise.py in_dir out_dir [chain ...]   (default: every chain)

Writes out_dir/<chain>/<file>.wav for every WAV in in_dir.

Building blocks, each from a published method:
- high-pass: 4th-order Butterworth, causal (so live analysis could use it too)
- noise PSD: 'quiet' = mean of the quietest 10 % of frames (whole take, not causal);
  'minstat' = minimum of the smoothed PSD over the last 1.5 s with a bias factor
  (Martin 2001, simplified; causal)
- gain: Wiener with decision-directed a-priori SNR (Ephraim & Malah 1984);
  'specsub' = power spectral subtraction with over-subtraction (Berouti et al. 1979);
  'lsa' = log-spectral amplitude estimator (Ephraim & Malah 1985);
  'omlsa' = LSA weighted by speech-presence probability (Cohen & Berdugo 2001)
- gain floor in dB (how far noise is turned down at most)
"""
import os
import sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt
from scipy.special import exp1


def highpass(x, sr, fc):
    return sosfilt(butter(4, fc, 'highpass', fs=sr, output='sos'), x)


def stft(x, sr):
    n = 1024 if sr > 32000 else 512
    h = n // 4
    w = np.hanning(n + 1)[:-1]
    pad = np.concatenate([np.zeros(n), x, np.zeros(n)])
    nf = (len(pad) - n) // h
    X = np.array([np.fft.rfft(pad[i * h:i * h + n] * w) for i in range(nf)])
    return X, (n, h, w, len(pad), len(x))


def istft(Y, geo):
    n, h, w, plen, xlen = geo
    y = np.zeros(plen)
    ws = np.zeros(plen)
    for i in range(len(Y)):
        y[i * h:i * h + n] += np.fft.irfft(Y[i], n) * w
        ws[i * h:i * h + n] += w ** 2
    return (y / np.maximum(ws, 1e-8))[n:n + xlen]


def noise_psd(P, sr, hop, how):
    if how == 'quiet':
        e = P.sum(1)
        q = e <= np.percentile(e[e > 0], 10) if (e > 0).any() else np.ones(len(e), bool)
        return np.tile(P[q].mean(0) + 1e-12, (len(P), 1))
    # minimum statistics (simplified): smoothed PSD, minimum over the last 1.5 s, bias 1.5
    sm = np.empty_like(P)
    s = P[0]
    for i in range(len(P)):
        s = 0.85 * s + 0.15 * P[i]
        sm[i] = s
    span = int(1.5 * sr / hop)
    out = np.empty_like(P)
    for i in range(len(P)):
        out[i] = sm[max(0, i - span):i + 1].min(0) * 1.5 + 1e-12
    return out


def suppress(x, sr, gain, est, floor_db):
    X, geo = stft(x, sr)
    P = np.abs(X) ** 2
    N = noise_psd(P, sr, geo[1], est)
    gmin = 10 ** (floor_db / 20)
    G = np.ones_like(P)
    prev = np.zeros(P.shape[1])
    for i in range(len(P)):
        gam = np.maximum(P[i] / N[i], 1e-6)
        if gain == 'specsub':
            g = np.sqrt(np.maximum(1 - 2.0 * N[i] / P[i].clip(1e-20), 0))
        else:
            xi = np.maximum(0.98 * prev / N[i] + 0.02 * np.maximum(gam - 1, 0), 10 ** (-25 / 10))
            g = xi / (1 + xi)
            if gain in ('lsa', 'omlsa'):
                v = np.minimum(xi / (1 + xi) * gam, 500)
                g = g * np.exp(0.5 * exp1(np.maximum(v, 1e-8)))
            if gain == 'omlsa':
                p = 1 / (1 + (1 + xi) * np.exp(-v))  # prior speech absence 0.5
                g = np.minimum(g, 1) ** p * gmin ** (1 - p)
        g = np.clip(g, gmin, 1)
        G[i] = g
        prev = g ** 2 * P[i]
    return istft(X * G, geo)


CHAINS = {
    'none': lambda x, sr: x,
    'hp50': lambda x, sr: highpass(x, sr, 50),
    'hp50_wiener25': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'wiener', 'quiet', -25),
    'hp50_specsub': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'specsub', 'quiet', -25),
    'hp50_lsa25': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'lsa', 'quiet', -25),
    'hp50_omlsa30': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'omlsa', 'quiet', -30),
    'hp50_omlsa30_ms': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'omlsa', 'minstat', -30),
    # round 2: gain floor and causal noise tracking for the two best gains
    'hp50_lsa15': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'lsa', 'quiet', -15),
    'hp50_lsa20': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'lsa', 'quiet', -20),
    'hp50_lsa30': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'lsa', 'quiet', -30),
    'hp50_lsa20_ms': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'lsa', 'minstat', -20),
    'hp50_specsub_ms': lambda x, sr: suppress(highpass(x, sr, 50), sr, 'specsub', 'minstat', -25),
    'hp70_lsa20': lambda x, sr: suppress(highpass(x, sr, 70), sr, 'lsa', 'quiet', -20),
}


def run(job):
    chain, src, dst = job
    sr, x = wavfile.read(src)
    x = x.astype(float) / 32768
    y = CHAINS[chain](x, sr)
    wavfile.write(dst, sr, np.clip(np.round(y * 32768), -32768, 32767).astype(np.int16))


def main():
    in_dir, out_dir = sys.argv[1], sys.argv[2]
    chains = sys.argv[3:] or list(CHAINS)
    jobs = []
    for c in chains:
        os.makedirs(f'{out_dir}/{c}', exist_ok=True)
        for f in sorted(os.listdir(in_dir)):
            if f.endswith('.wav'):
                jobs.append((c, f'{in_dir}/{f}', f'{out_dir}/{c}/{f}'))
    with ProcessPoolExecutor() as pool:
        for i, _ in enumerate(pool.map(run, jobs, chunksize=4)):
            if (i + 1) % 200 == 0:
                print(f'{i + 1}/{len(jobs)}', flush=True)
    print(len(jobs), 'files', flush=True)


if __name__ == '__main__':
    main()
