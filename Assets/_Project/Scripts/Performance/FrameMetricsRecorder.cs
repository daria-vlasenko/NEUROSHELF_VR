using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;

// Сбор метрик кадра в файл — основа всех замеров ЛР9.
// Каждый кадр пишет: время кадра (часы стены), время CPU основного потока и потока рендера,
// время GPU (FrameTimingManager — нужен Player Settings → Frame Timing Stats), счётчики рендера
// (вызовы отрисовки, пакеты, SetPass, треугольники), время по статьям бюджета (маркеры профайлера
// основного потока, сгруппированные в «скрипты / физика / анимация / звук / интерфейс / ИИ / XR / рендер»),
// число видимых объектов, перестроек графики холстов и звучащих голосов.
// Имена маркеров проверены по списку ProfilerRecorderHandle.GetAvailable в установленной Unity 6000.3;
// маркер, которого в сборке нет, не ломает замер — он помечается в протоколе как «нет».
public class FrameMetricsRecorder : IDisposable
{
    public const float BudgetMs = 1000f / 90f;          // HTC Vive, 90 Гц → 11.1 мс

    public static readonly (string article, string[] markers)[] Articles =
    {
        ("scripts",   new[] { "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "Update.ScriptRunDelayedDynamicFrameRate" }),
        ("physics",   new[] { "FixedUpdate.PhysicsFixedUpdate", "PreUpdate.PhysicsUpdate", "PreLateUpdate.PhysicsLateUpdate" }),
        ("animation", new[] { "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd", "PreLateUpdate.ConstraintManagerUpdate", "PostLateUpdate.UpdateAllSkinnedMeshes" }),
        ("audio",     new[] { "PostLateUpdate.UpdateAudio", "FixedUpdate.AudioFixedUpdate" }),
        ("ui",        new[] { "PostLateUpdate.PlayerUpdateCanvases", "EarlyUpdate.UpdateCanvasRectTransform", "PostLateUpdate.UpdateRectTransform" }),
        ("ai",        new[] { "PreUpdate.AIUpdate", "PreLateUpdate.AIUpdatePostScript" }),
        ("xr",        new[] { "Initialization.XREarlyUpdate", "EarlyUpdate.XRUpdate", "PostLateUpdate.XRPostLateUpdate" }),
        ("render",    new[] { "PostLateUpdate.FinishFrameRendering", "PostLateUpdate.UpdateAllRenderers" }),
        ("gpuwait",   new[] { "Gfx.WaitForPresentOnGfxThread" }),
    };
    // отдельные подробные маркеры (не суммируются в статьи, нужны для анализа)
    public static readonly string[] Details = { "Animators.Update", "AnimationScriptPlayable.ProcessAnimation", "MeshSkinning.Update", "Physics.Simulate", "Culling", "Camera.Render", "PlayerLoop" };
    static readonly string[] RenderCounters = { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count", "Instanced Batched Draw Calls Count", "GC Allocated In Frame" };

    public struct Sample
    {
        public float t, frame, cpuMain, cpuRender, gpu, presentWait;
        public long draws, batches, setpass, tris, verts, instanced, gcBytes;
        public int visible, visibleSkinned, voices, uiGraphics, uiLayout;
        public float[] art, det;
    }

    readonly List<ProfilerRecorder>[] artRec;
    readonly ProfilerRecorder[] detRec, cntRec;
    public readonly List<string> Missing = new List<string>();
    public readonly List<Sample> Samples = new List<Sample>(4096);
    Renderer[] renderers; bool[] isSkinned; AudioSource[] sources;
    readonly FrameTiming[] ft = new FrameTiming[1];
    public bool FrameTimingAvailable { get; private set; }
    float t0;

    public FrameMetricsRecorder()
    {
        var avail = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(avail);
        var names = new Dictionary<string, ProfilerRecorderHandle>();
        foreach (var h in avail) { var n = ProfilerRecorderHandle.GetDescription(h).Name; if (!names.ContainsKey(n)) names[n] = h; }
        ProfilerRecorder Make(string n)
        {
            if (names.TryGetValue(n, out var h)) return new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
            Missing.Add(n);   // маркер ещё не зарегистрирован (появляется позже) — пробуем по имени
            return ProfilerRecorder.StartNew(ProfilerCategory.Internal, n, 1);
        }
        artRec = Articles.Select(a => a.markers.Select(Make).ToList()).ToArray();
        detRec = Details.Select(Make).ToArray();
        cntRec = RenderCounters.Select(Make).ToArray();
        Rescan();
        UIRebuildProbe.Ensure();
    }

    public void Rescan()
    {
        renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        isSkinned = renderers.Select(r => r is SkinnedMeshRenderer).ToArray();
        sources = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
    }

    // Пул массивов на 4000 кадров создаётся один раз: сам замер не должен выделять память в куче каждый кадр,
    // иначе он загрязняет счётчик «GC Allocated In Frame», который тоже измеряется.
    const int Pool = 4000;
    float[][] artPool, detPool;
    public void Begin()
    {
        Samples.Clear(); t0 = Time.realtimeSinceStartup;
        if (artPool == null)
        {
            artPool = new float[Pool][]; detPool = new float[Pool][];
            for (int i = 0; i < Pool; i++) { artPool[i] = new float[Articles.Length]; detPool[i] = new float[Details.Length]; }
        }
    }

    // Вызывать раз в кадр (из Update). Значения маркеров и счётчиков — за предыдущий кадр.
    public void Tick()
    {
        var s = new Sample { t = Time.realtimeSinceStartup - t0, frame = Time.unscaledDeltaTime * 1000f };
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, ft) > 0)
        {
            FrameTimingAvailable = true;
            s.cpuMain = (float)ft[0].cpuMainThreadFrameTime; s.cpuRender = (float)ft[0].cpuRenderThreadFrameTime;
            s.gpu = (float)ft[0].gpuFrameTime; s.presentWait = (float)ft[0].cpuMainThreadPresentWaitTime;
        }
        else s.cpuMain = s.cpuRender = s.gpu = s.presentWait = float.NaN;
        if (artPool == null || Samples.Count >= Pool) return;
        s.art = artPool[Samples.Count]; s.det = detPool[Samples.Count];
        for (int i = 0; i < artRec.Length; i++)
        {
            float sum = 0f; var l = artRec[i];
            for (int j = 0; j < l.Count; j++) if (l[j].Valid) sum += l[j].LastValue * 1e-6f;
            s.art[i] = sum;
        }
        for (int i = 0; i < detRec.Length; i++) s.det[i] = detRec[i].Valid ? detRec[i].LastValue * 1e-6f : float.NaN;
        long C(int i) => cntRec[i].Valid ? cntRec[i].LastValue : -1;
        s.draws = C(0); s.batches = C(1); s.setpass = C(2); s.tris = C(3); s.verts = C(4); s.instanced = C(5); s.gcBytes = C(6);
        for (int i = 0; i < renderers.Length; i++) { var r = renderers[i]; if (r != null && r.isVisible) { s.visible++; if (isSkinned[i]) s.visibleSkinned++; } }
        for (int i = 0; i < sources.Length; i++) if (sources[i] != null && sources[i].isPlaying) s.voices++;
        s.uiGraphics = UIRebuildProbe.LastGraphic; s.uiLayout = UIRebuildProbe.LastLayout;
        Samples.Add(s);
    }

    public void Dispose()
    {
        foreach (var l in artRec) foreach (var r in l) if (r.Valid) r.Dispose();
        foreach (var r in detRec) if (r.Valid) r.Dispose();
        foreach (var r in cntRec) if (r.Valid) r.Dispose();
    }

    // ---------- итоги ----------
    public class Summary
    {
        public int frames;
        public float mean, median, p95, p99, worst1, overPct, maxMs;
        public float cpuMain, cpuRender, gpu, gpuP99, presentWait;
        public float[] art, det;
        public float draws, batches, setpass, tris, visible, visibleSkinned, voices, uiGraphics, instanced, gcKb, gcFramesPct;
    }

    public static Summary Summarize(IList<Sample> ss)
    {
        var f = ss.Select(x => x.frame).OrderBy(x => x).ToArray();
        int n = f.Length;
        float P(float[] a, float q) => a.Length == 0 ? float.NaN : a[Mathf.Clamp((int)Math.Ceiling(q * a.Length) - 1, 0, a.Length - 1)];
        int k = Math.Max(1, n / 100);
        float Avg(Func<Sample, float> g) { var v = ss.Select(g).Where(x => !float.IsNaN(x)).ToArray(); return v.Length > 0 ? v.Average() : float.NaN; }
        var gpu = ss.Select(x => x.gpu).Where(x => !float.IsNaN(x) && x > 0).OrderBy(x => x).ToArray();
        return new Summary
        {
            frames = n, mean = f.Average(), median = P(f, 0.5f), p95 = P(f, 0.95f), p99 = P(f, 0.99f),
            worst1 = f.Skip(n - k).Average(), overPct = 100f * f.Count(x => x > BudgetMs) / n, maxMs = f[n - 1],
            cpuMain = Avg(x => x.cpuMain), cpuRender = Avg(x => x.cpuRender), gpu = gpu.Length > 0 ? gpu.Average() : float.NaN, gpuP99 = P(gpu, 0.99f),
            presentWait = Avg(x => x.presentWait),
            art = Enumerable.Range(0, Articles.Length).Select(i => ss.Average(x => x.art[i])).ToArray(),
            det = Enumerable.Range(0, Details.Length).Select(i => Avg(x => x.det[i])).ToArray(),
            draws = (float)ss.Average(x => x.draws), batches = (float)ss.Average(x => x.batches), setpass = (float)ss.Average(x => x.setpass),
            tris = (float)ss.Average(x => x.tris), instanced = (float)ss.Average(x => x.instanced),
            visible = (float)ss.Average(x => x.visible), visibleSkinned = (float)ss.Average(x => x.visibleSkinned),
            voices = (float)ss.Average(x => x.voices), uiGraphics = (float)ss.Average(x => x.uiGraphics),
            gcKb = (float)ss.Average(x => Math.Max(0, x.gcBytes)) / 1024f, gcFramesPct = 100f * ss.Count(x => x.gcBytes > 0) / Math.Max(1, ss.Count),
        };
    }

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    public static string SummaryHeader =>
        "tag,condition,cell,rep,profiled,frames,mean_ms,median_ms,p95_ms,p99_ms,worst1pct_ms,over_budget_pct,max_ms,cpu_main_ms,cpu_render_ms,gpu_ms,gpu_p99_ms,present_wait_ms," +
        string.Join(",", Articles.Select(a => "art_" + a.article + "_ms")) + "," + string.Join(",", Details.Select(d => "det_" + d.Replace('.', '_').Replace(' ', '_') + "_ms")) +
        ",draw_calls,batches,setpass,triangles,instanced_draws,visible,visible_skinned,voices,ui_graphic_rebuilds,gc_kb_per_frame,gc_frames_pct";

    public static string SummaryLine(string tag, string cond, string cell, int rep, bool profiled, Summary s) =>
        string.Join(",", new[] { tag, cond, cell, rep.ToString(ci), profiled ? "1" : "0", s.frames.ToString(ci) }
            .Concat(new[] { s.mean, s.median, s.p95, s.p99, s.worst1, s.overPct, s.maxMs, s.cpuMain, s.cpuRender, s.gpu, s.gpuP99, s.presentWait }.Select(F))
            .Concat(s.art.Select(F)).Concat(s.det.Select(F))
            .Concat(new[] { s.draws, s.batches, s.setpass, s.tris, s.instanced, s.visible, s.visibleSkinned, s.voices, s.uiGraphics }.Select(F1))
            .Concat(new[] { s.gcKb, s.gcFramesPct }.Select(F)));

    static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.000", ci);
    static string F1(float v) => float.IsNaN(v) ? "" : v.ToString("0.0", ci);

    public void WriteFrames(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("t_s,frame_ms,cpu_main_ms,cpu_render_ms,gpu_ms,present_wait_ms," + string.Join(",", Articles.Select(a => a.article + "_ms")) +
                      ",draw_calls,batches,setpass,triangles,visible,visible_skinned,voices,ui_graphic_rebuilds,gc_bytes");
        foreach (var s in Samples)
            sb.AppendLine(string.Join(",", new[] { s.t, s.frame, s.cpuMain, s.cpuRender, s.gpu, s.presentWait }.Select(F).Concat(s.art.Select(F))
                .Concat(new[] { s.draws, s.batches, s.setpass, s.tris }.Select(x => x.ToString(ci)))
                .Concat(new[] { s.visible, s.visibleSkinned, s.voices, s.uiGraphics }.Select(x => x.ToString(ci))).Concat(new[] { s.gcBytes.ToString(ci) })));
        File.WriteAllText(path, sb.ToString());
    }
}

// Сколько элементов uGUI перестраивается за кадр. Читает очередь перестроения CanvasUpdateRegistry
// непосредственно перед её обработкой (событие Canvas.preWillRenderCanvases идёт раньше willRenderCanvases,
// на котором реестр её очищает). Запоминает и источники: какой холст перестраивался — для разбора ошибки
// «интерфейс обновляется каждый кадр».
public static class UIRebuildProbe
{
    public static int LastGraphic, LastLayout;
    public static readonly Dictionary<string, int> ByCanvas = new Dictionary<string, int>();
    static FieldInfo graphicQ, layoutQ;
    static readonly Dictionary<int, string> keyOf = new Dictionary<int, string>();
    static bool hooked;

