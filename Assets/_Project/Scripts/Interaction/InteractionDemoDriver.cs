using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Автоматический демонстрационный проход и проверка взаимодействия.
// Шлема нет, поэтому позы головы и контроллеров задаёт скрипт, а «нажатие курка» подаётся
// в штатный вход Select интерактора через bypass. Сам захват, притягивание лучом, сокеты,
// бросок и физику выполняют штатные компоненты XR Interaction Toolkit — скрипт их не подменяет.
// Используется только из меню NEUROSHELF → Взаимодействие (7.6 / 7.8), в сцене не хранится.
public class InteractionDemoDriver : MonoBehaviour
{
    public static string OutDir;          // куда класть кадры (задаёт редактор)
    public static bool Finished;
    public static bool ScenarioStarted;
    public static readonly StringBuilder Report = new StringBuilder();
    public static readonly List<string> Shots = new List<string>();
    public bool takeShots = true;
    public bool runPhysicsTests = true;

    class Button : IXRInputButtonReader
    {
        bool down; int pressed = -10, released = -10;
        public void Set(bool d) { if (d == down) return; down = d; if (d) pressed = Time.frameCount; else released = Time.frameCount; }
        public bool ReadIsPerformed() => down;
        public bool ReadWasPerformedThisFrame() => down && Time.frameCount - pressed <= 1;
        public bool ReadWasCompletedThisFrame() => !down && Time.frameCount - released <= 1;
        public float ReadValue() => down ? 1f : 0f;
        public bool TryReadValue(out float v) { v = ReadValue(); return true; }
    }

