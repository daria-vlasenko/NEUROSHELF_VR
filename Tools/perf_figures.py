# NEUROSHELF — графики и подписанные кадры ЛР9 из сырых данных замера (Отчёты/ЛР9/raw).
# Запуск: python Tools/perf_figures.py  (нужны numpy, matplotlib, pillow). Числа берутся только из CSV.
import csv, os, sys, statistics as st
from collections import defaultdict
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Отчёты", "ЛР9"))
RAW = os.path.join(ROOT, "raw")
BUDGET = 1000 / 90
CELLS = [("start", "старт"), ("aisle", "проход"), ("grab", "взятие"), ("maxvis", "макс. видимых")]

def font(sz):
    for f in ["C:/Windows/Fonts/arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"]:
        if os.path.exists(f): return ImageFont.truetype(f, sz)
    return ImageFont.load_default()

def rows(prefix):
    out = []
    for k in [""] + [f"_{i}" for i in range(1, 10)]:
        p = os.path.join(RAW, f"{prefix}{k}_summary.csv")
        if os.path.exists(p):
            out += [r for r in csv.DictReader(open(p, encoding="utf-8")) if r["profiled"] == "0"]
    return out

def mean_of(rs, cell, cond, key):
    v = [float(r[key]) for r in rs if r["cell"] == cell and r["condition"] == cond and r[key] != ""]
    return (st.mean(v), st.stdev(v) if len(v) > 1 else 0.0) if v else (float("nan"), 0.0)

def chart_matrix(tags, fname="40_Матрица_до_после.png", window="окно 2160 × 1200"):
    data = {t: rows(t) for t in tags}
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.8), sharey=True)
    for ax, key, title in [(axes[0], "mean_ms", "Среднее время кадра"), (axes[1], "p99_ms", "1% худших кадров (99-й процентиль)")]:
        x = range(len(CELLS)); w = 0.2
        for i, (t, cond, col) in enumerate([(tags[0], "A", "#c0392b"), (tags[0], "C", "#e6a19a"), (tags[1], "A", "#1f6fb5"), (tags[1], "C", "#9cc3e6")]):
            m = [mean_of(data[t], c, cond, key) for c, _ in CELLS]
            ax.bar([xx + (i - 1.5) * w for xx in x], [a for a, _ in m], w, yerr=[s for _, s in m], color=col, capsize=3,
                   label=f"{'до' if t == tags[0] else 'после'} · {cond}")
        ax.axhline(BUDGET, color="k", ls="--", lw=1); ax.text(3.45, BUDGET + 0.15, "бюджет 11.1 мс (90 Гц)", ha="right", fontsize=9)
        ax.set_xticks(list(x)); ax.set_xticklabels([n for _, n in CELLS]); ax.set_title(title); ax.grid(axis="y", alpha=.3)
    axes[0].set_ylabel("мс"); axes[0].legend(fontsize=9, loc="upper left")
    fig.suptitle(f"Матрица замеров в Development Build, {window}: условия A и C до и после оптимизации (среднее ± SD по повторам)")
    fig.tight_layout(); fig.savefig(os.path.join(ROOT, fname), dpi=130); plt.close(fig)

def frames(tag, cond, cell):
    import glob
    fs = sorted(glob.glob(os.path.join(RAW, f"{tag}_{cond}_{cell}_r*_frames.csv")))
    fs = [f for f in fs if "_prof" not in f]
    v = []
    for f in fs[:1]:
        v = [float(r["frame_ms"]) for r in csv.DictReader(open(f, encoding="utf-8"))]
    return v

def chart_hist(tb, ta, cell="aisle", fname="41_Распределение_кадров_A_C.png", window="окно 2160 × 1200"):
    fig, axes = plt.subplots(1, 2, figsize=(13, 4.2), sharey=True)
    for ax, t, ttl in [(axes[0], tb, "до"), (axes[1], ta, "после")]:
        for cond, col in [("A", "#c0392b"), ("C", "#1f6fb5")]:
            v = frames(t, cond, cell)
            if v: ax.hist(v, bins=[i * 0.25 for i in range(0, 81)], histtype="step", lw=1.6, color=col, density=True,
                          label=f"{cond}: среднее {st.mean(v):.2f} мс, сверх 11.1 мс {100*sum(x>BUDGET for x in v)/len(v):.1f}%")
        ax.axvline(BUDGET, color="k", ls="--", lw=1); ax.set_xlim(0, 20); ax.set_title(f"{ttl}: проход вдоль стеллажей, 10 с, {window}")
        ax.set_xlabel("время кадра, мс"); ax.legend(fontsize=9); ax.grid(alpha=.3)
    fig.tight_layout(); fig.savefig(os.path.join(ROOT, fname), dpi=130); plt.close(fig)

