using System;
using UnityEngine;
using UnityEngine.Audio;

// Мост к микшеру зала. Четыре группы и четыре открытых параметра громкости (дБ):
//   Music     — фоновая музыка (фактор условия эксперимента, эффект 7 концепта)
//   Ambience  — гул витрины, вентиляция, касса, дверь, шаги и голоса NPC
//   Stimulus  — звуковой якорь у стеллажа (экспериментальный стимул)
//   Interface — щелчки интерфейса
// Громкость меняют сценарий (SetVolumeDb) и панель настроек (через тот же метод) — одно место правды.
public class StoreAudioMixer : MonoBehaviour
{
    public enum Group { Music, Ambience, Stimulus, Interface }

    public AudioMixer mixer;
    public AudioMixerGroup music, ambience, stimulus, iface;

    public const float MinDb = -60f, MaxDb = 0f;
    public static string Param(Group g) => "Vol_" + g;

    public static StoreAudioMixer Instance { get; private set; }
    public static event Action<Group, float, string> VolumeChanged;   // группа, дБ, кто изменил

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; VolumeChanged = null; }

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public AudioMixerGroup For(Group g) => g switch
    {
        Group.Music => music,
        Group.Ambience => ambience,
        Group.Stimulus => stimulus,
        _ => iface,
    };

    public float GetVolumeDb(Group g)
    {
        if (mixer != null && mixer.GetFloat(Param(g), out var v)) return v;
        return float.NaN;
    }

    public bool SetVolumeDb(Group g, float db, string who)
    {
        if (mixer == null) return false;
        db = Mathf.Clamp(db, MinDb, MaxDb);
        bool ok = mixer.SetFloat(Param(g), db);
        if (ok)
        {
            Debug.Log($"[NS-AUD] громкость {g} = {db:0} дБ ({who})");
            VolumeChanged?.Invoke(g, db, who);
        }
        return ok;
    }
}
