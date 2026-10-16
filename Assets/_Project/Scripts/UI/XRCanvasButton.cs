using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Кнопка на World Space холсте для лучей XRI (XR UI Input Module + Tracked Device Graphic Raycaster).
// В отличие от стандартной uGUI Button недоступная кнопка остаётся целью луча: нажатие на неё — не «ничего»,
// а явный отказ (звук отказа + двойной импульс). Пользователь узнаёт, что нажатие дошло, но действие запрещено.
// Рука определяется по TrackedDeviceEventData.interactor — вибрирует та рука, которой нажали.
[RequireComponent(typeof(Image))]
public class XRCanvasButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public string id = "";
    public string text = "";
    public Image bg;
    public Text label;
    public Color availableColor = new Color(0.16f, 0.72f, 0.36f);
    public Color unavailableColor = new Color(0.30f, 0.31f, 0.33f);
    public Color hoverTint = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.55f, 0.95f, 0.65f);
    [Tooltip("Обратная связь холста (звук от холста + вибрация). Если не задана — ищется у родителя.")]
    public UIFeedback feedback;

    public bool Available { get; private set; } = true;
    public bool Hovered => hovering.Count > 0;
    public bool HoveredBy(InteractorHandedness h) => hovering.Contains(h);
    readonly HashSet<InteractorHandedness> hovering = new HashSet<InteractorHandedness>();
    public int Hovers { get; private set; }
    public int Clicks { get; private set; }
    public int Denials { get; private set; }
    public InteractorHandedness LastHand { get; private set; }

    public event Action<XRCanvasButton, InteractorHandedness> Clicked;
    public event Action<XRCanvasButton, InteractorHandedness> Denied;
    public event Action<XRCanvasButton, InteractorHandedness> HoverEntered;

    float flashUntil;

    UIFeedback Feedback => feedback != null ? feedback : (feedback = GetComponentInParent<UIFeedback>());

    void Awake()
    {
        if (bg == null) bg = GetComponent<Image>();
        if (label != null && text != "") label.text = text;
        Redraw();
    }

    public void SetAvailable(bool on)
    {
        if (Available == on) return;
        Available = on;
        Redraw();
    }

    void Redraw()
    {
        if (bg == null) return;
        var c = Time.time < flashUntil ? pressedColor : Available ? availableColor : unavailableColor;
        if (Hovered && Available) c = Color.Lerp(c, Color.white, 0.25f);
        bg.color = c;
        if (label != null) label.color = Available ? Color.white : new Color(0.62f, 0.63f, 0.66f);
    }

    void Update() { if (flashUntil > 0f && Time.time >= flashUntil) { flashUntil = 0f; Redraw(); } }

    public static InteractorHandedness HandOf(PointerEventData e)
    {
        if (e is TrackedDeviceEventData td && td.interactor is XRBaseInteractor bi) return bi.handedness;
        return InteractorHandedness.None;
    }

    public void OnPointerEnter(PointerEventData e) => Hover(HandOf(e));
    public void OnPointerExit(PointerEventData e) { hovering.Remove(HandOf(e)); Redraw(); }
    public void OnPointerClick(PointerEventData e) => Activate(HandOf(e));

    public void Hover(InteractorHandedness hand)
    {
        hovering.Add(hand); Hovers++; LastHand = hand;
        Redraw();
        Feedback?.Hover(this, hand);
        HoverEntered?.Invoke(this, hand);
    }

    /// Нажатие лучом (из OnPointerClick) или программно — один и тот же путь.
    public bool Activate(InteractorHandedness hand)
    {
        LastHand = hand;
        if (!Available)
        {
            Denials++;
            Feedback?.Deny(this, hand);
            Denied?.Invoke(this, hand);
            return false;
        }
        Clicks++;
        flashUntil = Time.time + 0.15f;
        Redraw();
        Feedback?.Press(this, hand);
        Clicked?.Invoke(this, hand);
        return true;
    }
}
