using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Раскладка товаров по планограмме текущего условия эксперимента.
// Подписан на ExperimentContext.ConditionChanged: сценарий меняет условие (A → C), а этот компонент
// переставляет товары, ячейки полок (сокеты ЛР5) и ценники. Сценарий о раскладке не знает.
//
// Товар, лежащий в корзине или в руке, сначала отпускается (штатный SelectExit XRI), затем ячейка его
// полки переезжает на новое место и забирает товар (SelectEnter). Корзина после смены условия пуста,
// полки полные — заход C начинается в тех же условиях, что и A, кроме самой выкладки.
[DefaultExecutionOrder(-50)]
public class PlanogramApplier : MonoBehaviour
{
    public ExperimentContext context;
    public Transform priceTagRoot;
    public Material tagMaterial;          // URP Unlit — цвет задаётся MaterialPropertyBlock
    [Tooltip("Z кромки полок со стороны прохода (ценники)")]
    public float shelfEdgeZ = -1.19f;
    public Color tagColor = new Color(0.96f, 0.96f, 0.93f);
    public Color tagAnchorColor = new Color(1f, 0.86f, 0.25f);
    public Color highlightColor = new Color(1f, 0.55f, 0.1f);
    public float highlightSeconds = 2.5f;

    public PlanogramSO Applied { get; private set; }
    public int LastMoved { get; private set; }
    public static event System.Action<PlanogramSO> PlanogramApplied;

    readonly Dictionary<string, PriceTag> tags = new Dictionary<string, PriceTag>();
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    class PriceTag
    {
        public Transform root; public Renderer back; public Text name, price, anchor; public GameObject strike;
        public float highlightUntil; public Color baseColor;
    }

