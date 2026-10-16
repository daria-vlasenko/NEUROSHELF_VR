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
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.EventSystems;

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

    // =====================================================================================
    // ЛР8 — звук, интерфейсы, вибрация. Категория LR8. Звук и вибрация проверяются по данным
    // (параметрам источников, значению окклюзии Steam Audio, журналу импульсов), а не на слух.
    // =====================================================================================

    HandRoleArbiter Arb => Object.FindFirstObjectByType<HandRoleArbiter>();
    StoreAudioDirector Dir => StoreAudioDirector.Instance;
    XRCanvasButton CanvasOf(ScenarioButton b) => Object.FindObjectsByType<ScenarioCanvasButton>(FindObjectsSortMode.None).First(x => x.logic == b).view;

    IEnumerator AimAt(Transform h, InteractorHandedness hand, XRCanvasButton b, int frames = 40)
    {
        var target = UIFeedback.Center(b.transform);
        for (int i = 0; i < frames && !b.HoveredBy(hand); i++)
        {
            h.rotation = Quaternion.LookRotation(target - h.position, Vector3.up);
            yield return null;
        }
        yield return null; yield return null;
    }

    IEnumerator AimAway(Transform h)
    {
        h.localRotation = Quaternion.Euler(70f, 0f, 0f);
        for (int i = 0; i < 5; i++) yield return null;
    }

    IEnumerator RayPress(NearFarInteractor i, Transform h, XRCanvasButton b)
    {
        i.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        yield return AimAt(h, i.handedness, b);
        i.uiPressInput.QueueManualState(true, 1f, true, false);
        yield return null; yield return null;
        i.uiPressInput.QueueManualState(false, 0f, false, true);
        yield return null; yield return null;
        i.uiPressInput.QueueManualState(false, 0f, false, false);
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_Listener_IsSingle_OnXRCamera_AtEyeHeight()
    {
        var ls = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        TestContext.WriteLine($"Audio Listener: {ls.Length} шт. на {string.Join(", ", ls.Select(l => l.name))}; камера {xr.Camera.name} y={head.position.y:0.000}, Camera Y Offset {xr.CameraYOffset:0.00}");
        Assert.AreEqual(1, ls.Length, "слушатель должен быть один");
        Assert.AreSame(xr.Camera.gameObject, ls[0].gameObject, "слушатель — на камере XR Origin");
        Assert.AreEqual(1.65f, xr.CameraYOffset, 0.001f, "Camera Y Offset не сброшен");
        Assert.AreEqual("Steam Audio Spatializer", AudioSettings.GetSpatializerPluginName());
        yield return null;
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_Music_IsConditionFactor_OnInA_OffInC()
    {
        yield return new WaitForSeconds(0.5f);
        Assert.IsTrue(sc.conditionA.backgroundMusic && sc.conditionA.musicClip != null, "в условии A музыка — поле условия");
        Assert.IsFalse(sc.conditionC.backgroundMusic, "в условии C музыки нет");
        Assert.IsTrue(Dir.music.isPlaying && Dir.MusicOn, "A: музыка играет");
        Assert.AreEqual(sc.conditionA.audioAnchorShelf, Dir.AnchorShelf);
        sc.context.SetCondition(sc.conditionC);
        yield return new WaitForSeconds(Dir.fadeSeconds + 0.3f);
        TestContext.WriteLine($"C: музыка играет={Dir.music.isPlaying}, якорь «{Dir.AnchorShelf}»");
        Assert.IsFalse(Dir.music.isPlaying, "C: музыка остановлена после затухания");
        Assert.AreEqual("", Dir.AnchorShelf, "C: якоря нет");
        sc.context.SetCondition(sc.conditionA);
        yield return new WaitForSeconds(0.3f);
        Assert.IsTrue(Dir.music.isPlaying, "снова A — музыка вернулась");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_Sources_MatchSpecTable()
    {
        yield return null;
        var all = Object.FindObjectsByType<StoreAudioSource>(FindObjectsSortMode.None);
        foreach (var s in all)
        {
            var a = s.Source;
            TestContext.WriteLine($"{s.sourceId,-22} blend={a.spatialBlend:0.00} {s.CurveName,-15} {a.minDistance:0.0}–{a.maxDistance:0.0} м группа={a.outputAudioMixerGroup?.name} HRTF={s.Binaural} окклюзия={s.OcclusionEnabled} reverbMix={a.reverbZoneMix:0.00} приоритет={a.priority}");
            Assert.IsNotNull(a.outputAudioMixerGroup, s.sourceId + ": нет группы микшера");
            Assert.AreEqual(s.group.ToString(), a.outputAudioMixerGroup.name, s.sourceId);
            switch (s.role)
            {
                case StoreAudioSource.Role.Music: Assert.Less(a.spatialBlend, 0.05f); break;
                case StoreAudioSource.Role.Anchor:
                    Assert.AreEqual(1f, a.spatialBlend); Assert.AreEqual(AudioRolloffMode.Logarithmic, a.rolloffMode);
                    Assert.IsTrue(s.Binaural && s.OcclusionEnabled, "якорь — HRTF и окклюзия");
                    Assert.LessOrEqual(a.reverbZoneMix, 0.35f, "у якоря реверберация снижена"); break;
                case StoreAudioSource.Role.Cooler:
                    Assert.That(a.spatialBlend, Is.InRange(0.6f, 0.85f)); Assert.AreEqual(AudioRolloffMode.Custom, a.rolloffMode);
                    Assert.LessOrEqual(a.maxDistance, 5f, "гул не расползается по залу"); break;
                case StoreAudioSource.Role.Ventilation:
                    Assert.That(a.spatialBlend, Is.InRange(0.6f, 0.85f)); Assert.AreEqual(AudioRolloffMode.Linear, a.rolloffMode); break;
                case StoreAudioSource.Role.Checkout:
                case StoreAudioSource.Role.Door:
                case StoreAudioSource.Role.Interface:
                    Assert.AreEqual(1f, a.spatialBlend); Assert.IsTrue(s.Binaural, s.sourceId + ": HRTF"); break;
                case StoreAudioSource.Role.NpcSteps:
                case StoreAudioSource.Role.NpcMurmur:
                    Assert.LessOrEqual(a.maxDistance, 6f); Assert.GreaterOrEqual(a.priority, 200); Assert.IsFalse(a.spatialize, "NPC — без HRTF (экономия процессора)"); break;
            }
        }
        Assert.Greater(all.Length, 10);
        Assert.Less(Dir.anchor.reverbZoneMix, Object.FindObjectsByType<StoreAudioSource>(FindObjectsSortMode.None).First(x => x.role == StoreAudioSource.Role.Checkout).Source.reverbZoneMix);
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_Mixer_ExposedParams_ChangeFromScenarioAndPanel_LockedDuringRun()
    {
        var m = StoreAudioMixer.Instance;
        foreach (StoreAudioMixer.Group g in System.Enum.GetValues(typeof(StoreAudioMixer.Group)))
            Assert.IsFalse(float.IsNaN(m.GetVolumeDb(g)), "открытый параметр " + StoreAudioMixer.Param(g));
        Assert.IsTrue(m.SetVolumeDb(StoreAudioMixer.Group.Stimulus, -6f, "сценарий (тест)"));
        Assert.AreEqual(-6f, m.GetVolumeDb(StoreAudioMixer.Group.Stimulus), 0.01f);
        var panel = Object.FindFirstObjectByType<AudioSettingsPanel>();
        var row = panel.rows.First(r => r.group == StoreAudioMixer.Group.Stimulus);
        yield return null;
        Assert.IsFalse(panel.Locked);
        Assert.IsTrue(row.plus.Activate(InteractorHandedness.Left), "до захода панель работает");
        Assert.AreEqual(-3f, m.GetVolumeDb(StoreAudioMixer.Group.Stimulus), 0.01f);
        yield return StartRunA();
        yield return null;
        Assert.IsTrue(panel.Locked, "во время захода панель заблокирована");
        int j0 = HapticFeedback.Journal.Count;
        Assert.IsFalse(row.plus.Activate(InteractorHandedness.Left), "нажатие — отказ");
        Assert.AreEqual(-3f, m.GetVolumeDb(StoreAudioMixer.Group.Stimulus), 0.01f, "громкость стимула не изменилась");
        yield return new WaitForSeconds(0.3f);
        var deny = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "UIDeny").ToList();
        Assert.AreEqual(2, deny.Count, "отказ — двойной импульс");
        Assert.IsTrue(deny.All(r => r.hand == "Left"));
        m.SetVolumeDb(StoreAudioMixer.Group.Stimulus, 0f, "тест: вернуть");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_Anchor_OccludedBehindShelf()
    {
        var a = Dir.anchor.GetComponent<StoreAudioSource>();
        yield return Teleport(new Vector3(-1.2f, 0f, 0.1f));
        yield return new WaitForSeconds(0.6f);
        float front = a.Occlusion;
        yield return Teleport(new Vector3(-1.2f, 0f, 2.6f));
        yield return new WaitForSeconds(0.6f);
        float behind = a.Occlusion;
        TestContext.WriteLine($"окклюзия якоря Shelf_03: в проходе {front:0.00}, за стеллажом Shelf_01 {behind:0.00} ({20f * Mathf.Log10(Mathf.Max(behind, 1e-3f)):0.0} дБ прямого звука)");
        Assert.Greater(front, 0.9f, "в прямой видимости не перекрыт");
        Assert.Less(behind, 0.5f, "за стеллажом перекрыт");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_ReverbZones_CoverRoute_WithBlendedTransitions()
    {
        yield return null;
        var zones = Object.FindObjectsByType<AudioReverbZone>(FindObjectsSortMode.None);
        Assert.AreEqual(3, zones.Length);
        foreach (var z in zones) Assert.Greater(z.maxDistance, z.minDistance, z.name + ": есть полоса перехода");
        var route = new[] { Zone("Entry").transform.position, Zone("StartMark").transform.position, Zone("Shelf_03").transform.position, Zone("Shelf_04").transform.position, Zone("Checkout").transform.position, new Vector3(0f, 1.5f, 0f) };
        foreach (var p in route)
        {
            var cover = zones.Where(z => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(z.transform.position.x, z.transform.position.z)) < z.maxDistance).Select(z => z.name).ToList();
            TestContext.WriteLine($"({p.x:0.0}; {p.z:0.0}) → {string.Join(", ", cover)}");
            Assert.IsNotEmpty(cover, "точка маршрута без реверберации");
        }
        var hall = zones.First(z => z.name.Contains("Hall")); var chk = zones.First(z => z.name.Contains("Checkout"));
        float d = Vector2.Distance(new Vector2(hall.transform.position.x, hall.transform.position.z), new Vector2(chk.transform.position.x, chk.transform.position.z));
        Assert.Less(d, hall.maxDistance + chk.maxDistance, "зоны зала и кассы перекрываются — переход плавный");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator UI_PriceTags_AreCanvases_FromPlanogram_AnchorOnlyInA()
    {
        yield return null;
        var applier = Object.FindFirstObjectByType<PlanogramApplier>();
        var plan = sc.conditionA.planogram;
        int n = 0;
        foreach (var t in applier.AllTags)
        {
            var c = t.GetComponent<Canvas>();
            Assert.AreEqual(RenderMode.WorldSpace, c.renderMode);
            Assert.AreEqual(0.001f, t.transform.lossyScale.x, 1e-5f, "1 px = 1 мм");
            Assert.IsNull(t.GetComponent<TrackedDeviceGraphicRaycaster>(), "ценник пассивный");
            var id = t.nameText.text;
            Assert.AreEqual(plan.PriceOf(id).price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), t.PriceShown, id);
            Assert.AreEqual(plan.PriceOf(id).anchorPrice > 0f, t.HasAnchor, id);
            n++;
        }
        Assert.AreEqual(12, n);
        Assert.IsTrue(applier.TagUI("VOLTA").HasAnchor, "A: у VOLTA якорная цена");
        sc.context.SetCondition(sc.conditionC);
        yield return null;
        Assert.IsFalse(applier.AllTags.Any(t => t.HasAnchor), "C: ни одной якорной цены");
        TestContext.WriteLine($"ценников {n}; в A якорь у VOLTA «{sc.conditionA.planogram.PriceOf("VOLTA").anchorPrice:0.00}», в C — нет ни у одного");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator UI_InteractiveCanvases_HaveTrackedDeviceRaycaster_AndXRUIModule()
    {
        yield return null;
        var es = Object.FindFirstObjectByType<EventSystem>();
        Assert.IsNotNull(es); Assert.IsNotNull(es.GetComponent<XRUIInputModule>(), "XR UI Input Module");
        var buttons = Object.FindObjectsByType<XRCanvasButton>(FindObjectsSortMode.None);
        Assert.GreaterOrEqual(buttons.Length, 12);
        foreach (var b in buttons)
        {
            var c = b.GetComponentInParent<Canvas>();
            Assert.AreEqual(RenderMode.WorldSpace, c.renderMode, b.id);
            Assert.IsNotNull(c.GetComponent<TrackedDeviceGraphicRaycaster>(), b.id + ": нет Tracked Device Graphic Raycaster");
            Assert.IsNotNull(b.GetComponentInParent<UIFeedback>(), b.id + ": нет звука холста");
        }
    }

    [UnityTest, Category("LR8")]
    public IEnumerator UI_RayHover_RealPipeline_HandRoles_WhileHoldingProduct()
    {
        yield return MakeReady();
        var arb = Arb; var left = arb.uiHand; var lh = GameObject.Find("Left Controller").transform;
        foreach (var c in lh.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        lh.localPosition = new Vector3(-0.22f, -0.45f, 0.30f); lh.localRotation = Quaternion.Euler(70f, 0f, 0f);
        hand.localRotation = Quaternion.Euler(70f, 0f, 0f);
        var b = CanvasOf(sc.startButton);
        yield return AimAt(hand, InteractorHandedness.Right, b);
        Assert.IsTrue(b.HoveredBy(InteractorHandedness.Right), "луч правой руки навёлся через XR UI Input Module");
        yield return AimAway(hand);
        Assert.IsFalse(b.HoveredBy(InteractorHandedness.Right));

        // правая берёт товар → её UI-луч выключен, интерфейс у левой
        var it = GameObject.Find("Can_ZEST").GetComponent<ProductItem>();
        mgr.SelectEnter((IXRSelectInteractor)nf, (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>());
        yield return null; yield return null;
        Assert.IsFalse(arb.ProductHandUI, "правая держит товар — UI-луч выключен");
        yield return AimAt(hand, InteractorHandedness.Right, b, 20);
        Assert.IsFalse(b.HoveredBy(InteractorHandedness.Right), "правая с товаром по интерфейсу не работает");
        yield return AimAt(lh, InteractorHandedness.Left, b);
        Assert.IsTrue(b.HoveredBy(InteractorHandedness.Left), "левая наводится на кнопку");
        TestContext.WriteLine("роли рук: " + string.Join(" | ", arb.Journal));
        mgr.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>());
        yield return null; yield return null;
        Assert.IsTrue(arb.ProductHandUI, "отпустила — UI-луч правой снова включён");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator UI_RayPress_StartsRun_AndUnavailableGivesDoubleDeny()
    {
        yield return MakeReady();
        int j0 = HapticFeedback.Journal.Count;
        var start = CanvasOf(sc.startButton);
        var fb = start.GetComponentInParent<UIFeedback>();
        int p0 = fb.Played;
        yield return RayPress(nf, hand, start);
        Assert.AreEqual(ScenarioState.RunA, sc.State, "нажатие лучом по холсту запустило заход");
        Assert.Greater(fb.Played, p0, "щелчок прозвучал");
        Assert.Less(Vector3.Distance(fb.LastPosition, UIFeedback.Center(start.transform)), 0.01f, "звук идёт от кнопки на холсте");
        Assert.IsTrue(fb.Source.spatialize && fb.Source.spatialBlend > 0.99f, "звук интерфейса — через спатиалайзер");
        var resume = CanvasOf(sc.resumeButton);
        int d0 = resume.Denials;
        yield return AimAway(hand);
        yield return RayPress(nf, hand, resume);
        yield return new WaitForSeconds(0.3f);
        Assert.AreEqual(d0 + 1, resume.Denials, "недоступная кнопка — отказ");
        var rec = HapticFeedback.Journal.Skip(j0).ToList();
        TestContext.WriteLine(string.Join("\n", rec.Select(r => r.ToString())));
        Assert.IsTrue(rec.Any(r => r.pattern == "UIHover" && r.hand == "Right"));
        Assert.IsTrue(rec.Any(r => r.pattern == "UIPress" && r.hand == "Right"));
        Assert.AreEqual(2, rec.Count(r => r.pattern == "UIDeny" && r.hand == "Right"), "двойной отказной импульс");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Haptics_Grab_AmplitudeByMass_BasketClick_OnActingHand()
    {
        yield return StartRunA();
        yield return Teleport(Zone("Shelf_03").transform.position);
        int j0 = HapticFeedback.Journal.Count;
        yield return Buy("Can_VOLTA");
        yield return Buy("Bottle_PURA");
        var grabs = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "Grab").ToList();
        var places = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "BasketPlace").ToList();
        TestContext.WriteLine(string.Join("\n", HapticFeedback.Journal.Skip(j0).Select(r => r.ToString())));
        float mV = GameObject.Find("Can_VOLTA").GetComponent<Rigidbody>().mass, mP = GameObject.Find("Bottle_PURA").GetComponent<Rigidbody>().mass;
        Assert.AreEqual(2, grabs.Count);
        Assert.Less(mV, mP);
        Assert.Less(grabs[0].amplitude, grabs[1].amplitude, "тяжелее — сильнее");
        Assert.IsTrue(grabs.All(r => r.hand == "Right"), "вибрирует рука, которой взяли");
        Assert.AreEqual(2, places.Count, "щелчок на каждый товар в корзине");
        Assert.IsTrue(places.All(r => r.hand == "Right" && r.duration < grabs[0].duration));
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Haptics_TierEffort_OffByDefault_OnlyWithConditionFlag()
    {
        yield return StartRunA();
        yield return Teleport(Zone("Shelf_03").transform.position);
        int j0 = HapticFeedback.Journal.Count;
        yield return Buy("Bottle_AURA");      // нижний ярус 0.60 м в условии A
        var g = HapticFeedback.Journal.Skip(j0).First(r => r.pattern == "Grab" || r.pattern == "TierEffort");
        Assert.AreEqual("Grab", g.pattern, "по умолчанию метки усилия нет");
        var cond = Object.Instantiate(sc.conditionA);
        cond.tierEffortHaptic = true;
        HapticFeedback.Journal.Clear();
        var it = GameObject.Find("Bottle_AURA").GetComponent<ProductItem>();
        var gi = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        var ctx = sc.context; var saved = ctx.condition;
        ctx.condition = cond;    // только флаг, без перестановки планограммы
        mgr.SelectEnter((IXRSelectInteractor)nf, gi);
        yield return new WaitForSeconds(0.2f);
        ctx.condition = saved;
        var e = HapticFeedback.Journal.First();
        TestContext.WriteLine($"с флагом условия: {e}");
        Assert.AreEqual("TierEffort", e.pattern);
        Assert.Greater(e.duration, g.duration, "длиннее обычного взятия");
        mgr.SelectExit((IXRSelectInteractor)nf, gi);
        yield return null;
    }

    [UnityTest, Category("LR8")]
    public IEnumerator UI_WristMenu_ShowsOnlyWhenLookingAtWrist()
    {
        yield return Enter();
        yield return TakeList();
        var w = Object.FindFirstObjectByType<WristMenu>();
        var lh = GameObject.Find("Left Controller").transform;
        foreach (var c in lh.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        lh.localPosition = new Vector3(-0.05f, -0.30f, 0.33f); lh.localRotation = Quaternion.Euler(-20f, 0f, 0f);
        head.localRotation = Quaternion.identity;
        yield return new WaitForSeconds(0.4f);
        float a0 = w.Alpha, g0 = w.GazeAngle;
        head.localRotation = Quaternion.LookRotation(head.parent.InverseTransformDirection(w.transform.position - head.position), Vector3.up);
        yield return new WaitForSeconds(0.4f);
        TestContext.WriteLine($"взгляд вперёд: угол {g0:0}°, α={a0:0.00}; на запястье: угол {w.GazeAngle:0}°, лицом {w.Facing:0.00}, α={w.Alpha:0.00}");
        Assert.Less(a0, 0.05f, "вперёд — меню скрыто");
        Assert.Greater(w.Alpha, 0.95f, "на запястье — меню видно");
        StringAssert.Contains("Бюджет", w.sum.text);
        Assert.IsFalse(sc.shoppingList.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "бумажный лист спрятан — список на часах");
        head.localRotation = Quaternion.identity;
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Metric_AnchorFirstApproach_RecordedPerShelf_TwoSensors()
    {
        yield return StartRunA();
        yield return new WaitForSeconds(0.3f);
        yield return Teleport(Zone("Shelf_04").transform.position);
        yield return new WaitForSeconds(0.6f);
        yield return Teleport(Zone("Shelf_03").transform.position);
        yield return new WaitForSeconds(0.6f);
        yield return Teleport(Zone("Checkout").transform.position);
        Assert.IsTrue(sc.finishButton.Press());
        yield return null;
        var rows = SoundAnchorMetric.Instance.Rows.Where(r => r.run == 1).ToList();
        foreach (var r in rows) TestContext.WriteLine(SoundAnchorMetric.ToCsv(r));
        Assert.AreEqual(4, rows.Count);
        var s3 = rows.First(r => r.shelf == "Shelf_03"); var s4 = rows.First(r => r.shelf == "Shelf_04");
        Assert.IsTrue(s3.hasAnchor && !s4.hasAnchor, "якорь отмечен у Shelf_03");
        Assert.AreEqual(1, s4.rank); Assert.AreEqual(2, s3.rank);
        Assert.Less(s4.tEnterZone, s3.tEnterZone);
        Assert.GreaterOrEqual(s3.tEnterRoute, 0f, "второй датчик (RouteLogger) тоже зафиксировал вход");
        Assert.IsTrue(rows.First(r => r.shelf == "Shelf_01").censored, "непосещённый стеллаж — цензурированное наблюдение");
    }

    [UnityTest, Category("LR8")]
    public IEnumerator Audio_VoiceBudget_WithinRealVoiceLimit()
    {
        yield return new WaitForSeconds(2f);
        var all = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        int playing = all.Count(a => a.isPlaying);
        int limit = AudioSettings.GetConfiguration().numRealVoices;
        TestContext.WriteLine($"звучит {playing} из {all.Length} источников, лимит реальных голосов {limit}");
        Assert.LessOrEqual(playing, limit);
    }
}
