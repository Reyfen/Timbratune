"""Makes noisy and degraded copies of clean takes for the noise bench.

usage: python make_noisy.py out_dir clean1.wav [clean2.wav ...]

Writes out_dir/<take>__clean.wav, out_dir/<take>__<noise>__snr<N>.wav for every additive noise
and SNR, and out_dir/<take>__<channel>.wav for channel effects (reverb, band limits, mic
response, clipping, low level). All noise is synthetic and seeded, so runs are repeatable;
babble and the competing talker are made from the other clean takes.

SNR is speech power over the active part of the take (10 ms frames within 35 dB of the
loudest) against the noise's mean power.
"""
import os
import sys

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt, fftconvolve

SNRS = [30, 20, 10, 5, 0]


def read(path):
    sr, x = wavfile.read(path)
    x = x.astype(float) / 32768
    if x.ndim > 1:
        x = x.mean(1)
    return sr, x


def write(path, sr, x):
    wavfile.write(path, sr, np.clip(np.round(x * 32768), -32768, 32767).astype(np.int16))


def active_power(x, sr):
    hop = sr // 100
    n = len(x) // hop
    p = (x[:n * hop].reshape(n, hop) ** 2).mean(1)
    db = 10 * np.log10(p + 1e-20)
    return p[db > db.max() - 35].mean()


def shaped(rng, n, sr, alpha):
    """Noise with power spectral density ~ 1/f^alpha (0 white, 1 pink, 2 brown)."""
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / sr)
    f[0] = f[1]
    spec *= f ** (-alpha / 2)
    return np.fft.irfft(spec, n)


def smooth_envelope(rng, n, sr, rate_hz, depth):
    """Slowly varying positive envelope (log-normal), changes about rate_hz times a second."""
    k = max(int(n / sr * rate_hz) + 2, 3)
    pts = rng.standard_normal(k) * depth
    env = np.interp(np.linspace(0, k - 1, n), np.arange(k), pts)
    sos = butter(2, rate_hz, 'lowpass', fs=sr, output='sos')
    return np.exp(sosfilt(sos, env))


def noise_white(rng, n, sr, others):
    return rng.standard_normal(n)


def noise_pink(rng, n, sr, others):
    return shaped(rng, n, sr, 1)


def noise_rumble(rng, n, sr, others):
    # traffic / HVAC / desk vibration: brown noise mostly below 150 Hz
    x = shaped(rng, n, sr, 2)
    return sosfilt(butter(2, 15, 'highpass', fs=sr, output='sos'), x)


def noise_wind(rng, n, sr, others):
    # turbulence on the mic: very low-frequency, gusty, now and then much louder
    x = shaped(rng, n, sr, 2)
    x = sosfilt(butter(4, 400, 'lowpass', fs=sr, output='sos'), x)
    x = sosfilt(butter(2, 10, 'highpass', fs=sr, output='sos'), x)
    return x * smooth_envelope(rng, n, sr, 1.5, 1.0)


def hum(rng, n, sr, mains):
    t = np.arange(n) / sr
    drift = 1 + 0.002 * np.sin(2 * np.pi * 0.1 * t)
    x = np.zeros(n)
    for k, a in zip(range(1, 12), [1, .3, .7, .2, .5, .1, .3, .1, .2, .05, .1]):
        x += a * np.sin(2 * np.pi * mains * k * drift * t + rng.uniform(0, 2 * np.pi))
    return x + 0.02 * rng.standard_normal(n)


def noise_hum50(rng, n, sr, others):
    return hum(rng, n, sr, 50)


def noise_hum60(rng, n, sr, others):
    return hum(rng, n, sr, 60)


def noise_fan(rng, n, sr, others):
    # computer / room fan: broadband pink, blade-pass tone with harmonics, a bearing whine
    t = np.arange(n) / sr
    x = shaped(rng, n, sr, 1)
    x /= np.sqrt((x ** 2).mean())
    blade = 95 * (1 + 0.003 * np.sin(2 * np.pi * 0.3 * t))
    for k, a in zip(range(1, 6), [0.5, 0.35, 0.2, 0.12, 0.08]):
        x += a * np.sin(2 * np.pi * blade * k * t + rng.uniform(0, 2 * np.pi))
    x += 0.08 * np.sin(2 * np.pi * 1730 * t)
    return x


def noise_babble(rng, n, sr, others):
    # café / office: several voices, each starting at a random point and looped
    x = np.zeros(n)
    for o in others:
        for _ in range(2):
            s = np.roll(np.resize(o, n), rng.integers(0, n))
            x += s / np.sqrt(active_power(o, sr))
    return x


