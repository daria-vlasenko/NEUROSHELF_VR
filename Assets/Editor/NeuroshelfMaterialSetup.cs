// ============================================================================
//  NEUROSHELF — ЛР2. Автоматическая сборка PBR-материала в Unity
//
//  Что делает: создаёт URP Lit материал и подключает к нему карты атласа
//  в правильные слоты — Base Map, Metallic/Smoothness, Normal Map.
//  Заодно выставляет корректные настройки импорта текстур
//  (sRGB для цвета, Linear для данных, тип Normal Map для карты рельефа).
//
//  КУДА ПОЛОЖИТЬ
//    Assets/Editor/NeuroshelfMaterialSetup.cs
//    Папка должна называться именно Editor — иначе Unity не соберёт проект.
//
//  КАК ЗАПУСТИТЬ
//    Верхнее меню Unity: NEUROSHELF -> Собрать материал атласа
//
//  ГДЕ ЛЕЖАТ ТЕКСТУРЫ
//    Assets/_Project/Art/Textures/  (см. константу TexDir ниже)
// ============================================================================

using UnityEditor;
using UnityEngine;
using System.IO;

public static class NeuroshelfMaterialSetup
{
    const string TexDir = "Assets/_Project/Art/Textures";
    const string MatDir = "Assets/_Project/Materials";
    const string MatName = "M_NEUROSHELF_Atlas";

    const string Albedo = "NEUROSHELF_Atlas_Albedo";
    const string MetSmooth = "NEUROSHELF_Atlas_MetallicSmoothness";
    const string Normal = "NEUROSHELF_Atlas_Normal";

    [MenuItem("NEUROSHELF/Собрать материал атласа")]
    public static void Build()
    {
        // --- 1. Настраиваем импорт каждой текстуры --------------------------
        // Цветовая карта читается как sRGB, карты данных — как линейные,
        // иначе шероховатость и металличность посчитаются неверно.
        var albedoTex = ImportTexture(Albedo, sRGB: true, isNormal: false);
        var maskTex = ImportTexture(MetSmooth, sRGB: false, isNormal: false);
        var normalTex = ImportTexture(Normal, sRGB: false, isNormal: true);

        if (albedoTex == null)
        {
            Debug.LogError($"[NEUROSHELF] Не нашла {Albedo}.png в {TexDir}. " +
                           "Положи туда файлы атласа и запусти ещё раз.");
            return;
        }

        // --- 2. Создаём материал на шейдере URP Lit -------------------------
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[NEUROSHELF] Шейдер URP Lit не найден. Проект точно на URP?");
            return;
        }

        Directory.CreateDirectory(MatDir);
        string matPath = $"{MatDir}/{MatName}.mat";

        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.shader = shader;

        // --- 3. Подключаем карты в слоты ------------------------------------
        mat.SetTexture("_BaseMap", albedoTex);
        mat.SetColor("_BaseColor", Color.white);

        if (maskTex != null)
        {
            // В URP металличность лежит в RGB, а гладкость — в альфе
            // одной и той же текстуры. Поэтому карты Metallic и Roughness
            // из методички объединены в один файл MetallicSmoothness.
            mat.SetTexture("_MetallicGlossMap", maskTex);
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // 0 = альфа карты металличности
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        if (normalTex != null)
        {
            mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = mat;
        EditorGUIUtility.PingObject(mat);

        Debug.Log($"[NEUROSHELF] Материал готов: {matPath}\n" +
                  $"  Base Map: {(albedoTex ? "подключена" : "НЕТ")}\n" +
                  $"  Metallic/Smoothness: {(maskTex ? "подключена" : "НЕТ")}\n" +
                  $"  Normal Map: {(normalTex ? "подключена" : "НЕТ")}");
    }

    /// <summary>
    /// Находит текстуру по имени, выставляет ей корректные настройки импорта
    /// и возвращает готовый ассет.
    /// </summary>
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
            importer.wrapMode = TextureWrapMode.Clamp; // атлас: без повторения, иначе поплывут соседние ячейки
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [MenuItem("NEUROSHELF/Назначить материал выделенным объектам")]
    public static void ApplyToSelection()
    {
        string matPath = $"{MatDir}/{MatName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Debug.LogError("[NEUROSHELF] Материала нет. Сначала «Собрать материал атласа».");
            return;
        }

        int n = 0;
        foreach (var go in Selection.gameObjects)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                Undo.RecordObject(r, "Назначение материала атласа");
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
                n++;
            }
        }
        Debug.Log($"[NEUROSHELF] Материал назначен на {n} рендереров. " +
                  "Все они теперь используют один материал — значит один Draw Call на всю группу.");
    }

    [MenuItem("NEUROSHELF/Показать статистику Draw Calls")]
    public static void Stats()
    {
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var mats = new System.Collections.Generic.HashSet<Material>();
        foreach (var r in renderers)
            foreach (var m in r.sharedMaterials)
                if (m != null) mats.Add(m);

        Debug.Log($"[NEUROSHELF] Рендереров на сцене: {renderers.Length}\n" +
                  $"Уникальных материалов: {mats.Count}\n" +
                  "Чем меньше уникальных материалов при том же числе объектов, " +
                  "тем меньше Draw Calls. Это и есть смысл текстурного атласа.");
    }
}
