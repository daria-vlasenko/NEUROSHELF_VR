// NEUROSHELF — скелетная анимация, Animator Controller, Animation Rigging (IK),
// NPC-покупатели на NavMesh, промоутер с указующим жестом, анимированные объекты окружения,
// автоматические кадры и протоколы проверки.
// Отдельный файл; NeuroshelfMaterialSetup.cs и NeuroshelfLightingSetup.cs не трогает.
//
// Меню NEUROSHELF → Анимация:
//   6.0 Полный цикл            → 6.1 … 6.6, затем кадры 6.7 (обычно нажимать только её)
//   6.1 Импорт и риг           → Humanoid, аватары, настройки клипов, проверка весов вершин
//   6.2 Animator Controllers   → AC_Shopper, AC_Promoter, AC_Door, AC_AdStand + клипы объектов
//   6.3 Условия эксперимента   → ассеты ExperimentConditionSO + объект Experiment в сцене
//   6.4 Анимированные объекты  → витрина с дверцей, вращающийся рекламный стенд
//   6.5 NPC                    → NavMesh, 4 покупателя, промоутер с Rig Builder / Two Bone IK
//   6.6 Проверка               → протокол 00_Отчёт_анимации.txt (веса, граф, пробы, NavMesh)
//   6.7 Кадры (Play Mode)      → PNG + протокол IK, сам входит и выходит из Play Mode
//   6.8 Замер FPS              → 10 с в Play Mode с NPC
//   6.9 Снять окна редактора   → Animator, Inspector (эксперимент)
//   6.98 Удалить созданное в сцене
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР4\

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using AnimatorController = UnityEditor.Animations.AnimatorController;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class NeuroshelfAnimationSetup
{
    // ===================== ПАРАМЕТРЫ =====================

    const string Menu = "NEUROSHELF/Анимация/";

    public const string CharDir = "Assets/_Project/Art/Characters/Mixamo";
    const string ShopperFbx = CharDir + "/Ch_Shopper.fbx";
    const string PromoterFbx = CharDir + "/Ch_Promoter.fbx";
    const string IdleFbx = CharDir + "/Anim_Idle.fbx";
    const string WalkFbx = CharDir + "/Anim_Walk.fbx";
    const string PointFbx = CharDir + "/Anim_Point.fbx";

    const string AnimDir = "Assets/_Project/Animation";
    const string CondDir = "Assets/_Project/Configs/Conditions";
    const string NpcMatDir = "Assets/_Project/Materials/NPC";
    const string PropMatDir = "Assets/_Project/Materials/Props";
    const string PrefabDir = "Assets/_Project/Prefabs/NPC";
    const string NavMeshPath = "Assets/_Project/Scenes/02_Store/NavMesh_Store.asset";

    public const string CondA_Volta = CondDir + "/Cond_A_Manipulated_VOLTA.asset";
    public const string CondA_Pura = CondDir + "/Cond_A_Manipulated_PURA.asset";
    public const string CondC = CondDir + "/Cond_C_Neutral.asset";

    // Раскладка (зал 12 × 8 м, центр в 0; продвигаемый стеллаж Shelf_03 x −2.4…0, z −1.8…−1.2)
    const int ShopperCount = 4;   // + 1 промоутер = 5 скиннинг-мешей на сцену
    public static readonly Vector3 PromoterPos = new Vector3(-2.85f, 0f, -0.40f);  // у торца Shelf_03
    public static readonly Vector3 PlayerViewPos = new Vector3(-1.20f, 0f, 0.00f); // проход перед полкой
    static readonly Vector3 StandPos = new Vector3(-4.40f, 0f, -2.60f);            // путь от входа
    static readonly Vector3 CoolerPos = new Vector3(-5.68f, 0f, 1.20f);            // у западной стены

    const float NavAgentRadius = 0.3f;

    public static string OutDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР4"));

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "6.0 Полный цикл (импорт → граф → объекты → NPC → проверка → кадры)", priority = 200)]
    public static void FullCycle()
    {
        if (!ImportRig()) return;
        BuildControllers();
        BuildConditions();
        BuildProps();
        if (!BuildNpc()) return;
        Verify();
        NeuroshelfAnimCapture.Start();
    }

    [MenuItem(Menu + "6.1 Импорт: Humanoid-риг, клипы, проверка весов", priority = 201)]
    public static void ImportMenu() => ImportRig();

    [MenuItem(Menu + "6.2 Animator Controllers", priority = 202)]
    public static void ControllersMenu() => BuildControllers();

    [MenuItem(Menu + "6.3 Условия эксперимента", priority = 203)]
    public static void ConditionsMenu() => BuildConditions();

    [MenuItem(Menu + "6.4 Анимированные объекты (витрина, стенд)", priority = 204)]
    public static void PropsMenu() { BuildControllers(propsOnly: true); BuildConditions(); BuildProps(); SaveScene(); }

    [MenuItem(Menu + "6.5 NPC: NavMesh, покупатели, промоутер + IK", priority = 205)]
    public static void NpcMenu() => BuildNpc();

    [MenuItem(Menu + "6.6 Проверка → отчёт", priority = 206)]
    public static void VerifyMenu() => Verify();

    [MenuItem(Menu + "6.7 Кадры для отчёта (Play Mode, ~40 с)", priority = 220)]
    public static void CaptureMenu() => NeuroshelfAnimCapture.Start();

    [MenuItem(Menu + "6.8 Замер FPS с NPC (10 с в Play Mode)", priority = 221)]
    public static void FpsMenu()
    {
        SessionState.SetBool(NeuroshelfAnimCapture.FpsKey, true);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem(Menu + "6.9 Снять окна редактора (эксперимент)", priority = 222)]
    public static void GrabMenu() => NeuroshelfAnimCapture.GrabEditorWindows();

    // Без шлема XR не активен, и XR Origin не применяет Camera Y Offset — камера остаётся
    // на сохранённой высоте объекта Camera Offset. Выравниваем его по полю Camera Y Offset.
    [MenuItem(Menu + "6.97 Выровнять Camera Offset по Camera Y Offset", priority = 297)]
    public static void SyncEyeHeight()
    {
        var xo = Object.FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xo == null || xo.CameraFloorOffsetObject == null) { Debug.LogError("[NEUROSHELF] XR Origin не найден."); return; }
        var t = xo.CameraFloorOffsetObject.transform;
        float was = t.localPosition.y;
        Undo.RecordObject(t, "Eye height");
        t.localPosition = new Vector3(t.localPosition.x, xo.CameraYOffset, t.localPosition.z);
        SaveScene();
        Log($"Camera Offset: {was:F3} → {xo.CameraYOffset:F3} м (поле Camera Y Offset не менялось)");
    }

    [MenuItem(Menu + "6.98 Удалить созданное в сцене", priority = 298)]
    public static void Remove()
    {
        foreach (var n in new[] { "NPC", "Props_Animated", "Experiment" })
        { var g = GameObject.Find(n); if (g != null) Undo.DestroyObjectImmediate(g); }
        SaveScene();
        Log("Объекты NPC, Props_Animated, Experiment удалены из сцены.");
    }

    // ===================== 6.1 ИМПОРТ И РИГ =====================

    static readonly StringBuilder ImportReport = new StringBuilder();

    static bool ImportRig()
    {
        var need = new[] { ShopperFbx, PromoterFbx, IdleFbx, WalkFbx, PointFbx };
        var missing = need.Where(p => !File.Exists(Path.GetFullPath(p))).ToArray();
        if (missing.Length > 0)
        {
            Debug.LogError("[NEUROSHELF] Нет файлов Mixamo:\n  " + string.Join("\n  ", missing) +
                           "\nПоложи их в " + CharDir);
            return false;
        }
        ImportReport.Clear();

        // Персонажи: Humanoid, аватар из собственной T-позы
        foreach (var p in new[] { ShopperFbx, PromoterFbx })
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(p);
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.optimizeGameObjects = false;          // кости нужны как объекты — к ним цепляется IK
            mi.importBlendShapes = false;
            mi.importCameras = false; mi.importLights = false;
            mi.skinWeights = ModelImporterSkinWeights.Standard;  // до 4 костей на вершину
            mi.SaveAndReimport();
        }
        var shopperAvatar = AssetDatabase.LoadAllAssetsAtPath(ShopperFbx).OfType<Avatar>().FirstOrDefault();
        var promoterAvatar = AssetDatabase.LoadAllAssetsAtPath(PromoterFbx).OfType<Avatar>().FirstOrDefault();
        foreach (var (n, a) in new[] { ("Ch_Shopper", shopperAvatar), ("Ch_Promoter", promoterAvatar) })
        {
            if (a == null || !a.isValid || !a.isHuman)
            { Debug.LogError($"[NEUROSHELF] {n}: аватар Humanoid не создан. Открой FBX → Rig → Configure."); return false; }
            ImportReport.AppendLine($"{n}: Animation Type = Humanoid, аватар валиден ({a.name})");
        }

        // Клипы: Humanoid, аватар копируется с персонажа (у Mixamo один скелет на всех)
        ConfigureClip(IdleFbx, "Idle", shopperAvatar, loop: true);
        ConfigureClip(WalkFbx, "Walk", shopperAvatar, loop: true);
        ConfigureClip(PointFbx, "Point", shopperAvatar, loop: true);

        foreach (var p in new[] { ShopperFbx, PromoterFbx }) ImportReport.Append(WeightReport(p));
        Log("Импорт и риг готовы.\n" + ImportReport);
        return true;
    }

    static void ConfigureClip(string path, string clipName, Avatar src, bool loop)
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        mi.sourceAvatar = src;
        mi.importAnimation = true;
        mi.importBlendShapes = false;
        mi.SaveAndReimport();   // нужен, чтобы появились defaultClipAnimations

        mi = (ModelImporter)AssetImporter.GetAtPath(path);
        var clips = mi.defaultClipAnimations;
        if (clips.Length == 0) { Debug.LogError($"[NEUROSHELF] В {path} нет анимации."); return; }
        var c = clips[0];
        c.name = clipName;
        c.loopTime = loop;
        c.loopPose = loop;
        // Анимации «на месте»: корень не уезжает и не поворачивается — двигает NavMesh/скрипт
        c.lockRootRotation = true; c.keepOriginalOrientation = true;
        c.lockRootHeightY = true;  c.keepOriginalPositionY = true;
        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
        mi.clipAnimations = new[] { c };
        mi.SaveAndReimport();
        ImportReport.AppendLine($"Клип {clipName}: {Path.GetFileName(path)}, кадры {c.firstFrame:F0}–{c.lastFrame:F0}, " +
                                $"Loop Time={loop}, корень зафиксирован (Bake Into Pose: rotation, Y, XZ)");
    }

    public static AnimationClip Clip(string fbx) =>
        AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    // Численная проверка скининга: дополняет визуальную проверку в Blender (Weight Paint)
    static string WeightReport(string fbx)
    {
        var sb = new StringBuilder();
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = smr.sharedMesh; if (m == null) continue;
            var perVertex = m.GetBonesPerVertex();
            var weights = m.GetAllBoneWeights();
            int verts = m.vertexCount, maxInf = 0, badSum = 0, zero = 0, rigid = 0, idx = 0;
            var used = new HashSet<int>();
            for (int v = 0; v < verts; v++)
            {
                int n = perVertex[v]; float sum = 0f, top = 0f;
                for (int k = 0; k < n; k++)
                {
                    var w = weights[idx + k]; sum += w.weight; top = Mathf.Max(top, w.weight);
                    if (w.weight > 0.001f) used.Add(w.boneIndex);
                }
                idx += n;
                maxInf = Mathf.Max(maxInf, n);
                if (n == 0 || sum < 1e-4f) zero++;
                else if (Mathf.Abs(sum - 1f) > 0.01f) badSum++;
                if (top > 0.999f) rigid++;
            }
            int tris = 0; for (int s = 0; s < m.subMeshCount; s++) tris += (int)m.GetIndexCount(s) / 3;
            sb.AppendLine($"  {Path.GetFileNameWithoutExtension(fbx)}/{smr.name}: вершин {verts}, треугольников {tris}, " +
                          $"костей {smr.bones.Length} (с весами {used.Count}), макс. костей на вершину {maxInf}");
            sb.AppendLine($"    без веса: {zero} · сумма весов ≠ 1: {badSum} · жёстко на одной кости: {100f * rigid / Mathf.Max(1, verts):F0}%");
        }
        return sb.ToString();
    }

    // ===================== 6.2 ANIMATOR CONTROLLERS =====================

    static void BuildControllers(bool propsOnly = false)
    {
        Directory.CreateDirectory(AnimDir);
        BuildDoorController();
        BuildStandController();
        if (propsOnly) { AssetDatabase.SaveAssets(); return; }

        var idle = Clip(IdleFbx); var walk = Clip(WalkFbx); var point = Clip(PointFbx);
        if (idle == null || walk == null || point == null)
        { Debug.LogError("[NEUROSHELF] Клипы Idle/Walk/Point не найдены — сначала 6.1."); return; }

        // --- Покупатель: Idle ⇄ Walk по булевой IsMoving; скорость шага — параметр Speed
        var ac = NewController("AC_Shopper");
        ac.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        ac.AddParameter(new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var sm = ac.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle", new Vector3(300, 100)); sIdle.motion = idle;
        var sWalk = sm.AddState("Walk", new Vector3(560, 100)); sWalk.motion = walk;
        sWalk.speedParameterActive = true; sWalk.speedParameter = "Speed";
        sm.defaultState = sIdle;
        Transition(sIdle, sWalk, 0.2f).AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
        Transition(sWalk, sIdle, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        // --- Промоутер: базовый слой Idle; слой Gesture (маска «правая рука») Empty → Point
        var mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers);
        }
        Replace(mask, AnimDir + "/AM_RightArm.mask");

        ac = NewController("AC_Promoter");
        ac.AddParameter("PlayerNear", AnimatorControllerParameterType.Bool);
        ac.AddParameter("Point", AnimatorControllerParameterType.Trigger);
        sm = ac.layers[0].stateMachine;
        var pIdle = sm.AddState("Idle", new Vector3(300, 100)); pIdle.motion = idle;
        sm.defaultState = pIdle;

        ac.AddLayer("Gesture");
        var layers = ac.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 0f;           // вес слоя ведёт PromoterIK синхронно с IK
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        ac.layers = layers;
        var gsm = ac.layers[1].stateMachine;
        var gEmpty = gsm.AddState("Empty", new Vector3(300, 100)); gEmpty.writeDefaultValues = false;
        var gPoint = gsm.AddState("Point", new Vector3(560, 100)); gPoint.motion = point; gPoint.writeDefaultValues = false;
        gsm.defaultState = gEmpty;
        Transition(gEmpty, gPoint, 0.25f).AddCondition(AnimatorConditionMode.If, 0, "Point");
        Transition(gPoint, gEmpty, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0, "PlayerNear");

        AssetDatabase.SaveAssets();
        Log("Animator Controllers: AC_Shopper, AC_Promoter, AC_Door, AC_AdStand → " + AnimDir);
    }

    static void BuildDoorController()
    {
        // Клипы создаются кодом: кривая угла поворота петли дверцы
        var open = new AnimationClip { name = "Door_Open" };
        open.SetCurve("Door_Hinge", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.EaseInOut(0f, 0f, 0.9f, -100f));
        var close = new AnimationClip { name = "Door_Close" };
        close.SetCurve("Door_Hinge", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.EaseInOut(0f, -100f, 0.7f, 0f));
        var closed = new AnimationClip { name = "Door_Closed" };
        closed.SetCurve("Door_Hinge", typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Constant(0f, 0.1f, 0f));
        foreach (var c in new[] { open, close, closed }) Replace(c, $"{AnimDir}/{c.name}.anim");
        open = Load<AnimationClip>($"{AnimDir}/Door_Open.anim");
        close = Load<AnimationClip>($"{AnimDir}/Door_Close.anim");
        closed = Load<AnimationClip>($"{AnimDir}/Door_Closed.anim");

        var ac = NewController("AC_Door");
        ac.AddParameter("Open", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Close", AnimatorControllerParameterType.Trigger);
        var sm = ac.layers[0].stateMachine;
        var sClosed = sm.AddState("Closed", new Vector3(300, 60)); sClosed.motion = closed;
        var sOpening = sm.AddState("Opening", new Vector3(560, 160)); sOpening.motion = open;
        var sClosing = sm.AddState("Closing", new Vector3(300, 260)); sClosing.motion = close;
        sm.defaultState = sClosed;
        Transition(sClosed, sOpening, 0f).AddCondition(AnimatorConditionMode.If, 0, "Open");
        Transition(sOpening, sClosing, 0f).AddCondition(AnimatorConditionMode.If, 0, "Close");
        Transition(sClosing, sOpening, 0f).AddCondition(AnimatorConditionMode.If, 0, "Open");
        var back = sClosing.AddTransition(sClosed); back.hasExitTime = true; back.exitTime = 1f; back.duration = 0f;
    }

    static void BuildStandController()
    {
        var spin = new AnimationClip { name = "AdStand_Spin" };
        var curve = AnimationCurve.Linear(0f, 0f, 8f, 360f);   // 45°/с
        spin.SetCurve("Rotor", typeof(Transform), "localEulerAnglesRaw.y", curve);
        var st = AnimationUtility.GetAnimationClipSettings(spin); st.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(spin, st);
        Replace(spin, AnimDir + "/AdStand_Spin.anim");
        spin = Load<AnimationClip>(AnimDir + "/AdStand_Spin.anim");

        var ac = NewController("AC_AdStand");
        ac.AddParameter(new AnimatorControllerParameter { name = "SpinSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        var s = ac.layers[0].stateMachine.AddState("Spin", new Vector3(300, 100));
        s.motion = spin; s.speedParameterActive = true; s.speedParameter = "SpinSpeed";
    }

    static AnimatorController NewController(string name)
    {
        var path = $"{AnimDir}/{name}.controller";
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
        return AnimatorController.CreateAnimatorControllerAtPath(path);
    }

    static AnimatorStateTransition Transition(AnimatorState a, AnimatorState b, float dur)
    {
        var t = a.AddTransition(b);
        t.hasExitTime = false; t.duration = dur; t.interruptionSource = TransitionInterruptionSource.Destination;
        return t;
    }

    // ===================== 6.3 УСЛОВИЯ ЭКСПЕРИМЕНТА =====================

    static void BuildConditions()
    {
        Directory.CreateDirectory(CondDir);
        Cond(CondA_Volta, "A_Manipulated_VOLTA", "Фаза A. Продвигается VOLTA на уровне глаз Shelf_03.", "Can_VOLTA", true, true);
        Cond(CondA_Pura, "A_Manipulated_PURA", "Фаза A, вариант: продвигается PURA. Нужна для проверки, что IK подстраивается.", "Bottle_PURA", true, true);
        Cond(CondC, "C_Neutral", "Фаза C. Манипуляции выключены: нет толпы, промоутер не указывает, стенд стоит.", "Can_VOLTA", false, false);
        AssetDatabase.SaveAssets();

        var go = GameObject.Find("Experiment") ?? new GameObject("Experiment");
        var ctx = go.GetComponent<ExperimentContext>() ?? go.AddComponent<ExperimentContext>();
        ctx.condition = Load<ExperimentConditionSO>(CondA_Volta);
        EditorUtility.SetDirty(ctx);
        Log("Условия: A_VOLTA (активно), A_PURA, C_Neutral → " + CondDir);
    }

    static void Cond(string path, string name, string desc, string product, bool crowd, bool active)
    {
        var c = Load<ExperimentConditionSO>(path);
        if (c == null) { c = ScriptableObject.CreateInstance<ExperimentConditionSO>(); AssetDatabase.CreateAsset(c, path); }
        c.conditionName = name; c.description = desc;
        c.promotedProduct = product; c.promotedShelf = "Shelf_03";
        c.socialProofCrowd = crowd; c.crowdShareAtPromo = 0.85f;
        c.promoterPointing = active; c.adStandMotion = active;
        EditorUtility.SetDirty(c);
        if (GameObject.Find(product) == null) Debug.LogWarning($"[NEUROSHELF] Товар {product} не найден в сцене.");
    }

    // ===================== 6.4 АНИМИРОВАННЫЕ ОБЪЕКТЫ =====================

    static void BuildProps()
    {
        Directory.CreateDirectory(PropMatDir);
        var old = GameObject.Find("Props_Animated"); if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("Props_Animated");   // не внутри Environment: там скрипт света ставит Static

        var metal = Load<Material>("Assets/_Project/Materials/M_Env_Metal.mat");
        var plastic = Load<Material>("Assets/_Project/Materials/M_Env_Plastic.mat");
        var glass = Mat("M_Prop_Glass", new Color(0.75f, 0.88f, 0.95f, 0.22f), transparent: true, smooth: 0.95f);
        var brand = Mat("M_Prop_Brand", new Color(0.95f, 0.42f, 0.12f), smooth: 0.4f);
        var white = Mat("M_Prop_White", new Color(0.92f, 0.92f, 0.9f), smooth: 0.3f);

        // --- Витрина-холодильник со стеклянной дверцей (лицом в зал, +X)
        var cooler = new GameObject("Vitrine_Cooler");
        cooler.transform.SetParent(root.transform);
        cooler.transform.SetPositionAndRotation(CoolerPos, Quaternion.Euler(0, 90, 0));
        float W = 0.8f, H = 1.9f, D = 0.6f;
        Box(cooler.transform, "Back", new Vector3(0, H / 2, -D / 2 + 0.02f), new Vector3(W, H, 0.04f), plastic);
        Box(cooler.transform, "Side_L", new Vector3(-W / 2 + 0.02f, H / 2, 0), new Vector3(0.04f, H, D), plastic);
        Box(cooler.transform, "Side_R", new Vector3(W / 2 - 0.02f, H / 2, 0), new Vector3(0.04f, H, D), plastic);
        Box(cooler.transform, "Top", new Vector3(0, H - 0.05f, 0), new Vector3(W, 0.1f, D), brand);
        Box(cooler.transform, "Base", new Vector3(0, 0.06f, 0), new Vector3(W, 0.12f, D), plastic);
        foreach (var y in new[] { 0.55f, 1.0f, 1.45f })
        {
            Box(cooler.transform, $"Rack_{y:F2}", new Vector3(0, y, 0), new Vector3(W - 0.08f, 0.02f, D - 0.08f), metal);
            for (int i = 0; i < 3; i++) ProductCopy(cooler.transform, i % 2 == 0 ? "Can_ZEST" : "Can_FERRO",
                cooler.transform.TransformPoint(new Vector3(-0.22f + 0.22f * i, y + 0.01f, 0.02f)));
        }
        var hinge = new GameObject("Door_Hinge").transform;
        hinge.SetParent(cooler.transform, false);
        hinge.localPosition = new Vector3(-W / 2, 0.12f, D / 2 + 0.015f);
        float dw = W, dh = H - 0.22f;
        Box(hinge, "Frame_L", new Vector3(0.025f, dh / 2, 0), new Vector3(0.05f, dh, 0.03f), metal);
        Box(hinge, "Frame_R", new Vector3(dw - 0.025f, dh / 2, 0), new Vector3(0.05f, dh, 0.03f), metal);
        Box(hinge, "Frame_T", new Vector3(dw / 2, dh - 0.025f, 0), new Vector3(dw, 0.05f, 0.03f), metal);
        Box(hinge, "Frame_B", new Vector3(dw / 2, 0.025f, 0), new Vector3(dw, 0.05f, 0.03f), metal);
        Box(hinge, "Glass", new Vector3(dw / 2, dh / 2, 0), new Vector3(dw - 0.1f, dh - 0.1f, 0.008f), glass);
        Box(hinge, "Handle", new Vector3(dw - 0.08f, dh / 2, 0.035f), new Vector3(0.025f, 0.35f, 0.025f), metal);
        var col = cooler.AddComponent<BoxCollider>(); col.center = new Vector3(0, H / 2, 0); col.size = new Vector3(W, H, D);
        var anim = cooler.AddComponent<Animator>();
        anim.runtimeAnimatorController = Load<RuntimeAnimatorController>(AnimDir + "/AC_Door.controller");
        var door = cooler.AddComponent<ProximityDoor>(); door.animator = anim;
        // центр витрины у самой стены — за краем сетки проб; выборку берём на 0.4 м ближе к залу
        ProbeAnchor(cooler.transform, new Vector3(0, 1.0f, 0.75f));

        // --- Вращающийся рекламный стенд VOLTA на пути от входа
        var stand = new GameObject("AdStand_VOLTA");
        stand.transform.SetParent(root.transform);
        stand.transform.position = StandPos;
        Cyl(stand.transform, "Base", new Vector3(0, 0.04f, 0), 0.70f, 0.08f, metal);
        var rotor = new GameObject("Rotor").transform; rotor.SetParent(stand.transform, false);
        Cyl(rotor, "Pole", new Vector3(0, 1.0f, 0), 0.06f, 1.9f, metal);
        foreach (var y in new[] { 0.75f, 1.15f, 1.55f })
        {
            Cyl(rotor, $"Tier_{y:F2}", new Vector3(0, y, 0), 0.60f, 0.02f, white);
            for (int i = 0; i < 4; i++)
            {
                var a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                ProductCopy(rotor, "Can_VOLTA", rotor.TransformPoint(new Vector3(Mathf.Cos(a) * 0.2f, y + 0.012f, Mathf.Sin(a) * 0.2f)));
            }
        }
        // табло: одна грань фирменного цвета, остальные белые — по ним видна фаза поворота
        for (int i = 0; i < 4; i++)
        {
            var dir = Quaternion.Euler(0, 90 * i, 0) * Vector3.forward;
            var p = Box(rotor, $"Sign_{i}", new Vector3(0, 2.05f, 0) + dir * 0.25f, new Vector3(0.5f, 0.32f, 0.02f), i % 2 == 0 ? brand : white);
            p.transform.localRotation = Quaternion.LookRotation(dir);
        }
        var sc = stand.AddComponent<CapsuleCollider>(); sc.center = new Vector3(0, 1f, 0); sc.radius = 0.35f; sc.height = 2.2f;
        var sa = stand.AddComponent<Animator>();
        sa.runtimeAnimatorController = Load<RuntimeAnimatorController>(AnimDir + "/AC_AdStand.controller");
        stand.AddComponent<AdStandMotion>().animator = sa;

        foreach (var r in root.GetComponentsInChildren<Renderer>()) DynamicRenderer(r);
        Log("Анимированные объекты: Vitrine_Cooler (дверца по триггерам Open/Close), AdStand_VOLTA (вращение 45°/с).");
    }

    static GameObject Box(Transform parent, string name, Vector3 lp, Vector3 size, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false); g.transform.localPosition = lp; g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static GameObject Cyl(Transform parent, string name, Vector3 lp, float diameter, float height, Material m)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = name; Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false); g.transform.localPosition = lp;
        g.transform.localScale = new Vector3(diameter, height / 2f, diameter);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // Копия упаковки из сцены, поставленная дном на точку (у мешей FBX смещён pivot)
    static void ProductCopy(Transform parent, string src, Vector3 bottomCenter)
    {
        var o = GameObject.Find(src); if (o == null) return;
        var mf = o.GetComponent<MeshFilter>(); var mr = o.GetComponent<MeshRenderer>(); if (mf == null || mr == null) return;
        var g = new GameObject(src + "_display");
        g.transform.SetParent(parent, true);
        g.transform.rotation = o.transform.rotation; g.transform.localScale = Vector3.one;
        var ls = o.transform.lossyScale; var ps = parent.lossyScale;
        g.transform.localScale = new Vector3(ls.x / ps.x, ls.y / ps.y, ls.z / ps.z);
        g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials;
        g.transform.position = bottomCenter;
        var b = r.bounds;
        g.transform.position += new Vector3(bottomCenter.x - b.center.x, bottomCenter.y - b.min.y, bottomCenter.z - b.center.z);
    }

    static void ProbeAnchor(Transform owner, Vector3 local)
    {
        var a = new GameObject("ProbeAnchor").transform; a.SetParent(owner, false); a.localPosition = local;
        foreach (var r in owner.GetComponentsInChildren<Renderer>()) r.probeAnchor = a;
    }

    // Динамический объект: без Static, свет — из Light Probes, отражения — из Reflection Probe
    static void DynamicRenderer(Renderer r)
    {
        GameObjectUtility.SetStaticEditorFlags(r.gameObject, (StaticEditorFlags)0);
        r.lightProbeUsage = LightProbeUsage.BlendProbes;
        r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        r.shadowCastingMode = ShadowCastingMode.Off;   // свет запечён — тени в реальном времени некому отбрасывать
        r.receiveShadows = false;
        r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    // ===================== 6.5 NPC =====================

    static bool BuildNpc()
    {
        var shopperModel = Load<GameObject>(ShopperFbx);
        var promoterModel = Load<GameObject>(PromoterFbx);
        var acShopper = Load<RuntimeAnimatorController>(AnimDir + "/AC_Shopper.controller");
        var acPromoter = Load<RuntimeAnimatorController>(AnimDir + "/AC_Promoter.controller");
        if (shopperModel == null || promoterModel == null || acShopper == null || acPromoter == null)
        { Debug.LogError("[NEUROSHELF] Нет моделей или контроллеров — сначала 6.1 и 6.2."); return false; }
        if (ExperimentContext.Find() == null) BuildConditions();

        Directory.CreateDirectory(NpcMatDir); Directory.CreateDirectory(PrefabDir);
        var old = GameObject.Find("NPC"); if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject("NPC");

        // --- NavMesh по физическим коллайдерам: пол, стены, стеллажи, касса, стенд, витрина
        SetAgentRadius(NavAgentRadius);
        var surf = root.AddComponent<NavMeshSurface>();
        surf.collectObjects = CollectObjects.All;
        surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surf.BuildNavMesh();
        if (AssetDatabase.LoadAssetAtPath<Object>(NavMeshPath) != null) AssetDatabase.DeleteAsset(NavMeshPath);
        AssetDatabase.CreateAsset(surf.navMeshData, NavMeshPath);

        var blob = BlobMaterial();
        var joints = Mat("M_NPC_Joints", new Color(0.16f, 0.16f, 0.18f), smooth: 0.35f);
        var tints = new[] { new Color(0.42f, 0.48f, 0.56f), new Color(0.66f, 0.58f, 0.46f),
                            new Color(0.45f, 0.52f, 0.40f), new Color(0.55f, 0.55f, 0.57f) };

        // --- Покупатели: префаб + 4 экземпляра, стартуют у продвигаемого стеллажа
        var shopperPrefab = MakeShopperPrefab(shopperModel, acShopper, joints, blob);
        var shelf = GameObject.Find("Shelf_03").transform;
        var slots = ShopperAgent.SlotPositions(shelf).ToArray();
        var shoppers = new GameObject("Shoppers").transform; shoppers.SetParent(root.transform);
        for (int i = 0; i < ShopperCount; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(shopperPrefab, shoppers);
            g.name = $"NPC_Shopper_{i + 1:00}";
            var p = slots[i % slots.Length];
            if (NavMesh.SamplePosition(p, out var hit, 0.5f, NavMesh.AllAreas)) p = hit.position;
            g.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-ShopperAgent.ShelfFront(shelf)));
            var body = Mat($"M_NPC_Shopper_{i + 1}", tints[i % tints.Length], smooth: 0.3f);
            foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => IsJoint(m, r) ? joints : body).ToArray();
            g.GetComponent<NavMeshAgent>().avoidancePriority = 40 + i * 5;
        }

        // --- Промоутер с Rig Builder: Two Bone IK (правая рука) + Multi-Aim (голова)
        BuildPromoter(root.transform, promoterModel, acPromoter, joints, blob);

        // --- Препятствие на месте игрока
        var po = new GameObject("PlayerObstacle"); po.transform.SetParent(root.transform);
        var ob = po.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Capsule;
        ob.radius = 0.35f; ob.height = 1.8f; ob.center = new Vector3(0, 0.9f, 0); ob.carving = false;
        po.AddComponent<PlayerObstacle>();

        SaveScene();
        Log($"NPC: {ShopperCount} покупателя + промоутер, NavMesh → {NavMeshPath}");
        return true;
    }

    static bool IsJoint(Material m, Renderer r) =>
        (m != null && m.name.ToLower().Contains("joint")) || r.name.ToLower().Contains("joint");

    static GameObject MakeShopperPrefab(GameObject model, RuntimeAnimatorController ac, Material joints, Material blob)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(model);
        g.name = "NPC_Shopper";
        var anim = SetupCharacter(g, ac, blob);
        var agent = g.AddComponent<NavMeshAgent>();
        agent.radius = NavAgentRadius; agent.height = 1.75f; agent.speed = 1.1f;
        agent.angularSpeed = 360f; agent.acceleration = 4f; agent.stoppingDistance = 0.1f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        g.AddComponent<ShopperAgent>().animator = anim;
        var path = PrefabDir + "/NPC_Shopper.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(g, path);
        Object.DestroyImmediate(g);
        return prefab;
    }

    static Animator SetupCharacter(GameObject g, RuntimeAnimatorController ac, Material blob)
    {
        var anim = g.GetComponent<Animator>() ?? g.AddComponent<Animator>();
        anim.runtimeAnimatorController = ac;
        anim.applyRootMotion = false;                               // двигают NavMesh и скрипт
        anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; // вне кадра скелет не считается
        foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            DynamicRenderer(r);
            r.updateWhenOffscreen = false;
            r.skinnedMotionVectors = false;
            r.probeAnchor = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Hips);
        }
        // контактная тень-«пятно»: запечённый свет не даёт динамическим объектам теней
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "BlobShadow"; Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(g.transform, false);
        q.transform.localPosition = new Vector3(0, 0.01f, 0);
        q.transform.localRotation = Quaternion.Euler(90, 0, 0);
        q.transform.localScale = new Vector3(0.75f, 0.75f, 1f);
        var qr = q.GetComponent<MeshRenderer>(); qr.sharedMaterial = blob;
        qr.shadowCastingMode = ShadowCastingMode.Off; qr.receiveShadows = false;
        qr.lightProbeUsage = LightProbeUsage.Off; qr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return anim;
    }

    static void BuildPromoter(Transform root, GameObject model, RuntimeAnimatorController ac, Material joints, Material blob)
    {
        var g = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        g.name = "NPC_Promoter";
        var toPlayer = PlayerViewPos - PromoterPos; toPlayer.y = 0;
        g.transform.SetPositionAndRotation(PromoterPos, Quaternion.LookRotation(toPlayer));
        var anim = SetupCharacter(g, ac, blob);
        var uniform = Mat("M_NPC_Promoter", new Color(0.92f, 0.40f, 0.10f), smooth: 0.35f);   // цвет бренда
        foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => IsJoint(m, r) ? joints : uniform).ToArray();

        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
        var shoulder = B(HumanBodyBones.RightUpperArm); var elbow = B(HumanBodyBones.RightLowerArm);
        var hand = B(HumanBodyBones.RightHand); var head = B(HumanBodyBones.Head);

        // Структура по гайду: Rig Builder на объекте с Animator, Rig — на дочернем пустом объекте
        var rigGo = new GameObject("Rig_Promoter"); rigGo.transform.SetParent(g.transform, false);
        var rig = rigGo.AddComponent<Rig>();
        var builder = g.AddComponent<RigBuilder>();
        builder.layers.Clear(); builder.layers.Add(new RigLayer(rig, true));

        // Two Bone IK: плечо → локоть → кисть, цель — маленький куб, подсказка — точка локтя
        var ikGo = new GameObject("IK_RightArm"); ikGo.transform.SetParent(rigGo.transform, false);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "IK_RightArm_Target"; Object.DestroyImmediate(target.GetComponent<Collider>());
        target.transform.SetParent(ikGo.transform, false);
        target.transform.localScale = Vector3.one * 0.05f;
        target.transform.SetPositionAndRotation(hand.position, hand.rotation);
        var tr = target.GetComponent<MeshRenderer>();
        tr.sharedMaterial = Mat("M_FX_IKTarget", new Color(0.1f, 0.9f, 0.3f), unlit: true);
        tr.shadowCastingMode = ShadowCastingMode.Off; tr.enabled = false;
        var hint = new GameObject("IK_RightArm_Hint").transform; hint.SetParent(ikGo.transform, false);
        hint.position = elbow.position - g.transform.up * 0.2f;
        var ik = ikGo.AddComponent<TwoBoneIKConstraint>();
        var d = ik.data;
        d.root = shoulder; d.mid = elbow; d.tip = hand; d.target = target.transform; d.hint = hint;
        d.targetPositionWeight = 1f; d.targetRotationWeight = 1f; d.hintWeight = 1f;
        ik.data = d; ik.weight = 0f;

        // Multi-Aim: голова смотрит на игрока; ось взгляда подбирается по фактической ориентации кости
        var aimGo = new GameObject("Aim_Head"); aimGo.transform.SetParent(rigGo.transform, false);
        var look = new GameObject("LookAt_Player").transform; look.SetParent(rigGo.transform, false);
        look.position = head.position + g.transform.forward * 2f;
        var aim = aimGo.AddComponent<MultiAimConstraint>();
        var ad = aim.data;
        ad.constrainedObject = head;
        var src = new WeightedTransformArray(0); src.Add(new WeightedTransform(look, 1f)); ad.sourceObjects = src;
        ad.aimAxis = BestAxis(head, g.transform.forward);
        ad.upAxis = BestAxis(head, g.transform.up);
        ad.limits = new Vector2(-70f, 70f);
        ad.constrainedXAxis = ad.constrainedYAxis = ad.constrainedZAxis = true;
        aim.data = ad; aim.weight = 0f;

        // Луч «кисть → товар» — только для кадров отчёта
        var rayGo = new GameObject("PointerRay"); rayGo.transform.SetParent(rigGo.transform, false);
        var lr = rayGo.AddComponent<LineRenderer>();
        lr.positionCount = 2; lr.widthMultiplier = 0.012f; lr.useWorldSpace = true;
        lr.sharedMaterial = Mat("M_FX_PointerRay", new Color(0.1f, 0.9f, 0.3f), unlit: true);
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.enabled = false;

        var p = g.AddComponent<PromoterIK>();
        p.animator = anim; p.armIK = ik; p.headAim = aim;
        p.ikTarget = target.transform; p.ikHint = hint; p.lookTarget = look; p.pointerRay = lr;

        // на промоутера тоже ставим препятствие, чтобы покупатели его обходили
        var ob = g.AddComponent<NavMeshObstacle>(); ob.shape = NavMeshObstacleShape.Capsule;
        ob.radius = 0.3f; ob.height = 1.8f; ob.center = new Vector3(0, 0.9f, 0);
    }

    static MultiAimConstraintData.Axis BestAxis(Transform bone, Vector3 world)
    {
        var axes = new (MultiAimConstraintData.Axis a, Vector3 v)[]
        {
            (MultiAimConstraintData.Axis.X, bone.right), (MultiAimConstraintData.Axis.X_NEG, -bone.right),
            (MultiAimConstraintData.Axis.Y, bone.up), (MultiAimConstraintData.Axis.Y_NEG, -bone.up),
            (MultiAimConstraintData.Axis.Z, bone.forward), (MultiAimConstraintData.Axis.Z_NEG, -bone.forward),
        };
        return axes.OrderByDescending(x => Vector3.Dot(x.v, world)).First().a;
    }

    static void SetAgentRadius(float r)
    {
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").FirstOrDefault();
        if (asset == null) return;
        var so = new SerializedObject(asset);
        var s = so.FindProperty("m_Settings");
        if (s == null || s.arraySize == 0) return;
        var rad = s.GetArrayElementAtIndex(0).FindPropertyRelative("agentRadius");
        if (rad != null && Mathf.Abs(rad.floatValue - r) > 1e-3f) { rad.floatValue = r; so.ApplyModifiedPropertiesWithoutUndo(); }
    }

    static Material BlobMaterial()
    {
        var texPath = NpcMatDir + "/T_BlobShadow.asset";
        var tex = Load<Texture2D>(texPath);
        if (tex == null)
        {
            const int N = 64;
            tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "T_BlobShadow" };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
                    float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a) * 0.55f;
                    tex.SetPixel(x, y, new Color(0, 0, 0, a));
                }
            tex.Apply();
            AssetDatabase.CreateAsset(tex, texPath);
        }
        var m = Mat("M_NPC_BlobShadow", Color.white, transparent: true, unlit: true);
        m.SetTexture("_BaseMap", tex);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ===================== 6.6 ПРОВЕРКА =====================

    static void Verify()
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        sb.AppendLine("NEUROSHELF — протокол настройки анимации");
        sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm} · Unity {Application.unityVersion}");
        sb.AppendLine();

        sb.AppendLine("1. ИМПОРТ И РИГ");
        foreach (var p in new[] { ShopperFbx, PromoterFbx })
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter; if (mi == null) { sb.AppendLine($"  нет {p}"); continue; }
            var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            sb.AppendLine($"  {Path.GetFileName(p)}: Animation Type={mi.animationType}, Avatar={mi.avatarSetup}, " +
                          $"human={av != null && av.isHuman}, valid={av != null && av.isValid}, Optimize Game Objects={mi.optimizeGameObjects}");
        }
        foreach (var p in new[] { IdleFbx, WalkFbx, PointFbx })
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter; if (mi == null) continue;
            var c = mi.clipAnimations.FirstOrDefault(); var clip = Clip(p);
            sb.AppendLine($"  {Path.GetFileName(p)}: {mi.animationType}, аватар {mi.avatarSetup} ← {(mi.sourceAvatar ? mi.sourceAvatar.name : "—")}, " +
                          $"клип «{c?.name}» {(clip ? clip.length : 0):F2} с, Loop={c?.loopTime}");
        }
        sb.AppendLine();
        sb.AppendLine("2. ВЕСА ВЕРШИН (численно)");
        foreach (var p in new[] { ShopperFbx, PromoterFbx }) if (File.Exists(Path.GetFullPath(p))) sb.Append(WeightReport(p));
        sb.AppendLine();

        sb.AppendLine("3. ГРАФЫ СОСТОЯНИЙ (Animator Controller)");
        foreach (var n in new[] { "AC_Shopper", "AC_Promoter", "AC_Door", "AC_AdStand" })
        {
            var ac = Load<AnimatorController>($"{AnimDir}/{n}.controller"); if (ac == null) continue;
            sb.AppendLine($"  {n}: параметры: " + string.Join(", ", ac.parameters.Select(x => $"{x.name} ({x.type})")));
            foreach (var l in ac.layers)
            {
                sb.AppendLine($"    слой «{l.name}»{(l.avatarMask ? $", маска {l.avatarMask.name}" : "")}, по умолчанию: {l.stateMachine.defaultState?.name}");
                foreach (var cs in l.stateMachine.states)
                {
                    var s = cs.state;
                    sb.AppendLine($"      [{s.name}] клип: {(s.motion ? s.motion.name : "—")}{(s.speedParameterActive ? $", скорость ← {s.speedParameter}" : "")}");
                    foreach (var t in s.transitions)
                        sb.AppendLine($"        → {t.destinationState?.name}: " +
                            (t.conditions.Length == 0 ? "" : string.Join(" и ", t.conditions.Select(Cnd))) +
                            (t.hasExitTime ? $"{(t.conditions.Length > 0 ? " и " : "")}по окончании клипа (Exit Time {t.exitTime:F1})" : "") +
                            $", переход {t.duration:F2} с");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("4. ANIMATION RIGGING");
        var prom = Object.FindFirstObjectByType<PromoterIK>();
        if (prom != null)
        {
            var d = prom.armIK.data;
            sb.AppendLine($"  Rig Builder: {prom.name} · Rig: {prom.armIK.transform.parent.name}");
            sb.AppendLine($"  Two Bone IK «{prom.armIK.name}»: Root={d.root?.name}, Mid={d.mid?.name}, Tip={d.tip?.name}, Target={d.target?.name}, Hint={d.hint?.name}");
            var ad = prom.headAim.data;
            sb.AppendLine($"  Multi-Aim «{prom.headAim.name}»: кость {ad.constrainedObject?.name}, ось взгляда {ad.aimAxis}, ось «вверх» {ad.upAxis}, пределы {ad.limits.x:F0}…{ad.limits.y:F0}°");
        }
        else sb.AppendLine("  промоутер не создан");
        sb.AppendLine();

        sb.AppendLine("5. NPC: NAVMESH И LIGHT PROBES");
        var tri = NavMesh.CalculateTriangulation();
        float area = 0; for (int i = 0; i < tri.indices.Length; i += 3)
            area += Vector3.Cross(tri.vertices[tri.indices[i + 1]] - tri.vertices[tri.indices[i]], tri.vertices[tri.indices[i + 2]] - tri.vertices[tri.indices[i]]).magnitude / 2f;
        sb.AppendLine($"  NavMesh: {tri.indices.Length / 3} треугольников, проходимая площадь {area:F1} м² (зал 96 м²), радиус агента {NavAgentRadius} м");

        var lp = LightmapSettings.lightProbes;
        Bounds pb = default;
        if (lp != null && lp.count > 0) { pb = new Bounds(lp.positions[0], Vector3.zero); foreach (var p in lp.positions) pb.Encapsulate(p); }
        sb.AppendLine(lp != null ? $"  Light Probes: {lp.count} шт., объём {V(pb.min)} … {V(pb.max)}" : "  Light Probes: НЕ ЗАПЕЧЕНЫ");
        float refL = AmbientAt(new Vector3(-1.2f, 1.2f, -0.6f), null);
        sb.AppendLine($"  Освещённость из проб, эталон (проход перед Shelf_03, 1.2 м): {refL:F3}");
        sb.AppendLine("  объект | точка выборки | внутри объёма проб | на NavMesh | освещённость из проб");
        var dyn = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)
            .Where(a => a.GetComponent<ShopperAgent>() || a.GetComponent<PromoterIK>() || a.GetComponent<ProximityDoor>() || a.GetComponent<AdStandMotion>())
            .OrderBy(a => a.name);
        int inside = 0, total = 0;
        foreach (var a in dyn)
        {
            var r = a.GetComponentInChildren<Renderer>();
            var pt = r.probeAnchor != null ? r.probeAnchor.position : r.bounds.center;
            bool inVol = lp != null && pb.Contains(pt);
            bool onNav = NavMesh.SamplePosition(a.transform.position, out var h, 0.3f, NavMesh.AllAreas);
            float L = AmbientAt(pt, r);
            total++; if (inVol) inside++;
            sb.AppendLine($"  {a.name} | {V(pt)} | {(inVol ? "да" : "НЕТ")} | {(a.GetComponent<NavMeshAgent>() || a.GetComponent<NavMeshObstacle>() ? (onNav ? "да" : "НЕТ") : "—")} | {L:F3} ({(refL > 0 ? 100f * L / refL : 0):F0}% эталона)");
        }
        sb.AppendLine($"  Внутри объёма проб: {inside} из {total}");
        sb.AppendLine();

        sb.AppendLine("6. НАГРУЗКА");
        var skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        int sv = skinned.Sum(s => s.sharedMesh ? s.sharedMesh.vertexCount : 0);
        int st = skinned.Sum(s => s.sharedMesh ? Enumerable.Range(0, s.sharedMesh.subMeshCount).Sum(i => (int)s.sharedMesh.GetIndexCount(i) / 3) : 0);
        sb.AppendLine($"  Скиннинг-мешей в сцене: {skinned.Length}, вершин {sv}, треугольников {st}");
        sb.AppendLine($"  Animator-ов: {Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Length}, Culling Mode = Cull Update Transforms");

        var path = Path.Combine(OutDir, "00_Отчёт_анимации.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Log("Протокол: " + path + "\n" + sb);
    }

    static string Cnd(AnimatorCondition c) => c.mode switch
    {
        AnimatorConditionMode.If => $"{c.parameter} = true",
        AnimatorConditionMode.IfNot => $"{c.parameter} = false",
        AnimatorConditionMode.Greater => $"{c.parameter} > {c.threshold}",
        AnimatorConditionMode.Less => $"{c.parameter} < {c.threshold}",
        _ => c.parameter
    };

    static float AmbientAt(Vector3 p, Renderer r)
    {
        LightProbes.GetInterpolatedProbe(p, r, out var sh);
        // L0-коэффициент ≈ средняя освещённость; яркость по формуле Rec.709
        return 0.2126f * sh[0, 0] + 0.7152f * sh[1, 0] + 0.0722f * sh[2, 0];
    }

    // ===================== ВСПОМОГАТЕЛЬНОЕ =====================

    public static T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(p);
    static string V(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
    static void Log(string s) => Debug.Log("[NEUROSHELF] " + s);

    static void Replace(Object o, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(o, path);
    }

    static void SaveScene()
    {
        var s = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(s);
        EditorSceneManager.SaveScene(s);
        AssetDatabase.SaveAssets();
    }

    static Material Mat(string name, Color c, bool transparent = false, bool unlit = false, float smooth = 0.5f)
    {
        var dir = name.StartsWith("M_NPC") || name.StartsWith("M_FX") ? NpcMatDir : PropMatDir;
        Directory.CreateDirectory(dir);
        var path = $"{dir}/{name}.mat";
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var m = Load<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetColor("_BaseColor", c);
        if (!unlit) m.SetFloat("_Smoothness", smooth);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        return m;
    }
}

// ============================================================================
//  Кадры для отчёта и замер FPS. Работают в Play Mode: анимация, IK и NavMesh
//  считаются только когда сцена запущена. Входит и выходит из Play Mode сам.
// ============================================================================
[InitializeOnLoad]
public static class NeuroshelfAnimCapture
{
    const string Key = "NEUROSHELF_ANIM_CAPTURE";
    public const string FpsKey = "NEUROSHELF_ANIM_FPS";
    const int ShotW = 1920, ShotH = 1080;

    class Step { public float at; public string label; public Action act; }
    static List<Step> steps;
    static int next;
    static double t0;
    static float standA0;
    static readonly StringBuilder Proto = new StringBuilder();

    // фиксированные ракурсы (одинаковые для пар кадров «до/после»)
    static Vector3 promoCamPos, promoCamLook;

    static NeuroshelfAnimCapture()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            if (SessionState.GetBool(FpsKey, false))
            {
                SessionState.SetBool(FpsKey, false);
                var probe = new GameObject("~FpsProbe").AddComponent<FpsProbe>();
                probe.reportFolder = "ЛР4"; probe.fileName = "12_Замер_FPS_с_NPC.txt";
            }
            if (SessionState.GetBool(Key, false)) { SessionState.SetBool(Key, false); Begin(); }
        };
    }

    public static void Start()
    {
        if (EditorApplication.isPlaying) { Begin(); return; }
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Begin()
    {
        Directory.CreateDirectory(NeuroshelfAnimationSetup.OutDir);
        Proto.Clear();
        Proto.AppendLine("NEUROSHELF — проверка анимации и IK в Play Mode (XR Device Simulator)");
        Proto.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}");
        steps = new List<Step>();
        next = 0; t0 = Time.time;

        var ctx = ExperimentContext.Find();
        var condV = NeuroshelfAnimationSetup.Load<ExperimentConditionSO>(NeuroshelfAnimationSetup.CondA_Volta);
        var condP = NeuroshelfAnimationSetup.Load<ExperimentConditionSO>(NeuroshelfAnimationSetup.CondA_Pura);
        var condC = NeuroshelfAnimationSetup.Load<ExperimentConditionSO>(NeuroshelfAnimationSetup.CondC);
        var prom = Object.FindFirstObjectByType<PromoterIK>();
        var door = Object.FindFirstObjectByType<ProximityDoor>();
        var stand = Object.FindFirstObjectByType<AdStandMotion>();
        var origin = GameObject.Find("XR Origin (VR)")?.transform;

        Add(0f, "подготовка", () =>
        {
            if (ctx != null && condV != null) ctx.SetCondition(condV);
            // игрок «встаёт» в проход перед продвигаемым стеллажом, лицом к промоутеру и полке.
            // Двигается только XR Origin целиком, Camera Y Offset не трогается.
            if (origin != null)
            {
                origin.position = NeuroshelfAnimationSetup.PlayerViewPos;
                var look = new Vector3(-2.5f, 0, -1.1f) - NeuroshelfAnimationSetup.PlayerViewPos;
                origin.rotation = Quaternion.LookRotation(look);
            }
            if (prom != null) prom.showDebug = true;
            if (door != null) door.manual = true;
            var cam = Camera.main;
            var xo = origin != null ? origin.GetComponent<Unity.XR.CoreUtils.XROrigin>() : null;
            Proto.AppendLine($"Камера игрока: высота глаз над полом {(cam ? cam.transform.position.y : -1):F3} м " +
                             $"(Camera Y Offset у XR Origin = {GetYOffset(origin):F2} м)");
            if (xo != null && cam != null)
                Proto.AppendLine($"  Camera Offset local y = {xo.CameraFloorOffsetObject.transform.localPosition.y:F3}, камера local y = {cam.transform.localPosition.y:F3}, " +
                                 $"режим трекинга = {xo.CurrentTrackingOriginMode} (запрошен {xo.RequestedTrackingOriginMode}), XR активен = {UnityEngine.XR.XRSettings.isDeviceActive}");
            if (prom != null)
            {
                var c = prom.transform.position + Vector3.up * 1.35f;
                var toPlayer = NeuroshelfAnimationSetup.PlayerViewPos - prom.transform.position; toPlayer.y = 0;
                var side = Quaternion.Euler(0, -35f, 0) * toPlayer.normalized;
                promoCamPos = prom.transform.position + side * 2.1f + Vector3.up * 1.55f;
                promoCamLook = c + Quaternion.Euler(0, -60f, 0) * toPlayer.normalized * 0.45f;
            }
        });

        Add(5f, "условие A, цель VOLTA", () =>
        {
            Shot("01_NPC_толпа_у_продвигаемого_стеллажа_условие_A", new Vector3(0.9f, 2.45f, 1.05f), new Vector3(-1.4f, 0.9f, -1.0f), 62f);
            Shot("02_Промоутер_IK_указывает_на_VOLTA_крупно", promoCamPos, promoCamLook, 48f);
            IkProto(prom, "VOLTA");
            CrowdProto("условие A");
            if (door != null) { Shot("04_Витрина_дверца_фаза1_закрыта", DoorCam(door, out var l), l, 55f); door.SetOpen(true); }
            if (stand != null) { Shot("06_Рекламный_стенд_фаза1", StandCam(stand, out var l2), l2, 50f); standA0 = stand.transform.Find("Rotor").localEulerAngles.y; }
        });

        Add(7f, "дверца открыта", () =>
        {
            if (door != null) Shot("05_Витрина_дверца_фаза2_открыта", DoorCam(door, out var l), l, 55f);
            if (stand != null) Shot("07_Рекламный_стенд_фаза2_через_2с", StandCam(stand, out var l2), l2, 50f);
            if (stand != null) Proto.AppendLine($"Стенд: поворот ротора между фазами за 2.0 с игрового времени: {Mathf.DeltaAngle(standA0, stand.transform.Find("Rotor").localEulerAngles.y):F1}° (ожидается 90°)");
            if (door != null)
            {
                var da = door.animator; var st = da.GetCurrentAnimatorStateInfo(0);
                Proto.AppendLine($"Дверца: состояние {(st.IsName("Opening") ? "Opening" : st.IsName("Closed") ? "Closed" : st.IsName("Closing") ? "Closing" : "?")}, " +
                                 $"угол петли {Mathf.DeltaAngle(0, door.transform.Find("Door_Hinge").localEulerAngles.y):F0}° (ожидается −100°)");
            }
            if (ctx != null && condP != null) ctx.SetCondition(condP);
        });

        Add(9.5f, "условие A, цель PURA", () =>
        {
            Shot("03_Промоутер_IK_указывает_на_PURA_тот_же_ракурс", promoCamPos, promoCamLook, 48f);
            IkProto(prom, "PURA");
            var cam = Camera.main;
            if (cam != null)
                Shot("08_Вид_от_первого_лица_1.65м", cam.transform.position, cam.transform.position + cam.transform.forward, cam.fieldOfView > 1 ? cam.fieldOfView : 60f);
            if (door != null) door.SetOpen(false);
            if (ctx != null && condC != null) ctx.SetCondition(condC);
        });

        Add(30f, "условие C", () =>
        {
            Shot("09_Контроль_условие_C_без_толпы", new Vector3(0.9f, 2.45f, 1.05f), new Vector3(-1.4f, 0.9f, -1.0f), 62f);
            CrowdProto("условие C (через 20 с после переключения)");
            if (prom != null) Proto.AppendLine($"Промоутер в условии C: вес IK {prom.armIK.weight:F2} (ожидается 0 — жест выключен)");
        });

        Add(31f, "завершение", () =>
        {
            if (prom != null) prom.showDebug = false;
            var path = Path.Combine(NeuroshelfAnimationSetup.OutDir, "10_Проверка_IK_и_состояний.txt");
            File.WriteAllText(path, Proto.ToString(), new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] Кадры и протокол → " + NeuroshelfAnimationSetup.OutDir + "\n" + Proto);
            EditorApplication.update -= Tick;
            EditorApplication.ExitPlaymode();
        });

        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Add(float at, string label, Action a) => steps.Add(new Step { at = at, label = label, act = a });

    static void Tick()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
        double t = Time.time - t0;
        while (next < steps.Count && t >= steps[next].at)
        {
            var s = steps[next++];
            try { s.act(); } catch (Exception e) { Debug.LogError($"[NEUROSHELF] Шаг «{s.label}»: {e}"); }
        }
    }

    static float GetYOffset(Transform origin)
    {
        if (origin == null) return -1f;
        var x = origin.GetComponent<Unity.XR.CoreUtils.XROrigin>();
        return x != null ? x.CameraYOffset : -1f;
    }

    static void IkProto(PromoterIK p, string label)
    {
        if (p == null) return;
        p.Measure(out var dist, out var err);
        Proto.AppendLine($"IK, цель {label}: вес {p.armIK.weight:F2}; кисть — цель IK {dist * 100f:F1} см; " +
                         $"отклонение руки от направления на товар {err:F1}°; позиция цели {p.ikTarget.position:F2}");
    }

    static void CrowdProto(string label)
    {
        var shoppers = Object.FindObjectsByType<ShopperAgent>(FindObjectsSortMode.None);
        var promo = ExperimentContext.Find()?.PromotedShelf;
        int at = 0, walking = 0;
        foreach (var s in shoppers)
        {
            var d = s.transform.position - (promo != null ? promo.position : Vector3.zero); d.y = 0;
            if (promo != null && d.magnitude < 2.0f) at++;
            if (!s.IsBrowsing) walking++;
        }
        Proto.AppendLine($"Толпа, {label}: у продвигаемого стеллажа {at} из {shoppers.Length}, в пути {walking}");
    }

    static Vector3 DoorCam(ProximityDoor d, out Vector3 look)
    {
        var t = d.transform;
        look = t.position + Vector3.up * 1.0f + t.forward * 0.2f;
        return t.position + t.forward * 2.3f + t.right * 0.9f + Vector3.up * 1.45f;
    }

    static Vector3 StandCam(AdStandMotion s, out Vector3 look)
    {
        var t = s.transform;
        look = t.position + Vector3.up * 1.4f;
        return t.position + new Vector3(1.6f, 1.6f, 1.6f);
    }

    public static void Shot(string file, Vector3 pos, Vector3 look, float fov)
    {
        var path = Path.Combine(NeuroshelfAnimationSetup.OutDir, file + ".png");
        var go = new GameObject("~NS_Capture") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.03f; cam.farClipPlane = 60f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        }
    }

    // ---------- окна редактора (эксперимент) ----------

    public static void GrabEditorWindows()
    {
        Directory.CreateDirectory(NeuroshelfAnimationSetup.OutDir);
        var prom = Object.FindFirstObjectByType<PromoterIK>();
        var door = Object.FindFirstObjectByType<ProximityDoor>();
        var q = new List<(int wait, Action act)>
        {
            (0, () => { if (prom) Selection.activeGameObject = prom.gameObject; EditorApplication.ExecuteMenuItem("Window/Animation/Animator"); }),
            (40, () => Grab("AnimatorControllerTool", "13_Animator_граф_промоутера")),
            (0, () => { var s = Object.FindFirstObjectByType<ShopperAgent>(); if (s) Selection.activeGameObject = s.gameObject; }),
            (40, () => Grab("AnimatorControllerTool", "14_Animator_граф_покупателя")),
            (0, () => { if (door) Selection.activeGameObject = door.gameObject; }),
            (40, () => Grab("AnimatorControllerTool", "15_Animator_граф_дверцы")),
            (0, () => { if (prom) Selection.activeGameObject = prom.armIK.gameObject; EditorApplication.ExecuteMenuItem("Window/General/Inspector"); }),
            (40, () => Grab("InspectorWindow", "16_Inspector_Two_Bone_IK")),
        };
        int i = 0, wait = 0;
        EditorApplication.CallbackFunction cb = null;
        cb = () =>
        {
            if (wait-- > 0) return;
            if (i >= q.Count) { EditorApplication.update -= cb; Debug.Log("[NEUROSHELF] Окна редактора сняты."); return; }
            try { q[i].act(); } catch (Exception e) { Debug.LogWarning(e.Message); }
            i++; wait = i < q.Count ? q[i].wait : 0;
        };
        EditorApplication.update += cb;
    }

    static void Grab(string typeName, string file)
    {
        var w = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(x => x.GetType().Name == typeName);
        if (w == null) { Debug.LogWarning($"[NEUROSHELF] Окно {typeName} не найдено."); return; }
        w.Focus(); w.Repaint();
        var r = w.position; float k = EditorGUIUtility.pixelsPerPoint;
        int W = Mathf.RoundToInt(r.width * k), H = Mathf.RoundToInt(r.height * k);
        var px = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(r.x * k, r.y * k), W, H);
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(NeuroshelfAnimationSetup.OutDir, file + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
