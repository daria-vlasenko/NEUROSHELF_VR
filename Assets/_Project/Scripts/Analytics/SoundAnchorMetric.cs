using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Измерение гипотезы о звуковом якоре. Слой аналитики: только слушает и пишет.
//
// H1: если у стеллажа звучит звуковой якорь, участник впервые подходит к этому стеллажу раньше,
//     чем к тому же стеллажу без якоря (меньше T_enter), и раньше поворачивает к нему голову (меньше T_look).
// H0: время первого подхода от наличия якоря не зависит.
//
// Отсчёт — от конца обратного отсчёта захода (RunRecord.tGo): до него участник обязан стоять на отметке.
// Для каждого стеллажа в каждом заходе пишется:
//   T_look   — первый момент, когда горизонтальное направление взгляда головы отклонилось от направления
//              на центр стеллажа меньше чем на lookAngle (head-gaze, как и везде в проекте — не eye-tracking);
//   T_enter  — первый вход в зону стеллажа по двум независимым датчикам:
//              ScenarioZone (капсула тела, ЛР7) и RouteLogger (зона по положению головы, ЛР6);
//   rank     — каким по счёту стеллаж посещён.
// Стеллаж, не посещённый до кассы, — цензурированное наблюдение (T = длительность захода, censored = 1):
// так данные пригодны для анализа выживаемости (Kaplan–Meier, Cox), а не теряются.
public class SoundAnchorMetric : MonoBehaviour
{
    public float lookAngle = 15f;
    public bool writeCsv = true;

    [Serializable]
    public class ShelfRow
    {
        public int run;
        public string condition, anchorShelf, shelf;
        public bool hasAnchor;
        public float tLook = -1f, tEnterZone = -1f, tEnterRoute = -1f;
        public int rank;
        public bool censored;
        public float runSeconds;
    }

    public static SoundAnchorMetric Instance { get; private set; }
    public readonly List<ShelfRow> Rows = new List<ShelfRow>();
    public string CsvPath { get; private set; }
    public static string LastCsv;

    readonly Dictionary<string, ShelfRow> cur = new Dictionary<string, ShelfRow>();
    readonly Dictionary<string, Vector3> shelfCenters = new Dictionary<string, Vector3>();
    int run, visits;
    float tGo = -1f;
    Transform head;
    RouteLogger route;
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable()
    {
        ScenarioController.StateChanged += OnState;
        ScenarioZone.AnyPresenceChanged += OnZone;
    }

    void OnDisable()
    {
        ScenarioController.StateChanged -= OnState;
        ScenarioZone.AnyPresenceChanged -= OnZone;
        Flush();
    }

    void Start()
    {
        head = Camera.main != null ? Camera.main.transform : null;
        route = FindFirstObjectByType<RouteLogger>();
        foreach (var s in new[] { "Shelf_01", "Shelf_02", "Shelf_03", "Shelf_04" })
        {
            var g = GameObject.Find(s);
            if (g == null) continue;
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            shelfCenters[s] = b.center;
        }
    }

    void OnState(ScenarioState from, ScenarioState to)
    {
        var sc = ScenarioController.Instance;
        if ((to == ScenarioState.RunA || to == ScenarioState.RunC) && from != ScenarioState.Paused && sc != null)
        {
            run = sc.Run;
            tGo = -1f;
            visits = 0;
            cur.Clear();
            var cond = sc.context != null ? sc.context.condition : null;
            string anchor = StoreAudioDirector.Instance != null ? StoreAudioDirector.Instance.AnchorShelf : cond?.audioAnchorShelf ?? "";
            foreach (var s in shelfCenters.Keys)
                cur[s] = new ShelfRow { run = run, condition = cond?.conditionName ?? "", anchorShelf = anchor, shelf = s, hasAnchor = s == anchor };
        }
        if (to == ScenarioState.Checkout && cur.Count > 0)
        {
            float dur = tGo >= 0f ? Time.time - tGo : 0f;
            foreach (var r in cur.Values)
            {
                r.runSeconds = dur;
                r.censored = r.tEnterZone < 0f;
                Rows.Add(r);
            }
            Debug.Log("[NS-AUD] якорь, заход " + run + ": " + string.Join("; ", cur.Values.Select(r =>
                $"{r.shelf}{(r.hasAnchor ? "*" : "")} look={F(r.tLook)} enter={F(r.tEnterZone)}/{F(r.tEnterRoute)} rank={r.rank}")));
            cur.Clear();
            Flush();
        }
    }

    static string F(float v) => v < 0f ? "—" : v.ToString("0.00", ci);

    float Now => tGo >= 0f ? Time.time - tGo : -1f;

    void OnZone(ScenarioZone z, bool inside, string source)
    {
        if (!inside || Now < 0f || !cur.TryGetValue(z.zoneId, out var r) || r.tEnterZone >= 0f) return;
        r.tEnterZone = Now;
        r.rank = ++visits;
    }

    void Update()
    {
        var sc = ScenarioController.Instance;
        if (cur.Count == 0 || sc == null) return;
        if (tGo < 0f)
        {
            if ((sc.State == ScenarioState.RunA || sc.State == ScenarioState.RunC) && !sc.InCountdown) tGo = Time.time;
            else return;
        }
        if (sc.State == ScenarioState.Paused) return;
        if (head != null)
        {
            var f = head.forward; f.y = 0f;
            foreach (var kv in shelfCenters)
            {
                var r = cur[kv.Key];
                if (r.tLook >= 0f) continue;
                var d = kv.Value - head.position; d.y = 0f;
                if (f.sqrMagnitude > 1e-4f && d.sqrMagnitude > 1e-4f && Vector3.Angle(f, d) <= lookAngle) r.tLook = Now;
            }
        }
        if (route != null && cur.TryGetValue(route.currentZone ?? "", out var rr) && rr.tEnterRoute < 0f) rr.tEnterRoute = Now;
    }

    public static string CsvHeader => "run,condition,anchor_shelf,shelf,has_anchor,t_look_s,t_enter_zone_s,t_enter_route_s,visit_rank,censored,run_s";

    public static string ToCsv(ShelfRow r) => string.Format(ci, "{0},{1},{2},{3},{4},{5:0.000},{6:0.000},{7:0.000},{8},{9},{10:0.000}",
        r.run, r.condition, r.anchorShelf == "" ? "none" : r.anchorShelf, r.shelf, r.hasAnchor ? 1 : 0, r.tLook, r.tEnterZone, r.tEnterRoute, r.rank, r.censored ? 1 : 0, r.runSeconds);

    public void Flush()
    {
        if (!writeCsv || Rows.Count == 0) return;
        try
        {
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Sessions"))
                : Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(dir);
            if (CsvPath == null) CsvPath = Path.Combine(dir, $"anchor_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new StringBuilder(CsvHeader + "\n");
            foreach (var r in Rows) sb.AppendLine(ToCsv(r));
            File.WriteAllText(CsvPath, sb.ToString(), new UTF8Encoding(true));
            LastCsv = CsvPath;
        }
        catch (Exception e) { Debug.LogWarning("[NS-AUD] CSV якоря не записан: " + e.Message); }
    }
}
