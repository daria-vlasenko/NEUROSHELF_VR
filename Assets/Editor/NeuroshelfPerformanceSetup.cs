// NEUROSHELF — профилирование и оптимизация производительности.
// Отдельный файл; остальные скрипты Editor не трогает.
//
// Меню NEUROSHELF → Производительность:
//   11.1  Инвентарь сцены и настроек            → 01_Инвентарь_сцены.txt
//   11.2  Сборка Development + Autoconnect Profiler → Builds/NS_Perf_<метка>/NEUROSHELF_VR.exe
//   11.3  Замер в сборке «до» / 11.13 «после»     → raw/<метка>_summary.csv, кадры, .raw профайлера
//   (остальные пункты — ниже по файлу)
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР9\

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static partial class NeuroshelfPerformanceSetup
{
    const string Menu = "NEUROSHELF/Производительность/";
    public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР9"));
    public static string RawDir => Path.Combine(OutDir, "raw");
    static string BuildDir(string tag) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "NS_Perf_" + tag));
    static string Exe(string tag) => Path.Combine(BuildDir(tag), "NEUROSHELF_VR.exe");
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // окно сборки = число пикселей двух глаз HTC Vive (2 × 1080 × 1200)
    public const int BenchWidth = 2160, BenchHeight = 1200;

    static NeuroshelfPerformanceSetup() { EditorApplication.update -= WatchRun; EditorApplication.update += WatchRun; }

    static void W(string file, string text)
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), text, new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] " + file);
    }

    // ===================== 11.2 СБОРКА =====================

    [MenuItem(Menu + "11.2a Сборка «до» (Development + Autoconnect Profiler)", priority = 702)] static void BuildBefore() => Build("before");
    [MenuItem(Menu + "11.2b Сборка «после» (Development + Autoconnect Profiler)", priority = 703)] static void BuildAfter() => Build("after");

    public static bool Build(string tag)
    {
        EditorSceneManager.SaveOpenScenes();
        // Галочки окна Build Profiles: Development Build и Autoconnect Profiler — то, что спросят на защите.
        EditorUserBuildSettings.development = true;
        EditorUserBuildSettings.connectProfiler = true;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = false;   // глубокое профилирование искажает время
        PlayerSettings.enableFrameTimingStats = true;                    // время CPU/GPU по кадрам (FrameTimingManager)
        var opts = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = Exe(tag),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development | BuildOptions.ConnectWithProfiler,
        };
        var sw = Stopwatch.StartNew();
        var rep = BuildPipeline.BuildPlayer(opts);
        var s = rep.summary;
        var msg = string.Format(ci, "Сборка «{0}»: {1}, {2:0} с, {3:0.0} МБ, ошибок {4}, предупреждений {5} → {6}\nОпции: {7}",
            tag, s.result, sw.Elapsed.TotalSeconds, s.totalSize / 1048576.0, s.totalErrors, s.totalWarnings, opts.locationPathName, s.options);
        Debug.Log("[NEUROSHELF] " + msg);
        Directory.CreateDirectory(RawDir);
        File.WriteAllText(Path.Combine(RawDir, tag + "_build.txt"), msg + "\n", new UTF8Encoding(true));
        return s.result == BuildResult.Succeeded;
    }

    // ===================== 11.3 ЗАПУСК ЗАМЕРА В СБОРКЕ =====================

    [MenuItem(Menu + "11.3a Замер в сборке «до» (≈12 мин, ноутбук не трогать)", priority = 704)] static void RunBefore() => RunBench("before", "before");
    [MenuItem(Menu + "11.3b Замер в сборке «после» (≈12 мин, ноутбук не трогать)", priority = 705)] static void RunAfter() => RunBench("after", "after");
    [MenuItem(Menu + "11.3c CPU или GPU: полное окно против ¼ пикселей, сборка «до» (≈3 мин)", priority = 706)] static void RunRes() => RunBench("before", "before_res", reps: 3, profiled: false, extra: "-nsres");
    [MenuItem(Menu + "11.3d CPU или GPU: полное окно против ¼ пикселей, сборка «после» (≈3 мин)", priority = 707)] static void RunResAfter() => RunBench("after", "after_res", reps: 3, profiled: false, extra: "-nsres");
    [MenuItem(Menu + "11.3e Короткий пробный запуск сборки «до» (1 повтор)", priority = 708)] static void RunSmoke() => RunBench("before", "smoke", BenchWidth, BenchHeight, 1, 4f, false);

    [MenuItem(Menu + "11.8a Окклюзия в сборке «до»: видимые объекты вдоль/поперёк (≈1 мин)", priority = 740)] static void OccBefore() => RunBench("before", "before_occl", extra: "-nsocc");
    [MenuItem(Menu + "11.8b Окклюзия в сборке «после»: видимые объекты вдоль/поперёк (≈1 мин)", priority = 741)] static void OccAfter() => RunBench("after", "after_occl", extra: "-nsocc");

    // Пробный запуск автопилота в Play Mode редактора — только для отладки, цифры редактора в отчёт не идут.
    public static void RunInEditor(string tag, string argsLine)
    {
        Directory.CreateDirectory(RawDir);
        var a = new List<string> { "-nsbench", "-nstag", tag, "-nsout", RawDir };
        a.AddRange(argsLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        File.WriteAllText(Path.Combine(Application.dataPath, "..", "Temp", "ns_bench_args.txt"), string.Join("\n", a));
        EditorSceneManager.SaveOpenScenes();
        EditorApplication.isPlaying = true;
    }
    [MenuItem(Menu + "_отладка: автопилот в редакторе (1 повтор, 3 с)", priority = 790)] static void DbgEditor() => RunInEditor("editor_dbg", "-nsreps 1 -nsdur 3 -nswarm 1 -nssettle 2 -nsprofile 0");
    [MenuItem(Menu + "_отладка: окклюзия в редакторе", priority = 791)] static void DbgEditorOcc() => RunInEditor("editor_occl", "-nsocc");

    static Process running; static string runningTag;

    public static void RunBench(string build, string tag, int w = BenchWidth, int h = BenchHeight, int reps = 5, float dur = 10f, bool profiled = true, string extra = "")
    {
        if (!File.Exists(Exe(build))) { Debug.LogError("[NEUROSHELF] нет сборки " + Exe(build) + " — сначала 11.2"); return; }
        Directory.CreateDirectory(RawDir);
        foreach (var f in Directory.GetFiles(RawDir, tag + "_*")) if (!f.EndsWith("_build.txt")) File.Delete(f);
        var args = string.Join(" ", new[]
        {
            "-screen-fullscreen 0", $"-screen-width {w}", $"-screen-height {h}",
            "-nsbench", $"-nstag {tag}", $"-nsout \"{RawDir}\"", $"-nsreps {reps}", $"-nsdur {dur.ToString(ci)}", $"-nsprofile {(profiled ? 1 : 0)}",
            $"-logFile \"{Path.Combine(RawDir, tag + "_player.log")}\"", extra,
        });
        running = Process.Start(new ProcessStartInfo(Exe(build), args) { UseShellExecute = false, WorkingDirectory = BuildDir(build) });
        runningTag = tag;
        SessionState.SetString("ns_perf_running", tag);
        Debug.Log($"[NEUROSHELF] Замер «{tag}» запущен: {Exe(build)} {args}");
    }

    // Чередующийся замер: сборки «до» и «после» запускаются по очереди (до, после, до, после) в одной сессии.
    // Так медленный дрейф ноутбука (нагрев после сборки, режим питания, фон Windows) ложится на обе сборки
    // одинаково, а не на одну. Очередь хранится в SessionState и переживает перекомпиляцию.
    const string QueueKey = "ns_perf_queue";
    [MenuItem(Menu + "11.12 Итоговый замер: до, после, до, после (≈35 мин, ноутбук не трогать)", priority = 760)]
    public static void Interleaved()
    {
        var q = new[]
        {
            "before|before_1|3|0|60", "after|after_1|3|1|0", "before|before_2|3|0|0", "after|after_2|3|0|0",
        };
        SessionState.SetString(QueueKey, string.Join(";", q));
        Debug.Log("[NEUROSHELF] 11.12 Очередь замеров: " + string.Join(", ", q));
        NextInQueue();
    }

    // Тот же чередующийся замер при нагрузке шлема: окно 3024 × 1680 = два глаза по 1512 × 1680.
    // Столько пикселей рендерится для HTC Vive по рекомендации Valve: экран 1080 × 1200 на глаз, но кадр рисуется
    // в 1.4 раза больше по каждой оси, чтобы после компенсации искажения линз в центре не терялась резкость
    // (Vlachos, Advanced VR Rendering, GDC 2015). Окно 2160 × 1200 — нижняя граница, это — оценка нагрузки шлема.
    public const int VrWidth = 3024, VrHeight = 1680;
    [MenuItem(Menu + "11.13 Итоговый замер при нагрузке шлема 3024×1680: до, после, до, после (≈30 мин)", priority = 761)]
    public static void InterleavedVr()
    {
        var q = new[] { "before|vr_before_1|3|0|30|vr", "after|vr_after_1|3|0|0|vr", "before|vr_before_2|3|0|0|vr", "after|vr_after_2|3|0|0|vr" };
        SessionState.SetString(QueueKey, string.Join(";", q));
        Debug.Log("[NEUROSHELF] 11.13 Очередь замеров (нагрузка шлема): " + string.Join(", ", q));
        NextInQueue();
    }

    static void NextInQueue()
    {
        var q = SessionState.GetString(QueueKey, "");
        if (q == "") return;
        var items = q.Split(';').ToList();
        var f = items[0].Split('|');
        items.RemoveAt(0);
        SessionState.SetString(QueueKey, string.Join(";", items));
        bool vr = f.Length > 5 && f[5] == "vr";
        RunBench(f[0], f[1], vr ? VrWidth : BenchWidth, vr ? VrHeight : BenchHeight, reps: int.Parse(f[2], ci), profiled: f[3] == "1", extra: $"-nscool {f[4]}");
    }

    static void WatchRun()
    {
        if (running == null)
        {
            // после перекомпиляции ссылка на процесс теряется — продолжаем очередь по файлу «готово»
            var tag = SessionState.GetString("ns_perf_running", "");
            if (tag != "" && File.Exists(Path.Combine(RawDir, tag + "_done.txt")))
            {
                SessionState.EraseString("ns_perf_running");
                Debug.Log($"[NEUROSHELF] Замер «{tag}» завершён (по файлу)");
                NextInQueue();
            }
            return;
        }
        try { if (!running.HasExited) return; } catch { }
        Debug.Log($"[NEUROSHELF] Замер «{runningTag}» завершён, код {running.ExitCode}; итог → {Path.Combine(RawDir, runningTag + "_summary.csv")}");
        running = null;
        SessionState.EraseString("ns_perf_running");
        NextInQueue();
    }
}

