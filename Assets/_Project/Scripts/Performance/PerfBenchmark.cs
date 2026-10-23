using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Автопилот замеров ЛР9. Запускается в сборке аргументом командной строки -nsbench
// (сборку запускает редакторный скрипт NEUROSHELF → Производительность). Без аргумента ничего не делает.
//
// Матрица замера: условие A и условие C × четыре ячейки:
//   start  — стояние на стартовой отметке, взгляд в зал;
//   aisle  — проход вдоль стеллажей по центру прохода, 0.64 м/с, голова «сканирует» полки ±35°;
//   grab   — у продвигаемого стеллажа: рука берёт товар с яруса 1.10 м, подносит к груди, возвращает в ячейку;
//   maxvis — поза с максимумом объектов в поле зрения (ищется перебором до замеров, одна и та же до и после).
// Маршрут задаётся скриптом, поэтому A и C проходятся одинаково: разница между ними — только условие.
// Порядок условий чередуется по повторам (A C | C A | A C …), чтобы прогрев и нагрев ноутбука не копились на одном условии.
public class PerfBenchmark : MonoBehaviour
{
    public static readonly string[] AllCells = { "start", "aisle", "grab", "maxvis" };
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // ---------- параметры (из командной строки) ----------
    string runTag = "run", outDir;
    int reps = 5;
    float warmup = 2f, duration = 10f, settle = 12f;
    bool profiledRep = true;
    string[] cells = AllCells;

    public static bool Running { get; private set; }
    public static bool FdReady;