def noise_talker(rng, n, sr, others):
    # one competing voice (TV, someone else talking): the hardest case for pitch
    o = others[rng.integers(0, len(others))]
    return np.roll(np.resize(o, n), rng.integers(0, n))


def noise_keyboard(rng, n, sr, others):
    # clicks: short decaying broadband bursts at irregular times, over faint hiss
    x = 0.01 * rng.standard_normal(n)
    t = 0
    while True:
        t += int(rng.exponential(0.15) * sr) + int(0.04 * sr)
        if t >= n:
            break
        m = int(0.012 * sr)
        burst = rng.standard_normal(m) * np.exp(-np.arange(m) / (0.002 * sr)) * rng.uniform(0.5, 1.5)
        x[t:t + m] += burst[:n - t]
    return x


def noise_street(rng, n, sr, others):
    # outdoors: rumble + babble + a little wind, mixed at equal power
    parts = [noise_rumble(rng, n, sr, others), noise_babble(rng, n, sr, others), noise_wind(rng, n, sr, others)]
    return sum(p / np.sqrt((p ** 2).mean()) for p in parts)


NOISES = {
    'white': noise_white, 'pink': noise_pink, 'rumble': noise_rumble, 'wind': noise_wind,
    'hum50': noise_hum50, 'hum60': noise_hum60, 'fan': noise_fan, 'babble': noise_babble,
    'talker': noise_talker, 'keyboard': noise_keyboard, 'street': noise_street,
}


def room(rng, x, sr, rt60):
    """Synthetic room: exponentially decaying noise impulse response with a direct path."""
    m = int(rt60 * sr)
    ir = rng.standard_normal(m) * np.exp(-6.9 * np.arange(m) / m)
    ir = sosfilt(butter(1, 4000, 'lowpass', fs=sr, output='sos'), ir) * 0.15
    ir[0] = 1
    y = fftconvolve(x, ir)[:len(x)]
    return y * np.sqrt(active_power(x, sr) / active_power(y, sr))


def channel_effects(rng, x, sr):
    out = {}
    out['reverb03'] = room(rng, x, sr, 0.3)
    out['reverb08'] = room(rng, x, sr, 0.8)
    # phone / Bluetooth headset: 300-3400 Hz
    out['phoneband'] = sosfilt(butter(4, [300, 3400], 'bandpass', fs=sr, output='sos'), x)
    # cheap laptop mic: no bass below ~200 Hz and a presence bump around 3 kHz
    y = sosfilt(butter(2, 200, 'highpass', fs=sr, output='sos'), x)
    bump = sosfilt(butter(2, [2500, 4000], 'bandpass', fs=sr, output='sos'), y)
    out['laptopmic'] = y + 1.0 * bump
    # close-talking headset mic with proximity effect: +8 dB low shelf below ~250 Hz
    low = sosfilt(butter(2, 250, 'lowpass', fs=sr, output='sos'), x)
    out['proximity'] = x + 1.5 * low
    # clipping: gain driven 12 dB past full scale on the loudest parts
    peak = np.abs(x).max()
    out['clipped'] = np.clip(x / peak * 4, -1, 1) * 0.9
    # very low level: peak at -45 dBFS (16-bit quantisation and a little hiss)
    y = x / peak * 10 ** (-45 / 20) + rng.standard_normal(len(x)) * 10 ** (-90 / 20)
    out['lowlevel'] = np.round(y * 32768) / 32768
    return out


def main():
    out_dir, cleans = sys.argv[1], sys.argv[2:]
    os.makedirs(out_dir, exist_ok=True)
    takes = {os.path.splitext(os.path.basename(p))[0]: read(p) for p in cleans}
    for ti, (name, (sr, x)) in enumerate(takes.items()):
        others = [y for k, (s, y) in takes.items() if k != name and s == sr]
        rng = np.random.default_rng(1000 + ti)
        write(f'{out_dir}/{name}__clean.wav', sr, x)
        ps = active_power(x, sr)
        for ni, (noise, make) in enumerate(NOISES.items()):
            nz = make(np.random.default_rng(ti * 100 + ni), len(x), sr, others)
            nz = nz / np.sqrt((nz ** 2).mean())
            for snr in SNRS:
                y = x + nz * np.sqrt(ps / 10 ** (snr / 10))
                peak = np.abs(y).max()
                if peak > 0.99:
                    y *= 0.99 / peak  # like an input that is turned down to avoid clipping
                write(f'{out_dir}/{name}__{noise}__snr{snr}.wav', sr, y)
        for ch, y in channel_effects(rng, x, sr).items():
            write(f'{out_dir}/{name}__{ch}.wav', sr, y)
        print(name, 'done', flush=True)


if __name__ == '__main__':
    main()