// ===================== 11.6 СВОДНАЯ МАТРИЦА ЗАМЕРОВ =====================
// Единица наблюдения — повтор ячейки (10 с записи), а не отдельный кадр: соседние кадры зависят друг
// от друга, и t-критерий по тысячам кадров сделал бы «значимой» любую разницу в сотые доли миллисекунды.
// A и C в каждом повторе идут подряд (порядок чередуется), поэтому разность A − C считается парно по повторам.
// Эквивалентность — TOST (два односторонних t-критерия, α = 0.05): разрыв считается незначимым, если
// 90%-й доверительный интервал разности целиком лежит внутри границы ±Δ. Границы объявлены до замеров:
//   Δ = 0.5 мс для среднего времени кадра; Δ = 1 п.п. для доли кадров сверх 11.1 мс.
public static partial class NeuroshelfPerformanceSetup
{
    public const float MarginMeanMs = 0.5f, MarginOverPp = 1.0f;
    static readonly double[] T95 = { double.NaN, 6.314, 2.920, 2.353, 2.132, 2.015, 1.943, 1.895, 1.860, 1.833, 1.812 }; // t(0.95; df)

    public class Row { public string tag, cond, cell; public int rep; public bool prof; public Dictionary<string, double> v = new Dictionary<string, double>(); }

    // «before» = все файлы before_summary.csv, before_1_summary.csv, before_2_summary.csv … (чередующийся замер);
    // номера повторов из разных запусков сдвигаются, чтобы пары A/C не смешались.
    public static List<Row> ReadSummary(string tag)
    {
        var rows = new List<Row>();
        var files = new List<string>();
        var exact = Path.Combine(RawDir, tag + "_summary.csv");
        if (File.Exists(exact)) files.Add(exact);
        for (int k = 1; k <= 9; k++) { var f = Path.Combine(RawDir, $"{tag}_{k}_summary.csv"); if (File.Exists(f)) files.Add(f); }
        int offset = 0;
        foreach (var path in files)
        {
            var part = ReadOne(path);
            foreach (var r in part) r.rep += offset;
            offset += 100;
            rows.AddRange(part);
        }
        return rows;
    }

    static List<Row> ReadOne(string path)
    {
        var rows = new List<Row>();
        var lines = File.ReadAllLines(path);
        var head = lines[0].Split(',');
        foreach (var l in lines.Skip(1).Where(x => x.Trim().Length > 0))
        {
            var f = l.Split(',');
            var r = new Row { tag = f[0], cond = f[1], cell = f[2], rep = int.Parse(f[3], ci), prof = f[4] == "1" };
            for (int i = 5; i < f.Length && i < head.Length; i++)
                if (double.TryParse(f[i], NumberStyles.Float, ci, out var d)) r.v[head[i]] = d;
            rows.Add(r);
        }
        return rows;
    }

    public struct Stat { public int n; public double mean, sd, lo90, hi90; }

    public static Stat Describe(IEnumerable<double> xs)
    {
        var a = xs.Where(x => !double.IsNaN(x)).ToArray();
        var s = new Stat { n = a.Length };
        if (a.Length == 0) { s.mean = s.sd = s.lo90 = s.hi90 = double.NaN; return s; }
        s.mean = a.Average();
        s.sd = a.Length > 1 ? Math.Sqrt(a.Sum(x => (x - s.mean) * (x - s.mean)) / (a.Length - 1)) : 0;
        double t = a.Length - 1 < T95.Length && a.Length > 1 ? T95[a.Length - 1] : 1.645;
        double se = a.Length > 1 ? s.sd / Math.Sqrt(a.Length) : 0;
        s.lo90 = s.mean - t * se; s.hi90 = s.mean + t * se;
        return s;
    }

    static double V(Row r, string k) => r.v.TryGetValue(k, out var d) ? d : double.NaN;

    // парные разности A − C по повторам одной ячейки
    public static Stat Gap(List<Row> rows, string cell, string key) =>
        Describe(rows.Where(r => !r.prof && r.cell == cell && r.cond == "A")
            .Join(rows.Where(r => !r.prof && r.cell == cell && r.cond == "C"), a => a.rep, c => c.rep, (a, c) => V(a, key) - V(c, key)));