    // В редакторе аргументы берутся из Temp/ns_bench_args.txt (пробный запуск в Play Mode), в сборке — из командной строки.
    static string[] args;
    static string[] Args
    {
        get
        {
            if (args != null) return args;
#if UNITY_EDITOR
            var f = Path.Combine(Application.dataPath, "..", "Temp", "ns_bench_args.txt");
            if (File.Exists(f)) { args = File.ReadAllText(f).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray(); File.Delete(f); return args; }
#endif
            return args = Environment.GetCommandLineArgs();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetArgs() { args = null; FdReady = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Args.Contains("-nsbench")) return;
        new GameObject("~PerfBenchmark").AddComponent<PerfBenchmark>();
    }

    static string Arg(string key, string def)
    {
        var a = Args;
        int i = Array.IndexOf(a, key);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    IEnumerator Start()
    {
        Running = true;
        DontDestroyOnLoad(gameObject);
        runTag = Arg("-nstag", runTag);
        outDir = Arg("-nsout", Path.Combine(Application.persistentDataPath, "bench"));
        reps = int.Parse(Arg("-nsreps", "5"), ci);
        duration = float.Parse(Arg("-nsdur", "10"), ci);
        warmup = float.Parse(Arg("-nswarm", "2"), ci);
        settle = float.Parse(Arg("-nssettle", "12"), ci);
        profiledRep = Arg("-nsprofile", "1") == "1";
        cells = Arg("-nscells", string.Join(";", AllCells)).Split(';');
        Directory.CreateDirectory(outDir);
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        Application.runInBackground = true;

        yield return new WaitForSeconds(1f);
        var rig = BenchRig.Take();
        float cool = float.Parse(Arg("-nscool", "0"), ci);
        if (cool > 0f) { BenchRig.Pose(rig, Cells.StartPos, Cells.StartYaw, Cells.StartPitch); yield return new WaitForSeconds(cool); }   // остывание после сборки
        var log = new StringBuilder();
        void L(string s) { log.AppendLine(s); Debug.Log("[NS-BENCH] " + s); File.WriteAllText(Path.Combine(outDir, runTag + "_meta.txt"), log.ToString()); }

        L($"метка {runTag} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        L($"Unity {Application.unityVersion} · development {Debug.isDebugBuild} · profiler supported {Profiler.supported}");
        L($"GPU {SystemInfo.graphicsDeviceName} · API {SystemInfo.graphicsDeviceType} · CPU {SystemInfo.processorType} ×{SystemInfo.processorCount} · RAM {SystemInfo.systemMemorySize} МБ");
        L($"окно {Screen.width}×{Screen.height} · полноэкранный {Screen.fullScreen} · vSync {QualitySettings.vSyncCount} · targetFrameRate {Application.targetFrameRate} · качество {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
        L($"XR-устройство активно {UnityEngine.XR.XRSettings.isDeviceActive} · режим стерео {UnityEngine.XR.XRSettings.stereoRenderingMode} · камера fov {rig.cam.fieldOfView:0.0}° far {rig.cam.farClipPlane:0.#} м · occlusion {rig.cam.useOcclusionCulling}");
        L($"остывание перед замером {cool:0} с");
        L($"высота глаз {rig.head.position.y:0.000} м · повторов {reps} · ячейка: прогрев {warmup:0.#} с + запись {duration:0.#} с · смена условия: выдержка {settle:0.#} с");

        var maxPose = BenchRig.FindMaxVisiblePose(rig);
        L(string.Format(ci, "поза maxvis: x {0:0.00} z {1:0.00} рысканье {2:0}° — в пирамиде видимости {3} объектов (перебор {4} поз)", maxPose.x, maxPose.z, maxPose.yaw, maxPose.score, maxPose.tried));

        if (Args.Contains("-nsocc"))
        {
            var rows = new List<OcclusionProbe.Row>();
            yield return OcclusionProbe.Measure(rig, rows);
            File.WriteAllText(Path.Combine(outDir, runTag + "_occlusion.txt"), log + "\n" + OcclusionProbe.Report(rows));
            File.WriteAllText(Path.Combine(outDir, runTag + "_occlusion_plan.csv"), OcclusionProbe.Dump(rows));
            File.WriteAllText(Path.Combine(outDir, runTag + "_done.txt"), "ok");
            Running = false;
            yield return new WaitForSeconds(0.5f);
            Quit();
            yield break;
        }

        var sc = ScenarioController.Instance;
        var ctx = ExperimentContext.Find();
        if (Args.Contains("-nsfd"))
        {
            // Поза для разбора Frame Debugger (только в редакторе): условие A, поза maxvis, держать позу.
            if (ctx != null && sc != null) ctx.SetCondition(Arg("-nsfdcond", "A") == "C" ? sc.conditionC : sc.conditionA);
            float tFd = 0f;
            while (true)
            {
                BenchRig.Pose(rig, new Vector2(maxPose.x, maxPose.z), maxPose.yaw, 5f);
                tFd += Time.unscaledDeltaTime;
                if (tFd > settle) FdReady = true;
                yield return null;
            }
        }
        if (Args.Contains("-nsres"))
        {
            // Проверка «во что упираемся»: в одном процессе, попеременно, полное окно и окно с ¼ пикселей.
            // Если кадр упирается в GPU, время падает примерно пропорционально числу пикселей; если в CPU — почти не меняется.
            if (ctx != null && sc != null) ctx.SetCondition(sc.conditionA);
            BenchRig.Pose(rig, Cells.StartPos, Cells.StartYaw, Cells.StartPitch);
            yield return new WaitForSeconds(settle);
            int w0 = Screen.width, h0 = Screen.height;
            var resRec = new FrameMetricsRecorder();
            var resPath = Path.Combine(outDir, runTag + "_summary.csv");
            File.WriteAllText(resPath, FrameMetricsRecorder.SummaryHeader + "\n");
            for (int rep = 1; rep <= reps; rep++)
                foreach (var full in rep % 2 == 1 ? new[] { true, false } : new[] { false, true })
                {
                    Screen.SetResolution(full ? w0 : w0 / 2, full ? h0 : h0 / 2, false);
                    yield return new WaitForSeconds(1.5f);
                    foreach (var cell in new[] { "start", "maxvis" })
                    {
                        resRec.Rescan();
                        yield return Cells.Run(cell, rig, maxPose, warmup, duration, resRec);
                        var s0 = FrameMetricsRecorder.Summarize(resRec.Samples);
                        File.AppendAllText(resPath, FrameMetricsRecorder.SummaryLine(runTag, full ? "full" : "quarter", cell, rep, false, s0) + "\n");
                        L(string.Format(ci, "повтор {0} · окно {1}×{2} · {3,-6}: среднее {4:0.00} мс, CPU осн. {5:0.00} мс, GPU {6:0.00} мс, ожидание GPU {7:0.00} мс",
                            rep, Screen.width, Screen.height, cell, s0.mean, s0.cpuMain, s0.gpu, s0.presentWait));
                    }
                }
            resRec.Dispose();
            L("ГОТОВО");
            File.WriteAllText(Path.Combine(outDir, runTag + "_done.txt"), "ok");
            Running = false;
            yield return new WaitForSeconds(0.5f);
            Quit();
            yield break;
        }
        var conds = new Dictionary<string, ExperimentConditionSO> { { "A", sc != null ? sc.conditionA : null }, { "C", sc != null ? sc.conditionC : null } };
        L($"условие A = {conds["A"]?.name} · условие C = {conds["C"]?.name}");

        var rec = new FrameMetricsRecorder();
        L("маркеры, которых нет в списке при старте (берутся по имени, если появятся позже): " + (rec.Missing.Count == 0 ? "нет" : string.Join(", ", rec.Missing)));
        var summaryPath = Path.Combine(outDir, runTag + "_summary.csv");
        File.WriteAllText(summaryPath, FrameMetricsRecorder.SummaryHeader + "\n");
        UIRebuildProbe.ByCanvas.Clear();

        int totalReps = reps + (profiledRep && Profiler.supported ? 1 : 0);
        for (int rep = 1; rep <= totalReps; rep++)
        {
            bool profiled = rep > reps;
            var order = rep % 2 == 1 ? new[] { "A", "C" } : new[] { "C", "A" };
            foreach (var cn in order)
            {
                UnityEngine.Random.InitState(9000 + rep * 10 + (cn == "A" ? 1 : 2));
                if (ctx != null && conds[cn] != null) ctx.SetCondition(conds[cn]);
                BenchRig.Pose(rig, Cells.StartPos, Cells.StartYaw, Cells.StartPitch);
                yield return new WaitForSeconds(settle);
                if (profiled)
                {
                    Profiler.logFile = Path.Combine(outDir, $"{runTag}_{cn}.raw");
                    Profiler.enableBinaryLog = true;
                    Profiler.enabled = true;
                }
                foreach (var cell in cells)
                {
                    rec.Rescan();
                    yield return Cells.Run(cell, rig, maxPose, warmup, duration, rec);
                    var s = FrameMetricsRecorder.Summarize(rec.Samples);
                    File.AppendAllText(summaryPath, FrameMetricsRecorder.SummaryLine(runTag, cn, cell, rep, profiled, s) + "\n");
                    if (rep == 1 || profiled) rec.WriteFrames(Path.Combine(outDir, $"{runTag}_{cn}_{cell}_r{rep}{(profiled ? "_prof" : "")}_frames.csv"));
                    L(string.Format(ci, "повтор {0}{1} · {2} · {3,-6}: кадров {4}, среднее {5:0.00} мс, p99 {6:0.00} мс, сверх бюджета {7:0.0}%, CPU {8:0.00} / GPU {9:0.00} мс, видимых {10:0}, вызовов {11:0}",
                        rep, profiled ? " (профайлер)" : "", cn, cell, s.frames, s.mean, s.p99, s.overPct, s.cpuMain, s.gpu, s.visible, s.draws));
                }
                if (profiled) { Profiler.enabled = false; Profiler.enableBinaryLog = false; Profiler.logFile = ""; }
            }
        }
        L("перестроения графики uGUI по холстам за весь замер (элементов): " +
          string.Join("; ", UIRebuildProbe.ByCanvas.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        L($"FrameTimingManager доступен: {rec.FrameTimingAvailable}");
        L("ГОТОВО");
        rec.Dispose();
        File.WriteAllText(Path.Combine(outDir, runTag + "_done.txt"), "ok");
        Running = false;
        yield return new WaitForSeconds(0.5f);
        Quit();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }
}

// Ячейки матрицы замера. Позы в координатах зала (x −6…6, z −4…4; стеллажи 01/02 — северный ряд, 03/04 — южный,
// центр прохода z = 0; стартовая отметка (4.3; 0.35) у восточной стены).
public static class Cells
{
    public static readonly Vector2 StartPos = new Vector2(4.30f, 0.35f);
    public const float StartYaw = 270f, StartPitch = 5f;           // лицом на запад, вдоль прохода
    public static readonly Vector2 AisleFrom = new Vector2(3.2f, 0f), AisleTo = new Vector2(-3.2f, 0f);
    public static readonly Vector2 GrabPos = new Vector2(-1.20f, -0.55f);   // перед Shelf_03 (продвигаемый)
    public const float GrabYaw = 180f, GrabPitch = 28f;
    public static readonly Vector3 GrabTarget = new Vector3(-1.20f, 1.15f, -1.35f); // ярус 1.10 м, середина стеллажа

    public static IEnumerator Run(string cell, BenchRig.Rig rig, BenchRig.MaxPose maxPose, float warmup, float duration, FrameMetricsRecorder rec)
    {
        float total = warmup + duration, t = 0f;
        bool recording = false;
        var grab = cell == "grab" ? new GrabCycle(rig) : null;
        while (t < total)
        {
            float u = Mathf.Clamp01(t / total);
            switch (cell)
            {
                case "start": BenchRig.Pose(rig, StartPos, StartYaw, StartPitch); break;
                case "aisle":
                    BenchRig.Pose(rig, Vector2.Lerp(AisleFrom, AisleTo, u), 270f + 35f * Mathf.Sin(2f * Mathf.PI * t / 4f), 8f);
                    break;
                case "grab": BenchRig.Pose(rig, GrabPos, GrabYaw, GrabPitch); grab.Step(t); break;
                case "maxvis": BenchRig.Pose(rig, new Vector2(maxPose.x, maxPose.z), maxPose.yaw, 5f); break;
            }
            if (!recording && t >= warmup) { recording = true; rec.Begin(); }
            yield return null;
            t += Time.unscaledDeltaTime;
            if (recording) rec.Tick();
        }
        grab?.Finish();
    }

    // Цикл взятия 2.5 с: рука к товару → захват штатным XRInteractionManager → к груди → обратно → отпустить в ячейку.
    class GrabCycle
    {
        readonly BenchRig.Rig rig;
        readonly ProductItem item;
        readonly XRGrabInteractable grab;
        bool held;
        public GrabCycle(BenchRig.Rig r)
        {
            rig = r;
            item = UnityEngine.Object.FindObjectsByType<ProductItem>(FindObjectsSortMode.None)
                .OrderBy(p => (ExperimentContext.PointOf(p.transform) - GrabTarget).sqrMagnitude).FirstOrDefault();
            grab = item != null ? item.GetComponent<XRGrabInteractable>() : null;
        }
        public void Step(float t)
        {
            if (rig.nf == null || grab == null) return;
            float c = t % 2.5f;
            var at = ExperimentContext.PointOf(item.transform);
            var chest = rig.head.position + rig.origin.forward * 0.35f - Vector3.up * 0.35f;
            if (c < 0.4f) rig.hand.position = Vector3.Lerp(chest, at, c / 0.4f);
            else if (c < 1.4f) { if (!held) { rig.mgr.SelectEnter((IXRSelectInteractor)rig.nf, grab); held = true; } rig.hand.position = Vector3.Lerp(rig.hand.position, chest, 0.15f); }
            else if (c < 2.0f) rig.hand.position = Vector3.Lerp(rig.hand.position, item.homeSlot != null ? item.homeSlot.transform.position : at, 0.25f);
            else if (held) { rig.mgr.SelectExit((IXRSelectInteractor)rig.nf, grab); held = false; }
        }
        public void Finish() { if (held && rig.nf != null && grab != null) rig.mgr.SelectExit((IXRSelectInteractor)rig.nf, grab); held = false; }
    }
}

// Управление позой головы и руки без шлема: трекинг отключается, поза задаётся скриптом
// (так же, как в автотестах Play Mode ЛР7–ЛР8). Высота глаз берётся из Camera Offset (1.65 м), не меняется.
public static class BenchRig
{
    public class Rig
    {
        public XROrigin xr; public Transform origin, head, hand; public Camera cam;
        public NearFarInteractor nf; public XRInteractionManager mgr;
    }

    public struct MaxPose { public float x, z, yaw; public int score, tried; }

    public static Rig Take()
    {
        var r = new Rig { xr = UnityEngine.Object.FindFirstObjectByType<XROrigin>() };
        r.origin = r.xr.Origin.transform;
        r.cam = r.xr.Camera != null ? r.xr.Camera : Camera.main;
        r.head = r.cam.transform;
        var sc = ScenarioController.Instance;
        r.nf = sc != null && sc.rig != null ? sc.rig.rightNearFar : null;
        r.hand = r.nf != null ? r.nf.transform.parent : null;
        r.mgr = r.nf != null ? r.nf.interactionManager : UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>();
        foreach (var tr in new[] { r.head, r.hand, GameObject.Find("Left Controller")?.transform })
            if (tr != null) foreach (var c in tr.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var sim = GameObject.Find("XR Device Simulator"); if (sim != null) sim.SetActive(false);
        r.head.localPosition = Vector3.zero; r.head.localRotation = Quaternion.identity;
        if (r.hand != null) r.hand.localPosition = new Vector3(0.22f, -0.45f, 0.30f);
        return r;
    }

    public static void Pose(Rig r, Vector2 xz, float yaw, float pitch)
    {
        r.origin.SetPositionAndRotation(new Vector3(xz.x, r.origin.position.y, xz.y), Quaternion.Euler(0f, yaw, 0f));
        r.head.localPosition = Vector3.zero;
        r.head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // Перебор поз по сетке зала с шагом 0.5 м и поворотом 15°: считается, сколько объектов сцены
    // (без NPC и без самого XR Origin) попадает в пирамиду видимости камеры до 15 м. Зависит только от
    // геометрии и пирамиды, не от окклюзии и не от случайных NPC — поэтому поза одна и та же до и после.
    public static MaxPose FindMaxVisiblePose(Rig r)
    {
        var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(x => x.enabled && !(x is SkinnedMeshRenderer) && !(x is LineRenderer) && !x.transform.IsChildOf(r.xr.transform)).ToArray();
        var blockers = rs.Where(x => x.name.StartsWith("Back") || x.name.StartsWith("Checkout_Counter") || x.name.StartsWith("Side_")).Select(x => x.bounds).ToArray();
        var best = new MaxPose { score = -1 };
        var planes = new Plane[6];
        float oldFar = r.cam.farClipPlane;
        var keepPos = r.origin.position; var keepRot = r.origin.rotation; var keepHead = r.head.localRotation;
        r.cam.farClipPlane = 15f;
        for (float x = -5.5f; x <= 5.51f; x += 0.5f)
            for (float z = -3.5f; z <= 3.51f; z += 0.5f)
            {
                var p = new Vector3(x, 1.0f, z);
                if (blockers.Any(b => { var e = b; e.Expand(0.5f); return e.Contains(p); })) continue;
                for (float yaw = 0; yaw < 360f; yaw += 15f)
                {
                    Pose(r, new Vector2(x, z), yaw, 5f);
                    GeometryUtility.CalculateFrustumPlanes(r.cam, planes);
                    int n = 0;
                    foreach (var q in rs) if (GeometryUtility.TestPlanesAABB(planes, q.bounds)) n++;
                    best.tried++;
                    if (n > best.score) { best.score = n; best.x = x; best.z = z; best.yaw = yaw; }
                }
            }
        r.cam.farClipPlane = oldFar;
        r.origin.SetPositionAndRotation(keepPos, keepRot); r.head.localRotation = keepHead;
        return best;
    }
}
