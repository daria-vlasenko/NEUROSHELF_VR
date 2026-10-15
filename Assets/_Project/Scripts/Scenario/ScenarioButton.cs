using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Кнопка сценария: куб + XR Simple Interactable, нажимается выбором лучом (курок).
// Недоступная кнопка не только серая: у неё сброшен слой взаимодействия (Interaction Layers = Nothing),
// поэтому луч её физически не выбирает. Недоступность проверяется не глазами, а самим XRI.
[RequireComponent(typeof(XRSimpleInteractable))]
public class ScenarioButton : MonoBehaviour
{
    public string buttonId = "Start";
    public string label = "Начать";
    public Renderer face;
    public Text text;
    public Color availableColor = new Color(0.16f, 0.72f, 0.36f);
    public Color unavailableColor = new Color(0.30f, 0.31f, 0.33f);
    public Color pressedColor = new Color(0.55f, 0.95f, 0.65f);
    [Tooltip("Слои взаимодействия доступной кнопки")]
    public InteractionLayerMask enabledLayers = 1;

    public bool Available { get; private set; }
    public int PressCount { get; private set; }
    public event Action<ScenarioButton> Pressed;

    XRSimpleInteractable si;
    MaterialPropertyBlock mpb;
    bool initialized;

    public XRSimpleInteractable Interactable => si != null ? si : (si = GetComponent<XRSimpleInteractable>());

    void Awake() => Init();

    void Init()
    {
        if (initialized) return;
        initialized = true;
        mpb = new MaterialPropertyBlock();
        Interactable.selectEntered.AddListener(OnSelect);
        if (text != null) text.text = label;
        SetAvailable(false);
    }

    void OnDestroy() { if (si != null) si.selectEntered.RemoveListener(OnSelect); }

    void OnSelect(SelectEnterEventArgs _) => Press();

    public void SetAvailable(bool on)
    {
        Init();
        Available = on;
        Interactable.interactionLayers = on ? enabledLayers : (InteractionLayerMask)0;
        WorldText.Tint(face, on ? availableColor : unavailableColor, mpb);
        if (text != null) text.color = on ? Color.white : new Color(0.62f, 0.63f, 0.66f);
    }

    /// То же, что выбор лучом. Возвращает false, если кнопка недоступна.
    public bool Press()
    {
        Init();
        if (!Available) return false;
        PressCount++;
        if (isActiveAndEnabled) StartCoroutine(Flash());
        Pressed?.Invoke(this);
        return true;
    }

    IEnumerator Flash()
    {
        var p0 = face != null ? face.transform.localPosition : Vector3.zero;
        if (face != null) face.transform.localPosition = p0 + new Vector3(0f, 0f, 0.012f);
        WorldText.Tint(face, pressedColor, mpb);
        yield return new WaitForSeconds(0.15f);
        if (face != null) face.transform.localPosition = p0;
        WorldText.Tint(face, Available ? availableColor : unavailableColor, mpb);
    }
}