    public static void Ensure()
    {
        if (hooked) return;
        hooked = true;
        var t = typeof(CanvasUpdateRegistry);
        graphicQ = t.GetField("m_GraphicRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        layoutQ = t.GetField("m_LayoutRebuildQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        Canvas.preWillRenderCanvases += Probe;
    }

    static void Probe()
    {
        var reg = CanvasUpdateRegistry.instance;
        LastGraphic = Count(graphicQ?.GetValue(reg), true);
        LastLayout = Count(layoutQ?.GetValue(reg), false);
    }

    static int Count(object q, bool attribute)
    {
        // IndexedSet<T> в uGUI не поддерживает перечисление (GetEnumerator бросает NotImplementedException) — только по индексу
        if (!(q is IList<ICanvasElement> e)) return 0;
        int n = e.Count;
        for (int i = 0; i < n; i++)
        {
            var o = e[i];
            if (attribute && o is Component c && c != null)
            {
                int id = c.GetInstanceID();
                if (!keyOf.TryGetValue(id, out var key))
                {
                    var cv = c.GetComponentInParent<Canvas>();
                    var root = cv != null ? cv.rootCanvas : null;
                    key = root != null ? (root.transform.parent != null ? root.transform.parent.name + "/" : "") + root.name : "?";
                    keyOf[id] = key;
                }
                ByCanvas.TryGetValue(key, out var k); ByCanvas[key] = k + 1;
            }
        }
        return n;
    }
}
