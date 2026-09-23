// ============================================================================
//  NEUROSHELF — сборка материалов из атласа и расстановка товаров по планограмме
//
//  Меню NEUROSHELF:
//    1. Собрать материалы            — материал товаров + 4 материала окружения
//    2. Разложить материалы по сцене — назначает их объектам по именам
//    3. Расставить товары по планограмме — раскладывает упаковки по ярусам
//    4. Показать статистику Draw Calls
//
//  Куда положить: Assets/Editor/NeuroshelfMaterialSetup.cs
// ============================================================================

using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public static class NeuroshelfMaterialSetup
{
    const string TexDir = "Assets/_Project/Art/Textures";
    const string MatDir = "Assets/_Project/Materials";

    const string Albedo    = "NEUROSHELF_Atlas_Albedo";
    const string MetSmooth = "NEUROSHELF_Atlas_MetallicSmoothness";
    const string Normal    = "NEUROSHELF_Atlas_Normal";

    const int Grid = 4;
    const float Cell = 1f / Grid;

    struct EnvMat { public string name; public int cell; public float smooth; }
    static readonly EnvMat[] EnvMaterials =
    {
        new EnvMat { name = "M_Env_Metal",   cell = 0, smooth = 0.72f },
        new EnvMat { name = "M_Env_Plastic", cell = 1, smooth = 0.58f },
        new EnvMat { name = "M_Env_Wood",    cell = 2, smooth = 0.32f },
        new EnvMat { name = "M_Env_Floor",   cell = 3, smooth = 0.45f },
    };

    // ================== 1. МАТЕРИАЛЫ ==================
    [MenuItem("NEUROSHELF/1. Собрать материалы")]
    public static void BuildAll()
    {
        var albedoTex = ImportTexture(Albedo,    sRGB: true,  isNormal: false);
        var maskTex   = ImportTexture(MetSmooth, sRGB: false, isNormal: false);
        var normalTex = ImportTexture(Normal,    sRGB: false, isNormal: true);

        if (albedoTex == null) { Debug.LogError($"[NEUROSHELF] Нет {Albedo}.png в {TexDir}."); return; }

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) { Debug.LogError("[NEUROSHELF] Шейдер URP Lit не найден."); return; }

        Directory.CreateDirectory(MatDir);

        MakeMaterial("M_NEUROSHELF_Atlas", shader, albedoTex, maskTex, normalTex,
                     Vector2.one, Vector2.zero, 1f);

        foreach (var e in EnvMaterials)
            MakeMaterial(e.name, shader, albedoTex, maskTex, normalTex,
                         new Vector2(Cell, Cell), CellOffset(e.cell), e.smooth);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[NEUROSHELF] Материалов создано: {EnvMaterials.Length + 1}");
    }

    static Vector2 CellOffset(int cell) =>
        new Vector2((cell % Grid) * Cell, 1f - (cell / Grid + 1) * Cell);

    static Material MakeMaterial(string name, Shader shader,
                                 Texture2D albedo, Texture2D mask, Texture2D normal,
                                 Vector2 tiling, Vector2 offset, float smoothness)
    {
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;

        mat.SetTexture("_BaseMap", albedo);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetTextureOffset("_BaseMap", offset);

        if (mask != null)
        {
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTextureScale("_MetallicGlossMap", tiling);
            mat.SetTextureOffset("_MetallicGlossMap", offset);
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetTextureScale("_BumpMap", tiling);
            mat.SetTextureOffset("_BumpMap", offset);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ================== 2. РАСКЛАДКА МАТЕРИАЛОВ ==================
    [MenuItem("NEUROSHELF/2. Разложить материалы по сцене")]
    public static void ApplyMaterials()
    {
        var metal   = Load("M_Env_Metal");
        var plastic = Load("M_Env_Plastic");
        var wood    = Load("M_Env_Wood");
        var floor   = Load("M_Env_Floor");
        var atlas   = Load("M_NEUROSHELF_Atlas");

        if (metal == null || atlas == null)
        { Debug.LogError("[NEUROSHELF] Сначала «1. Собрать материалы»."); return; }

        int n = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string nm = r.gameObject.name;
            Material t = null;
            if      (nm.StartsWith("Floor"))    t = floor;
            else if (nm.StartsWith("Ceiling"))  t = plastic;
            else if (nm.StartsWith("Wall"))     t = plastic;
            else if (nm.StartsWith("Checkout")) t = wood;
            else if (nm.StartsWith("Side_") || nm.StartsWith("Back") || nm.StartsWith("Shelf_")) t = metal;
            else if (IsProduct(nm))             t = atlas;
            if (t == null) continue;

            Undo.RecordObject(r, "Раскладка материалов");
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = t;
            r.sharedMaterials = arr;
            n++;
        }
        Debug.Log($"[NEUROSHELF] Материалы назначены {n} объектам.");
        Stats();
    }

    static bool IsProduct(string n) =>
        n.StartsWith("Pack_") || n.StartsWith("Can_") || n.StartsWith("Bottle_");

    static Material Load(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

    // ================== 3. ПЛАНОГРАММА ==================
    //
    // Раскладка условия A: дорогие позиции на уровне глаз, базовые товары внизу.
    // Именно эта асимметрия и есть проверяемый эффект уровня глаз.
    //
    // tier: 0 — нижний ярус 0.60 м, 1 — средний 1.10 м, 2 — уровень глаз 1.60 м
    struct Slot { public string obj; public int shelf; public int tier; public int pos; }
    static readonly Slot[] Planogram =
    {
        // уровень глаз — высокая маржа
        new Slot { obj = "Can_VOLTA",    shelf = 0, tier = 2, pos = 0 },
        new Slot { obj = "Can_FERRO",    shelf = 0, tier = 2, pos = 1 },
        new Slot { obj = "Bottle_PURA",  shelf = 1, tier = 2, pos = 0 },
        // средний ярус
        new Slot { obj = "Bottle_NUBO",  shelf = 0, tier = 1, pos = 0 },
        new Slot { obj = "Can_ZEST",     shelf = 0, tier = 1, pos = 1 },
        new Slot { obj = "Pack_KRISP",   shelf = 1, tier = 1, pos = 0 },
        new Slot { obj = "Pack_OKTA",    shelf = 1, tier = 1, pos = 1 },
        new Slot { obj = "Bottle_LUMEN", shelf = 1, tier = 1, pos = 2 },
        // нижний ярус — базовые товары
        new Slot { obj = "Bottle_AURA",  shelf = 0, tier = 0, pos = 0 },
        new Slot { obj = "Pack_MIRA",    shelf = 0, tier = 0, pos = 1 },
        new Slot { obj = "Pack_NORDA",   shelf = 1, tier = 0, pos = 0 },
        new Slot { obj = "Pack_GRANO",   shelf = 1, tier = 0, pos = 1 },
    };

    static readonly float[] TierY = { 0.60f, 1.10f, 1.60f };  // высоты полок
    const float BoardHalf = 0.025f;                            // половина толщины полки
    const float FrontZ    = 0.05f;                             // чуть вперёд от центра стеллажа

    [MenuItem("NEUROSHELF/3. Расставить товары по планограмме")]
    public static void PlaceProducts()
    {
        var shelves = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                            .Where(t => t.name.StartsWith("Shelf_0"))
                            .OrderBy(t => t.name)
                            .ToArray();

        if (shelves.Length < 2)
        { Debug.LogError("[NEUROSHELF] Не нашла стеллажи Shelf_01…Shelf_04."); return; }

        int placed = 0, missing = 0;
        foreach (var s in Planogram)
        {
            var obj = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                            .FirstOrDefault(t => t.name == s.obj);
            if (obj == null) { missing++; continue; }

            var shelf = shelves[Mathf.Clamp(s.shelf, 0, shelves.Length - 1)];

            Undo.RecordObject(obj, "Расстановка по планограмме");
            obj.SetParent(shelf, worldPositionStays: false);

            // разносим по ширине полки: три слота на ярус
            float x = -0.75f + s.pos * 0.75f;
            obj.localPosition = new Vector3(x, 0f, FrontZ);
            obj.localRotation = Quaternion.identity;

            // ставим ровно на поверхность полки, независимо от того,
            // где у модели точка привязки — в центре или у основания
            var rend = obj.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                float shelfTopWorld = shelf.TransformPoint(
                    new Vector3(0f, TierY[s.tier] + BoardHalf, 0f)).y;
                float pivotToBottom = obj.position.y - rend.bounds.min.y;
                var p = obj.position;
                p.y = shelfTopWorld + pivotToBottom;
                obj.position = p;
            }
            placed++;
        }

        Debug.Log($"[NEUROSHELF] Расставлено товаров: {placed}" +
                  (missing > 0 ? $", не найдено: {missing}" : "") +
                  "\nУровень глаз (1.60): VOLTA, FERRO, PURA — дорогие позиции" +
                  "\nСредний (1.10): NUBO, ZEST, KRISP, OKTA, LUMEN" +
                  "\nНижний (0.60): AURA, MIRA, NORDA, GRANO — базовые товары");
    }

    // ================== 4. СТАТИСТИКА ==================
    [MenuItem("NEUROSHELF/4. Показать статистику Draw Calls")]
    public static void Stats()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var mats = new HashSet<Material>();
        var texs = new HashSet<Texture>();
        foreach (var r in renderers)
            foreach (var m in r.sharedMaterials)
                if (m != null)
                {
                    mats.Add(m);
                    if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                        texs.Add(m.GetTexture("_BaseMap"));
                }

        Debug.Log($"[NEUROSHELF] Рендереров: {renderers.Length} · " +
                  $"уникальных материалов: {mats.Count} · " +
                  $"уникальных цветовых текстур: {texs.Count}\n" +
                  "Без атласа каждая из 12 упаковок потребовала бы отдельного " +
                  "материала и отдельной текстуры.");
    }

    // ================== ИМПОРТ ТЕКСТУР ==================
    static Texture2D ImportTexture(string fileName, bool sRGB, bool isNormal)
    {
        string path = $"{TexDir}/{fileName}.png";
        if (!File.Exists(path)) return null;

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = isNormal ? TextureImporterType.NormalMap
                                            : TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;  // для атласа обязательно
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
