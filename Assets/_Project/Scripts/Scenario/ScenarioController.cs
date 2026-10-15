using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Сценарий эксперимента как конечный автомат (ScenarioState).
//
// Что делает контроллер:
//  • держит блок условий (ScenarioConditions) и пересчитывает его по событиям источников и сверкой каждый кадр;
//  • переводит состояния: Ready = логическое «и» флагов; нарушение во время захода → Paused;
//  • между заходами меняет условие эксперимента (ExperimentConditionSO A → C) — раскладку делает подписчик;
//  • фиксирует профиль перемещения на всю сессию: A и C должны идти одним способом перемещения;
//  • публикует StateChanged, не зная, кто подписан (табло, среда, аналитика).
//
// Чего контроллер не делает: не пишет аналитику и не вызывает её. Логгеры ЛР5/ЛР6 привязываются к состоянию сами.
[DefaultExecutionOrder(-20)]
public class ScenarioController : MonoBehaviour
{
    [Header("Условия эксперимента (ЛР4)")]
    public ExperimentContext context;
    public ExperimentConditionSO conditionA;
    public ExperimentConditionSO conditionC;

    [Header("Источники условий")]
    public ScenarioZone entryZone;
    public ScenarioZone startZone;
    public ScenarioZone checkoutZone;
    public ScenarioZone debriefZone;
    public BasketCarry basket;
    public ShoppingListCard shoppingList;
    public LocomotionRig rig;

    [Header("Кнопки")]
    public ScenarioButton startButton;
    public ScenarioButton resumeButton;
    public ScenarioButton finishButton;
    public ScenarioButton nextButton;

    [Header("Параметры")]
    [Tooltip("Обратный отсчёт после «Начать», в течение которого нужно стоять на отметке, с")]
    public float countdownSeconds = 3f;

    public static ScenarioController Instance { get; private set; }
    /// (из какого, в какое). Подписчики: табло, реакция среды, слой аналитики.
    public static event Action<ScenarioState, ScenarioState> StateChanged;
    /// Служебные записи сценария (отклонённая смена профиля и т. п.)
    public static event Action<string> Note;
    /// Изменился любой флаг или присутствие в зоне — для перерисовки табло и меток.
    public event Action ConditionsChanged;

    public ScenarioState State { get; private set; } = ScenarioState.WaitingForEntry;
    public ScenarioConditions Conditions { get; } = new ScenarioConditions();
    public int Run { get; private set; } = 1;                 // номер текущего или ближайшего захода
    public readonly RunRecord[] Runs = new RunRecord[3];      // [1] — A, [2] — C
    public RunRecord CurrentRun => Run >= 1 && Run <= 2 ? Runs[Run] : null;
    public float CountdownLeft { get; private set; }
    public bool InCountdown => CountdownLeft > 0f;
    public ScenarioState ResumeTo { get; private set; }
    public bool PausedInCountdown { get; private set; }
    public string PauseReason { get; private set; } = "";
    public string PauseFix { get; private set; } = "";
    public LocomotionProfileSO LockedProfile { get; private set; }
    public int ProfileChangeAttempts { get; private set; }
    public bool AtCheckout => checkoutZone != null && checkoutZone.PlayerInside;
    public bool AtDebrief => debriefZone != null && debriefZone.PlayerInside;
    public bool Active { get; private set; }
    public readonly List<string> Journal = new List<string>();

    float pauseStart;
    string lastSnapshot = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { StateChanged = null; Note = null; Instance = null; }

    void Awake()
    {
        Instance = this;
        if (context == null) context = ExperimentContext.Find();
        if (rig == null) rig = FindFirstObjectByType<LocomotionRig>();
    }

    void OnEnable()
    {
        foreach (var z in new[] { entryZone, startZone, checkoutZone, debriefZone })
            if (z != null) z.PresenceChanged += OnZone;
        if (basket != null) basket.Changed += OnBasket;
        if (shoppingList != null) shoppingList.TakenChanged += OnList;
        if (startButton != null) startButton.Pressed += OnStartPressed;
        if (resumeButton != null) resumeButton.Pressed += OnResumePressed;
        if (finishButton != null) finishButton.Pressed += OnFinishPressed;
        if (nextButton != null) nextButton.Pressed += OnNextPressed;
    }

