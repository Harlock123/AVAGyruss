#!/usr/bin/env python3
"""Synthesises AVAGyruss's sound effects and tunes into ../src/AVAGyruss/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the music is an original arrangement of Bach's Toccata and Fugue in D minor (public domain). Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAGyruss", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out

def organ(seconds, f, gain=1.0, release=0.25):
    """A pipe-organ-ish note: a fundamental, its octave and a little reed."""
    return mix(tone(seconds, f, kind="sine", attack=0.02, release=release, gain=gain),
               tone(seconds, f * 2, kind="sine", attack=0.02, release=release, gain=gain * 0.45),
               tone(seconds, f, kind="square", attack=0.02, release=release, gain=gain * 0.12))


def organ_line(bpm, notes, gain=0.5, low=False):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        if note == "-":
            out.extend(silence(d))
        else:
            f = hz(note)
            out.extend(mix(organ(d, f, gain), organ(d, f / 2, gain * 0.7)) if low else organ(d, f, gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ship's guns and the things they hit.
    write("Fire.wav", mix(tone(0.09, 1800, 500, "square", release=1.2), tone(0.05, 4000, 1500, "noise", release=1.0, gain=0.4)), 0.25)
    write("Hit.wav", mix(tone(0.18, 900, 160, "square", release=0.9), tone(0.16, 3000, 600, "noise", release=0.8, gain=0.6)), 0.4)
    write("Group.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["D6", "F6", "A6", "D7"]], tone(0.25, hz("F7"), kind="pulse", release=0.6)), 0.38)
    write("Dive.wav", tone(0.55, 1400, 500, "tri", release=0.4, vibrato=0.04, vib_rate=18), 0.3)
    write("EnemyFire.wav", tone(0.12, 700, 1500, "pulse", release=1.0), 0.22)
    write("Meteor.wav", mix(tone(0.9, 90, 240, "noise", release=0.6), tone(0.9, 70, 120, "square", release=0.6, gain=0.25)), 0.4)
    write("Satellite.wav", cat(*[tone(0.07, f, kind="sine", release=0.3) for f in [1200, 1600, 1200, 1600, 1200, 1600]]), 0.35)
    write("Power.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["A5", "C#6", "E6", "A6", "C#7", "E7"]], tone(0.4, hz("A7"), kind="pulse", release=0.7, vibrato=0.01)), 0.4)
    write("Explode.wav", mix(tone(1.3, 900, 60, "noise", release=1.6), tone(1.0, 200, 40, "square", release=1.4, gain=0.4)), 0.6)
    write("Warp.wav", mix(tone(2.0, 120, 2400, "saw", attack=0.05, release=0.5, gain=0.5), tone(2.0, 300, 6000, "noise", attack=0.3, release=0.6, gain=0.4)), 0.4)
    write("StageStart.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.4) for n in ["D5", "F5", "A5"]], tone(0.45, hz("D6"), kind="pulse", release=0.7)), 0.38)
    write("Chance.wav", tune(180, [("A5", 0.5), ("D6", 0.5), ("F6", 0.5), ("A6", 0.5), ("G6", 0.5), ("A6", 1.5)], [("D3", 1), ("A2", 1), ("D3", 2)]), 0.42)
    write("Perfect.wav", tune(170, [("D6", 0.5), ("F6", 0.5), ("A6", 0.5), ("D7", 0.5), ("C#7", 0.5), ("D7", 0.5), ("F7", 0.5), ("A7", 2)], [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 1), ("D3", 2)]), 0.45)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["D6", "F#6", "A6", "D7", "F#7"]]), 0.4)

    # Flying music: a driving arrangement built on the Toccata's opening figures (Bach, public domain). Loops.
    bpm = 150
    lead = [("A5", 0.125), ("G5", 0.125), ("A5", 1.75), ("-", 1), ("G5", 0.25), ("F5", 0.25), ("E5", 0.25), ("D5", 0.25), ("C#5", 1.5), ("D5", 2), ("-", 0.5),
            ("A4", 0.125), ("G4", 0.125), ("A4", 1.75), ("-", 1), ("E4", 0.5), ("F4", 0.5), ("C#4", 1.5), ("D4", 2), ("-", 0.5)]
    lead += [(n, 0.5) for n in ["D5", "A4", "F5", "A4", "E5", "A4", "F5", "A4", "D5", "A4", "G5", "A4", "F5", "A4", "E5", "A4",
                                "C#5", "A4", "E5", "A4", "G5", "A4", "E5", "A4"]]
    lead += [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 2.5)]
    roots = ["D", "D", "D", "D", "A", "A", "D", "D"] * 2 + ["D", "D", "D", "D", "Bb", "Bb", "A", "A"] + ["A", "A", "A", "A", "D", "D", "D", "D"]
    bass = []
    for r in roots:
        bass += [(r + "2", 0.5), (r + "3", 0.5)]
    hats = cat(*[cat(silence(60 / bpm / 2), tone(60 / bpm / 2, 7000, kind="noise", release=3, gain=0.5)) for _ in range(len(roots))])
    write("Music.wav", mix(seq(bpm, lead, kind="pulse", gain=0.32, release=0.4), seq(bpm, bass, kind="tri", gain=0.5, release=0.4), [h * 0.12 for h in hats]), 0.42)

    # The title: the Toccata's famous opening, on the organ.
    title = [("A4", 0.125), ("G4", 0.125), ("A4", 2.75), ("-", 0.5), ("G4", 0.25), ("F4", 0.25), ("E4", 0.25), ("D4", 0.25), ("C#4", 2), ("D4", 3), ("-", 1),
             ("A3", 0.125), ("G3", 0.125), ("A3", 2.75), ("-", 0.5), ("E3", 0.5), ("F3", 0.5), ("C#3", 2), ("D3", 3), ("-", 1)]
    write("TitleTune.wav", organ_line(76, title, low=True), 0.5)
    write("StartTune.wav", organ_line(150, [("A4", 0.125), ("G4", 0.125), ("A4", 1), ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 2)]), 0.45)
    write("GameOver.wav", organ_line(70, [("G4", 0.5), ("F4", 0.5), ("E4", 0.5), ("D4", 0.5), ("C#4", 1.5), ("D4", 3)], low=True), 0.45)


if __name__ == "__main__":
    main()
