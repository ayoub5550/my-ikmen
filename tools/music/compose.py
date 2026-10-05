#!/usr/bin/env python3
"""Original background music for my-ikmen, synthesised from code (no samples, no third-party
material), so the project owns it outright (MIT, like the rest of our code).

Every track is generated deterministically from the table at the bottom: tempo, key, chord
progression, groove and a seeded melody generator. Output: Ogg Vorbis files in
unity/Assets/IK/Resources/music/ (loaded with Resources.Load<AudioClip>("music/<name>")).

    python3 tools/music/compose.py            # all tracks
    python3 tools/music/compose.py fight1     # one track
Needs numpy, scipy and ffmpeg with libvorbis.
"""
from __future__ import annotations

import pathlib
import subprocess
import sys
import tempfile

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter

SR = 44100
REPO = pathlib.Path(__file__).resolve().parents[2]
OUT = REPO / "unity" / "Assets" / "IK" / "Resources" / "music"

NOTE = {"C": 0, "C#": 1, "Db": 1, "D": 2, "D#": 3, "Eb": 3, "E": 4, "F": 5, "F#": 6, "Gb": 6,
        "G": 7, "G#": 8, "Ab": 8, "A": 9, "A#": 10, "Bb": 10, "B": 11}
MINOR = [0, 2, 3, 5, 7, 8, 10]
MAJOR = [0, 2, 4, 5, 7, 9, 11]


def hz(midi: float) -> float:
    return 440.0 * 2 ** ((midi - 69) / 12)


def chord_notes(name: str, octave: int = 4) -> list[int]:
    """'Am' / 'F' / 'B7' / 'Dsus' → midi notes (root position)."""
    root = name[:2] if len(name) > 1 and name[1] in "#b" else name[:1]
    q = name[len(root):]
    r = NOTE[root] + 12 * (octave + 1)
    if q.startswith("m") and not q.startswith("maj"):
        iv = [0, 3, 7]
    elif q.startswith("sus"):
        iv = [0, 5, 7]
    elif q.startswith("dim"):
        iv = [0, 3, 6]
    else:
        iv = [0, 4, 7]
    if q.endswith("7"):
        iv.append(10)
    return [r + i for i in iv]


# ---- oscillators and envelopes -------------------------------------------------------------

def osc(kind: str, f: float, n: int, phase: float = 0.0, detune: float = 0.0) -> np.ndarray:
    t = np.arange(n) / SR
    p = (f * (1 + detune) * t + phase) % 1.0
    if kind == "sine":
        return np.sin(2 * np.pi * p)
    if kind == "saw":
        return 2 * p - 1
    if kind == "square":
        return np.where(p < 0.5, 1.0, -1.0)
    if kind == "pulse25":
        return np.where(p < 0.25, 1.0, -1.0)
    if kind == "tri":
        return 4 * np.abs(p - 0.5) - 1
    raise ValueError(kind)


def adsr(n: int, a: float, d: float, s: float, r: float) -> np.ndarray:
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    env = np.full(n, s, dtype=np.float64)
    if a_n > 0:
        env[:min(a_n, n)] = np.linspace(0, 1, a_n)[:n]
    if d_n > 0 and a_n < n:
        seg = np.linspace(1, s, d_n)
        env[a_n:a_n + d_n] = seg[:max(0, min(d_n, n - a_n))]
    if r_n > 0:
        r_n = min(r_n, n)
        env[n - r_n:] *= np.linspace(1, 0, r_n)
    return env


def lowpass(x: np.ndarray, cutoff: float, order: int = 2) -> np.ndarray:
    b, a = butter(order, min(cutoff / (SR / 2), 0.99), btype="low")
    return lfilter(b, a, x)


def highpass(x: np.ndarray, cutoff: float, order: int = 2) -> np.ndarray:
    b, a = butter(order, min(cutoff / (SR / 2), 0.99), btype="high")
    return lfilter(b, a, x)


# ---- instruments (return mono buffers) ------------------------------------------------------

