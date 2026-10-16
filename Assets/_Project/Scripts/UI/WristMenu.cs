using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Наручное меню на левом контроллере: этап сценария, список покупок, бюджет, сумма корзины.
// Появляется, только когда участник смотрит на запястье: направление взгляда головы отклоняется от
// направления на меню меньше чем на showAngle, и лицевая сторона меню обращена к глазам.
// Скрывается с гистерезисом (hideAngle > showAngle), чтобы меню не мигало на границе.
// Холст пассивный — нажимать на запястье нечего, обе руки свободны для товара и табло.
//
// Список покупок со стойки (ShoppingListCard, ЛР7) остаётся источником условия «список взят»;
// после взятия его бумажный лист прячется, а содержимое показывает это меню — список «переехал» на часы.
public class WristMenu : MonoBehaviour
{
    public Transform head;
    public CanvasGroup group;
    public Text stage, list, sum;
    public Image sumBar;
    public ShoppingListCard card;
    public BasketCarry basket;
    public float showAngle = 25f, hideAngle = 35f, fadeSeconds = 0.15f;
    [Tooltip("Минимальный косинус между нормалью меню и направлением на глаза")]
    public float facingCos = 0.35f;

    public bool Shown { get; private set; }
    public float GazeAngle { get; private set; }
    public float Facing { get; private set; }
    public float Alpha => group != null ? group.alpha : 0f;

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    float nextText;
    bool cardHidden;

    void Start()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (group != null) group.alpha = 0f;
        if (card != null) card.TakenChanged += OnCard;
    }

    void OnDestroy() { if (card != null) card.TakenChanged -= OnCard; }

    void OnCard(ShoppingListCard c)
    {
        if (!c.Taken || cardHidden) return;
        foreach (var r in c.GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var cv in c.GetComponentsInChildren<Canvas>()) cv.enabled = false;
        cardHidden = true;
        Refresh();
    }

    void LateUpdate()
    {
        if (head == null || group == null) return;
        var toMenu = transform.position - head.position;
        GazeAngle = Vector3.Angle(head.forward, toMenu);
        // холст uGUI читается со стороны своей −z: глаза должны быть с этой стороны
        Facing = Vector3.Dot(transform.forward, toMenu.normalized);
        bool want = Facing > facingCos && GazeAngle < (Shown ? hideAngle : showAngle);
        Shown = want;
        group.alpha = Mathf.MoveTowards(group.alpha, want ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        if (Time.time >= nextText) { nextText = Time.time + 0.25f; Refresh(); }
    }

    public void Refresh()
    {
        var sc = ScenarioController.Instance;
        if (stage != null) stage.text = StageText(sc);
        bool taken = card != null && card.Taken;
        float budget = card != null ? card.budget : 0f;
        if (list != null)
            list.text = taken ? card.task.Replace(" — 1 шт.", "") : "<color=#ffb060>Список не взят —\nвозьмите его на стойке у входа</color>";
        var plan = ExperimentContext.Find()?.condition?.planogram;
        var items = basket != null ? basket.Items.ToList() : new System.Collections.Generic.List<ProductItem>();
        float total = items.Sum(i => plan?.PriceOf(i.productId)?.price ?? 0f);
        if (sum != null)
        {
            var sb = new StringBuilder();
            sb.Append(string.Format(ci, "Корзина: {0} шт · <b>{1:0.00}</b>\n", items.Count, total));
            sb.Append(string.Format(ci, "Бюджет: {0:0.00}", budget));
            if (total > budget && budget > 0f) sb.Append(string.Format(ci, "  <color=#ff8a65>+{0:0.00}</color>", total - budget));
            sum.text = sb.ToString();
        }
        if (sumBar != null && budget > 0f)
        {
            var rt = sumBar.rectTransform;
            float w = Mathf.Clamp01(total / budget) * 104f;
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            sumBar.color = total > budget ? UIKit.Warn : UIKit.Ok;
        }
    }

    public static string StageText(ScenarioController sc)
    {
        if (sc == null || !sc.Active) return "NEUROSHELF";
        switch (sc.State)
        {
            case ScenarioState.WaitingForEntry: return "Вход в магазин";
            case ScenarioState.Preparation: return $"Подготовка · заход {sc.Run} из 2";
            case ScenarioState.Ready: return $"Готово к старту · заход {sc.Run} из 2";
            case ScenarioState.RunA:
            case ScenarioState.RunC: return sc.InCountdown ? $"Старт через {Mathf.CeilToInt(sc.CountdownLeft)}" : $"Покупки · заход {sc.Run} из 2";
            case ScenarioState.Paused: return "Пауза";
            case ScenarioState.Checkout: return "Касса";
            case ScenarioState.Debrief: return "Разбор";
            case ScenarioState.Results: return "Итоги";
        }
        return sc.State.ToString();
    }
}
