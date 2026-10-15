using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Реакция среды на сценарий: главное табло, метки на полу и на стойке, подсветка кассы, дверь.
// Только слушает ScenarioController (StateChanged, ConditionsChanged) и ничего в нём не меняет.
public class ScenarioView : MonoBehaviour
{
    public ScenarioController controller;

    [Header("Главное табло")]
    public Text title;
    public Text body;
    public Text footer;
    [Tooltip("Малое табло у входа (виден из тамбура) — дублирует заголовок и текст главного")]
    public Text entranceTitle;
    public Text entranceBody;

    [Header("Метки")]
    public Renderer startMark;          // круг на полу — стартовая отметка
    public Renderer basketMark;         // площадка корзины на стойке
    public Renderer listMark;           // площадка списка на стойке
    public Text standChecklist;         // табличка над стойкой
    public Renderer checkoutLight;      // полоса подсветки прилавка

    [Header("Цвета")]
    public Color idle = new Color(0.32f, 0.33f, 0.35f);
    public Color todo = new Color(1.00f, 0.55f, 0.12f);
    public Color done = new Color(0.20f, 0.80f, 0.38f);
    public Color running = new Color(0.20f, 0.55f, 0.95f);
    public Color paused = new Color(0.95f, 0.25f, 0.20f);

    public string CurrentTitle => title != null ? title.text : "";
    public string CurrentBody => body != null ? body.text : "";
    public Color StartMarkColor { get; private set; }
    public Color BasketMarkColor { get; private set; }
    public bool CheckoutLit { get; private set; }

    MaterialPropertyBlock mpb;
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    int lastCountdown = -1;

    void Awake() { mpb = new MaterialPropertyBlock(); if (controller == null) controller = FindFirstObjectByType<ScenarioController>(); }

    void OnEnable()
    {
        ScenarioController.StateChanged += OnState;
        if (controller != null) controller.ConditionsChanged += Redraw;
    }

    void OnDisable()
    {
        ScenarioController.StateChanged -= OnState;
        if (controller != null) controller.ConditionsChanged -= Redraw;
    }

    void Start() => Redraw();

    void OnState(ScenarioState from, ScenarioState to) => Redraw();

    void Update()
    {
        if (controller == null || !controller.InCountdown) { lastCountdown = -1; return; }
        int s = Mathf.CeilToInt(controller.CountdownLeft);
        if (s != lastCountdown) { lastCountdown = s; Redraw(); }
    }

