using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Автотесты сценария в Play Mode. Каждый тест загружает рабочую сцену 02_Store заново.
// Условия выставляются настоящими источниками: телепорт в зону (перестановка XR Origin),
// захват корзины и списка штатным XRInteractionManager.SelectEnter/SelectExit, кнопки — Press()
// (тот же метод, что вызывает выбор лучом). Ничего не подменяется флагами в обход источников.
public class ScenarioPlayModeTests
{
    const string ScenePath = "Assets/_Project/Scenes/02_Store.unity";

    ScenarioController sc;
    XROrigin xr; Transform origin, head, hand;
    NearFarInteractor nf; XRInteractionManager mgr;

    [UnitySetUp]
    public IEnumerator Load()
    {
        ScenarioRuntime.ForceLegacy = false;
#if UNITY_EDITOR
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        yield return SceneManager.LoadSceneAsync("02_Store");
#endif
        float t = 0f;
        while ((ScenarioController.Instance == null || !ScenarioController.Instance.Active) && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        sc = ScenarioController.Instance;
        Assert.IsNotNull(sc, "нет ScenarioController в сцене (меню 9.0)");
        Assert.IsTrue(sc.Active, "сценарий не активен");
        xr = Object.FindFirstObjectByType<XROrigin>();
        origin = xr.Origin.transform;
        head = Camera.main.transform;
        nf = sc.rig.rightNearFar;
        hand = nf.transform.parent;
        mgr = nf.interactionManager;
        foreach (var tr in new[] { head, hand, GameObject.Find("Left Controller")?.transform })
            if (tr != null) foreach (var c in tr.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var sim = GameObject.Find("XR Device Simulator"); if (sim != null) sim.SetActive(false);
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity;
        yield return null;
    }

    // ---------- помощники: настоящие источники условий ----------

    ScenarioZone Zone(string id) => Object.FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).First(z => z.zoneId == id);

    IEnumerator Teleport(Vector3 p)
    {
        origin.position = new Vector3(p.x, origin.position.y, p.z);
        hand.localPosition = new Vector3(0.22f, -0.45f, 0.30f);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    IEnumerator Enter() { yield return Teleport(Zone("Entry").transform.position); }
    IEnumerator ToMark() { yield return Teleport(Zone("StartMark").transform.position); }
    IEnumerator ToNowhere() { yield return Teleport(GameObject.Find("Scenario/Points/Outside").transform.position); }

    IEnumerator TakeBasket()
    {
        var g = (IXRSelectInteractable)sc.basket.Grab;
        mgr.SelectEnter((IXRSelectInteractor)nf, g);
        yield return new WaitForSeconds(0.15f);
        hand.localPosition = new Vector3(0.18f, -0.55f, 0.25f);   // поднёс к поясу
        yield return new WaitForSeconds(0.15f);                     // корзина следует за рукой (Instantaneous)
        mgr.SelectExit((IXRSelectInteractor)nf, g);
        yield return null;
    }

    IEnumerator DropBasket()
    {
        var g = (IXRSelectInteractable)sc.basket.Grab;
        mgr.SelectEnter((IXRSelectInteractor)nf, g);
        yield return new WaitForSeconds(0.15f);
        hand.localPosition = new Vector3(0.95f, -0.40f, 0.30f);   // рука в сторону
        yield return new WaitForSeconds(0.15f);
        mgr.SelectExit((IXRSelectInteractor)nf, g);
        yield return null;
        hand.localPosition = new Vector3(0.22f, -0.45f, 0.30f);
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    IEnumerator TakeList()
    {
        var si = (IXRSelectInteractable)sc.shoppingList.GetComponent<XRSimpleInteractable>();
        mgr.SelectEnter((IXRSelectInteractor)nf, si);
        yield return null;
        if (nf.IsSelecting(si)) mgr.SelectExit((IXRSelectInteractor)nf, si);
        yield return null;
    }

    IEnumerator Buy(string obj)
    {
        var it = GameObject.Find(obj).GetComponent<ProductItem>();
        var g = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        mgr.SelectEnter((IXRSelectInteractor)nf, g);
        yield return new WaitForSeconds(0.2f);
        var slot = sc.basket.Slots.First(s => !s.hasSelection);
        it.transform.SetPositionAndRotation(slot.transform.position + Vector3.up * 0.02f, slot.transform.rotation);
        yield return new WaitForFixedUpdate();
        mgr.SelectExit((IXRSelectInteractor)nf, g);       // отпустил над корзиной
        mgr.SelectEnter((IXRSelectInteractor)slot, g);    // ячейка подхватила
        yield return new WaitForSeconds(0.5f);
        Assert.IsTrue(slot.hasSelection, obj + " не лёг в корзину");
    }

    IEnumerator MakeReady()
    {
        yield return Enter();
        yield return TakeBasket();
        yield return TakeList();
        yield return ToMark();
        Assert.AreEqual(ScenarioState.Ready, sc.State, sc.Conditions.Snapshot());
    }

    IEnumerator StartRunA()
    {
        yield return MakeReady();
        Assert.IsTrue(sc.startButton.Press(), "«Начать» не нажалась");
        yield return new WaitForSeconds(sc.countdownSeconds + 0.2f);
        Assert.AreEqual(ScenarioState.RunA, sc.State);
        Assert.IsFalse(sc.InCountdown);
    }

    bool RaySelectable(ScenarioButton b) => mgr.IsSelectPossible((IXRSelectInteractor)nf, b.Interactable);

    // ---------- 1. без любого из условий «Начать» не запускает заход ----------

    public static readonly string[] Conditions = { "Entered", "Basket", "List", "Mark", "Profile" };

    [UnityTest]
    public IEnumerator Start_IsBlocked_WithoutAnyOneCondition([ValueSource(nameof(Conditions))] string missing)
    {
        if (missing != "Entered") yield return Enter();
        if (missing != "Basket") yield return TakeBasket();
        if (missing != "List") yield return TakeList();
        LocomotionProfileSO saved = sc.rig.profile;
        if (missing == "Profile") sc.rig.profile = null;
        if (missing != "Mark") yield return ToMark(); else yield return Teleport(Zone("Entry").transform.position);
        yield return null;

        TestContext.WriteLine($"без условия {missing}: {sc.Conditions.Snapshot()} → состояние {sc.State}");
        Assert.AreNotEqual(ScenarioState.Ready, sc.State);
        Assert.IsFalse(sc.startButton.Available, "кнопка должна быть недоступна");
        Assert.AreEqual(0, sc.startButton.Interactable.interactionLayers.value, "у недоступной кнопки слой взаимодействия сброшен");
        Assert.IsFalse(RaySelectable(sc.startButton), "луч не должен выбирать недоступную кнопку");
        Assert.IsFalse(sc.startButton.Press(), "нажатие недоступной кнопки ничего не делает");
        Assert.IsFalse(sc.Begin(), "Begin() в обход кнопки тоже отклоняется");
        Assert.IsFalse(sc.State == ScenarioState.RunA);

        // контроль: восстановили недостающее условие — заход запускается
        if (missing == "Entered") { yield return Enter(); yield return ToMark(); }
        if (missing == "Basket") yield return TakeBasket();
        if (missing == "List") yield return TakeList();
        if (missing == "Profile") { sc.rig.profile = saved; yield return null; }
        if (missing == "Mark") yield return ToMark();
        Assert.AreEqual(ScenarioState.Ready, sc.State, sc.Conditions.Snapshot());
        Assert.IsTrue(RaySelectable(sc.startButton), "доступную кнопку луч выбирает");
        Assert.IsTrue(sc.startButton.Press());
        Assert.AreEqual(ScenarioState.RunA, sc.State);
    }

    // ---------- 2. нарушение во время захода → пауза без потери данных ----------

    [UnityTest]
    public IEnumerator Violation_DuringRun_PausesWithoutDataLoss()
    {
        yield return StartRunA();
        yield return Teleport(Zone("Shelf_03").transform.position);
        yield return Buy("Can_VOLTA");
        var run = sc.CurrentRun;
        var rec = ScenarioSessionRecorder.Instance;
        int picks = rec.Runs[1].picks, items = sc.basket.ItemCount;
        float dwell = rec.Runs[1].Dwell("Shelf_03");
        Assert.AreEqual(1, items);

        yield return DropBasket();
        TestContext.WriteLine("пауза: " + sc.PauseReason + " · корзина " + sc.basket.State);
        Assert.AreEqual(ScenarioState.Paused, sc.State);
        Assert.AreEqual(ScenarioState.RunA, sc.ResumeTo);
        Assert.IsFalse(sc.resumeButton.Available, "пока корзина не поднята, продолжить нельзя");

        yield return new WaitForSeconds(1.5f);
        Assert.AreEqual(ScenarioState.Paused, sc.State, "пауза не снимается сама");

        yield return TakeBasket();
        yield return null;
        Assert.IsTrue(sc.resumeButton.Available, "условие восстановлено → «Продолжить» доступна");
        Assert.AreEqual(ScenarioState.Paused, sc.State, "и всё равно ждёт подтверждения");
        Assert.IsTrue(sc.resumeButton.Press());

        Assert.AreEqual(ScenarioState.RunA, sc.State);
        Assert.AreEqual(1, sc.Run);
        Assert.AreSame(run, sc.CurrentRun, "запись захода та же, а не новая");
        Assert.AreEqual(1, run.pauses);
        Assert.AreEqual(items, sc.basket.ItemCount, "товар остался в корзине");
        Assert.AreEqual(picks, rec.Runs[1].picks, "взятия не потеряны");
        Assert.GreaterOrEqual(rec.Runs[1].Dwell("Shelf_03"), dwell, "время у стеллажа не обнулилось");
    }

    [UnityTest]
    public IEnumerator LeavingMark_DuringCountdown_Pauses_ThenCountdownRestarts()
    {
        yield return MakeReady();
        Assert.IsTrue(sc.startButton.Press());
        Assert.IsTrue(sc.InCountdown);
        yield return Teleport(Zone("Entry").transform.position);
        Assert.AreEqual(ScenarioState.Paused, sc.State, "сошёл с отметки до конца отсчёта");
        Assert.IsFalse(sc.resumeButton.Available, "продолжить можно только с отметки");
        yield return ToMark();
        Assert.IsTrue(sc.resumeButton.Press());
        Assert.AreEqual(ScenarioState.RunA, sc.State);
        Assert.IsTrue(sc.InCountdown, "отсчёт начинается заново");
        yield return new WaitForSeconds(sc.countdownSeconds + 0.2f);
        yield return Teleport(Zone("Shelf_04").transform.position);
        Assert.AreEqual(ScenarioState.RunA, sc.State, "после отсчёта уход с отметки — норма, это и есть заход");
    }

    // ---------- 3. смена условия между заходами меняет планограмму ----------

    [UnityTest]
    public IEnumerator ConditionSwitch_BetweenRuns_ChangesPlanogram()
    {
        var volta = GameObject.Find("Can_VOLTA").GetComponent<ProductItem>();
        var applier = Object.FindFirstObjectByType<PlanogramApplier>();
        Assert.AreSame(sc.conditionA, sc.context.condition);
        Vector3 posA = volta.transform.position; int tierA = volta.tier;
        var tagA = applier.TagOf("VOLTA").Find("Strike").gameObject.activeSelf;
        Assert.AreEqual(2, tierA, "в условии A VOLTA на уровне глаз");
        Assert.IsTrue(tagA, "в условии A у VOLTA зачёркнутая якорная цена");

        yield return StartRunA();
        yield return Teleport(Zone("Shelf_03").transform.position);
        yield return Buy("Can_VOLTA");
        yield return Teleport(Zone("Checkout").transform.position);
        Assert.IsTrue(sc.finishButton.Press());
        yield return Teleport(Zone("Debrief").transform.position);
        Assert.AreEqual(ScenarioState.Debrief, sc.State);

        Assert.AreSame(sc.conditionC, sc.context.condition, "условие эксперимента — C");
        Assert.AreEqual(1, volta.tier, "в условии C VOLTA на среднем ярусе");
        Assert.Greater(Vector3.Distance(posA, volta.transform.position), 0.5f, "VOLTA физически переставлена");
        Assert.AreEqual(volta.transform.position.x, volta.homeSlot.transform.position.x, 0.01f, "ячейка полки переехала вместе с товаром");
        Assert.IsFalse(applier.TagOf("VOLTA").Find("Strike").gameObject.activeSelf, "якорной цены нет");
        Assert.AreEqual(0, sc.basket.ItemCount, "корзина пуста перед заходом C");
        // все напитки C на одном ярусе
        var plan = sc.conditionC.planogram;
        var drinks = plan.rows.Where(r => r.products.Any(p => p.StartsWith("Can_") || p.StartsWith("Bottle_"))).ToList();
        Assert.AreEqual(1, drinks.Count, "напитки в условии C — одним рядом");
        foreach (var n in drinks[0].products) Assert.AreEqual(drinks[0].tier, GameObject.Find(n).GetComponent<ProductItem>().tier, n);
        TestContext.WriteLine($"VOLTA: A {posA} ярус {tierA} → C {volta.transform.position} ярус {volta.tier}");
    }

    // ---------- 4. зона фиксирует присутствие после телепорта ----------

    [UnityTest]
    public IEnumerator Zone_DetectsPresence_AfterTeleport()
    {
        yield return ToNowhere();
        foreach (var z in Object.FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).OrderBy(z => z.zoneId))
        {
            int te = z.triggerEnters, re = z.reconciledEnters;
            yield return Teleport(z.transform.position);
            Assert.IsTrue(z.PlayerInside, z.zoneId + ": после телепорта внутрь присутствие не зафиксировано");
            TestContext.WriteLine($"{z.zoneId,-10} вход: OnTriggerEnter {z.triggerEnters - te}, сверка {z.reconciledEnters - re}, источник «{z.lastSource}»");
            int tx = z.triggerExits, rx = z.reconciledExits;
            yield return ToNowhere();
            Assert.IsFalse(z.PlayerInside, z.zoneId + ": после телепорта наружу присутствие не снято");
            TestContext.WriteLine($"{"",-10} выход: OnTriggerExit {z.triggerExits - tx}, сверка {z.reconciledExits - rx}, источник «{z.lastSource}»");
        }
    }

    [UnityTest]
    public IEnumerator Zone_DetectsRoomScaleStep_WithoutLocomotion()
    {
        // Человек сходит с отметки ногами (room-scale): XR Origin и Character Controller не двигаются,
        // двигается только голова. Зона должна это увидеть — тело участника считается под головой.
        yield return ToMark();
        var z = Zone("StartMark");
        Assert.IsTrue(z.PlayerInside);
        var cc = origin.GetComponent<CharacterController>();
        var ccPos = origin.position;
        head.localPosition = new Vector3(1.0f, 0f, 0f);    // шаг в сторону на 1 м
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.AreEqual(ccPos, origin.position, "XR Origin не двигался");
        Assert.IsFalse(z.PlayerInside, "шаг ногами с отметки должен сниматься");
        TestContext.WriteLine($"шаг ногами на 1 м: XR Origin на месте, зона StartMark PlayerInside={z.PlayerInside}, источник «{z.lastSource}»");
        head.localPosition = Vector3.zero;
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert.IsTrue(z.PlayerInside, "вернулся на отметку ногами");
    }

    [UnityTest]
    public IEnumerator Zone_WithoutReconcile_CanMissTeleport_Diagnostic()
    {
        // Контрольный опыт: та же зона только на событиях. Тест не падает — он фиксирует, что получилось.
        var z = Zone("StartMark");
        yield return ToNowhere();
        z.reconcile = false;
        int te = z.triggerEnters;
        origin.position = new Vector3(z.transform.position.x, origin.position.y, z.transform.position.z);
        yield return null;   // кадр сразу после телепорта: решения принимаются в Update
        bool sameFrame = z.PlayerInside;
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
        TestContext.WriteLine($"без сверки: в первом кадре после телепорта PlayerInside={sameFrame}, через 2 шага физики {z.PlayerInside}, OnTriggerEnter пришло {z.triggerEnters - te}");
        z.reconcile = true;
        z.Reconcile();
        Assert.IsTrue(z.PlayerInside, "сверка восстанавливает состояние");
    }

    // ---------- 5. профиль перемещения зафиксирован ----------

    [UnityTest]
    public IEnumerator Profile_LockedAtStart_ChangeIsRejectedAndLogged()
    {
        yield return StartRunA();
        var locked = sc.LockedProfile;
        var other = ScriptableObject.CreateInstance<LocomotionProfileSO>();   // любой другой профиль
        other.profileName = "Test_Other";
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PROFILE_CHANGE_REJECTED"));
        sc.rig.SetProfile(other);
        Assert.AreSame(locked, sc.rig.profile, "SetProfile отклонён");
        Assert.AreEqual(1, sc.ProfileChangeAttempts);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PROFILE_CHANGE_REJECTED"));
        sc.rig.profile = other;          // прямое присваивание в обход метода
        yield return null;
        Assert.AreSame(locked, sc.rig.profile, "прямое присваивание откатано");
        Assert.AreEqual(2, sc.ProfileChangeAttempts);
        Assert.AreEqual(ScenarioState.RunA, sc.State);
    }
}
