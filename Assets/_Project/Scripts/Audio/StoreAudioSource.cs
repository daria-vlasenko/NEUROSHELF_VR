using System.Reflection;
using UnityEngine;

// Паспорт источника звука: роль, место, группа микшера, обоснование кривой затухания.
// Настраивает его редакторный скрипт (меню 10.4), а этот компонент хранит описание и даёт тестам
// и протоколу прочитать фактические параметры, включая окклюзию Steam Audio (без жёсткой ссылки
// на сборку плагина: значение читается рефлексией, проект компилируется и без Steam Audio).
[RequireComponent(typeof(AudioSource))]
public class StoreAudioSource : MonoBehaviour
{
    public enum Role { Music, Anchor, Cooler, Ventilation, Checkout, NpcSteps, NpcMurmur, Door, Interface }

    public Role role;
    public string sourceId = "";
    [Tooltip("Где стоит источник — для таблицы отчёта")]
    public string place = "";
    public StoreAudioMixer.Group group;
    [Tooltip("Почему такая кривая затухания")]
    [TextArea] public string curveNote = "";

    AudioSource src;
    Component steam;
    FieldInfo occField, binField, occOnField;

    public AudioSource Source => src != null ? src : (src = GetComponent<AudioSource>());

    Component Steam
    {
        get
        {
            if (steam != null) return steam;
            foreach (var c in GetComponents<Component>())
                if (c != null && c.GetType().FullName == "SteamAudio.SteamAudioSource") { steam = c; break; }
            if (steam != null)
            {
                var t = steam.GetType();
                occField = t.GetField("occlusionValue");
                binField = t.GetField("directBinaural");
                occOnField = t.GetField("occlusion");
            }
            return steam;
        }
    }

    public bool HasSteam => Steam != null;
    /// Обработка по HRTF (бинаурально) — только у источников со Steam Audio Source и directBinaural.
    public bool Binaural => Source.spatialize && Steam != null && binField != null && (bool)binField.GetValue(steam);
    public bool OcclusionEnabled => Steam != null && occOnField != null && (bool)occOnField.GetValue(steam);
    /// 1 — прямой звук не перекрыт, 0 — полностью перекрыт (значение симуляции Steam Audio).
    public float Occlusion => Steam != null && occField != null ? (float)occField.GetValue(steam) : 1f;

    public string CurveName =>
        Source.spatialBlend < 0.01f ? "— (2D)" :
        Source.rolloffMode == AudioRolloffMode.Logarithmic ? "логарифмическая" :
        Source.rolloffMode == AudioRolloffMode.Linear ? "линейная" : "своя (Custom)";
}
