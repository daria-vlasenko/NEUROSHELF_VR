// NEUROSHELF — сценарий эксперимента: конечный автомат, триггерные зоны со сверкой, блок условий,
// табло и кнопки, реакция среды, планограммы A/C, привязка логгеров ЛР5/ЛР6 к состоянию.
// Отдельный файл; остальные скрипты Editor не трогает.
//
// Меню NEUROSHELF → Сценарий:
//   9.0 Полный цикл        → 9.1 … 9.5 + протокол настройки (обычно нажимать только её)
//   9.1 Данные             → планограммы PL_A / PL_C с ценами, ссылки из условий эксперимента
//   9.2 Слои и физика      → слой ScenarioUI (кнопки, ручка корзины, список), матрица, маски лучей рук
//   9.3 Зоны               → вход, стартовая отметка, касса, разбор, 4 зоны у стеллажей
//   9.4 Стойка и корзина   → стойка у входа, корзина с ручкой (XR Grab), список покупок
//   9.5 Табло и кнопки     → главное табло, табло разбора, кнопки, метки, подсветка кассы, контроллер
//   9.6 Автотесты Play Mode (меню в NeuroshelfTestRunner)
//   9.7 Прогон сценария в Play Mode → протокол «действие → реакция» + кадры каждого состояния
//   9.8 Запись Unity Recorder (вход → … → результаты, с нарушением посередине)
//   9.9 Карта зала для сценария (координаты, без изменений сцены)
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР7\

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NeuroshelfScenarioSetup
{
    // ===================== ПАРАМЕТРЫ =====================

    const string Menu = "NEUROSHELF/Сценарий/";
    const string CondDir = "Assets/_Project/Configs/Conditions";
    const string PlanDir = "Assets/_Project/Configs/Planograms";
    const string MatDir = "Assets/_Project/Materials/Scenario";
    public const int LayerScenarioUI = 13;
    const int LayerIgnoreRaycast = 2, LayerProducts = 8, LayerPlayer = 10, LayerBasket = 11;

    public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР7"));
    static string RecDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // ----- раскладка сценария в зале (м). Зал 12 × 8: x −6…6, z −4…4 (меню 9.9).
    // Стены зала глухие и запечены в лайтмапу, поэтому вход сделан внутри зала: тамбур в северо-восточном
    // углу (x 3.6…6.0, z 2.2…4.0), отделённый перилами высотой 1.1 м, с распашной дверью из ЛР4 (AC_Door).
    static class L
    {
        public static Vector3 Outside = new Vector3(4.8f, 0f, 3.6f);        // точка появления в тамбуре, вне всех зон
        public static float OutsideYaw = 180f;                               // лицом к двери
        public static float VestibuleX = 3.6f, VestibuleZ = 2.2f;           // перила тамбура
        public static float DoorX = 4.8f, DoorWidth = 0.9f;
        public static Vector3 EntryC = new Vector3(4.8f, 1.25f, 1.6f);     public static Vector3 EntryS = new Vector3(1.8f, 2.5f, 1.2f);
        public static Vector3 StandPos = new Vector3(5.6f, 0f, 1.7f);       public static float StandYaw = 90f;    // спиной к восточной стене
        public static Vector3 StartC = new Vector3(4.3f, 0f, 0.35f);        public static float StartR = 0.45f;
        public static float StartYaw = 90f;                                   // лицом к главному табло
        public static Vector3 BoardPos = new Vector3(5.97f, 1.80f, 0.35f);  public static float BoardYaw = 90f;   // на восточной стене
        public static Vector3 DebriefC = new Vector3(0f, 1.25f, 2.9f);      public static Vector3 DebriefS = new Vector3(3.0f, 2.5f, 1.6f);
        public static Vector3 DebriefBoardPos = new Vector3(0f, 1.60f, 3.97f); public static float DebriefYaw = 0f; // на северной стене
        public static Vector3 CheckoutC = new Vector3(4.5f, 1.25f, -1.05f); public static Vector3 CheckoutS = new Vector3(1.4f, 2.5f, 1.1f);
        public static Vector3 CheckoutButton = new Vector3(4.5f, 1.20f, -1.72f); public static float CheckoutYaw = 180f;
        public static Vector3 CheckoutLightPos = new Vector3(4.5f, 1.08f, -1.585f); public static Vector3 CheckoutLightSize = new Vector3(2.0f, 0.04f, 0.03f);
    }

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "9.0 Полный цикл (данные → слои → зоны → стойка → табло → протокол)", priority = 500)]
    public static void All()
    {
        Data(); Layers(); Zones(); StandAndBasket(); Boards();
        Save();
        Verify();
        Debug.Log("[NEUROSHELF] Сценарий настроен. Дальше: 9.6 (автотесты) и 9.7 (прогон с кадрами).");
    }
    [MenuItem(Menu + "9.1 Данные: планограммы A/C и цены", priority = 501)] static void M1() { Data(); Save(); }
    [MenuItem(Menu + "9.2 Слои и физика", priority = 502)] static void M2() { Layers(); Save(); }
    [MenuItem(Menu + "9.3 Триггерные зоны", priority = 503)] static void M3() { Zones(); Save(); }
    [MenuItem(Menu + "9.4 Стойка, корзина, список покупок", priority = 504)] static void M4() { StandAndBasket(); Save(); }
    [MenuItem(Menu + "9.5 Табло, кнопки, метки, контроллер", priority = 505)] static void M5() { Boards(); Save(); }
    [MenuItem(Menu + "9.7 Прогон сценария в Play Mode + кадры (~90 с)", priority = 521)] static void M7() => StartPlay("check");
    [MenuItem(Menu + "9.8 Запись Unity Recorder (~100 с)", priority = 522)] static void M8() => StartPlay("record");
    [MenuItem(Menu + "9.9 Карта зала для сценария", priority = 540)] static void M9() => MapDump();

    // ===================== общие =====================

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
        var t = parent != null ? parent.Find(name) : GameObject.Find(name)?.transform;
        if (t != null) return t;
        t = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(t.gameObject, name);
        if (parent != null) t.SetParent(parent, false);
        return t;
    }

    static Transform Root() => Child(null, "Scenario");

    static Transform Prim(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, bool collider, int layer = 0)
    {
        var t = parent.Find(name);
        if (t == null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Undo.RegisterCreatedObjectUndo(g, name);
            t = g.transform; t.SetParent(parent, false);
        }
        t.localPosition = localPos; t.localRotation = Quaternion.identity; t.localScale = scale;
        t.gameObject.layer = layer;
        var c = t.GetComponent<Collider>();
        if (!collider && c != null) Object.DestroyImmediate(c);
        var r = t.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
        }
        GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        return t;
    }

    static Material Mat(string name, string shader, Color c)
    {
        Directory.CreateDirectory(MatDir);
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit => Mat("M_ScenarioUnlit", "Universal Render Pipeline/Unlit", Color.white);
    static Material BoardMat => Mat("M_ScenarioBoard", "Universal Render Pipeline/Unlit", new Color(0.07f, 0.08f, 0.10f));
    static Material StandMat => Mat("M_ScenarioStand", "Universal Render Pipeline/Lit", new Color(0.55f, 0.56f, 0.58f));

    static ExperimentConditionSO Cond(string n) => AssetDatabase.LoadAssetAtPath<ExperimentConditionSO>($"{CondDir}/{n}.asset");
    static XROrigin Origin() => Object.FindFirstObjectByType<XROrigin>();

    // ===================== 9.1 ДАННЫЕ =====================

    // Цены условные (у. е.), бренды вымышленные. Базовая цена в A и C одинакова — меняется только подача:
    // в A у VOLTA зачёркнутая «старая» цена 12.90 (эффект якоря, Tversky & Kahneman, 1974).
    static readonly (string id, float price)[] Prices =
    {
        ("AURA", 4.20f), ("NUBO", 3.60f), ("LUMEN", 3.90f), ("PURA", 5.40f),
        ("VOLTA", 7.90f), ("FERRO", 6.50f), ("ZEST", 3.20f),
        ("OKTA", 4.50f), ("MIRA", 2.90f), ("NORDA", 3.30f), ("GRANO", 2.70f), ("KRISP", 3.80f),
    };

    static void Data()
    {
        Directory.CreateDirectory(PlanDir);
        PlanogramSO Make(string file, string name, string desc, (string shelf, int tier, string[] items)[] rows, Dictionary<string, float> anchors)
        {
            var path = $"{PlanDir}/{file}.asset";
            var p = AssetDatabase.LoadAssetAtPath<PlanogramSO>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<PlanogramSO>(); AssetDatabase.CreateAsset(p, path); }
            p.planogramName = name; p.description = desc;
            p.rows = rows.Select(r => new PlanogramSO.Row
            {
                shelf = r.shelf, tier = r.tier, products = r.items,
                xStart = r.shelf == "Shelf_03" ? -2.4f : 0f, width = 2.4f, z = -1.45f,
            }).ToList();
            p.prices = Prices.Select(x => new PlanogramSO.Price { productId = x.id, price = x.price, anchorPrice = anchors.TryGetValue(x.id, out var a) ? a : 0f }).ToList();
            EditorUtility.SetDirty(p);
            return p;
        }

        // A — та же выкладка, что выставил редактор ЛР5 (NeuroshelfInteractionSetup.Planogram): продвигаемые
        // VOLTA, FERRO, PURA на уровне глаз Shelf_03 под тёплым акцентом 3000 K.
        var A = Make("PL_A_Manipulated", "A_Manipulated",
            "Условие A: продвигаемые напитки на уровне глаз (1.60 м) продвигаемого стеллажа Shelf_03 под тёплым акцентом; якорная цена у VOLTA.",
            new[]
            {
                ("Shelf_03", 0, new[] { "Bottle_AURA" }),
                ("Shelf_03", 1, new[] { "Bottle_NUBO", "Pack_OKTA", "Bottle_LUMEN" }),
                ("Shelf_03", 2, new[] { "Can_VOLTA", "Can_FERRO", "Bottle_PURA" }),
                ("Shelf_04", 0, new[] { "Pack_MIRA", "Pack_NORDA", "Pack_GRANO" }),
                ("Shelf_04", 1, new[] { "Can_ZEST", "Pack_KRISP" }),
            },
            new Dictionary<string, float> { { "VOLTA", 12.90f } });

        // C — нейтральная: все 7 напитков одним рядом на среднем ярусе 1.10 м стеллажа Shelf_04 (без акцента),
        // по одному фейсингу, по алфавиту; без якорных цен. Товары в пачках — на Shelf_03.
        var C = Make("PL_C_Neutral", "C_Neutral",
            "Условие C: все напитки на одном ярусе 1.10 м одного стеллажа, по алфавиту, по одному фейсингу, без якорных цен.",
            new[]
            {
                ("Shelf_03", 1, new[] { "Pack_GRANO", "Pack_KRISP", "Pack_MIRA" }),
                ("Shelf_03", 2, new[] { "Pack_NORDA", "Pack_OKTA" }),
                ("Shelf_04", 1, new[] { "Bottle_AURA", "Can_FERRO", "Bottle_LUMEN", "Bottle_NUBO", "Bottle_PURA", "Can_VOLTA", "Can_ZEST" }),
            },
            new Dictionary<string, float>());

        foreach (var (n, p) in new[] { ("Cond_A_Manipulated_VOLTA", A), ("Cond_A_Manipulated_PURA", A), ("Cond_C_Neutral", C) })
        {
            var c = Cond(n);
            if (c == null) { Debug.LogWarning("[NEUROSHELF] Нет условия " + n); continue; }
            c.planogram = p;
            EditorUtility.SetDirty(c);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[NEUROSHELF] 9.1 Планограммы PL_A_Manipulated / PL_C_Neutral, цены, ссылки из условий");
    }

    // ===================== 9.2 СЛОИ =====================

    static void Layers()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        var el = layers.GetArrayElementAtIndex(LayerScenarioUI);
        if (string.IsNullOrEmpty(el.stringValue)) el.stringValue = "ScenarioUI";
        tm.ApplyModifiedProperties();
        // ScenarioUI — только для выбора руками и лучом: физически ни с чем не сталкивается
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerScenarioUI, i, true);
        // зоны (Ignore Raycast) должны получать события от капсулы игрока
        Physics.IgnoreLayerCollision(LayerIgnoreRaycast, LayerPlayer, false);

        // руки: Near и Far кастеры видят слой ScenarioUI
        foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
        {
            var near = nf.GetComponent<SphereInteractionCaster>();
            var far = nf.GetComponent<CurveInteractionCaster>();
            if (near != null) { Undo.RecordObject(near, "mask"); near.physicsLayerMask |= 1 << LayerScenarioUI; EditorUtility.SetDirty(near); }
            if (far != null) { Undo.RecordObject(far, "mask"); far.raycastMask |= 1 << LayerScenarioUI; EditorUtility.SetDirty(far); }
        }
        Debug.Log("[NEUROSHELF] 9.2 Слой ScenarioUI (13), матрица, маски рук");
    }

    // ===================== 9.3 ЗОНЫ =====================

    static ScenarioZone Zone(Transform parent, string id, Vector3 center, Vector3 size)
    {
        var t = Child(parent, "Zone_" + id);
        t.position = center; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
        t.gameObject.layer = LayerIgnoreRaycast;
        var b = Ensure<BoxCollider>(t.gameObject);
        b.isTrigger = true; b.center = Vector3.zero; b.size = size;
        var z = Ensure<ScenarioZone>(t.gameObject);
        z.zoneId = id; z.reconcile = true;
        z.player = Origin()?.GetComponent<CharacterController>();
        EditorUtility.SetDirty(z);
        return z;
    }

    static void Zones()
    {
        var zr = Child(Root(), "Zones");
        Zone(zr, "Entry", L.EntryC, L.EntryS);
        Zone(zr, "StartMark", new Vector3(L.StartC.x, 1.25f, L.StartC.z), new Vector3(L.StartR * 2f, 2.5f, L.StartR * 2f));
        Zone(zr, "Checkout", L.CheckoutC, L.CheckoutS);
        Zone(zr, "Debrief", L.DebriefC, L.DebriefS);
        // у стеллажей — половина прохода во всю длину стеллажа (как зоны RouteLogger ЛР6)
        foreach (var s in new[] { "Shelf_01", "Shelf_02", "Shelf_03", "Shelf_04" })
        {
            var sh = GameObject.Find(s);
            if (sh == null) continue;
            var b = Bounds(sh);
            bool south = b.center.z < 0f;
            float edge = south ? b.max.z : b.min.z;
            float zc = edge + (south ? 0.6f : -0.6f);
            Zone(zr, s, new Vector3(b.center.x, 1.25f, zc), new Vector3(b.size.x, 2.5f, 1.2f));
        }
        Debug.Log("[NEUROSHELF] 9.3 Зоны: Entry, StartMark, Checkout, Debrief, Shelf_01…04");
    }

    static Bounds Bounds(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ===================== 9.4 СТОЙКА, КОРЗИНА, СПИСОК =====================

    static void Vestibule(Transform root)
    {
        var v = Child(root, "Vestibule");
        var mat = Mat("M_Railing", "Universal Render Pipeline/Lit", new Color(0.35f, 0.37f, 0.40f));
        float h = 1.1f, t = 0.05f, x0 = L.VestibuleX, x1 = 6.0f, z0 = L.VestibuleZ, z1 = 4.0f;
        float d0 = L.DoorX - L.DoorWidth * 0.5f, d1 = L.DoorX + L.DoorWidth * 0.5f;
        void Rail(string n, Vector3 a, Vector3 b)
        {
            var c = (a + b) * 0.5f; var size = new Vector3(Mathf.Max(t, Mathf.Abs(b.x - a.x)), h, Mathf.Max(t, Mathf.Abs(b.z - a.z)));
            var r = Prim(v, n, PrimitiveType.Cube, new Vector3(c.x, h * 0.5f, c.z), size, mat, true);
            r.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.BlendProbes;
        }
        Rail("Rail_South_W", new Vector3(x0, 0, z0), new Vector3(d0, 0, z0));
        Rail("Rail_South_E", new Vector3(d1, 0, z0), new Vector3(x1, 0, z0));
        Rail("Rail_West", new Vector3(x0, 0, z0), new Vector3(x0, 0, z1));
        // дверь: тот же Animator Controller AC_Door и ProximityDoor, что у витрины в ЛР4 (клип поворачивает Door_Hinge на −100°)
        var door = Child(v, "EntryDoor");
        door.position = new Vector3(d0, 0f, z0); door.rotation = Quaternion.identity;
        var hinge = Child(door, "Door_Hinge"); hinge.localPosition = Vector3.zero; hinge.localRotation = Quaternion.identity;
        var panel = Prim(hinge, "Panel", PrimitiveType.Cube, new Vector3(L.DoorWidth * 0.5f, 1.0f, 0f), new Vector3(L.DoorWidth - 0.02f, 2.0f, 0.03f),
            Mat("M_EntryDoor", "Universal Render Pipeline/Lit", new Color(0.55f, 0.75f, 0.85f)), false);
        panel.GetComponent<Renderer>().lightProbeUsage = LightProbeUsage.BlendProbes;
        var an = Ensure<Animator>(door.gameObject);
        an.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/_Project/Animation/AC_Door.controller");
        var pd = Ensure<ProximityDoor>(door.gameObject);
        pd.animator = an; pd.openDistance = 1.2f; pd.manual = false;   // из точки появления (1.5 м) дверь закрыта, открывается при подходе
        EditorUtility.SetDirty(pd);
    }

    static void StandAndBasket()
    {
        var root = Root();
        Vestibule(root);
        var stand = Child(root, "Stand");
        stand.position = L.StandPos; stand.rotation = Quaternion.Euler(0f, L.StandYaw, 0f);
        // стол 1.0 × 0.5 м, высота 0.85 м: корзину берут без наклона
        Prim(stand, "Table", PrimitiveType.Cube, new Vector3(0f, 0.425f, 0f), new Vector3(1.0f, 0.85f, 0.5f), StandMat, true);
        var basketMark = Prim(stand, "Mark_Basket", PrimitiveType.Cube, new Vector3(-0.22f, 0.853f, 0f), new Vector3(0.48f, 0.004f, 0.36f), Unlit, false);
        var listMark = Prim(stand, "Mark_List", PrimitiveType.Cube, new Vector3(0.30f, 0.853f, 0f), new Vector3(0.26f, 0.004f, 0.20f), Unlit, false);
        var basketPoint = Child(stand, "BasketPoint");
        basketPoint.localPosition = new Vector3(-0.22f, 0.856f, 0f); basketPoint.localRotation = Quaternion.identity;

        // табличка-чеклист над стойкой
        var sign = Prim(stand, "Sign", PrimitiveType.Cube, new Vector3(0f, 1.55f, 0.20f), new Vector3(0.80f, 0.32f, 0.02f), BoardMat, false);
        var cv = WorldText.Canvas(stand, "SignCanvas", new Vector2(0.76f, 0.30f), new Vector3(0f, 1.55f, 0.188f));
        var checklist = WorldText.Label(cv.transform, "Checklist", new Rect(20, 15, 720, 270), 52, TextAnchor.MiddleLeft);

        // список покупок: пустой корень с коллайдером выбора (слой ScenarioUI), бумага и текст — дочерние
        var card = Child(stand, "ShoppingList");
        card.localPosition = new Vector3(0.30f, 0.862f, 0f); card.localRotation = Quaternion.identity; card.localScale = Vector3.one;
        card.gameObject.layer = LayerScenarioUI;
        var cardCol = Ensure<BoxCollider>(card.gameObject); cardCol.size = new Vector3(0.20f, 0.02f, 0.14f); cardCol.center = Vector3.zero;
        Prim(card, "Paper", PrimitiveType.Cube, Vector3.zero, new Vector3(0.20f, 0.006f, 0.14f), Mat("M_ScenarioPaper", "Universal Render Pipeline/Unlit", new Color(0.97f, 0.96f, 0.90f)), false, LayerScenarioUI);
        var si = Ensure<XRSimpleInteractable>(card.gameObject);
        si.colliders.Clear(); si.colliders.Add(cardCol);
        var listCv = WorldText.Canvas(card, "Canvas", new Vector2(0.19f, 0.13f), new Vector3(0f, 0.0035f, 0f), 8f);
        listCv.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // лёжа, читается сверху
        var listText = WorldText.Label(listCv.transform, "Text", new Rect(6, 4, 178, 122), 9, TextAnchor.UpperLeft);
        listText.color = new Color(0.08f, 0.08f, 0.1f);
        var list = Ensure<ShoppingListCard>(card.gameObject);
        list.text = listText;
        list.wrist = GameObject.Find("Left Controller")?.transform;
        list.wristOffset = new Vector3(0.03f, 0.05f, 0.02f); list.wristEuler = new Vector3(-60f, 0f, 0f);

        // корзина ЛР5: ручка + XR Grab Interactable + BasketCarry
        var basketGo = Object.FindFirstObjectByType<BodyAnchor>()?.gameObject;
        if (basketGo == null) { Debug.LogError("[NEUROSHELF] Нет корзины (BodyAnchor). Сначала меню 7.0 и 8.0."); return; }
        var handle = Prim(basketGo.transform, "Handle", PrimitiveType.Cube, new Vector3(0f, 0.30f, 0f), new Vector3(0.30f, 0.025f, 0.03f), Mat("M_BasketHandle", "Universal Render Pipeline/Lit", new Color(0.25f, 0.25f, 0.27f)), true, LayerScenarioUI);
        foreach (var x in new[] { -0.15f, 0.15f })
            Prim(basketGo.transform, x < 0 ? "Handle_L" : "Handle_R", PrimitiveType.Cube, new Vector3(x, 0.26f, 0f), new Vector3(0.02f, 0.08f, 0.02f), handle.GetComponent<Renderer>().sharedMaterial, false);
        var grip = Child(handle, "Grip"); grip.localPosition = Vector3.zero; grip.localRotation = Quaternion.identity;

        var grab = Ensure<XRGrabInteractable>(basketGo);
        grab.colliders.Clear();
        grab.colliders.Add(handle.GetComponent<Collider>());   // выбирается только за ручку — корзину не схватить, доставая товар
        grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        grab.throwOnDetach = false;
        grab.trackScale = false;
        grab.attachTransform = grip;
        grab.useDynamicAttach = false;
        grab.interactionLayers = 1 << 1;     // слой взаимодействия товаров: руки его выбирают, сокеты товаров — нет (нет ProductItem)
        EditorUtility.SetDirty(grab);

        var carry = Ensure<BasketCarry>(basketGo);
        carry.standPoint = basketPoint;
        carry.bodyAnchor = basketGo.GetComponent<BodyAnchor>();
        carry.restFrame = basketGo.GetComponent<BasketRestFrame>();
        carry.head = Camera.main != null ? Camera.main.transform : null;
        carry.startCarried = false;
        EditorUtility.SetDirty(carry);
        list.basket = carry;
        EditorUtility.SetDirty(list);

        // руки должны выбирать слой взаимодействия кнопок/списка — берём слои правой руки
        var nf = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None).FirstOrDefault(n => n.handedness == InteractorHandedness.Right);
        si.interactionLayers = nf != null ? (InteractionLayerMask)(nf.interactionLayers.value & ~(1 << 31)) : (InteractionLayerMask)1;
        EditorUtility.SetDirty(si);

        var view = Object.FindFirstObjectByType<ScenarioView>();
        if (view != null) { view.basketMark = basketMark.GetComponent<Renderer>(); view.listMark = listMark.GetComponent<Renderer>(); view.standChecklist = checklist; EditorUtility.SetDirty(view); }
        Debug.Log("[NEUROSHELF] 9.4 Стойка, корзина с ручкой (XR Grab Interactable + BasketCarry), список покупок");
    }

    // ===================== 9.5 ТАБЛО, КНОПКИ, КОНТРОЛЛЕР =====================

    static ScenarioButton Button(Transform parent, string name, string label, Vector3 localPos, Vector3 size)
    {
        var t = Child(parent, name);
        t.localPosition = localPos; t.localRotation = Quaternion.identity;
        t.gameObject.layer = LayerScenarioUI;
        var col = Ensure<BoxCollider>(t.gameObject); col.size = size; col.center = Vector3.zero;
        var face = Prim(t, "Face", PrimitiveType.Cube, Vector3.zero, size, Unlit, false, LayerScenarioUI);
        var cv = WorldText.Canvas(t, "Canvas", new Vector2(size.x, size.y), new Vector3(0f, 0f, -size.z * 0.5f - 0.002f));
        var tx = WorldText.Label(cv.transform, "Label", new Rect(0, 0, size.x * 1000f, size.y * 1000f), Mathf.RoundToInt(size.y * 1000f * 0.42f), TextAnchor.MiddleCenter, FontStyle.Bold);
        tx.text = label;
        var si = Ensure<XRSimpleInteractable>(t.gameObject);
        si.colliders.Clear(); si.colliders.Add(col);
        var b = Ensure<ScenarioButton>(t.gameObject);
        b.buttonId = name; b.label = label; b.face = face.GetComponent<Renderer>(); b.text = tx;
        var nf = Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None).FirstOrDefault(n => n.handedness == InteractorHandedness.Right);
        b.enabledLayers = nf != null ? (InteractionLayerMask)(nf.interactionLayers.value & ~(1 << 31)) : (InteractionLayerMask)1;
        si.interactionLayers = 0;
        EditorUtility.SetDirty(b); EditorUtility.SetDirty(si);
        return b;
    }

    // Соглашение для табло, стойки и кнопок: локальная +z смотрит «в стену», от зрителя.
    // Холст uGUI читается со стороны −z своих осей, поэтому текст сразу обращён к зрителю.
    // yaw = 0 — табло на северной стене (зритель южнее), 90 — на восточной.
    static Transform Board(Transform parent, string name, Vector3 pos, float yaw, Vector2 size)
    {
        var t = Child(parent, name);
        t.position = pos; t.rotation = Quaternion.Euler(0f, yaw, 0f);
        Prim(t, "Back", PrimitiveType.Cube, new Vector3(0f, 0f, 0.02f), new Vector3(size.x + 0.06f, size.y + 0.06f, 0.04f), BoardMat, true);
        return t;
    }

    static void Boards()
    {
        var root = Root();

        // ---------- главное табло ----------
        var main = Board(root, "MainBoard", L.BoardPos, L.BoardYaw, new Vector2(1.6f, 0.9f));
        var cv = WorldText.Canvas(main, "Canvas", new Vector2(1.6f, 0.9f), new Vector3(0f, 0f, -0.002f));
        var title = WorldText.Label(cv.transform, "Title", new Rect(40, 25, 1520, 120), 84, TextAnchor.UpperLeft, FontStyle.Bold);
        var body = WorldText.Label(cv.transform, "Body", new Rect(40, 160, 1520, 650), 62, TextAnchor.UpperLeft);
        var footer = WorldText.Label(cv.transform, "Footer", new Rect(40, 820, 1520, 60), 32, TextAnchor.LowerLeft);
        footer.color = new Color(0.62f, 0.65f, 0.7f);
        var start = Button(main, "Btn_Start", "Начать", new Vector3(-0.32f, -0.62f, -0.03f), new Vector3(0.50f, 0.16f, 0.06f));
        var resume = Button(main, "Btn_Resume", "Продолжить", new Vector3(0.32f, -0.62f, -0.03f), new Vector3(0.50f, 0.16f, 0.06f));

        // ---------- малое табло у входа: видно из тамбура, пока участник ещё снаружи ----------
        var ent = Board(root, "EntranceBoard", new Vector3(5.60f, 1.70f, L.VestibuleZ + 0.06f), 180f, new Vector2(0.80f, 0.45f));
        var ecv = WorldText.Canvas(ent, "Canvas", new Vector2(0.80f, 0.45f), new Vector3(0f, 0f, -0.002f));
        var eTitle = WorldText.Label(ecv.transform, "Title", new Rect(25, 20, 750, 90), 48, TextAnchor.UpperLeft, FontStyle.Bold);
        var eBody = WorldText.Label(ecv.transform, "Body", new Rect(25, 120, 750, 310), 34, TextAnchor.UpperLeft);
        Prim(ent, "Post", PrimitiveType.Cube, new Vector3(0f, -0.62f, 0.02f), new Vector3(0.05f, 0.80f, 0.05f), BoardMat, false);

        // ---------- стартовая отметка ----------
        var mark = Prim(root, "StartMarkDisc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(L.StartR * 2f, 0.003f, L.StartR * 2f), Unlit, false);
        mark.position = new Vector3(L.StartC.x, 0.004f, L.StartC.z);

        // ---------- касса ----------
        var co = Child(root, "Checkout");
        co.position = L.CheckoutButton; co.rotation = Quaternion.Euler(0f, L.CheckoutYaw, 0f);
        var finish = Button(co, "Btn_Finish", "Оплатить", Vector3.zero, new Vector3(0.40f, 0.14f, 0.06f));
        var light = Prim(root, "CheckoutLight", PrimitiveType.Cube, Vector3.zero, L.CheckoutLightSize, Unlit, false);
        light.position = L.CheckoutLightPos;

        // ---------- табло разбора ----------
        var deb = Board(root, "DebriefBoard", L.DebriefBoardPos, L.DebriefYaw, new Vector2(2.2f, 1.1f));
        var dcv = WorldText.Canvas(deb, "Canvas", new Vector2(2.2f, 1.1f), new Vector3(0f, 0f, -0.002f));
        var dTitle = WorldText.Label(dcv.transform, "Title", new Rect(30, 20, 2140, 80), 52, TextAnchor.UpperLeft, FontStyle.Bold);
        var dBody = WorldText.Label(dcv.transform, "Body", new Rect(800, 110, 1370, 960), 34, TextAnchor.UpperLeft);
        var dLegend = WorldText.Label(dcv.transform, "Legend", new Rect(30, 1020, 740, 60), 28, TextAnchor.UpperLeft);
        var cName = WorldText.Label(dcv.transform, "ColName", new Rect(800, 110, 640, 520), 30, TextAnchor.UpperLeft);
        var cA = WorldText.Label(dcv.transform, "ColA", new Rect(1450, 110, 220, 520), 30, TextAnchor.UpperRight);
        var cC = WorldText.Label(dcv.transform, "ColC", new Rect(1690, 110, 220, 520), 30, TextAnchor.UpperRight);
        var cD = WorldText.Label(dcv.transform, "ColD", new Rect(1930, 110, 220, 520), 30, TextAnchor.UpperRight);
        var next = Button(deb, "Btn_Next", "Ко второму заходу", new Vector3(0f, -0.70f, -0.03f), new Vector3(0.70f, 0.16f, 0.06f));

        // план зала: 12 × 8 м → 0.60 × 0.40 м (1 м = 5 см), левая часть табло
        var map = Child(deb, "Map");
        map.localPosition = new Vector3(-0.70f, 0.02f, -0.006f); map.localRotation = Quaternion.identity;
        var oldFloor = map.Find("Floor"); if (oldFloor != null) oldFloor.name = "MapFloor";   // не путать с полом зала
        Prim(map, "MapFloor", PrimitiveType.Cube, new Vector3(0f, 0f, 0.002f), new Vector3(0.62f, 0.42f, 0.002f), Mat("M_ScenarioMap", "Universal Render Pipeline/Unlit", new Color(0.16f, 0.18f, 0.21f)), false);
        var hall = HallBounds();
        var db = Ensure<DebriefBoard>(deb.gameObject);
        db.map = map; db.mapScale = 0.05f; db.hallCenter = new Vector2(hall.center.x, hall.center.z);
        // зритель смотрит на север: восток (x+) у него справа, север (z+) — вверх по табло
        db.mapSign = new Vector2(Mathf.Sign(Vector3.Dot(deb.right, Vector3.right)), 1f);
        foreach (var s in new[] { "Shelf_01", "Shelf_02", "Shelf_03", "Shelf_04", "Checkout" })
        {
            var g = GameObject.Find(s == "Checkout" ? "Environment/Checkout" : s); if (g == null) continue;
            var b = Bounds(g);
            var r = Prim(map, "Rect_" + s, PrimitiveType.Cube, Vector3.zero, new Vector3(b.size.x * 0.05f, b.size.z * 0.05f, 0.002f), Mat("M_ScenarioMapShelf", "Universal Render Pipeline/Unlit", new Color(0.45f, 0.47f, 0.52f)), false);
            var mp = db.ToMap(b.center); r.localPosition = new Vector3(mp.x, mp.y, 0f);
        }
        db.routeA = Line(map, "RouteA", new Color(1f, 0.54f, 0.24f));
        db.routeC = Line(map, "RouteC", new Color(0.29f, 0.64f, 1f));
        db.title = dTitle; db.body = dBody; db.legend = dLegend; db.colName = cName; db.colA = cA; db.colC = cC; db.colD = cD;
        EditorUtility.SetDirty(db);

        // ---------- ценники ----------
        var tags = Child(root, "PriceTags");

        // ---------- контроллер ----------
        var ctl = Child(root, "Controller");
        var sc = Ensure<ScenarioController>(ctl.gameObject);
        sc.context = Object.FindFirstObjectByType<ExperimentContext>();
        sc.conditionA = Cond("Cond_A_Manipulated_VOLTA");
        sc.conditionC = Cond("Cond_C_Neutral");
        var zones = Object.FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None);
        sc.entryZone = zones.FirstOrDefault(z => z.zoneId == "Entry");
        sc.startZone = zones.FirstOrDefault(z => z.zoneId == "StartMark");
        sc.checkoutZone = zones.FirstOrDefault(z => z.zoneId == "Checkout");
        sc.debriefZone = zones.FirstOrDefault(z => z.zoneId == "Debrief");
        sc.basket = Object.FindFirstObjectByType<BasketCarry>();
        sc.shoppingList = Object.FindFirstObjectByType<ShoppingListCard>();
        sc.rig = Object.FindFirstObjectByType<LocomotionRig>();
        sc.startButton = start; sc.resumeButton = resume; sc.finishButton = finish; sc.nextButton = next;
        EditorUtility.SetDirty(sc);

        var view = Ensure<ScenarioView>(ctl.gameObject);
        view.controller = sc; view.title = title; view.body = body; view.footer = footer;
        view.entranceTitle = eTitle; view.entranceBody = eBody;
        view.startMark = mark.GetComponent<Renderer>(); view.checkoutLight = light.GetComponent<Renderer>();
        var stand = root.Find("Stand");
        if (stand != null)
        {
            view.basketMark = stand.Find("Mark_Basket")?.GetComponent<Renderer>();
            view.listMark = stand.Find("Mark_List")?.GetComponent<Renderer>();
            view.standChecklist = stand.Find("SignCanvas/Checklist")?.GetComponent<Text>();
        }
        EditorUtility.SetDirty(view);

        Ensure<ScenarioSessionRecorder>(ctl.gameObject);
        var ap = Ensure<PlanogramApplier>(ctl.gameObject);
        ap.context = sc.context; ap.priceTagRoot = tags; ap.tagMaterial = Unlit;
        EditorUtility.SetDirty(ap);

        Points(root);
        Debug.Log("[NEUROSHELF] 9.5 Табло, кнопки, метки, контроллер сценария");
    }

    static LineRenderer Line(Transform parent, string name, Color c)
    {
        var t = Child(parent, name);
        t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
        var lr = Ensure<LineRenderer>(t.gameObject);
        lr.useWorldSpace = false; lr.widthMultiplier = 0.006f; lr.positionCount = 0;
        lr.sharedMaterial = Mat("M_Route_" + name, "Universal Render Pipeline/Unlit", c);
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        lr.numCapVertices = 2; lr.numCornerVertices = 2;
        lr.alignment = LineAlignment.TransformZ;
        return lr;
    }

    static Bounds HallBounds()
    {
        var f = GameObject.Find("Environment/Structure/Floor");
        return f != null ? Bounds(f) : new Bounds(Vector3.zero, new Vector3(12f, 0f, 8f));
    }

    static void Point(Transform parent, string name, Vector3 pos, float yaw, float pitch = 0f, float fov = 0f)
    {
        var t = Child(parent, name);
        t.position = pos; t.rotation = Quaternion.Euler(pitch, yaw, 0f);
        if (fov > 0f) { var sp = Ensure<ScenarioShotPoint>(t.gameObject); sp.fov = fov; EditorUtility.SetDirty(sp); }
    }

    static void LookPoint(Transform parent, string name, Vector3 pos, Vector3 look, float fov)
    {
        var t = Child(parent, name);
        t.position = pos; t.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
        var sp = Ensure<ScenarioShotPoint>(t.gameObject); sp.fov = fov; EditorUtility.SetDirty(sp);
    }

    static void Points(Transform root)
    {
        var pts = Child(root, "Points");
        Point(pts, "Outside", L.Outside, L.OutsideYaw);
        var standFront = L.StandPos + Quaternion.Euler(0f, L.StandYaw, 0f) * new Vector3(0f, 0f, -0.9f);
        Point(pts, "StandLook", standFront, L.StandYaw);
        Point(pts, "StartLook", L.StartC, L.StartYaw);
        Point(pts, "CheckoutLook", L.CheckoutC, L.CheckoutYaw);

        var cams = Child(root, "Cameras");
        var sc = Object.FindFirstObjectByType<ScenarioController>();
        LookPoint(cams, "Cam_Entry", new Vector3(5.75f, 1.70f, 3.85f), new Vector3(5.1f, 1.35f, 2.2f), 66f);
        LookPoint(cams, "Cam_Board", new Vector3(3.35f, 1.75f, -0.55f), new Vector3(5.9f, 1.40f, 0.60f), 60f);
        LookPoint(cams, "Cam_Stand", standFront + new Vector3(-0.3f, 1.6f, 0f), L.StandPos + new Vector3(0f, 1.0f, 0f), 62f);
        if (sc != null && sc.startButton != null)
        {
            var b = sc.startButton.transform;
            LookPoint(cams, "Cam_StartButton", b.position - b.forward * 0.8f, b.position, 35f);
        }
        LookPoint(cams, "Cam_Shelf", new Vector3(-1.2f, 2.35f, 0.45f), new Vector3(-1.2f, 1.10f, -1.45f), 62f);   // выше голов толпы NPC
        LookPoint(cams, "Cam_Shelf04", new Vector3(1.2f, 2.35f, 0.45f), new Vector3(1.2f, 1.10f, -1.45f), 62f);
        LookPoint(cams, "Cam_Tag", new Vector3(-1.75f, 1.66f, -0.92f), new Vector3(-1.8f, 1.58f, -1.2f), 50f);   // между толпой NPC и полкой
        LookPoint(cams, "Cam_Checkout", new Vector3(3.0f, 1.85f, 0.4f), L.CheckoutButton, 62f);
        var d = L.DebriefBoardPos;
        LookPoint(cams, "Cam_Debrief", new Vector3(d.x, 1.62f, d.z - 1.25f), d, 66f);
    }

    // ===================== ПРОТОКОЛ НАСТРОЙКИ =====================

    static void Verify()
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        void W(string s = "") => sb.AppendLine(s);
        W("NEUROSHELF — протокол настройки сценария " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        W("Unity " + Application.unityVersion + " · XR Interaction Toolkit " + Pkg(typeof(XRGrabInteractable)));
        W();
        W("1. КЛАССЫ XRI, ИСПОЛЬЗОВАННЫЕ В СЦЕНАРИИ (рефлексия по сборке установленного пакета)");
        foreach (var t in new[] { typeof(XRSimpleInteractable), typeof(XRGrabInteractable), typeof(XRSocketInteractor), typeof(NearFarInteractor), typeof(XRInteractionManager) })
            W($"   {t.FullName,-75} {(t.IsDefined(typeof(ObsoleteAttribute), false) ? "[Obsolete]" : "доступен")}");
        var m = typeof(XRInteractionManager).GetMethod("IsSelectPossible");
        W($"   XRInteractionManager.IsSelectPossible: {(m != null ? "есть" : "НЕТ")}");
        W();
        W("2. ЗОНЫ (BoxCollider-триггер, слой " + LayerMask.LayerToName(LayerIgnoreRaycast) + ", сверка каждый шаг физики)");
        foreach (var z in Object.FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).OrderBy(z => z.zoneId))
        {
            var b = z.GetComponent<BoxCollider>();
            W(string.Format(ci, "   {0,-10} центр ({1:0.00}; {2:0.00}) размер {3:0.00} × {4:0.00} м  reconcile={5}", z.zoneId, z.transform.position.x, z.transform.position.z, b.size.x, b.size.z, z.reconcile));
        }
        var cc = Origin()?.GetComponent<CharacterController>();
        W($"   игрок: Character Controller r={cc?.radius:0.00} м, слой {LayerMask.LayerToName(cc != null ? cc.gameObject.layer : 0)}; PlayerBody × Ignore Raycast = {(Physics.GetIgnoreLayerCollision(LayerIgnoreRaycast, LayerPlayer) ? "ВЫКЛ" : "вкл")}");
        W();
        W("3. ПЛАНОГРАММЫ");
        foreach (var c in new[] { "Cond_A_Manipulated_VOLTA", "Cond_A_Manipulated_PURA", "Cond_C_Neutral" })
        {
            var cond = Cond(c);
            W($"   {c} → {cond?.planogram?.planogramName ?? "НЕТ"}");
            if (cond?.planogram == null) continue;
            foreach (var r in cond.planogram.rows) W($"      {r.shelf} ярус {PlanogramSO.TierY[r.tier]:0.00}: {string.Join(", ", r.products)}");
        }
        W();
        W("4. ОБЪЕКТЫ СЦЕНАРИЯ");
        var sc = Object.FindFirstObjectByType<ScenarioController>();
        W($"   ScenarioController: {(sc != null ? "есть" : "НЕТ")}; кнопки: {sc?.startButton?.label}, {sc?.resumeButton?.label}, {sc?.finishButton?.label}, {sc?.nextButton?.label}");
        var carry = Object.FindFirstObjectByType<BasketCarry>();
        var grab = carry != null ? carry.GetComponent<XRGrabInteractable>() : null;
        W($"   корзина: BasketCarry {(carry != null ? "есть" : "НЕТ")}; XR Grab: коллайдеров выбора {grab?.colliders.Count} ({string.Join(", ", grab != null ? grab.colliders.Select(c => c.name) : new string[0])}), movement {grab?.movementType}");
        W($"   слой ScenarioUI ({LayerScenarioUI}) = «{LayerMask.LayerToName(LayerScenarioUI)}», сталкивается с чем-либо: {Enumerable.Range(0, 32).Any(i => !Physics.GetIgnoreLayerCollision(LayerScenarioUI, i))}");
        foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
            W($"   {nf.handedness}: interactionLayers={nf.interactionLayers.value}, Near видит ScenarioUI: {((nf.GetComponent<SphereInteractionCaster>()?.physicsLayerMask.value ?? 0) & (1 << LayerScenarioUI)) != 0}, Far видит ScenarioUI: {((nf.GetComponent<CurveInteractionCaster>()?.raycastMask.value ?? 0) & (1 << LayerScenarioUI)) != 0}");
        var path = Path.Combine(OutDir, "00_Протокол_настройки_сценария.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] Протокол: " + path + "\n" + sb);
    }

    static string Pkg(Type t) => UnityEditor.PackageManager.PackageInfo.FindForAssembly(t.Assembly)?.version ?? "?";

    // ===================== 9.9 КАРТА =====================

    static void MapDump()
    {
        var sb = new StringBuilder("NEUROSHELF — карта зала (Renderer.bounds, м)\n");
        foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => Depth(t) <= 2).Select(t => t.gameObject).OrderBy(g => g.name))
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
            var b = Bounds(g);
            sb.AppendLine(string.Format(ci, "{0,-40} x {1,6:0.00}…{2,6:0.00}  y {3,5:0.00}…{4,5:0.00}  z {5,6:0.00}…{6,6:0.00}", HPath(g.transform), b.min.x, b.max.x, b.min.y, b.max.y, b.min.z, b.max.z));
        }
        var o = Origin(); if (o != null) sb.AppendLine($"XR Origin: {o.transform.position} yaw {o.transform.eulerAngles.y:0}");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "99_Карта_зала.txt"), sb.ToString(), new UTF8Encoding(true));
        Debug.Log(sb.ToString());
    }

    static int Depth(Transform t) => t.parent == null ? 0 : 1 + Depth(t.parent);
    static string HPath(Transform t) => t.parent == null ? t.name : HPath(t.parent) + "/" + t.name;

    // ===================== PLAY MODE =====================

    const string ModeKey = "ns_scenario_mode";
    static object recorder;

    static NeuroshelfScenarioSetup()
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
        ScenarioDemoDriver.OutDir = OutDir;
        var d = new GameObject("~NS_ScenarioDemo").AddComponent<ScenarioDemoDriver>();
        d.mode = mode;
        if (mode == "record") recorder = StartRecorder();
        Debug.Log("[NEUROSHELF] Прогон сценария запущен: " + mode);
    }

    static void Watch()
    {
        if (!EditorApplication.isPlaying) return;
        var mode = SessionState.GetString(ModeKey, "");
        if (mode == "" || !ScenarioDemoDriver.Finished) return;
        SessionState.EraseString(ModeKey);
        if (recorder != null) { ((UnityEditor.Recorder.RecorderController)recorder).StopRecording(); recorder = null; }
        if (mode == "check")
        {
            var path = Path.Combine(OutDir, "41_Прогон_сценария_протокол.txt");
            File.WriteAllText(path, "NEUROSHELF — прогон сценария в Play Mode " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\n\n" + ScenarioDemoDriver.Report, new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 9.7 Протокол: " + path);
        }
        else Debug.Log("[NEUROSHELF] 9.8 Запись завершена → " + RecDir);
        EditorApplication.isPlaying = false;
    }

    static object StartRecorder()
    {
        Directory.CreateDirectory(RecDir);
        var cs = ScriptableObject.CreateInstance<UnityEditor.Recorder.RecorderControllerSettings>();
        var m = ScriptableObject.CreateInstance<UnityEditor.Recorder.MovieRecorderSettings>();
        m.name = "NEUROSHELF scenario";
        m.Enabled = true;
        m.ImageInputSettings = new UnityEditor.Recorder.Input.CameraInputSettings
        {
            Source = UnityEditor.Recorder.ImageSource.MainCamera,
            OutputWidth = 1280,
            OutputHeight = 720,
        };
        m.AudioInputSettings.PreserveAudio = false;
        m.OutputFile = Path.Combine(RecDir, "ns_scenario").Replace('\\', '/');
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
