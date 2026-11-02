"""Builds assets/teaser-audio.wav: ambience, UI sounds, a quiet music bed and the voice-over,
all placed on the same timeline as teaser.html. Needs numpy only.

    python3 audio.py            # writes assets/teaser-audio.wav

Voice-over lines are read from assets/vo/l1.wav … l5.wav (mono, 48 kHz). Replace them with a real
recording any time and re-run; the timings live in VO below.
"""
import wave
from pathlib import Path

import numpy as np

SR = 48000
DUR = 25.4
HERE = Path(__file__).parent
rng = np.random.default_rng(7)

N = int(SR * DUR)
amb = np.zeros((N, 2))   # ambience bed (ducked under voice)
mus = np.zeros((N, 2))   # music bed (ducked under voice)
sfx = np.zeros((N, 2))   # UI sounds
vo = np.zeros(N)         # narration

VO = [(7.45, 'l1'), (12.45, 'l2'), (16.35, 'l3'), (18.1, 'l4'), (22.75, 'l5')]
BPM, MUSIC_START, MUSIC_END = 96, 7.0, 12.35
BEAT = 60 / BPM


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def hz(note):  # 'A4' style
    names = {'C': -9, 'D': -7, 'E': -5, 'F': -4, 'G': -2, 'A': 0, 'B': 2}
    n = names[note[0]] + (1 if '#' in note else 0) + 12 * (int(note[-1]) - 4)
    return 440 * 2 ** (n / 12)


def put(buf, at, sig, gain=1.0, pan=0.0):
    i = int(at * SR)
    if i >= len(buf):
        return
    sig = sig[: len(buf) - i] * gain
    if buf.ndim == 1:
        buf[i:i + len(sig)] += sig
    else:
        l, r = np.sqrt(0.5 * (1 - pan)), np.sqrt(0.5 * (1 + pan))
        buf[i:i + len(sig), 0] += sig * l * 1.414
        buf[i:i + len(sig), 1] += sig * r * 1.414


def onepole(x, cutoff):
    """Low-pass with a cutoff that may change per sample."""
    cutoff = np.broadcast_to(cutoff, x.shape)
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc = (1 - a[i]) * x[i] + a[i] * acc
        y[i] = acc
    return y


def reverb(x, secs=1.6, mix=0.25):
    ir_t = t_axis(secs)
    ir = rng.standard_normal(len(ir_t)) * np.exp(-ir_t * 5.5 / secs)
    ir = onepole(ir, 5000)
    ir /= np.sqrt(np.sum(ir ** 2))
    n = len(x) + len(ir)
    wet = np.fft.irfft(np.fft.rfft(x, n) * np.fft.rfft(ir, n), n)[: len(x)]
    return x * (1 - mix) + wet * mix * 1.8


def chime(freqs, dur=1.2, bright=1.0):
    t = t_axis(dur)
    s = np.zeros_like(t)
    for k, f in enumerate(freqs):
        d = k * 0.085
        tt = np.clip(t - d, 0, None)
        on = (t >= d)
        tone = np.sin(2 * np.pi * f * tt) + 0.25 * bright * np.sin(2 * np.pi * 2 * f * tt) * np.exp(-tt * 9)
        s += on * tone * np.exp(-tt * 4.2) * np.minimum(1, tt * 400)
    return s / max(1, len(freqs))


def whoosh(dur, peak, lo=300, hi=4500, gain=1.0):
    t = t_axis(dur)
    x = rng.standard_normal(len(t))
    env = np.where(t < peak, (t / peak) ** 2.2, np.exp(-(t - peak) * 7))
    cut = lo + (hi - lo) * env
    return onepole(x, cut) * env * gain


def click(dur=0.03, f=2400):
    t = t_axis(dur)
    return (np.sin(2 * np.pi * f * t) * 0.5 + rng.standard_normal(len(t)) * 0.5) * np.exp(-t * 260)


def thump(f0=140, f1=70, dur=0.35):
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t * 18)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 11)


# ---------- ambience: a soft, slow chord that breathes ----------
t = np.arange(N) / SR
pad = np.zeros(N)
for i, n in enumerate(['C3', 'G3', 'B3', 'D4', 'E4']):
    f = hz(n)
    pad += np.sin(2 * np.pi * f * t + i) * (0.8 + 0.2 * np.sin(2 * np.pi * (0.07 + i * 0.03) * t))
    pad += 0.3 * np.sin(2 * np.pi * f * 1.003 * t)
pad = onepole(pad / 6, 900)
amb_env = np.interp(t, [0, 1.6, 4.0, 7.0, 7.6, 12.3, 13.0, 21.8, 23.0, DUR - 0.6, DUR],
                       [0, .9, 1, .8, .35, .35, .8, .8, 1.15, 1.1, 0])
amb[:, 0] += pad * amb_env * 0.11
amb[:, 1] += np.roll(pad, 240) * amb_env * 0.11
room = onepole(rng.standard_normal(N), 500) * 0.03 * np.interp(t, [0, 1, 4.5, 6, 20.8, 22], [0, 1, 1, .4, .4, 0])
amb += room[:, None]

# ---------- 0-4 s: desktop notifications, the pause, the glide ----------
for at, notes, pan in [(0.7, ['E6', 'B5'], .45), (1.35, ['G#5', 'E6'], .5), (2.2, ['C#6', 'A5'], .2)]:
    put(sfx, at, reverb(chime([hz(n) for n in notes], 1.3), 1.2, .3), 0.16, pan)