    void OnDisable()
    {
        foreach (var z in new[] { entryZone, startZone, checkoutZone, debriefZone })
            if (z != null) z.PresenceChanged -= OnZone;
        if (basket != null) basket.Changed -= OnBasket;
        if (shoppingList != null) shoppingList.TakenChanged -= OnList;
        if (startButton != null) startButton.Pressed -= OnStartPressed;
        if (resumeButton != null) resumeButton.Pressed -= OnResumePressed;
        if (finishButton != null) finishButton.Pressed -= OnFinishPressed;
        if (nextButton != null) nextButton.Pressed -= OnNextPressed;
        ReleaseProfileGuard();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (ScenarioRuntime.LegacyDemoActive)
        {
            Active = false;
            Write("сценарий отключён: идёт проверка сцены ЛР4–ЛР6");
            UpdateButtons();
            return;
        }
        Active = true;
        if (context != null && conditionA != null && context.condition != conditionA) context.SetCondition(conditionA);
        Write($"сессия начата · условие {conditionA?.conditionName} · профиль {rig?.profile?.profileName}");
        ChangeState(ScenarioState.WaitingForEntry, force: true);
        RefreshConditions();
    }

    // ===================== источники =====================

    void OnZone(ScenarioZone z, bool inside) => RefreshConditions();
    void OnBasket(BasketCarry b) { Write($"корзина: {b.State} ({b.LastChangeReason})"); RefreshConditions(); }
    void OnList(ShoppingListCard l) { Write("список покупок взят: " + l.Taken); RefreshConditions(); }

    /// Сверка всех флагов с источниками. Вызывается по событиям и каждый кадр: если событие
    /// потерялось (телепорт, отключённый объект), состояние всё равно станет верным.
    public void RefreshConditions()
    {
        if (!Active || !isActiveAndEnabled) return;   // выход из Play Mode: зоны выключаются раньше контроллера
        var c = Conditions;
        c.EnteredStore |= entryZone != null && entryZone.PlayerInside;     // вход фиксируется один раз
        c.BasketCarried = basket != null && basket.IsCarried;
        c.ListTaken = shoppingList != null && shoppingList.Taken;
        c.OnStartMark = startZone != null && startZone.PlayerInside;
        c.ProfileSelected = rig != null && rig.profile != null && (LockedProfile == null || rig.profile == LockedProfile);
        c.BasketEmpty = basket == null || basket.ItemCount == 0;

        var snap = c.Snapshot() + (AtCheckout ? " checkout" : "") + (AtDebrief ? " debrief" : "");
        if (snap == lastSnapshot) return;
        lastSnapshot = snap;
        Write("условия: " + snap);
        Evaluate();
        ConditionsChanged?.Invoke();
    }

    void Evaluate()
    {
        switch (State)
        {
            case ScenarioState.WaitingForEntry:
            case ScenarioState.Preparation:
            case ScenarioState.Ready:
                var target = !Conditions.EnteredStore ? ScenarioState.WaitingForEntry
                           : Conditions.Ready ? ScenarioState.Ready : ScenarioState.Preparation;
                if (target != State) ChangeState(target);
                break;
            case ScenarioState.RunA:
            case ScenarioState.RunC:
                if (!RunIntact(out var why, out var fix)) Pause(why, fix);
                break;
            case ScenarioState.Checkout:
                if (AtDebrief) ChangeState(Run == 1 ? ScenarioState.Debrief : ScenarioState.Results);
                break;
        }
        UpdateButtons();
    }

