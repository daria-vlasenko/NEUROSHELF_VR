# NEUROSHELF — сводные таблицы ЛР9 из сырых CSV замеров (Отчёты/ЛР9/raw). Числа только из файлов.
# Запуск: python Tools/perf_tables.py  → Отчёты/ЛР9/50_Матрица_замеров.md
import csv, os, math, statistics as st
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "Отчёты", "ЛР9"))
RAW = os.path.join(ROOT, "raw")
CELLS = [("start", "стояние на старте"), ("aisle", "проход вдоль стеллажей"), ("grab", "взятие товара у полки"), ("maxvis", "максимум видимых объектов")]
T95 = [float("nan"), 6.314, 2.920, 2.353, 2.132, 2.015, 1.943, 1.895, 1.860, 1.833, 1.812]
M_MEAN, M_OVER = 0.5, 1.0

def load(tags):
    out = []
    for t in tags:
        p = os.path.join(RAW, t + "_summary.csv")
        if not os.path.exists(p): continue
        for r in csv.DictReader(open(p, encoding="utf-8")):
            if r["profiled"] != "0": continue
            r["run"] = t
            out.append(r)
    return out

def f(r, k):
    v = r.get(k, "")
    if v == "": return float("nan")
    x = float(v)
    if k.startswith("gpu") and (x <= 0 or x > 100): return float("nan")   # редкие недостоверные значения FrameTimingManager
    return x

def desc(xs):
    xs = [x for x in xs if not math.isnan(x)]
    if not xs: return (float("nan"),) * 4 + (0,)
    m = st.mean(xs); sd = st.stdev(xs) if len(xs) > 1 else 0.0
    t = T95[len(xs) - 1] if 1 < len(xs) <= 10 else 1.645
    se = sd / math.sqrt(len(xs)) if len(xs) > 1 else 0
    return m, sd, m - t * se, m + t * se, len(xs)

def sel(rows, cell, cond): return [r for r in rows if r["cell"] == cell and r["condition"] == cond]

def gap(rows, cell, key):
    a = {(r["run"], r["rep"]): f(r, key) for r in sel(rows, cell, "A")}
    c = {(r["run"], r["rep"]): f(r, key) for r in sel(rows, cell, "C")}
    return desc([a[k] - c[k] for k in a if k in c])

def fmt(x, d=2): return "—" if math.isnan(x) else f"{x:.{d}f}"
def sg(x, d=2): return "—" if math.isnan(x) else f"{x:+.{d}f}"