    public static bool Equivalent(Stat d, double margin) => d.n > 1 && d.lo90 > -margin && d.hi90 < margin;
}

// ===================== 11.4 ОПТИМИЗАЦИИ ПО СЛОЯМ =====================
public static partial class NeuroshelfPerformanceSetup
{
    const string LodDir = "Assets/_Project/Art/Characters/LOD";
    // доля треугольников уровня от исходного (LOD0 — меш ЛР4, уже упрощённый до 30%/20% от Mixamo)
    public static readonly float[] LodQuality = { 1f, 0.5f, 0.25f };
    // пороги переключения по расстоянию, м — обоснование в 25_ЛР9_ход_работы.md, этап 4.1
    public const float Lod1Distance = 4.2f, Lod2Distance = 6.0f, RigOffDistance = 5.0f;

    static readonly StringBuilder opLog = new StringBuilder();
    static void Op(string s) { opLog.AppendLine(s); Debug.Log("[NEUROSHELF] " + s); }

    static int Tris(Mesh m) { if (m == null) return 0; int n = 0; for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3; return n; }

    static Mesh Simplify(Mesh src, float q, string path)
    {
        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityMeshSimplifier.MeshSimplifier")).FirstOrDefault(x => x != null);
        if (t == null) throw new Exception("нет пакета UnityMeshSimplifier");
        var ms = Activator.CreateInstance(t);
        t.GetMethod("Initialize", new[] { typeof(Mesh) }).Invoke(ms, new object[] { src });
        t.GetMethod("SimplifyMesh", new[] { typeof(float) }).Invoke(ms, new object[] { q });
        var low = (Mesh)t.GetMethod("ToMesh", Type.EmptyTypes).Invoke(ms, null);
        low.name = Path.GetFileNameWithoutExtension(path);
        low.bindposes = src.bindposes;
        low.RecalculateBounds();
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old != null) { EditorUtility.CopySerialized(low, old); return old; }
        AssetDatabase.CreateAsset(low, path);
        return low;
    }

    // 11.4a — уровни детализации персонажей + управление риггингом и аниматором
    public static void SetupLods()
    {
        Directory.CreateDirectory(LodDir);
        var roots = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(a => a.isHuman && a.GetComponentInChildren<SkinnedMeshRenderer>(true) != null).Select(a => a.gameObject).OrderBy(g => g.name).ToArray();
        foreach (var g in roots)
        {
            // убрать прошлые уровни (повторный запуск)
            foreach (var old in g.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.name.EndsWith("_LOD1") || s.name.EndsWith("_LOD2")).ToArray())
                Object.DestroyImmediate(old.gameObject);
            var baseSmr = g.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToArray();
            var levels = new List<Renderer>[3] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            var line = new StringBuilder($"{g.name}:");
            foreach (var smr in baseSmr)
            {
                levels[0].Add(smr);
                line.Append($" {smr.name} {Tris(smr.sharedMesh)}");
                for (int k = 1; k <= 2; k++)
                {
                    var key = smr.sharedMesh.name.Replace("_LOW", "");
                    var mesh = Simplify(smr.sharedMesh, LodQuality[k], $"{LodDir}/{key}_LOD{k}.asset");
                    var go = new GameObject(smr.name + "_LOD" + k);
                    go.transform.SetParent(smr.transform.parent, false);
                    go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
                    go.layer = smr.gameObject.layer;
                    var c = go.AddComponent<SkinnedMeshRenderer>();
                    c.sharedMesh = mesh; c.bones = smr.bones; c.rootBone = smr.rootBone; c.sharedMaterials = smr.sharedMaterials;
                    c.localBounds = smr.localBounds; c.quality = smr.quality; c.updateWhenOffscreen = false;
                    c.lightProbeUsage = smr.lightProbeUsage; c.reflectionProbeUsage = smr.reflectionProbeUsage;
                    c.shadowCastingMode = smr.shadowCastingMode; c.receiveShadows = smr.receiveShadows;
                    c.skinnedMotionVectors = smr.skinnedMotionVectors;
                    levels[k].Add(c);
                    line.Append($" → LOD{k} {Tris(mesh)}");
                }
            }
            var lg = g.GetComponent<LODGroup>(); if (lg == null) lg = g.AddComponent<LODGroup>();
            lg.fadeMode = LODFadeMode.None;
            lg.SetLODs(new[] { new LOD(0.5f, levels[0].ToArray()), new LOD(0.25f, levels[1].ToArray()), new LOD(0.0f, levels[2].ToArray()) });
            lg.RecalculateBounds();
            var gov = g.GetComponent<NpcLodGovernor>(); if (gov == null) gov = g.AddComponent<NpcLodGovernor>();
            gov.lod1Distance = Lod1Distance; gov.lod2Distance = Lod2Distance; gov.rigOffDistance = RigOffDistance;
            var an = g.GetComponent<Animator>(); an.cullingMode = AnimatorCullingMode.CullCompletely; EditorUtility.SetDirty(an);
            // пороги для Scene View (fov 60°) — в игре компонент пересчитает под реальную камеру
            var lods = lg.GetLODs();
            float size = lg.size;
            lods[0].screenRelativeTransitionHeight = NpcLodGovernor.ScreenHeight(size, Lod1Distance, 60f, QualitySettings.lodBias);
            lods[1].screenRelativeTransitionHeight = NpcLodGovernor.ScreenHeight(size, Lod2Distance, 60f, QualitySettings.lodBias);
            lg.SetLODs(lods);
            line.Append(string.Format(ci, " | размер LODGroup {0:0.00} м; пороги при fov 60°, lodBias {1}: {2:0.000} / {3:0.000} доли экрана", size, QualitySettings.lodBias, lods[0].screenRelativeTransitionHeight, lods[1].screenRelativeTransitionHeight));
            Op(line.ToString());
            EditorUtility.SetDirty(g);
        }
        AssetDatabase.SaveAssets();
    }

    // 11.4b — окклюзия: разметка окклюдеров и запекание
    public static readonly string[] OccluderParts = { "Back", "Side_Left", "Side_Right", "Checkout_Counter" };
    public const float SmallestOccluder = 0.5f, SmallestHole = 0.05f;

    public static void SetupOcclusion()
    {
        var env = GameObject.Find("Environment");
        int occluders = 0, occludees = 0;
        foreach (var r in env.GetComponentsInChildren<Renderer>(true))
        {
            var go = r.gameObject;
            var f = GameObjectUtility.GetStaticEditorFlags(go);
            if ((f & StaticEditorFlags.ContributeGI) == 0 && (f & StaticEditorFlags.BatchingStatic) == 0) continue;   // не статика
            bool occ = OccluderParts.Any(p => go.name == p);
            f &= ~(StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            f |= StaticEditorFlags.OccludeeStatic;
            if (occ) { f |= StaticEditorFlags.OccluderStatic; occluders++; } else occludees++;
            GameObjectUtility.SetStaticEditorFlags(go, f);
        }
        // динамические объекты (товары с физикой, персонажи, реквизит) не Static: окклюдерами быть не могут,
        // но отсекаются стеллажами, если у рендерера включено Dynamic Occlusion — проверяется замером (11.8)
        int dyn = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!r.gameObject.isStatic) { r.allowOcclusionWhenDynamic = true; dyn++; }
        var cam = Camera.main; if (cam != null) cam.useOcclusionCulling = true;
        StaticOcclusionCulling.smallestOccluder = SmallestOccluder;
        StaticOcclusionCulling.smallestHole = SmallestHole;
        StaticOcclusionCulling.backfaceThreshold = 100f;
        var sw = Stopwatch.StartNew();
        bool ok = StaticOcclusionCulling.Compute();
        Op(string.Format(ci, "Окклюзия: окклюдеров {0} (задние стенки и боковины стеллажей, прилавок), только отсекаемых {1}, динамических с Dynamic Occlusion {2}; " +
            "Smallest Occluder {3} м (по умолчанию было 5 м — больше стеллажа 2.4 × 1.8 м), Smallest Hole {4} м; запекание {5}, {6:0.0} с, данных {7} байт",
            occluders, occludees, dyn, SmallestOccluder, SmallestHole, ok ? "успешно" : "ОШИБКА", sw.Elapsed.TotalSeconds, StaticOcclusionCulling.umbraDataSize));
    }

    // 11.4c — камера: дальняя плоскость по размеру зала
    public static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float was = cam.farClipPlane;
        cam.farClipPlane = 20f;   // диагональ зала 12 × 8 м = 14.4 м + запас; было 1000 м
        EditorUtility.SetDirty(cam);
        Op(string.Format(ci, "Камера: Far Clip {0:0} → {1:0} м (диагональ зала 14.4 м); Near {2} м не менялся; Occlusion Culling {3}", was, cam.farClipPlane, cam.nearClipPlane, cam.useOcclusionCulling));
    }

    // аниматоры реквизита: вне кадра не обновлять
    public static void SetupPropAnimators()
    {
        foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(a => !a.isHuman))
        {
            var was = a.cullingMode;
            a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            EditorUtility.SetDirty(a);
            Op($"Animator {a.name}: {was} → {a.cullingMode}");
        }
    }
}

