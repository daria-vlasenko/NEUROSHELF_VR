"""NEUROSHELF — синтез звуковых файлов зала (все звуки сгенерированы, сторонних записей нет).

Запуск:  python synth_audio.py <папка_вывода>
Нужны numpy и scipy. Результат детерминирован (фиксированные seed): тот же скрипт даёт те же файлы.

Петли (гул витрины, вентиляция, бормотание) — шум и тона с перекрёстным затуханием на стыке:
генерируется N + F отсчётов, первые F отсчётов смешиваются с хвостом [N, N+F) по равной мощности,
поэтому последний отсчёт петли непрерывно переходит в первый.
Музыка — тактовая петля: хвосты нот, вышедшие за конец петли, переносятся в её начало (циклическая
свёртка), это стандартный способ получить бесшовную музыкальную петлю.
"""
import json
import sys
import numpy as np
from scipy.io import wavfile
from scipy import signal

SR = 48000
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
report = {}


def t_axis(sec):
    return np.arange(int(round(sec * SR))) / SR


def bandpass(x, lo, hi, order=4):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def lowpass(x, f, order=4):
    return signal.sosfilt(signal.butter(order, f, btype="low", fs=SR, output="sos"), x)


def highpass(x, f, order=4):
    return signal.sosfilt(signal.butter(order, f, btype="high", fs=SR, output="sos"), x)


def pink(n, rng):
    # шум 1/f: белый шум, отфильтрованный в частотной области
    w = rng.standard_normal(n)
    W = np.fft.rfft(w)
    f = np.fft.rfftfreq(n, 1 / SR)
    f[0] = f[1]
    return np.fft.irfft(W / np.sqrt(f), n)


def norm(x, peak=0.7):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def fade(x, a=0.005, b=0.01):
    na, nb = int(a * SR), int(b * SR)
    x = x.copy()
    if na:
        x[:na] *= np.linspace(0, 1, na)
    if nb:
        x[-nb:] *= np.linspace(1, 0, nb)
    return x


def crossfade_loop(x, n, f):
    """x длиной n+f → петля длиной n. Равная мощность: sin/cos."""
    y = x[:n].copy()
    k = np.linspace(0, np.pi / 2, f)
    y[:f] = x[:f] * np.sin(k) + x[n:n + f] * np.cos(k)
    return y


def seam_metrics(y):
    """Скачок на стыке относительно типичного шага сигнала. Близко к 1 — стык не выделяется."""
    d = np.abs(np.diff(y))
    jump = abs(y[0] - y[-1])
    return {"jump_on_seam": float(jump), "median_step": float(np.median(d)),
            "p99_step": float(np.percentile(d, 99)),
            "seam_vs_p99": float(jump / max(np.percentile(d, 99), 1e-12))}


def save(name, x, stereo=False):
    x = np.clip(x, -1, 1)
    data = (x * 32767).astype(np.int16)
    wavfile.write(f"{OUT}/{name}.wav", SR, data)
    ch = 2 if stereo else 1
    n = data.shape[0]
    rms = float(np.sqrt(np.mean(x.astype(np.float64) ** 2)))
    report.setdefault(name, {}).update({"seconds": round(n / SR, 3), "channels": ch,
                                         "peak_dbfs": round(20 * np.log10(np.max(np.abs(x)) + 1e-12), 1),
                                         "rms_dbfs": round(20 * np.log10(rms + 1e-12), 1)})


# ---------------- 1. гул холодильной витрины: петля 8 с ----------------
def cooler():
    rng = np.random.default_rng(11)
    n, f = 8 * SR, int(0.5 * SR)
    t = np.arange(n + f) / SR
    # компрессор питается от сети 50 Гц: гармоники 50/100/150/200/300 Гц
    h = sum(a * np.sin(2 * np.pi * fr * t + rng.uniform(0, 6.28))
            for fr, a in [(50, .5), (100, 1.0), (150, .4), (200, .25), (300, .12)])
    h *= 1 + 0.05 * np.sin(2 * np.pi * 0.25 * t)   # медленное «дыхание» мотора, 4 периода на 16 с
    body = bandpass(rng.standard_normal(n + f), 120, 900) * 0.25           # дребезг корпуса
    fan = bandpass(rng.standard_normal(n + f), 2000, 6000) * 0.05          # вентилятор конденсатора
    x = crossfade_loop(norm(h + body + fan, 0.6), n, f)
    save("amb_cooler_hum", x)
    report["amb_cooler_hum"].update(seam_metrics(x))