put(sfx, 2.55, reverb(click(0.05, 900), 1.5, .5), 0.12)                     # the freeze
put(sfx, 2.75, whoosh(2.0, 1.55, 200, 3800), 0.20)                           # glide to the notch
put(sfx, 4.25, reverb(np.pad(thump(220, 110, .5), (0, int(.9 * SR))) * .6 + chime([hz('E5'), hz('B5')], 1.4, .4) * .5, 1.8, .35), 0.32)  # Eave opens

# ---------- 7-12 s: music bed, in time with the waveform ----------
chords = [['F3', 'A3', 'C4', 'E4'], ['A3', 'C4', 'E4', 'G4'], ['D3', 'F3', 'A3', 'C4'], ['G3', 'B3', 'D4', 'F4']]
mt = MUSIC_START
beat_i = 0
while mt < MUSIC_END:
    put(mus, mt, thump(110, 48, .45), 0.55)                                     # kick on every beat
    put(mus, mt + BEAT / 2, onepole(rng.standard_normal(int(.05 * SR)), 9000) * np.exp(-t_axis(.05) * 90)
        - onepole(rng.standard_normal(int(.05 * SR)), 2500) * np.exp(-t_axis(.05) * 90), 0.18, .3)  # hat
    if beat_i % 4 == 0:
        ch = chords[(beat_i // 4) % 4]
        bar = t_axis(BEAT * 4 + .4)
        s = sum(np.sin(2 * np.pi * hz(n) * bar) + .2 * np.sin(2 * np.pi * hz(n) * 2.001 * bar) for n in ch)
        s = onepole(s, 1400) * np.minimum(1, bar * 12) * np.exp(-bar * .5) / 4
        put(mus, mt, s, 0.30)
    arp = chords[(beat_i // 4) % 4]
    for k in range(2):
        nt = t_axis(.5)
        f = hz(arp[(beat_i * 2 + k) % 4]) * 2
        put(mus, mt + k * BEAT / 2, np.sin(2 * np.pi * f * nt) * np.exp(-nt * 7), 0.07, -.3 + .6 * k)
    mt += BEAT
    beat_i += 1
mus_env = np.interp(t, [MUSIC_START - .1, MUSIC_START + .4, MUSIC_END - .6, MUSIC_END], [0, 1, 1, 0])
mus *= mus_env[:, None] * 0.7
mus[:, 0] = reverb(mus[:, 0], 1.2, .18)
mus[:, 1] = reverb(mus[:, 1], 1.25, .18)
put(sfx, 6.95, whoosh(.6, .35, 600, 6000), 0.09, .3)

# ---------- 12-16 s: Claude ----------
put(sfx, 12.0, whoosh(.6, .35, 600, 6000), 0.09, -.3)
put(sfx, 12.15, reverb(chime([hz('A5'), hz('E6')], 1.2), 1.4, .35), 0.12)
tt = 12.45
while tt < 13.75:                                                               # typing glow ticks
    put(sfx, tt, click(.02, 3200 + rng.uniform(-400, 400)), 0.025, rng.uniform(-.2, .2))
    tt += rng.uniform(.045, .09)
put(sfx, 14.08, np.pad(click(.04, 1800), (0, int(.04 * SR))) + thump(400, 260, .08) * .4, 0.22)               # the tap
put(sfx, 14.15, whoosh(.55, .3, 400, 5000), 0.08)
put(sfx, 15.35, whoosh(.5, .3, 300, 3000), 0.06)

# ---------- 16-20 s: weather and the day ----------
put(sfx, 16.0, whoosh(.6, .35, 600, 6000), 0.09, .3)
put(sfx, 16.15, reverb(chime([hz('G5'), hz('D6'), hz('G6')], 1.4), 1.6, .35), 0.11)
put(sfx, 17.55, reverb(chime([hz('C6'), hz('E6')], 1.0), 1.2, .3), 0.10, -.2)
put(sfx, 18.45, reverb(chime([hz('E6'), hz('A6')], 1.0), 1.2, .3), 0.08, .2)

# ---------- 20-25 s: tuck away, back to work, logo ----------
put(sfx, 19.95, whoosh(.5, .25, 500, 5000), 0.08)
put(sfx, 20.6, thump(160, 90, .3), 0.18)                                       # island closes
put(sfx, 20.4, whoosh(1.6, .5, 4000, 300), 0.12)                                 # zoom out
put(sfx, 21.8, whoosh(1.0, .7, 300, 6000), 0.14)                                 # the sheet
end = chime([hz('C5'), hz('E5'), hz('G5'), hz('B5'), hz('D6')], 3.0, .6)
put(sfx, 22.85, reverb(end, 2.4, .45), 0.2)

# ---------- voice-over ----------
for at, name in VO:
    p = HERE / 'assets' / 'vo' / f'{name}.wav'
    if not p.exists():
        continue
    with wave.open(str(p)) as w:
        assert w.getframerate() == SR and w.getnchannels() == 1, f'{p} must be mono {SR} Hz'
        x = np.frombuffer(w.readframes(w.getnframes()), np.int16).astype(float) / 32768
    put(vo, at, x, 1.0)
vo = reverb(vo, .5, .08)

# duck beds under the voice (smoothed envelope)
v_env = onepole(np.abs(vo) > 0.01, 6.0)
v_env = np.clip(v_env * 3, 0, 1)
duck = 1 - 0.5 * v_env
mix = (amb + mus) * duck[:, None] + sfx + vo[:, None] * 0.9

# fade out the last moment and normalise
mix *= np.interp(t, [0, DUR - .5, DUR], [1, 1, 0])[:, None]
mix = np.tanh(mix * 1.1) / np.tanh(1.1)
mix *= 0.89 / np.max(np.abs(mix))
out = HERE / 'assets' / 'teaser-audio.wav'
with wave.open(str(out), 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((mix * 32767).astype(np.int16).tobytes())
print('wrote', out)
