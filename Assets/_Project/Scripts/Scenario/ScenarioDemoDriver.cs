using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Прогон сценария целиком без шлема: «от входа до результатов».
// Позы головы и руки задаёт скрипт; выбор (захват корзины, списка, товара, нажатие кнопки) подаётся
// штатным XRInteractionManager.SelectEnter/SelectExit — дальше работают те же события XRI, что и от курка.
// Перемещение — перестановка XR Origin, как это делает телепорт (зоны ловят его сверкой).
// Режимы: check — протокол «действие → реакция» + кадры; record — то же медленно, для Unity Recorder.
// Используется только из меню NEUROSHELF → Сценарий, в сцене не хранится.
public class ScenarioDemoDriver : MonoBehaviour
{
    public static string OutDir;
    public static bool Finished;
    public static readonly StringBuilder Report = new StringBuilder();
    public string mode = "check";
    public int shotWidth = 1600, shotHeight = 900;

    ScenarioController sc; ScenarioView view; DebriefBoard debrief; PlanogramApplier applier; ScenarioSessionRecorder rec;
    XROrigin xr; Transform origin, head, hand; NearFarInteractor nf; XRInteractionManager mgr; LocomotionRig rig;
    int pass, fail;
    readonly CultureInfo ci = CultureInfo.InvariantCulture;
    bool slow => mode == "record";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Finished = false; Report.Clear(); }

    IEnumerator Start()
    {
        Report.Clear(); Finished = false;
        yield return new WaitForSeconds(0.8f);
        sc = ScenarioController.Instance; view = FindFirstObjectByType<ScenarioView>(); debrief = FindFirstObjectByType<DebriefBoard>();
        applier = FindFirstObjectByType<PlanogramApplier>(); rec = ScenarioSessionRecorder.Instance;
        xr = FindFirstObjectByType<XROrigin>(); rig = FindFirstObjectByType<LocomotionRig>();
        head = Camera.main != null ? Camera.main.transform : null;
        nf = rig != null ? rig.rightNearFar : null;
        if (sc == null || xr == null || head == null || nf == null)
        { Line("ОШИБКА: нет ScenarioController / XR Origin / камеры / правого Near-Far. Сначала меню 9.0."); Finished = true; yield break; }
        origin = xr.Origin.transform; hand = nf.transform.parent; mgr = nf.interactionManager;
        Bind();
        yield return mode == "record" ? Record() : Check();
        Finished = true;
    }

    // ===================== привязка =====================

    void Bind()
    {
        foreach (var t in new[] { head, hand, GameObject.Find("Left Controller")?.transform })
            if (t != null) foreach (var c in t.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var sim = GameObject.Find("XR Device Simulator"); if (sim != null) sim.SetActive(false);
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity;
        HandRest();
    }

    void HandRest() { hand.localPosition = new Vector3(0.22f, -0.45f, 0.30f); hand.localRotation = Quaternion.identity; }

    Transform Z(string id) => FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.zoneId == id)?.transform;
    Transform P(string path) => GameObject.Find(path)?.transform;

    // перестановка как телепорт: XR Origin в точку, взгляд по yaw
    void Place(Vector3 p, float yaw, float pitch = 8f)
    {
        origin.SetPositionAndRotation(new Vector3(p.x, origin.position.y, p.z), Quaternion.Euler(0f, yaw, 0f));
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        HandRest();
        var ba = sc.basket != null ? sc.basket.bodyAnchor : null;
        if (ba != null && ba.enabled) ba.SnapNow();
    }

    IEnumerator Walk(Vector3 to, float yaw, float speed = 1.4f, float pitch = 8f)
    {
        var from = origin.position; to.y = from.y;
        float d = Vector3.Distance(from, to), t = 0f, dur = Mathf.Max(0.2f, d / speed);
        float y0 = origin.eulerAngles.y;
        while (t < dur)
        {
            t += Time.deltaTime; float k = Mathf.SmoothStep(0f, 1f, t / dur);
            origin.SetPositionAndRotation(Vector3.Lerp(from, to, k), Quaternion.Euler(0f, Mathf.LerpAngle(y0, yaw, k), 0f));
            head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            yield return null;
        }
    }

    float StartYaw => P("Scenario/Points/StartLook") != null ? P("Scenario/Points/StartLook").eulerAngles.y : 90f;

    float YawTo(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

    IEnumerator Wait(float fast, float slowS) { yield return new WaitForSeconds(slow ? slowS : fast); }

    IEnumerator Select(IXRSelectInteractable target, float hold = 0.25f)
    {
        mgr.SelectEnter((IXRSelectInteractor)nf, target);
        yield return new WaitForSeconds(hold);
        if (nf.IsSelecting(target)) mgr.SelectExit((IXRSelectInteractor)nf, target);
        yield return new WaitForFixedUpdate();
    }

    IEnumerator Press(ScenarioButton b)
    {
        bool possible = mgr.IsSelectPossible((IXRSelectInteractor)nf, b.Interactable) || b.Available;
        if (!b.Available) { Line($"   кнопка «{b.label}» недоступна, луч её не выбирает (IsSelectPossible={mgr.IsSelectPossible((IXRSelectInteractor)nf, b.Interactable)})"); yield break; }
        int n = b.PressCount;
        yield return Select(b.Interactable, 0.15f);
        if (b.PressCount == n) b.Press();   // на случай, если выбор пришёл без события (не должно)
        if (!possible) Line("   (!) кнопка доступна, но IsSelectPossible=false");
    }

    IEnumerator TakeBasket()
    {
        var g = sc.basket.Grab;
        mgr.SelectEnter((IXRSelectInteractor)nf, (IXRSelectInteractable)g);
        yield return Wait(0.3f, 0.8f);
        hand.localPosition = new Vector3(0.18f, -0.55f, 0.25f);   // поднёс к поясу
        yield return Wait(0.3f, 0.6f);
        mgr.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)g);
        yield return new WaitForFixedUpdate();
        HandRest();
    }

    IEnumerator DropBasket()
    {
        var g = sc.basket.Grab;
        mgr.SelectEnter((IXRSelectInteractor)nf, (IXRSelectInteractable)g);
        yield return Wait(0.2f, 0.5f);
        hand.localPosition = new Vector3(0.95f, -0.40f, 0.30f);   // рука в сторону — корзина далеко от тела
        yield return Wait(0.2f, 0.4f);
        mgr.SelectExit((IXRSelectInteractor)nf, (IXRSelectInteractable)g);
        yield return new WaitForFixedUpdate();
        HandRest();
    }

    ProductItem Item(string name) => GameObject.Find(name)?.GetComponent<ProductItem>();

    // Рука переносит товар в ячейку: товар над ячейкой → отпустить (Select Exited рукой) → ячейка подхватывает
    // (Select Entered сокетом). Так же, как при настоящем отпускании над корзиной, ProductItem получает оба события
    // и пишет исход Purchased / Returned.
    IEnumerator PutInto(ProductItem it, ProductSocket slot)
    {
        var g = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        it.transform.SetPositionAndRotation(slot.transform.position + Vector3.up * 0.02f, slot.transform.rotation);
        yield return new WaitForFixedUpdate();
        if (nf.IsSelecting(g)) mgr.SelectExit((IXRSelectInteractor)nf, g);
        mgr.SelectEnter((IXRSelectInteractor)slot, g);
        yield return new WaitForSeconds(0.6f);           // ProductItem ждёт исход отпускания 0.35 с
    }

    IEnumerator Buy(string name)
    {
        var it = Item(name);
        if (it == null) { Line("   нет товара " + name); yield break; }
        var g = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        mgr.SelectEnter((IXRSelectInteractor)nf, g);
        yield return Wait(0.5f, 1.2f);
        var slot = sc.basket.Slots.FirstOrDefault(s => !s.hasSelection);
        if (slot == null) { Line("   нет свободной ячейки корзины"); yield break; }
        yield return PutInto(it, slot);
    }

    IEnumerator PickAndReturn(string name)
    {
        var it = Item(name);
        if (it == null || it.homeSlot == null) yield break;
        var g = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        mgr.SelectEnter((IXRSelectInteractor)nf, g);
        yield return Wait(0.6f, 1.5f);
        yield return PutInto(it, it.homeSlot);
    }

    // ===================== check: протокол + кадры =====================

    IEnumerator Check()
    {
        Line("ПРОГОН СЦЕНАРИЯ ОТ ВХОДА ДО РЕЗУЛЬТАТОВ (Play Mode, без шлема)");
        Line($"   профиль перемещения: {rig.profile?.profileName}; условие на старте: {sc.context?.condition?.conditionName}");
        Line("");

        var entry = Z("Entry"); var start = Z("StartMark"); var checkout = Z("Checkout"); var deb = Z("Debrief");
        var outside = P("Scenario/Points/Outside"); var standLook = P("Scenario/Points/StandLook");

        Line("1. ОЖИДАНИЕ ВХОДА");
        Place(outside.position, outside.eulerAngles.y);
        yield return new WaitForSeconds(0.5f);
        Check("состояние WaitingForEntry", sc.State == ScenarioState.WaitingForEntry);
        Check($"табло: «{view.CurrentTitle}»", view.CurrentTitle.Length > 0);
        Check("кнопка «Начать» недоступна, слой взаимодействия = 0", !sc.startButton.Available && sc.startButton.Interactable.interactionLayers.value == 0);
        yield return Shot("01_Ожидание_входа", "Cam_Entry");

        Line("2. ВХОД → ПОДГОТОВКА (телепорт внутрь зоны входа)");
        int tr0 = entry.GetComponent<ScenarioZone>().triggerEnters;
        Place(entry.position, outside.eulerAngles.y);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        var ez = entry.GetComponent<ScenarioZone>();
        Line($"   зона Entry: OnTriggerEnter за 2 шага физики: {ez.triggerEnters - tr0}, обнаружено сверкой: {ez.reconciledEnters}, источник: {ez.lastSource}");
        Check("после телепорта зона входа фиксирует присутствие", ez.PlayerInside);
        Check("состояние Preparation", sc.State == ScenarioState.Preparation);
        Check("табло перечисляет невыполненное: " + string.Join("; ", sc.Conditions.Missing()), sc.Conditions.Missing().Count() >= 3);
        yield return new WaitForSeconds(0.3f);
        Place(standLook.position, standLook.eulerAngles.y, 22f);
        yield return Shot("02_Подготовка_невыполненные_условия", "Cam_Board");
        yield return Shot("03_Стойка_до_взятия_корзины", "Cam_Stand");
        yield return Shot("05_Кнопка_Начать_серая_крупно", "Cam_StartButton");

        Line("3. КОРЗИНА И СПИСОК");
        var mark0 = view.BasketMarkColor;
        yield return TakeBasket();
        Check($"корзина у участника ({sc.basket.State}: {sc.basket.LastChangeReason})", sc.Conditions.BasketCarried);
        Check("метка корзины сменила цвет (оранжевая → зелёная)", view.BasketMarkColor != mark0 && view.BasketMarkColor.g > 0.6f);
        yield return Select(sc.shoppingList.GetComponent<XRSimpleInteractable>());
        Check("список покупок взят, переехал на запястье", sc.Conditions.ListTaken);
        Check("ещё не Ready — участник не на отметке", sc.State == ScenarioState.Preparation);
        yield return Shot("04_Стойка_корзина_и_список_взяты", "Cam_Stand");

        Line("4. СТАРТОВАЯ ОТМЕТКА → ГОТОВО");
        Place(start.position, StartYaw);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check("зона отметки фиксирует присутствие после телепорта", start.GetComponent<ScenarioZone>().PlayerInside);
        Check("Ready = все условия: " + sc.Conditions.Snapshot(), sc.State == ScenarioState.Ready);
        Check("отметка на полу зелёная", view.StartMarkColor.g > 0.6f && view.StartMarkColor.r < 0.5f);
        Check("кнопка «Начать» доступна, слой взаимодействия ≠ 0, луч может её выбрать",
            sc.startButton.Available && mgr.IsSelectPossible((IXRSelectInteractor)nf, sc.startButton.Interactable));
        yield return Shot("06_Готово_кнопка_Начать_зелёная", "Cam_Board");
        yield return Shot("07_Кнопка_Начать_зелёная_крупно", "Cam_StartButton");

        Line("5. ЗАХОД A");
        yield return Press(sc.startButton);
        Check("состояние RunA, идёт отсчёт", sc.State == ScenarioState.RunA && sc.InCountdown);
        Check("профиль зафиксирован: " + sc.LockedProfile?.profileName, sc.LockedProfile == rig.profile);
        yield return Shot("08_Заход_A_отсчёт", "Cam_Board");
        yield return new WaitForSeconds(sc.countdownSeconds + 0.3f);
        Check("отсчёт окончен", !sc.InCountdown && sc.State == ScenarioState.RunA);

        var other = ScriptableObject.CreateInstance<LocomotionProfileSO>();   // любой другой профиль
        other.profileName = "Smooth_Other";
        {
            int a0 = sc.ProfileChangeAttempts;
            rig.SetProfile(other);
            Check($"попытка сменить профиль на {other.profileName} отклонена и записана", rig.profile == sc.LockedProfile && sc.ProfileChangeAttempts == a0 + 1);
        }

        var s03 = Z("Shelf_03");
        Place(s03.position, 180f, 12f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check("зона Shelf_03 фиксирует присутствие после телепорта к якорю", Z("Shelf_03").GetComponent<ScenarioZone>().PlayerInside);
        yield return Shot("09_Заход_A_выкладка_с_приёмами_Shelf_03", "Cam_Shelf");
        yield return PickAndReturn("Can_FERRO");
        yield return Buy("Can_VOLTA");
        Check("ценник VOLTA подсвечен после взятия", applier != null && applier.IsHighlighted("VOLTA"));
        yield return Shot("10_Ценник_подсвечен_после_взятия", "Cam_Tag");
        yield return Buy("Bottle_PURA");
        Check("чек на запястье обновился: в корзине 2 товара", sc.basket.ItemCount == 2 && sc.shoppingList.text != null && sc.shoppingList.text.text.Contains("VOLTA"));
        yield return ShotFromHead("11_Список_и_чек_на_запястье", sc.shoppingList.transform, 0.32f);

        Line("6. НАРУШЕНИЕ УСЛОВИЯ ВО ВРЕМЯ ЗАХОДА → ПАУЗА");
        int events0 = rec != null ? rec.EventsTotal : 0; int picks0 = rec != null ? rec.Runs[1].picks : 0; int items0 = sc.basket.ItemCount;
        yield return DropBasket();
        yield return new WaitForSeconds(0.5f);
        Check("корзина уронена → Paused: " + sc.PauseReason, sc.State == ScenarioState.Paused);
        Check("«Продолжить» недоступна, пока корзина не поднята", !sc.resumeButton.Available);
        var bp = sc.basket.transform.position;
        Place(start.position, StartYaw);
        yield return Shot("12_Пауза_корзина_уронена", "Cam_Board");
        yield return new WaitForSeconds(1.0f);
        Check("пауза не снимается сама (через 1 с всё ещё Paused)", sc.State == ScenarioState.Paused);
        Place(new Vector3(bp.x, 0f, bp.z) - origin.forward * 0.3f, origin.eulerAngles.y);
        yield return TakeBasket();
        Check("корзина снова у участника, «Продолжить» доступна", sc.resumeButton.Available);
        Place(start.position, StartYaw);
        yield return Shot("13_Пауза_можно_продолжить", "Cam_Board");
        Check($"накопленные данные сохранены: товаров в корзине {sc.basket.ItemCount} (было {items0}), взятий {rec?.Runs[1].picks} (было {picks0})",
            sc.basket.ItemCount == items0 && (rec == null || rec.Runs[1].picks == picks0));
        yield return Press(sc.resumeButton);
        Check("продолжение: снова RunA, тот же заход 1", sc.State == ScenarioState.RunA && sc.Run == 1);

        Place(Z("Shelf_04").position, 180f, 12f);
        yield return new WaitForFixedUpdate();
        yield return Buy("Pack_GRANO");

        Line("7. КАССА");
        Check("до кассы «Завершить» недоступна", !sc.finishButton.Available);
        var cp = P("Scenario/Points/CheckoutLook");
        Place(checkout.position, cp != null ? cp.eulerAngles.y : 180f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check("у кассы: касса подсвечена, «Завершить» доступна", view.CheckoutLit && sc.finishButton.Available);
        yield return Shot("14_Касса_подсвечена", "Cam_Checkout");
        yield return Press(sc.finishButton);
        Check($"Checkout: чек {sc.Runs[1].receipt.Count} поз. на {sc.Runs[1].Total:0.00}", sc.State == ScenarioState.Checkout && sc.Runs[1].receipt.Count == 3);
        yield return Shot("15_Касса_чек_захода_A", "Cam_Board");

        Line("8. РАЗБОР");
        var volta = Item("Can_VOLTA");
        var vA = volta.transform.position; int tierA = volta.tier;
        Place(deb.position + new Vector3(1.2f, 0f, 0f), deb.eulerAngles.y);   // сбоку от оси табло: корзина не загораживает кадр
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
        Check("вход в зону разбора → Debrief", sc.State == ScenarioState.Debrief);
        Check("условие эксперимента сменилось на " + sc.context.condition.conditionName, sc.context.condition == sc.conditionC);
        var vC = volta.transform.position;
        Check(string.Format(ci, "планограмма сменилась: VOLTA ярус {0} → {1}, смещение {2:0.00} м", tierA, volta.tier, Vector3.Distance(vA, vC)), volta.tier != tierA && Vector3.Distance(vA, vC) > 0.5f);
        Check("корзина пуста — товары вернулись на полки", sc.basket.ItemCount == 0);
        Check("табло разбора показывает приёмы для покупок", debrief != null && debrief.CurrentBody.Contains("действовали"));
        yield return Shot("16_Разбор_захода_A", "Cam_Debrief");

        Line("9. ЗАХОД C");
        yield return Press(sc.nextButton);
        Check("Preparation, заход 2", sc.State == ScenarioState.Preparation && sc.Run == 2);
        Place(start.position, StartYaw);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Check("Ready перед заходом 2", sc.State == ScenarioState.Ready);
        yield return Press(sc.startButton);
        yield return new WaitForSeconds(sc.countdownSeconds + 0.3f);
        Place(s03.position, 180f, 12f);
        yield return Shot("17_Заход_C_выкладка_нейтральная_Shelf_03", "Cam_Shelf");
        Place(Z("Shelf_04").position, 180f, 12f);
        yield return new WaitForFixedUpdate();
        yield return Shot("18_Заход_C_напитки_на_одном_ярусе_Shelf_04", "Cam_Shelf04");
        yield return Buy("Can_ZEST");
        yield return Buy("Bottle_NUBO");
        Place(Z("Shelf_03").position, 180f, 12f);
        yield return Buy("Pack_GRANO");
        Place(start.position, StartYaw);
        yield return Shot("19_Заход_C", "Cam_Board");
        Place(checkout.position, cp != null ? cp.eulerAngles.y : 180f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        yield return Press(sc.finishButton);

        Line("10. РЕЗУЛЬТАТЫ");
        Place(deb.position + new Vector3(1.2f, 0f, 0f), deb.eulerAngles.y);   // сбоку от оси табло: корзина не загораживает кадр
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
        Check("Results", sc.State == ScenarioState.Results);
        Check(string.Format(ci, "сравнение: чек A {0:0.00} против C {1:0.00}, под воздействием {2} против {3}", sc.Runs[1].Total, sc.Runs[2].Total, sc.Runs[1].Manipulated, sc.Runs[2].Manipulated),
            sc.Runs[1].Total > 0f && sc.Runs[2].Total > 0f);
        Check("профиль в обоих заходах один: " + sc.Runs[1].profile + " / " + sc.Runs[2].profile, sc.Runs[1].profile == sc.Runs[2].profile);
        yield return Shot("20_Результаты_A_против_C", "Cam_Debrief");

        Line("");
        Line("ЖУРНАЛ СЦЕНАРИЯ:");
        foreach (var j in sc.Journal) Line("   " + j);
        if (rec != null)
        {
            rec.Flush();
            Line("");
            Line($"CSV сессии (события с колонками run и state): {ScenarioSessionRecorder.LastCsv}");
            if (OutDir != null && ScenarioSessionRecorder.LastCsv != null)
                File.Copy(ScenarioSessionRecorder.LastCsv, Path.Combine(OutDir, "31_Сессия_пример.csv"), true);
        }
        if (rig.route != null) { rig.route.FlushNow(); if (OutDir != null && RouteLogger.LastCsv != null) File.Copy(RouteLogger.LastCsv, Path.Combine(OutDir, "32_Маршрут_с_метками_захода.csv"), true); }
        Line("");
        Line($"ИТОГ: пройдено {pass}, не пройдено {fail}");
    }

    // ===================== record: тот же сценарий, медленно =====================

    IEnumerator Record()
    {
        var entry = Z("Entry"); var start = Z("StartMark"); var checkout = Z("Checkout"); var deb = Z("Debrief");
        var outside = P("Scenario/Points/Outside"); var standLook = P("Scenario/Points/StandLook"); var cp = P("Scenario/Points/CheckoutLook");
        Place(outside.position, outside.eulerAngles.y, 4f);
        yield return new WaitForSeconds(2.0f);
        yield return Walk(entry.position, outside.eulerAngles.y);
        yield return new WaitForSeconds(1.0f);
        yield return Walk(standLook.position, standLook.eulerAngles.y, 1.4f, 22f);
        yield return new WaitForSeconds(2.0f);
        yield return TakeBasket();
        yield return new WaitForSeconds(1.0f);
        yield return Select(sc.shoppingList.GetComponent<XRSimpleInteractable>());
        yield return new WaitForSeconds(1.5f);
        yield return Walk(start.position, StartYaw, 1.4f, 12f);
        yield return new WaitForSeconds(2.0f);
        yield return Press(sc.startButton);
        yield return new WaitForSeconds(sc.countdownSeconds + 0.5f);
        var s03 = Z("Shelf_03");
        Place(s03.position, 180f, 14f);   // телепорт
        yield return new WaitForSeconds(2.0f);
        yield return PickAndReturn("Can_FERRO");
        yield return Buy("Can_VOLTA");
        yield return new WaitForSeconds(1.0f);
        yield return Buy("Bottle_PURA");
        yield return new WaitForSeconds(1.0f);
        // намеренное нарушение: корзину уронили
        yield return DropBasket();
        Place(start.position, StartYaw, 10f);
        yield return new WaitForSeconds(3.0f);
        var bp = sc.basket.transform.position;
        Place(new Vector3(bp.x, 0f, bp.z) - Vector3.forward * 0.3f, 0f, 35f);
        yield return new WaitForSeconds(1.0f);
        yield return TakeBasket();
        Place(start.position, StartYaw, 10f);
        yield return new WaitForSeconds(2.0f);
        yield return Press(sc.resumeButton);
        yield return new WaitForSeconds(1.0f);
        Place(Z("Shelf_04").position, 180f, 14f);
        yield return new WaitForSeconds(1.0f);
        yield return Buy("Pack_GRANO");
        Place(checkout.position, cp != null ? cp.eulerAngles.y : 180f, 15f);
        yield return new WaitForSeconds(2.0f);
        yield return Press(sc.finishButton);
        Place(start.position, StartYaw, 10f);
        yield return new WaitForSeconds(2.5f);
        Place(deb.position, deb.eulerAngles.y, 6f);
        yield return new WaitForSeconds(5.0f);
        yield return Press(sc.nextButton);
        yield return Walk(start.position, StartYaw, 1.4f, 10f);
        yield return new WaitForSeconds(1.5f);
        yield return Press(sc.startButton);
        yield return new WaitForSeconds(sc.countdownSeconds + 0.5f);
        Place(s03.position, 180f, 14f);
        yield return new WaitForSeconds(2.0f);
        Place(Z("Shelf_04").position, 180f, 14f);
        yield return new WaitForSeconds(1.5f);
        yield return Buy("Can_ZEST");
        yield return Buy("Bottle_NUBO");
        Place(Z("Shelf_03").position, 180f, 14f);
        yield return Buy("Pack_GRANO");
        Place(checkout.position, cp != null ? cp.eulerAngles.y : 180f, 15f);
        yield return new WaitForSeconds(1.5f);
        yield return Press(sc.finishButton);
        yield return new WaitForSeconds(1.5f);
        Place(deb.position, deb.eulerAngles.y, 6f);
        yield return new WaitForSeconds(6.0f);
    }

    // ===================== кадры =====================

    // Кадры полок и табло разбора снимаются со стороны, поэтому «тело» участника (корзина, контроллеры, список
    // на запястье) на время кадра скрывается — иначе оно стоит между камерой и полкой. На логику не влияет.
    static readonly string[] HidePlayerFor = { "Cam_Shelf", "Cam_Shelf04", "Cam_Tag", "Cam_Debrief" };

    IEnumerator Shot(string file, string camName)
    {
        if (slow || OutDir == null) yield break;
        var cam = P("Scenario/Cameras/" + camName);
        if (cam == null) { Line("   (нет точки съёмки " + camName + ")"); yield break; }
        yield return new WaitForEndOfFrame();
        var hidden = new List<Renderer>();
        if (HidePlayerFor.Contains(camName))
            foreach (var r in origin.GetComponentsInChildren<Renderer>().Concat(sc.basket.GetComponentsInChildren<Renderer>()).Concat(sc.shoppingList.GetComponentsInChildren<Renderer>()).Distinct())
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
        var canv = new List<Canvas>();
        if (hidden.Count > 0) foreach (var c in sc.shoppingList.GetComponentsInChildren<Canvas>()) if (c.enabled) { c.enabled = false; canv.Add(c); }
        var sp = cam.GetComponent<ScenarioShotPoint>();
        Render(file, cam.position, cam.rotation, sp != null ? sp.fov : 60f);
        foreach (var r in hidden) r.enabled = true;
        foreach (var c in canv) c.enabled = true;
    }

    IEnumerator ShotFromHead(string file, Transform target, float dist)
    {
        if (slow || OutDir == null || target == null) yield break;
        yield return new WaitForEndOfFrame();
        var pos = target.position + target.up * dist;   // лицевая сторона листа — его локальная +y
        Render(file, pos, Quaternion.LookRotation(target.position - pos, target.forward), 45f);
    }

    void Render(string file, Vector3 pos, Quaternion rot, float fov)
    {
        var go = new GameObject("~NS_ScenarioCapture");
        var rt = new RenderTexture(shotWidth, shotHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(shotWidth, shotHeight, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(pos, rot);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, shotWidth, shotHeight), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
            Line("   кадр: " + file + ".png");
        }
        catch (Exception e) { Line("   кадр " + file + " не снят: " + e.Message); }
        finally { Destroy(go); Destroy(tex); rt.Release(); Destroy(rt); }
    }

    // ===================== протокол =====================

    void Check(string what, bool ok)
    {
        if (ok) pass++; else fail++;
        Line($"   [{(ok ? "OK" : "FAIL")}] {what}");
    }

    void Line(string s)
    {
        Report.AppendLine(s);
        Debug.Log("[NS-SCN-CHECK] " + s);
    }
}