// ===================== 11.1 ИНВЕНТАРЬ =====================
public static partial class NeuroshelfPerformanceSetup
{
    [MenuItem(Menu + "11.1 Инвентарь сцены и настроек → 01_Инвентарь (до)", priority = 701)] static void InvBefore() => Inventory("01_Инвентарь_до.txt");
    [MenuItem(Menu + "11.1b Инвентарь сцены и настроек → 02_Инвентарь (после)", priority = 701)] static void InvAfter() => Inventory("02_Инвентарь_после.txt");

    static string P(Transform t) => t.parent == null ? t.name : P(t.parent) + "/" + t.name;

    static object Get(object o, string member)
    {
        if (o == null) return null;
        var t = o.GetType();
        var p = t.GetProperty(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (p != null) return p.GetValue(o);
        var f = t.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f?.GetValue(o);
    }

    public static string OpenXRRenderMode()
    {
        var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.XR.OpenXR.OpenXRSettings")).FirstOrDefault(x => x != null);
        var m = t?.GetMethod("GetSettingsForBuildTargetGroup", new[] { typeof(BuildTargetGroup) });
        var s = m?.Invoke(null, new object[] { BuildTargetGroup.Standalone });
        return Get(s, "renderMode")?.ToString() ?? "?";
    }

    public static void Inventory(string file)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NEUROSHELF — инвентарь сцены и настроек для профилирования, " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        sb.AppendLine($"Unity {Application.unityVersion} · сцена {EditorSceneManager.GetActiveScene().path}");
        sb.AppendLine();
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled).ToArray();
        sb.AppendLine("1. РЕНДЕРЕРЫ (активные)");
        foreach (var g in rs.GroupBy(r => r.GetType().Name)) sb.AppendLine($"   {g.Key}: {g.Count()}");
        int stat = rs.Count(r => r.gameObject.isStatic), occ = rs.Count(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.OccluderStatic) != 0);
        sb.AppendLine($"   помечены Static: {stat} (окклюдеров {occ}, батчинг-статика {rs.Count(r => (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.BatchingStatic) != 0)}); в лайтмапе: {rs.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 65534)}; лайтмап-текстур: {LightmapSettings.lightmaps.Length}");
        sb.AppendLine($"   товары (ProductItem, Rigidbody, не Static): {Object.FindObjectsByType<ProductItem>(FindObjectsSortMode.None).Length}");
        sb.AppendLine();
        sb.AppendLine("2. МАТЕРИАЛЫ И ШЕЙДЕРЫ (варианты = набор ключевых слов: разные варианты разрывают пакет SRP Batcher)");
        foreach (var g in rs.SelectMany(r => r.sharedMaterials.Where(m => m != null).Select(m => (r, m))).GroupBy(x => x.m.shader.name))
        {
            sb.AppendLine($"   {g.Key}: материалов {g.Select(x => x.m).Distinct().Count()}, использований {g.Count()}");
            foreach (var mg in g.GroupBy(x => x.m))
                sb.AppendLine($"      {mg.Key.name,-26} ×{mg.Count(),3}  инстансинг {(mg.Key.enableInstancing ? "вкл" : "выкл")}  ключи [{string.Join(" ", mg.Key.shaderKeywords)}]");
        }
        sb.AppendLine();
        sb.AppendLine("3. ПЕРСОНАЖИ (скиннинг)");
        foreach (var r in rs.OfType<SkinnedMeshRenderer>())
            sb.AppendLine($"   {P(r.transform),-46} треугольников {Tris(r.sharedMesh),6}  костей {r.bones.Length}  вне кадра обновлять {r.updateWhenOffscreen}");
        foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            sb.AppendLine($"   Animator {P(a.transform),-34} Culling Mode {a.cullingMode}  Root Motion {a.applyRootMotion}");
        foreach (var rb in Object.FindObjectsByType<UnityEngine.Animations.Rigging.RigBuilder>(FindObjectsSortMode.None))
            sb.AppendLine($"   RigBuilder {P(rb.transform)}: слоёв {rb.layers.Count}, ограничений {rb.GetComponentsInChildren<UnityEngine.Animations.Rigging.IRigConstraint>(true).Length}");
        foreach (var lg in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
            sb.AppendLine($"   LODGroup {P(lg.transform)}: уровней {lg.lodCount}, " + string.Join(" / ", lg.GetLODs().Select((l, i) => $"LOD{i} {l.renderers.Sum(x => x is SkinnedMeshRenderer s ? Tris(s.sharedMesh) : 0)} тр. до {l.screenRelativeTransitionHeight:0.000}")));
        sb.AppendLine($"   LODGroup всего: {Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length}; QualitySettings.lodBias {QualitySettings.lodBias}");
        sb.AppendLine();
        sb.AppendLine("4. ИНТЕРФЕЙСЫ");
        var cv = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas).ToArray();
        sb.AppendLine($"   корневых холстов {cv.Length}, графики {cv.Sum(c => c.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length)} (ценники-холсты создаются при старте планограммой)");
        sb.AppendLine();
        sb.AppendLine("5. ЗВУК");
        var cfg = AudioSettings.GetConfiguration();
        sb.AppendLine($"   AudioSource {Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Length}; Real Voices {cfg.numRealVoices}, Virtual {cfg.numVirtualVoices}; спатиалайзер {AudioSettings.GetSpatializerPluginName()}");
        sb.AppendLine();
        sb.AppendLine("6. КАМЕРА И КОНВЕЙЕР");
        var cam = Camera.main;
        if (cam != null)
        {
            var ad = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            sb.AppendLine($"   {P(cam.transform)}: fov {cam.fieldOfView}, near {cam.nearClipPlane}, far {cam.farClipPlane}, Occlusion Culling {cam.useOcclusionCulling}, HDR {cam.allowHDR}, MSAA {cam.allowMSAA}");
            if (ad != null) sb.AppendLine($"   URP-данные камеры: Post Processing {ad.renderPostProcessing}, сглаживание {ad.antialiasing}, Depth Texture {ad.requiresDepthOption}, Opaque Texture {ad.requiresColorOption}, тени {ad.renderShadows}");
        }
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (rp != null)
        {
            sb.AppendLine($"   URP Asset {rp.name}: HDR {rp.supportsHDR}, MSAA {rp.msaaSampleCount}, Render Scale {rp.renderScale}, Depth Texture {rp.supportsCameraDepthTexture}, Opaque Texture {rp.supportsCameraOpaqueTexture}, SRP Batcher {rp.useSRPBatcher}, Dynamic Batching {rp.supportsDynamicBatching}");
            sb.AppendLine($"   GPU Resident Drawer {rp.gpuResidentDrawerMode}, основной свет — тени {rp.supportsMainLightShadows}, доп. свет {rp.additionalLightsRenderingMode}");
            var rd = (Get(rp, "m_RendererDataList") as UnityEngine.Rendering.Universal.ScriptableRendererData[])?.FirstOrDefault() as UnityEngine.Rendering.Universal.UniversalRendererData;
            if (rd != null)
            {
                sb.AppendLine($"   Renderer {rd.name}: путь {rd.renderingMode}, Depth Priming {rd.depthPrimingMode}, Native Render Pass {Get(rd, "useNativeRenderPass")}");
                foreach (var f in rd.rendererFeatures) sb.AppendLine($"      Renderer Feature {f.name}: {(f.isActive ? "ВКЛ" : "выкл")}");
            }
        }
        sb.AppendLine($"   Graphics API Windows: {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))} (авто {PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64)}); Graphics Jobs {PlayerSettings.graphicsJobs}; GPU Skinning {PlayerSettings.gpuSkinning}");
        sb.AppendLine($"   Frame Timing Stats {PlayerSettings.enableFrameTimingStats}; скриптовый бэкенд {PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone)}; стек вызовов в логе: Log {PlayerSettings.GetStackTraceLogType(LogType.Log)}, Warning {PlayerSettings.GetStackTraceLogType(LogType.Warning)}, Error {PlayerSettings.GetStackTraceLogType(LogType.Error)}");
        sb.AppendLine($"   OpenXR (Standalone) Render Mode: {OpenXRRenderMode()}");
        sb.AppendLine($"   Окклюзия: данных {StaticOcclusionCulling.umbraDataSize} байт, Smallest Occluder {StaticOcclusionCulling.smallestOccluder} м, Smallest Hole {StaticOcclusionCulling.smallestHole} м");
        sb.AppendLine($"   Качество {QualitySettings.names[QualitySettings.GetQualityLevel()]}, vSync {QualitySettings.vSyncCount}");
        W(file, sb.ToString());
    }
}

