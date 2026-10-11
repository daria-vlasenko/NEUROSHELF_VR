using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Запись маршрута покупателя: позиция головы с интервалом, события телепорта,
// вход/выход из зон у стеллажей и кассы. Консоль [NS-LOC] + CSV.
// Во второй части сюда подключится SessionRecorder (концепт, §4.4) — логгер ничего не вызывает
// в обратную сторону, только пишет.
//
// Расстояние раскладывается на три слагаемых, чтобы режимы были сопоставимы по одной метрике:
//   physical_m — ногами в room-scale (голова относительно XR Origin);
//   smooth_m   — стиком (смещение XR Origin без скачков);
//   teleport_m — перенос телепортом (длина скачков головы).
// У телепорта «пройденное» = physical_m, у плавного = physical_m + smooth_m; teleport_m — то, что
// телепорт сэкономил участнику. Это и есть величина искажения «цены доступа».
[DefaultExecutionOrder(100)]
public class RouteLogger : MonoBehaviour
{
    public Transform head;
    public Transform origin;
    public LocomotionProfileSO profile;
    public float interval = 0.2f;
    public bool writeCsv = true;
    public bool logToConsole = true;

    [Serializable]
    public class Zone
    {
        public string id;
        public Vector3 center;
        public Vector2 size;    // x, z
        public bool Contains(Vector3 p) =>
            Mathf.Abs(p.x - center.x) <= size.x * 0.5f && Mathf.Abs(p.z - center.z) <= size.y * 0.5f;
    }
    public List<Zone> zones = new List<Zone>();

    [Header("Итоги (только чтение)")]
    public float physicalMeters, smoothMeters, teleportMeters;
    public int teleportCount;
    public string currentZone = "";

    public readonly Dictionary<string, float> dwell = new Dictionary<string, float>();
    public readonly List<string> visitOrder = new List<string>();
    public static readonly List<string> Events = new List<string>();
    public string CsvPath { get; private set; }

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    readonly StringBuilder csv = new StringBuilder();
    Vector3 lastHeadLocal, lastHead; bool hasLast;
    float nextSample, startTime;
    string pendingTarget;

    void OnEnable()
    {
        Events.Clear(); dwell.Clear(); visitOrder.Clear();
        physicalMeters = smoothMeters = teleportMeters = 0f; teleportCount = 0; currentZone = "";
        hasLast = false; startTime = Time.time; nextSample = 0f;
        foreach (var t in FindObjectsByType<BaseTeleportationInteractable>(FindObjectsSortMode.None))
            t.teleporting.AddListener(OnTeleporting);
        if (profile != null) interval = profile.logInterval;
        var cond = ExperimentContext.Find()?.condition;
        csv.Clear();
        csv.AppendLine($"# NEUROSHELF route · {DateTime.Now:yyyy-MM-dd HH:mm:ss} · profile={profile?.profileName} · mode={profile?.mode} · turn={profile?.turn} · speed={(profile != null ? profile.moveSpeed : 0f).ToString("0.00", ci)} · condition={cond?.conditionName}");
        csv.AppendLine("t,event,head_x,head_y,head_z,yaw,zone,physical_m,smooth_m,teleport_m,detail");
    }

    void OnDisable()
    {
        foreach (var t in FindObjectsByType<BaseTeleportationInteractable>(FindObjectsSortMode.None))
            t.teleporting.RemoveListener(OnTeleporting);
        if (hasLast) Finish();
    }

    void OnTeleporting(TeleportingEventArgs e)
    {
        var i = e.interactableObject as Component;
        pendingTarget = i is ShelfTeleportationAnchor a ? "Anchor:" + a.anchorId : "Floor";
    }

