using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Товар на полке. Стоит рядом со штатным XR Grab Interactable и переводит его
// события (Select Entered / Select Exited) в события исследования:
// что взято, с какого яруса, рукой или лучом, сколько держали, чем закончилось —
// покупка (корзина), возврат на полку, бросок или падение на пол.
// Методы OnSelectEntered / OnSelectExited подключены к UnityEvent-ам XR Grab Interactable
// в инспекторе (видно в разделе Interactable Events) — это и есть точка расширения.
[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class ProductItem : MonoBehaviour, IXRSelectFilter
{
    [Header("Идентификация")]
    public string productId;
    public PackagingType packaging;
    [Tooltip("0 — нижний ярус 0.60 м, 1 — средний 1.10 м, 2 — уровень глаз 1.60 м")]
    public int tier;
    public float tierHeight;

    [Header("Планограмма")]
    [Tooltip("Ячейка полки, откуда товар взят. Возврат сюда = «вернул на место».")]
    public ProductSocket homeSlot;

    [Header("Политика захвата")]
    [Tooltip("Разрешить брать лучом. Для чистого замера цены доступа к ярусу луч можно запретить.")]
    public bool allowFarGrab = true;
    [Tooltip("Дальше этого расстояния от руки захват считается дистанционным (Far), м")]
    public float nearGrabMaxDistance = 0.35f;
    [Tooltip("Скорость товара после отпускания, выше которой это бросок, м/с")]
    public float throwSpeedThreshold = 1.2f;
    [Tooltip("Сколько ждать, подхватит ли товар сокет после отпускания, с")]
    public float outcomeWindow = 0.35f;

    XRGrabInteractable grab;
    Rigidbody rb;
    IXRSelectInteractor handHolder;
    float pickTime;
    GrabRegion pickRegion;
    ProductSocket lastSocket;
    float lastSocketExitTime = -10f;
    bool onFloor, awaitingFloor, pendingRelease, initialized;
    ReleaseOutcome pendingOutcome;
    Vector3 handPrev, handVel;

    public Transform Grip
    {
        get
        {
            if (grab == null) grab = GetComponent<XRGrabInteractable>();
            return grab != null ? grab.attachTransform : null;
        }
    }
    public bool HeldByHand => handHolder != null;
    public IXRSelectInteractor HandHolder => handHolder;
    public GrabRegion LastRegion => pickRegion;
    public float HandSpeed => handVel.magnitude;
    public Rigidbody Body => rb;

    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();
    }

    void OnEnable() { if (grab != null) grab.selectFilters.Add(this); }
    void OnDisable() { if (grab != null) grab.selectFilters.Remove(this); }

    // Стартовая выкладка (полка забирает товар при загрузке сцены) не пишется как действие покупателя.
    IEnumerator Start()
    {
        yield return null;
        yield return null;
        initialized = true;
    }

    // ---------- IXRSelectFilter: можно ли взять этим интерактором ----------
    public bool canProcess => isActiveAndEnabled;

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (allowFarGrab || interactor is XRSocketInteractor) return true;
        if (handHolder == interactor) return true;
        return Vector3.Distance(interactor.transform.position, rb.worldCenterOfMass) <= nearGrabMaxDistance;
    }

    void Update()
    {
        if (handHolder == null) return;
        var p = handHolder.transform.position;
        if (Time.deltaTime > 0f) handVel = Vector3.Lerp(handVel, (p - handPrev) / Time.deltaTime, 0.5f);
        handPrev = p;
    }

    // ---------- точки расширения: подключены к XR Grab Interactable → Interactable Events ----------

    public void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (args.interactorObject is ProductSocket socket) { OnSocketEntered(socket); return; }

        var from = lastSocket != null && Time.time - lastSocketExitTime < 0.1f
            ? (lastSocket.kind == SocketKind.Basket ? ProductPlace.Basket : ProductPlace.Shelf)
            : pendingRelease ? ProductPlace.Hand
            : onFloor ? ProductPlace.Floor : ProductPlace.Air;
        if (pendingRelease) pendingOutcome = ReleaseOutcome.HandToHand;

        handHolder = args.interactorObject;
        pickTime = Time.time;
        handPrev = handHolder.transform.position;
        handVel = Vector3.zero;
        onFloor = awaitingFloor = false;

        float dist = Vector3.Distance(handHolder.transform.position, rb.worldCenterOfMass);
        pickRegion = dist > nearGrabMaxDistance ? GrabRegion.Far : GrabRegion.Near;
        if (handHolder is NearFarInteractor nf && nf.selectionRegion.Value != NearFarInteractor.Region.None)
            pickRegion = nf.selectionRegion.Value == NearFarInteractor.Region.Far ? GrabRegion.Far : GrabRegion.Near;

        var e = Make(ProductEventType.Picked);
        e.hand = HandName(handHolder);
        e.from = from;
        e.region = pickRegion;
        e.distance = dist;
        e.handHeight = handHolder.transform.position.y;
        ProductInteractionHub.Raise(e);
    }

    public void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject is ProductSocket socket)
        {
            lastSocket = socket;
            lastSocketExitTime = Time.time;
            return;
        }
        if (args.interactorObject != handHolder) return;

        float held = Time.time - pickTime;
        float hs = handVel.magnitude;
        string hand = HandName(handHolder);
        float handY = handHolder.transform.position.y;
        handHolder = null;
        pendingRelease = true;
        pendingOutcome = ReleaseOutcome.None;
        if (isActiveAndEnabled) StartCoroutine(ResolveRelease(held, hs, hand, handY));
    }

    IEnumerator ResolveRelease(float held, float handSpeed, string hand, float handY)
    {
        // скорость броска XR Grab Interactable выставляет при отпускании — читаем после двух шагов физики
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        float speed = rb.isKinematic ? 0f : rb.linearVelocity.magnitude;
        float t0 = Time.time;
        while (Time.time - t0 < outcomeWindow && pendingOutcome == ReleaseOutcome.None) yield return null;
        if (pendingOutcome == ReleaseOutcome.None)
        {
            pendingOutcome = speed >= throwSpeedThreshold ? ReleaseOutcome.Thrown : ReleaseOutcome.Dropped;
            awaitingFloor = !onFloor;
        }
        pendingRelease = false;

        var e = Make(ProductEventType.Released);
        e.hand = hand;
        e.region = pickRegion;
        e.holdSeconds = held;
        e.releaseSpeed = speed;
        e.handSpeed = handSpeed;
        e.handHeight = handY;
        e.outcome = pendingOutcome;
        ProductInteractionHub.Raise(e);
    }

    void OnSocketEntered(ProductSocket s)
    {
        onFloor = awaitingFloor = false;
        if (!initialized) return;
        if (pendingRelease)
            pendingOutcome = s.kind == SocketKind.Basket ? ReleaseOutcome.Purchased : ReleaseOutcome.Returned;

        var e = Make(ProductEventType.Placed);
        e.hand = s.name;
        e.place = s.kind == SocketKind.Basket ? ProductPlace.Basket : ProductPlace.Shelf;
        e.slot = s.slotIndex;
        e.ownSlot = s == homeSlot;
        ProductInteractionHub.Raise(e);
    }

    // ---------- пол ----------

    void OnCollisionEnter(Collision c)
    {
        if (!IsFloor(c.collider)) return;
        if (awaitingFloor && handHolder == null)
        {
            awaitingFloor = false;
            ProductInteractionHub.Raise(Make(ProductEventType.LandedOnFloor));
        }
        onFloor = true;
    }

    void OnCollisionExit(Collision c) { if (IsFloor(c.collider)) onFloor = false; }

    static bool IsFloor(Collider c) =>
        c.name.StartsWith("Floor") || (c.bounds.max.y < 0.05f && c.bounds.size.x > 2f);

    // ---------- служебное ----------

    ProductInteractionEvent Make(ProductEventType type)
    {
        var cam = Camera.main;
        return new ProductInteractionEvent
        {
            type = type,
            productId = productId,
            packaging = packaging,
            tier = tier,
            tierHeight = tierHeight,
            headHeight = cam != null ? cam.transform.position.y : 0f,
        };
    }

    static string HandName(IXRSelectInteractor i)
    {
        if (i is XRBaseInteractor b && b.handedness != InteractorHandedness.None) return b.handedness.ToString();
        return i != null ? i.transform.name : "?";
    }
}