    public void Redraw()
    {
        if (controller == null) return;
        var c = controller.Conditions;
        var st = controller.State;
        string T = "", B = "";
        var sb = new StringBuilder();
        string task = controller.shoppingList != null ? controller.shoppingList.task : "";
        float budget = controller.shoppingList != null ? controller.shoppingList.budget : 0f;

        if (!controller.Active)
        {
            T = "NEUROSHELF";
            B = "Сценарий отключён на время проверки сцены (ЛР4–ЛР6).";
        }
        else switch (st)
        {
            case ScenarioState.WaitingForEntry:
                T = "Добро пожаловать";
                B = "Это магазин. Подойдите к двери и войдите.\nДальше табло подскажет, что делать.";
                break;
            case ScenarioState.Preparation:
                T = controller.Run == 1 ? "Подготовка" : "Подготовка ко второму заходу";
                sb.Append("Чтобы начать, осталось:\n");
                foreach (var m in c.Missing()) sb.Append("<color=#ffb060>•  ").Append(m).Append("</color>\n");
                sb.Append("\n<color=#8d939b>");
                if (c.EnteredStore) sb.Append("готово: вход\n");
                if (c.BasketCarried) sb.Append("готово: корзина\n");
                if (c.ListTaken) sb.Append("готово: список покупок\n");
                if (c.OnStartMark) sb.Append("готово: стоите на отметке\n");
                sb.Append("</color>");
                B = sb.ToString();
                break;
            case ScenarioState.Ready:
                T = "Всё готово. Нажмите «Начать»";
                B = (controller.Run == 1 ? "Заход 1 из 2. " : "Заход 2 из 2. Задание то же. ") +
                    "Купите по списку:\n" + task + string.Format(ci, "\nБюджет: {0:0.00}", budget);
                break;
            case ScenarioState.RunA:
            case ScenarioState.RunC:
                if (controller.InCountdown)
                {
                    T = $"Заход {controller.Run} из 2 — старт через {Mathf.CeilToInt(controller.CountdownLeft)}";
                    B = "Стойте на отметке до конца отсчёта.";
                }
                else
                {
                    T = $"Заход {controller.Run} из 2";
                    B = "Соберите покупки по списку и подойдите к кассе.\nСписок и сумма — на левом запястье.";
                }
                break;
            case ScenarioState.Paused:
                T = "Пауза — результаты сохранены";
                B = "Причина: " + controller.PauseReason + "\nЧто сделать: " + controller.PauseFix +
                    (controller.ResumePossible ? "\n\n<color=#7dff9a>Можно продолжить — нажмите «Продолжить».</color>"
                                               : "\n\nЗатем станет доступна кнопка «Продолжить».");
                break;
            case ScenarioState.Checkout:
                T = $"Касса — заход {controller.Run} завершён";
                var r = controller.CurrentRun;
                if (r != null)
                {
                    foreach (var l in r.receipt) sb.Append(string.Format(ci, "{0,-8}  {1,6:0.00}\n", l.productId, l.price));
                    if (r.receipt.Count == 0) sb.Append("корзина пуста\n");
                    sb.Append(string.Format(ci, "<b>Итого: {0:0.00}</b>   бюджет {1:0.00}\n", r.Total, budget));
                }
                sb.Append(controller.Run == 1 ? "\nПройдите к табло разбора." : "\nПройдите к табло разбора — там итоги.");
                B = sb.ToString();
                break;
            case ScenarioState.Debrief:
                T = "Разбор первого захода";
                B = "Смотрите табло разбора: ваш маршрут, покупки и приёмы,\nкоторые действовали на каждую из них.\nВыкладка в зале уже сменилась на нейтральную.\nКогда будете готовы — «Ко второму заходу».";
                break;
            case ScenarioState.Results:
                T = "Итоги: заход 1 против захода 2";
                B = "Сравнение — на табло разбора.\nСпасибо за участие!";
                break;
        }
        if (title != null) title.text = T;
        if (body != null) body.text = B;
        if (entranceTitle != null) entranceTitle.text = T;
        if (entranceBody != null) entranceBody.text = st == ScenarioState.Preparation || st == ScenarioState.Ready ? "Подробности — на табло справа" : B;
        if (footer != null)
            footer.text = controller.Active
                ? $"Заход {controller.Run} из 2 · способ перемещения: {(controller.LockedProfile != null ? controller.LockedProfile.profileName + " (зафиксирован)" : controller.rig != null && controller.rig.profile != null ? controller.rig.profile.profileName : "—")}"
                : "";

        // метки
        bool run = st == ScenarioState.RunA || st == ScenarioState.RunC;
        StartMarkColor = !controller.Active || st == ScenarioState.WaitingForEntry ? idle
                       : st == ScenarioState.Ready || (run && controller.InCountdown) ? done
                       : st == ScenarioState.Paused ? paused
                       : st == ScenarioState.Preparation ? todo : idle;
        BasketMarkColor = c.BasketCarried ? done : st == ScenarioState.WaitingForEntry ? idle : todo;
        WorldText.Tint(startMark, StartMarkColor, mpb);
        WorldText.Tint(basketMark, BasketMarkColor, mpb);
        WorldText.Tint(listMark, c.ListTaken ? done : st == ScenarioState.WaitingForEntry ? idle : todo, mpb);
        if (standChecklist != null)
        {
            string Item(bool ok, string s) => ok ? $"<color=#5f646b>{s} — готово</color>" : $"<color=#ffffff>{s}</color>";
            standChecklist.text = Item(c.BasketCarried, "1. Возьмите корзину") + "\n" + Item(c.ListTaken, "2. Возьмите список") + "\n" + Item(c.OnStartMark, "3. Встаньте на отметку");
        }
        CheckoutLit = run && !controller.InCountdown && controller.AtCheckout;
        WorldText.Tint(checkoutLight, CheckoutLit ? new Color(1f, 0.8f, 0.25f) : (run ? new Color(0.45f, 0.38f, 0.2f) : idle), mpb);
    }
}
