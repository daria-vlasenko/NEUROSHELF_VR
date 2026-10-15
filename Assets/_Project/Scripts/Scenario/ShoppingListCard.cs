using System;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Список покупок на стойке у входа. Выбор (рукой или лучом) — «взял список»: карточка переезжает
// на левое запястье и дальше работает как чек: показывает, что лежит в корзине, и сумму.
// Флаг ListTaken не сбрасывается до конца сессии: потерять список нельзя.
[RequireComponent(typeof(XRSimpleInteractable))]
public class ShoppingListCard : MonoBehaviour
{
    [TextArea(3, 8)]
    public string task = "1. Напиток в банке — 1 шт.\n2. Напиток в бутылке — 1 шт.\n3. Товар в пачке — 1 шт.";
    public float budget = 15f;
    public Transform wrist;                         // левый контроллер
    public Vector3 wristOffset = new Vector3(0.03f, 0.05f, 0.02f);
    public Vector3 wristEuler = new Vector3(-60f, 0f, 0f);   // лист наклонён к лицу, как часы на руке
    public Text text;
    public BasketCarry basket;

    public bool Taken { get; private set; }
    public event Action<ShoppingListCard> TakenChanged;

    XRSimpleInteractable si;
    Vector3 standPos; Quaternion standRot; Transform standParent;
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    void Awake()
    {
        si = GetComponent<XRSimpleInteractable>();
        si.selectEntered.AddListener(_ => Take());
        standParent = transform.parent; standPos = transform.localPosition; standRot = transform.localRotation;
    }

    void OnEnable() { ProductInteractionHub.Raised += OnProduct; Refresh(); }
    void OnDisable() { ProductInteractionHub.Raised -= OnProduct; }

    void OnProduct(ProductInteractionEvent e)
    {
        if (e.type == ProductEventType.Placed || e.type == ProductEventType.Picked) Invoke(nameof(Refresh), 0.05f);
    }

    public void Take()
    {
        if (Taken) return;
        Taken = true;
        si.interactionLayers = 0;    // взятый список больше не выбирается
        if (wrist != null)
        {
            transform.SetParent(wrist, false);
            transform.localPosition = wristOffset;
            transform.localRotation = Quaternion.Euler(wristEuler);
        }
        Refresh();
        TakenChanged?.Invoke(this);
    }

    public void ResetToStand()
    {
        Taken = false;
        si.interactionLayers = 1;
        transform.SetParent(standParent, false);
        transform.localPosition = standPos; transform.localRotation = standRot;
        Refresh();
        TakenChanged?.Invoke(this);
    }

    public void Refresh()
    {
        if (text == null) return;
        var sb = new StringBuilder();
        sb.Append("<b>СПИСОК ПОКУПОК</b>\n").Append(task).Append('\n');
        sb.Append(string.Format(ci, "Бюджет: <b>{0:0.00}</b>\n", budget));
        if (Taken && basket != null)
        {
            var plan = ExperimentContext.Find()?.condition?.planogram;
            float sum = 0f;
            sb.Append("\n<b>В корзине:</b>\n");
            var items = basket.Items.ToList();
            if (items.Count == 0) sb.Append("— пусто —\n");
            foreach (var it in items)
            {
                float p = plan?.PriceOf(it.productId)?.price ?? 0f;
                sum += p;
                sb.Append(string.Format(ci, "{0,-8} {1:0.00}\n", it.productId, p));
            }
            sb.Append(string.Format(ci, "<b>Итого: {0:0.00}</b>", sum));
            if (sum > budget) sb.Append(string.Format(ci, "  <color=#ff8a65>(+{0:0.00} к бюджету)</color>", sum - budget));
        }
        text.text = sb.ToString();
    }
}