def lod_figure():
    ims, labels = [], ["LOD0 · ближе 4.2 м", "LOD1 · 4.2–6.0 м", "LOD2 · дальше 6.0 м"]
    for k in range(3):
        p = os.path.join(ROOT, f"_lod{k}.png")
        if not os.path.exists(p): return
        ims.append(Image.open(p).convert("RGB"))
        labels[k] += f"\n{int(open(os.path.join(ROOT, f'_lod{k}.txt')).read()):,} треугольников".replace(",", " ")
    w, h = ims[0].size
    out = Image.new("RGB", (w * 3, h + 150), "white"); d = ImageDraw.Draw(out)
    for k, im in enumerate(ims):
        out.paste(im, (k * w, 150)); d.multiline_text((k * w + 20, 20), labels[k], fill="black", font=font(34))
        if k: d.line([(k * w, 150), (k * w, h + 150)], fill="white", width=4)
    out.save(os.path.join(ROOT, "20_Уровни_детализации_персонажа_пороги.png"))

def ssao_figure():
    a, b = os.path.join(ROOT, "_ssao_on.png"), os.path.join(ROOT, "_ssao_off.png")
    if not (os.path.exists(a) and os.path.exists(b)): return
    A, B = Image.open(a).convert("RGB"), Image.open(b).convert("RGB")
    w, h = A.size
    out = Image.new("RGB", (w * 2 + 10, h + 70), "white"); d = ImageDraw.Draw(out)
    out.paste(A, (0, 70)); out.paste(B, (w + 10, 70))
    d.text((20, 15), "ДО: SSAO включён (лишний проход глубины и нормалей + 4 полноэкранных)", fill="black", font=font(26))
    d.text((w + 30, 15), "ПОСЛЕ: SSAO выключен, AO запечён в лайтмапы (ЛР3)", fill="black", font=font(26))
    out.save(os.path.join(ROOT, "21_SSAO_до_после.png"))

def occlusion_plan(tag):
    p = os.path.join(RAW, f"{tag}_occlusion_plan.csv")
    if not os.path.exists(p): return
    rs = list(csv.DictReader(open(p, encoding="utf-8"), delimiter=";"))
    fig, axes = plt.subplots(2, 2, figsize=(13, 9.5))
    poses = {0: ("вдоль прохода", (4.3, 0.35), 270), 1: ("поперёк рядов", (0.0, -3.4), 0)}
    import math
    for pose in (0, 1):
        for occl in (0, 1):
            ax = axes[pose][occl]
            sel = [r for r in rs if int(r["pose"]) == pose and int(r["occl"]) == occl]
            vis = sum(int(r["visible"]) for r in sel)
            for r in sel:
                x0, z0, x1, z1 = map(float, (r["minx"], r["minz"], r["maxx"], r["maxz"]))
                if x1 - x0 > 11 and z1 - z0 > 7: continue   # пол и потолок
                col = {"product": "#e67e22", "npc": "#8e44ad", "env": "#7f8c8d", "other": "#95a5a6"}[r["kind"]]
                ax.add_patch(plt.Rectangle((x0, z0), max(x1 - x0, .05), max(z1 - z0, .05),
                             facecolor=col if r["visible"] == "1" else "none", edgecolor=col, lw=1, alpha=.85 if r["visible"] == "1" else .5,
                             ls="-" if r["visible"] == "1" else ":"))
            name, (px, pz), yaw = poses[pose]
            ax.plot(px, pz, "k^", ms=10)
            for s in (-30, 30):
                a = math.radians(yaw + s); ax.plot([px, px + 6 * math.sin(a)], [pz, pz + 6 * math.cos(a)], "k-", lw=.8)
            ax.set_xlim(-6.2, 6.2); ax.set_ylim(-4.2, 4.2); ax.set_aspect("equal")
            ax.set_title(f"{name}, Occlusion Culling {'вкл' if occl else 'выкл'}: отрисовано {vis}")
    fig.suptitle("Зал сверху: залитые — отрисованы камерой, пунктир — отсечены (серый — окружение, оранжевый — товары, фиолетовый — персонажи)")
    fig.tight_layout(); fig.savefig(os.path.join(ROOT, "22_Окклюзия_схема_сверху.png"), dpi=120); plt.close(fig)

if __name__ == "__main__":
    lod_figure(); ssao_figure(); occlusion_plan("after_occl")
    if rows("before") and rows("after"):
        chart_matrix(["before", "after"]); chart_hist("before_1", "after_1")
    if rows("vr_before") and rows("vr_after"):
        chart_matrix(["vr_before", "vr_after"], "42_Матрица_до_после_нагрузка_шлема.png", "окно 3024 × 1680 (нагрузка шлема)")
        chart_hist("vr_before_1", "vr_after_1", fname="43_Распределение_кадров_A_C_нагрузка_шлема.png", window="3024 × 1680")
    print("ok")
