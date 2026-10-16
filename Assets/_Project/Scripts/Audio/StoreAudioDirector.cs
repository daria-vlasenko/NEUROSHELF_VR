using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Звук как часть условия эксперимента. Подписан на ExperimentContext.ConditionChanged и читает поля
// ExperimentConditionSO — ничего о продвигаемом товаре в коде не знает:
//   backgroundMusic / musicClip   — музыка в зале (эффект 7: North, Hargreaves & McKendrick, 1999);
//   audioAnchorShelf              — у какого стеллажа звучит звуковой якорь ("" — якоря нет).
// Музыку и якорь при смене условия не обрывает, а уводит за fadeSeconds: щелчок от обрыва сам был бы стимулом.
[DefaultExecutionOrder(-40)]
public class StoreAudioDirector : MonoBehaviour
{
    public ExperimentContext context;
    public AudioSource music;
    public AudioSource anchor;
    [Tooltip("Точки крепления якоря на стеллажах: имя = AnchorMount_<Shelf>")]
    public Transform[] anchorMounts = new Transform[0];
    [Tooltip("Пауза между повторами джингла якоря, с")]
    public float anchorInterval = 6f;
    public float fadeSeconds = 1.5f;
    public float musicVolume = 0.45f, anchorVolume = 0.8f;

    public static StoreAudioDirector Instance { get; private set; }
    public string AnchorShelf { get; private set; } = "";
    public bool MusicOn { get; private set; }
    public int AnchorPlays { get; private set; }
    public readonly List<string> Journal = new List<string>();

    Coroutine musicFade, anchorLoop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Instance = null;

    void Awake()
    {
        Instance = this;
        if (context == null) context = ExperimentContext.Find();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable() { if (context != null) context.ConditionChanged += Apply; }
    void OnDisable() { if (context != null) context.ConditionChanged -= Apply; }

    void Start() { if (context != null) Apply(context.condition); }

    public void Apply(ExperimentConditionSO c)
    {
        // ---- музыка ----
        bool want = c != null && c.backgroundMusic && c.musicClip != null;
        if (music != null)
        {
            if (musicFade != null) StopCoroutine(musicFade);
            if (want)
            {
                if (music.clip != c.musicClip || !music.isPlaying) { music.clip = c.musicClip; music.loop = true; music.volume = 0f; music.Play(); }
                musicFade = StartCoroutine(Fade(music, musicVolume, false));
            }
            else if (music.isPlaying) musicFade = StartCoroutine(Fade(music, 0f, true));
        }
        MusicOn = want;

        // ---- звуковой якорь ----
        AnchorShelf = c != null ? c.audioAnchorShelf ?? "" : "";
        var mount = anchorMounts.FirstOrDefault(m => m != null && m.name == "AnchorMount_" + AnchorShelf);
        if (anchorLoop != null) { StopCoroutine(anchorLoop); anchorLoop = null; }
        if (anchor != null)
        {
            if (mount != null)
            {
                anchor.transform.SetPositionAndRotation(mount.position, mount.rotation);
                anchor.volume = anchorVolume;
                anchorLoop = StartCoroutine(AnchorLoop());
            }
            else if (anchor.isPlaying) StartCoroutine(Fade(anchor, 0f, true));
        }
        Note($"условие {c?.conditionName}: музыка {(want ? c.musicClip.name + $" ({c.musicTempoBpm:0} уд/мин)" : "выкл")}, якорь {(mount != null ? AnchorShelf : "нет")}");
    }

    IEnumerator AnchorLoop()
    {
        yield return new WaitForSeconds(0.5f);
        while (true)
        {
            anchor.volume = anchorVolume;
            anchor.Play();
            AnchorPlays++;
            yield return new WaitForSeconds(Mathf.Max(0.5f, (anchor.clip != null ? anchor.clip.length : 0f) + anchorInterval));
        }
    }

    IEnumerator Fade(AudioSource s, float to, bool stopAtEnd)
    {
        float from = s.volume, t = 0f;
        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            s.volume = Mathf.Lerp(from, to, t / fadeSeconds);
            yield return null;
        }
        s.volume = to;
        if (stopAtEnd) s.Stop();
    }

    void Note(string s)
    {
        var line = $"t={Time.time,7:0.00}  {s}";
        Journal.Add(line);
        Debug.Log("[NS-AUD] " + s);
    }
}
