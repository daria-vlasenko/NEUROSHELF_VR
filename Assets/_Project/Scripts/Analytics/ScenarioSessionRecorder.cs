using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

// Слой аналитики сессии. Только подписывается и пишет, ничего не вызывает в обратную сторону.
//
//  • ScenarioController.StateChanged → обновляет SessionStamp (состояние, номер захода, условие).
//    По этой метке ProductInteractionHub (ЛР5) и RouteLogger (ЛР6) помечают свои записи — данные A и C
//    разделяются по колонке run, а сценарий при этом не знает о существовании логгеров.
//  • ProductInteractionHub.Raised → взятия, возвраты, покупки по заходам.
//  • ScenarioZone.AnyPresenceChanged + опрос зон → время у стеллажей (мера внимания) и порядок обхода.
//  • позиция головы каждые 0.2 с во время захода → маршрут для табло разбора.
//
// Выключите компонент — сценарий, взаимодействие и перемещение работают как прежде.
[DefaultExecutionOrder(-30)]
public class ScenarioSessionRecorder : MonoBehaviour
{
    public float sampleInterval = 0.2f;
    public bool writeCsv = true;

    public class RunStats
    {
        public int run;
        public string condition = "";
        public int picks, returns, purchases, drops, farPicks;
        public readonly Dictionary<string, float> dwell = new Dictionary<string, float>();
        public readonly List<string> visitOrder = new List<string>();
        public readonly List<Vector3> route = new List<Vector3>();
        public readonly Dictionary<string, int> picksByProduct = new Dictionary<string, int>();
        public readonly Dictionary<string, int> returnsByProduct = new Dictionary<string, int>();
        public float pathLength;
        public float Dwell(string zone) => dwell.TryGetValue(zone, out var v) ? v : 0f;
        public int PicksOf(string id) => picksByProduct.TryGetValue(id, out var v) ? v : 0;
        public int ReturnsOf(string id) => returnsByProduct.TryGetValue(id, out var v) ? v : 0;
    }

    public static ScenarioSessionRecorder Instance { get; private set; }
    public readonly RunStats[] Runs = { null, new RunStats { run = 1 }, new RunStats { run = 2 } };
    public readonly List<string> Lines = new List<string>();
    public string CsvPath { get; private set; }
    public static string LastCsv;
    public int EventsTotal { get; private set; }

    Transform head;
    ScenarioZone[] shelfZones = new ScenarioZone[0];
    float nextSample;
    bool collecting;
    int run;
    readonly StringBuilder csv = new StringBuilder();
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    void Awake()
    {
        Instance = this;
        csv.AppendLine("t,run,condition,state,source,event,detail");
    }

    void OnEnable()
    {
        ScenarioController.StateChanged += OnState;
        ScenarioController.Note += OnNote;
        ProductInteractionHub.Raised += OnProduct;
        ScenarioZone.AnyPresenceChanged += OnZone;
    }

    void OnDisable()
    {
        ScenarioController.StateChanged -= OnState;
        ScenarioController.Note -= OnNote;
        ProductInteractionHub.Raised -= OnProduct;
        ScenarioZone.AnyPresenceChanged -= OnZone;
        Flush();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        head = Camera.main != null ? Camera.main.transform : null;
        shelfZones = FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).Where(z => z.zoneId.StartsWith("Shelf_")).OrderBy(z => z.zoneId).ToArray();
    }

    RunStats Cur => run >= 1 && run <= 2 ? Runs[run] : null;

    void OnState(ScenarioState from, ScenarioState to)
    {
        var sc = ScenarioController.Instance;
        bool inRun = to == ScenarioState.RunA || to == ScenarioState.RunC || to == ScenarioState.Paused || to == ScenarioState.Checkout;
        run = inRun && sc != null ? sc.Run : 0;
        SessionStamp.State = to.ToString();
        SessionStamp.Run = run;
        SessionStamp.Condition = sc != null && sc.context != null && sc.context.condition != null ? sc.context.condition.conditionName : "";
        collecting = to == ScenarioState.RunA || to == ScenarioState.RunC;
        if (Cur != null) Cur.condition = SessionStamp.Condition;
        Row("scenario", "STATE", $"{from}->{to}");
        if (to == ScenarioState.Results) Flush();
    }

    void OnNote(string line) => Row("scenario", "NOTE", line.Trim());

    void OnProduct(ProductInteractionEvent e)
    {
        Row("product", e.type.ToString().ToUpperInvariant(), e.ToString().Trim());
        var s = Cur;
        if (s == null || !collecting) return;
        switch (e.type)
        {
            case ProductEventType.Picked:
                s.picks++;
                if (e.region == GrabRegion.Far) s.farPicks++;
                s.picksByProduct[e.productId] = s.PicksOf(e.productId) + 1;
                break;
            case ProductEventType.Released:
                if (e.outcome == ReleaseOutcome.Returned) { s.returns++; s.returnsByProduct[e.productId] = s.ReturnsOf(e.productId) + 1; }
                else if (e.outcome == ReleaseOutcome.Purchased) s.purchases++;
                else if (e.outcome == ReleaseOutcome.Dropped || e.outcome == ReleaseOutcome.Thrown) s.drops++;
                break;
        }
    }

    void OnZone(ScenarioZone z, bool inside, string source)
    {
        Row("zone", inside ? "ENTER" : "EXIT", $"{z.zoneId} via={source}");
        if (inside && collecting && Cur != null && z.zoneId.StartsWith("Shelf_")) Cur.visitOrder.Add(z.zoneId);
    }

    void Update()
    {
        var s = Cur;
        if (s == null || !collecting) return;
        foreach (var z in shelfZones) if (z.PlayerInside) s.dwell[z.zoneId] = s.Dwell(z.zoneId) + Time.deltaTime;
        if (head != null && Time.time >= nextSample)
        {
            nextSample = Time.time + sampleInterval;
            var p = head.position; p.y = 0f;
            if (s.route.Count > 0)
            {
                float d = Vector3.Distance(s.route[s.route.Count - 1], p);
                if (d < 0.35f) s.pathLength += d;   // скачки телепорта в длину пройденного пути не входят
            }
            s.route.Add(p);
        }
    }

    void Row(string source, string ev, string detail)
    {
        EventsTotal++;
        var line = string.Format(ci, "{0:0.000},{1},{2},{3},{4},{5},\"{6}\"", Time.time, SessionStamp.Run, SessionStamp.Condition, SessionStamp.State, source, ev, detail.Replace("\"", "'"));
        csv.AppendLine(line);
        Lines.Add(line);
    }

    public void Flush()
    {
        if (!writeCsv || csv.Length == 0) return;
        try
        {
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Sessions"))
                : Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(dir);
            if (CsvPath == null) CsvPath = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(CsvPath, csv.ToString(), new UTF8Encoding(true));
            LastCsv = CsvPath;
        }
        catch (Exception ex) { Debug.LogWarning("[NS-SCN] CSV сессии не записан: " + ex.Message); }
    }
}
