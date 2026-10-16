using UnityEngine;

// Вход в зал (ЛР7): распашная дверь тамбура звучит при открывании, колокольчик — когда участник входит
// в зону Entry. Источник висит на петле двери, звук приходит со стороны двери.
[RequireComponent(typeof(AudioSource))]
public class EntryAudio : MonoBehaviour
{
    public ProximityDoor door;
    public ScenarioZone entryZone;
    public AudioClip doorOpen, chime;

    public int DoorSounds { get; private set; }
    public int Chimes { get; private set; }

    AudioSource src;
    bool wasOpen;

    void Awake() => src = GetComponent<AudioSource>();

    void OnEnable() { if (entryZone != null) entryZone.PresenceChanged += OnZone; }
    void OnDisable() { if (entryZone != null) entryZone.PresenceChanged -= OnZone; }

    void Update()
    {
        if (door == null) return;
        if (door.IsOpen && !wasOpen && doorOpen != null) { src.PlayOneShot(doorOpen, 0.8f); DoorSounds++; }
        wasOpen = door.IsOpen;
    }

    void OnZone(ScenarioZone z, bool inside)
    {
        if (!inside || chime == null) return;
        src.PlayOneShot(chime, 0.7f);
        Chimes++;
    }
}