// ===================== 11.7 FRAME DEBUGGER: ЧТО РАЗБИВАЕТ ПАКЕТЫ =====================
// Окно Frame Debugger показывает события кадра по одному. Здесь то же самое читается программно
// (внутренний API редактора UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility — имена проверены
// в установленной 6000.3 дампом членов через рефлексию): для каждого события — тип (SRP Batch, Mesh, SkinOnGPU…),
// шейдер, проход, ключевые слова, число вызовов внутри пакета и причина, по которой пакет разорван.
public static partial class NeuroshelfPerformanceSetup
{
    [MenuItem(Menu + "11.7a Frame Debugger: разбор пакетов «до» (Play Mode, ≈40 с)", priority = 735)] static void FdBefore() => StartFd("до");
    [MenuItem(Menu + "11.7b Frame Debugger: разбор пакетов «после» (Play Mode, ≈40 с)", priority = 736)] static void FdAfter() => StartFd("после");

    const string FdKey = "ns_fd_label";
    static void StartFd(string label)
    {
        SessionState.SetString(FdKey, label);
        RunInEditor("fd", "-nsfd -nssettle 8");
    }

    static readonly Type FdUtil = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility, UnityEditor");
    static readonly Type FdData = Type.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData, UnityEditor");
    static int fdStage, fdIndex, fdWait; static object fdData; static readonly List<string[]> fdRows = new List<string[]>();

    [InitializeOnLoadMethod] static void FdHook() { EditorApplication.update -= FdTick; EditorApplication.update += FdTick; }

