using System;
using UnityEngine;

// Триггерная зона сценария: вход, стартовая отметка, касса, разбор, зоны у стеллажей.
//
// Главный подвох: у нас есть телепорт. Телепорт переставляет XR Origin вместе с Character Controller
// за один кадр. Физика узнаёт о новом положении только на следующей синхронизации, и OnTriggerEnter
// может не прийти вовсе (капсула «появилась» внутри зоны, пересечения границы не было), а OnTriggerExit —
// не прийти при прыжке из зоны. Поэтому зона не доверяет только событиям: каждый шаг физики
// она сверяет фактическое пересечение капсулы игрока со своим объёмом (Physics.OverlapCapsule
// по геометрии капсулы, вычисленной из текущего transform — без отставания физической сцены).
// Каждое расхождение «события сказали одно, сверка — другое» считается отдельно: это и есть
// численное доказательство, что проблема реальна.
[RequireComponent(typeof(BoxCollider))]
public class ScenarioZone : MonoBehaviour
{
    public string zoneId = "Zone";
    [Tooltip("Сверять пересечение каждый шаг физики, а не только по OnTriggerEnter/Exit")]
    public bool reconcile = true;
    public CharacterController player;
    [Tooltip("Голова (камера). Тело участника = вертикальная капсула под головой: так зона видит и телепорт, и физические шаги в room-scale")]
    public Transform head;

    public bool PlayerInside { get; private set; }
    public float DwellSeconds { get; private set; }
    public int Visits { get; private set; }

    [Header("Счётчики источника (только чтение)")]
    public int triggerEnters, triggerExits;
    public int reconciledEnters, reconciledExits;   // вход/выход, которые обнаружила только сверка
    public string lastSource = "";

    public event Action<ScenarioZone, bool> PresenceChanged;
    public static event Action<ScenarioZone, bool, string> AnyPresenceChanged;

    BoxCollider box;
    readonly Collider[] buf = new Collider[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { AnyPresenceChanged = null; }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        if (player == null) player = FindFirstObjectByType<CharacterController>();
    }

    void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<CharacterController>();
        if (head == null && Camera.main != null) head = Camera.main.transform;
    }

    void OnDisable() { if (PlayerInside) Set(false, "disable"); }

    bool IsPlayer(Collider c) => player != null ? c == player : c is CharacterController;

    void OnTriggerEnter(Collider c)
    {
        if (!IsPlayer(c)) return;
        triggerEnters++;
        if (!PlayerInside) Set(true, "trigger");
    }

    void OnTriggerExit(Collider c)
    {
        if (!IsPlayer(c)) return;
        triggerExits++;
        if (PlayerInside && !(reconcile && Overlaps())) Set(false, "trigger");
    }

    void FixedUpdate() { if (reconcile) Reconcile(); }

    void Update() { if (PlayerInside) DwellSeconds += Time.deltaTime; }

    /// Сверка фактического пересечения. Публичная — сценарий вызывает её перед решениями,
    /// не дожидаясь шага физики (как RefreshConditions в процедуре обследования).
    public void Reconcile()
    {
        bool actual = Overlaps();
        if (actual == PlayerInside) return;
        if (actual) reconciledEnters++; else reconciledExits++;
        Set(actual, "reconcile");
    }

    // Капсула тела участника для сверки. Центр по горизонтали — под головой, а не центр Character Controller:
    // Character Controller подстраивается под голову только при искусственном перемещении (ЛР6), а человек
    // в room-scale может дойти до отметки ногами. Радиус и пол — от Character Controller.
    public void BodyCapsule(out Vector3 bottom, out Vector3 top, out float r)
    {
        var t = player.transform;
        var s = t.lossyScale;
        r = player.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        float floor = t.position.y;
        if (head != null)
        {
            var h = head.position;
            bottom = new Vector3(h.x, floor + r, h.z);
            top = new Vector3(h.x, Mathf.Max(h.y - r, floor + r), h.z);
            return;
        }
        float height = Mathf.Max(player.height * Mathf.Abs(s.y), 2f * r);
        var c = t.TransformPoint(player.center);
        var up = t.up * (height * 0.5f - r);
        bottom = c - up; top = c + up;
    }

    public bool Overlaps()
    {
        if (player == null || !player.enabled || box == null) return false;
        BodyCapsule(out var p0, out var p1, out var r);
        int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r, buf, 1 << gameObject.layer, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++) if (buf[i] == box) return true;
        return false;
    }

    void Set(bool inside, string source)
    {
        PlayerInside = inside;
        lastSource = source;
        if (inside) Visits++;
        PresenceChanged?.Invoke(this, inside);
        AnyPresenceChanged?.Invoke(this, inside, source);
    }

    public void ResetDwell() { DwellSeconds = 0f; Visits = 0; }

    void OnDrawGizmos()
    {
        var b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Application.isPlaying && PlayerInside ? new Color(0.2f, 1f, 0.3f, 0.25f) : new Color(1f, 0.7f, 0.1f, 0.12f);
        Gizmos.DrawCube(b.center, b.size);
        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.8f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