# ---------------- 2. вентиляция: петля 12 с ----------------
def vent():
    rng = np.random.default_rng(12)
    n, f = 12 * SR, int(1.0 * SR)
    x = pink(n + f, rng)
    x = lowpass(highpass(x, 60, 2), 1500, 4)
    t = np.arange(n + f) / SR
    x *= 1 + 0.12 * np.sin(2 * np.pi * 0.083 * t + 1.0)     # медленные порывы
    x = crossfade_loop(norm(x, 0.5), n, f)
    save("amb_ventilation", x)
    report["amb_ventilation"].update(seam_metrics(x))


# ---------------- 3. музыка: две петли по 8 тактов, до мажор ----------------
NOTE = {n: i for i, n in enumerate(["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"])}


def hz(name, octave):
    return 440.0 * 2 ** ((NOTE[name] + 12 * (octave + 1) - 69) / 12)


def fm_note(freq, dur, amp, ratio=1.0, index=2.0, decay=2.5, sr=SR):
    t = np.arange(int(dur * sr)) / sr
    env = np.exp(-decay * t) * (1 - np.exp(-t / 0.004))
    mod = index * np.exp(-3 * t) * np.sin(2 * np.pi * freq * ratio * t)
    return amp * env * np.sin(2 * np.pi * freq * t + mod)


def pad_chord(freqs, dur, amp):
    t = np.arange(int(dur * SR)) / SR
    env = np.minimum(1, t / 0.6) * np.minimum(1, (dur - t) / 0.8 + 0.0)
    env = np.clip(env, 0, 1)
    y = np.zeros_like(t)
    for fr in freqs:
        for k, a in [(1, 1.0), (2, 0.35), (3, 0.15)]:
            for det in (-0.004, 0.004):
                y += a * np.sin(2 * np.pi * fr * k * (1 + det) * t)
    return amp * env * y / (len(freqs) * 3)


