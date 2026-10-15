using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Состояния сценария. Трёхфазная схема концепта (наивный заход → разбор → осознанный заход)
// как конечный автомат. Подготовка и «Готово» общие для обоих заходов: перед заходом C участник
// проходит ту же процедуру, что и перед A, — иначе условия старта заходов различались бы.
public enum ScenarioState
{
    WaitingForEntry,   // 1. участник у входа, табло объясняет, что делать
    Preparation,       // 2. внутри, табло перечисляет невыполненные условия
    Ready,             // 3. все условия выполнены, доступна «Начать»
    RunA,              // 4. наивный заход: манипуляции включены, участник не предупреждён
    Checkout,          // 5. касса: чек подведён, корзина зафиксирована
    Debrief,           // 6. разбор собственных данных
    RunC,              // 7. повторный заход с нейтральной планограммой
    Results,           // 8. сравнение A и C — измеренный эффект
    Paused             // 9. условие нарушено во время захода
}

// Блок условий перехода. Каждый флаг выставляет свой источник; Ready — их логическое «и».
[Serializable]
public class ScenarioConditions
{
    public bool EnteredStore;      // зона входа; не сбрасывается до конца сессии
    public bool BasketCarried;     // корзина у участника (на теле или в руке)
    public bool ListTaken;         // список покупок взят со стойки; не сбрасывается
    public bool OnStartMark;       // капсула игрока в зоне стартовой отметки
    public bool ProfileSelected;   // выбран профиль перемещения (а после старта — тот же, что зафиксирован)
    public bool BasketEmpty;       // корзина пуста — заход начинается с нуля

    public bool Ready => EnteredStore && BasketCarried && ListTaken && OnStartMark && ProfileSelected && BasketEmpty;

    public IEnumerable<string> Missing()
    {
        if (!EnteredStore) yield return "Войдите в магазин";
        if (!BasketCarried) yield return "Возьмите корзину со стойки";
        if (!ListTaken) yield return "Возьмите список покупок";
        if (!ProfileSelected) yield return "Не выбран способ перемещения (обратитесь к экспериментатору)";
        if (!BasketEmpty) yield return "Корзина должна быть пустой";
        if (!OnStartMark) yield return "Встаньте на стартовую отметку";
    }

    public string Snapshot() =>
        $"entered={B(EnteredStore)} basket={B(BasketCarried)} list={B(ListTaken)} mark={B(OnStartMark)} profile={B(ProfileSelected)} empty={B(BasketEmpty)} → Ready={B(Ready)}";
    static string B(bool b) => b ? "1" : "0";
}

// Строка чека. Приёмы влияния считаются из данных условия и планограммы, а не вписаны руками.
[Serializable]
public class ReceiptLine
{
    public string productId;
    public string objectName;
    public float price;
    public float anchorPrice;
    public string shelf;
    public int tier;
    public List<string> techniques = new List<string>();
}

[Serializable]
public class RunRecord
{
    public int index;                 // 1 — A, 2 — C
    public string condition;
    public string profile;
    public float tStart, tGo, tEnd;   // старт (кнопка), конец отсчёта, касса
    public float pausedSeconds;
    public int pauses;
    public List<string> pauseReasons = new List<string>();
    public List<ReceiptLine> receipt = new List<ReceiptLine>();
    public bool finished;

    public float Total => receipt.Sum(r => r.price);
    public float Duration => (finished ? tEnd : Time.time) - tStart - pausedSeconds;
    public int Manipulated => receipt.Count(r => r.techniques.Count > 0);
}

public static class Techniques
{
    // Какие приёмы влияния действовали на товар в данном условии.
    // Это не «что сработало» (причинность по одной покупке не установить), а «что было включено».
    public static List<string> For(string objectName, string productId, ExperimentConditionSO cond, PlanogramSO plan)
    {
        var list = new List<string>();
        if (cond == null || plan == null || !plan.Find(objectName, out var row, out _)) return list;
        bool promoted = cond.promotedProduct == objectName;
        if (row.tier == 2) list.Add("уровень глаз 1.60 м");
        if (row.shelf == "Shelf_03") list.Add("тёплый световой акцент 3000 K");
        var p = plan.PriceOf(productId);
        if (p != null && p.anchorPrice > 0f) list.Add($"якорная цена {p.anchorPrice:0.00} → {p.price:0.00}");
        if (cond.socialProofCrowd && row.shelf == cond.promotedShelf) list.Add("социальное доказательство (толпа NPC)");
        if (cond.promoterPointing && promoted) list.Add("жест промоутера (IK)");
        if (cond.adStandMotion && promoted) list.Add("движущийся рекламный стенд");
        return list;
    }
}

// Сцены ЛР4–ЛР6 запускают свои проверки в той же сцене. Пока идёт их проверка, сценарий не вмешивается:
// корзина сразу на теле, условие эксперимента и профиль не трогаются.
public static class ScenarioRuntime
{
    static readonly string[] LegacyKeys = { "ns_interaction_mode", "ns_locomotion_mode", "ns_animation_mode", "ns_anim_mode" };
    public static bool ForceLegacy;   // для тестов

    public static bool LegacyDemoActive
    {
        get
        {
            if (ForceLegacy) return true;
#if UNITY_EDITOR
            foreach (var k in LegacyKeys)
                if (!string.IsNullOrEmpty(UnityEditor.SessionState.GetString(k, ""))) return true;
#endif
            return false;
        }
    }
}
