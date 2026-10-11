// NEUROSHELF — взаимодействие: захват товаров, дистанционный выбор лучом, точки фиксации (сокеты),
// физика упаковок, слои коллизий, события для будущего слоя аналитики, проверка и запись.
// Отдельный файл; остальные скрипты Editor не трогает.
//
// Меню NEUROSHELF → Взаимодействие:
//   7.0 Полный цикл        → 7.1 … 7.5 (обычно нажимать только её), затем 7.6
//   7.1 Слои и физика      → слои Products / NPC, матрица коллизий, Interaction Layer «Product»,
//                            физические материалы, капсулы NPC, маска NavMeshSurface
//   7.2 Товары             → корень с центром у основания, Rigidbody, Box/Capsule, точка захвата,
//                            XR Grab Interactable (бросок), ProductItem + события Select
//   7.3 Руки               → Left/Right Controller, Tracked Pose Driver, Near-Far Interactor, модель контроллера
//   7.4 Сокеты             → ячейки полок (возврат) и корзина у пояса (покупка, 6 ячеек)
//   7.5 Проверка → отчёт   → 00_Отчёт_взаимодействия.txt + кадры 01–02, 07–09
//   7.6 Проверка в Play Mode (~40 с) → сценарий + физика + NPC → 10_Проверка_в_Play_Mode.txt + кадры 03–06
//   7.8 Запись Unity Recorder (~30 с) → Recordings/ns_interaction.mp4
//   7.9 Снять окна редактора (Inspector)
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР5\

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using Object = UnityEngine.Object;
using TrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;

[InitializeOnLoad]
public static class NeuroshelfInteractionSetup
{
    // ===================== ПАРАМЕТРЫ =====================

    const string Menu = "NEUROSHELF/Взаимодействие/";

    public const int LayerProducts = 8;
    public const int LayerNPC = 9;
    const int InteractionLayerProduct = 1;          // бит 1 в XRI Interaction Layers

    const string SA = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/";
    const string InputActionsPath = SA + "XRI Default Input Actions.inputactions";
    const string PhysDir = "Assets/_Project/Physics";
    const string MatDir = "Assets/_Project/Materials/Interaction";

    static readonly string[] ProductNames =
    {
        "Can_VOLTA", "Can_FERRO", "Bottle_PURA",
        "Bottle_NUBO", "Can_ZEST", "Pack_KRISP", "Pack_OKTA", "Bottle_LUMEN",
        "Bottle_AURA", "Pack_MIRA", "Pack_NORDA", "Pack_GRANO",
    };
    static readonly float[] TierY = { 0.60f, 1.10f, 1.60f };
    const float BoardHalf = 0.025f;

    // Физика упаковки. Масса — по содержимому, drag — по площади и материалу.
    struct Pack
    {
        public float mass, drag, angDrag, smooth; public string material;
        public float neck;   // доля высоты, на которой точка захвата (бутылка — горлышко)
    }
    static Pack Phys(PackagingType t, Vector3 size) => t switch
    {
        // ПЭТ-бутылка 0.5 л: 500 г воды + ~25 г тары; гладкая, обтекаемая
        PackagingType.Bottle => new Pack { mass = 0.53f, drag = 0.05f, angDrag = 0.10f, smooth = 8f, material = "PM_PET", neck = 0.85f },
        // алюминиевая банка 0.33 л: 330 г + ~15 г
        PackagingType.Can => new Pack { mass = 0.35f, drag = 0.05f, angDrag = 0.05f, smooth = 12f, material = "PM_Aluminium", neck = 0.5f },
        // картонная коробка с сухим продуктом: ~0.10 кг на литр объёма, большая площадь → высокий drag
        _ => new Pack { mass = Mathf.Clamp(size.x * size.y * size.z * 1000f * 0.10f, 0.15f, 0.50f), drag = 0.35f, angDrag = 0.50f, smooth = 16f, material = "PM_Cardboard", neck = 0.5f },
    };