    MaterialPropertyBlock mpb;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (context == null) context = ExperimentContext.Find();
    }

    void OnEnable()
    {
        if (context == null) context = ExperimentContext.Find();
        if (context != null) context.ConditionChanged += OnCondition;
        ProductInteractionHub.Raised += OnProduct;
    }

    void OnDisable()
    {
        if (context != null) context.ConditionChanged -= OnCondition;
        ProductInteractionHub.Raised -= OnProduct;
    }

    void Start()
    {
        // стартовая раскладка — та же, что сделал редактор (ЛР5); выравниваем только ценники и данные товаров
        if (context != null && context.condition != null && context.condition.planogram != null) Apply(context.condition.planogram, false);
    }

    void OnCondition(ExperimentConditionSO c)
    {
        if (c != null && c.planogram != null) Apply(c.planogram, true);
    }

    public void Apply(PlanogramSO plan, bool restock)
    {
        if (plan == null) return;
        Applied = plan;
        LastMoved = 0;
        foreach (var row in plan.rows)
            for (int i = 0; i < row.products.Length; i++)
            {
                var go = GameObject.Find(row.products[i]);
                var item = go != null ? go.GetComponent<ProductItem>() : null;
                if (item == null) { Debug.LogWarning("[NS-SCN] Планограмма: нет товара " + row.products[i]); continue; }
                var pos = plan.PositionOf(row, i);
                if (Place(item, pos, row, restock)) LastMoved++;
                UpdateTag(item, plan, pos, row);
            }
        Physics.SyncTransforms();
        Debug.Log($"[NS-SCN] Планограмма {plan.planogramName}: переставлено товаров {LastMoved}");
        PlanogramApplied?.Invoke(plan);
    }

    bool Place(ProductItem item, Vector3 pos, PlanogramSO.Row row, bool restock)
    {
        bool moved = (item.transform.position - pos).sqrMagnitude > 1e-4f;
        item.tier = row.tier;
        item.tierHeight = PlanogramSO.TierY[row.tier];
        var grab = item.GetComponent<XRGrabInteractable>();
        var mgr = grab != null ? grab.interactionManager : null;
        var home = item.homeSlot;

        if (restock && grab != null && mgr != null)
        {
            // отпустить из руки или корзины
            foreach (var i in grab.interactorsSelecting.ToList())
                if (!(i is ProductSocket s && s == home)) mgr.SelectExit(i, (IXRSelectInteractable)grab);
        }
        if (home != null) home.transform.SetPositionAndRotation(pos, Quaternion.identity);
        if (moved || restock)
        {
            var rb = item.Body;
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            item.transform.SetPositionAndRotation(pos, Quaternion.identity);
        }
        if (restock && grab != null && mgr != null && home != null && !grab.isSelected)
            mgr.SelectEnter((IXRSelectInteractor)home, (IXRSelectInteractable)grab);
        return moved;
    }

    // ---------- ценники ----------

    void UpdateTag(ProductItem item, PlanogramSO plan, Vector3 pos, PlanogramSO.Row row)
    {
        if (priceTagRoot == null) return;
        if (!tags.TryGetValue(item.productId, out var t)) tags[item.productId] = t = BuildTag(item.productId);
        var price = plan.PriceOf(item.productId);
        // ценник на кромке полки, под товаром, лицом в проход. Холст uGUI читается со стороны −z своих осей,
        // поэтому у стеллажей южного ряда (z < 0) ярлык развёрнут на 180°.
        float side = row.z < 0f ? 1f : -1f;
        t.root.position = new Vector3(pos.x, pos.y - PlanogramSO.BoardHalf - 0.045f, shelfEdgeZ * side);
        t.root.rotation = Quaternion.Euler(0f, side > 0f ? 180f : 0f, 0f);
        bool anchor = price != null && price.anchorPrice > 0f;
        t.price.text = price != null ? price.price.ToString("0.00", ci) : "—";
        t.anchor.text = anchor ? price.anchorPrice.ToString("0.00", ci) : "";
        t.strike.SetActive(anchor);
        t.baseColor = anchor ? tagAnchorColor : tagColor;
        WorldText.Tint(t.back, t.baseColor, mpb);
    }

    PriceTag BuildTag(string id)
    {
        var root = priceTagRoot.Find("Tag_" + id);
        if (root == null)
        {
            root = new GameObject("Tag_" + id).transform;
            root.SetParent(priceTagRoot, false);
        }
        var back = root.Find("Back");
        if (back == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Back"; Destroy(g.GetComponent<Collider>());
            back = g.transform; back.SetParent(root, false);
            if (tagMaterial != null) g.GetComponent<Renderer>().sharedMaterial = tagMaterial;
        }
        back.localPosition = new Vector3(0f, 0f, 0.004f);
        back.localScale = new Vector3(0.11f, 0.065f, 0.006f);
        var cv = WorldText.Canvas(root, "Canvas", new Vector2(0.11f, 0.065f), Vector3.zero, 8f);
        var t = new PriceTag
        {
            root = root, back = back.GetComponent<Renderer>(),
            name = WorldText.Label(cv.transform, "Name", new Rect(4, 2, 102, 18), 14, TextAnchor.UpperLeft, FontStyle.Bold),
            price = WorldText.Label(cv.transform, "Price", new Rect(36, 22, 70, 40), 30, TextAnchor.LowerRight, FontStyle.Bold),
            anchor = WorldText.Label(cv.transform, "Anchor", new Rect(4, 34, 40, 22), 15, TextAnchor.MiddleLeft),
        };
        t.name.text = id;
        foreach (var tx in new[] { t.name, t.price, t.anchor }) tx.color = new Color(0.08f, 0.08f, 0.1f);
        t.anchor.color = new Color(0.55f, 0.1f, 0.1f);
        var strike = root.Find("Strike");
        if (strike == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Strike"; Destroy(g.GetComponent<Collider>());
            strike = g.transform; strike.SetParent(root, false);
            g.GetComponent<Renderer>().sharedMaterial = t.back.sharedMaterial;
        }
        // поверх старой цены: центр поля Anchor (x 4…44 мм, y 34…56 мм от левого верхнего угла 110 × 65 мм)
        strike.localPosition = new Vector3(-0.031f, -0.0125f, -0.0015f);
        strike.localScale = new Vector3(0.036f, 0.0025f, 0.001f);
        WorldText.Tint(strike.GetComponent<Renderer>(), new Color(0.75f, 0.1f, 0.1f), mpb);
        t.strike = strike.gameObject;
        return t;
    }

    void OnProduct(ProductInteractionEvent e)
    {
        if (e.type != ProductEventType.Picked || !tags.TryGetValue(e.productId, out var t)) return;
        t.highlightUntil = Time.time + highlightSeconds;
        WorldText.Tint(t.back, highlightColor, mpb);
    }

    void Update()
    {
        foreach (var t in tags.Values)
            if (t.highlightUntil > 0f && Time.time > t.highlightUntil)
            {
                t.highlightUntil = 0f;
                WorldText.Tint(t.back, t.baseColor, mpb);
            }
    }

    public bool IsHighlighted(string productId) => tags.TryGetValue(productId, out var t) && t.highlightUntil > Time.time;
    public Transform TagOf(string productId) => tags.TryGetValue(productId, out var t) ? t.root : null;
}
