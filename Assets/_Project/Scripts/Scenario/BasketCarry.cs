using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Корзина как условие сценария. Корзина из ЛР5 (6 сокетов «положил = купил») и rest frame из ЛР6
// не изменены: этот компонент лишь включает и выключает их.
//
//   OnStand  — стоит на стойке у входа; BodyAnchor и BasketRestFrame выключены
//   InHand   — взята за ручку (XR Grab Interactable, Select Entered рукой)
//   Carried  — отпущена у тела → садится на тело, работает rest frame ЛР6
//   Dropped  — отпущена в стороне от тела → падает (Rigidbody перестаёт быть кинематическим)
//
// Флаг сценария BasketCarried = InHand или Carried. Источник — события захвата XRI, а не опрос позиций.
[RequireComponent(typeof(XRGrabInteractable))]
[DefaultExecutionOrder(40)]
public class BasketCarry : MonoBehaviour
{
    public enum Mode { OnStand, InHand, Carried, Dropped }

    public Transform standPoint;
    public BodyAnchor bodyAnchor;
    public BasketRestFrame restFrame;
    public Transform head;
    [Tooltip("Отпущенная ближе этого расстояния от оси тела (по горизонтали) корзина садится на тело, м")]
    public float mountRadius = 0.75f;
    [Tooltip("Начать с корзиной на теле (сцены и проверки ЛР5–ЛР6)")]
    public bool startCarried;

    [SerializeField] Mode mode = Mode.OnStand;
    public Mode State => mode;
    public bool IsCarried => mode == Mode.InHand || mode == Mode.Carried;
    public string LastChangeReason { get; private set; } = "";

    public event Action<BasketCarry> Changed;

    XRGrabInteractable grab;
    Rigidbody rb;
    ProductSocket[] slots;

    public XRGrabInteractable Grab => grab != null ? grab : (grab = GetComponent<XRGrabInteractable>());
    public IEnumerable<ProductItem> Items =>
        Slots.Where(s => s != null && s.hasSelection && (s.firstInteractableSelected as Object) != null)
             .Select(s => s.firstInteractableSelected.transform.GetComponent<ProductItem>()).Where(i => i != null);
    public ProductSocket[] Slots => slots ??= GetComponentsInChildren<ProductSocket>(true);
    public int ItemCount => Items.Count();

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
        if (head == null && Camera.main != null) head = Camera.main.transform;
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void Start()
    {
        if (startCarried || ScenarioRuntime.LegacyDemoActive) Mount("старт сцены ЛР5/ЛР6");
        else PlaceOnStand("старт сценария");
    }

    void OnDestroy()
    {
        if (grab == null) return;
        grab.selectEntered.RemoveListener(OnGrab);
        grab.selectExited.RemoveListener(OnRelease);
    }

    static bool IsHand(IXRSelectInteractor i) => !(i is XRSocketInteractor);

    void OnGrab(SelectEnterEventArgs a)
    {
        if (!IsHand(a.interactorObject)) return;
        Anchors(false);
        Set(Mode.InHand, "взята рукой: " + a.interactorObject.transform.name);
    }

    void OnRelease(SelectExitEventArgs a)
    {
        if (!IsHand(a.interactorObject) || a.isCanceled && !isActiveAndEnabled) return;
        if (NearBody()) Mount("отпущена у тела");
        else Drop("отпущена в стороне от тела");
    }

    public bool NearBody()
    {
        if (head == null) return false;
        var d = transform.position - head.position;
        float vertical = -d.y;
        d.y = 0f;
        return d.magnitude <= mountRadius && vertical > 0.1f;
    }

    public void Mount(string reason)
    {
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        Anchors(true);
        if (bodyAnchor != null) bodyAnchor.SnapNow();
        Set(Mode.Carried, reason);
    }

    public void Drop(string reason)
    {
        Anchors(false);
        if (rb != null)
        {
            rb.isKinematic = false; rb.useGravity = true;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
        }
        Set(Mode.Dropped, reason);
    }

    public void PlaceOnStand(string reason)
    {
        Anchors(false);
        if (rb != null)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.isKinematic = true; rb.useGravity = false;
        }
        if (standPoint != null) transform.SetPositionAndRotation(standPoint.position, standPoint.rotation);
        Set(Mode.OnStand, reason);
    }

    void Anchors(bool on)
    {
        if (bodyAnchor != null) bodyAnchor.enabled = on;
        if (restFrame != null) restFrame.enabled = on;
    }

    void Set(Mode m, string reason)
    {
        mode = m;
        LastChangeReason = reason;
        Changed?.Invoke(this);
    }
}
