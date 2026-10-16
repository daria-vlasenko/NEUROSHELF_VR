using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Обратная связь холста: звук идёт от самого холста (AudioSource на холсте, 3D, через спатиалайзер),
// поэтому щелчок слышен с той стороны, где панель; вибрация — на руку, которой выполнено действие.
// Перед каждым щелчком источник переставляется в точку элемента: у широкого табло (1.6 м) левая и правая
// кнопки звучат с разных сторон.
public class UIFeedback : MonoBehaviour
{
    public AudioClip hover, press, deny;
    [Range(0f, 1f)] public float hoverVolume = 0.35f, pressVolume = 0.8f, denyVolume = 0.8f;

    [Tooltip("Источник на дочернем объекте холста: его переставляют в точку элемента, сам холст не двигается")]
    public AudioSource source;
    public AudioSource Source => source != null ? source : (source = GetComponentInChildren<AudioSource>());
    public int Played { get; private set; }
    public string LastClip { get; private set; } = "";
    public Vector3 LastPosition { get; private set; }

    void Play(AudioClip c, float v, Component at)
    {
        if (c == null) return;
        if (at != null) Source.transform.position = Center(at.transform);
        Source.PlayOneShot(c, v);
        Played++; LastClip = c.name; LastPosition = Source.transform.position;
    }

    public static Vector3 Center(Transform t) => t is RectTransform rt ? rt.TransformPoint(rt.rect.center) : t.position;

    public void Hover(XRCanvasButton b, InteractorHandedness hand)
    {
        Play(hover, hoverVolume, b);
        HapticFeedback.Play(HapticFeedback.Pattern.UIHover, hand, b.id);
    }

    public void Press(XRCanvasButton b, InteractorHandedness hand)
    {
        Play(press, pressVolume, b);
        HapticFeedback.Play(HapticFeedback.Pattern.UIPress, hand, b.id);
    }

    public void Deny(XRCanvasButton b, InteractorHandedness hand)
    {
        Play(deny, denyVolume, b);
        HapticFeedback.Play(HapticFeedback.Pattern.UIDeny, hand, b.id);
    }
}