def kick(rng) -> np.ndarray:
    n = int(0.35 * SR)
    t = np.arange(n) / SR
    f = 50 + 110 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 9) * 1.1


def snare(rng) -> np.ndarray:
    n = int(0.25 * SR)
    t = np.arange(n) / SR
    noise = highpass(rng.standard_normal(n), 1500) * np.exp(-t * 18)
    tone = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 30)
    return 0.55 * noise + 0.5 * tone


def hat(rng, open_: bool = False) -> np.ndarray:
    n = int((0.18 if open_ else 0.05) * SR)
    t = np.arange(n) / SR
    return highpass(rng.standard_normal(n), 7000) * np.exp(-t * (14 if open_ else 70)) * 0.18


def crash(rng) -> np.ndarray:
    n = int(1.6 * SR)
    t = np.arange(n) / SR
    return highpass(rng.standard_normal(n), 4000) * np.exp(-t * 2.6) * 0.3


def bass_note(f: float, dur: float, style: str) -> np.ndarray:
    n = int(dur * SR)
    if style == "saw":
        x = 0.6 * osc("saw", f, n) + 0.5 * osc("square", f / 2, n)
        x = lowpass(x, 900)
    else:
        x = osc("tri", f, n) + 0.3 * osc("square", f, n)
        x = lowpass(x, 1400)
    return x * adsr(n, 0.004, 0.08, 0.7, 0.03)


def pad_chord(notes: list[int], dur: float) -> np.ndarray:
    n = int(dur * SR)
    x = np.zeros(n)
    for m in notes:
        for d in (-0.004, 0.0, 0.005):
            x += osc("saw", hz(m), n, phase=abs(d) * 37, detune=d)
    x = lowpass(x / (3 * len(notes)), 1800)
    return x * adsr(n, 0.25, 0.4, 0.8, 0.3)


def lead_note(f: float, dur: float, kind: str) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / 0.25, 0, 1)
    ph = np.cumsum(f * vib) / SR % 1.0
    if kind == "pulse":
        x = np.where(ph < 0.3, 1.0, -1.0) * 0.6 + (2 * ph - 1) * 0.4
    else:
        x = 2 * ph - 1
    x = lowpass(x, 4200)
    return x * adsr(n, 0.01, 0.12, 0.65, min(0.08, dur / 3))


def arp_note(f: float, dur: float) -> np.ndarray:
    n = int(dur * SR)
    x = osc("pulse25", f, n) * 0.5 + osc("square", f * 2, n) * 0.15
    return lowpass(x, 3000) * adsr(n, 0.002, 0.06, 0.25, 0.02)


# ---- arrangement ------------------------------------------------------------------------------

class Mix:
    def __init__(self, seconds: float):
        self.n = int(seconds * SR) + SR * 3
        self.l = np.zeros(self.n)
        self.r = np.zeros(self.n)

    def add(self, buf: np.ndarray, at: float, gain: float, pan: float = 0.0):
        i = int(at * SR)
        if i >= self.n:
            return
        buf = buf[: self.n - i]
        self.l[i:i + len(buf)] += buf * gain * (1 - max(0.0, pan))
        self.r[i:i + len(buf)] += buf * gain * (1 + min(0.0, pan))