    static object FdProp(string n) => FdUtil.GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
    // лимит события меняется через само окно Frame Debugger (как щелчок по строке дерева): иначе окно
    // держит свой выбор и данные события приходят только для него
    static void FdSetLimit(int v)
    {
        var wt = Type.GetType("UnityEditor.FrameDebuggerWindow, UnityEditor");
        var w = Resources.FindObjectsOfTypeAll(wt).FirstOrDefault();
        var m = wt?.GetMethod("ChangeFrameEventLimit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { typeof(int) }, null);
        if (w != null && m != null) m.Invoke(w, new object[] { v });
        else FdUtil.GetProperty("limit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).SetValue(null, v);
    }
    static object FdF(string n) => FdData.GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(fdData);

    static void FdTick()
    {
        var label = SessionState.GetString(FdKey, "");
        if (label == "" || !EditorApplication.isPlaying) { fdStage = 0; return; }
        if (fdStage > 0) UnityEditorInternal.InternalEditorUtility.RepaintAllViews();   // без перерисовки Frame Debugger не получает кадр, когда редактор в фоне
        switch (fdStage)
        {
            case 0:
                if (!PerfBenchmark.FdReady) { if (++fdWait % 300 == 0) Debug.Log("[NEUROSHELF] Frame Debugger: жду позу"); return; }
                EditorApplication.isPaused = true;
                EditorApplication.ExecuteMenuItem("Window/Analysis/Frame Debugger");
                FdUtil.GetMethod("SetEnabled").Invoke(null, new object[] { true, 0 });
                fdStage = 1; fdWait = 0; Debug.Log("[NEUROSHELF] Frame Debugger включён, жду кадр"); return;
            case 1:
                if (++fdWait % 100 == 0) Debug.Log($"[NEUROSHELF] Frame Debugger: событий {(int)FdProp("count")}, ожидание {fdWait}");
                if (fdWait < 30 || (int)FdProp("count") == 0) return;
                Debug.Log($"[NEUROSHELF] Frame Debugger: событий {(int)FdProp("count")}, читаю");
                fdData = Activator.CreateInstance(FdData); fdRows.Clear(); fdIndex = 0; fdWait = 0;
                FdSetLimit(1); fdStage = 2; return;
            case 2:
            {
                int count = (int)FdProp("count");
                if (fdIndex >= count) { FdWrite(label, count); fdStage = 3; return; }
                bool ok = (bool)FdUtil.GetMethod("GetFrameEventData").Invoke(null, new[] { (object)fdIndex, fdData });
                // Подробные данные события (шейдер, причина разрыва) редактор отдаёт только для события, выбранного
                // в окне на переднем плане; при работе через мост окно в фоне, поэтому здесь берутся тип, имя
                // и объект события (их хватает для разбора проходов), а причины разрыва снимаются кадром окна руками.
                if ((!ok || (int)FdF("m_FrameEventIndex") != fdIndex) && ++fdWait < 3) return;
                ok = ok && (int)FdF("m_FrameEventIndex") == fdIndex;
                var evs = (Array)FdUtil.GetMethod("GetFrameEvents").Invoke(null, null);
                var ev = evs.GetValue(fdIndex);
                string type = ev.GetType().GetField("m_Type").GetValue(ev).ToString();
                var causes = (string[])FdUtil.GetMethod("GetBatchBreakCauseStrings").Invoke(null, null);
                int cause = ok ? (int)FdF("m_BatchBreakCause") : -1;
                string name = (string)FdUtil.GetMethod("GetFrameEventInfoName").Invoke(null, new object[] { fdIndex });
                var obj = FdUtil.GetMethod("GetFrameEventObject").Invoke(null, new object[] { fdIndex }) as Object;
                fdRows.Add(new[] { fdIndex.ToString(ci), type, name ?? "", obj != null ? obj.name : "", ok ? (string)FdF("m_RealShaderName") : "", ok ? (string)FdF("m_PassName") : "",
                                   ok ? ((string)FdF("shaderKeywords") ?? "") : "", ok ? FdF("m_DrawCallCount").ToString() : "", ok ? FdF("m_InstanceCount").ToString() : "",
                                   cause >= 0 && cause < causes.Length ? causes[cause] : "", ok ? (string)FdF("m_RenderTargetName") : "" });
                fdIndex++; fdWait = 0; FdSetLimit(fdIndex + 1);
                return;
            }
            case 3:
                FdUtil.GetMethod("SetEnabled").Invoke(null, new object[] { false, 0 });
                SessionState.EraseString(FdKey);
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = false;
                fdStage = 0; return;
        }
    }

    static void FdWrite(string label, int count)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"NEUROSHELF — Frame Debugger, кадр «{label}», условие A, поза «максимум видимых объектов», {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine("Снято в Play Mode редактора: Frame Debugger показывает состав кадра (что и в каком порядке рисуется), а не время — время берётся только из сборки.");
        sb.AppendLine($"Событий в кадре: {count}");
        var draws = fdRows.Where(r => r[1] == "SRPBatch" || r[1] == "Mesh" || r[1] == "InstancedMesh" || r[1] == "StaticBatch" || r[1] == "DynamicBatch" || r[1] == "HybridBatch" || r[1] == "DrawProcedural" || r[1] == "DrawProceduralIndexed").ToList();
        sb.AppendLine("По типам событий: " + string.Join(", ", fdRows.GroupBy(r => r[1]).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        int dc(string[] r) => int.TryParse(r[7], out var v) ? v : 1;
        sb.AppendLine($"Событий отрисовки {draws.Count}, из них пакетов SRP Batch {draws.Count(r => r[1] == "SRPBatch")} (число вызовов внутри пакета редактор в фоне не отдаёт — см. кадр окна Frame Debugger)");
        var causes = draws.Where(r => r[9] != "").GroupBy(r => r[9]).OrderByDescending(g => g.Count()).ToList();
        if (causes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ПРИЧИНЫ РАЗРЫВА ПАКЕТА (по числу случаев):");
            foreach (var g in causes) sb.AppendLine($"   {g.Count(),4} × {g.Key}");
        }
        sb.AppendLine();
        sb.AppendLine("ПО ПРОХОДАМ (имя прохода в дереве Frame Debugger → событий отрисовки):");
        foreach (var g in draws.GroupBy(r => r[5]).OrderByDescending(g => g.Count()))
            sb.AppendLine($"   {g.Key,-28} {g.Count(),4} событий, {g.Sum(dc),4} вызовов");
        sb.AppendLine();
        sb.AppendLine("СОБЫТИЯ (№ | тип | имя | объект | шейдер | проход | ключевые слова | вызовов | инстансов | причина разрыва | цель рендера):");
        foreach (var r in fdRows) sb.AppendLine(string.Join(" | ", r));
        W(label == "до" ? "30_Frame_Debugger_до.txt" : "31_Frame_Debugger_после.txt", sb.ToString());
    }
}

// ===================== 11.4d–h: GPU-путь URP, журнал, стерео, звук =====================
public static partial class NeuroshelfPerformanceSetup
{
    static UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset PcAsset() =>
        AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");

    // 11.4d — GPU-путь кадра. Кадр упирается в видеокарту и в число пикселей (этап 3: при ¼ пикселей GPU −65%),
    // поэтому сначала убираются полноэкранные проходы, у которых в проекте нет потребителя (видно в Frame Debugger, 30_…):
    //  • SSAO — дубль запечённого AO из ЛР3 (Ambient Occlusion 0.6 м в лайтмапах). Требует прохода DepthNormalPrepass
    //    (вся непрозрачная геометрия рисуется второй раз) и четырёх полноэкранных проходов;
    //  • Opaque Texture — проход CopyColor (копия кадра после непрозрачных) для шейдеров преломления; таких нет;
    //  • Depth Texture — копия глубины для мягких частиц и эффектов; единственным потребителем был SSAO.
    // HDR и MSAA не трогаются: это вопрос картинки, а не лишних проходов.
    public static void SetupGpuPath()
    {
        var rp = PcAsset();
        var rd = (Get(rp, "m_RendererDataList") as UnityEngine.Rendering.Universal.ScriptableRendererData[])?.FirstOrDefault();
        foreach (var f in rd.rendererFeatures.Where(f => f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion")))
        {
            Op($"Renderer Feature {f.name}: {(f.isActive ? "вкл" : "выкл")} → выкл (AO уже запечён в лайтмапы ЛР3)");
            f.SetActive(false); EditorUtility.SetDirty(f);
        }
        Op($"URP {rp.name}: Opaque Texture {rp.supportsCameraOpaqueTexture} → False, Depth Texture {rp.supportsCameraDepthTexture} → False (в проекте нет шейдеров, читающих их)");
        rp.supportsCameraOpaqueTexture = false;
        rp.supportsCameraDepthTexture = false;
        EditorUtility.SetDirty(rp); EditorUtility.SetDirty(rd);
        AssetDatabase.SaveAssets();
    }

    // 11.4f — журнал. В Development Build каждый Debug.Log пишет файл журнала и собирает стек вызовов.
    // События товара (ЛР5) и ролей рук (ЛР8) пишутся в консоль на каждом взятии — провал кадра при взятии.
    // Стек для обычных сообщений и предупреждений не нужен (источник указан в тексте), для ошибок оставлен.
    public static void SetupLogging()
    {
        foreach (var t in new[] { LogType.Log, LogType.Warning })
        {
            Op($"Stack Trace для {t}: {PlayerSettings.GetStackTraceLogType(t)} → None");
            PlayerSettings.SetStackTraceLogType(t, StackTraceLogType.None);
        }
        Op($"Stack Trace для Error/Exception/Assert остаётся {PlayerSettings.GetStackTraceLogType(LogType.Error)}");
    }

    public static void CheckStereo() =>
        Op($"OpenXR → Render Mode (Standalone): {OpenXRRenderMode()} — уже выбран, менять не нужно (проверено по OpenXRRenderSettings.cs установленного пакета 1.16.1)");

    public static void CheckAudio()
    {
        var cfg = AudioSettings.GetConfiguration();
        var src = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        Op($"Звук: Real Voices {cfg.numRealVoices} (ЛР8), источников {src.Length}, через спатиалайзер Steam Audio {src.Count(a => a.spatialize)}; фактически звучит — столбец «голосов» замера");
    }

    // ===================== 11.4 ПОЛНЫЙ ЦИКЛ =====================
    [MenuItem(Menu + "11.4 Применить оптимизации (LOD, риггинг, окклюзия, камера, GPU-путь, журнал)", priority = 710)]
    public static void ApplyAll()
    {
        opLog.Clear();
        Op("NEUROSHELF — применённые оптимизации, " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        Op("\n1. УРОВНИ ДЕТАЛИЗАЦИИ ПЕРСОНАЖЕЙ И РИГГИНГ"); SetupLods();
        Op("\n2. OCCLUSION CULLING"); SetupOcclusion();
        Op("\n3. КАМЕРА И АНИМАТОРЫ РЕКВИЗИТА"); SetupCamera(); SetupPropAnimators();
        Op("\n4. GPU-ПУТЬ URP"); SetupGpuPath();
        Op("\n5. ЖУРНАЛ"); SetupLogging();
        Op("\n6. СТЕРЕО И ЗВУК"); CheckStereo(); CheckAudio();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        // первый запуск фиксирует «было → стало»; повторный не затирает его
        W(File.Exists(Path.Combine(OutDir, "03_Протокол_оптимизаций.txt")) ? "03_Протокол_оптимизаций_повторный_запуск.txt" : "03_Протокол_оптимизаций.txt", opLog.ToString());
    }
}

// ===================== 11.6 ОТЧЁТ: МАТРИЦА, БЮДЖЕТ, РАЗРЫВ A − C =====================
public static partial class NeuroshelfPerformanceSetup
{
    static readonly string[] CellNames = { "start", "aisle", "grab", "maxvis" };
    static readonly Dictionary<string, string> CellRu = new Dictionary<string, string>
    { { "start", "стояние на старте" }, { "aisle", "проход вдоль стеллажей" }, { "grab", "взятие товара у полки" }, { "maxvis", "максимум видимых объектов" } };

    // Бюджет по статьям объявлен до оптимизации (этап 2 хода работы). Значения — цель, мс на кадр.
    public static readonly (string key, string ru, float target)[] Budget =
    {
        ("gpu",       "GPU: отрисовка кадра (оба глаза)",            8.0f),
        ("render",    "CPU: подготовка рендера (отсечение, SRP)",    2.5f),
        ("scripts",   "CPU: скрипты (Update/LateUpdate/корутины)",   1.0f),
        ("physics",   "CPU: физика",                                 0.5f),
        ("animation", "CPU: анимация, IK, скиннинг",                 1.0f),
        ("audio",     "CPU: звук (основной поток)",                  0.3f),
        ("ui",        "CPU: интерфейсы (перестроение холстов)",      0.3f),
        ("ai",        "CPU: навигация NPC",                          0.3f),
    };

    [MenuItem(Menu + "11.6 Сводная таблица замеров и разрыв A − C → 50_Матрица_замеров.txt", priority = 730)]
    public static void Report() => Report(new[] { "before", "after" }, "50_Матрица_замеров.txt");

    public static void Report(string[] tags, string file)
    {
        var sb = new StringBuilder();
        sb.AppendLine("NEUROSHELF — матрица замеров в Development Build (Windows, D3D12, окно 2160 × 1200 = пиксели двух глаз Vive)");
        sb.AppendLine("Ячейка = 2 с прогрева + 10 с записи. Повторы — единица наблюдения; в каждой ячейке среднее по повторам ± SD.");
        sb.AppendLine("«1% худших» — 99-й процентиль времени кадра; «сверх» — доля кадров дольше 11.1 мс (90 Гц).");
        sb.AppendLine($"Границы эквивалентности (объявлены до замеров): |A − C| среднего < {MarginMeanMs} мс, доли сверх бюджета < {MarginOverPp} п.п.; TOST, α = 0.05 (90% ДИ внутри границ).");
        var all = new Dictionary<string, List<Row>>();
        foreach (var tag in tags) all[tag] = ReadSummary(tag);
        foreach (var tag in tags)
        {
            var rows = all[tag];
            if (rows.Count == 0) { sb.AppendLine($"\n[{tag}] — нет данных"); continue; }
            sb.AppendLine();
            sb.AppendLine($"==== {tag.ToUpperInvariant()} ====  (повторов без профайлера: {rows.Where(r => !r.prof).Select(r => r.rep).Distinct().Count()})");
            sb.AppendLine("ячейка | усл. | среднее, мс | медиана | 1% худших (p99) | сверх 11.1, % | CPU осн. | CPU рендер | GPU | вызовов | пакетов SetPass | треуг., тыс | видимых | персонажей в кадре | голосов | перестр. UI/кадр | GC, КБ/кадр");
            foreach (var cell in CellNames)
                foreach (var cond in new[] { "A", "C" })
                {
                    var rr = rows.Where(r => !r.prof && r.cell == cell && r.cond == cond).ToList();
                    if (rr.Count == 0) continue;
                    string M(string k, string f = "0.00") { var d = Describe(rr.Select(r => V(r, k))); return d.mean.ToString(f, ci) + (d.n > 1 ? "±" + d.sd.ToString(f, ci) : ""); }
                    string A(string k, string f = "0") => Describe(rr.Select(r => V(r, k))).mean.ToString(f, ci);
                    sb.AppendLine($"{CellRu[cell]} | {cond} | {M("mean_ms")} | {A("median_ms", "0.00")} | {M("p99_ms")} | {M("over_budget_pct", "0.0")} | {A("cpu_main_ms", "0.00")} | {A("cpu_render_ms", "0.00")} | {A("gpu_ms", "0.00")} | {A("draw_calls")} | {A("setpass")} | {(Describe(rr.Select(r => V(r, "triangles"))).mean / 1000).ToString("0.0", ci)} | {A("visible")} | {A("visible_skinned", "0.0")} | {A("voices", "0.0")} | {A("ui_graphic_rebuilds", "0.00")} | {A("gc_kb_per_frame", "0.00")}");
                }
            sb.AppendLine();
            sb.AppendLine("Разрыв A − C (парно по повторам), 90% ДИ, вывод TOST:");
            sb.AppendLine("ячейка | Δ среднего, мс [90% ДИ] | эквивалентно ±0.5? | Δ p99, мс | Δ доли сверх, п.п. [90% ДИ] | эквивалентно ±1? | Δ GPU, мс | Δ CPU осн., мс");
            foreach (var cell in CellNames)
            {
                var dm = Gap(rows, cell, "mean_ms"); if (dm.n == 0) continue;
                var dp = Gap(rows, cell, "p99_ms"); var dov = Gap(rows, cell, "over_budget_pct");
                var dg = Gap(rows, cell, "gpu_ms"); var dc = Gap(rows, cell, "cpu_main_ms");
                sb.AppendLine($"{CellRu[cell]} | {dm.mean:+0.00;-0.00} [{dm.lo90:+0.00;-0.00}; {dm.hi90:+0.00;-0.00}] | {(Equivalent(dm, MarginMeanMs) ? "да" : "НЕТ")} | {dp.mean:+0.00;-0.00} | {dov.mean:+0.0;-0.0} [{dov.lo90:+0.0;-0.0}; {dov.hi90:+0.0;-0.0}] | {(Equivalent(dov, MarginOverPp) ? "да" : "НЕТ")} | {dg.mean:+0.00;-0.00} | {dc.mean:+0.00;-0.00}");
            }
            sb.AppendLine();
            sb.AppendLine("Статьи бюджета основного потока, мс (среднее по всем ячейкам и повторам), и GPU:");
            sb.AppendLine("статья | цель | A | C | укладывается?");
            foreach (var b in Budget)
            {
                string key = b.key == "gpu" ? "gpu_ms" : "art_" + b.key + "_ms";
                double a = Describe(rows.Where(r => !r.prof && r.cond == "A").Select(r => V(r, key))).mean;
                double c = Describe(rows.Where(r => !r.prof && r.cond == "C").Select(r => V(r, key))).mean;
                sb.AppendLine($"{b.ru} | ≤ {b.target:0.0} | {a:0.00} | {c:0.00} | {(Math.Max(a, c) <= b.target ? "да" : "НЕТ")}");
            }
            sb.AppendLine("подробно, мс: " + string.Join("; ", new[] { "det_Animators_Update_ms", "det_AnimationScriptPlayable_ProcessAnimation_ms", "det_MeshSkinning_Update_ms", "det_Physics_Simulate_ms", "det_Culling_ms", "det_Camera_Render_ms", "art_gpuwait_ms" }
                .Select(k => $"{k.Replace("det_", "").Replace("art_", "").Replace("_ms", "")} A {Describe(rows.Where(r => !r.prof && r.cond == "A").Select(r => V(r, k))).mean:0.000} / C {Describe(rows.Where(r => !r.prof && r.cond == "C").Select(r => V(r, k))).mean:0.000}")));
        }
        if (tags.Length == 2 && all[tags[0]].Count > 0 && all[tags[1]].Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"==== ДО → ПОСЛЕ ({tags[0]} → {tags[1]}) ====");
            sb.AppendLine("ячейка | усл. | среднее до → после, мс | p99 до → после | сверх 11.1 до → после, % | GPU до → после | разрыв A − C до → после, мс");
            foreach (var cell in CellNames)
                foreach (var cond in new[] { "A", "C" })
                {
                    double X(string tag, string k) => Describe(all[tag].Where(r => !r.prof && r.cell == cell && r.cond == cond).Select(r => V(r, k))).mean;
                    var g0 = Gap(all[tags[0]], cell, "mean_ms"); var g1 = Gap(all[tags[1]], cell, "mean_ms");
                    sb.AppendLine($"{CellRu[cell]} | {cond} | {X(tags[0], "mean_ms"):0.00} → {X(tags[1], "mean_ms"):0.00} | {X(tags[0], "p99_ms"):0.00} → {X(tags[1], "p99_ms"):0.00} | {X(tags[0], "over_budget_pct"):0.0} → {X(tags[1], "over_budget_pct"):0.0} | {X(tags[0], "gpu_ms"):0.00} → {X(tags[1], "gpu_ms"):0.00} | {(cond == "A" ? $"{g0.mean:+0.00;-0.00} → {g1.mean:+0.00;-0.00}" : "")}");
                }
        }
        W(file, sb.ToString());
    }
}

// ===================== 11.10 КАДРЫ РЕНДЕРОМ =====================
// То, что можно снять камерой без окон редактора: уровни детализации персонажа и SSAO «до / после».
// Окна (Profiler, Frame Debugger, Build Profiles, Stats, Occlusion) снимаются руками — см. 27_ЛР9_что_сделать_руками.md.
public static partial class NeuroshelfPerformanceSetup
{
    static Texture2D Shot(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture; cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        cam.targetTexture = prev; RenderTexture.active = null; rt.Release();
        return tex;
    }

    static void Save(Texture2D t, string file) { Directory.CreateDirectory(OutDir); File.WriteAllBytes(Path.Combine(OutDir, file), t.EncodeToPNG()); Debug.Log("[NEUROSHELF] кадр " + file); }

    [MenuItem(Menu + "11.10b Кадр без SSAO (после 11.10)", priority = 771)] static void ShotSsaoOff() => ShotSsao(false);

    static void ShotSsao(bool on)
    {
        var rp = PcAsset();
        var rd = (Get(rp, "m_RendererDataList") as UnityEngine.Rendering.Universal.ScriptableRendererData[])?.FirstOrDefault();
        var ssao = rd.rendererFeatures.FirstOrDefault(f => f != null && f.GetType().Name.Contains("ScreenSpaceAmbientOcclusion"));
        if (ssao == null) return;
        bool wasActive = ssao.isActive, wasDepth = rp.supportsCameraDepthTexture;
        ssao.SetActive(on); rp.supportsCameraDepthTexture = on;
        var go = new GameObject("~ShotCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        cam.fieldOfView = 60f; cam.farClipPlane = 20f;
        cam.transform.position = new Vector3(3.6f, 1.65f, 0.0f); cam.transform.rotation = Quaternion.Euler(14f, 262f, 0f);
        Shot(cam, 1200, 800); Save(Shot(cam, 1200, 800), on ? "_ssao_on.png" : "_ssao_off.png");
        Object.DestroyImmediate(go);
        ssao.SetActive(wasActive); rp.supportsCameraDepthTexture = wasDepth;
    }

    [MenuItem(Menu + "_отладка: окклюзия вкл/выкл с одной позы", priority = 792)]
    static void DbgOccShot()
    {
        var go = new GameObject("~ShotCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        cam.fieldOfView = 60f; cam.farClipPlane = 20f;
        cam.transform.position = new Vector3(-0.4f, 1.65f, -0.35f); cam.transform.rotation = Quaternion.Euler(18f, 205f, 0f);
        cam.useOcclusionCulling = true; Save(Shot(cam, 800, 500), "_dbg_occ_on.png");
        cam.useOcclusionCulling = false; Save(Shot(cam, 800, 500), "_dbg_occ_off.png");
        Object.DestroyImmediate(go);
    }

    [MenuItem(Menu + "11.10 Кадры рендером: уровни детализации, SSAO до/после", priority = 770)]
    public static void Renders()
    {
        var go = new GameObject("~ShotCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f;
        go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        try
        {
            // 1. Уровни детализации: покупатель временно ставится в центр прохода (вокруг пусто), LOD0 / LOD1 / LOD2 крупно
            var npc = GameObject.Find("NPC_Shopper_01");
            var lg = npc != null ? npc.GetComponent<LODGroup>() : null;
            if (lg != null)
            {
                var keepP = npc.transform.position; var keepR = npc.transform.rotation;
                npc.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
                var c = lg.transform.TransformPoint(lg.localReferencePoint);
                cam.transform.position = c + new Vector3(2.4f, 0.25f, 0.6f);
                cam.transform.LookAt(c);
                var lods = lg.GetLODs();
                for (int k = 0; k < 3; k++)
                {
                    lg.ForceLOD(k);
                    int tris = lods[k].renderers.Sum(r => r is SkinnedMeshRenderer sk ? Tris(sk.sharedMesh) : 0);
                    Save(Shot(cam, 600, 900), $"_lod{k}.png");
                    File.WriteAllText(Path.Combine(OutDir, $"_lod{k}.txt"), tris.ToString(ci));
                }
                lg.ForceLOD(-1);
                npc.transform.SetPositionAndRotation(keepP, keepR);
            }
            // 2. SSAO до / после: ракурс вдоль прохода (стыки пола и стеллажей). Два снимка в разных кадрах редактора:
            //    переключение Depth Texture внутри одного кадра даёт артефакт (пропадает потолок).
            ShotSsao(true);
            // второй снимок — пунктом 11.10b (отдельный кадр редактора)
        }
        finally { Object.DestroyImmediate(go); }
        Debug.Log("[NEUROSHELF] 11.10 кадры сняты (сборка подписей — Tools/perf_figures.py)");
    }
}
