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
    public Material tagMaterial;          // не используется с ЛР8: ценник — холст PriceTagUI (поле оставлено для совместимости сцены)
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

    // Ценник — World Space Canvas (PriceTagUI, ЛР8). Раньше это был куб с текстом; содержимое и правила те же.
    class PriceTag
    {
        public Transform root; public PriceTagUI ui;
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
        t.ui.SetData(price != null ? price.price : 0f, anchor ? price.anchorPrice : 0f);
        t.baseColor = anchor ? tagAnchorColor : tagColor;
        t.ui.SetColor(t.baseColor);
    }

    PriceTag BuildTag(string id)
    {
        var ui = PriceTagUI.Build(priceTagRoot, id);
        return new PriceTag { root = ui.transform, ui = ui };
    }

    void OnProduct(ProductInteractionEvent e)
    {
        if (e.type != ProductEventType.Picked || !tags.TryGetValue(e.productId, out var t)) return;
        t.highlightUntil = Time.time + highlightSeconds;
        t.ui.SetColor(highlightColor);
    }

    void Update()
    {
        foreach (var t in tags.Values)
            if (t.highlightUntil > 0f && Time.time > t.highlightUntil)
            {
                t.highlightUntil = 0f;
                t.ui.SetColor(t.baseColor);
            }
    }

    public bool IsHighlighted(string productId) => tags.TryGetValue(productId, out var t) && t.highlightUntil > Time.time;
    public Transform TagOf(string productId) => tags.TryGetValue(productId, out var t) ? t.root : null;
    public PriceTagUI TagUI(string productId) => tags.TryGetValue(productId, out var t) ? t.ui : null;
    public IEnumerable<PriceTagUI> AllTags => tags.Values.Select(t => t.ui);
}