def music(name, bpm, hats, seed):
    rng = np.random.default_rng(seed)
    beat = 60.0 / bpm
    bars = 8
    n = int(round(bars * 4 * beat * SR))
    tail = int(3 * SR)
    L = np.zeros(n + tail)
    R = np.zeros(n + tail)

    def put(y, start, pan=0.0):
        i = int(round(start * SR))
        y = y[: max(0, n + tail - i)]
        gl, gr = np.cos((pan + 1) * np.pi / 4), np.sin((pan + 1) * np.pi / 4)
        L[i:i + len(y)] += y * gl
        R[i:i + len(y)] += y * gr

    prog = [("C", ["C", "E", "G"]), ("A", ["A", "C", "E"]), ("F", ["F", "A", "C"]), ("G", ["G", "B", "D"])]
    melody = [("E", 5), ("G", 5), ("C", 6), ("B", 5), ("A", 5), ("G", 5), ("E", 5), ("D", 5)]
    for bar in range(bars):
        root, chord = prog[bar % 4]
        t0 = bar * 4 * beat
        put(pad_chord([hz(c, 4) for c in chord], 4 * beat + 0.6, 0.30), t0, 0.0)
        for b in (0, 2):
            put(fm_note(hz(root, 2), 1.8 * beat, 0.55, ratio=1, index=0.6, decay=1.6), t0 + b * beat, 0.0)
        arp = [chord[0], chord[1], chord[2], chord[1]] * 2
        for k, c in enumerate(arp):
            put(fm_note(hz(c, 5), beat * 1.2, 0.16, ratio=1.0, index=1.6, decay=3.0), t0 + k * beat / 2, 0.35 if k % 2 else -0.35)
        if bar % 2 == 0:
            for k in range(4):
                nm, oc = melody[(bar // 2 * 4 + k) % len(melody)]
                put(fm_note(hz(nm, oc), beat * 1.6, 0.20, ratio=2.0, index=1.0, decay=1.8), t0 + k * beat, 0.1)
        if hats:
            for k in range(4):
                h = highpass(rng.standard_normal(int(0.05 * SR)), 7000, 2) * np.exp(-np.arange(int(0.05 * SR)) / SR / 0.012)
                put(h * 0.08, t0 + k * beat + beat / 2, 0.2)
    # циклический перенос хвостов в начало — бесшовная петля
    L[:tail] += L[n:n + tail]
    R[:tail] += R[n:n + tail]
    st = np.stack([L[:n], R[:n]], axis=1)
    st = norm(st, 0.6)
    save(name, st, stereo=True)
    report[name].update({"bpm": bpm, "bars": bars, "key": "C major", "progression": "I–vi–IV–V"})
    report[name].update(seam_metrics(st[:, 0]))


# ---------------- 4. звуковой якорь: джингл 2.6 с ----------------
def jingle():
    rng = np.random.default_rng(4)
    y = np.zeros(int(2.6 * SR))
    for i, (nm, oc) in enumerate([("G", 5), ("C", 6), ("E", 6)]):
        b = fm_note(hz(nm, oc), 1.9, 0.5, ratio=3.5, index=2.2, decay=2.2)   # колокольчик (FM, негармонический спектр)
        s = int(i * 0.28 * SR)
        y[s:s + len(b)] += b[: len(y) - s]
        # шейкер: широкополосный шум выше 4 кГц — спектральные признаки, по которым HRTF различает «спереди/сзади»
        sh = highpass(rng.standard_normal(int(0.09 * SR)), 4000, 4) * np.exp(-np.arange(int(0.09 * SR)) / SR / 0.025) * 0.18
        y[s:s + len(sh)] += sh
    save("stim_anchor_jingle", norm(fade(y, 0.002, 0.2), 0.7))


# ---------------- 5. касса ----------------
def checkout():
    t = t_axis(0.11)
    beep = (np.sin(2 * np.pi * 2400 * t) + 0.25 * np.sin(2 * np.pi * 7200 * t))
    save("chk_scanner_beep", norm(fade(beep, 0.004, 0.006), 0.6))
    rng = np.random.default_rng(5)
    t = t_axis(1.4)
    buzz = bandpass(rng.standard_normal(len(t)), 1500, 5000) * (0.6 + 0.4 * np.sign(np.sin(2 * np.pi * 180 * t)))
    env = np.clip(np.minimum(t / 0.03, (1.25 - t) / 0.05), 0, 1)
    y = buzz * env * 0.5
    tear = bandpass(rng.standard_normal(int(0.08 * SR)), 800, 6000) * np.exp(-np.arange(int(0.08 * SR)) / SR / 0.02)
    i = int(1.28 * SR)
    y[i:i + len(tear)] += tear * 0.9
    save("chk_receipt_print", norm(fade(y), 0.55))


# ---------------- 6. шаги по кафелю ×4 ----------------
def steps():
    for k in range(4):
        rng = np.random.default_rng(60 + k)
        n = int(0.26 * SR)
        tt = np.arange(n) / SR
        heel = bandpass(rng.standard_normal(n), 900 + 150 * k, 6000) * np.exp(-tt / 0.018)
        thump = np.sin(2 * np.pi * (95 + 12 * k) * tt) * np.exp(-tt / 0.045)
        toe = np.zeros(n)
        j = int((0.07 + 0.01 * k) * SR)
        toe[j:] = bandpass(rng.standard_normal(n - j), 1500, 7000) * np.exp(-np.arange(n - j) / SR / 0.012) * 0.45
        save(f"npc_step_{k + 1:02d}", norm(fade(heel * 0.8 + thump * 0.6 + toe, 0.0005, 0.02), 0.6))


# ---------------- 7. бормотание покупателей: петля 8 с ----------------
def murmur():
    rng = np.random.default_rng(7)
    n, f = 8 * SR, int(0.8 * SR)
    total = n + f
    y = np.zeros(total)
    vowels = [(730, 1090), (270, 2290), (530, 1840), (570, 840), (300, 870), (660, 1720)]   # F1/F2 гласных
    for v in range(4):
        f0 = rng.uniform(105, 210)
        t = np.arange(total) / SR
        f0t = f0 * (1 + 0.04 * np.sin(2 * np.pi * rng.uniform(0.2, 0.5) * t))
        ph = np.cumsum(2 * np.pi * f0t / SR)
        src = signal.sawtooth(ph) * 0.5
        out = np.zeros(total)
        pos = int(rng.uniform(0, 0.3) * SR)
        while pos < total:
            syl = int(rng.uniform(0.14, 0.26) * SR)
            gap = int(rng.uniform(0.02, 0.35) * SR)
            e = min(total, pos + syl)
            F1, F2 = vowels[rng.integers(len(vowels))]
            seg = src[pos:e]
            seg = bandpass(seg, F1 * 0.8, F1 * 1.2, 2) + 0.6 * bandpass(seg, F2 * 0.85, F2 * 1.15, 2)
            seg *= np.hanning(len(seg)) if len(seg) > 8 else 1
            out[pos:e] += seg
            pos = e + gap
        y += out * rng.uniform(0.6, 1.0)
    y = lowpass(y, 3000, 4)   # неразборчиво: речь «за спиной», без согласных
    x = crossfade_loop(norm(y, 0.5), n, f)
    save("npc_murmur", x)
    report["npc_murmur"].update(seam_metrics(x))


# ---------------- 8. дверь и колокольчик входа ----------------
def door():
    rng = np.random.default_rng(8)
    t = t_axis(1.2)
    sweep = bandpass(rng.standard_normal(len(t)), 150, 1200) * np.sin(np.pi * np.clip(t / 0.9, 0, 1)) ** 2
    click = np.zeros(len(t))
    i = int(1.0 * SR)
    m = int(0.03 * SR)
    click[i:i + m] = bandpass(rng.standard_normal(m), 1000, 5000) * np.exp(-np.arange(m) / SR / 0.006)
    save("env_door_open", norm(fade(sweep * 0.6 + click, 0.01, 0.05), 0.55))
    y = np.zeros(int(1.8 * SR))
    for k, (fr, s) in enumerate([(hz("E", 5), 0.0), (hz("C", 5), 0.45)]):
        tt = t_axis(1.3)
        b = (np.sin(2 * np.pi * fr * tt) + 0.3 * np.sin(2 * np.pi * 2.0 * fr * tt) + 0.1 * np.sin(2 * np.pi * 3.0 * fr * tt)) * np.exp(-tt / 0.45)
        j = int(s * SR)
        y[j:j + len(b)] += b[: len(y) - j]
    save("env_entry_chime", norm(fade(y, 0.002, 0.1), 0.5))


# ---------------- 9. интерфейс ----------------
def ui():
    t = t_axis(0.03)
    save("ui_hover", norm(np.sin(2 * np.pi * 1800 * t) * np.exp(-t / 0.006), 0.35))
    rng = np.random.default_rng(9)
    t = t_axis(0.07)
    press = bandpass(rng.standard_normal(len(t)), 2000, 8000) * np.exp(-t / 0.004) * 0.6 + np.sin(2 * np.pi * 900 * t) * np.exp(-t / 0.02)
    save("ui_press", norm(fade(press, 0.0005, 0.01), 0.55))
    t = t_axis(0.09)
    buzz = signal.square(2 * np.pi * 180 * t) * 0.6 + np.sin(2 * np.pi * 180 * t) * 0.4
    one = lowpass(buzz, 2500) * np.clip(np.minimum(t / 0.004, (0.09 - t) / 0.01), 0, 1)
    gap = np.zeros(int(0.07 * SR))
    save("ui_deny", norm(np.concatenate([one, gap, one]), 0.55))


cooler(); vent()
music("mus_slow_64bpm", 64, hats=False, seed=31)
music("mus_fast_104bpm", 104, hats=True, seed=32)
jingle(); checkout(); steps(); murmur(); door(); ui()
with open(f"{OUT}/_synth_report.json", "w", encoding="utf-8") as fp:
    json.dump(report, fp, ensure_ascii=False, indent=1)
print(json.dumps(report, ensure_ascii=False, indent=1))