    /// Условия, которые должны держаться во время захода. На отметке нужно стоять только до конца отсчёта.
    bool RunIntact(out string why, out string fix)
    {
        why = fix = "";
        if (!Conditions.BasketCarried)
        {
            why = "корзина не у вас (выпала или оставлена)";
            fix = "Поднимите корзину и отпустите её у пояса";
            return false;
        }
        if (InCountdown && !Conditions.OnStartMark)
        {
            why = "вы сошли со стартовой отметки до конца отсчёта";
            fix = "Вернитесь на стартовую отметку";
            return false;
        }
        if (!Conditions.ProfileSelected)
        {
            why = "изменён способ перемещения";
            fix = "Способ перемещения восстановлен; подтвердите продолжение";
            return false;
        }
        return true;
    }

    public bool ResumePossible =>
        State == ScenarioState.Paused && Conditions.BasketCarried && Conditions.ProfileSelected && (!PausedInCountdown || Conditions.OnStartMark);

    void UpdateButtons()
    {
        bool run = State == ScenarioState.RunA || State == ScenarioState.RunC;
        if (startButton != null) startButton.SetAvailable(Active && State == ScenarioState.Ready);
        if (resumeButton != null) resumeButton.SetAvailable(Active && ResumePossible);
        if (finishButton != null) finishButton.SetAvailable(Active && run && !InCountdown && AtCheckout);
        if (nextButton != null) nextButton.SetAvailable(Active && State == ScenarioState.Debrief);
    }

    // ===================== кнопки =====================

    void OnStartPressed(ScenarioButton _) => Begin();
    void OnResumePressed(ScenarioButton _) => Resume();
    void OnFinishPressed(ScenarioButton _) => FinishRun();
    void OnNextPressed(ScenarioButton _) => NextRun();

    /// «Начать». Без любого из условий заход не запускается — даже если вызвать метод в обход кнопки.
    public bool Begin()
    {
        RefreshConditions();
        if (!Active || State != ScenarioState.Ready || !Conditions.Ready)
        {
            Write($"«Начать» отклонено: состояние {State}, {Conditions.Snapshot()}");
            return false;
        }
        if (Run == 1)
        {
            LockedProfile = rig.profile;
            LocomotionRig.ChangeGuard = OnProfileChangeRequest;
            Write("профиль перемещения зафиксирован до конца сессии: " + LockedProfile.profileName);
        }
        Runs[Run] = new RunRecord
        {
            index = Run,
            condition = context != null && context.condition != null ? context.condition.conditionName : "?",
            profile = LockedProfile != null ? LockedProfile.profileName : "?",
            tStart = Time.time,
        };
        CountdownLeft = Mathf.Max(0.01f, countdownSeconds);
        ChangeState(Run == 1 ? ScenarioState.RunA : ScenarioState.RunC);
        return true;
    }

    public bool Resume()
    {
        RefreshConditions();
        if (!ResumePossible) { Write("«Продолжить» отклонено: " + Conditions.Snapshot()); return false; }
        var r = CurrentRun;
        if (r != null) r.pausedSeconds += Time.time - pauseStart;
        if (PausedInCountdown) CountdownLeft = countdownSeconds;   // отсчёт заново, с отметки
        PausedInCountdown = false;
        PauseReason = PauseFix = "";
        ChangeState(ResumeTo);
        return true;
    }

    public bool FinishRun()
    {
        if (!(State == ScenarioState.RunA || State == ScenarioState.RunC) || InCountdown || !AtCheckout) return false;
        var r = CurrentRun;
        r.tEnd = Time.time;
        r.finished = true;
        r.receipt = BuildReceipt();
        Write($"касса: заход {Run}, позиций {r.receipt.Count}, сумма {r.Total:0.00}");
        ChangeState(ScenarioState.Checkout);
        if (AtDebrief) Evaluate();
        return true;
    }

    public bool NextRun()
    {
        if (State != ScenarioState.Debrief) return false;
        Run = 2;
        ChangeState(ScenarioState.Preparation);
        lastSnapshot = "";
        RefreshConditions();
        return true;
    }