def matrix(rows, title):
    o = [f"### {title}", "", "| ячейка | усл. | среднее, мс | 1% худших (p99), мс | сверх 11.1 мс, % | CPU осн., мс | GPU, мс | вызовов | треуг., тыс | видимых | скиннинг-мешей в кадре (2 на персонажа) | GC, КБ/кадр |", "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for cell, ru in CELLS:
        for cond in "AC":
            rr = sel(rows, cell, cond)
            if not rr: continue
            m = desc([f(r, "mean_ms") for r in rr]); p = desc([f(r, "p99_ms") for r in rr]); ov = desc([f(r, "over_budget_pct") for r in rr])
            g = lambda k: desc([f(r, k) for r in rr])[0]
            o.append(f"| {ru} | {cond} | {fmt(m[0])} ± {fmt(m[1])} | {fmt(p[0])} | {fmt(ov[0],1)} | {fmt(g('cpu_main_ms'))} | {fmt(g('gpu_ms'))} | {fmt(g('draw_calls'),0)} | {fmt(g('triangles')/1000,1)} | {fmt(g('visible'),0)} | {fmt(g('visible_skinned'),1)} | {fmt(g('gc_kb_per_frame'),2)} |")
    n = len({(r['run'], r['rep']) for r in rows})
    o += ["", f"Повторов (пар A/C): {n}. Среднее ± SD по повторам.", ""]
    return o

def gaps(rows, title):
    o = [f"### {title}", "", "| ячейка | Δ среднего A − C, мс [90% ДИ] | эквивалентно ±0.5 мс? | Δ p99, мс | Δ доли сверх 11.1, п.п. [90% ДИ] | эквивалентно ±1 п.п.? | Δ GPU, мс | Δ CPU, мс |", "|---|---|---|---|---|---|---|---|"]
    for cell, ru in CELLS:
        d = gap(rows, cell, "mean_ms")
        if d[4] == 0: continue
        dp = gap(rows, cell, "p99_ms"); dov = gap(rows, cell, "over_budget_pct"); dg = gap(rows, cell, "gpu_ms"); dc = gap(rows, cell, "cpu_main_ms")
        eq = lambda x, m: "да" if x[4] > 1 and x[2] > -m and x[3] < m else "**нет**"
        o.append(f"| {ru} | {sg(d[0])} [{sg(d[2])}; {sg(d[3])}] | {eq(d, M_MEAN)} | {sg(dp[0])} | {sg(dov[0],1)} [{sg(dov[2],1)}; {sg(dov[3],1)}] | {eq(dov, M_OVER)} | {sg(dg[0])} | {sg(dc[0])} |")
    o.append("")
    return o

def articles(rows, title):
    keys = [("gpu_ms", "GPU: отрисовка кадра", 8.0), ("art_render_ms", "CPU: подготовка рендера (отсечение, SRP)", 2.5), ("art_scripts_ms", "CPU: скрипты", 1.0),
            ("art_physics_ms", "CPU: физика", 0.5), ("art_animation_ms", "CPU: анимация, IK, скиннинг", 1.0), ("art_audio_ms", "CPU: звук (основной поток)", 0.3),
            ("art_ui_ms", "CPU: интерфейсы", 0.3), ("art_ai_ms", "CPU: навигация NPC", 0.3)]
    o = [f"### {title}", "", "| статья | цель, мс | A | C |", "|---|---|---|---|"]
    for k, ru, tgt in keys:
        a = desc([f(r, k) for r in rows if r["condition"] == "A"])[0]; c = desc([f(r, k) for r in rows if r["condition"] == "C"])[0]
        o.append(f"| {ru} | ≤ {tgt} | {fmt(a, 3)} | {fmt(c, 3)} |")
    det = ["det_Animators_Update_ms", "det_AnimationScriptPlayable_ProcessAnimation_ms", "det_MeshSkinning_Update_ms", "det_Physics_Simulate_ms"]
    for k in det:
        a = desc([f(r, k) for r in rows if r["condition"] == "A"])[0]; c = desc([f(r, k) for r in rows if r["condition"] == "C"])[0]
        o.append(f"| — в т.ч. {k[4:-3]} | | {fmt(a, 3)} | {fmt(c, 3)} |")
    o.append("")
    return o

def beforeafter(b, a, title):
    o = [f"### {title}", "", "| ячейка | усл. | среднее до → после, мс | p99 до → после | сверх 11.1 до → после, % | GPU до → после | CPU до → после | разрыв A − C до → после, мс |", "|---|---|---|---|---|---|---|---|"]
    for cell, ru in CELLS:
        for cond in "AC":
            X = lambda rows, k: desc([f(r, k) for r in sel(rows, cell, cond)])[0]
            gb = gap(b, cell, "mean_ms")[0]; ga = gap(a, cell, "mean_ms")[0]
            o.append(f"| {ru} | {cond} | {fmt(X(b,'mean_ms'))} → {fmt(X(a,'mean_ms'))} | {fmt(X(b,'p99_ms'))} → {fmt(X(a,'p99_ms'))} | {fmt(X(b,'over_budget_pct'),1)} → {fmt(X(a,'over_budget_pct'),1)} | {fmt(X(b,'gpu_ms'))} → {fmt(X(a,'gpu_ms'))} | {fmt(X(b,'cpu_main_ms'))} → {fmt(X(a,'cpu_main_ms'))} | {(sg(gb) + ' → ' + sg(ga)) if cond == 'A' else ''} |")
    o.append("")
    return o

out = ["# ЛР9 — матрица замеров (сгенерировано Tools/perf_tables.py из Отчёты/ЛР9/raw)", ""]
sets = [("2160 × 1200 (пиксели экранов Vive, нижняя граница)", ["before_1", "before_2"], ["after_1", "after_2"]),
        ("3024 × 1680 (рендер-таргет SteamVR для Vive, оценка нагрузки шлема)", ["vr_before_1", "vr_before_2"], ["vr_after_1", "vr_after_2"])]
for name, bt, at in sets:
    b, a = load(bt), load(at)
    if not b or not a: continue
    out += [f"## Окно {name}, чередующийся замер {', '.join(bt + at)}", ""]
    out += matrix(b, "До оптимизации") + matrix(a, "После оптимизации")
    out += gaps(b, "Разрыв A − C до оптимизации") + gaps(a, "Разрыв A − C после оптимизации")
    out += beforeafter(b, a, "До → после")
    out += articles(b, "Статьи бюджета до (среднее по ячейкам)") + articles(a, "Статьи бюджета после")
base = load(["baseline"])
if base:
    out += ["## Первый замер «до» 19.10 (ноутбук сразу после сборки, нагрет), окно 2160 × 1200, 5 повторов", ""]
    out += matrix(base, "До оптимизации, 19.10") + gaps(base, "Разрыв A − C, 19.10")
open(os.path.join(ROOT, "50_Матрица_замеров.md"), "w", encoding="utf-8").write("\n".join(out))
print("\n".join(out))
