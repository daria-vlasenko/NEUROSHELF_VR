// NEUROSHELF — освещение, запекание световых карт, пробы, авто-рендер кадров для отчёта.
// Отдельный файл; NeuroshelfMaterialSetup.cs не трогает.
//
// Меню NEUROSHELF → Свет:
//   5.0 Полный цикл                → 5.1 + 5.2 подряд (нажимать обычно только её)
//   5.1 Подготовить свет           → источники, Static, Lighting, пробы, кадры «A_до»
//   5.2 Запечь                     → CPU-запекание, кадры «B_после», текстовый отчёт
//   5.3 Снять кадры (как есть)     → те же ракурсы, суффикс «C_текущее»
//   5.4 Замер FPS                  → 10 с в Play Mode, результат в fps-файл
//   5.5 Снять окна редактора       → Lighting, Inspector, режимы Scene View (эксперимент)
//   5.9 Сбросить запекание
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР3\

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class NeuroshelfLightingSetup
{
    // ===================== ПАРАМЕТРЫ (крутить здесь) =====================

    const string Menu = "NEUROSHELF/Свет/";
    const string LightingRootName = "Lighting";
    const string SettingsFolder = "Assets/_Project/Settings";
    const string SettingsPath = SettingsFolder + "/LS_Store_Baked.lighting";

    // Какой стеллаж продвигаемый (тёплый акцент), какой — уценка (холодный акцент).
    // Индексы по порядку имён Shelf_01…Shelf_04.
    // Товары по планограмме стоят на южном ряду: Shelf_03 (дорогие на уровне глаз)
    // и Shelf_04 (базовые внизу) — соседние стеллажи, контраст попадает в один кадр.
    const int PromoShelf = 2;
    const int DiscountShelf = 3;
    const int UnderShelfShot = 2;   // стеллаж для кадра «под нижней полкой»

    // Цветовые температуры, K
    const float SunK = 5500f, CeilingK = 4000f, WarmK = 3000f, ColdK = 5000f;

    // Интенсивности (линейные единицы URP, спад обратно квадрату расстояния)
    const float SunIntensity = 0.6f;
    const float CeilingIntensity = 2.5f, CeilingRange = 7f;
    const float AccentIntensity = 18.0f, AccentRange = 5f;

    // Лайтмапы: щадящие настройки под встроенную графику, CPU-лайтмаппер
    const float TexelsPerUnit = 12f;
    const int LightmapMaxSize = 1024;

    // Кадры
    const int ShotW = 1920, ShotH = 1080;

    static string OutDir =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР3"));

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "5.0 Полный цикл (подготовить → кадры ДО → запечь → кадры ПОСЛЕ)", priority = 100)]
    public static void FullCycle()
    {
        if (!Prepare()) return;
        Bake();
    }

    [MenuItem(Menu + "5.1 Подготовить свет, Static, Lighting, пробы + кадры ДО", priority = 101)]
    public static void PrepareMenu() => Prepare();

    [MenuItem(Menu + "5.2 Запечь (CPU) + кадры ПОСЛЕ + отчёт", priority = 102)]
    public static void BakeMenu() => Bake();

    [MenuItem(Menu + "5.3 Снять кадры текущего состояния", priority = 120)]
    public static void ShotsNow()
    {
        var ctx = SceneCtx.Find(); if (ctx == null) return;
        RenderAll(ctx, "C_текущее");
    }

    [MenuItem(Menu + "5.4 Замер FPS (10 секунд в Play Mode)", priority = 121)]
    public static void MeasureFps()
    {
        SessionState.SetBool(NeuroshelfFpsHook.Key, true);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem(Menu + "5.5 Снять окна редактора (экспериментально)", priority = 122)]
    public static void GrabEditorWindows() => EditorGrab.Run();

    [MenuItem(Menu + "5.8 Диагностика сцены", priority = 139)]
    public static void Diagnose()
    {
        var ctx = SceneCtx.Find(); if (ctx == null) return;
        var sb = new StringBuilder("[NEUROSHELF] Диагностика\n");
        sb.AppendLine($"room={ctx.room} floorY={ctx.floorY} ceilingY={ctx.ceilingY}");
        foreach (var n in new[] { "Floor", "Ceiling" })
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name == n))
                sb.AppendLine($"{n}: path={HPath(t)} bounds={t.GetComponent<Renderer>()?.bounds}");
        foreach (var s in Shots(ctx)) sb.AppendLine($"shot {s.file}: pos={s.pos} look={s.look}");
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(IsProduct))
            sb.AppendLine($"product {HPath(r.transform)} pos={r.bounds.center} static={GameObjectUtility.GetStaticEditorFlags(r.gameObject)}");
        Debug.Log(sb.ToString());
    }

    static string HPath(Transform t) => t.parent == null ? t.name : HPath(t.parent) + "/" + t.name;

    [MenuItem(Menu + "5.9 Сбросить запекание", priority = 140)]
    public static void ClearBake()
    {
        Lightmapping.Clear();
        Lightmapping.ClearLightingDataAsset();
        Debug.Log("[NEUROSHELF] Запечённые данные удалены.");
    }

    // ===================== 5.1 ПОДГОТОВКА =====================

    static bool Prepare()
    {
        var ctx = SceneCtx.Find(); if (ctx == null) return false;
        Directory.CreateDirectory(OutDir);

        EnsureUrpUsesLightProbeGroups();
        var lights = BuildLights(ctx);
        var (staticCount, dynamicCount, uvIssues) = SetupStaticFlags(ctx);
        SetupLightingSettings();
        BuildProbes(ctx);

        // Кадры «ДО»: те же источники, но в режиме Realtime и без лайтмап.
        // Это картинка, которую дал бы движок без запекания: только прямой свет,
        // без переотражений, без мягких теней и затенения в углах.
        Lightmapping.Clear();
        SetMode(lights, LightmapBakeType.Realtime);
        RenderAll(ctx, "A_до");
        SetMode(lights, LightmapBakeType.Baked);

        EditorSceneManager.MarkSceneDirty(ctx.scene);
        EditorSceneManager.SaveScene(ctx.scene);

        Debug.Log($"[NEUROSHELF] Свет подготовлен: источников {lights.Count}, " +
                  $"статичных объектов {staticCount}, динамических (товары) {dynamicCount}." +
                  (uvIssues.Count > 0 ? $"\nБез UV2: {string.Join(", ", uvIssues)}" : "\nUV2 есть у всех статичных мешей."));
        return true;
    }

    // ---------- источники ----------

    static List<Light> BuildLights(SceneCtx c)
    {
        var old = GameObject.Find(LightingRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(LightingRootName);
        Undo.RegisterCreatedObjectUndo(root, "NEUROSHELF Lighting");
        var list = new List<Light>();

        // 1) Directional — дневной свет через зенитное остекление.
        //    Потолок не отбрасывает тень (см. SetupStaticFlags), поэтому солнце
        //    проходит внутрь и даёт тени стеллажей на полу.
        var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.name = "L_Sun_Directional";
        sun.transform.SetParent(root.transform, true);
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
        Common(sun, SunK, SunIntensity);
        sun.shadowAngle = 2.5f;                         // мягкость запечённой тени
        list.Add(sun);

        // 2) Point — потолочные светильники сеткой по залу
        int nx = c.room.size.x >= c.room.size.z ? 3 : 2;
        int nz = c.room.size.x >= c.room.size.z ? 2 : 3;
        float y = c.ceilingY - 0.25f;
        for (int i = 0; i < nx; i++)
        for (int j = 0; j < nz; j++)
        {
            var p = new Vector3(
                Mathf.Lerp(c.room.min.x, c.room.max.x, (i + 0.5f) / nx), y,
                Mathf.Lerp(c.room.min.z, c.room.max.z, (j + 0.5f) / nz));
            var l = NewLight(root.transform, $"L_Ceiling_{i + 1}{j + 1}", LightType.Point, p, Quaternion.identity);
            Common(l, CeilingK, CeilingIntensity);
            l.range = CeilingRange;
            l.shadowRadius = 0.15f;
            list.Add(l);
        }

        // 3) Spot — акценты (эффект №9, LightAccent)
        if (c.shelves.Length > PromoShelf)
            list.Add(Accent(root.transform, "L_Accent_Warm3000K_Promo", c.shelves[PromoShelf], c, WarmK));
        if (c.shelves.Length > DiscountShelf)
            list.Add(Accent(root.transform, "L_Accent_Cold5000K_Discount", c.shelves[DiscountShelf], c, ColdK));
        if (c.checkout != null)
        {
            var top = new Vector3(c.checkout.position.x, c.ceilingY - 0.15f, c.checkout.position.z);
            var aim = c.checkout.position + Vector3.up * 1.0f;
            var l = NewLight(root.transform, "L_Accent_Warm3000K_Checkout", LightType.Spot, top,
                             Quaternion.LookRotation(aim - top));
            Common(l, WarmK, AccentIntensity);
            l.range = AccentRange; l.spotAngle = 65f; l.innerSpotAngle = 35f; l.shadowRadius = 0.08f;
            list.Add(l);
        }
        return list;
    }

    static Light Accent(Transform parent, string name, Transform shelf, SceneCtx c, float k)
    {
        var front = ShelfFront(shelf, c.room.center);
        var pos = shelf.position + front * 1.3f;
        pos.y = c.ceilingY - 0.15f;
        var aim = shelf.position + Vector3.up * 1.1f + front * 0.1f;
        var l = NewLight(parent, name, LightType.Spot, pos, Quaternion.LookRotation(aim - pos));
        Common(l, k, AccentIntensity);
        l.range = AccentRange; l.spotAngle = 55f; l.innerSpotAngle = 30f; l.shadowRadius = 0.08f;
        return l;
    }

    static Light NewLight(Transform parent, string name, LightType t, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var l = go.AddComponent<Light>();
        l.type = t;
        return l;
    }

    static void Common(Light l, float kelvin, float intensity)
    {
        l.useColorTemperature = true;
        l.colorTemperature = kelvin;
        l.color = Color.white;
        l.intensity = intensity;
        l.bounceIntensity = 1f;
        l.shadows = LightShadows.Soft;
        l.lightmapBakeType = LightmapBakeType.Baked;
    }

    static void SetMode(List<Light> lights, LightmapBakeType mode)
    {
        foreach (var l in lights) if (l != null) l.lightmapBakeType = mode;
    }

    // ---------- Static ----------

    static (int, int, List<string>) SetupStaticFlags(SceneCtx c)
    {
        // «Everything» — как в методичке: все флаги Static разом
        StaticEditorFlags flags = 0;
        foreach (StaticEditorFlags f in Enum.GetValues(typeof(StaticEditorFlags))) flags |= f;
        int st = 0, dyn = 0;
        var uvIssues = new List<string>();
        var reimport = new HashSet<string>();

        foreach (var r in c.env.GetComponentsInChildren<MeshRenderer>(true))
        {
            var go = r.gameObject;
            if (IsProduct(r))
            {
                // Товар: не статичен (в ЛР5 его берут в руки). Свет получает от Light Probes.
                GameObjectUtility.SetStaticEditorFlags(go, 0);
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                dyn++;
                continue;
            }

            GameObjectUtility.SetStaticEditorFlags(go, flags);
            r.receiveGI = ReceiveGI.Lightmaps;
            r.shadowCastingMode = go.name == "Ceiling" ? ShadowCastingMode.Off : ShadowCastingMode.On;

            var so = new SerializedObject(r);
            var scale = so.FindProperty("m_ScaleInLightmap");
            if (scale != null)
            {
                scale.floatValue = go.name == "Floor" ? 1.5f : go.name == "Ceiling" ? 0.5f : 1f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            st++;

            var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
            {
                var path = AssetDatabase.GetAssetPath(mesh);
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.generateSecondaryUV)
                { mi.generateSecondaryUV = true; reimport.Add(path); }
                else uvIssues.Add(go.name);
            }
        }
        // товары могут лежать вне Environment (корневой объект NEUROSHELF_Products)
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!IsProduct(r) || r.transform.IsChildOf(c.env)) continue;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, 0);
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            dyn++;
        }
        foreach (var p in reimport) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        return (st, dyn, uvIssues);
    }

    static bool IsProduct(Renderer r)
    {
        var mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh != null && AssetDatabase.GetAssetPath(mesh).Contains("NEUROSHELF_Products")) return true;
        for (var t = r.transform; t != null; t = t.parent)
            if (t.name.StartsWith("Pack_") || t.name.StartsWith("Can_") || t.name.StartsWith("Bottle_")) return true;
        return false;
    }

    // ---------- Lighting Settings ----------

    static void SetupLightingSettings()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
            AssetDatabase.CreateFolder("Assets/_Project", "Settings");

        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
        if (ls == null)
        {
            ls = new LightingSettings { name = "LS_Store_Baked" };
            AssetDatabase.CreateAsset(ls, SettingsPath);
        }

        ls.bakedGI = true;
        ls.realtimeGI = false;
        ls.autoGenerate = false;
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;   // GPU на Intel Arc ненадёжен
        ls.lightmapResolution = TexelsPerUnit;
        ls.lightmapPadding = 4;
        ls.lightmapMaxSize = LightmapMaxSize;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.directionalityMode = LightmapsMode.NonDirectional;            // вдвое меньше памяти
        ls.ao = true;
        ls.aoMaxDistance = 0.6f;
        ls.directSampleCount = 32;
        ls.indirectSampleCount = 256;
        ls.environmentSampleCount = 128;
        ls.maxBounces = 2;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        EditorUtility.SetDirty(ls);
        AssetDatabase.SaveAssets();
        Lightmapping.lightingSettings = ls;

        // Окружение: тусклый нейтральный эмбиент вместо неба (помещение закрытое)
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.09f, 0.09f, 0.10f);

        GraphicsSettings.lightsUseLinearIntensity = true;
        GraphicsSettings.lightsUseColorTemperature = true;
    }

    // В URP 17 есть Adaptive Probe Volumes. Если они включены, Light Probe Group игнорируется.
    static void EnsureUrpUsesLightProbeGroups()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(asset);
            var p = so.FindProperty("m_LightProbeSystem");
            if (p != null && p.intValue != 0)
            {
                p.intValue = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[NEUROSHELF] {asset.name}: Light Probe System → Light Probe Groups");
            }
        }
    }

    // ---------- Light Probes + Reflection Probe ----------

    static void BuildProbes(SceneCtx c)
    {
        var root = GameObject.Find(LightingRootName).transform;
        var pts = new List<Vector3>();
        var blockers = c.shelves.Select(s => BoundsOf(s, 0.08f)).ToList();
        if (c.checkout != null) blockers.Add(BoundsOf(c.checkout, 0.08f));

        // сетка по залу, шаг 1.5 м, три уровня высоты
        for (float x = c.room.min.x + 0.5f; x <= c.room.max.x - 0.5f + 1e-3f; x += 1.5f)
        for (float z = c.room.min.z + 0.5f; z <= c.room.max.z - 0.5f + 1e-3f; z += 1.5f)
        foreach (var h in new[] { 0.4f, 1.4f, 2.4f })
        {
            var p = new Vector3(x, c.floorY + h, z);
            if (!blockers.Any(b => b.Contains(p))) pts.Add(p);
        }

        // плотнее у стеллажей: внутри полок (там стоят товары) и в проходе перед ними
        foreach (var s in c.shelves)
        {
            var f = ShelfFront(s, c.room.center);
            var right = Vector3.Cross(Vector3.up, f);
            foreach (var x in new[] { -0.9f, 0f, 0.9f })
            foreach (var y in new[] { 0.3f, 0.85f, 1.35f, 1.85f })
            foreach (var d in new[] { 0.05f, 0.55f })
                pts.Add(s.position + right * x + Vector3.up * y + f * d);
        }

        var lpg = new GameObject("LightProbes_Store").AddComponent<LightProbeGroup>();
        lpg.transform.SetParent(root, false);
        lpg.probePositions = pts.ToArray();

        var rp = new GameObject("ReflectionProbe_Store").AddComponent<ReflectionProbe>();
        rp.transform.SetParent(root, false);
        rp.transform.position = new Vector3(c.room.center.x, c.floorY + 1.5f, c.room.center.z);
        rp.mode = ReflectionProbeMode.Baked;
        rp.size = new Vector3(c.room.size.x, c.ceilingY - c.floorY, c.room.size.z);
        rp.boxProjection = true;
        rp.resolution = 128;

        Debug.Log($"[NEUROSHELF] Light Probes: {pts.Count} точек; Reflection Probe: 1 (Baked, 128).");
    }

    // ===================== 5.2 ЗАПЕКАНИЕ =====================

    static readonly Stopwatch BakeTimer = new Stopwatch();

    static void Bake()
    {
        if (Lightmapping.isRunning) { Debug.LogWarning("[NEUROSHELF] Запекание уже идёт."); return; }
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).ToList();
        SetMode(lights, LightmapBakeType.Baked);

        Lightmapping.bakeCompleted -= OnBakeCompleted;
        Lightmapping.bakeCompleted += OnBakeCompleted;
        BakeTimer.Restart();
        if (!Lightmapping.BakeAsync())
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            Debug.LogError("[NEUROSHELF] Запекание не запустилось. Проверь, что открыта сцена 02_Store.");
            return;
        }
        Debug.Log("[NEUROSHELF] Запекание запущено (CPU). Прогресс — внизу справа в редакторе. " +
                  "Кадры «ПОСЛЕ» и отчёт снимутся сами.");
    }

    static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        BakeTimer.Stop();
        var ctx = SceneCtx.Find(); if (ctx == null) return;
        EditorSceneManager.SaveScene(ctx.scene);
        RenderAll(ctx, "B_после");
        WriteReport(ctx);
    }

    // ===================== ОТЧЁТ =====================

    static void WriteReport(SceneCtx c)
    {
        var sb = new StringBuilder();
        var ls = Lightmapping.lightingSettings;
        sb.AppendLine("NEUROSHELF — отчёт об освещении");
        sb.AppendLine($"Дата: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} · CPU {SystemInfo.processorType}");
        sb.AppendLine();
        sb.AppendLine("[Lighting Settings]");
        sb.AppendLine($"Lightmapper: {ls.lightmapper}");
        sb.AppendLine($"Lightmap Resolution: {ls.lightmapResolution} texels/unit");
        sb.AppendLine($"Lightmap Padding: {ls.lightmapPadding} px · Max Size: {ls.lightmapMaxSize}");
        sb.AppendLine($"Compression: {ls.lightmapCompression} · Directional Mode: {ls.directionalityMode}");
        sb.AppendLine($"Samples direct/indirect/env: {ls.directSampleCount}/{ls.indirectSampleCount}/{ls.environmentSampleCount}");
        sb.AppendLine($"Bounces: {ls.maxBounces} · AO: {ls.ao} ({ls.aoMaxDistance} м) · Filtering: {ls.filteringMode}");
        sb.AppendLine($"Время запекания: {BakeTimer.Elapsed.TotalSeconds:F1} с");
        sb.AppendLine();

        sb.AppendLine("[Источники света]");
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            sb.AppendLine($"{l.name,-32} {l.type,-11} {l.lightmapBakeType,-8} {l.colorTemperature,5:F0} K  I={l.intensity:F1}" +
                          (l.type == LightType.Directional ? "" : $"  R={l.range:F1} м") +
                          (l.type == LightType.Spot ? $"  угол={l.spotAngle:F0}°" : ""));
        sb.AppendLine();

        var all = c.env.GetComponentsInChildren<MeshRenderer>(true);
        var stat = all.Where(r => GameObjectUtility.GetStaticEditorFlags(r.gameObject).HasFlag(StaticEditorFlags.ContributeGI)).ToList();
        int mapped = stat.Count(r => r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE);
        sb.AppendLine("[Объекты]");
        sb.AppendLine($"Статичных (Contribute GI): {stat.Count}, из них получили лайтмапу: {mapped}");
        sb.AppendLine($"Динамических (товары, освещаются пробами): {Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Count(IsProduct)}");
        sb.AppendLine();

        var maps = LightmapSettings.lightmaps;
        sb.AppendLine("[Лайтмапы]");
        sb.AppendLine($"Количество: {maps.Length}");
        long bytes = 0;
        for (int i = 0; i < maps.Length; i++)
        {
            var t = maps[i].lightmapColor;
            if (t == null) continue;
            long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
            bytes += b;
            sb.AppendLine($"  #{i}: {t.width}×{t.height}, {t.format}, {b / 1024f / 1024f:F2} МБ");
        }
        sb.AppendLine($"Итого видеопамяти на лайтмапы: {bytes / 1024f / 1024f:F2} МБ");
        sb.AppendLine($"Light Probes в запечённых данных: {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}");
        sb.AppendLine($"LightingDataAsset: {(Lightmapping.lightingDataAsset != null ? AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset) : "нет")}");

        var path = Path.Combine(OutDir, "00_Отчёт_освещения.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] Отчёт: " + path + "\n" + sb);
    }

    // ===================== КАДРЫ =====================

    struct Shot { public string file; public Vector3 pos, look; public float fov; }

    static List<Shot> Shots(SceneCtx c)
    {
        var list = new List<Shot>();
        var corners = new[]
        {
            new Vector3(c.room.min.x, c.floorY, c.room.min.z), new Vector3(c.room.max.x, c.floorY, c.room.min.z),
            new Vector3(c.room.min.x, c.floorY, c.room.max.z), new Vector3(c.room.max.x, c.floorY, c.room.max.z),
        };
        var entry = c.entry != null ? c.entry.position : c.room.center;
        var far = corners.OrderByDescending(p => Flat(p - entry).sqrMagnitude).First();
        var near = corners.OrderBy(p => Flat(p - far).sqrMagnitude).Last();   // диагонально от «far»
        var center = new Vector3(c.room.center.x, c.floorY, c.room.center.z);

        // 01 — общий вид из дальнего от входа угла, под потолком
        var p1 = far + Flat(center - far).normalized * 0.6f; p1.y = c.ceilingY - 0.35f;
        list.Add(new Shot { file = "01_Общий_вид_зала", pos = p1, look = center + Vector3.up * 0.6f, fov = 75f });

        // 02 — от точки входа, уровень глаз 1.65 м, взгляд в зал
        var eye = new Vector3(entry.x, c.floorY + 1.65f, entry.z);
        // взгляд в боковой проход между торцами стеллажей и стеной, чуть ниже горизонта
        var aisle = new Vector3(c.room.min.x + 1.6f, c.floorY + 1.2f, c.room.center.z + 0.5f);
        list.Add(new Shot { file = "02_Вход_уровень_глаз_1.65м", pos = eye, look = aisle, fov = 70f });

        // 03 / 04 — акцентные стеллажи
        void ShelfShot(int i, string file)
        {
            if (c.shelves.Length <= i) return;
            var s = c.shelves[i]; var f = ShelfFront(s, c.room.center); var r = Vector3.Cross(Vector3.up, f);
            list.Add(new Shot { file = file, pos = s.position + f * 2.3f + r * 0.5f + Vector3.up * 1.5f,
                                look = s.position + Vector3.up * 1.05f, fov = 55f });
        }
        ShelfShot(PromoShelf, "03_Стеллаж_тёплый_акцент_3000K");
        ShelfShot(DiscountShelf, "04_Стеллаж_уценка_холодный_5000K");

        // 05 — касса (импульсная зона)
        if (c.checkout != null)
        {
            var k = c.checkout.position; var to = Flat(center - k).normalized;
            list.Add(new Shot { file = "05_Касса_импульсная_зона_3000K", pos = k + to * 2.4f + Vector3.up * 1.6f,
                                look = k + Vector3.up * 0.9f, fov = 55f });
        }

        // 06 — пространство под нижней полкой (мягкая тень и AO)
        if (c.shelves.Length > UnderShelfShot)
        {
            var s = c.shelves[UnderShelfShot]; var f = ShelfFront(s, c.room.center); var r = Vector3.Cross(Vector3.up, f);
            list.Add(new Shot { file = "06_Под_нижней_полкой_мягкая_тень", pos = s.position + f * 1.1f + r * 0.6f + Vector3.up * 0.35f,
                                look = s.position + f * 0.1f + Vector3.up * 0.2f, fov = 60f });
        }

        // 07 — угол комнаты (стык двух стен и пола)
        var cp = near + Flat(center - near).normalized * 2.6f; cp.y = c.floorY + 1.4f;
        list.Add(new Shot { file = "07_Угол_комнаты", pos = cp, look = near + Vector3.up * 0.4f, fov = 60f });
        return list;
    }

    static void RenderAll(SceneCtx c, string suffix)
    {
        Directory.CreateDirectory(OutDir);
        var shots = Shots(c);
        // холостой кадр: первый рендер после изменений сцены бывает неполным
        RenderShot(shots[0], Path.Combine(Path.GetTempPath(), "ns_warmup.png"));
        foreach (var s in shots) RenderShot(s, Path.Combine(OutDir, $"{s.file}_{suffix}.png"));
        Debug.Log($"[NEUROSHELF] Кадры «{suffix}»: {shots.Count} шт. → {OutDir}");
    }

    static void RenderShot(Shot s, string path)
    {
        var go = new GameObject("~NS_Capture") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
        try
        {
            go.transform.SetPositionAndRotation(s.pos, Quaternion.LookRotation(s.look - s.pos, Vector3.up));
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = s.fov;
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 60f;
                        var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
        }
        finally
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(tex);
            rt.Release(); Object.DestroyImmediate(rt);
        }
    }

    // ===================== ВСПОМОГАТЕЛЬНОЕ =====================

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    // Лицевая сторона стеллажа: противоположная задней стенке «Back»
    static Vector3 ShelfFront(Transform s, Vector3 roomCenter)
    {
        var back = s.Find("Back");
        if (back != null && Mathf.Abs(back.localPosition.z) > 0.01f)
            return back.localPosition.z < 0 ? s.forward : -s.forward;
        var toC = Flat(roomCenter - s.position);
        return Vector3.Dot(toC, s.forward) >= 0 ? s.forward : -s.forward;
    }

    static Bounds BoundsOf(Transform t, float pad)
    {
        var rs = t.GetComponentsInChildren<Renderer>().Where(r => !IsProduct(r)).ToArray();
        if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        b.Expand(pad * 2f);
        return b;
    }

    class SceneCtx
    {
        public UnityEngine.SceneManagement.Scene scene;
        public Transform env, checkout, entry;
        public Transform[] shelves;
        public Bounds room;
        public float floorY, ceilingY;

        public static SceneCtx Find()
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform T(string n) => all.FirstOrDefault(t => t.name == n);

            var c = new SceneCtx { scene = EditorSceneManager.GetActiveScene(), env = T("Environment") };
            if (c.env == null)
            { Debug.LogError("[NEUROSHELF] Не найден объект Environment. Открой сцену 02_Store."); return null; }

            var floor = T("Floor")?.GetComponent<Renderer>();
            c.room = floor != null ? floor.bounds : new Bounds(new Vector3(0, 0, 0), new Vector3(12, 0, 8));
            c.floorY = floor != null ? floor.bounds.max.y : 0f;
            var ceil = T("Ceiling")?.GetComponent<Renderer>();
            c.ceilingY = ceil != null ? ceil.bounds.min.y : c.floorY + 3f;

            c.shelves = all.Where(t => t.name.StartsWith("Shelf_0")).OrderBy(t => t.name).ToArray();
            c.checkout = T("Checkout_Counter") ?? T("Checkout");
            c.entry = T("XR Origin (VR)");
            return c;
        }
    }

    // ===================== 5.5 ОКНА РЕДАКТОРА (эксперимент) =====================

    static class EditorGrab
    {
        class Step { public int wait; public Action act; }
        static readonly List<Step> Queue = new List<Step>();
        static SceneView.CameraMode savedMode;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            Queue.Clear();

            Add(0, () => EditorApplication.ExecuteMenuItem("Window/Rendering/Lighting"));
            Add(30, () => Grab("LightingWindow", "10_Окно_Lighting_настройки"));

            Add(0, () =>
            {
                var spot = GameObject.Find("L_Accent_Warm3000K_Promo");
                if (spot != null) Selection.activeGameObject = spot;
                EditorApplication.ExecuteMenuItem("Window/General/Inspector");
            });
            Add(30, () => Grab("InspectorWindow", "11_Inspector_тёплый_спот"));

            Add(0, () =>
            {
                var sv = SceneView.lastActiveSceneView; if (sv == null) return;
                var ctx = SceneCtx.Find(); if (ctx == null) return;
                savedMode = sv.cameraMode;
                var shot = Shots(ctx)[0];
                sv.pivot = shot.look;
                sv.rotation = Quaternion.LookRotation(shot.look - shot.pos);
                sv.size = 6f;
                sv.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.BakedLightmap);
                sv.Focus(); sv.Repaint();
            });
            Add(40, () => Grab("SceneView", "12_SceneView_Baked_Lightmap"));
            Add(0, () =>
            {
                var sv = SceneView.lastActiveSceneView; if (sv == null) return;
                sv.cameraMode = SceneView.GetBuiltinCameraMode(DrawCameraMode.BakedUVOverlap);
                sv.Repaint();
            });
            Add(40, () => Grab("SceneView", "13_SceneView_UV_Overlap"));
            Add(0, () =>
            {
                var sv = SceneView.lastActiveSceneView; if (sv == null) return;
                sv.cameraMode = savedMode; sv.Repaint();
                Debug.Log("[NEUROSHELF] Окна редактора сняты → " + OutDir +
                          "\nЕсли кадр обрезан или смещён (масштаб экрана Windows) — сними это окно вручную.");
            });

            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Add(int wait, Action a) => Queue.Add(new Step { wait = wait, act = a });

        static void Tick()
        {
            if (Queue.Count == 0) { EditorApplication.update -= Tick; return; }
            var s = Queue[0];
            if (s.wait-- > 0) return;
            Queue.RemoveAt(0);
            try { s.act(); } catch (Exception e) { Debug.LogException(e); }
        }

        static void Grab(string typeName, string file)
        {
            var w = Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(x => x.GetType().Name == typeName);
            if (w == null) { Debug.LogWarning($"[NEUROSHELF] Окно {typeName} не найдено."); return; }
            w.Focus();
            var r = w.position;
            float k = EditorGUIUtility.pixelsPerPoint;
            int W = Mathf.RoundToInt(r.width * k), H = Mathf.RoundToInt(r.height * k);
            var px = InternalEditorUtility.ReadScreenPixel(new Vector2(r.x * k, r.y * k), W, H);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, file + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}

// Хук для замера FPS: после входа в Play Mode добавляет в сцену FpsProbe.
[InitializeOnLoad]
static class NeuroshelfFpsHook
{
    public const string Key = "NEUROSHELF_FPS_PENDING";

    static NeuroshelfFpsHook()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            new GameObject("~FpsProbe").AddComponent<FpsProbe>();
        };
    }
}