    public static string OutDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР5"));
    static string RecDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "7.0 Полный цикл (слои → товары → руки → сокеты → проверка)", priority = 300)]
    public static void All()
    {
        SetupLayers(); SetupProducts(); SetupHands(); SetupSockets();
        Save();
        Verify();
        Debug.Log("[NEUROSHELF] Взаимодействие настроено. Дальше: 7.6 (проверка в Play Mode).");
    }

    [MenuItem(Menu + "7.1 Слои, физика, NPC", priority = 301)] static void M1() { SetupLayers(); Save(); }
    [MenuItem(Menu + "7.2 Товары: Rigidbody, коллайдеры, захват", priority = 302)] static void M2() { SetupProducts(); Save(); }
    [MenuItem(Menu + "7.3 Руки: Near-Far Interactor", priority = 303)] static void M3() { SetupHands(); Save(); }
    [MenuItem(Menu + "7.4 Сокеты: полки и корзина", priority = 304)] static void M4() { SetupSockets(); Save(); }
    [MenuItem(Menu + "7.5 Проверка → отчёт + кадры", priority = 305)] static void M5() => Verify();
    [MenuItem(Menu + "7.6 Проверка в Play Mode (~40 с)", priority = 320)] static void M6() => StartPlay("check");
    [MenuItem(Menu + "7.7 Замер FPS с взаимодействием (10 с)", priority = 320)] static void M7() => StartPlay("fps");
    [MenuItem(Menu + "7.8 Запись Unity Recorder (~30 с)", priority = 321)] static void M8() => StartPlay("record");
    [MenuItem(Menu + "7.9 Снять окна редактора (Inspector)", priority = 322)] static void M9() => GrabInspectors();
    [MenuItem(Menu + "7.97 Карта зала (габариты объектов)", priority = 397)]
    static void MapDump()
    {
        var sb = new StringBuilder("[NS-MAP]\n");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                     .Where(t => t.parent == null || t.parent.parent == null || t.name.StartsWith("Shelf_0") || t.name.StartsWith("Rack")))
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            sb.AppendLine(string.Format(ci, "{0,-40} min=({1:0.00},{2:0.00},{3:0.00}) max=({4:0.00},{5:0.00},{6:0.00})", HPath(t), b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z));
        }
        Debug.Log(sb.ToString());
    }

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

    // ===================== 7.1 СЛОИ И ФИЗИКА =====================

    static void SetupLayers()
    {
        // физические слои
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        void SetLayer(int i, string n)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue) || p.stringValue == n) p.stringValue = n;
            else Debug.LogWarning($"[NEUROSHELF] Слой {i} занят «{p.stringValue}», ожидался «{n}».");
        }
        SetLayer(LayerProducts, "Products");
        SetLayer(LayerNPC, "NPC");
        tm.ApplyModifiedPropertiesWithoutUndo();

        // матрица: товар проходит сквозь NPC (не застревает и не толкает агента), NPC между собой не сталкиваются
        Physics.IgnoreLayerCollision(LayerProducts, LayerNPC, true);
        Physics.IgnoreLayerCollision(LayerNPC, LayerNPC, true);

        // XRI Interaction Layers — отдельная от физики система: кто с кем может взаимодействовать
        var ils = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/XRI/Settings/Resources/InteractionLayerSettings.asset");
        if (ils != null)
        {
            var so = new SerializedObject(ils);
            var names = so.FindProperty("m_LayerNames");
            if (names != null && names.arraySize > InteractionLayerProduct)
            {
                names.GetArrayElementAtIndex(InteractionLayerProduct).stringValue = "Product";
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // физические материалы упаковок
        Directory.CreateDirectory(PhysDir);
        PhysMat("PM_Cardboard", 0.55f, 0.65f, 0.05f);   // картон шершавый, почти не отскакивает
        PhysMat("PM_PET", 0.35f, 0.45f, 0.15f);         // гладкий пластик, немного пружинит
        PhysMat("PM_Aluminium", 0.30f, 0.40f, 0.20f);   // металл, звонкий отскок

        // NPC: капсула на слое NPC + кинематический Rigidbody (движущийся коллайдер без Rigidbody
        // PhysX считает статическим и перестраивает при каждом сдвиге — дорого)
        foreach (var npc in Npcs())
        {
            Undo.RecordObject(npc, "NPC layer");
            foreach (var t in npc.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerNPC;
            var cap = Ensure<CapsuleCollider>(npc);
            cap.direction = 1; cap.radius = 0.25f; cap.height = 1.75f; cap.center = new Vector3(0f, 0.875f, 0f);
            var rb = Ensure<Rigidbody>(npc);
            rb.isKinematic = true; rb.useGravity = false;
        }

        // Пол и потолок из блокаута — плоские Mesh Collider. Заменяем на Box: примитив дешевле,
        // а толщина 0.2 м под полом не даёт брошенному товару «провалиться» сквозь плоскость
        foreach (var n in new[] { "Floor", "Ceiling" })
        {
            var g = GameObject.Find(n);
            var mc = g != null ? g.GetComponent<MeshCollider>() : null;
            if (mc == null) continue;
            var b = g.GetComponent<Renderer>().bounds;
            Undo.DestroyObjectImmediate(mc);
            var bc = Undo.AddComponent<BoxCollider>(g);
            const float th = 0.2f;
            var wc = n == "Floor" ? new Vector3(b.center.x, b.max.y - th * 0.5f, b.center.z) : new Vector3(b.center.x, b.min.y + th * 0.5f, b.center.z);
            bc.center = g.transform.InverseTransformPoint(wc);
            var ls = g.transform.lossyScale;
            bc.size = new Vector3(b.size.x / Mathf.Abs(ls.x), th / Mathf.Abs(ls.y), b.size.z / Mathf.Abs(ls.z));
        }

        // NavMesh строится по физическим коллайдерам: товары на полу и сами NPC не должны стать «стенами»
        foreach (var s in Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(s, "NavMesh mask");
            s.layerMask &= ~((1 << LayerProducts) | (1 << LayerNPC));
        }
        Debug.Log("[NEUROSHELF] 7.1 Слои: Products=8, NPC=9; Products×NPC выкл.; Interaction Layer 1 = Product; капсулы NPC: " + Npcs().Count());
    }

    static IEnumerable<GameObject> Npcs()
    {
        var list = Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None).Select(a => a.gameObject).ToList();
        var prom = GameObject.Find("NPC_Promoter");
        if (prom != null && !list.Contains(prom)) list.Add(prom);
        return list.OrderBy(g => g.name);
    }

    static PhysicsMaterial PhysMat(string name, float dyn, float stat, float bounce)
    {
        var path = $"{PhysDir}/{name}.physicMaterial";
        var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (m == null) { m = new PhysicsMaterial(name); AssetDatabase.CreateAsset(m, path); }
        m.dynamicFriction = dyn; m.staticFriction = stat; m.bounciness = bounce;
        m.frictionCombine = PhysicsMaterialCombine.Average; m.bounceCombine = PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ===================== 7.2 ТОВАРЫ =====================

    static PackagingType TypeOf(string n) =>
        n.StartsWith("Bottle_") ? PackagingType.Bottle : n.StartsWith("Can_") ? PackagingType.Can : PackagingType.Box;

    static GameObject FindProduct(string n) =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
              .Where(t => t.name == n && (t.GetComponent<ProductItem>() != null || t.GetComponent<MeshRenderer>() != null))
              .Select(t => t.gameObject).FirstOrDefault();

    static void SetupProducts()
    {
        // пустые корни от прерванного запуска
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (ProductNames.Contains(t.name) && t.GetComponentInChildren<Renderer>(true) == null && t.GetComponent<ProductItem>() == null)
                Undo.DestroyObjectImmediate(t.gameObject);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.EndsWith("_Mesh") && ProductNames.Contains(t.name.Substring(0, t.name.Length - 5)) &&
                (t.parent == null || t.parent.GetComponent<ProductItem>() == null))
                t.name = t.name.Substring(0, t.name.Length - 5);
        int done = 0;
        foreach (var n in ProductNames)
        {
            var go = FindProduct(n);
            if (go == null) { Debug.LogWarning("[NEUROSHELF] Нет товара " + n); continue; }
            var root = EnsureRoot(go);
            ConfigureProduct(root, n);
            done++;
        }
        Relayout();
        Debug.Log($"[NEUROSHELF] 7.2 Товары настроены: {done} из {ProductNames.Length}");
    }

    // Выкладка без пересечений, по планограмме условия A.
    // Найдено при добавлении коллайдеров: VOLTA и FERRO на верхнем ярусе стояли друг в друге,
    // на стыке стеллажей 03/04 и большей частью на 04 — под холодным светом уценки 5000 K,
    // хотя продвигаемый стеллаж по условиям эксперимента и ЛР3 — Shelf_03 (тёплый акцент 3000 K).
    // VOLTA была вдавлена в боковую стенку стеллажа, MIRA и NORDA пересекались.
    // Товары одного яруса делят ширину полки (2.4 м) поровну, порядок слева направо — как в таблице.
    public const float ShelfDepthZ = -1.45f;   // центр товара: 0.25 м от передней кромки полки
    static readonly (string shelf, string[][] tiers)[] Planogram =
    {
        // Shelf_03 — продвигаемый (x −2.4…0): дорогие позиции на уровне глаз
        ("Shelf_03", new[] { new[] { "Bottle_AURA" }, new[] { "Bottle_NUBO", "Pack_OKTA", "Bottle_LUMEN" }, new[] { "Can_VOLTA", "Can_FERRO", "Bottle_PURA" } }),
        // Shelf_04 — уценка (x 0…2.4): базовые товары внизу
        ("Shelf_04", new[] { new[] { "Pack_MIRA", "Pack_NORDA", "Pack_GRANO" }, new[] { "Can_ZEST", "Pack_KRISP" }, new string[0] }),
    };

    static void Relayout()
    {
        var items = new List<ProductItem>();
        foreach (var (shelf, tiers) in Planogram)
        {
            float x0 = shelf == "Shelf_03" ? -2.4f : 0f, w = 2.4f;
            for (int tier = 0; tier < 3; tier++)
            {
                var names = tiers[tier];
                for (int k = 0; k < names.Length; k++)
                {
                    var it = FindProduct(names[k])?.GetComponent<ProductItem>();
                    if (it == null) continue;
                    items.Add(it);
                    var t = it.transform;
                    Undo.RecordObject(t, "relayout");
                    t.SetPositionAndRotation(new Vector3(x0 + w * (k + 1) / (names.Length + 1), TierY[tier] + BoardHalf, ShelfDepthZ), Quaternion.identity);
                    it.tier = tier; it.tierHeight = TierY[tier];
                    EditorUtility.SetDirty(it);
                    if (it.homeSlot != null)
                    {
                        Undo.RecordObject(it.homeSlot.transform, "relayout");
                        it.homeSlot.transform.SetPositionAndRotation(t.position, t.rotation);
                    }
                }
            }
        }
        // проверка: пересечения коллайдеров товаров
        int overlaps = 0;
        var cols = items.Select(i => i.GetComponent<Collider>()).ToList();
        Physics.SyncTransforms();
        for (int i = 0; i < cols.Count; i++) for (int j = i + 1; j < cols.Count; j++)
            if (cols[i].bounds.Intersects(cols[j].bounds)) { overlaps++; Debug.LogWarning($"[NEUROSHELF] Пересекаются {cols[i].name} и {cols[j].name}"); }
        Debug.Log($"[NEUROSHELF] Выкладка: пересечений товаров {overlaps}");
    }

    // Точка привязки мешей из Blender вынесена за модель (у NUBO на 2.8 м).
    // Делаем корень с центром у основания, меш — дочерним. Имя товара остаётся на корне,
    // поэтому промоутер (PromoterIK) и условия эксперимента находят его как раньше.
    static GameObject EnsureRoot(GameObject go)
    {
        if (go.GetComponent<ProductItem>() != null) return go;
        // товары лежат внутри экземпляра FBX-префаба NEUROSHELF_Products; Unity не даёт менять
        // иерархию внутри префаба, поэтому экземпляр распаковывается в обычные объекты сцены
        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            Debug.Log("[NEUROSHELF] Распакован экземпляр префаба " + outer.name);
            PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }
        var r = go.GetComponent<Renderer>();
        var b = r.bounds;
        var root = new GameObject(go.name);
        Undo.RegisterCreatedObjectUndo(root, "Product root");
        root.transform.SetParent(go.transform.parent, false);
        root.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
        root.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y, b.center.z), Quaternion.identity);
        Undo.SetTransformParent(go.transform, root.transform, "Product root");
        go.name = go.name + "_Mesh";
        foreach (var mc in go.GetComponents<MeshCollider>()) Undo.DestroyObjectImmediate(mc);
        return root;
    }

    static void ConfigureProduct(GameObject root, string n)
    {
        var type = TypeOf(n);
        var r = root.GetComponentInChildren<MeshRenderer>();
        var b = r.bounds;
        var size = b.size;
        var ph = Phys(type, size);

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = LayerProducts;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);   // подвижный объект не может быть в лайтмапе
        }
        r.lightProbeUsage = LightProbeUsage.BlendProbes;               // свет — из Light Probes ЛР3
        r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

        // Rigidbody
        var rb = Ensure<Rigidbody>(root);
        rb.mass = ph.mass;
        rb.linearDamping = ph.drag;
        rb.angularDamping = ph.angDrag;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Speculative: работает и для кинематического тела (товар в сокете кинематический),
        // и не даёт быстро брошенной упаковке пролететь сквозь тонкую полку
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // Коллайдер — только примитив
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>($"{PhysDir}/{ph.material}.physicMaterial");
        foreach (var c in root.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
        Collider col;
        if (type == PackagingType.Bottle)
        {
            var cap = Undo.AddComponent<CapsuleCollider>(root);
            cap.direction = 1;
            cap.radius = Mathf.Max(size.x, size.z) * 0.5f;
            cap.height = size.y;
            cap.center = new Vector3(0f, size.y * 0.5f, 0f);
            col = cap;
        }
        else
        {
            // банка: высота ≈ диаметру, капсула выродилась бы в шар и катилась — берём Box
            var box = Undo.AddComponent<BoxCollider>(root);
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            col = box;
        }
        col.sharedMaterial = pm;

        // Точка захвата (Attach Transform)
        var grip = root.transform.Find("Attach_Grip");
        if (grip == null)
        {
            grip = new GameObject("Attach_Grip").transform;
            Undo.RegisterCreatedObjectUndo(grip.gameObject, "grip");
            grip.SetParent(root.transform, false);
        }
        grip.gameObject.layer = LayerProducts;
        // Стеллажи 03/04 открыты в проход (+z), покупатель тянется к полке, глядя в −z.
        // Поворот точки захвата 180° вокруг Y: товар ложится в руку, не разворачиваясь, лицевой стороной к покупателю.
        grip.localRotation = Quaternion.Euler(0f, 180f, 0f);
        grip.localPosition = type switch
        {
            PackagingType.Bottle => new Vector3(0f, size.y * ph.neck, 0f),          // горлышко
            PackagingType.Can => new Vector3(0f, size.y * 0.5f, 0f),                // обхват корпуса
            _ => new Vector3(0f, size.y * 0.5f, size.z * 0.5f),                      // центр лицевой грани (к проходу)
        };

        // XR Grab Interactable
        var grab = Ensure<XRGrabInteractable>(root);
        grab.attachTransform = grip;
        grab.useDynamicAttach = false;
        grab.interactionLayers = 1 << InteractionLayerProduct;
        grab.selectMode = InteractableSelectMode.Single;
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;  // держимый товар упирается в полку, а не проходит сквозь
        grab.trackPosition = true; grab.trackRotation = true;
        grab.smoothPosition = true; grab.smoothPositionAmount = ph.smooth;
        grab.smoothRotation = true; grab.smoothRotationAmount = ph.smooth;
        grab.farAttachMode = UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Near;   // взятый лучом товар притягивается в руку
        grab.attachEaseInTime = 0.15f;
        grab.throwOnDetach = true;
        grab.throwSmoothingDuration = 0.20f;   // усреднение скорости руки за последние 0.2 с — убирает дрожь трекинга
        grab.throwVelocityScale = 1.0f;        // 1:1 — товар уходит со скоростью руки (по умолчанию в XRI 1.5)
        grab.throwAngularVelocityScale = 1.0f;
        grab.retainTransformParent = true;
        EditorUtility.SetDirty(grab);

        // ProductItem + точки расширения на событиях Select
        var item = Ensure<ProductItem>(root);
        item.productId = n.Substring(n.IndexOf('_') + 1);
        item.packaging = type;
        float bottom = b.min.y;
        item.tier = Enumerable.Range(0, 3).OrderBy(i => Mathf.Abs(bottom - (TierY[i] + BoardHalf))).First();
        item.tierHeight = TierY[item.tier];
        EditorUtility.SetDirty(item);

        RemoveListeners(grab.selectEntered); RemoveListeners(grab.selectExited);
        UnityEventTools.AddPersistentListener(grab.selectEntered, item.OnSelectEntered);
        UnityEventTools.AddPersistentListener(grab.selectExited, item.OnSelectExited);
    }

    static void RemoveListeners<T>(UnityEngine.Events.UnityEvent<T> ev)
    {
        for (int i = ev.GetPersistentEventCount() - 1; i >= 0; i--)
            if (ev.GetPersistentTarget(i) is ProductItem) UnityEventTools.RemovePersistentListener(ev, i);
    }

    // ===================== 7.3 РУКИ =====================

    static void SetupHands()
    {
        var origin = GameObject.Find("XR Origin (VR)");
        var offset = origin != null ? origin.transform.Find("Camera Offset") : null;
        if (offset == null) { Debug.LogError("[NEUROSHELF] Нет XR Origin (VR)/Camera Offset"); return; }
        Hand(offset, "Left");
        Hand(offset, "Right");
        Debug.Log("[NEUROSHELF] 7.3 Руки: Near-Far Interactor на обоих контроллерах");
    }

    static void Hand(Transform offset, string side)
    {
        var ctl = offset.Find(side + " Controller");
        if (ctl == null)
        {
            ctl = new GameObject(side + " Controller").transform;
            Undo.RegisterCreatedObjectUndo(ctl.gameObject, "controller");
            ctl.SetParent(offset, false);
            ctl.localPosition = new Vector3(side == "Left" ? -0.2f : 0.2f, -0.5f, 0.3f);
        }

        var tpd = Ensure<TrackedPoseDriver>(ctl.gameObject);
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        tpd.positionInput = new InputActionProperty(ActionRef($"XRI {side}/Position"));
        tpd.rotationInput = new InputActionProperty(ActionRef($"XRI {side}/Rotation"));
        tpd.trackingStateInput = new InputActionProperty(ActionRef($"XRI {side}/Tracking State"));
        EditorUtility.SetDirty(tpd);

        var nf = ctl.GetComponentInChildren<NearFarInteractor>(true);
        if (nf == null)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{SA}Prefabs/Interactors/{side}_NearFarInteractor.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, ctl);
            Undo.RegisterCreatedObjectUndo(inst, "near-far");
            inst.name = "Near-Far Interactor";
            nf = inst.GetComponent<NearFarInteractor>();
        }
        if (ctl.Find("Controller Model") == null)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{SA}Prefabs/Controllers/XR Controller {side}.prefab");
            if (pf != null)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(pf, ctl);
                Undo.RegisterCreatedObjectUndo(m, "model");
                m.name = "Controller Model";
            }
        }

        // руки берут товары (Product) и всё базовое (Default — на будущее: UI, кнопки)
        nf.interactionLayers = (1 << 0) | (1 << InteractionLayerProduct);
        var near = nf.GetComponent<SphereInteractionCaster>();
        if (near != null)
        {
            near.physicsLayerMask = 1 << LayerProducts;     // ближний захват ищет только товары
            near.castRadius = 0.10f;
            EditorUtility.SetDirty(near);
            PrefabUtility.RecordPrefabInstancePropertyModifications(near);
        }
        var far = nf.GetComponent<CurveInteractionCaster>();
        if (far != null)
        {
            // луч упирается в стены и стеллажи (Default), берёт товары; NPC и сокеты (Ignore Raycast) пропускает,
            // чтобы проходящий покупатель не «перехватывал» луч в момент притягивания
            far.raycastMask = (1 << 0) | (1 << 5) | (1 << LayerProducts);
            far.castDistance = 6f;
            EditorUtility.SetDirty(far);
            PrefabUtility.RecordPrefabInstancePropertyModifications(far);
        }
        EditorUtility.SetDirty(nf);
        PrefabUtility.RecordPrefabInstancePropertyModifications(nf);
    }

    static InputActionReference ActionRef(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(InputActionsPath).OfType<InputActionReference>().FirstOrDefault(r => r.name == name);

    // ===================== 7.4 СОКЕТЫ =====================

    static void SetupSockets()
    {
        var inter = GameObject.Find("Interaction") ?? new GameObject("Interaction");
        Undo.RegisterCreatedObjectUndo(inter, "Interaction");
        var shelfRoot = Child(inter.transform, "ShelfSlots");

        int slots = 0;
        foreach (var n in ProductNames)
        {
            var go = FindProduct(n);
            var item = go != null ? go.GetComponent<ProductItem>() : null;
            if (item == null) continue;
            var grab = go.GetComponent<XRGrabInteractable>();
            var slot = Child(shelfRoot, "Slot_" + item.productId);
            // ячейку ставим туда, где товар стоит по планограмме (если товар сейчас на полке)
            if (item.homeSlot == null || item.homeSlot.startingSelectedInteractable == grab)
                slot.SetPositionAndRotation(go.transform.position, go.transform.rotation);
            slot.gameObject.layer = 2;   // Ignore Raycast: триггер ячейки не должен перехватывать луч

            var size = go.GetComponent<Collider>().bounds.size;
            var trig = Ensure<BoxCollider>(slot.gameObject);
            trig.isTrigger = true;
            trig.size = new Vector3(size.x + 0.06f, size.y + 0.04f, size.z + 0.06f);
            trig.center = new Vector3(0f, size.y * 0.5f, 0f);

            var s = Ensure<ProductSocket>(slot.gameObject);
            s.kind = SocketKind.Shelf;
            s.acceptOnlyId = item.productId;    // на своё место планограммы принимается только свой товар
            s.slotIndex = 0;
            s.interactionLayers = 1 << InteractionLayerProduct;
            s.startingSelectedInteractable = grab;
            s.showInteractableHoverMeshes = true;
            s.recycleDelayTime = 0.5f;
            s.socketScaleMode = SocketScaleMode.None;
            EditorUtility.SetDirty(s);
            item.homeSlot = s;
            EditorUtility.SetDirty(item);
            slots++;
        }

        BuildBasket(inter.transform);
        Debug.Log($"[NEUROSHELF] 7.4 Сокеты: ячеек полки {slots}, ячеек корзины 6");
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

    static void BuildBasket(Transform parent)
    {
        var basket = Child(parent, "Basket");
        Directory.CreateDirectory(MatDir);
        var matPath = MatDir + "/M_Basket.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetColor("_BaseColor", new Color(0.78f, 0.13f, 0.10f));   // красная пластиковая корзина
        mat.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(mat);

        const float W = 0.42f, D = 0.30f, H = 0.22f, T = 0.012f;
        var visual = Child(basket, "Visual");
        void Part(string n, Vector3 pos, Vector3 scale)
        {
            var p = visual.Find(n);
            if (p == null)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);   // BoxCollider идёт в комплекте
                g.name = n;
                Undo.RegisterCreatedObjectUndo(g, n);
                p = g.transform;
                p.SetParent(visual, false);
            }
            p.localPosition = pos; p.localRotation = Quaternion.identity; p.localScale = scale;
            var mr = p.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.lightProbeUsage = LightProbeUsage.BlendProbes;
            mr.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(p.gameObject, 0);
        }
        Part("Bottom", new Vector3(0, T * 0.5f, 0), new Vector3(W, T, D));
        Part("Wall_Front", new Vector3(0, H * 0.5f, D * 0.5f), new Vector3(W, H, T));
        Part("Wall_Back", new Vector3(0, H * 0.5f, -D * 0.5f), new Vector3(W, H, T));
        Part("Wall_Left", new Vector3(-W * 0.5f, H * 0.5f, 0), new Vector3(T, H, D));
        Part("Wall_Right", new Vector3(W * 0.5f, H * 0.5f, 0), new Vector3(T, H, D));

        // корзина двигается за покупателем → кинематический Rigidbody, иначе её коллайдеры — «статические, но движущиеся»
        var rb = Ensure<Rigidbody>(basket.gameObject);
        rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;

        var slotsRoot = Child(basket, "Slots");
        int k = 0;
        foreach (var z in new[] { 0.07f, -0.07f })
            foreach (var x in new[] { -0.13f, 0f, 0.13f })
            {
                k++;
                var s = Child(slotsRoot, $"BasketSlot_{k}");
                s.localPosition = new Vector3(x, T, z); s.localRotation = Quaternion.Euler(0f, 180f, 0f);   // этикеткой к покупателю
                s.gameObject.layer = 2;
                var trig = Ensure<BoxCollider>(s.gameObject);
                trig.isTrigger = true;
                trig.size = new Vector3(0.13f, 0.40f, 0.14f);
                trig.center = new Vector3(0f, 0.20f, 0f);
                var sock = Ensure<ProductSocket>(s.gameObject);
                sock.kind = SocketKind.Basket;
                sock.acceptOnlyId = "";
                sock.slotIndex = k;
                sock.interactionLayers = 1 << InteractionLayerProduct;
                sock.showInteractableHoverMeshes = true;
                sock.recycleDelayTime = 1.0f;
                sock.socketScaleMode = SocketScaleMode.None;
                EditorUtility.SetDirty(sock);
            }

        var anchor = Ensure<BodyAnchor>(basket.gameObject);
        anchor.head = Camera.main != null ? Camera.main.transform : null;
        anchor.offset = new Vector2(-0.32f, 0.12f);   // слева у пояса: при наклоне к нижней полке корзина не въезжает в стеллаж
        anchor.waistHeight = 0.85f;
        var origin = GameObject.Find("XR Origin (VR)");
        anchor.floor = origin != null ? origin.transform : null;
        anchor.SnapNow();
        EditorUtility.SetDirty(anchor);
    }

    // ===================== 7.5 ПРОВЕРКА =====================

    static void Verify()
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        void L(string s = "") => sb.AppendLine(s);
        L("NEUROSHELF — протокол настройки взаимодействия " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        L("Unity " + Application.unityVersion + " · XR Interaction Toolkit " + PkgVersion("com.unity.xr.interaction.toolkit"));
        L();

        L("1. ИНТЕРАКТОРЫ, ДОСТУПНЫЕ В УСТАНОВЛЕННОЙ ВЕРСИИ XRI (рефлексия по сборке пакета)");
        foreach (var t in typeof(XRBaseInteractor).Assembly.GetTypes()
                     .Where(t => typeof(XRBaseInteractor).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic).OrderBy(t => t.Name))
            L($"   {t.Name,-22} {(t.IsDefined(typeof(ObsoleteAttribute), false) ? "[Obsolete]" : "доступен")}");
        L();

        L("2. РУКИ");
        foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None).OrderBy(x => x.handedness.ToString()))
        {
            var near = nf.GetComponent<SphereInteractionCaster>();
            var far = nf.GetComponent<CurveInteractionCaster>();
            var tpd = nf.transform.parent.GetComponent<TrackedPoseDriver>();
            L($"   {HPath(nf.transform)}  ({nf.handedness})");
            L($"      Near Caster: Sphere r={(near != null ? near.castRadius : 0f).ToString("0.00", ci)} м, слои: {MaskNames(near != null ? (int)near.physicsLayerMask : 0)}");
            L($"      Far Caster:  Curve дальность {(far != null ? far.castDistance : 0f).ToString("0.0", ci)} м, слои: {MaskNames(far != null ? (int)far.raycastMask : 0)}");
            L($"      Interaction Layers: {nf.interactionLayers.value} · Tracked Pose Driver: {(tpd != null ? tpd.positionInput.reference?.name + " / " + tpd.rotationInput.reference?.name : "НЕТ")}");
        }
        L();

        L("3. ТОВАРЫ");
        L("   товар        упаковка ярус  масса  drag  angDrag  коллайдер                 Mesh-колл. Static лайтмапа пробы  точка захвата                       бросок");
        int meshCols = 0, staticCnt = 0;
        foreach (var n in ProductNames)
        {
            var go = FindProduct(n); var item = go?.GetComponent<ProductItem>();
            if (item == null) { L($"   {n} — НЕ НАСТРОЕН"); continue; }
            var rb = go.GetComponent<Rigidbody>(); var grab = go.GetComponent<XRGrabInteractable>();
            var r = go.GetComponentInChildren<MeshRenderer>();
            int mc = go.GetComponentsInChildren<MeshCollider>(true).Length; meshCols += mc;
            bool st = go.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0); if (st) staticCnt++;
            var col = go.GetComponent<Collider>();
            string cs = col is CapsuleCollider cc ? string.Format(ci, "Capsule r={0:0.000} h={1:0.000}", cc.radius, cc.height)
                      : col is BoxCollider bc ? string.Format(ci, "Box {0:0.00}×{1:0.00}×{2:0.00}", bc.size.x, bc.size.y, bc.size.z) : "?";
            var h = r.bounds.size.y; var g = grab.attachTransform.localPosition;
            string grip = item.packaging == PackagingType.Bottle ? string.Format(ci, "горлышко, {0:0%} высоты", g.y / h)
                        : item.packaging == PackagingType.Box ? string.Format(ci, "центр лицевой грани (z=+{0:0.000})", g.z)
                        : string.Format(ci, "центр корпуса, {0:0%} высоты", g.y / h);
            L(string.Format(ci, "   {0,-12} {1,-8} {2}/{3:0.00}  {4:0.00}  {5:0.00}  {6:0.00}     {7,-25} {8,-10} {9,-6} {10,-8} {11,-6} {12,-35} smooth {13:0.00} с ×{14:0.0}",
                n, item.packaging, item.tier, item.tierHeight, rb.mass, rb.linearDamping, rb.angularDamping, cs, mc, st ? "ДА" : "нет",
                r.lightmapIndex < 0 || r.lightmapIndex == 65535 ? "нет" : r.lightmapIndex.ToString(), r.lightProbeUsage == LightProbeUsage.BlendProbes ? "да" : "нет",
                grip, grab.throwSmoothingDuration, grab.throwVelocityScale));
        }
        L($"   Mesh Collider на товарах: {meshCols} · товаров со Static: {staticCnt}");
        var anyGrab = FindProduct(ProductNames[0])?.GetComponent<XRGrabInteractable>();
        if (anyGrab != null)
            L(string.Format(ci, "   XR Grab Interactable: Movement {0}, Far Attach {1}, Attach Ease In {2:0.00} с, Throw On Detach {3}, Throw Smoothing {4:0.00} с, Velocity Scale {5:0.0}, Angular {6:0.0}, Select Entered/Exited → ProductItem: {7}/{8}",
                anyGrab.movementType, anyGrab.farAttachMode, anyGrab.attachEaseInTime, anyGrab.throwOnDetach, anyGrab.throwSmoothingDuration,
                anyGrab.throwVelocityScale, anyGrab.throwAngularVelocityScale, anyGrab.selectEntered.GetPersistentEventCount(), anyGrab.selectExited.GetPersistentEventCount()));
        L();

        L("4. СОКЕТЫ");
        var socks = Object.FindObjectsByType<ProductSocket>(FindObjectsSortMode.None);
        L($"   ячеек полки: {socks.Count(s => s.kind == SocketKind.Shelf)} (у каждой Starting Selected = свой товар: {socks.Count(s => s.kind == SocketKind.Shelf && s.startingSelectedInteractable != null)})");
        L($"   ячеек корзины: {socks.Count(s => s.kind == SocketKind.Basket)}");
        var anchor = Object.FindFirstObjectByType<BodyAnchor>();
        if (anchor != null) L(string.Format(ci, "   корзина: дно на {0:0.00} м от пола, смещение от головы x={1:0.00} z={2:0.00} м, поворот за телом при развороте > {3:0}°", anchor.waistHeight, anchor.offset.x, anchor.offset.y, anchor.yawThreshold));
        L();

        L("5. СЛОИ И МАТРИЦА КОЛЛИЗИЙ");
        L($"   слой 8 = {LayerMask.LayerToName(8)}, слой 9 = {LayerMask.LayerToName(9)}");
        foreach (var (a, bb) in new[] { (LayerProducts, LayerNPC), (LayerProducts, 0), (LayerProducts, LayerProducts), (LayerProducts, 2), (LayerNPC, 0) })
            L($"   {LayerMask.LayerToName(a),-9} × {LayerMask.LayerToName(bb),-14} {(Physics.GetIgnoreLayerCollision(a, bb) ? "НЕ сталкиваются" : "сталкиваются")}");
        foreach (var npc in Npcs())
            L($"   {npc.name,-16} слой {LayerMask.LayerToName(npc.layer)}, {(npc.GetComponent<CapsuleCollider>() != null ? "Capsule" : "нет коллайдера")}, Rigidbody kinematic = {npc.GetComponent<Rigidbody>()?.isKinematic}");
        foreach (var s in Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None))
            L($"   NavMeshSurface слои сбора геометрии: {MaskNames(s.layerMask)}");
        int sceneMesh = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Length;
        L($"   Mesh Collider во всей сцене: {sceneMesh} — {string.Join(", ", Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Select(m => HPath(m.transform)))}");
        L();

        File.WriteAllText(Path.Combine(OutDir, "00_Отчёт_взаимодействия.txt"), sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] 7.5 Протокол: " + Path.Combine(OutDir, "00_Отчёт_взаимодействия.txt") + "\n" + sb);
        RenderStatic();
    }

    static string PkgVersion(string id)
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(XRBaseInteractor).Assembly);
        return info != null ? info.version : "?";
    }

    static string MaskNames(int mask) =>
        string.Join(", ", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(i => { var n = LayerMask.LayerToName(i); return n == "" ? "#" + i : n; }));

    static string HPath(Transform t) => t.parent == null ? t.name : HPath(t.parent) + "/" + t.name;

    // ===================== КАДРЫ (статика) =====================

    const int ShotW = 1600, ShotH = 900;

    static void RenderStatic()
    {
        Directory.CreateDirectory(OutDir);
        var temp = new List<GameObject>();
        var mGrip = Unlit(new Color(1f, 0.85f, 0f));
        var mAxisF = Unlit(new Color(0.15f, 0.45f, 1f));
        var mAxisU = Unlit(new Color(0.2f, 0.9f, 0.25f));
        var mCol = Transparent(new Color(0.1f, 1f, 0.4f, 0.28f));
        var mSlot = Transparent(new Color(0.2f, 0.7f, 1f, 0.30f));
        var mNpc = Transparent(new Color(1f, 0.3f, 0.2f, 0.30f));
        // в режиме редактирования NPC стоят в T-позе прямо перед стеллажом 03 и закрывают товары —
        // на крупных планах их прячем, показываем только на кадре 09 (коллайдеры NPC)
        var npcR = Npcs().SelectMany(n => n.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToList();
        foreach (var r in npcR) r.enabled = false;
        try
        {
            // маркеры точек захвата: жёлтая сфера + синяя ось «вперёд руки» + зелёная «вверх»
            foreach (var n in ProductNames)
            {
                var item = FindProduct(n)?.GetComponent<ProductItem>(); if (item == null) continue;
                var g = item.Grip;
                temp.Add(Prim(PrimitiveType.Sphere, g.position, g.rotation, Vector3.one * 0.03f, mGrip));
                temp.Add(Prim(PrimitiveType.Cube, g.position + g.forward * 0.05f, g.rotation, new Vector3(0.006f, 0.006f, 0.10f), mAxisF));
                temp.Add(Prim(PrimitiveType.Cube, g.position + g.up * 0.04f, g.rotation, new Vector3(0.006f, 0.08f, 0.006f), mAxisU));
            }
            var aura = FindProduct("Bottle_AURA").transform.position;
            var krisp = FindProduct("Pack_KRISP").transform.position;
            var volta = FindProduct("Can_VOLTA").transform.position;
            Warm();
            Shot("01_Точки_захвата_все_товары_стеллаж", new Vector3(-0.1f, 1.30f, 0.75f), new Vector3(-0.1f, 1.15f, -1.6f), 58f);
            Shot("02a_Точка_захвата_бутылка_горлышко", aura + new Vector3(0.28f, 0.30f, 0.50f), aura + Vector3.up * 0.15f, 32f);
            Shot("02b_Точка_захвата_коробка_центр_грани", krisp + new Vector3(0.32f, 0.22f, 0.55f), krisp + Vector3.up * 0.14f, 32f);
            Shot("02c_Точка_захвата_банка_центр", volta + new Vector3(0.28f, 0.10f, 0.50f), volta + Vector3.up * 0.08f, 32f);
            foreach (var t in temp) Object.DestroyImmediate(t); temp.Clear();

            // примитивные коллайдеры товаров (полупрозрачные зелёные оболочки)
            foreach (var n in ProductNames)
            {
                var go = FindProduct(n); var c = go?.GetComponent<Collider>(); if (c == null) continue;
                if (c is BoxCollider b) temp.Add(Prim(PrimitiveType.Cube, go.transform.TransformPoint(b.center), go.transform.rotation, b.size, mCol));
                if (c is CapsuleCollider cc) temp.Add(Prim(PrimitiveType.Capsule, go.transform.TransformPoint(cc.center), go.transform.rotation, new Vector3(cc.radius * 2, cc.height * 0.5f, cc.radius * 2), mCol));
            }
            Shot("08_Коллайдеры_товаров_Box_и_Capsule", new Vector3(0.4f, 1.35f, 0.45f), new Vector3(-0.1f, 1.1f, -1.6f), 55f);
            foreach (var t in temp) Object.DestroyImmediate(t); temp.Clear();

            // корзина с ячейками
            var basket = Object.FindFirstObjectByType<BodyAnchor>()?.transform;
            if (basket != null)
            {
                foreach (var s in basket.GetComponentsInChildren<ProductSocket>())
                {
                    var bc = s.GetComponent<BoxCollider>();
                    temp.Add(Prim(PrimitiveType.Cube, s.transform.TransformPoint(bc.center), s.transform.rotation, bc.size * 0.96f, mSlot));
                    temp.Add(Prim(PrimitiveType.Cylinder, s.transform.position + Vector3.up * 0.002f, s.transform.rotation, new Vector3(0.05f, 0.002f, 0.05f), mGrip));
                }
                var models = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "Controller Model" && t.gameObject.activeSelf).ToList();
                foreach (var m in models) m.gameObject.SetActive(false);
                Shot("07_Корзина_6_ячеек_Socket_Interactor", basket.position + basket.rotation * new Vector3(0.45f, 0.65f, -0.55f), basket.position + Vector3.up * 0.12f, 42f);
                foreach (var m in models) m.gameObject.SetActive(true);
                foreach (var t in temp) Object.DestroyImmediate(t); temp.Clear();
            }

            // ячейки полки
            foreach (var s in Object.FindObjectsByType<ProductSocket>(FindObjectsSortMode.None).Where(x => x.kind == SocketKind.Shelf))
            {
                var bc = s.GetComponent<BoxCollider>();
                temp.Add(Prim(PrimitiveType.Cube, s.transform.TransformPoint(bc.center), s.transform.rotation, bc.size, mSlot));
            }
            Shot("07b_Ячейки_полки_зоны_возврата", new Vector3(-0.1f, 1.30f, 0.75f), new Vector3(-0.1f, 1.15f, -1.6f), 58f);
            foreach (var t in temp) Object.DestroyImmediate(t); temp.Clear();

            // NPC: капсулы на слое NPC
            foreach (var npc in Npcs())
            {
                var c = npc.GetComponent<CapsuleCollider>(); if (c == null) continue;
                temp.Add(Prim(PrimitiveType.Capsule, npc.transform.TransformPoint(c.center), npc.transform.rotation, new Vector3(c.radius * 2, c.height * 0.5f, c.radius * 2), mNpc));
            }
            foreach (var r in npcR) r.enabled = true;
            var first = Npcs().FirstOrDefault();
            if (first != null)
                Shot("09_Капсулы_NPC_слой_NPC", new Vector3(1.6f, 2.5f, 1.0f), new Vector3(-1.4f, 0.8f, -0.6f), 60f);
        }
        finally
        {
            foreach (var r in npcR) if (r) r.enabled = true;
            foreach (var t in temp) if (t) Object.DestroyImmediate(t);
            foreach (var m in new[] { mGrip, mAxisF, mAxisU, mCol, mSlot, mNpc }) Object.DestroyImmediate(m);
        }
        Debug.Log("[NEUROSHELF] Кадры статики → " + OutDir);
    }

    static GameObject Prim(PrimitiveType t, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        g.hideFlags = HideFlags.DontSave;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetPositionAndRotation(pos, rot);
        g.transform.localScale = scale;
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        return g;
    }

    static Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
        m.SetColor("_BaseColor", c);
        return m;
    }

    static Material Transparent(Color c)
    {
        var m = Unlit(c);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    static void Warm() => Shot(null, new Vector3(0, 1.6f, -3f), new Vector3(0, 1.2f, 0), 60f);

    static void Shot(string file, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~NS_Capture") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = 60f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            if (file == null) return;
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
    }

    // ===================== PLAY MODE: проверка и запись =====================

    const string ModeKey = "ns_interaction_mode";
    static object recorder;   // RecorderController (через отражение не нужно — тип доступен, но держим как object)

    static NeuroshelfInteractionSetup()
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
        if (mode == "fps")
        {
            SessionState.EraseString(ModeKey);
            var probe = new GameObject("~FpsProbe").AddComponent<FpsProbe>();
            probe.reportFolder = "ЛР5";
            probe.fileName = "12_Замер_FPS_с_взаимодействием.txt";
            Debug.Log("[NEUROSHELF] 7.7 Замер FPS: 3 с прогрева + 10 с");
            return;
        }
        InteractionDemoDriver.OutDir = OutDir;
        var go = new GameObject("~NS_InteractionDemo");
        var d = go.AddComponent<InteractionDemoDriver>();
        d.takeShots = mode == "check";
        d.runPhysicsTests = mode == "check";
        if (mode == "record") recorder = NeuroshelfRecorder.Start(RecDir);
        Debug.Log("[NEUROSHELF] Сценарий взаимодействия запущен: " + mode);
    }

    static void Watch()
    {
        if (!EditorApplication.isPlaying) return;
        var mode = SessionState.GetString(ModeKey, "");
        if (mode == "" || !InteractionDemoDriver.Finished) return;
        SessionState.EraseString(ModeKey);
        if (recorder != null) { NeuroshelfRecorder.Stop(recorder); recorder = null; }
        if (mode == "check")
        {
            var path = Path.Combine(OutDir, "10_Проверка_в_Play_Mode.txt");
            File.WriteAllText(path, "NEUROSHELF — проверка взаимодействия в Play Mode " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\n\n" + InteractionDemoDriver.Report, new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 7.6 Протокол: " + path + "\n" + InteractionDemoDriver.Report);
        }
        else Debug.Log("[NEUROSHELF] 7.8 Запись завершена → " + RecDir);
        EditorApplication.isPlaying = false;
    }

    // ===================== ОКНА РЕДАКТОРА =====================

    static void GrabInspectors()
    {
        Directory.CreateDirectory(OutDir);
        GameObject P(string n) => FindProduct(n);
        var q = new List<(int wait, Action act)>
        {
            (0, () => { Selection.activeGameObject = P("Bottle_AURA"); EditorApplication.ExecuteMenuItem("Window/General/Inspector"); }),
            (60, () => Grab("InspectorWindow", "11_Inspector_XR_Grab_Interactable_бутылка")),
            (0, () => { Selection.activeGameObject = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None).FirstOrDefault(n => n.handedness == InteractorHandedness.Right)?.gameObject; }),
            (60, () => Grab("InspectorWindow", "12_Inspector_Near-Far_Interactor")),
            (0, () => { Selection.activeGameObject = GameObject.Find("BasketSlot_1"); }),
            (60, () => Grab("InspectorWindow", "13_Inspector_Socket_корзины")),
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
        File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}

// Unity Recorder: запись Game View сценария в MP4 (H.264), 30 кадров/с, 1280×720.
static class NeuroshelfRecorder
{
    public static object Start(string dir)
    {
        Directory.CreateDirectory(dir);
        var cs = ScriptableObject.CreateInstance<UnityEditor.Recorder.RecorderControllerSettings>();
        var m = ScriptableObject.CreateInstance<UnityEditor.Recorder.MovieRecorderSettings>();
        m.name = "NEUROSHELF interaction";
        m.Enabled = true;
        m.ImageInputSettings = new UnityEditor.Recorder.Input.CameraInputSettings
        {
            Source = UnityEditor.Recorder.ImageSource.MainCamera,
            OutputWidth = 1280,
            OutputHeight = 720,
        };
        m.AudioInputSettings.PreserveAudio = false;
        m.OutputFile = Path.Combine(dir, "ns_interaction").Replace('\\', '/');
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

    public static void Stop(object rc) => ((UnityEditor.Recorder.RecorderController)rc).StopRecording();
}