    void LateUpdate()
    {
        if (head == null || origin == null) return;
        float t = Time.time - startTime;
        var h = head.position;
        if (!hasLast) { lastHead = h; lastHeadLocal = origin.InverseTransformPoint(h); hasLast = true; Sample(t, "START", ""); }

        // Где оказалась бы голова, если бы человек не шевелился, а двигался только XR Origin.
        // Разница с прошлой позицией — перемещение средствами локомоции (стик, поворот, телепорт),
        // остаток — собственные шаги в room-scale. Поворот XR Origin вокруг головы даёт 0, как и должно.
        var predicted = origin.TransformPoint(lastHeadLocal);
        var dLoco = predicted - lastHead; dLoco.y = 0f;
        var dPhys = h - predicted; dPhys.y = 0f;
        physicalMeters += dPhys.magnitude;
        bool jump = pendingTarget != null || dLoco.magnitude > 0.35f;
        if (jump && dLoco.magnitude > 0.01f)
        {
            teleportCount++;
            teleportMeters += dLoco.magnitude;
            var detail = string.Format(ci, "{0} from=({1:0.00};{2:0.00}) to=({3:0.00};{4:0.00}) jump={5:0.00} m", pendingTarget ?? "?", lastHead.x, lastHead.z, h.x, h.z, dLoco.magnitude);
            Sample(t, "TELEPORT", detail);
            Log("TELEPORT " + detail);
            pendingTarget = null;
        }
        else if (!jump) smoothMeters += dLoco.magnitude;
        lastHead = h; lastHeadLocal = origin.InverseTransformPoint(h);

        // зоны
        var z = zones.FirstOrDefault(q => q.Contains(h))?.id ?? "";
        if (z != currentZone)
        {
            if (currentZone != "") { Sample(t, "EXIT", currentZone); Log(string.Format(ci, "EXIT  {0}  dwell_total={1:0.0} с", currentZone, Dwell(currentZone))); }
            currentZone = z;
            if (z != "") { visitOrder.Add(z); Sample(t, "ENTER", z); Log("ENTER " + z); }
        }
        if (currentZone != "") dwell[currentZone] = Dwell(currentZone) + Time.deltaTime;

        if (Time.time >= nextSample) { nextSample = Time.time + interval; Sample(t, "POS", ""); }
    }

    float Dwell(string z) => dwell.TryGetValue(z, out var v) ? v : 0f;

    void Sample(float t, string ev, string detail)
    {
        var h = head.position;
        csv.AppendLine(string.Format(ci, "{0:0.000},{1},{2:0.000},{3:0.000},{4:0.000},{5:0.0},{6},{7:0.000},{8:0.000},{9:0.000},\"{10}\"",
            t, ev, h.x, h.y, h.z, head.eulerAngles.y, currentZone, physicalMeters, smoothMeters, teleportMeters, detail));
    }

    void Log(string s)
    {
        var line = string.Format(ci, "[NS-LOC] t={0,6:0.00}  {1}", Time.time - startTime, s);
        Events.Add(line);
        if (logToConsole) Debug.Log(line);
    }

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(ci, "profile={0} mode={1}", profile?.profileName, profile?.mode));
        sb.AppendLine(string.Format(ci, "physical_m={0:0.00}  smooth_m={1:0.00}  teleport_m={2:0.00}  teleports={3}", physicalMeters, smoothMeters, teleportMeters, teleportCount));
        sb.AppendLine("visit_order=" + string.Join(" → ", visitOrder));
        foreach (var kv in dwell) sb.AppendLine(string.Format(ci, "dwell[{0}]={1:0.0} с", kv.Key, kv.Value));
        return sb.ToString();
    }

    void Finish()
    {
        Sample(Time.time - startTime, "END", "");
        Log("SUMMARY " + Summary().Replace("\n", " | "));
        if (!writeCsv) return;
        try
        {
            string dir = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Routes"))
                : Path.Combine(Application.persistentDataPath, "Routes");
            Directory.CreateDirectory(dir);
            CsvPath = Path.Combine(dir, $"route_{DateTime.Now:yyyyMMdd_HHmmss}_{profile?.profileName}.csv");
            File.WriteAllText(CsvPath, csv.ToString(), new UTF8Encoding(true));
            LastCsv = CsvPath;
            Debug.Log("[NS-LOC] CSV → " + CsvPath);
        }
        catch (Exception e) { Debug.LogWarning("[NS-LOC] CSV не записан: " + e.Message); }
    }

    public static string LastCsv;
    public void FlushNow() { if (hasLast) { Finish(); hasLast = false; } }
}
