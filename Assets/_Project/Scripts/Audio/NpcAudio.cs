using UnityEngine;
using UnityEngine.AI;

// Шаги и бормотание NPC-покупателя (ЛР4). Звук считает процессор, а у ноутбука нет запаса:
//  • шаги — короткие one-shot (0.26 с) на одном источнике, по пройденному расстоянию, а не по таймеру;
//  • радиус слышимости 6 м, низкий приоритет (200) — при нехватке голосов Unity первыми выключает их;
//  • без Steam Audio: встроенная панорама Unity, без HRTF — направление шагов за спиной не нужно для эксперимента.
[RequireComponent(typeof(AudioSource))]
public class NpcAudio : MonoBehaviour
{
    public NavMeshAgent agent;
    public AudioClip[] steps = new AudioClip[0];
    [Tooltip("Длина шага при ходьбе, м")]
    public float stride = 0.72f;
    public float stepVolume = 0.55f;
    [Tooltip("Источник бормотания (необязательно, отдельный дочерний объект)")]
    public AudioSource murmur;

    public int StepsPlayed { get; private set; }

    AudioSource src;
    float travelled;
    Vector3 last;
    int lastClip = -1;

    void Awake()
    {
        src = GetComponent<AudioSource>();
        if (agent == null) agent = GetComponentInParent<NavMeshAgent>();
        last = transform.position;
    }

    void OnEnable()
    {
        if (murmur != null && murmur.clip != null && !murmur.isPlaying)
        {
            murmur.time = Random.Range(0f, murmur.clip.length * 0.9f);   // голоса не в унисон
            murmur.Play();
        }
    }

    void Update()
    {
        var p = transform.position;
        var d = p - last; d.y = 0f;
        last = p;
        if (agent != null && agent.velocity.sqrMagnitude < 0.01f) { travelled = 0f; return; }
        travelled += d.magnitude;
        if (travelled < stride || steps.Length == 0) return;
        travelled = 0f;
        int i = Random.Range(0, steps.Length);
        if (i == lastClip && steps.Length > 1) i = (i + 1) % steps.Length;
        lastClip = i;
        src.pitch = Random.Range(0.94f, 1.06f);
        src.PlayOneShot(steps[i], stepVolume);
        StepsPlayed++;
    }
}