    void Pause(string why, string fix)
    {
        ResumeTo = State;
        PausedInCountdown = InCountdown;
        CountdownLeft = 0f;
        pauseStart = Time.time;
        PauseReason = why; PauseFix = fix;
        var r = CurrentRun;
        if (r != null) { r.pauses++; r.pauseReasons.Add(why); }
        Write("ПАУЗА: " + why);
        ChangeState(ScenarioState.Paused);
    }

    // ===================== профиль перемещения =====================

    bool OnProfileChangeRequest(LocomotionProfileSO p)
    {
        if (LockedProfile == null || p == LockedProfile) return true;
        ProfileChangeAttempts++;
        var msg = $"PROFILE_CHANGE_REJECTED попытка сменить {LockedProfile.profileName} → {(p != null ? p.profileName : "null")} в состоянии {State}, заход {Run}";
        Debug.LogWarning("[NS-SCN] " + msg);
        Write(msg);
        return false;
    }

    void ReleaseProfileGuard()
    {
        if (LocomotionRig.ChangeGuard == (Func<LocomotionProfileSO, bool>)OnProfileChangeRequest) LocomotionRig.ChangeGuard = null;
    }

    // ===================== кадр =====================

    void Update()
    {
        if (!Active) return;
        // профиль мог быть заменён прямым присваиванием поля в обход SetProfile — откатываем
        if (LockedProfile != null && State != ScenarioState.Results && rig != null && rig.profile != LockedProfile)
        {
            OnProfileChangeRequest(rig.profile);
            rig.SetProfile(LockedProfile);
        }
        if (InCountdown && (State == ScenarioState.RunA || State == ScenarioState.RunC))
        {
            CountdownLeft -= Time.deltaTime;
            if (CountdownLeft <= 0f)
            {
                CountdownLeft = 0f;
                if (CurrentRun != null) CurrentRun.tGo = Time.time;
                Write($"заход {Run}: отсчёт окончен, можно идти");
                UpdateButtons();
                ConditionsChanged?.Invoke();
            }
        }
        RefreshConditions();
    }

    // ===================== чек =====================

    List<ReceiptLine> BuildReceipt()
    {
        var list = new List<ReceiptLine>();
        var cond = context != null ? context.condition : null;
        var plan = cond != null ? cond.planogram : null;
        if (basket == null) return list;
        foreach (var it in basket.Items)
        {
            var p = plan != null ? plan.PriceOf(it.productId) : null;
            var line = new ReceiptLine
            {
                productId = it.productId,
                objectName = it.gameObject.name,
                price = p != null ? p.price : 0f,
                anchorPrice = p != null ? p.anchorPrice : 0f,
                tier = it.tier,
                techniques = Techniques.For(it.gameObject.name, it.productId, cond, plan),
            };
            if (plan != null && plan.Find(it.gameObject.name, out var row, out _)) line.shelf = row.shelf;
            list.Add(line);
        }
        return list;
    }

    // ===================== служебное =====================

    void ChangeState(ScenarioState to, bool force = false)
    {
        if (to == State && !force) return;
        var from = State;
        State = to;
        if (to == ScenarioState.Results) { ReleaseProfileGuard(); Write("сессия завершена, профиль разблокирован"); }
        Write($"STATE {from} → {to} · заход {Run}");
        Debug.Log($"[NS-SCN] STATE {from} → {to} · run={Run} · condition={context?.condition?.conditionName}");
        try { StateChanged?.Invoke(from, to); } catch (Exception e) { Debug.LogException(e); }
        if (to == ScenarioState.Debrief && context != null && conditionC != null)
        {
            // Завершение захода A: смена условия эксперимента — подстановка данных, а не вторая сцена.
            // Планограмму, ценники, толпу NPC и жест промоутера перестроят подписчики ExperimentContext.
            context.SetCondition(conditionC);
            Write("условие эксперимента → " + conditionC.conditionName);
        }
        UpdateButtons();
        ConditionsChanged?.Invoke();
    }

    void Write(string s)
    {
        var line = $"t={Time.time,7:0.00}  {s}";
        Journal.Add(line);
        try { Note?.Invoke(line); } catch (Exception e) { Debug.LogException(e); }
    }
}
