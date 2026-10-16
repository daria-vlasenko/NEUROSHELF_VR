using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Тактильная обратная связь с журналом импульсов.
//
// Вибрация отправляется штатным HapticImpulsePlayer (XRI 3.3.2) той руки, которой выполнено действие.
// Без шлема вибрацию ощутить нельзя, поэтому каждый импульс пишется в журнал: рука, шаблон, амплитуда,
// длительность, номер импульса в шаблоне и доставлен ли он устройству (SendHapticImpulse = true/false).
// Тесты читают журнал, а не ощущения.
//
// Шаблоны:
//   UIHover     — наведение луча на элемент интерфейса: слабый, короткий
//   UIPress     — нажатие кнопки: средний
//   UIDeny      — нажатие недоступной кнопки: двойной (два импульса с паузой) — отличим от нажатия без звука
//   Grab        — взятие товара: амплитуда растёт с массой упаковки (ЛР5: 0.15…0.53 кг)
//   BasketPlace — товар лёг в корзину: короткий «щелчок» подтверждения
//   TierEffort  — дополнительная метка усилия на нижнем ярусе; ВЫКЛЮЧЕНА по умолчанию (поле условия
//                 tierEffortHaptic): это стимул, которого нет в настоящем магазине, он смешался бы с эффектом яруса.
public class HapticFeedback : MonoBehaviour
{
    public enum Pattern { UIHover, UIPress, UIDeny, Grab, BasketPlace, TierEffort }

    [Serializable]
    public struct Spec
    {
        public float amplitude, duration;   // 0…1, с
        public int pulses; public float gap;
        public Spec(float a, float d, int n = 1, float g = 0f) { amplitude = a; duration = d; pulses = n; gap = g; }
    }

    public HapticImpulsePlayer left, right;

    [Header("Шаблоны")]
    public Spec uiHover = new Spec(0.10f, 0.015f);
    public Spec uiPress = new Spec(0.40f, 0.035f);
    public Spec uiDeny = new Spec(0.55f, 0.045f, 2, 0.060f);
    public Spec basketPlace = new Spec(0.35f, 0.020f);
    [Tooltip("Взятие: амплитуда от лёгкой до тяжёлой упаковки")]
    public Vector2 grabAmplitude = new Vector2(0.25f, 0.80f);
    public Vector2 grabMassRange = new Vector2(0.15f, 0.53f);
    public float grabDuration = 0.050f;
    public float tierEffortExtra = 0.060f;

