using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Панель настроек звука для экспериментатора (рядом с главным табло).
// Четыре группы микшера, шаг ±3 дБ кнопками, а не ползунком: ползунок лучом тянут дрожащей рукой
// и попадают не туда, дискретный шаг даёт повторяемое значение, которое можно записать в протокол.
// Во время захода (RunA, RunC, Paused, Checkout) панель заблокирована: громкость стимула и музыки —
// часть условия эксперимента, её нельзя менять посреди замера. Нажатие на заблокированную кнопку —
// явный отказ (звук + двойной импульс), а не «ничего не произошло».
public class AudioSettingsPanel : MonoBehaviour
{
    [System.Serializable]
    public class Row
    {
        public StoreAudioMixer.Group group;
        public XRCanvasButton minus, plus;
        public Text value;
        public Image bar;
    }

    public List<Row> rows = new List<Row>();
    public Text status;
    public float stepDb = 3f;
    public float barWidthMm = 200f;

    public bool Locked { get; private set; }
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    void OnEnable()
    {
        foreach (var r in rows)
        {
            var row = r;
            if (row.minus != null) row.minus.Clicked += (b, h) => Step(row, -stepDb, h);
            if (row.plus != null) row.plus.Clicked += (b, h) => Step(row, +stepDb, h);
        }
        StoreAudioMixer.VolumeChanged += OnVolume;
    }

    void OnDisable() => StoreAudioMixer.VolumeChanged -= OnVolume;

    void Start() => Redraw();

    void OnVolume(StoreAudioMixer.Group g, float db, string who) => Redraw();

    void Step(Row r, float delta, InteractorHandedness hand)
    {
        var m = StoreAudioMixer.Instance;
        if (m == null) return;
        float cur = m.GetVolumeDb(r.group);
        if (float.IsNaN(cur)) cur = 0f;
        m.SetVolumeDb(r.group, cur + delta, "панель настроек, " + hand);
    }

    void Update()
    {
        var sc = ScenarioController.Instance;
        bool lockNow = sc != null && sc.Active && (sc.State == ScenarioState.RunA || sc.State == ScenarioState.RunC ||
                                                   sc.State == ScenarioState.Paused || sc.State == ScenarioState.Checkout);
        if (lockNow != Locked) { Locked = lockNow; Redraw(); }
    }

    public void Redraw()
    {
        var m = StoreAudioMixer.Instance;
        foreach (var r in rows)
        {
            float db = m != null ? m.GetVolumeDb(r.group) : float.NaN;
            if (r.value != null) r.value.text = float.IsNaN(db) ? "—" : db.ToString("0", ci) + " дБ";
            if (r.bar != null && !float.IsNaN(db))
            {
                float k = Mathf.InverseLerp(StoreAudioMixer.MinDb, StoreAudioMixer.MaxDb, db);
                r.bar.rectTransform.sizeDelta = new Vector2(Mathf.Max(2f, k * barWidthMm), r.bar.rectTransform.sizeDelta.y);
            }
            if (r.minus != null) r.minus.SetAvailable(!Locked && db > StoreAudioMixer.MinDb + 0.01f);
            if (r.plus != null) r.plus.SetAvailable(!Locked && db < StoreAudioMixer.MaxDb - 0.01f);
        }
        if (status != null)
            status.text = Locked ? "<color=#ffb060>Заблокировано: идёт заход — громкость входит в условие эксперимента</color>"
                                 : "Шаг ±3 дБ. Меняет экспериментатор до старта захода.";
    }
}