    Transform head, rightCtl, leftCtl;
    NearFarInteractor right, left;
    readonly Button rBtn = new Button(), lBtn = new Button();
    Transform basket;
    readonly CultureInfo ci = CultureInfo.InvariantCulture;
    int pass, fail;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Finished = false; ScenarioStarted = false; Report.Clear(); Shots.Clear(); }

    IEnumerator Start()
    {
        Report.Clear(); Shots.Clear(); Finished = false;
        yield return new WaitForSeconds(0.6f);   // сокеты полок забирают товары, NPC встают на NavMesh
        if (!Bind()) { Finished = true; yield break; }
        ScenarioStarted = true;
        yield return Scenario();
        if (runPhysicsTests) yield return PhysicsTests();
        Line("");
        Line($"ИТОГ: пройдено {pass}, не пройдено {fail}");
        Line("");
        Line("Журнал событий (ProductInteractionHub, как в консоли):");
        foreach (var e in ProductInteractionHub.History) Line(string.Format(ci, "  t={0,6:0.00}  {1}", e.time, e));
        right.selectInput.bypass = null; left.selectInput.bypass = null;
        Finished = true;
    }

    bool Bind()
    {
        head = Camera.main != null ? Camera.main.transform : null;
        var nfs = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
        right = nfs.FirstOrDefault(n => n.handedness == InteractorHandedness.Right);
        left = nfs.FirstOrDefault(n => n.handedness == InteractorHandedness.Left);
        var b = FindFirstObjectByType<BodyAnchor>();
        basket = b != null ? b.transform : null;
        if (head == null || right == null || left == null || basket == null)
        { Line("ОШИБКА: не найдены камера / интеракторы / корзина. Сначала 7.0 или 7.1–7.4."); return false; }
        rightCtl = right.transform.parent; leftCtl = left.transform.parent;
        // позы задаёт скрипт: отключаем трекинг головы и контроллеров
        foreach (var t in new[] { head, rightCtl, leftCtl })
            foreach (var c in t.GetComponents<Behaviour>())
                if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        right.selectInput.bypass = rBtn; left.selectInput.bypass = lBtn;
        return true;
    }

    // ===================== сценарий =====================

    IEnumerator Scenario()
    {
        var grano = Item("Pack_GRANO");
        var volta = Item("Can_VOLTA");
        var lumen = Item("Bottle_LUMEN");
        var krisp = Item("Pack_KRISP");

        Line("СЦЕНАРИЙ (позы задаёт скрипт, захват — штатные интеракторы XRI). Стеллажи 03 (продвигаемый) и 04 (уценка), проход между рядами, взгляд на −z.");
        // 0. вход в проход, лицом к стеллажу
        yield return Move(1.2f, Pose(head, new Vector3(1.0f, 1.65f, -0.45f), new Vector3(1.0f, 1.15f, -1.6f)));
        basket.GetComponent<BodyAnchor>().SnapNow();
        yield return Move(0.6f, HandsRest());
        yield return new WaitForSeconds(0.4f);

        // 1. нижний ярус: наклониться, взять рукой (Near), положить в корзину
        Line("");
        Line("1. GRANO (коробка), нижний ярус 0.60 м: наклон, захват рукой за центр лицевой грани → корзина");
        var g = grano.Grip;
        yield return Move(1.0f, Pose(head, new Vector3(g.position.x - 0.15f, 1.28f, -0.92f), g.position),
            (rightCtl, g.position - g.forward * 0.25f + Vector3.up * 0.04f, g.rotation));
        yield return Move(0.5f, (rightCtl, g.position, g.rotation));
        rBtn.Set(true);
        yield return new WaitForSeconds(0.35f);
        Check("GRANO взята правой рукой", grano.HeldByHand && grano.HandHolder == (IXRSelectInteractor)right);
        var pick = LastOf(ProductEventType.Picked, "GRANO");
        Check("событие PICK: ярус 0, from=Shelf, via=Near", pick.HasValue && pick.Value.tier == 0 && pick.Value.from == ProductPlace.Shelf && pick.Value.region == GrabRegion.Near);
        if (pick.HasValue) Line(string.Format(ci, "   высота головы при взятии {0:0.00} м (стоя 1.65) → наклон {1:0.00} м; рука на высоте {2:0.00} м", pick.Value.headHeight, 1.65f - pick.Value.headHeight, pick.Value.handHeight));
        yield return Move(0.9f, Pose(head, new Vector3(g.position.x - 0.45f, 1.65f, -0.55f), new Vector3(g.position.x - 0.3f, 0.85f, -1.0f)),
            (rightCtl, rightCtl.position + new Vector3(-0.05f, 0.40f, 0.30f), rightCtl.rotation));
        yield return InBasket(grano, "GRANO", "05_Товар_в_корзине_socket");

        // 2. верхний ярус лучом (Far): притянуть, вернуть на место
        Line("");
        Line("2. VOLTA (банка), уровень глаз 1.60 м: луч с ~2 м → притянуть → вернуть на место");
        var vc = volta.Body.worldCenterOfMass;
        // луч должен пройти между полкой 1.60 и крышей стеллажа 1.80 — рука поднята почти к уровню глаз
        yield return Move(1.0f, HandsRestAt(new Vector3(vc.x + 0.07f, 1.65f, 0.45f), 180f).Append(Pose(head, new Vector3(vc.x + 0.07f, 1.65f, 0.45f), vc + Vector3.down * 0.15f)).ToArray());
        var hp = head.position + head.rotation * new Vector3(0.18f, -0.15f, 0.30f);
        yield return Move(0.8f, (rightCtl, hp, Quaternion.LookRotation(vc - hp)));
        yield return new WaitForSeconds(0.5f);
        Check(string.Format(ci, "луч наводится на VOLTA (расстояние рука—товар {0:0.00} м)", Vector3.Distance(hp, vc)), right.interactablesHovered.Any(h => h.transform == volta.transform));
        foreach (var h in Physics.RaycastAll(hp, (vc - hp).normalized, Vector3.Distance(hp, vc) + 0.2f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance))
            Line(string.Format(ci, "      луч пересекает: {0} (слой {1}, trigger={2}) на {3:0.00} м, точка y={4:0.00}", h.collider.name, LayerMask.LayerToName(h.collider.gameObject.layer), h.collider.isTrigger, h.distance, h.point.y));
        var far = right.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.CurveInteractionCaster>();
        if (far != null) Line(string.Format(ci, "      castOrigin={0} pos={1} fwd={2}; рука pos={3} fwd={4}", far.effectiveCastOrigin ? far.effectiveCastOrigin.name : "null", far.effectiveCastOrigin ? far.effectiveCastOrigin.position : Vector3.zero, far.effectiveCastOrigin ? far.effectiveCastOrigin.forward : Vector3.zero, rightCtl.position, rightCtl.forward));
        yield return Shoot("03_Луч_от_контроллера_к_верхнему_ярусу", hp + new Vector3(0.95f, 0.30f, 0.35f), Vector3.Lerp(hp, vc, 0.5f), 55f, false);
        yield return ShootPov("03b_Луч_вид_от_первого_лица");
        rBtn.Set(true);
        yield return new WaitForSeconds(0.9f);
        pick = LastOf(ProductEventType.Picked, "VOLTA");
        float d = Vector3.Distance(volta.Grip.position, right.transform.position);
        Check(string.Format(ci, "PICK: ярус 2, via=Far, расстояние при захвате {0:0.00} м", pick.HasValue ? pick.Value.distance : 0f), pick.HasValue && pick.Value.tier == 2 && pick.Value.region == GrabRegion.Far);
        Check(string.Format(ci, "товар притянут в руку: точка захвата — рука {0:0.000} м (< 0.10)", d), d < 0.10f);
        var home = volta.homeSlot.GetAttachTransform(volta.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>());
        var homePos = home.position; var homeRot = home.rotation;
        yield return Move(1.1f, Pose(head, new Vector3(homePos.x + 0.07f, 1.62f, -0.88f), homePos),
            (rightCtl, homePos + Vector3.forward * 0.22f, homeRot));
        yield return Move(0.6f, (rightCtl, homePos + Vector3.forward * 0.01f, homeRot));
        yield return Shoot("06_Призрак_ячейки_полки_перед_возвратом", homePos + new Vector3(0.40f, 0.12f, 0.50f), homePos, 40f);
        rBtn.Set(false);
        yield return new WaitForSeconds(0.7f);
        var rel = LastOf(ProductEventType.Released, "VOLTA");
        var pl = LastOf(ProductEventType.Placed, "VOLTA");
        Check("VOLTA возвращена: RELEASE → Returned, PLACE → Shelf (своё место)", rel.HasValue && rel.Value.outcome == ReleaseOutcome.Returned && pl.HasValue && pl.Value.place == ProductPlace.Shelf && pl.Value.ownSlot);
        yield return Move(0.6f, HandsRest());

        // 3. бутылка лучом с соседнего стеллажа: в руку за горлышко → корзина
        Line("");
        Line("3. LUMEN (бутылка), средний ярус 1.10 м, соседний стеллаж: луч → в руку за горлышко → корзина");
        var lc = lumen.Body.worldCenterOfMass;
        // заход фронтально: под углом луч упирается в боковую стенку между стеллажами 03 и 04
        yield return Move(1.0f, HandsRestAt(new Vector3(lc.x - 0.03f, 1.65f, 0.40f), 180f).Append(Pose(head, new Vector3(lc.x - 0.03f, 1.65f, 0.40f), lc)).ToArray());
        hp = head.position + head.rotation * new Vector3(0.18f, -0.25f, 0.30f);
        yield return Move(0.7f, (rightCtl, hp, Quaternion.LookRotation(lc - hp)));
        yield return new WaitForSeconds(0.4f);
        rBtn.Set(true);
        yield return new WaitForSeconds(0.9f);
        pick = LastOf(ProductEventType.Picked, "LUMEN");
        Check(string.Format(ci, "PICK LUMEN: ярус 1, via=Far, расстояние {0:0.00} м", pick.HasValue ? pick.Value.distance : 0f), pick.HasValue && pick.Value.tier == 1 && pick.Value.region == GrabRegion.Far);
        yield return Move(0.7f, (rightCtl, head.position + head.rotation * new Vector3(0.12f, -0.30f, 0.38f), head.rotation * Quaternion.Euler(-10f, 0f, 0f)));
        yield return new WaitForSeconds(0.3f);
        var lg = lumen.Grip.position;
        Check(string.Format(ci, "бутылка в руке за горлышко: точка захвата на {0:0%} высоты, рука — точка захвата {1:0.000} м", lumen.Grip.localPosition.y / lumen.GetComponent<CapsuleCollider>().height, Vector3.Distance(lg, right.transform.position)), Vector3.Distance(lg, right.transform.position) < 0.1f);
        yield return Shoot("04_Бутылка_в_руке_точка_захвата_на_горлышке", lg + head.rotation * new Vector3(0.35f, 0.05f, 0.25f), lg + Vector3.down * 0.06f, 40f);
        yield return Move(0.9f, Pose(head, new Vector3(0.85f, 1.65f, -0.5f), new Vector3(1.1f, 0.85f, -0.7f)));
        yield return InBasket(lumen, "LUMEN", null);

        // 4. бросок: средний ярус, KRISP — инерция руки должна сохраниться
        Line("");
        Line("4. KRISP (коробка), средний ярус 1.10 м: захват рукой → бросок вдоль прохода");
        g = krisp.Grip;
        yield return Move(1.0f, Pose(head, new Vector3(g.position.x - 0.13f, 1.60f, -0.85f), g.position),
            (rightCtl, g.position - g.forward * 0.25f, g.rotation));
        yield return Move(0.45f, (rightCtl, g.position, g.rotation));
        rBtn.Set(true);
        yield return new WaitForSeconds(0.3f);
        Check("KRISP взята", krisp.HeldByHand);
        yield return Move(0.9f, Pose(head, new Vector3(g.position.x - 0.3f, 1.65f, -0.6f), new Vector3(4.6f, 1.0f, -0.4f)),
            (rightCtl, new Vector3(g.position.x - 0.45f, 1.30f, -0.80f), Quaternion.LookRotation(Vector3.right)));
        var vHand = new Vector3(2.6f, 0.9f, 0.3f);
        float swing = 0.35f, t = 0f;
        var p0 = rightCtl.position;
        while (t < swing) { t += Time.deltaTime; rightCtl.position = p0 + vHand * t; yield return null; }
        var pRel = rightCtl.position;
        rBtn.Set(false);
        yield return new WaitForSeconds(0.45f);
        rel = LastOf(ProductEventType.Released, "KRISP");
        if (rel.HasValue)
        {
            float ratio = rel.Value.releaseSpeed / Mathf.Max(0.01f, vHand.magnitude);
            Line(string.Format(ci, "   скорость руки: задана {0:0.00} м/с, замер ProductItem {1:0.00} м/с; скорость товара после отпускания {2:0.00} м/с → сохранено {3:0%}",
                vHand.magnitude, rel.Value.handSpeed, rel.Value.releaseSpeed, ratio));
            Check("RELEASE → Thrown", rel.Value.outcome == ReleaseOutcome.Thrown);
            Check(string.Format(ci, "инерция руки сохранена: v_товара / v_руки = {0:0.00} (норма 0.85…1.15)", ratio), ratio > 0.85f && ratio < 1.15f);
        }
        else Check("RELEASE KRISP", false);
        float tw = 0f;
        while (tw < 3f && !LastOf(ProductEventType.LandedOnFloor, "KRISP").HasValue) { tw += Time.deltaTime; yield return null; }
        Check(string.Format(ci, "FLOOR: KRISP упал на пол через {0:0.00} с, в {1:0.00} м от точки отпускания", tw, Vector3.Distance(Flat(krisp.transform.position), Flat(pRel))), LastOf(ProductEventType.LandedOnFloor, "KRISP").HasValue);
        yield return Move(0.8f, HandsRestAt(new Vector3(1.2f, 1.65f, -0.6f), 90f).Append(Pose(head, new Vector3(1.2f, 1.65f, -0.6f), krisp.transform.position)).ToArray());
        yield return new WaitForSeconds(0.8f);
    }

    IEnumerator InBasket(ProductItem it, string id, string shot)
    {
        var top = basket.position + Vector3.up * 0.30f;
        var gripOff = it.Grip.position - it.transform.position;
        // с ЛР6 корзина вынесена вперёд, в нижнее поле зрения: руку сначала поднимаем над бортом, потом ведём к корзине
        var lift = rightCtl.position; lift.y = Mathf.Max(lift.y, top.y + gripOff.y);
        yield return Move(0.4f, (rightCtl, lift, rightCtl.rotation));
        yield return Move(0.9f, (rightCtl, top + gripOff, it.Grip.rotation));
        yield return new WaitForSeconds(0.25f);
        rBtn.Set(false);
        yield return new WaitForSeconds(0.7f);
        var rel = LastOf(ProductEventType.Released, id);
        var pl = LastOf(ProductEventType.Placed, id);
        Check(string.Format(ci, "{0} в корзине: RELEASE → Purchased (держали {1:0.00} с), PLACE → Basket slot {2}", id, rel.HasValue ? rel.Value.holdSeconds : 0f, pl.HasValue ? pl.Value.slot : 0),
            rel.HasValue && rel.Value.outcome == ReleaseOutcome.Purchased && pl.HasValue && pl.Value.place == ProductPlace.Basket);
        if (shot != null)
            yield return Shoot(shot, basket.position + basket.rotation * new Vector3(0.35f, 0.55f, -0.45f), basket.position + Vector3.up * 0.12f, 45f);
        yield return Move(0.6f, HandsRest());
    }

    // ===================== физика: массы, сопротивление, NPC =====================

    IEnumerator PhysicsTests()
    {
        Line("");
        Line("ФИЗИКА (без рук: товары снимаются с полок и запускаются с одинаковой скоростью)");
        var mgr = FindFirstObjectByType<XRInteractionManager>();
        var bottle = Item("Bottle_NUBO");
        var box = Item("Pack_NORDA");
        var can = Item("Can_ZEST");
        // на время теста сокеты выключены, иначе корзина или полка подхватят товар обратно
        foreach (var sk in FindObjectsByType<ProductSocket>(FindObjectsSortMode.None)) sk.socketActive = false;
        var items = new[] { bottle, can, box };
        var start = new Vector3(2.7f, 1.4f, 0.5f);
        var v0 = new Vector3(3.0f, 1.0f, 0f);
        var landX = new float[3]; var landT = new float[3]; var restX = new float[3];
        for (int i = 0; i < 3; i++)
        {
            var it = items[i];
            foreach (var s in it.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>().interactorsSelecting.ToList())
                mgr.SelectExit(s, it.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>());
            yield return new WaitForFixedUpdate();
            var rb = it.Body;
            rb.isKinematic = false;
            rb.position = start + new Vector3(0, 0, -0.40f * i); rb.rotation = Quaternion.identity;
            rb.linearVelocity = v0; rb.angularVelocity = Vector3.zero;
        }
        float tt = 0f; var landed = new bool[3];
        while (tt < 4f)
        {
            yield return new WaitForFixedUpdate();
            tt += Time.fixedDeltaTime;
            for (int i = 0; i < 3; i++)
                if (!landed[i] && items[i].Body.position.y < 0.25f && items[i].Body.linearVelocity.y <= 0.05f)
                { landed[i] = true; landX[i] = items[i].Body.position.x - start.x; landT[i] = tt; }
        }
        for (int i = 0; i < 3; i++) restX[i] = items[i].Body.position.x - start.x;
        Line("   старт: высота 1.40 м, скорость 3.0 м/с вперёд + 1.0 м/с вверх");
        Line("   товар        упаковка  масса    drag  angDrag  касание пола: дальность/время   остановился на");
        for (int i = 0; i < 3; i++)
        {
            var rb = items[i].Body;
            Line(string.Format(ci, "   {0,-12} {1,-8} {2,5:0.00} кг {3,5:0.00} {4,6:0.00}   {5,6:0.00} м / {6:0.00} с            {7,6:0.00} м",
                items[i].productId, items[i].packaging, rb.mass, rb.linearDamping, rb.angularDamping, landX[i], landT[i], restX[i]));
        }
        Check("картонная коробка (больше drag) улетает ближе бутылки", landX[2] < landX[0]);

        // NPC: товар не застревает в покупателе и не сбивает его с маршрута
        Line("");
        Line("NPC: товар, брошенный в покупателя");
        int lp = LayerMask.NameToLayer("Products"), ln = LayerMask.NameToLayer("NPC");
        Check("матрица слоёв: Products × NPC выключено", lp >= 0 && ln >= 0 && Physics.GetIgnoreLayerCollision(lp, ln));
        var agent = FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None).OrderBy(a => a.name).FirstOrDefault();
        if (agent != null)
        {
            var col = agent.GetComponent<CapsuleCollider>();
            Check("у покупателя есть капсульный коллайдер на слое NPC", col != null && agent.gameObject.layer == ln);
            var it = bottle;
            var rb = it.Body;
            var target = agent.transform.position + Vector3.up * 1.1f;
            var dir = Flat(target - Camera.main.transform.position).normalized;
            if (dir == Vector3.zero) dir = Vector3.forward;
            rb.position = target - dir * 0.8f; rb.linearVelocity = dir * 4f; rb.angularVelocity = Vector3.zero;
            var dest = agent.destination; var vel = agent.velocity;
            yield return new WaitForSeconds(0.45f);
            float along = Vector3.Dot(rb.position - target, dir);
            Check(string.Format(ci, "товар прошёл сквозь {0}: через 0.45 с он на {1:0.00} м дальше покупателя", agent.name, along), along > 0.4f);
            Check("маршрут покупателя не изменился (та же цель NavMeshAgent)", (agent.destination - dest).sqrMagnitude < 0.0001f);
        }
        yield return null;
    }

    // ===================== кадры =====================

    IEnumerator Shoot(string name, Vector3 pos, Vector3 look, float fov, bool hideNpc = true)
    {
        if (!takeShots || string.IsNullOrEmpty(OutDir)) yield break;
        yield return new WaitForEndOfFrame();
        // крупный план: покупатели не должны загораживать руку и товар
        var hidden = new List<Renderer>();
        if (hideNpc)
            foreach (var a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
                if (a.gameObject.layer == LayerMask.NameToLayer("NPC"))
                    foreach (var r in a.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
        const int W = 1600, H = 900;
        var go = new GameObject("~NS_Shot");
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f;
            RenderTo(cam, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Shots.Add(name);
        }
        finally { foreach (var r in hidden) if (r) r.enabled = true; Destroy(go); Destroy(tex); rt.Release(); Destroy(rt); }
    }

    IEnumerator ShootPov(string name)
    {
        yield return Shoot(name, head.position, head.position + head.forward, 70f, false);
    }

    static void RenderTo(Camera cam, RenderTexture rt)
    {
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
    }

    // ===================== вспомогательное =====================

    (Transform, Vector3, Quaternion)[] HandsRest() => HandsRestAt(head.position, head.eulerAngles.y);

    (Transform, Vector3, Quaternion)[] HandsRestAt(Vector3 headPos, float yaw)
    {
        var r = Quaternion.Euler(0f, yaw, 0f);
        var rot = r * Quaternion.Euler(20f, 0f, 0f);
        return new[]
        {
            (rightCtl, headPos + r * new Vector3(0.20f, -0.50f, 0.28f), rot),
            (leftCtl, headPos + r * new Vector3(-0.20f, -0.50f, 0.28f), rot),
        };
    }

    static (Transform, Vector3, Quaternion) Pose(Transform t, Vector3 pos, Vector3 look) =>
        (t, pos, Quaternion.LookRotation(look - pos, Vector3.up));

    IEnumerator Move(float dur, params (Transform t, Vector3 p, Quaternion r)[] targets) =>
        MoveAll(dur, targets);

    IEnumerator MoveAll(float dur, (Transform t, Vector3 p, Quaternion r)[] targets)
    {
        var p0 = targets.Select(x => x.t.position).ToArray();
        var r0 = targets.Select(x => x.t.rotation).ToArray();
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            for (int i = 0; i < targets.Length; i++)
                targets[i].t.SetPositionAndRotation(Vector3.Lerp(p0[i], targets[i].p, k), Quaternion.Slerp(r0[i], targets[i].r, k));
            yield return null;
        }
    }

    static ProductItem Item(string n)
    {
        var g = GameObject.Find(n);
        return g != null ? g.GetComponent<ProductItem>() : null;
    }

    static ProductInteractionEvent? LastOf(ProductEventType type, string id)
    {
        for (int i = ProductInteractionHub.History.Count - 1; i >= 0; i--)
        {
            var e = ProductInteractionHub.History[i];
            if (e.type == type && e.productId == id) return e;
        }
        return null;
    }

    void Check(string what, bool ok)
    {
        if (ok) pass++; else fail++;
        Line((ok ? "   [OK]   " : "   [FAIL] ") + what);
    }

    static void Line(string s) { Report.AppendLine(s); }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