    [Serializable]
    public struct Record
    {
        public float t;
        public string hand, pattern, detail;
        public float amplitude, duration;
        public int pulse;
        public bool delivered;
        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "t={0,7:0.000}  {1,-5} {2,-11} A={3:0.00} {4,3:0} мс  импульс {5}  {6}  {7}",
            t, hand, pattern, amplitude, duration * 1000f, pulse, delivered ? "доставлен" : "нет устройства", detail);
    }

    public static HapticFeedback Instance { get; private set; }
    public static readonly List<Record> Journal = new List<Record>();
    public static string LastCsv;

    readonly Dictionary<string, InteractorHandedness> holder = new Dictionary<string, InteractorHandedness>();
    Dictionary<string, ProductItem> items;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; Journal.Clear(); }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() => ProductInteractionHub.Raised += OnProduct;
    void OnDisable() { ProductInteractionHub.Raised -= OnProduct; WriteCsv(); }

    // ---------------- события товара (ЛР5) ----------------

    void OnProduct(ProductInteractionEvent e)
    {
        if (e.type == ProductEventType.Picked)
        {
            var hand = Parse(e.hand);
            holder[e.productId] = hand;
            float m = MassOf(e.productId);
            float k = Mathf.InverseLerp(grabMassRange.x, grabMassRange.y, m);
            float a = Mathf.Lerp(grabAmplitude.x, grabAmplitude.y, k);
            var cond = ExperimentContext.Find()?.condition;
            bool effort = cond != null && cond.tierEffortHaptic && e.tier == 0;
            Send(effort ? Pattern.TierEffort : Pattern.Grab, hand, new Spec(a, grabDuration + (effort ? tierEffortExtra : 0f)),
                string.Format(CultureInfo.InvariantCulture, "{0} масса {1:0.00} кг ярус {2}", e.productId, m, e.tier));
        }
        else if (e.type == ProductEventType.Placed && e.place == ProductPlace.Basket)
        {
            var hand = holder.TryGetValue(e.productId, out var h) ? h : InteractorHandedness.Right;
            Send(Pattern.BasketPlace, hand, basketPlace, e.productId + " → корзина");
        }
    }

    float MassOf(string productId)
    {
        if (items == null || items.Values.Any(v => v == null))
            items = FindObjectsByType<ProductItem>(FindObjectsSortMode.None).GroupBy(i => i.productId).ToDictionary(g => g.Key, g => g.First());
        return items.TryGetValue(productId, out var it) && it.Body != null ? it.Body.mass : grabMassRange.x;
    }

    static InteractorHandedness Parse(string hand) =>
        hand == "Left" ? InteractorHandedness.Left : hand == "Right" ? InteractorHandedness.Right : InteractorHandedness.None;

    // ---------------- API для интерфейса ----------------

    public static void Play(Pattern p, InteractorHandedness hand, string detail)
    {
        if (Instance == null) return;
        var s = p switch
        {
            Pattern.UIHover => Instance.uiHover,
            Pattern.UIPress => Instance.uiPress,
            Pattern.UIDeny => Instance.uiDeny,
            Pattern.BasketPlace => Instance.basketPlace,
            _ => Instance.uiPress,
        };
        Instance.Send(p, hand, s, detail);
    }

    public Spec SpecOf(Pattern p) => p switch
    {
        Pattern.UIHover => uiHover, Pattern.UIPress => uiPress, Pattern.UIDeny => uiDeny, Pattern.BasketPlace => basketPlace,
        _ => new Spec(grabAmplitude.y, grabDuration),
    };

    void Send(Pattern p, InteractorHandedness hand, Spec s, string detail)
    {
        if (hand == InteractorHandedness.None) hand = InteractorHandedness.Right;   // нажатие не из луча (программно)
        if (isActiveAndEnabled) StartCoroutine(Pulses(p, hand, s, detail));
    }

    IEnumerator Pulses(Pattern p, InteractorHandedness hand, Spec s, string detail)
    {
        var player = hand == InteractorHandedness.Left ? left : right;
        for (int i = 1; i <= Mathf.Max(1, s.pulses); i++)
        {
            bool ok = player != null && player.SendHapticImpulse(s.amplitude, s.duration);
            Journal.Add(new Record { t = Time.time, hand = hand.ToString(), pattern = p.ToString(), amplitude = s.amplitude, duration = s.duration, pulse = i, delivered = ok, detail = detail });
            if (i < s.pulses) yield return new WaitForSeconds(s.duration + s.gap);
        }
    }

    public static string CsvHeader => "t,hand,pattern,amplitude,duration_s,pulse,delivered,detail";

    public void WriteCsv()
    {
        if (Journal.Count == 0) return;
        try
        {
            string dir = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Haptics"))
                                              : Path.Combine(Application.persistentDataPath, "Haptics");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"haptics_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new StringBuilder(CsvHeader + "\n");
            foreach (var r in Journal)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.000},{1},{2},{3:0.00},{4:0.000},{5},{6},\"{7}\"", r.t, r.hand, r.pattern, r.amplitude, r.duration, r.pulse, r.delivered ? 1 : 0, r.detail));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            LastCsv = path;
        }
        catch (Exception e) { Debug.LogWarning("[NS-HAP] журнал не записан: " + e.Message); }
    }
}