def melody(rng, scale_root: int, scale: list[int], chords: list[str], bars: int, beats: int, rhythm_density: float):
    """A phrase generator: motifs of 2 bars repeated as A A' B A, notes biased to chord tones."""
    def scale_note(deg: int) -> int:
        o, d = divmod(deg, len(scale))
        return scale_root + 12 * o + scale[d]

    def motif():
        out = []
        t = 0.0
        deg = int(rng.integers(0, 5))
        while t < 2 * beats - 1e-6:
            dur = rng.choice([0.5, 0.5, 1.0, 1.0, 1.5, 2.0] if rng.random() > rhythm_density else [0.25, 0.5, 0.5])
            dur = min(dur, 2 * beats - t)
            step = int(rng.choice([-2, -1, -1, 0, 1, 1, 2, 3, -3]))
            deg = int(np.clip(deg + step, -2, 9))
            rest = rng.random() < 0.12
            out.append((t, dur, None if rest else deg))
            t += dur
        return out

    a, b = motif(), motif()
    a2 = [(t, d, (g + (1 if i == len(a) - 1 and g is not None else 0)) if g is not None else None) for i, (t, d, g) in enumerate(a)]
    form = [a, a2, b, a]
    notes = []
    for bar in range(0, bars, 2):
        m = form[(bar // 2) % 4]
        chord = chord_notes(chords[bar % len(chords)], 5)
        for t, d, g in m:
            if g is None:
                continue
            midi = scale_note(g)
            # land on a chord tone on strong beats
            if abs((t % beats)) < 1e-6:
                midi = min(chord + [c + 12 for c in chord], key=lambda c: abs(c - midi))
            notes.append((bar * beats + t, d, midi))
    return notes


def render(spec: dict, rng) -> tuple[np.ndarray, int]:
    bpm, beats, bars = spec["bpm"], 4, spec["bars"]
    beat = 60.0 / bpm
    root = NOTE[spec["key"]] + 12 * 4
    scale = MINOR if spec.get("mode", "minor") == "minor" else MAJOR
    chords = spec["chords"]
    length = bars * beats * beat
    mix = Mix(length)
    K, S = kick(rng), snare(rng)
    H, HO, CR = hat(rng), hat(rng, True), crash(rng)
    drums = spec.get("drums", "rock")
    intro_bars = spec.get("intro", 0)

    for bar in range(bars):
        t0 = bar * beats * beat
        chord = chords[bar % len(chords)]
        cn = chord_notes(chord, 3)
        full = bar >= intro_bars
        # pad
        mix.add(pad_chord(chord_notes(chord, 4), beats * beat), t0, spec.get("pad", 0.22))
        # bass
        if full or spec.get("bass_in_intro"):
            pattern = spec.get("bass", [0, 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5])
            for k, b in enumerate(pattern):
                nxt = pattern[k + 1] if k + 1 < len(pattern) else beats
                f = hz(cn[0] - 12 + (12 if (k % 4 == 3 and spec.get("octave_bass")) else 0))
                mix.add(bass_note(f, (nxt - b) * beat * 0.92, spec.get("bass_style", "saw")), t0 + b * beat, 0.42)
        # arpeggio
        if spec.get("arp"):
            step = spec["arp"]
            k = 0
            tt = 0.0
            while tt < beats - 1e-6:
                m = chord_notes(chord, 5)[k % 3] + (12 if (k // 3) % 2 else 0)
                mix.add(arp_note(hz(m), step * beat * 0.9), t0 + tt * beat, 0.11, pan=0.35 if k % 2 else -0.35)
                tt += step
                k += 1
        # drums
        if full and drums != "none":
            if bar % 8 == 0:
                mix.add(CR, t0, 0.8, 0.2)
            for q in range(beats * 4):
                tq = t0 + q * beat / 4
                if drums == "rock":
                    if q in (0, 8) or (q == 10 and bar % 2):
                        mix.add(K, tq, 0.9)
                    if q in (4, 12):
                        mix.add(S, tq, 0.7, -0.05)
                    if q % 2 == 0:
                        mix.add(H, tq, 0.6 if q % 4 else 0.8, 0.25)
                elif drums == "drive":
                    if q % 4 == 0 or q == 14:
                        mix.add(K, tq, 0.9)
                    if q in (4, 12):
                        mix.add(S, tq, 0.75, -0.05)
                    mix.add(HO if q % 4 == 2 else H, tq, 0.5, 0.25)
                    if bar % 4 == 3 and q >= 12:
                        mix.add(S, tq, 0.45, 0.1)
                elif drums == "half":
                    if q == 0:
                        mix.add(K, tq, 0.85)
                    if q == 8:
                        mix.add(S, tq, 0.6)
                    if q % 4 == 0:
                        mix.add(H, tq, 0.5, 0.25)
    # lead melody
    if spec.get("lead", True):
        lead_bars = bars - intro_bars
        notes = melody(rng, root + 12, scale, chords[intro_bars % len(chords):] + chords[:intro_bars % len(chords)],
                       lead_bars, beats, spec.get("density", 0.35))
        for t, d, m in notes:
            at = (intro_bars * beats + t) * beat
            mix.add(lead_note(hz(m), d * beat * 0.95, spec.get("lead_kind", "pulse")), at, spec.get("lead_gain", 0.2), 0.1)
            if spec.get("echo", True):
                mix.add(lead_note(hz(m), d * beat * 0.9, spec.get("lead_kind", "pulse")), at + beat * 0.75, 0.06, -0.4)

    # simple room reverb (feedback delays), then wrap the tail for seamless loops
    out = np.stack([mix.l, mix.r], axis=1)
    rev = np.zeros_like(out)
    for dly, g in ((0.031, 0.32), (0.047, 0.28), (0.071, 0.22), (0.113, 0.16)):
        d = int(dly * SR)
        tmp = np.zeros_like(out)
        tmp[d:] = out[:-d] * g
        rev += tmp
    out = out + lowpass(rev.T, 5000).T * 0.6
    n = int(length * SR)
    if spec.get("loop", True):
        body = out[:n].copy()
        tail = out[n:]
        body[: len(tail)] += tail[: len(body)]
        out = body
    else:
        out = out[: n + SR * 2]
        fade = int(1.5 * SR)
        out[-fade:] *= np.linspace(1, 0, fade)[:, None]
    peak = np.max(np.abs(out)) or 1.0
    out = np.tanh(out / peak * 1.4) / np.tanh(1.4) * 0.89
    return out, n


TRACKS = {
    # name: spec. Keys/progressions are common-practice; melodies are generated (seeded).
    "title":    dict(bpm=96,  key="A", chords=["Am", "F", "C", "G"], bars=32, intro=4, drums="half", arp=0.5, density=0.2, lead_kind="saw", seed=11),
    "select":   dict(bpm=118, key="D", chords=["Dm", "Bb", "C", "Am"], bars=24, intro=2, drums="rock", arp=0.25, density=0.45, seed=23),
    "fight1":   dict(bpm=150, key="E", chords=["Em", "C", "D", "B7"], bars=48, intro=2, drums="drive", arp=0.25, density=0.5, octave_bass=True, seed=31),
    "fight2":   dict(bpm=140, key="G", chords=["Gm", "Eb", "F", "D7"], bars=44, intro=2, drums="rock", arp=0.5, density=0.45, bass=[0, 0.75, 1.5, 2, 2.75, 3.5], seed=47),
    "fight3":   dict(bpm=162, key="C", chords=["Cm", "Ab", "Bb", "G7"], bars=52, intro=4, drums="drive", arp=0.25, density=0.55, lead_kind="saw", octave_bass=True, seed=53),
    "versus":   dict(bpm=130, key="E", chords=["Em", "C", "D", "B7"], bars=4, drums="drive", density=0.6, loop=False, seed=61),
    "winner":   dict(bpm=124, key="C", mode="major", chords=["C", "F", "G", "C"], bars=6, drums="rock", arp=0.25, density=0.4, loop=False, seed=71),
    "continue": dict(bpm=80,  key="A", chords=["Am", "Dm", "E7", "Am"], bars=4, drums="half", density=0.15, loop=False, lead_kind="saw", seed=83),
}


def main(names: list[str]) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name in names or list(TRACKS):
        spec = TRACKS[name]
        rng = np.random.default_rng(spec["seed"])
        audio, n = render(spec, rng)
        with tempfile.TemporaryDirectory() as tmp:
            wav = pathlib.Path(tmp) / f"{name}.wav"
            wavfile.write(wav, SR, (audio * 32767).astype(np.int16))
            dst = OUT / f"{name}.ogg"
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(wav), "-c:a", "libvorbis", "-q:a", "3", str(dst)], check=True)
        print(f"{name}: {len(audio) / SR:.1f}s loop={spec.get('loop', True)} -> {dst.relative_to(REPO)} ({dst.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main(sys.argv[1:])
