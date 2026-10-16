using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Чек на корзине: что уже положено и на какую сумму. Пассивный холст на дальнем борту корзины,
// наклонён к глазам: участник видит его, опустив взгляд на корзину, без отдельного жеста.
// Обновляется по событиям товара (ЛР5) и смене условия (цены берутся из планограммы текущего условия).
public class BasketReceipt : MonoBehaviour
{
    public BasketCarry basket;
    public Text lines, total;
    public ShoppingListCard card;

    public string Shown => (lines != null ? lines.text : "") + "\n" + (total != null ? total.text : "");
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    void OnEnable()
    {
        ProductInteractionHub.Raised += OnProduct;
        var ctx = ExperimentContext.Find(); if (ctx != null) ctx.ConditionChanged += OnCond;
        Refresh();
    }

    void OnDisable()
    {
        ProductInteractionHub.Raised -= OnProduct;
        var ctx = ExperimentContext.Find(); if (ctx != null) ctx.ConditionChanged -= OnCond;
    }

    void OnProduct(ProductInteractionEvent e) { if (isActiveAndEnabled) Invoke(nameof(Refresh), 0.05f); }
    void OnCond(ExperimentConditionSO c) { if (isActiveAndEnabled) Invoke(nameof(Refresh), 0.1f); }

    public void Refresh()
    {
        if (basket == null) return;
        var plan = ExperimentContext.Find()?.condition?.planogram;
        var items = basket.Items.ToList();
        var sb = new StringBuilder();
        float sum = 0f;
        foreach (var it in items)
        {
            float p = plan?.PriceOf(it.productId)?.price ?? 0f;
            sum += p;
            sb.Append(string.Format(ci, "{0,-7}{1,7:0.00}\n", it.productId, p));
        }
        if (items.Count == 0) sb.Append("— корзина пуста —");
        if (lines != null) lines.text = sb.ToString().TrimEnd();
        float budget = card != null ? card.budget : 0f;
        if (total != null)
            total.text = string.Format(ci, "<b>Итого {0:0.00}</b>", sum) +
                         (budget > 0f && sum > budget ? string.Format(ci, "  <color=#ff8a65>+{0:0.00}</color>", sum - budget) : "");
    }
}
