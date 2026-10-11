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
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Автоматическая проверка перемещения (шлема нет). Позы головы и контроллеров задаёт скрипт,
// а «стик» и «курок» подаются в штатные входы провайдеров XRI через bypass. Само перемещение,
// поворот, телепорт, Character Controller и виньетку выполняют штатные компоненты.
// Используется только из меню NEUROSHELF → Перемещение (8.6–8.8), в сцене не хранится.
public class LocomotionDemoDriver : MonoBehaviour
{
    public static string OutDir;
    public static bool Finished;
    public static readonly StringBuilder Report = new StringBuilder();
    public string mode = "check";   // check | fps | record
    public LocomotionProfileSO teleportProfile, smoothProfile, continuousProfile;

    class Stick : IXRInputValueReader<Vector2>
    {
        public Vector2 v;
        public Vector2 ReadValue() => v;
        public bool TryReadValue(out Vector2 value) { value = v; return v != Vector2.zero; }
    }
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

    LocomotionRig rig; XROrigin xr; Transform origin, head, rightCtl, offsetT; CharacterController cc;
    SpeedVignetteProvider vig; TunnelingVignetteController vigCtl; RouteLogger route;
    readonly Stick move = new Stick(), turn = new Stick(); readonly Button tp = new Button();
    readonly CultureInfo ci = CultureInfo.InvariantCulture;
    int pass, fail;
    const float EyeHeight = 1.65f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Finished = false; Report.Clear(); }

    IEnumerator Start()
    {
        Report.Clear(); Finished = false;
        rig = FindFirstObjectByType<LocomotionRig>();
        xr = FindFirstObjectByType<XROrigin>();
        head = Camera.main != null ? Camera.main.transform : null;
        if (rig == null || xr == null || head == null || rig.teleportInteractor == null)
        { Line("ОШИБКА: нет LocomotionRig / XR Origin / камеры / Teleport Interactor. Сначала меню 8.0."); Finished = true; yield break; }
        origin = xr.Origin.transform; offsetT = xr.CameraFloorOffsetObject.transform;
        cc = origin.GetComponent<CharacterController>();
        vig = rig.vignette; vigCtl = vig != null ? vig.controller : null; route = rig.route;
        rightCtl = rig.teleportInteractor.transform.parent;

        // 1. калибровка — до того как скрипт заберёт позы у трекинга (как видит пользователь)
        yield return new WaitForSeconds(1.0f);
        var camY0 = head.position.y;
        Line("КАЛИБРОВКА ВЫСОТЫ ГЛАЗ (Play Mode, трекинг XR Device Simulator включён)");
        Line(string.Format(ci, "   XR Origin: Camera Y Offset (поле) = {0:0.000} м, объект Camera Offset y = {1:0.000} м, режим {2}", xr.CameraYOffset, offsetT.localPosition.y, xr.CurrentTrackingOriginMode));
        Line(string.Format(ci, "   камера: мировая высота {0:0.000} м, локальная внутри Camera Offset {1}", camY0, V(head.localPosition)));
        if (cc != null) Line(string.Format(ci, "   Character Controller: radius {0:0.00} м, height {1:0.000} м, center {2}, skinWidth {3:0.00}, stepOffset {4:0.00}, слой {5}", cc.radius, cc.height, V(cc.center), cc.skinWidth, cc.stepOffset, LayerMask.LayerToName(origin.gameObject.layer)));
        Check(string.Format(ci, "камера на уровне глаз 1.65 м после добавления Character Controller ({0:0.000})", camY0), Mathf.Abs(camY0 - EyeHeight) < 0.01f);
        Check("поле Camera Y Offset не изменено (1.65)", Mathf.Abs(xr.CameraYOffset - EyeHeight) < 0.001f);

        Bind();
        if (mode == "fps") { yield return FpsWalk(); Finished = true; yield break; }
        if (mode == "record") { yield return RecordScenario(); Finished = true; yield break; }

        yield return SmoothTests();
        yield return VignetteThresholds();
        yield return ColliderTests();
        yield return TeleportTests();
        ValidatorTable();
        yield return RestFrameCheck();

        Line("");
        if (route != null)
        {
            route.FlushNow();
            Line("ЖУРНАЛ МАРШРУТА (RouteLogger, вся проверка одной сессией):");
            foreach (var e in RouteLogger.Events) Line("   " + e);
            if (!string.IsNullOrEmpty(RouteLogger.LastCsv) && OutDir != null)
            {
                var dst = Path.Combine(OutDir, "14_Маршрут_пример.csv");
                File.Copy(RouteLogger.LastCsv, dst, true);
                Line("   CSV: " + RouteLogger.LastCsv + "  → копия " + dst);
            }
        }
        Line("");
        Line($"ИТОГ: пройдено {pass}, не пройдено {fail}");
        Unbind();
        Finished = true;
    }

    // ===================== привязка =====================

    void Bind()
    {
        foreach (var t in new[] { head, rightCtl, rig.rightNearFar != null ? rig.rightNearFar.transform.parent : null })
            if (t != null) foreach (var c in t.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var lc = GameObject.Find("Left Controller");
        if (lc != null) foreach (var c in lc.GetComponents<Behaviour>()) if (c.GetType().Name == "TrackedPoseDriver") c.enabled = false;
        var sim = GameObject.Find("XR Device Simulator"); if (sim != null) sim.SetActive(false);
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity;
        if (rig.move != null) rig.move.leftHandMoveInput.bypass = move;
        if (rig.snapTurn != null) rig.snapTurn.rightHandTurnInput.bypass = turn;
        if (rig.continuousTurn != null) rig.continuousTurn.rightHandTurnInput.bypass = turn;
        rig.teleportInteractor.selectInput.bypass = tp;
    }

    void Unbind()
    {
        if (rig.move != null) rig.move.leftHandMoveInput.bypass = null;
        if (rig.snapTurn != null) rig.snapTurn.rightHandTurnInput.bypass = null;
        if (rig.continuousTurn != null) rig.continuousTurn.rightHandTurnInput.bypass = null;
        rig.teleportInteractor.selectInput.bypass = null;
    }

    void Place(float x, float z, float yaw)
    {
        origin.SetPositionAndRotation(new Vector3(x, origin.position.y, z), Quaternion.Euler(0f, yaw, 0f));
        head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity;
        Physics.SyncTransforms();
        rig.basket?.GetComponent<BodyAnchor>()?.SnapNow();
        HandsRest();
    }

    void HandsRest()
    {
        rightCtl.position = head.position + head.rotation * new Vector3(0.22f, -0.50f, 0.30f);
        rightCtl.rotation = Quaternion.Euler(30f, head.eulerAngles.y, 0f);
    }

    // ===================== 2. плавное перемещение =====================

    IEnumerator SmoothTests()
    {
        Line("");
        Line("ПЛАВНОЕ ПЕРЕМЕЩЕНИЕ (профиль " + smoothProfile.profileName + ")");
        rig.SetProfile(smoothProfile);
        Place(-2.2f, 0f, 90f);
        yield return new WaitForSeconds(0.5f);
        move.v = new Vector2(0f, 1f);
        float x0 = 0f, t0 = 0f, yMin = 9f, yMax = 0f, zDev = 0f; bool shot1 = false; float shotX = 0f;
        for (float t = 0; t < 3.6f; t += Time.deltaTime)
        {
            if (t >= 0.5f && t0 == 0f) { x0 = head.position.x; t0 = Time.time; }
            yMin = Mathf.Min(yMin, head.position.y); yMax = Mathf.Max(yMax, head.position.y);
            zDev = Mathf.Max(zDev, Mathf.Abs(head.position.z));
            if (t > 2.0f && !shot1) { shot1 = true; shotX = head.position.x; yield return ShootPov("06_Виньетка_на_ходу_1.4_мс"); }
            yield return null;
        }
        float v = (head.position.x - x0) / (Time.time - t0);
        float apMove = vig != null ? vig.aperture : 1f;
        move.v = Vector2.zero;
        Line(string.Format(ci, "   стик вперёд 3.6 с по проходу между рядами (z = 0): измеренная скорость {0:0.000} м/с", v));
        Check(string.Format(ci, "скорость шага 1.40 м/с ±3% ({0:0.00})", v), Mathf.Abs(v - 1.4f) < 0.042f);
        Check(string.Format(ci, "проход 2.4 м пройден насквозь, отклонение от оси {0:0.00} м (NPC обходятся)", zDev), head.position.x > 2.0f);
        Check(string.Format(ci, "высота камеры на ходу {0:0.000}…{1:0.000} м (не съехала)", yMin, yMax), Mathf.Abs(yMin - EyeHeight) < 0.01f && Mathf.Abs(yMax - EyeHeight) < 0.01f);
        if (cc != null) Line(string.Format(ci, "   Character Controller после движения: height {0:0.000} м (= высота камеры над полом), center {1}", cc.height, V(cc.center)));
        Check(string.Format(ci, "виньетка на ходу 1.4 м/с: апертура {0:0.00} (< 1 — сужение есть)", apMove), apMove < 0.95f);
        yield return new WaitForSeconds(1.2f);
        float apRest = vig != null ? vig.aperture : 1f;
        float shader = vigCtl != null ? vigCtl.currentParameters.apertureSize : 1f;
        Check(string.Format(ci, "в покое через 1.2 с виньетка исчезает полностью: апертура {0:0.00}, в шейдере {1:0.00}", apRest, shader), apRest >= 0.999f && shader >= 0.999f);
        Place(shotX, 0f, 90f);          // та же точка, что и кадр 06 на ходу
        yield return new WaitForSeconds(0.8f);
        yield return ShootPov("05_Виньетка_в_покое");
    }

    // ===================== 3. пороги виньетки =====================

    IEnumerator VignetteThresholds()
    {
        if (vig == null) yield break;
        Line("");
        Line(string.Format(ci, "ПОРОГИ ВИНЬЕТКИ (линейная {0:0.00}…{1:0.00} м/с, угловая {2:0}…{3:0} °/с, апертура 1 → {4:0.00})",
            vig.linearSpeedRange.x, vig.linearSpeedRange.y, vig.angularSpeedRange.x, vig.angularSpeedRange.y, vig.minAperture));
        Line("   скорость стика | измерено   | интенсивность | апертура | ожидалось");
        foreach (var s in new[] { 0.05f, 0.10f, 0.35f, 0.70f, 1.05f, 1.40f, 2.00f })
        {
            rig.move.moveSpeed = s;
            Place(-2.2f, 0f, 90f);
            yield return new WaitForSeconds(0.3f);
            move.v = new Vector2(0f, 1f);
            yield return new WaitForSeconds(1.2f);
            float x1 = head.position.x, t1 = Time.time; float ap = 0f, it = 0f; int n = 0;
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { ap += vig.aperture; it += vig.intensity; n++; yield return null; }
            float meas = (head.position.x - x1) / (Time.time - t1);
            move.v = Vector2.zero;
            ap /= n; it /= n;
            float exp = vig.IntensityFor(s, 0f);
            Line(string.Format(ci, "   {0,5:0.00} м/с     | {1,5:0.00} м/с | {2,13:0.00} | {3,8:0.00} | {4:0.00}", s, meas, it, ap, Mathf.Lerp(1f, vig.minAperture, exp)));
            if (s <= 0.10f) Check(string.Format(ci, "при {0:0.00} м/с виньетки нет", s), it < 0.02f);
            if (s >= 1.40f) Check(string.Format(ci, "при {0:0.00} м/с виньетка полная (апертура {1:0.00})", s, ap), ap <= vig.minAperture + 0.02f);
            yield return new WaitForSeconds(0.6f);
        }
        rig.SetProfile(continuousProfile);
        Line("   скорость поворота | измерено    | интенсивность | апертура");
        foreach (var w in new[] { 5f, 10f, 30f, 60f, 90f, 120f })
        {
            rig.continuousTurn.turnSpeed = w;
            Place(0.5f, 0f, 90f);
            yield return new WaitForSeconds(0.3f);
            turn.v = new Vector2(1f, 0f);
            yield return new WaitForSeconds(1.0f);
            float y1 = origin.eulerAngles.y, t1 = Time.time; float ap = 0f, it = 0f; int n = 0;
            for (float t = 0; t < 0.5f; t += Time.deltaTime) { ap += vig.aperture; it += vig.intensity; n++; yield return null; }
            float meas = Mathf.Abs(Mathf.DeltaAngle(y1, origin.eulerAngles.y)) / (Time.time - t1);
            turn.v = Vector2.zero;
            ap /= n; it /= n;
            Line(string.Format(ci, "   {0,6:0} °/с        | {1,6:0.0} °/с | {2,13:0.00} | {3,8:0.00}", w, meas, it, ap));
            if (w <= 10f) Check(string.Format(ci, "поворот {0:0} °/с — виньетки нет", w), it < 0.05f);
            if (w >= 90f) Check(string.Format(ci, "поворот {0:0} °/с — виньетка полная", w), ap <= vig.minAperture + 0.02f);
            if (Mathf.Approximately(w, 60f)) yield return ShootPov("06b_Виньетка_при_плавном_повороте_60");
            yield return new WaitForSeconds(0.6f);
        }
        rig.SetProfile(smoothProfile);
        Place(0.5f, 0f, 90f);
        yield return new WaitForSeconds(0.6f);
        int j0 = vig.jumpsIgnored; float yawBefore = origin.eulerAngles.y, maxIt = 0f;
        turn.v = new Vector2(1f, 0f);
        for (float t = 0; t < 0.12f; t += Time.deltaTime) { maxIt = Mathf.Max(maxIt, vig.intensity); yield return null; }
        turn.v = Vector2.zero;
        for (float t = 0; t < 0.6f; t += Time.deltaTime) { maxIt = Mathf.Max(maxIt, vig.intensity); yield return null; }
        float snapped = Mathf.DeltaAngle(yawBefore, origin.eulerAngles.y);
        Check(string.Format(ci, "Snap Turn {0:0}° за один кадр — виньетка не включается (макс. интенсивность {1:0.00}, скачков отфильтровано {2})", snapped, maxIt, vig.jumpsIgnored - j0),
            Mathf.Abs(Mathf.Abs(snapped) - smoothProfile.snapAngle) < 1f && maxIt < 0.05f);
        rig.move.moveSpeed = smoothProfile.moveSpeed;
    }

    // ===================== 4. Character Controller =====================

    IEnumerator ColliderTests()
    {
        if (cc == null) yield break;
        Line("");
        Line("CHARACTER CONTROLLER: стеллаж Shelf_04 (у Shelf_03 по условию A стоит толпа NPC), лицевая кромка полок z = −1.20 (проход 2.4 м: z от −1.2 до +1.2)");
        rig.SetProfile(smoothProfile);
        var aura = GameObject.Find("Pack_NORDA");
        var ap = aura != null ? aura.GetComponent<ProductItem>() : null;
        float px = ap != null ? ap.Grip.position.x : 1.2f;
        Place(px, -0.3f, 180f);
        yield return new WaitForSeconds(0.4f);
        move.v = new Vector2(0f, 1f);
        yield return new WaitForSeconds(2.0f);
        move.v = Vector2.zero;
        yield return new WaitForSeconds(0.4f);
        float gap = head.position.z - (-1.2f);
        Line(string.Format(ci, "   стик вперёд 2 с на полку: ось тела остановилась в {0:0.000} м от кромки (radius {1:0.00} + skinWidth {2:0.00} = {3:0.00})", gap, cc.radius, cc.skinWidth, cc.radius + cc.skinWidth));
        Check("игрок не проходит в стеллаж: ось тела перед кромкой на ≥ radius", gap >= cc.radius - 0.01f && gap < cc.radius + cc.skinWidth + 0.06f);
        Check(string.Format(ci, "высота камеры у стеллажа {0:0.000} м", head.position.y), Mathf.Abs(head.position.y - EyeHeight) < 0.01f);
        if (ap != null)
        {
            var g = ap.Grip.position;
            var sh = head.position + Vector3.down * 0.25f + head.right * 0.18f;   // правое плечо ≈ 0.25 м ниже глаз, 0.18 м вбок
            float reach = Vector3.Distance(sh, g);
            Line(string.Format(ci, "   нижний ярус 0.60 м (NORDA, точка захвата y = {0:0.00}): от правого плеча до неё {1:0.00} м по прямой, по горизонтали {2:0.00} м", g.y, reach, Mathf.Abs(sh.z - g.z)));
            ReachWithCC = reach;
        }
        yield return ShootThird("04_Character_Controller_габариты_у_стеллажа", head.position + new Vector3(1.75f, 0.35f, 0.55f), new Vector3(head.position.x - 0.1f, 0.85f, -1.15f), 52f, true, true);
        float basketFront = BasketMinZ();
        Line(string.Format(ci, "   корзина у стеллажа: передний край z = {0:0.00} (кромка −1.20), отодвинута на {1:0.00} м", basketFront, rig.basket != null ? rig.basket.retracted : 0f));
        Check("корзина не входит в стеллаж", basketFront >= -1.2f - 0.01f);

        // без коллайдера
        cc.enabled = false;
        move.v = new Vector2(0f, 1f);
        yield return new WaitForSeconds(0.5f);
        move.v = Vector2.zero;
        yield return null;
        float gapNo = head.position.z - (-1.2f);
        if (ap != null) ReachNoCC = Vector3.Distance(head.position + Vector3.down * 0.25f + head.right * 0.18f, ap.Grip.position);
        Line(string.Format(ci, "   тот же стик 0.5 с с выключенным Character Controller: голова в {0:0.00} м {1} кромки — камера внутри стеллажа; до NORDA от плеча {2:0.00} м",
            Mathf.Abs(gapNo), gapNo < 0 ? "за" : "перед", ReachNoCC));
        yield return new WaitForSeconds(0.8f);   // виньетка гаснет — видно, где оказалась камера
        yield return ShootPov("04b_Без_коллайдера_камера_внутри_стеллажа");
        Check("без коллайдера игрок въезжает в стеллаж (контроль: эффект даёт именно коллайдер)", gapNo < 0f);
        cc.enabled = true;
        Place(px, -0.3f, 180f);
        yield return new WaitForSeconds(0.3f);
    }
    float ReachWithCC, ReachNoCC;

    float BasketMinZ()
    {
        if (rig.basket == null) return 0f;
        var rs = rig.basket.GetComponentsInChildren<Renderer>();
        return rs.Length == 0 ? 0f : rs.Min(r => r.bounds.min.z);
    }

    // ===================== 5. телепорт =====================

    IEnumerator TeleportTests()
    {
        Line("");
        Line("ТЕЛЕПОРТ (профиль " + teleportProfile.profileName + ")");
        rig.SetProfile(teleportProfile);
        var ti = rig.teleportInteractor;
        float h = 1.15f;
        float range = LocomotionRig.CurveRange(ti.velocity, ti.acceleration, h, out var best);
        Line(string.Format(ci, "   кривая Projectile: v = {0:0.0} м/с, a = {1:0.0} м/с²; максимальная дальность с высоты руки {2:0.00} м = {3:0.00} м (при наклоне {4:0}°); длина зала 12 м, ширина 8 м",
            ti.velocity, ti.acceleration, h, range, best));
        Check(string.Format(ci, "дальность кривой {0:0.0} м не превышает длину зала 12 м", range), range < 12f);
        float rDefault = LocomotionRig.CurveRange(10f, 9.8f, h, out _), rGame = LocomotionRig.CurveRange(16f, 9.8f, h, out _);
        Line(string.Format(ci, "   для сравнения: v = 10 (префаб Starter Assets) → {0:0.0} м, v = 16 (значение по умолчанию XRRayInteractor) → {1:0.0} м", rDefault, rGame));

        Place(4.0f, 0.2f, -90f);
        yield return new WaitForSeconds(0.4f);

        // a) валидная цель на полу прохода
        var target = new Vector3(-0.4f, 0f, 0.0f);
        yield return Aim(target);
        bool ok = TargetState(out var hit, out var what);
        Line(string.Format(ci, "   a) цель на полу прохода {0}: луч попал в {1} → {2}", V(target), what, ok ? "валидна" : "НЕвалидна"));
        Check("кривая к полу прохода — валидная цель", ok);
        yield return ShootThird("01_Кривая_телепорта_валидная_цель", new Vector3(1.9f, 2.85f, -1.05f), new Vector3(1.5f, 0.55f, 0.35f), 68f, true);
        yield return ShootPov("01b_Кривая_телепорта_вид_от_первого_лица");
        var before = head.position;
        yield return Release();
        var d = head.position - target; d.y = 0;
        Check(string.Format(ci, "телепорт выполнен: голова в {0:0.00} м от точки прицела", d.magnitude), d.magnitude < 0.15f);
        Check(string.Format(ci, "после телепорта камера на {0:0.000} м", head.position.y), Mathf.Abs(head.position.y - EyeHeight) < 0.01f);
        Line(string.Format(ci, "   перенос: {0:0.00} м за 1 кадр", Vector3.Distance(new Vector3(before.x, 0, before.z), new Vector3(head.position.x, 0, head.position.z))));

        // b) кривая упирается в стеллаж
        Place(4.0f, 0.2f, -90f);
        yield return new WaitForSeconds(0.3f);
        yield return Aim(new Vector3(1.6f, 1.05f, 1.25f));
        ok = TargetState(out hit, out what);
        Line(string.Format(ci, "   b) прицел в средний ярус Shelf_02: луч попал в {0} → {1}", what, ok ? "валидна" : "заблокирована"));
        Check("кривая, упёршаяся в стеллаж, — невалидная цель", !ok);
        yield return ShootThird("02_Кривая_телепорта_невалидная_упёрлась_в_стеллаж", new Vector3(3.4f, 2.6f, -1.9f), new Vector3(2.2f, 0.9f, 0.9f), 55f, true);
        yield return Release();
        Check("с невалидной цели телепорт не выполняется", Vector3.Distance(head.position, new Vector3(4f, head.position.y, 0.2f)) < 0.05f);

        // c) пол вплотную к стеллажу: коллайдер пола есть, NavMesh — нет
        int triesC = 0;
        do
        {
            yield return Aim(new Vector3(1.2f, 0f, -1.12f));
            ok = TargetState(out hit, out what);
            if (hit.collider == null || hit.collider.name != "Floor")
            { Line("   кривую перекрыл проходящий " + what + " (это тоже отказ) — повтор через 1 с, нужен именно пол"); tp.Set(false); rig.SetTeleportAim(false); yield return new WaitForSeconds(1f); }
        } while ((hit.collider == null || hit.collider.name != "Floor") && ++triesC < 6);
        Line(string.Format(ci, "   c) пол в 0.08 м от кромки Shelf_04: луч попал в {0} ({1}), проверка NavMesh: {2} → {3}", what, V(hit.point), TeleportTargetValidator.Check(hit.point), ok ? "валидна" : "заблокирована"));
        Check("пол у основания стеллажа отклонён по NavMesh", !ok && hit.collider != null && hit.collider.name == "Floor");
        yield return ShootThird("02b_Кривая_у_основания_стеллажа_отказ_NavMesh", new Vector3(3.3f, 2.4f, 0.9f), new Vector3(1.6f, 0.3f, -1.0f), 50f, true);
        yield return Release();

        // d) якорь у дальнего стеллажа
        var anchors = FindObjectsByType<ShelfTeleportationAnchor>(FindObjectsSortMode.None);
        var a1 = anchors.FirstOrDefault(a => a.anchorId == "Shelf_03");
        if (a1 != null)
        {
            // якорь у продвигаемого стеллажа: места NPC (0.55 м от полок) с ним не пересекаются,
            // но проходящий покупатель может временно занять точку — тогда ждём, как ждал бы человек
            int tries = 0;
            do
            {
                yield return Aim(a1.transform.position);
                ok = TargetState(out hit, out what);
                if (!ok) { Line("   якорь Shelf_03 временно занят проходящим NPC — повтор через 1 с"); tp.Set(false); rig.SetTeleportAim(false); yield return new WaitForSeconds(1f); }
            } while (!ok && ++tries < 6);
            Line(string.Format(ci, "   d) прицел в якорь Shelf_03: луч попал в {0} {1}, якорь занят NPC: {2}", what, V(hit.point), TeleportTargetValidator.Occupied(a1.transform.position)));
            Check("якорь Shelf_03 (продвигаемый стеллаж, 5.2 м) — валидная цель", ok);
            yield return Release();
            var p = a1.teleportAnchorTransform.position; var dd = head.position - p; dd.y = 0;
            float yawErr = Mathf.Abs(Mathf.DeltaAngle(head.eulerAngles.y, a1.teleportAnchorTransform.eulerAngles.y));
            Check(string.Format(ci, "после телепорта на якорь: позиция совпала ({0:0.000} м), взгляд на стеллаж (отклонение {1:0.0}°)", dd.magnitude, yawErr), dd.magnitude < 0.05f && yawErr < 1f);
            yield return ShootPov("03_Вид_с_якоря_Shelf_03_от_первого_лица");
        }
        Line("   якоря в Play Mode (позиция · направление взгляда · проверка правилом проходимости):");
        foreach (var a in anchors.OrderBy(a => a.anchorId))
        {
            var t = a.teleportAnchorTransform;
            Line(string.Format(ci, "      {0,-9} {1}  yaw {2,4:0}°  {3}", a.anchorId, V(t.position), t.eulerAngles.y, TeleportTargetValidator.Check(t.position)));
        }
        Place(0f, 0f, 0f);
        yield return ShootThird("02c_Якоря_телепорта_вид_сверху", new Vector3(0.6f, 9.0f, -4.6f), new Vector3(0.6f, 0f, -0.3f), 60f, false);
    }

    IEnumerator Aim(Vector3 target)
    {
        rig.SetTeleportAim(true);
        var ti = rig.teleportInteractor;
        var ro = ti.rayOriginTransform != null ? ti.rayOriginTransform : ti.transform;
        for (int it = 0; it < 4; it++)
        {
            var s = ro.position;
            var flat = target - s; float dy = flat.y; flat.y = 0; float dx = flat.magnitude;
            float v = ti.velocity, g = ti.acceleration;
            float disc = v * v * v * v - g * (g * dx * dx + 2f * dy * v * v);
            float th = disc >= 0 ? Mathf.Atan((v * v - Mathf.Sqrt(disc)) / (g * dx)) : Mathf.PI / 4f;
            var want = Quaternion.LookRotation(flat.normalized) * Quaternion.Euler(-th * Mathf.Rad2Deg, 0f, 0f);
            var delta = Quaternion.Inverse(rightCtl.rotation) * ro.rotation;
            rightCtl.rotation = want * Quaternion.Inverse(delta);
            yield return null;
        }
        yield return new WaitForSeconds(0.25f);
        tp.Set(true);
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator Release()
    {
        tp.Set(false);
        rig.SetTeleportAim(false);
        yield return new WaitForSeconds(0.3f);
        HandsRest();
    }

    bool TargetState(out RaycastHit hit, out string what)
    {
        var ti = rig.teleportInteractor;
        what = "—";
        if (!ti.TryGetCurrent3DRaycastHit(out hit) || hit.collider == null) return false;
        what = hit.collider.name;
        if (!ti.interactionManager.TryGetInteractableForCollider(hit.collider, out var inter)) return false;
        return inter is BaseTeleportationInteractable bt && bt.IsSelectableBy((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)ti);
    }

    // ===================== 6. правило проходимости =====================

    void ValidatorTable()
    {
        Line("");
        Line("ПРАВИЛО ПРОХОДИМОСТИ ТЕЛЕПОРТА (NavMesh из ЛР4 + капсулы NPC) — контрольные точки:");
        Place(4.0f, 0.2f, -90f);   // игрок в стороне: его NavMeshObstacle (ЛР4) вырезает NavMesh вокруг себя
        Physics.SyncTransforms();
        var npc = FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.layer == TeleportTargetValidator.LayerNPC && c.name.Contains("Shopper"));
        var pts = new List<(string, Vector3, TeleportVerdict)>
        {
            ("центр прохода между рядами", new Vector3(0f, 0f, 0f), TeleportVerdict.Ok),
            ("пол у кассы, со стороны зала", new Vector3(4.5f, 0f, -0.9f), TeleportVerdict.Ok),
            ("пол в 0.10 м от кромки стеллажа", new Vector3(-1.2f, 0f, -1.1f), TeleportVerdict.OffNavMesh),
            ("верх стеллажа Shelf_03 (1.80 м)", new Vector3(-1.2f, 1.8f, -1.5f), TeleportVerdict.OffNavMesh),
            ("внутри прилавка кассы", new Vector3(4.5f, 0f, -2.0f), TeleportVerdict.OffNavMesh),
            ("в 0.10 м от стены", new Vector3(0f, 0f, 3.9f), TeleportVerdict.OffNavMesh),
        };
        if (npc != null)
        {
            var np = npc.transform.position; np.y = 0;
            pts.Add(("место, где стоит " + npc.name, np, TeleportVerdict.OccupiedByNpc));
            var toAisle = new Vector3(0f, 0f, -Mathf.Sign(np.z)) * 0.40f;   // в сторону середины прохода, на NavMesh
            pts.Add(("в 0.40 м от " + npc.name + " (капсулы 0.25 + 0.25 пересекаются)", np + toAisle, TeleportVerdict.OccupiedByNpc));
        }
        foreach (var (n, p, exp) in pts)
        {
            Physics.SyncTransforms();   // NPC двигает NavMeshAgent в Update — подтягиваем позиции капсул в физику
            var r = TeleportTargetValidator.Check(p);
            Line(string.Format(ci, "   {0,-58} {1} → {2}", n, V(p), r));
            Check($"{n}: ожидалось {exp}", r == exp);
        }
    }

    // ===================== 7. корзина как rest frame =====================

    IEnumerator RestFrameCheck()
    {
        Line("");
        Line("КОРЗИНА КАК ФИКСИРОВАННАЯ ТОЧКА ОТСЧЁТА");
        rig.SetProfile(smoothProfile);
        Place(-2.2f, 0f, 90f);
        yield return new WaitForSeconds(0.8f);
        var b = rig.basket.transform;
        var e = head.position;
        // угол ниже горизонта для точки на верхнем крае корзины (0.42 × 0.30 × 0.22 м из ЛР5)
        float Below(Vector3 local) { var p = b.TransformPoint(local); var f = p - e; float hz = new Vector2(f.x, f.z).magnitude; return Mathf.Atan2(-f.y, hz) * Mathf.Rad2Deg; }
        float near = Mathf.Max(Below(new Vector3(-0.21f, 0.22f, -0.15f)), Below(new Vector3(0.21f, 0.22f, -0.15f)));
        float far = Mathf.Min(Below(new Vector3(-0.21f, 0.22f, 0.15f)), Below(new Vector3(0.21f, 0.22f, 0.15f)));
        var oldPos = new Vector3(-0.32f, 0f, 0.12f); float oldRim = 0.85f + 0.22f;
        float oldAng = Mathf.Atan2(e.y - oldRim, new Vector2(oldPos.x, oldPos.z).magnitude) * Mathf.Rad2Deg;
        Line(string.Format(ci, "   взгляд прямо (голова 1.65 м): край корзины на {0:0.0}° (ближний) … {1:0.0}° (дальний) ниже горизонта", near, far));
        Line(string.Format(ci, "   было в ЛР5 (0.32 м влево, 0.12 м вперёд, край на 1.07 м): ≈ {0:0.0}° ниже горизонта", oldAng));
        Check("дальний край корзины в нижнем поле зрения при взгляде прямо (выше −45°)", far < 45f);
        // на ходу корзина неподвижна относительно головы
        var rel0 = Quaternion.Inverse(origin.rotation) * (b.position - head.position);
        move.v = new Vector2(0f, 1f);
        float maxDev = 0f; bool shot2 = false;
        var eof = new WaitForEndOfFrame();   // замер после LateUpdate: корзина уже поставлена в этом кадре
        for (float t = 0; t < 2.0f; t += Time.deltaTime)
        {
            yield return eof;
            if (t > 0.3f) maxDev = Mathf.Max(maxDev, (Quaternion.Inverse(origin.rotation) * (b.position - head.position) - rel0).magnitude);
            if (t > 1.2f && !shot2) { shot2 = true; yield return ShootPov("07_Корзина_в_нижнем_поле_зрения_на_ходу"); }
        }
        move.v = Vector2.zero;
        Check(string.Format(ci, "на ходу 1.4 м/с корзина неподвижна относительно головы: смещение ≤ {0:0.000} м", maxDev), maxDev < 0.02f);
        Line(string.Format(ci, "   (с исходным BodyAnchor запаздывание составило бы ≈ v / followSpeed = 1.4 / 10 = 0.14 м)"));
        Place(1.2f, -0.2f, 180f);   // точка якоря Shelf_04
        yield return new WaitForSeconds(1.0f);
        yield return ShootPov("08_Вид_от_первого_лица_1.65_м_с_якоря_Shelf_04", true);
        Line(string.Format(ci, "   кадр 08: камера на {0:0.000} м, точка якоря Shelf_04 (1.0 м от кромки полок); NPC на кадре скрыты", head.position.y));
    }

    // ===================== режимы fps / record =====================

    IEnumerator FpsWalk()
    {
        rig.SetProfile(smoothProfile);
        float dir = 1f;
        Place(-2.2f, 0f, 90f);
        for (float t = 0; t < 15f; t += 2.6f)
        {
            move.v = new Vector2(0f, 1f);
            yield return new WaitForSeconds(2.6f);
            dir = -dir;
            Place(dir > 0 ? -2.2f : 2.2f, 0f, dir > 0 ? 90f : -90f);
        }
        move.v = Vector2.zero;
    }

    IEnumerator RecordScenario()
    {
        Camera.main.fieldOfView = PovFov;   // в видео — поле зрения шлема, а не 60° окна редактора
        // 1. телепорт к дальнему стеллажу
        rig.SetProfile(teleportProfile);
        Place(4.6f, 0.2f, -90f);
        head.localRotation = Quaternion.Euler(12f, 0f, 0f);
        yield return new WaitForSeconds(1.5f);
        var a1 = FindObjectsByType<ShelfTeleportationAnchor>(FindObjectsSortMode.None).FirstOrDefault(a => a.anchorId == "Shelf_03");
        if (a1 != null)
        {
            yield return Aim(a1.transform.position);
            yield return new WaitForSeconds(1.8f);
            yield return Release();
            head.localRotation = Quaternion.Euler(15f, 0f, 0f);
            yield return new WaitForSeconds(2.5f);
        }
        // 2. плавное перемещение по проходу с виньеткой: два Snap Turn влево (лицом вдоль прохода на восток), затем шаг 1.4 м/с
        rig.SetProfile(smoothProfile);
        turn.v = new Vector2(-1f, 0f); yield return new WaitForSeconds(0.1f); turn.v = Vector2.zero;   // Snap 45°
        yield return new WaitForSeconds(0.8f);
        turn.v = new Vector2(-1f, 0f); yield return new WaitForSeconds(0.1f); turn.v = Vector2.zero;   // ещё 45° — вдоль прохода
        yield return new WaitForSeconds(0.8f);
        head.localRotation = Quaternion.Euler(8f, 0f, 0f);
        move.v = new Vector2(0f, 1f);
        for (float t = 0; t < 3.8f; t += Time.deltaTime)
        {
            head.localRotation = Quaternion.Euler(8f, Mathf.Sin(t * 2.2f) * 12f, 0f);   // покупатель поглядывает на полки
            yield return null;
        }
        move.v = Vector2.zero;
        yield return new WaitForSeconds(2.0f);
    }

    // ===================== кадры =====================

    const int ShotW = 1600, ShotH = 900;
    public const float PovFov = 90f;   // вертикальное поле зрения кадров «от первого лица», °

    IEnumerator ShootPov(string file, bool hideNpc = false)
    {
        if (OutDir == null || mode != "check") yield break;
        yield return new WaitForEndOfFrame();
        var hidden = new List<Renderer>();
        if (hideNpc) { var npcRoot = GameObject.Find("NPC"); if (npcRoot != null) foreach (var r in npcRoot.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); } }
        Render(Camera.main, file, null);
        foreach (var r in hidden) if (r) r.enabled = true;
    }

    IEnumerator ShootThird(string file, Vector3 pos, Vector3 look, float fov, bool ghost, bool hideNpc = false)
    {
        if (OutDir == null || mode != "check") yield break;
        yield return new WaitForEndOfFrame();
        var temp = new List<GameObject>();
        var hidden = new List<Renderer>();
        if (vigCtl != null) foreach (var r in vigCtl.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); }
        if (hideNpc) { var npcRoot = GameObject.Find("NPC"); if (npcRoot != null) foreach (var r in npcRoot.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; hidden.Add(r); } }
        if (ghost && cc != null)
        {
            var c = origin.TransformPoint(cc.center);
            temp.Add(Prim(PrimitiveType.Capsule, c, Quaternion.identity, new Vector3(cc.radius * 2, cc.height * 0.5f, cc.radius * 2), new Color(0.1f, 1f, 0.4f, 0.30f)));
            temp.Add(Prim(PrimitiveType.Sphere, head.position, head.rotation, Vector3.one * 0.12f, new Color(1f, 0.85f, 0f, 0.9f)));
            temp.Add(Prim(PrimitiveType.Cube, head.position + head.forward * 0.18f, head.rotation, new Vector3(0.02f, 0.02f, 0.25f), new Color(0.15f, 0.45f, 1f, 0.9f)));
        }
        var go = new GameObject("~NS_Shot");
        try
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f;
            Render(cam, file, null);
        }
        finally
        {
            Destroy(go);
            foreach (var t in temp) Destroy(t);
            foreach (var r in hidden) if (r) r.enabled = true;
        }
    }

    void Render(Camera cam, string file, RenderTexture _)
    {
        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        float fov0 = cam.fieldOfView;
        try
        {
            if (cam == Camera.main) cam.fieldOfView = PovFov;   // поле зрения как у шлема, а не 60° окна редактора
            var data = cam.GetUniversalAdditionalCameraData();
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.aspect = (float)ShotW / ShotH;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { var prevT = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prevT; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
            Line("   [кадр] " + file + ".png");
        }
        catch (Exception ex) { Line("   [кадр не снят] " + file + ": " + ex.Message); }
        finally { cam.ResetAspect(); cam.fieldOfView = fov0; Destroy(tex); rt.Release(); Destroy(rt); }
    }

    GameObject Prim(PrimitiveType t, Vector3 pos, Quaternion rot, Vector3 scale, Color c)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetPositionAndRotation(pos, rot); g.transform.localScale = scale;
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        if (c.a < 0.99f)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
        }
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        return g;
    }

    // ===================== протокол =====================

    void Line(string s) { Report.AppendLine(s); }
    void Check(string what, bool ok) { if (ok) pass++; else fail++; Line((ok ? "   [OK]   " : "   [FAIL] ") + what); }
    string V(Vector3 v) => string.Format(ci, "({0:0.00}; {1:0.00}; {2:0.00})", v.x, v.y, v.z);
}
