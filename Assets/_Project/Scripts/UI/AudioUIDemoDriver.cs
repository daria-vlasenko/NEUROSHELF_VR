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
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Прогон звука и интерфейсов без шлема (меню 10.12 / 10.13).
// Позы головы и рук задаёт скрипт. Интерфейс нажимается настоящим путём XRI: рука наводится на кнопку,
// XR UI Input Module пускает луч Near-Far по холсту (Tracked Device Graphic Raycaster), нажатие подаётся
// в штатный вход uiPressInput интерактора (ручное значение вместо курка). Товар и корзина — штатные
// SelectEnter/SelectExit, как в ЛР5–ЛР7. Звук и вибрация проверяются по журналам, а не на слух.
public class AudioUIDemoDriver : MonoBehaviour
{
    public static string OutDir;
    public static bool Finished;
    public static readonly StringBuilder Report = new StringBuilder();
    public string mode = "check";
    public int shotWidth = 1600, shotHeight = 900;

    ScenarioController sc; StoreAudioDirector dir; HandRoleArbiter arb; PlanogramApplier applier; SoundAnchorMetric metric;
    XROrigin xr; Transform origin, head, rHand, lHand; NearFarInteractor rNF, lNF; XRInteractionManager mgr; WristMenu wrist;
    int pass, fail;
    readonly CultureInfo ci = CultureInfo.InvariantCulture;
    bool slow => mode == "record";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Finished = false; Report.Clear(); }

    IEnumerator Start()
    {
        Report.Clear(); Finished = false;
        yield return new WaitForSeconds(1.0f);
        sc = ScenarioController.Instance; dir = StoreAudioDirector.Instance; arb = FindFirstObjectByType<HandRoleArbiter>();
        applier = FindFirstObjectByType<PlanogramApplier>(); metric = SoundAnchorMetric.Instance; wrist = FindFirstObjectByType<WristMenu>();
        xr = FindFirstObjectByType<XROrigin>();
        head = Camera.main != null ? Camera.main.transform : null;
        rNF = arb != null ? arb.productHand : null; lNF = arb != null ? arb.uiHand : null;
        if (sc == null || xr == null || head == null || rNF == null || lNF == null)
        { Line("ОШИБКА: нет сценария / XR Origin / камеры / рук (меню 9.0 и 10.0)."); Finished = true; yield break; }
        origin = xr.Origin.transform; rHand = rNF.transform.parent; lHand = GameObject.Find("Left Controller").transform;
        mgr = rNF.interactionManager;
        foreach (var t in new[] { head, rHand, lHand })
            foreach (var c in t.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var sim = GameObject.Find("XR Device Simulator"); if (sim != null) sim.SetActive(false);
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity;
        HandsRest();
        foreach (var nf in new[] { rNF, lNF }) nf.uiPressInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
        yield return slow ? Record() : Check();
        Finished = true;
    }

    // ===================== позы =====================

    void HandsRest()
    {
        // руки опущены лучами вниз: свободная рука не «водит» лучом по табло, пока её не наводят
        rHand.localPosition = new Vector3(0.22f, -0.45f, 0.30f); rHand.localRotation = Quaternion.Euler(70f, 0f, 0f);
        lHand.localPosition = new Vector3(-0.22f, -0.45f, 0.30f); lHand.localRotation = Quaternion.Euler(70f, 0f, 0f);
    }

    void Place(Vector3 p, float yaw, float pitch = 8f)
    {
        origin.SetPositionAndRotation(new Vector3(p.x, origin.position.y, p.z), Quaternion.Euler(0f, yaw, 0f));
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        HandsRest();
        var ba = sc.basket != null ? sc.basket.bodyAnchor : null;
        if (ba != null && ba.enabled) ba.SnapNow();
    }

    float YawTo(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

    IEnumerator Walk(Vector3 to, float yaw, float speed, Func<float, float> headYaw = null)
    {
        var from = origin.position; to.y = from.y;
        float d = Vector3.Distance(from, to), t = 0f, dur = Mathf.Max(0.2f, d / speed);
        float y0 = origin.eulerAngles.y;
        while (t < dur)
        {
            t += Time.deltaTime; float k = Mathf.SmoothStep(0f, 1f, t / dur);
            origin.SetPositionAndRotation(Vector3.Lerp(from, to, k), Quaternion.Euler(0f, Mathf.LerpAngle(y0, yaw, k), 0f));
            float hy = headYaw != null ? headYaw(k) : 0f;
            head.localRotation = Quaternion.Euler(8f, hy, 0f);
            yield return null;
        }
    }

    Transform Z(string id) => FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.zoneId == id)?.transform;
    Transform Pt(string path) => GameObject.Find(path)?.transform;
    IEnumerator W(float fast, float slowS) { yield return new WaitForSeconds(slow ? slowS : fast); }

    // ===================== интерфейс: настоящий луч XRI =====================

    // Наводит руку на кнопку и ждёт, пока XR UI Input Module сообщит о наведении (OnPointerEnter).
    IEnumerator Aim(Transform hand, InteractorHandedness h, XRCanvasButton b, int maxFrames = 40)
    {
        var target = UIFeedback.Center(b.transform);
        for (int i = 0; i < maxFrames && !b.HoveredBy(h); i++)
        {
            hand.rotation = Quaternion.LookRotation(target - hand.position, Vector3.up);
            yield return null;
        }
        yield return null; yield return null;
    }

    // Нажатие: штатный вход uiPressInput интерактора получает «нажато» на кадр, затем «отпущено» — XR UI Input Module
    // формирует PointerDown/PointerUp/PointerClick так же, как от курка контроллера.
    IEnumerator Click(NearFarInteractor nf, Transform hand, XRCanvasButton b, string what)
    {
        int c0 = b.Clicks, d0 = b.Denials;
        yield return Aim(hand, nf.handedness, b);
        bool hovered = b.HoveredBy(nf.handedness);
        nf.uiPressInput.QueueManualState(true, 1f, true, false);
        yield return null; yield return null;
        nf.uiPressInput.QueueManualState(false, 0f, false, true);
        yield return null; yield return null;
        nf.uiPressInput.QueueManualState(false, 0f, false, false);
        yield return new WaitForSeconds(0.25f);   // двойной отказной импульс идёт 0.15 с
        bool reached = b.Clicks > c0 || b.Denials > d0;
        string hit = nf.TryGetUIModel(out var model) && model.currentRaycast.isValid ? model.currentRaycast.gameObject.name : "ничего";
        Line($"   {what}: луч {nf.handedness} {(hovered ? "навёлся" : "НЕ навёлся")} (UI-луч попадает в «{hit}»), нажатие {(reached ? (b.Clicks > c0 ? "прошло" : "отказ") : "НЕ дошло")}");
        if (!reached)
        {
            b.Activate(nf.handedness);   // запасной путь — тот же обработчик кнопки; отмечается в протоколе
            Line("   (!) нажатие подано программно в обработчик кнопки");
        }
        yield return null;
        HandsRest();
    }

    XRCanvasButton CanvasOf(ScenarioButton b) => FindObjectsByType<ScenarioCanvasButton>(FindObjectsSortMode.None).FirstOrDefault(x => x.logic == b)?.view;

    // ===================== корзина, список, товар (как в ЛР7) =====================

    IEnumerator Select(IXRSelectInteractable target, float hold = 0.25f)
    {
        mgr.SelectEnter((IXRSelectInteractor)rNF, target);
        yield return new WaitForSeconds(hold);
        if (rNF.IsSelecting(target)) mgr.SelectExit((IXRSelectInteractor)rNF, target);
        yield return new WaitForFixedUpdate();
    }

    IEnumerator TakeBasket()
    {
        var g = (IXRSelectInteractable)sc.basket.Grab;
        mgr.SelectEnter((IXRSelectInteractor)rNF, g);
        yield return W(0.3f, 0.8f);
        rHand.localPosition = new Vector3(0.18f, -0.55f, 0.25f);
        yield return W(0.3f, 0.6f);
        mgr.SelectExit((IXRSelectInteractor)rNF, g);
        yield return new WaitForFixedUpdate();
        HandsRest();
    }

    IEnumerator MakeReady()
    {
        Place(Z("Entry").position, 180f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        var stand = Pt("Scenario/Points/StandLook");
        Place(stand.position, stand.eulerAngles.y, 22f);
        yield return W(0.3f, 1.0f);
        yield return TakeBasket();
        yield return Select(sc.shoppingList.GetComponent<XRSimpleInteractable>());
        var start = Z("StartMark"); var sl = Pt("Scenario/Points/StartLook");
        Place(start.position, sl != null ? sl.eulerAngles.y : 90f, 10f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
    }

    ProductItem Item(string n) => GameObject.Find(n)?.GetComponent<ProductItem>();

    IEnumerator Grab(ProductItem it)
    {
        mgr.SelectEnter((IXRSelectInteractor)rNF, (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>());
        yield return W(0.4f, 1.2f);
    }

    IEnumerator PutInBasket(ProductItem it)
    {
        var g = (IXRSelectInteractable)it.GetComponent<XRGrabInteractable>();
        var slot = sc.basket.Slots.FirstOrDefault(s => !s.hasSelection);
        if (slot == null) yield break;
        it.transform.SetPositionAndRotation(slot.transform.position + Vector3.up * 0.02f, slot.transform.rotation);
        yield return new WaitForFixedUpdate();
        if (rNF.IsSelecting(g)) mgr.SelectExit((IXRSelectInteractor)rNF, g);
        mgr.SelectEnter((IXRSelectInteractor)slot, g);
        yield return new WaitForSeconds(0.6f);
    }

    // ===================== check =====================

    IEnumerator Check()
    {
        Line("ПРОГОН ЗВУКА И ИНТЕРФЕЙСОВ (Play Mode, без шлема)");
        Line($"   условие: {sc.context?.condition?.conditionName}; спатиалайзер: «{AudioSettings.GetSpatializerPluginName()}»");
        Line("");

        Line("1. СЛУШАТЕЛЬ И ИСТОЧНИКИ");
        var ls = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        Check($"Audio Listener один ({ls.Length}) и висит на камере XR Origin ({(ls.Length > 0 ? ls[0].name : "—")})", ls.Length == 1 && ls[0].gameObject == xr.Camera.gameObject);
        Check($"камера на высоте глаз: {head.position.y:0.000} м (Camera Y Offset {xr.CameraYOffset:0.00})", Mathf.Abs(head.position.y - xr.CameraYOffset) < 0.02f);
        yield return new WaitForSeconds(0.5f);
        Check($"музыка в условии A играет ({(dir.music.clip != null ? dir.music.clip.name : "—")}, громкость {dir.music.volume:0.00})", dir.MusicOn && dir.music.isPlaying);
        Check($"якорь у {dir.AnchorShelf}: источник в {Fmt(dir.anchor.transform.position)}", dir.AnchorShelf == "Shelf_03");
        yield return Shot("01_Схема_звука_сверху", TopDown());

        Line("2. ЦЕННИКИ — ХОЛСТЫ ИЗ ПЛАНОГРАММЫ");
        var tag = applier.TagUI("VOLTA");
        Check($"ценник VOLTA — World Space Canvas {tag.GetComponent<Canvas>().renderMode}, масштаб {tag.transform.lossyScale.x:0.000} (1 px = 1 мм), {PriceTagUI.WidthMm:0}×{PriceTagUI.HeightMm:0} мм", tag.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace && Mathf.Abs(tag.transform.lossyScale.x - 0.001f) < 1e-5f);
        Check($"условие A: якорная цена «{tag.AnchorShown}» зачёркнута, цена «{tag.PriceShown}»", tag.HasAnchor && tag.AnchorShown == "12.90" && tag.PriceShown == "7.90");
        yield return ShotAt("02_Ценник_VOLTA_якорная_цена_A_крупно", tag.transform.position + tag.transform.forward * -0.32f + Vector3.up * 0.04f, tag.transform.position, 28f);
        yield return ShotCam("03_Ценники_Shelf_03_условие_A", "Interface/Cameras/Cam_Shelf03_Tags");
        sc.context.SetCondition(sc.conditionC);
        yield return new WaitForSeconds(2.0f);
        tag = applier.TagUI("VOLTA");
        Check($"условие C: только честная цена «{tag.PriceShown}», якоря нет", !tag.HasAnchor && tag.AnchorShown == "" && tag.PriceShown == "7.90");
        Check($"условие C: музыка выключена (играет: {dir.music.isPlaying}), якоря нет («{dir.AnchorShelf}»)", !dir.music.isPlaying && dir.AnchorShelf == "");
        yield return ShotAt("04_Ценник_VOLTA_нейтральный_C_крупно", tag.transform.position + tag.transform.forward * -0.32f + Vector3.up * 0.04f, tag.transform.position, 28f);
        sc.context.SetCondition(sc.conditionA);
        yield return new WaitForSeconds(2.0f);
        Check("назад в A: музыка и якорь вернулись", dir.music.isPlaying && dir.AnchorShelf == "Shelf_03");

        Line("3. ТАБЛИЧКА У ВХОДА, ТАБЛО, НАСТРОЙКИ");
        yield return ShotCam("10_Табличка_у_входа", "Interface/Cameras/Cam_Sign");
        yield return MakeReady();
        Check($"сценарий готов к старту: {sc.State}", sc.State == ScenarioState.Ready);
        yield return ShotCam("05_Табло_сценария_холст", "Interface/Cameras/Cam_BoardUI");
        yield return ShotCam("08_Панель_настроек_звука", "Interface/Cameras/Cam_Settings");

        Line("4. НАЖАТИЕ ЛУЧОМ: «Начать» правой рукой");
        var startView = CanvasOf(sc.startButton);
        int j0 = HapticFeedback.Journal.Count;
        var fb = startView.GetComponentInParent<UIFeedback>();
        int played0 = fb.Played;
        yield return Click(rNF, rHand, startView, "«Начать»");
        Check($"сценарий перешёл в {sc.State}", sc.State == ScenarioState.RunA);
        var newRec = HapticFeedback.Journal.Skip(j0).Where(r => r.detail == startView.id).ToList();
        Check("вибрация: наведение и нажатие на правую руку — " + string.Join("; ", newRec.Select(r => $"{r.pattern} {r.hand} A={r.amplitude:0.00}")),
            newRec.Any(r => r.pattern == "UIHover" && r.hand == "Right") && newRec.Any(r => r.pattern == "UIPress" && r.hand == "Right"));
        Check($"звук щелчка от холста: {fb.Played - played0} шт., источник в {Fmt(fb.LastPosition)} (центр кнопки {Fmt(UIFeedback.Center(startView.transform))}), spatialBlend {fb.Source.spatialBlend:0.0}, через спатиалайзер {fb.Source.spatialize}",
            fb.Played > played0 && Vector3.Distance(fb.LastPosition, UIFeedback.Center(startView.transform)) < 0.01f && fb.Source.spatialize);
        yield return new WaitForSeconds(sc.countdownSeconds + 0.3f);

        Line("5. ОТКАЗ: панель настроек заблокирована во время захода (левая рука)");
        var settings = FindFirstObjectByType<AudioSettingsPanel>();
        var plus = settings.rows.First(r => r.group == StoreAudioMixer.Group.Stimulus).plus;
        float db0 = StoreAudioMixer.Instance.GetVolumeDb(StoreAudioMixer.Group.Stimulus);
        var setPt = Pt("Interface/Cameras/Cam_Settings");
        Place(setPt.position + Vector3.right * 0.3f, 90f, 5f);
        yield return null;
        j0 = HapticFeedback.Journal.Count;
        yield return Click(lNF, lHand, plus, "«+ Stimulus»");
        var deny = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "UIDeny").ToList();
        Check($"панель заблокирована ({settings.Locked}), громкость Stimulus не изменилась ({db0:0} → {StoreAudioMixer.Instance.GetVolumeDb(StoreAudioMixer.Group.Stimulus):0} дБ)", settings.Locked && Mathf.Approximately(db0, StoreAudioMixer.Instance.GetVolumeDb(StoreAudioMixer.Group.Stimulus)));
        Check($"двойной отказной импульс на левую руку: {deny.Count} импульса, {string.Join(", ", deny.Select(r => r.hand))}", deny.Count == 2 && deny.All(r => r.hand == "Left"));

        Line("6. НАРУЧНОЕ МЕНЮ: появляется только при взгляде на запястье");
        Place(Z("StartMark").position, 270f, 0f);
        yield return null;
        lHand.localPosition = new Vector3(-0.05f, -0.30f, 0.33f);
        lHand.localRotation = Quaternion.Euler(-20f, 0f, 0f);
        head.localRotation = Quaternion.Euler(8f, 0f, 0f);
        yield return new WaitForSeconds(0.4f);
        Check($"взгляд вперёд: меню скрыто (угол {wrist.GazeAngle:0}°, α={wrist.Alpha:0.00})", wrist.Alpha < 0.05f);
        head.localRotation = Quaternion.LookRotation(head.parent.InverseTransformDirection(wrist.transform.position - head.position), Vector3.up);
        yield return new WaitForSeconds(0.4f);
        Check($"взгляд на запястье: меню видно (угол {wrist.GazeAngle:0}°, лицом к глазам {wrist.Facing:0.00}, α={wrist.Alpha:0.00})", wrist.Alpha > 0.95f);
        yield return ShotHead("06_Наручное_меню", wrist.transform, 0.30f);
        head.localRotation = Quaternion.Euler(8f, 0f, 0f);
        HandsRest();

        Line("7. ЗВУКОВОЙ ЯКОРЬ: окклюзия стеллажом");
        var anchor = dir.anchor.GetComponent<StoreAudioSource>();
        Place(new Vector3(-1.2f, 0f, 0.1f), 180f);
        yield return new WaitForSeconds(0.6f);
        float occFront = anchor.Occlusion;
        Place(new Vector3(-1.2f, 0f, 2.6f), 180f);
        yield return new WaitForSeconds(0.6f);
        float occBehind = anchor.Occlusion;
        Check($"в проходе перед Shelf_03 якорь в прямой видимости: окклюзия {occFront:0.00} (1 = не перекрыт)", occFront > 0.9f);
        Check($"за стеллажом Shelf_01 якорь перекрыт: окклюзия {occBehind:0.00} → прямой звук {20f * Mathf.Log10(Mathf.Max(occBehind, 1e-3f)):0.0} дБ", occBehind < 0.5f);

        Line("8. ВЗЯТИЕ ТОВАРА: вибрация по массе, роли рук");
        Place(Z("Shelf_03").position, 180f, 14f);
        yield return new WaitForSeconds(0.5f);
        j0 = HapticFeedback.Journal.Count;
        var volta = Item("Can_VOLTA"); var pura = Item("Bottle_PURA");
        yield return Grab(volta);
        Check($"правая держит товар → её UI-луч выключен ({!arb.ProductHandUI}), левая — UI ({arb.UIHandUI})", !arb.ProductHandUI && arb.UIHandUI);
        yield return PutInBasket(volta);
        yield return null;
        Check($"после отпускания UI-луч правой снова включён ({arb.ProductHandUI})", arb.ProductHandUI);
        yield return Grab(pura);
        yield return PutInBasket(pura);
        var grabs = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "Grab").ToList();
        var places = HapticFeedback.Journal.Skip(j0).Where(r => r.pattern == "BasketPlace").ToList();
        Check("взятие: " + string.Join("; ", grabs.Select(r => $"{r.detail} → A={r.amplitude:0.00}")) + " — тяжелее = сильнее",
            grabs.Count == 2 && grabs[1].amplitude > grabs[0].amplitude && volta.Body.mass < pura.Body.mass);
        Check($"товар в корзину: {places.Count} подтверждающих щелчка, рука {string.Join(",", places.Select(r => r.hand).Distinct())}", places.Count == 2 && places.All(r => r.hand == "Right"));
        yield return new WaitForSeconds(0.3f);
        var receipt = FindFirstObjectByType<BasketReceipt>();
        Check($"чек на корзине: «{receipt.Shown.Replace("\n", " | ")}»", receipt.Shown.Contains("VOLTA") && receipt.Shown.Contains("PURA"));
        yield return ShotHead("07_Чек_на_корзине", receipt.transform, 0.45f);

        Line("9. КАССА: «Оплатить» левой рукой, пикание по позициям");
        var cp = Pt("Scenario/Points/CheckoutLook");
        Place(Z("Checkout").position + Vector3.forward * 0.35f, cp != null ? cp.eulerAngles.y : 180f, 15f);   // 1 м до кнопки, как при подходе
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate(); yield return null;
        var chk = FindFirstObjectByType<CheckoutAudio>();
        int beeps0 = chk.BeepsPlayed;
        yield return Click(lNF, lHand, CanvasOf(sc.finishButton), "«Оплатить»");
        yield return new WaitForSeconds(1.5f);
        Check($"касса: состояние {sc.State}, пиканий {chk.BeepsPlayed - beeps0} на {sc.Runs[1]?.receipt.Count} позиции, чек напечатан ({chk.ReceiptsPrinted})", sc.State == ScenarioState.Checkout && chk.BeepsPlayed - beeps0 == sc.Runs[1].receipt.Count);

        Line("10. МЕТРИКА ЗВУКОВОГО ЯКОРЯ (заход 1)");
        foreach (var r in metric.Rows.Where(r => r.run == 1))
            Line(string.Format(ci, "   {0}{1,-2} T_look={2,6} с  T_enter(зона)={3,6} с  T_enter(RouteLogger)={4,6} с  порядок={5}  цензур={6}",
                r.shelf, r.hasAnchor ? "*" : "", F(r.tLook), F(r.tEnterZone), F(r.tEnterRoute), r.rank, r.censored ? 1 : 0));
        Check("строки метрики записаны для 4 стеллажей, якорь отмечен у Shelf_03", metric.Rows.Count(r => r.run == 1) == 4 && metric.Rows.Any(r => r.run == 1 && r.hasAnchor && r.shelf == "Shelf_03"));

        Line("11. ГОЛОСА");
        var all = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        int playing = all.Count(a => a.isPlaying);
        Check($"звучит {playing} источников при лимите реальных голосов {AudioSettings.GetConfiguration().numRealVoices}", playing <= AudioSettings.GetConfiguration().numRealVoices);

        Line("");
        Line("ЖУРНАЛ ИМПУЛЬСОВ");
        foreach (var r in HapticFeedback.Journal) Line("   " + r);
        Line("");
        Line("ЖУРНАЛ РОЛЕЙ РУК");
        foreach (var s in arb.Journal) Line("   " + s);
        Line("");
        Line($"ИТОГ: {pass} из {pass + fail}");
    }

    static string F(float v) => v < 0f ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture);
    string Fmt(Vector3 v) => string.Format(ci, "({0:0.00}; {1:0.00}; {2:0.00})", v.x, v.y, v.z);

    // ===================== record =====================

    IEnumerator Record()
    {
        yield return MakeReady();
        yield return new WaitForSeconds(1.0f);
        yield return Click(rNF, rHand, CanvasOf(sc.startButton), "«Начать»");
        yield return new WaitForSeconds(sc.countdownSeconds + 0.5f);
        // проход вдоль ряда мимо звукового якоря: голова поворачивается на звук
        var a = dir.anchor.transform.position;
        yield return Walk(new Vector3(2.8f, 0f, 0.2f), 270f, 1.0f);
        yield return Walk(new Vector3(0.6f, 0f, 0.1f), 270f, 0.8f, k => Mathf.Lerp(0f, -60f, Mathf.Clamp01(k * 1.5f)));
        yield return Walk(new Vector3(-1.2f, 0f, 0.0f), 180f, 0.6f);
        yield return new WaitForSeconds(2.5f);
        var volta = Item("Can_VOLTA");
        head.localRotation = Quaternion.Euler(14f, 0f, 0f);
        yield return Grab(volta);
        yield return new WaitForSeconds(1.0f);
        yield return PutInBasket(volta);
        yield return new WaitForSeconds(1.0f);
        yield return Walk(new Vector3(4.5f, 0f, -0.9f), 180f, 1.2f);
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(1.0f);
        yield return Click(lNF, lHand, CanvasOf(sc.finishButton), "«Оплатить»");
        yield return new WaitForSeconds(3.0f);
    }

    // ===================== кадры =====================

    struct CamPose { public Vector3 pos; public Quaternion rot; public float fov; public bool ortho; public float size; }

    CamPose TopDown() => new CamPose { pos = new Vector3(0f, 2.85f, 0f), rot = Quaternion.Euler(90f, 0f, 0f), ortho = true, size = 4.3f };

    IEnumerator ShotCam(string file, string path)
    {
        var t = Pt(path);
        if (t == null) { Line("   (нет точки съёмки " + path + ")"); yield break; }
        var sp = t.GetComponent<ScenarioShotPoint>();
        yield return Shot(file, new CamPose { pos = t.position, rot = t.rotation, fov = sp != null ? sp.fov : 60f });
    }

    IEnumerator ShotAt(string file, Vector3 pos, Vector3 at, float fov) =>
        Shot(file, new CamPose { pos = pos, rot = Quaternion.LookRotation(at - pos, Vector3.up), fov = fov });

    // кадр «глазами участника»: камера со стороны лица холста (−z холста) на расстоянии dist
    IEnumerator ShotHead(string file, Transform canvas, float dist)
    {
        var pos = canvas.position - canvas.forward * dist;
        yield return Shot(file, new CamPose { pos = pos, rot = Quaternion.LookRotation(canvas.forward, canvas.up), fov = 42f });
    }

    readonly List<GameObject> markers = new List<GameObject>();

    IEnumerator Shot(string file, CamPose p)
    {
        if (slow || OutDir == null) yield break;
        bool top = p.ortho;
        if (top) BuildMarkers();
        yield return new WaitForEndOfFrame();
        var go = new GameObject("~NS_AudioCapture");
        var rt = new RenderTexture(shotWidth, shotHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(shotWidth, shotHeight, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(p.pos, p.rot);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f;
            cam.orthographic = p.ortho; cam.orthographicSize = p.size; cam.fieldOfView = p.fov > 0 ? p.fov : 60f;
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
        finally
        {
            Destroy(go); Destroy(tex); rt.Release(); Destroy(rt);
            if (top) { foreach (var m in markers) Destroy(m); markers.Clear(); }
        }
    }

    // Схема звука сверху: метки источников (цвет — группа микшера), окружности min/max расстояния источников
    // и окружности внутреннего (сплошная) и внешнего (тонкая) радиуса зон реверберации. Временные объекты — только на кадр.
    void BuildMarkers()
    {
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Color GroupColor(StoreAudioMixer.Group g) => g switch
        {
            StoreAudioMixer.Group.Music => new Color(0.65f, 0.45f, 1f),
            StoreAudioMixer.Group.Stimulus => new Color(1f, 0.25f, 0.25f),
            StoreAudioMixer.Group.Interface => new Color(0.3f, 0.85f, 1f),
            _ => new Color(1f, 0.8f, 0.2f),
        };
        foreach (var s in FindObjectsByType<StoreAudioSource>(FindObjectsSortMode.None))
        {
            if (s.role == StoreAudioSource.Role.Interface || s.role == StoreAudioSource.Role.NpcMurmur) continue;
            var c = GroupColor(s.group);
            var p = s.transform.position; p.y = 2.2f;
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            dot.transform.position = p; dot.transform.localScale = Vector3.one * 0.28f;
            dot.GetComponent<Renderer>().material = new Material(unlit) { color = c };
            markers.Add(dot);
            if (s.role != StoreAudioSource.Role.Music && s.role != StoreAudioSource.Role.NpcSteps)
            {
                markers.Add(Ring(p, s.Source.minDistance, c, 0.05f));
                markers.Add(Ring(p, s.Source.maxDistance, c * 0.7f, 0.02f));
            }
            markers.Add(Label(p + new Vector3(0.18f, 0f, 0.18f), s.role == StoreAudioSource.Role.NpcSteps ? "шаги" : s.sourceId, c));
        }
        foreach (var z in FindObjectsByType<AudioReverbZone>(FindObjectsSortMode.None))
        {
            var p = z.transform.position; p.y = 2.1f;
            var c = new Color(0.35f, 0.95f, 0.55f);
            markers.Add(Ring(p, z.minDistance, c, 0.05f));
            markers.Add(Ring(p, z.maxDistance, c * 0.6f, 0.025f));
            markers.Add(Label(p + new Vector3(-0.4f, 0f, -0.3f), "reverb " + z.name.Replace("Reverb_", ""), c));
        }
        foreach (var m in dir.anchorMounts)
        {
            var p = m.position; p.y = 2.2f;
            markers.Add(Label(p + new Vector3(-0.5f, 0f, -0.35f), "крепление якоря", new Color(1f, 0.55f, 0.55f)));
        }
    }

    GameObject Ring(Vector3 c, float r, Color col, float w)
    {
        var go = new GameObject("~ring");
        var lr = go.AddComponent<LineRenderer>();
        int n = 96; lr.positionCount = n; lr.loop = true; lr.useWorldSpace = true; lr.widthMultiplier = w;
        lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = col };
        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)); }
        return go;
    }

    GameObject Label(Vector3 p, string text, Color c)
    {
        var go = new GameObject("~label");
        go.transform.position = p; go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var cv = WorldText.Canvas(go.transform, "C", new Vector2(1.6f, 0.25f), Vector3.zero, 4f);
        var t = WorldText.Label(cv.transform, "T", new Rect(0, 0, 1600, 250), 170, TextAnchor.UpperLeft, FontStyle.Bold);
        t.text = text; t.color = c; t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return go;
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
        Debug.Log("[NS-AUD-CHECK] " + s);
    }
}
