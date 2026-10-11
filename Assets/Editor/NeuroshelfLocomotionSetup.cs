// NEUROSHELF — перемещение пользователя: телепорт и плавное движение как параметр эксперимента,
// борьба с укачиванием (динамическая виньетка, корзина как rest frame), коллайдер игрока, запись маршрута.
// Отдельный файл; остальные скрипты Editor не трогает.
//
// Меню NEUROSHELF → Перемещение:
//   8.0 Полный цикл        → 8.1 … 8.5 + протокол настройки (обычно нажимать только её), затем 8.6
//   8.1 Профили            → LocomotionProfileSO: Teleport_Snap, Smooth_Snap, Smooth_Continuous
//   8.2 Слои и коллайдер   → слои PlayerBody / Basket / Teleport, матрица, Character Controller на XR Origin
//   8.3 Провайдеры         → Locomotion Mediator, XR Body Transformer, Continuous Move, Snap/Continuous Turn, Teleportation
//   8.4 Телепорт           → Teleport Interactor (Projectile), зона на полу с проверкой NavMesh, 5 якорей
//   8.5 Комфорт и запись   → Tunneling Vignette + SpeedVignetteProvider, корзина-rest frame, RouteLogger
//   8.6 Проверка в Play Mode (~70 с) → 10_Проверка_перемещения_Play_Mode.txt + кадры
//   8.7 Замер FPS при плавном движении с виньеткой (15 с)
//   8.8 Запись Unity Recorder (~20 с) → Recordings/ns_locomotion.mp4
//   8.91–8.93 Активный профиль сцены: Teleport / Smooth+Snap / Smooth+Continuous
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР6\

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.AI.Navigation;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NeuroshelfLocomotionSetup
{
    // ===================== ПАРАМЕТРЫ =====================

    const string Menu = "NEUROSHELF/Перемещение/";
    const string SA = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/";
    const string InputActionsPath = SA + "XRI Default Input Actions.inputactions";
    const string ProfileDir = "Assets/_Project/Configs/Locomotion";
    const string MatDir = "Assets/_Project/Materials/Locomotion";

    public const int LayerProducts = 8, LayerNPC = 9, LayerPlayer = 10, LayerBasket = 11, LayerTeleport = 12;
    const int InteractionLayerTeleport = 31;      // «Teleport» в настройках XRI Interaction Layers

    // Character Controller: радиус = радиус капсулы NPC из ЛР5 (0.25 м) ≈ половина ширины плеч взрослого.
    const float PlayerRadius = 0.25f, SkinWidth = 0.03f, StepOffset = 0.10f, EyeHeight = 1.65f;
    // Якорь — в 1.0 м от кромки полок: с этой точки в поле зрения все три яруса 0.60 / 1.10 / 1.60 м;
    // чтобы взять товар, нужен реальный шаг вперёд. И мимо мест NPC у полок (0.55 м от кромки) — не перекрываются.
    const float AnchorDistance = 1.0f;

    public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР6"));
    static string RecDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "8.0 Полный цикл (профили → коллайдер → провайдеры → телепорт → комфорт → протокол)", priority = 400)]
    public static void All()
    {
        Profiles(); LayersAndBody(); Providers(); Teleport(); Comfort();
        Save();
        Verify();
        Debug.Log("[NEUROSHELF] Перемещение настроено. Дальше: 8.6 (проверка в Play Mode).");
    }
    [MenuItem(Menu + "8.1 Профили перемещения (ScriptableObject)", priority = 401)] static void M1() { Profiles(); Save(); }
    [MenuItem(Menu + "8.2 Слои и Character Controller", priority = 402)] static void M2() { LayersAndBody(); Save(); }
    [MenuItem(Menu + "8.3 Провайдеры перемещения и поворота", priority = 403)] static void M3() { Providers(); Save(); }
    [MenuItem(Menu + "8.4 Телепорт: кривая, зона, якоря", priority = 404)] static void M4() { Teleport(); Save(); }
    [MenuItem(Menu + "8.5 Комфорт: виньетка, корзина, логгер маршрута", priority = 405)] static void M5() { Comfort(); Save(); }
    [MenuItem(Menu + "8.6 Проверка в Play Mode (~70 с)", priority = 420)] static void M6() => StartPlay("check");
    [MenuItem(Menu + "8.7 Замер FPS при движении (15 с)", priority = 421)] static void M7() => StartPlay("fps");
    [MenuItem(Menu + "8.8 Запись Unity Recorder (~20 с)", priority = 422)] static void M8() => StartPlay("record");
    [MenuItem(Menu + "8.91 Профиль сцены: Телепорт + Snap Turn", priority = 440)] static void P1() => UseProfile("LP_Teleport_Snap");
    [MenuItem(Menu + "8.92 Профиль сцены: Плавное + Snap Turn", priority = 441)] static void P2() => UseProfile("LP_Smooth_Snap");
    [MenuItem(Menu + "8.93 Профиль сцены: Плавное + Continuous Turn", priority = 442)] static void P3() => UseProfile("LP_Smooth_Continuous");

    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static void Save()
    {
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
    }

    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        t = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(t.gameObject, name);
        t.SetParent(parent, false);
        return t;
    }

    static XROrigin Origin() => Object.FindFirstObjectByType<XROrigin>();
    static InputActionReference ActionRef(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(InputActionsPath).OfType<InputActionReference>().FirstOrDefault(r => r.name == name);
    static LocomotionProfileSO LoadProfile(string n) => AssetDatabase.LoadAssetAtPath<LocomotionProfileSO>($"{ProfileDir}/{n}.asset");

    // ===================== 8.1 ПРОФИЛИ =====================

    static void Profiles()
    {
        Directory.CreateDirectory(ProfileDir);
        LocomotionProfileSO Make(string file, Action<LocomotionProfileSO> set)
        {
            var path = $"{ProfileDir}/{file}.asset";
            var p = AssetDatabase.LoadAssetAtPath<LocomotionProfileSO>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<LocomotionProfileSO>(); AssetDatabase.CreateAsset(p, path); }
            // общие для всех профилей параметры комфорта (значения по умолчанию из LocomotionProfileSO)
            p.moveSpeed = 1.4f; p.snapAngle = 45f; p.continuousTurnSpeed = 60f;
            p.curveVelocity = 8.5f; p.curveAcceleration = 9.8f; p.freeFloorTeleport = true;
            p.vignette = true; p.vignetteStrength = 1f; p.minAperture = 0.7f; p.vignetteVerticalShift = -0.12f;
            p.linearSpeedRange = new Vector2(0.10f, 1.40f); p.angularSpeedRange = new Vector2(10f, 90f); p.vignetteSmoothing = 0.15f;
            p.basketOffset = new Vector2(-0.22f, 0.42f); p.basketHeight = 0.95f; p.logInterval = 0.2f;
            set(p);
            EditorUtility.SetDirty(p);
            return p;
        }
        Make("LP_Teleport_Snap", p =>
        {
            p.profileName = "Teleport_Snap"; p.mode = LocomotionMode.Teleport; p.turn = TurnMode.Snap;
            p.description = "Основной режим стенда. Телепорт: на пол (с проверкой NavMesh) или на якорь перед стеллажом. " +
                            "Расстояние между точками не проходится — пишется отдельно как teleport_m. Поворот — Snap 45°.";
        });
        Make("LP_Smooth_Snap", p =>
        {
            p.profileName = "Smooth_Snap"; p.mode = LocomotionMode.Smooth; p.turn = TurnMode.Snap;
            p.description = "Плавное движение 1.4 м/с (скорость шага). Пройденные метры реальны — «цена доступа» к дальнему стеллажу сохраняется. " +
                            "Риск укачивания снижается динамической виньеткой и корзиной в нижнем поле зрения. Поворот — Snap 45°.";
        });
        Make("LP_Smooth_Continuous", p =>
        {
            p.profileName = "Smooth_Continuous"; p.mode = LocomotionMode.Smooth; p.turn = TurnMode.Continuous;
            p.description = "Как Smooth_Snap, но плавный поворот 60 °/с: естественнее, сильнее провоцирует укачивание — виньетка реагирует и на поворот.";
        });
        AssetDatabase.SaveAssets();
        Debug.Log("[NEUROSHELF] 8.1 Профили перемещения → " + ProfileDir);
    }

    static void UseProfile(string file)
    {
        var rig = Object.FindFirstObjectByType<LocomotionRig>();
        var p = LoadProfile(file);
        if (rig == null || p == null) { Debug.LogError("[NEUROSHELF] Нет LocomotionRig или профиля — сначала 8.0"); return; }
        Undo.RecordObject(rig, "profile");
        rig.profile = p;
        EditorUtility.SetDirty(rig);
        if (EditorApplication.isPlaying) rig.Apply(); else Save();
        Debug.Log("[NEUROSHELF] Активный профиль перемещения: " + p.profileName);
    }

    // ===================== 8.2 СЛОИ И CHARACTER CONTROLLER =====================

    static void LayersAndBody()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        void SetLayer(int i, string n)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue) || p.stringValue == n) p.stringValue = n;
            else Debug.LogWarning($"[NEUROSHELF] Слой {i} занят «{p.stringValue}», ожидался «{n}».");
        }
        SetLayer(LayerPlayer, "PlayerBody");
        SetLayer(LayerBasket, "Basket");
        SetLayer(LayerTeleport, "Teleport");
        tm.ApplyModifiedPropertiesWithoutUndo();

        // Тело игрока: стены, стеллажи, прилавок (Default) и NPC — препятствия.
        // Товары — нет: товар в руке не должен толкать собственное тело, лежащий на полу — не стена.
        Physics.IgnoreLayerCollision(LayerPlayer, LayerProducts, true);
        Physics.IgnoreLayerCollision(LayerPlayer, LayerBasket, true);     // своя корзина не блокирует шаг
        Physics.IgnoreLayerCollision(LayerPlayer, LayerTeleport, true);   // диски якорей на полу — не ступенька
        Physics.IgnoreLayerCollision(LayerPlayer, LayerNPC, false);       // в NPC не проходим насквозь
        Physics.IgnoreLayerCollision(LayerBasket, LayerNPC, true);        // как товар × NPC в ЛР5
        Physics.IgnoreLayerCollision(LayerBasket, LayerProducts, false);  // товар лежит в корзине
        Physics.IgnoreLayerCollision(LayerTeleport, LayerProducts, false);
        Physics.IgnoreLayerCollision(LayerTeleport, LayerNPC, true);

        var xr = Origin();
        if (xr == null) { Debug.LogError("[NEUROSHELF] Нет XR Origin"); return; }
        var go = xr.Origin;
        Undo.RecordObject(go, "layer");
        go.layer = LayerPlayer;                          // только корневой объект: дочерние (руки, камера) не трогаем
        var cc = Ensure<CharacterController>(go);
        cc.radius = PlayerRadius;
        cc.skinWidth = SkinWidth;
        cc.stepOffset = StepOffset;
        cc.slopeLimit = 45f;
        cc.minMoveDistance = 0f;
        cc.height = EyeHeight;                           // в Play Mode высоту подгоняет XR Body Transformer по камере
        cc.center = new Vector3(0f, EyeHeight * 0.5f + SkinWidth, 0f);
        EditorUtility.SetDirty(cc);

        // корзина — свой слой
        var basket = GameObject.Find("Interaction/Basket");
        if (basket != null)
        {
            foreach (var t in basket.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer == 0) { Undo.RecordObject(t.gameObject, "layer"); t.gameObject.layer = LayerBasket; }
        }
        // NavMesh при возможном перезапекании не должен видеть тело игрока, корзину и диски якорей
        foreach (var s in Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(s, "NavMesh mask");
            s.layerMask &= ~((1 << LayerPlayer) | (1 << LayerBasket) | (1 << LayerTeleport));
        }
        Debug.Log($"[NEUROSHELF] 8.2 Слои PlayerBody=10, Basket=11, Teleport=12; Character Controller r={PlayerRadius} на {go.name}");
    }

    // ===================== 8.3 ПРОВАЙДЕРЫ =====================

    static void Providers()
    {
        var xr = Origin(); if (xr == null) return;
        var loc = Child(xr.transform, "Locomotion");
        Ensure<LocomotionMediator>(loc.gameObject);
        var body = Ensure<XRBodyTransformer>(loc.gameObject);
        body.xrOrigin = xr;
        body.useCharacterControllerIfExists = true;      // XRI 3.x: вместо устаревшего CharacterControllerDriver
        EditorUtility.SetDirty(body);

        var cam = Camera.main != null ? Camera.main.transform : null;
        var moveGo = Child(loc, "Move");
        var move = Ensure<ContinuousMoveProvider>(moveGo.gameObject);
        move.moveSpeed = 1.4f;
        move.enableStrafe = true;
        move.enableFly = false;
        move.forwardSource = cam;                         // «вперёд» = куда смотрит голова
        move.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        move.leftHandMoveInput.inputActionReference = ActionRef("XRI Left Locomotion/Move");
        move.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        EditorUtility.SetDirty(move);

        var turnGo = Child(loc, "Turn");
        var snap = Ensure<SnapTurnProvider>(turnGo.gameObject);
        snap.turnAmount = 45f;
        snap.enableTurnAround = false;
        snap.rightHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        snap.rightHandTurnInput.inputActionReference = ActionRef("XRI Right Locomotion/Snap Turn");
        snap.leftHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        EditorUtility.SetDirty(snap);
        var cont = Ensure<ContinuousTurnProvider>(turnGo.gameObject);
        cont.turnSpeed = 60f;
        cont.rightHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        cont.rightHandTurnInput.inputActionReference = ActionRef("XRI Right Locomotion/Turn");
        cont.leftHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
        EditorUtility.SetDirty(cont);

        var tpGo = Child(loc, "Teleportation");
        var tp = Ensure<TeleportationProvider>(tpGo.gameObject);
        tp.delayTime = 0f;
        EditorUtility.SetDirty(tp);

        var rig = Ensure<LocomotionRig>(loc.gameObject);
        rig.move = move; rig.snapTurn = snap; rig.continuousTurn = cont; rig.teleportation = tp;
        if (rig.profile == null) rig.profile = LoadProfile("LP_Teleport_Snap");
        EditorUtility.SetDirty(rig);
        Debug.Log("[NEUROSHELF] 8.3 Провайдеры: Locomotion Mediator, XR Body Transformer (Character Controller), Continuous Move 1.4 м/с, Snap 45° / Continuous 60°/с, Teleportation");
    }

    // ===================== 8.4 ТЕЛЕПОРТ =====================

    static Material Mat(string name, Color c)
    {
        Directory.CreateDirectory(MatDir);
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        if (c.a < 0.99f)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Teleport()
    {
        var xr = Origin(); if (xr == null) return;
        var rig = Object.FindFirstObjectByType<LocomotionRig>();
        if (rig == null) { Providers(); rig = Object.FindFirstObjectByType<LocomotionRig>(); }
        var right = GameObject.Find("XR Origin (VR)/Camera Offset/Right Controller");
        if (right == null) { Debug.LogError("[NEUROSHELF] Нет Right Controller — сначала меню 7.3 (ЛР5)"); return; }

        // Teleport Interactor из Starter Assets: XR Ray Interactor с кривой Projectile
        var ti = right.GetComponentsInChildren<XRRayInteractor>(true).FirstOrDefault(r => r.name == "Teleport Interactor");
        if (ti == null)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(SA + "Prefabs/Interactors/Teleport Interactor.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, right.transform);
            Undo.RegisterCreatedObjectUndo(inst, "teleport interactor");
            inst.name = "Teleport Interactor";
            ti = inst.GetComponent<XRRayInteractor>();
        }
        ti.lineType = XRRayInteractor.LineType.ProjectileCurve;
        ti.velocity = 8.5f;          // дальность ≈ 8.4 м < длины зала 12 м (расчёт — в протоколе)
        ti.acceleration = 9.8f;
        ti.additionalGroundHeight = 0.1f;
        ti.additionalFlightTime = 0.5f;
        ti.raycastMask = (1 << 0) | (1 << LayerProducts) | (1 << LayerNPC) | (1 << LayerTeleport);   // стеллаж, товар, NPC — блокируют
        ti.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        ti.interactionLayers = 1 << InteractionLayerTeleport;
        ti.handedness = InteractorHandedness.Right;
        var tm = ActionRef("XRI Right Locomotion/Teleport Mode");
        ti.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
        ti.selectInput.inputActionReferencePerformed = tm;
        ti.selectInput.inputActionReferenceValue = tm;
        ti.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
        EditorUtility.SetDirty(ti);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ti);
        ti.gameObject.SetActive(false);   // включается, только пока стик правой руки отклонён вперёд

        rig.teleportInteractor = ti;
        rig.rightNearFar = right.GetComponentInChildren<NearFarInteractor>(true);
        rig.teleportMode = tm;
        rig.teleportCancel = ActionRef("XRI Right Locomotion/Teleport Mode Cancel");
        EditorUtility.SetDirty(rig);

        // зона телепорта = пол (стеллажи, прилавок, витрина — отдельные коллайдеры, в зону не входят)
        var floor = GameObject.Find("Floor");
        var area = Ensure<NavMeshTeleportationArea>(floor);
        area.interactionLayers = 1 << InteractionLayerTeleport;
        area.teleportationProvider = rig.teleportation;
        area.matchOrientation = MatchOrientation.WorldSpaceUp;
        area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        area.filterSelectionByHitNormal = true;
        EditorUtility.SetDirty(area);

        // якоря
        // якоря — объекты зала, а не игрока: в корне сцены (под XR Origin они ехали бы вместе с ним)
        var rootGo = GameObject.Find("TeleportAnchors");
        if (rootGo == null) { rootGo = new GameObject("TeleportAnchors"); Undo.RegisterCreatedObjectUndo(rootGo, "anchors"); }
        var root = rootGo.transform;
        if (root.parent != null) Undo.SetTransformParent(root, null, "anchors");
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var mDisc = Mat("M_TeleportAnchor", new Color(0.10f, 0.75f, 1.00f, 0.45f));
        var mArrow = Mat("M_TeleportAnchor_Arrow", new Color(0.10f, 0.75f, 1.00f, 0.85f));
        var defs = new List<(string id, Vector3 pos, float yaw)>();
        foreach (var n in new[] { "Shelf_01", "Shelf_02", "Shelf_03", "Shelf_04" })
        {
            var s = GameObject.Find(n); if (s == null) continue;
            var rs = s.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var front = ShopperAgent.ShelfFront(s.transform); front.y = 0; front.Normalize();
            float frontEdge = Vector3.Dot(b.center, front) + Vector3.Dot(b.extents, new Vector3(Mathf.Abs(front.x), 0, Mathf.Abs(front.z)));
            var c = new Vector3(b.center.x, 0f, b.center.z);
            var p = c + front * (frontEdge - Vector3.Dot(c, front) + AnchorDistance);
            defs.Add((n, p, Quaternion.LookRotation(-front).eulerAngles.y));
        }
        var co = GameObject.Find("Checkout_Counter") ?? GameObject.Find("Checkout");
        if (co != null)
        {
            var rs = co.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            defs.Add(("Checkout", new Vector3(b.center.x, 0f, b.max.z + 0.70f), 180f));   // со стороны зала, лицом к прилавку
        }
        float floorY = floor.GetComponent<Collider>().bounds.max.y;
        foreach (var (id, pos, yaw) in defs)
        {
            var a = Child(root, "Anchor_" + id);
            a.gameObject.layer = LayerTeleport;
            a.SetPositionAndRotation(new Vector3(pos.x, floorY, pos.z), Quaternion.Euler(0f, yaw, 0f));
            var bc = Ensure<BoxCollider>(a.gameObject);
            bc.center = new Vector3(0f, 0.006f, 0f); bc.size = new Vector3(0.36f, 0.012f, 0.36f);
            var anc = Ensure<ShelfTeleportationAnchor>(a.gameObject);
            anc.anchorId = id;
            anc.teleportAnchorTransform = a;
            anc.interactionLayers = 1 << InteractionLayerTeleport;
            anc.teleportationProvider = rig.teleportation;
            anc.matchOrientation = MatchOrientation.TargetUpAndForward;    // приземление лицом к стеллажу
            anc.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
            EditorUtility.SetDirty(anc);
            Visual(a, "Disc", PrimitiveType.Cylinder, new Vector3(0f, 0.004f, 0f), new Vector3(0.36f, 0.004f, 0.36f), mDisc);
            Visual(a, "Arrow", PrimitiveType.Cube, new Vector3(0f, 0.009f, 0.10f), new Vector3(0.05f, 0.004f, 0.16f), mArrow);
            Visual(a, "ArrowTip", PrimitiveType.Cube, new Vector3(0f, 0.009f, 0.19f), new Vector3(0.10f, 0.004f, 0.04f), mArrow);
        }
        Debug.Log($"[NEUROSHELF] 8.4 Телепорт: Teleport Interactor (Projectile v=8.5, a=9.8), зона — пол с проверкой NavMesh, якорей {defs.Count}");
    }

    static void Visual(Transform parent, string name, PrimitiveType t, Vector3 lp, Vector3 ls, Material m)
    {
        var v = parent.Find(name);
        if (v == null)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name;
            Undo.RegisterCreatedObjectUndo(g, name);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            v = g.transform; v.SetParent(parent, false);
        }
        v.gameObject.layer = LayerTeleport;
        v.localPosition = lp; v.localRotation = Quaternion.identity; v.localScale = ls;
        var r = v.GetComponent<MeshRenderer>();
        r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(v.gameObject, 0);
    }

    // ===================== 8.5 КОМФОРТ И ЗАПИСЬ =====================

    static void Comfort()
    {
        var xr = Origin(); if (xr == null) return;
        var rig = Object.FindFirstObjectByType<LocomotionRig>();
        if (rig == null) { Providers(); rig = Object.FindFirstObjectByType<LocomotionRig>(); }
        var cam = Camera.main;

        // Tunneling Vignette из Starter Assets — дочерний объект камеры
        var vc = cam.GetComponentInChildren<TunnelingVignetteController>(true);
        if (vc == null)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(SA + "TunnelingVignette/TunnelingVignette.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, cam.transform);
            Undo.RegisterCreatedObjectUndo(inst, "vignette");
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            vc = inst.GetComponent<TunnelingVignetteController>();
        }
        vc.locomotionVignetteProviders.Clear();    // штатные «вкл/выкл» провайдеры не нужны — интенсивность задаёт SpeedVignetteProvider
        EditorUtility.SetDirty(vc);
        PrefabUtility.RecordPrefabInstancePropertyModifications(vc);

        var sv = Ensure<SpeedVignetteProvider>(rig.gameObject);
        sv.controller = vc; sv.origin = xr.transform;
        EditorUtility.SetDirty(sv);
        rig.vignette = sv;

        // корзина ЛР5 — rest frame
        var basket = GameObject.Find("Interaction/Basket");
        if (basket != null)
        {
            var rf = Ensure<BasketRestFrame>(basket);
            rf.head = cam.transform; rf.origin = xr.transform; rf.bodyAnchor = basket.GetComponent<BodyAnchor>();
            rf.obstacles = 1 << 0;
            var p = rig.profile;
            if (p != null) rf.Configure(p.basketOffset, p.basketHeight);
            EditorUtility.SetDirty(rf);
            if (rf.bodyAnchor != null) { EditorUtility.SetDirty(rf.bodyAnchor); rf.bodyAnchor.SnapNow(); }
            rig.basket = rf;
        }

        var route = Ensure<RouteLogger>(rig.gameObject);
        route.head = cam.transform; route.origin = xr.transform; route.profile = rig.profile;
        route.zones = new List<RouteLogger.Zone>
        {
            Zone("Shelf_01", -1.2f, 0.6f, 2.4f, 1.2f),
            Zone("Shelf_02", 1.2f, 0.6f, 2.4f, 1.2f),
            Zone("Shelf_03", -1.2f, -0.6f, 2.4f, 1.2f),
            Zone("Shelf_04", 1.2f, -0.6f, 2.4f, 1.2f),
            Zone("Checkout", 4.5f, -0.95f, 2.4f, 1.3f),
        };
        EditorUtility.SetDirty(route);
        rig.route = route;
        EditorUtility.SetDirty(rig);
        Debug.Log("[NEUROSHELF] 8.5 Комфорт: Tunneling Vignette + SpeedVignetteProvider, корзина-rest frame, RouteLogger (зон " + route.zones.Count + ")");
    }

    static RouteLogger.Zone Zone(string id, float x, float z, float sx, float sz) =>
        new RouteLogger.Zone { id = id, center = new Vector3(x, 0f, z), size = new Vector2(sx, sz) };

    // ===================== ПРОТОКОЛ НАСТРОЙКИ =====================

    static void Verify()
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        sb.AppendLine("NEUROSHELF — протокол настройки перемещения " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        sb.AppendLine();

        // 1. имена классов в установленном пакете
        sb.AppendLine("1. XR Interaction Toolkit: классы перемещения в установленной версии (рефлексия по сборке пакета)");
        var asm = typeof(XRBodyTransformer).Assembly;
        sb.AppendLine("   сборка: " + asm.GetName().Name + " " + asm.GetName().Version + " · пакет " + PkgVersion("com.unity.xr.interaction.toolkit"));
        foreach (var n in new[]
        {
            "UnityEngine.XR.Interaction.Toolkit.LocomotionSystem",
            "UnityEngine.XR.Interaction.Toolkit.ActionBasedContinuousMoveProvider",
            "UnityEngine.XR.Interaction.Toolkit.ActionBasedSnapTurnProvider",
            "UnityEngine.XR.Interaction.Toolkit.ActionBasedContinuousTurnProvider",
            "UnityEngine.XR.Interaction.Toolkit.CharacterControllerDriver",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionMediator",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.CharacterControllerBodyManipulator",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.ContinuousTurnProvider",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor",
            "UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort.TunnelingVignetteController",
        })
        {
            var t = asm.GetType(n);
            var ob = t?.GetCustomAttribute<ObsoleteAttribute>();
            sb.AppendLine(string.Format("   {0,-30} {1}", n.Split('.').Last(), t == null ? "нет в сборке" : ob != null ? "УСТАРЕЛ: " + ob.Message : "доступен"));
        }
        sb.AppendLine("   Tunneling Vignette (образец): " + (File.Exists(SA + "TunnelingVignette/TunnelingVignette.prefab") ? "импортирован в Starter Assets/TunnelingVignette" : "НЕ импортирован — Package Manager → XR Interaction Toolkit → Samples → Starter Assets → Import"));
        sb.AppendLine();

        // 2. XR Origin
        var xr = Origin();
        var cc = xr != null ? xr.Origin.GetComponent<CharacterController>() : null;
        var body = Object.FindFirstObjectByType<XRBodyTransformer>();
        sb.AppendLine("2. XR Origin и тело игрока");
        sb.AppendLine(string.Format(ci, "   Camera Y Offset = {0:0.000} м (не менялось), объект Camera Offset y = {1:0.000} м", xr.CameraYOffset, xr.CameraFloorOffsetObject.transform.localPosition.y));
        if (cc != null) sb.AppendLine(string.Format(ci, "   Character Controller: radius {0:0.00}, height {1:0.00}, center {2}, skinWidth {3:0.00}, stepOffset {4:0.00}, слой {5}",
            cc.radius, cc.height, cc.center, cc.skinWidth, cc.stepOffset, LayerMask.LayerToName(xr.Origin.layer)));
        sb.AppendLine("   XR Body Transformer: useCharacterControllerIfExists = " + (body != null && body.useCharacterControllerIfExists));
        sb.AppendLine(string.Format(ci, "   проход между рядами 2.4 м / диаметр тела {0:0.00} м = запас {1:0.00} м с каждой стороны", PlayerRadius * 2, (2.4f - PlayerRadius * 2) / 2));
        sb.AppendLine("   матрица: PlayerBody × Default/NPC — сталкиваются; × Products/Basket/Teleport — нет");
        sb.AppendLine();

        // 3. провайдеры и профили
        var rig = Object.FindFirstObjectByType<LocomotionRig>();
        sb.AppendLine("3. Провайдеры и профиль");
        if (rig != null)
        {
            sb.AppendLine(string.Format(ci, "   ContinuousMoveProvider: {0:0.00} м/с, strafe {1}, ввод {2}", rig.move.moveSpeed, rig.move.enableStrafe, rig.move.leftHandMoveInput.inputActionReference?.name));
            sb.AppendLine(string.Format(ci, "   SnapTurnProvider: {0:0}°, ввод {1}", rig.snapTurn.turnAmount, rig.snapTurn.rightHandTurnInput.inputActionReference?.name));
            sb.AppendLine(string.Format(ci, "   ContinuousTurnProvider: {0:0} °/с, ввод {1}", rig.continuousTurn.turnSpeed, rig.continuousTurn.rightHandTurnInput.inputActionReference?.name));
            sb.AppendLine("   активный профиль сцены: " + rig.profile?.name);
        }
        foreach (var f in new[] { "LP_Teleport_Snap", "LP_Smooth_Snap", "LP_Smooth_Continuous" })
        {
            var p = LoadProfile(f); if (p == null) continue;
            sb.AppendLine(string.Format(ci, "   {0,-22} режим {1,-8} скорость {2:0.00}  поворот {3,-10} snap {4:0}° / {5:0} °/с  виньетка {6} (апертура до {7:0.00}, {8:0.00}…{9:0.00} м/с, {10:0}…{11:0} °/с)",
                f, p.mode, p.moveSpeed, p.turn, p.snapAngle, p.continuousTurnSpeed, p.vignette ? "вкл" : "выкл", p.minAperture, p.linearSpeedRange.x, p.linearSpeedRange.y, p.angularSpeedRange.x, p.angularSpeedRange.y));
        }
        sb.AppendLine();

        // 4. телепорт
        sb.AppendLine("4. Телепорт");
        if (rig != null && rig.teleportInteractor != null)
        {
            var ti = rig.teleportInteractor;
            float range = LocomotionRig.CurveRange(ti.velocity, ti.acceleration, 1.15f, out var ang);
            sb.AppendLine(string.Format(ci, "   Teleport Interactor ({0}): {1}, v = {2:0.0} м/с, a = {3:0.0} м/с², маска лучей: {4}", HPath(ti.transform), ti.lineType, ti.velocity, ti.acceleration, MaskNames(ti.raycastMask)));
            sb.AppendLine(string.Format(ci, "   дальность кривой с высоты руки 1.15 м: {0:0.00} м при наклоне {1:0}° (зал 12 × 8 м). Для сравнения v = 10 → {2:0.00} м, v = 16 → {3:0.00} м",
                range, ang, LocomotionRig.CurveRange(10f, 9.8f, 1.15f, out _), LocomotionRig.CurveRange(16f, 9.8f, 1.15f, out _)));
        }
        var area = Object.FindFirstObjectByType<NavMeshTeleportationArea>();
        sb.AppendLine("   зона: " + (area != null ? HPath(area.transform) + " (NavMeshTeleportationArea: проверка NavMesh ЛР4 + капсулы NPC)" : "нет"));
        sb.AppendLine(string.Format(ci, "   NavMesh: агент Humanoid, радиус {0:0.00} м; допуск выборки {1:0.00} м", NavMesh.GetSettingsByIndex(0).agentRadius, TeleportTargetValidator.NavMeshTolerance));
        var tri = NavMesh.CalculateTriangulation();
        sb.AppendLine(string.Format(ci, "   треугольников NavMesh в сцене: {0}", tri.indices.Length / 3));
        sb.AppendLine("   якоря (позиция · направление взгляда · проверка NavMesh):");
        foreach (var a in Object.FindObjectsByType<ShelfTeleportationAnchor>(FindObjectsSortMode.None).OrderBy(a => a.anchorId))
        {
            var p = a.transform.position;
            bool on = NavMesh.SamplePosition(p, out var h, 0.2f, NavMesh.AllAreas) && new Vector2(h.position.x - p.x, h.position.z - p.z).magnitude <= TeleportTargetValidator.NavMeshTolerance;
            sb.AppendLine(string.Format(ci, "      {0,-9} ({1:0.00}; {2:0.00}; {3:0.00})  yaw {4,4:0}°  {5}", a.anchorId, p.x, p.y, p.z, a.transform.eulerAngles.y, on ? "на NavMesh" : "ВНЕ NavMesh"));
        }
        sb.AppendLine();

        // 5. комфорт
        sb.AppendLine("5. Комфорт");
        var vc = Camera.main.GetComponentInChildren<TunnelingVignetteController>(true);
        sb.AppendLine("   Tunneling Vignette: " + (vc != null ? HPath(vc.transform) + ", штатных провайдеров " + vc.locomotionVignetteProviders.Count + " (интенсивность задаёт SpeedVignetteProvider)" : "нет"));
        var rf = Object.FindFirstObjectByType<BasketRestFrame>();
        if (rf != null) sb.AppendLine(string.Format(ci, "   корзина-rest frame: {0:0.00} м влево, {1:0.00} м вперёд от головы, дно на {2:0.00} м (край на {3:0.00} м)", -rf.offset.x, rf.offset.y, rf.height, rf.height + 0.22f));
        var rl = Object.FindFirstObjectByType<RouteLogger>();
        if (rl != null) sb.AppendLine(string.Format(ci, "   RouteLogger: интервал {0:0.0} с, зон {1}: {2}", rl.interval, rl.zones.Count, string.Join(", ", rl.zones.Select(z => z.id))));

        var path = Path.Combine(OutDir, "00_Протокол_настройки_перемещения.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] Протокол настройки: " + path + "\n" + sb);
    }

    static string PkgVersion(string id)
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(XRBodyTransformer).Assembly);
        return info != null ? info.version : "?";
    }
    static string MaskNames(int mask) => string.Join(", ", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(i => LayerMask.LayerToName(i) is var n && n != "" ? n : i.ToString()));
    static string HPath(Transform t) => t.parent == null ? t.name : HPath(t.parent) + "/" + t.name;

    // ===================== PLAY MODE =====================

    const string ModeKey = "ns_locomotion_mode";
    static object recorder;

    static NeuroshelfLocomotionSetup()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }

    static void StartPlay(string mode)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[NEUROSHELF] Уже в Play Mode"); return; }
        Save();
        SessionState.SetString(ModeKey, mode);
        EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange s)
    {
        var mode = SessionState.GetString(ModeKey, "");
        if (s != PlayModeStateChange.EnteredPlayMode || mode == "") return;
        Directory.CreateDirectory(OutDir);
        LocomotionDemoDriver.OutDir = OutDir;
        var go = new GameObject("~NS_LocomotionDemo");
        var d = go.AddComponent<LocomotionDemoDriver>();
        d.mode = mode;
        d.teleportProfile = LoadProfile("LP_Teleport_Snap");
        d.smoothProfile = LoadProfile("LP_Smooth_Snap");
        d.continuousProfile = LoadProfile("LP_Smooth_Continuous");
        if (mode == "fps")
        {
            var probe = new GameObject("~FpsProbe").AddComponent<FpsProbe>();
            probe.warmup = 4f;
            probe.reportFolder = "ЛР6";
            probe.fileName = "13_Замер_FPS_при_движении.txt";
        }
        if (mode == "record") recorder = StartRecorder();
        Debug.Log("[NEUROSHELF] Сценарий перемещения запущен: " + mode);
    }

    static void Watch()
    {
        if (!EditorApplication.isPlaying) return;
        var mode = SessionState.GetString(ModeKey, "");
        if (mode == "" || !LocomotionDemoDriver.Finished) return;
        if (mode == "fps" && Object.FindFirstObjectByType<FpsProbe>() != null) return;   // FpsProbe сам выйдет из Play Mode
        SessionState.EraseString(ModeKey);
        if (recorder != null) { ((UnityEditor.Recorder.RecorderController)recorder).StopRecording(); recorder = null; }
        if (mode == "check")
        {
            var path = Path.Combine(OutDir, "10_Проверка_перемещения_Play_Mode.txt");
            File.WriteAllText(path, "NEUROSHELF — проверка перемещения в Play Mode " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\n\n" + LocomotionDemoDriver.Report, new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 8.6 Протокол: " + path + "\n" + LocomotionDemoDriver.Report);
        }
        else if (mode == "record") Debug.Log("[NEUROSHELF] 8.8 Запись завершена → " + RecDir);
        EditorApplication.isPlaying = false;
    }

    static object StartRecorder()
    {
        Directory.CreateDirectory(RecDir);
        var cs = ScriptableObject.CreateInstance<UnityEditor.Recorder.RecorderControllerSettings>();
        var m = ScriptableObject.CreateInstance<UnityEditor.Recorder.MovieRecorderSettings>();
        m.name = "NEUROSHELF locomotion";
        m.Enabled = true;
        m.ImageInputSettings = new UnityEditor.Recorder.Input.CameraInputSettings
        {
            Source = UnityEditor.Recorder.ImageSource.MainCamera,
            OutputWidth = 1280,
            OutputHeight = 720,
        };
        m.AudioInputSettings.PreserveAudio = false;
        m.OutputFile = Path.Combine(RecDir, "ns_locomotion").Replace('\\', '/');
        cs.AddRecorderSettings(m);
        cs.SetRecordModeToManual();
        cs.FrameRatePlayback = UnityEditor.Recorder.FrameRatePlayback.Constant;
        cs.FrameRate = 30f;
        cs.CapFrameRate = true;
        var rc = new UnityEditor.Recorder.RecorderController(cs);
        rc.PrepareRecording();
        rc.StartRecording();
        Debug.Log("[NEUROSHELF] Recorder: запись → " + m.OutputFile + ".mp4");
        return rc;
    }
}
