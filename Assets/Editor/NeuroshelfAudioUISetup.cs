// NEUROSHELF — пространственный звук и диегетические интерфейсы.
// Отдельный файл; остальные скрипты Editor не трогает.
//
// Меню NEUROSHELF → Звук и интерфейс:
//   10.1 Steam Audio: импорт пакета (ThirdParty/SteamAudio_win64_4.8.1.unitypackage) — один раз, затем перекомпиляция
//   10.0 Полный цикл         → 10.2 … 10.8 + протокол настройки (обычно нажимать только её)
//   10.2 Проект              → спатиалайзер Steam Audio, лимит голосов, трассировщик Steam = физика Unity, слой Acoustic
//   10.3 Звуковые файлы      → импорт WAV (моно для 3D, сжатие), поля музыки и якоря в условиях эксперимента
//   10.4 Микшер              → группы Music / Ambience / Stimulus / Interface, открытые параметры Vol_*
//   10.5 Источники           → музыка, якорь, витрина, вентиляция, касса, дверь, NPC; окклюзия стеллажами
//   10.6 Реверберация        → три Audio Reverb Zone с плавным переходом; RT60 по Сэбину из материалов Steam Audio
//   10.7 Интерфейсы          → EventSystem + XR UI Input Module, табло на холстах, кнопки, запястье, чек, табличка, настройки,
//                              разведение ролей рук (держащая предмет рука не нажимает интерфейс)
//   10.8 Тактильная связь    → HapticImpulsePlayer на обеих руках, журнал импульсов, метрика звукового якоря
//   10.9 Протокол настройки  → таблицы источников, зон, холстов, вибрации
//   10.10 Замер HRTF         → бинаурально vs панорама, 6 направлений, уровни по октавам, ITD → таблица дБ
//   10.11 Автотесты Play Mode (меню в Tests/Editor/NeuroshelfAudioTestRunner.cs)
//   10.12 Прогон в Play Mode + кадры
//   10.13 Запись Unity Recorder со звуком
//   10.F0 / 10.F1 Замер FPS до / после звука
//
// Всё складывается в  D:\cowork\3d\Отчёты\ЛР8\

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class NeuroshelfAudioUISetup
{
    // ===================== ПАРАМЕТРЫ =====================

    const string Menu = "NEUROSHELF/Звук и интерфейс/";
    const string AudioDir = "Assets/_Project/Art/Audio";
    const string MixerPath = "Assets/_Project/Settings/NS_StoreMixer.mixer";
    const string CondDir = "Assets/_Project/Configs/Conditions";
    const string PackagePath = "ThirdParty/SteamAudio_win64_4.8.1.unitypackage";
    const string LeftNearFarPrefab = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/Interactors/Left_NearFarInteractor.prefab";
    const string InputActions = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/XRI Default Input Actions.inputactions";
    public const int LayerAcoustic = 14;
    const int LayerUI = 5, LayerScenarioUI = 13;
    public const int MaxRealVoices = 16;

    public static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Отчёты", "ЛР8"));
    static string RecDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    // ----- раскладка звука в зале (м). Зал 12 × 8: x −6…6, z −4…4. Стеллажи: 01/02 — северный ряд (z 1.2…1.8),
    // 03/04 — южный ряд (z −1.8…−1.2), ряды лицом друг к другу. Продвигаемый — Shelf_03.
    static class P
    {
        // якорь — на лицевой кромке стеллажа под крышей, 5 см перед фасадом, на высоте 1.75 м (над уровнем глаз)
        public static readonly Dictionary<string, Vector3> AnchorMounts = new Dictionary<string, Vector3>
        {
            { "Shelf_01", new Vector3(-1.2f, 1.75f, 1.15f) }, { "Shelf_02", new Vector3(1.2f, 1.75f, 1.15f) },
            { "Shelf_03", new Vector3(-1.2f, 1.75f, -1.15f) }, { "Shelf_04", new Vector3(1.2f, 1.75f, -1.15f) },
        };
        public static Vector3 Music = new Vector3(0f, 2.9f, 0f);
        public static Vector3 Cooler = new Vector3(-5.65f, 0.25f, 1.20f);        // компрессор — внизу витрины
        public static Vector3 VentA = new Vector3(-3.0f, 2.95f, -2.2f), VentB = new Vector3(2.6f, 2.95f, 2.2f);
        public static Vector3 Checkout = new Vector3(4.5f, 1.15f, -2.0f);         // сканер на прилавке
        public static Vector3 Door = new Vector3(4.8f, 1.2f, 2.2f);               // проём двери тамбура
        public static Vector3 SettingsPos = new Vector3(5.97f, 1.45f, -1.00f);    // восточная стена, слева от главного табло
        public static Vector3 SignPos = new Vector3(3.66f, 1.45f, 3.10f);         // перила тамбура, видно из точки появления
    }

    // ===================== МЕНЮ =====================

    [MenuItem(Menu + "10.1 Steam Audio: импорт пакета (один раз)", priority = 601)]
    static void ImportSteamAudio()
    {
        var full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", PackagePath));
        if (!File.Exists(full)) { Debug.LogError("[NEUROSHELF] Нет пакета " + full); return; }
        AssetDatabase.importPackageCompleted += n => Debug.Log("[NEUROSHELF] 10.1 Пакет импортирован: " + n);
        AssetDatabase.importPackageFailed += (n, e) => Debug.LogError("[NEUROSHELF] 10.1 Импорт не удался: " + e);
        AssetDatabase.ImportPackage(full, false);
    }

    [MenuItem(Menu + "10.0 Полный цикл (проект → файлы → микшер → источники → реверберация → интерфейсы → вибрация → протокол)", priority = 600)]
    public static void All()
    {
        ProjectSettings(); Clips(); Mixer(); Sources(); Reverb(); Interfaces(); Haptics();
        Save();
        Protocol();
        Debug.Log("[NEUROSHELF] Звук и интерфейс настроены. Дальше: 10.10 (HRTF), 10.11 (автотесты), 10.12 (прогон с кадрами).");
    }
    [MenuItem(Menu + "10.2 Проект: спатиалайзер, голоса, Steam Audio", priority = 602)] static void M2() { ProjectSettings(); Save(); }
    [MenuItem(Menu + "10.3 Звуковые файлы и условия", priority = 603)] static void M3() { Clips(); Save(); }
    [MenuItem(Menu + "10.4 Микшер: группы и открытые параметры", priority = 604)] static void M4() { Mixer(); Save(); }
    [MenuItem(Menu + "10.5 Источники звука", priority = 605)] static void M5() { Sources(); Save(); }
    [MenuItem(Menu + "10.6 Зоны реверберации", priority = 606)] static void M6() { Reverb(); Save(); }
    [MenuItem(Menu + "10.7 Интерфейсы (холсты, кнопки, роли рук)", priority = 607)] static void M7() { Interfaces(); Save(); }
    [MenuItem(Menu + "10.8 Тактильная связь и метрика якоря", priority = 608)] static void M8() { Haptics(); Save(); }
    [MenuItem(Menu + "10.9 Протокол настройки", priority = 609)] static void M9() => Protocol();
    [MenuItem(Menu + "10.10 Замер HRTF: бинаурально против панорамы", priority = 610)] static void M10() => HrtfProbe.Run(OutDir);
    [MenuItem(Menu + "10.12 Прогон в Play Mode + кадры (~90 с)", priority = 622)] static void M12() => StartPlay("check");
    [MenuItem(Menu + "10.13 Запись Unity Recorder со звуком (~60 с)", priority = 623)] static void M13() => StartPlay("record");
    [MenuItem(Menu + "10.F0 Замер FPS «до звука» (13 с)", priority = 690)] static void F0() => StartPlay("fps:60_FPS_до_звука.txt");
    [MenuItem(Menu + "10.F1 Замер FPS «со звуком» (13 с)", priority = 691)] static void F1() => StartPlay("fps:61_FPS_со_звуком.txt");
    // Контрольная пара в одной сессии редактора: те же условия, но все AudioSource выключены.
    // Замеры «до» и «после» в разные часы отличаются фоном редактора сильнее, чем стоит звук.
    [MenuItem(Menu + "10.F2 Замер FPS: звук выключен в той же сцене (13 с)", priority = 692)] static void F2() => StartPlay("fps0:62_FPS_звук_выключен.txt");
    [MenuItem(Menu + "10.F3 Замер FPS: звук включён, повтор (13 с)", priority = 693)] static void F3() => StartPlay("fps:63_FPS_звук_включён_повтор.txt");

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

    static Transform Root() => Child(null, "Audio");
    static Transform UIRoot() => Child(null, "Interface");
    static XROrigin Origin() => Object.FindFirstObjectByType<XROrigin>();
    static ExperimentConditionSO Cond(string n) => AssetDatabase.LoadAssetAtPath<ExperimentConditionSO>($"{CondDir}/{n}.asset");
    static AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioDir}/{n}.wav");
    static AudioMixer LoadMixer() => AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);

    static Bounds Bounds(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);

    static void SetField(object o, string name, object value)
    {
        var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f == null) { Debug.LogWarning($"[NEUROSHELF] нет поля {o.GetType().Name}.{name}"); return; }
        f.SetValue(o, f.FieldType.IsEnum && value is string s ? Enum.Parse(f.FieldType, s) : value);
    }

    static object GetField(object o, string name) =>
        o?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);

    // ===================== 10.2 ПРОЕКТ =====================

    static void ProjectSettings()
    {
        // спатиалайзер и декодер амбисоники — Project Settings → Audio
        var am = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
        void S(string p, string v) { var pr = am.FindProperty(p); if (pr != null) pr.stringValue = v; else Debug.LogWarning("[NEUROSHELF] нет " + p); }
        void I(string p, int v) { var pr = am.FindProperty(p); if (pr != null) pr.intValue = v; else Debug.LogWarning("[NEUROSHELF] нет " + p); }
        S("m_SpatializerPlugin", "Steam Audio Spatializer");
        S("m_AmbisonicDecoderPlugin", "Steam Audio Ambisonics");
        I("m_RealVoiceCount", MaxRealVoices);      // одновременно звучащих голосов; лишние уходят в виртуальные по приоритету
        I("m_VirtualVoiceCount", 128);
        I("m_SampleRate", 48000);
        am.ApplyModifiedProperties();
        AudioSettings.SetSpatializerPluginName("Steam Audio Spatializer");

        // слой Acoustic (14): упрощённые коробки стеллажей и прилавка для окклюзии; физически ни с чем не сталкивается
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var el = tm.FindProperty("layers").GetArrayElementAtIndex(LayerAcoustic);
        if (string.IsNullOrEmpty(el.stringValue)) el.stringValue = "Acoustic";
        tm.ApplyModifiedProperties();
        for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(LayerAcoustic, i, true);

        // Steam Audio: трассировка лучей окклюзии через физику Unity (SceneType = Unity) по слою Acoustic.
        // Экспорт сцены в формат Steam Audio не нужен: стеллажи не двигаются, а коробки-заглушки — обычные коллайдеры.
        var st = FindType("SteamAudio.SteamAudioSettings");
        if (st == null) { Debug.LogError("[NEUROSHELF] Steam Audio не импортирован: сначала меню 10.1"); return; }
        var settings = st.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static).GetValue(null) as ScriptableObject;
        SetField(settings, "sceneType", "Custom");                 // в инспекторе называется «Unity»
        SetField(settings, "layerMask", (LayerMask)(1 << LayerAcoustic));
        SetField(settings, "maxOcclusionSamples", 16);
        SetField(settings, "hrtfNormalizationType", "None");
        SetField(settings, "simulationUpdateInterval", 0.1f);
        EditorUtility.SetDirty(settings);

        // лучи рук и телепорта не должны упираться в акустические коробки
        foreach (var c in Object.FindObjectsByType<CurveInteractionCaster>(FindObjectsSortMode.None)) { c.raycastMask &= ~(1 << LayerAcoustic); EditorUtility.SetDirty(c); }
        foreach (var r in Object.FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None)) { r.raycastMask &= ~(1 << LayerAcoustic); EditorUtility.SetDirty(r); }
        Debug.Log($"[NEUROSHELF] 10.2 Спатиалайзер Steam Audio, голосов {MaxRealVoices}, трассировщик = физика Unity (слой Acoustic {LayerAcoustic})");
    }

    // ===================== 10.3 ФАЙЛЫ И УСЛОВИЯ =====================

    static void Clips()
    {
        foreach (var path in Directory.GetFiles(AudioDir, "*.wav"))
        {
            var p = path.Replace('\\', '/');
            var imp = AssetImporter.GetAtPath(p) as AudioImporter;
            if (imp == null) continue;
            var n = Path.GetFileNameWithoutExtension(p);
            bool music = n.StartsWith("mus_");
            bool loop = music || n.StartsWith("amb_") || n == "npc_murmur";
            imp.forceToMono = !music;                 // 3D-источник обрабатывается как моно: стерео не нужно спатиалайзеру
            imp.loadInBackground = music;
            var s = imp.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = loop || music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            s.quality = 0.7f;
            s.preloadAudioData = !music;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
        }

        // условия эксперимента: музыка — фактор условия, якорь — поле условия
        void Set(string n, bool music, string clip, float bpm, string anchor, string desc = null)
        {
            var c = Cond(n);
            if (c == null)
            {
                var src = Cond("Cond_A_Manipulated_VOLTA");
                if (src == null) return;
                c = Object.Instantiate(src); c.name = n; c.conditionName = n.Replace("Cond_", "");
                AssetDatabase.CreateAsset(c, $"{CondDir}/{n}.asset");
            }
            c.backgroundMusic = music; c.musicClip = music ? Clip(clip) : null; c.musicTempoBpm = music ? bpm : 0f;
            c.audioAnchorShelf = anchor; c.tierEffortHaptic = false;
            if (desc != null) c.description = desc;
            EditorUtility.SetDirty(c);
        }
        // A: медленная музыка 64 уд/мин (Milliman, 1982: медленный темп — медленнее движение и больше покупок), якорь у продвигаемого стеллажа
        Set("Cond_A_Manipulated_VOLTA", true, "mus_slow_64bpm", 64f, "Shelf_03");
        Set("Cond_A_Manipulated_PURA", true, "mus_slow_64bpm", 64f, "Shelf_03");
        Set("Cond_C_Neutral", false, null, 0f, "");
        // варианты для проверки гипотезы о якоре (между участниками): якорь отделён от остальных приёмов
        Set("Cond_A_AnchorShelf04", true, "mus_slow_64bpm", 64f, "Shelf_04",
            "Условие A, но звуковой якорь — у НЕпродвигаемого Shelf_04. Отделяет действие звука от толпы, света и выкладки на Shelf_03.");
        Set("Cond_A_NoAnchor", true, "mus_slow_64bpm", 64f, "",
            "Условие A без звукового якоря. Контроль к Cond_A_Manipulated_VOLTA и Cond_A_AnchorShelf04.");
        AssetDatabase.SaveAssets();
        Debug.Log("[NEUROSHELF] 10.3 Импорт WAV, поля музыки и якоря в условиях A / C + 2 варианта для гипотезы о якоре");
    }

    // ===================== 10.4 МИКШЕР =====================
    // У AudioMixer нет публичного API создания групп — используется внутренний AudioMixerController (рефлексия).
    // Результат проверяется публичным API: FindMatchingGroups и GetFloat по открытому параметру.

    static void Mixer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MixerPath));
        var ctrlType = FindType("UnityEditor.Audio.AudioMixerController");
        if (ctrlType == null) { Debug.LogError("[NEUROSHELF] нет UnityEditor.Audio.AudioMixerController"); return; }
        var all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        // микшер пересоздаётся с нуля: так набор групп и открытых параметров всегда ровно тот, что в таблице
        // (ссылки источников на группы заново выставляет 10.5)
        if (AssetDatabase.LoadAssetAtPath<Object>(MixerPath) != null) AssetDatabase.DeleteAsset(MixerPath);
        object ctrl = ctrlType.GetMethod("CreateMixerControllerAtPath", all).Invoke(null, new object[] { MixerPath });
        var master = ctrlType.GetProperty("masterGroup", all).GetValue(ctrl);
        var exposedProp = ctrlType.GetProperty("exposedParameters", all);
        var pathType = FindType("UnityEditor.Audio.AudioGroupParameterPath");

        foreach (StoreAudioMixer.Group g in Enum.GetValues(typeof(StoreAudioMixer.Group)))
        {
            var name = g.ToString();
            var mixerAsset = (AudioMixer)ctrl;
            var existing = mixerAsset.FindMatchingGroups("Master/" + name).FirstOrDefault(x => x.name == name);
            object grp = existing;
            if (grp == null)
            {
                grp = ctrlType.GetMethod("CreateNewGroup", all).Invoke(ctrl, new object[] { name, false });
                ctrlType.GetMethod("AddChildToParent", all).Invoke(ctrl, new[] { grp, master });
                try { ctrlType.GetMethod("AddGroupToCurrentView", all)?.Invoke(ctrl, new[] { grp }); } catch { }   // только отображение в окне Audio Mixer
            }
            var guid = grp.GetType().GetMethod("GetGUIDForVolume", all).Invoke(grp, null);
            // уже открыт?
            var arr = (Array)exposedProp.GetValue(ctrl);
            bool found = false;
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr.GetValue(i);
                if (GetField(e, "guid").Equals(guid)) { found = true; SetField(e, "name", StoreAudioMixer.Param(g)); arr.SetValue(e, i); }
            }
            if (!found)
            {
                var path = Activator.CreateInstance(pathType, grp, guid);
                ctrlType.GetMethod("AddExposedParameter", all).Invoke(ctrl, new[] { path });
                arr = (Array)exposedProp.GetValue(ctrl);
                for (int i = 0; i < arr.Length; i++)
                {
                    var e = arr.GetValue(i);
                    if (GetField(e, "guid").Equals(guid)) { SetField(e, "name", StoreAudioMixer.Param(g)); arr.SetValue(e, i); }
                }
            }
            exposedProp.SetValue(ctrl, arr);
        }
        EditorUtility.SetDirty((Object)ctrl);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(MixerPath);

        var mixer = LoadMixer();
        var bridgeGo = Child(Root(), "Mixer");
        var bridge = Ensure<StoreAudioMixer>(bridgeGo.gameObject);
        bridge.mixer = mixer;
        AudioMixerGroup G(string n) => mixer.FindMatchingGroups("Master/" + n).FirstOrDefault(x => x.name == n);
        bridge.music = G("Music"); bridge.ambience = G("Ambience"); bridge.stimulus = G("Stimulus"); bridge.iface = G("Interface");
        EditorUtility.SetDirty(bridge);
        var names = string.Join(", ", ((Array)exposedProp.GetValue(ctrl)).Cast<object>().Select(e => GetField(e, "name")));
        Debug.Log($"[NEUROSHELF] 10.4 Микшер {MixerPath}: группы {string.Join(", ", new[] { bridge.music, bridge.ambience, bridge.stimulus, bridge.iface }.Select(x => x != null ? x.name : "НЕТ"))}; открытые параметры: {names}");
    }

    // ===================== 10.5 ИСТОЧНИКИ =====================

    public class Spec
    {
        public string id, place, note; public StoreAudioSource.Role role; public StoreAudioMixer.Group group;
        public float blend, min, max, volume = 1f, reverbMix = 1f; public int priority = 128;
        public AudioRolloffMode rolloff; public AnimationCurve curve; public bool steam, binaural, occlusion, air, loop, playOnAwake;
        public string clip;
    }

    // Таблица источников — она же идёт в отчёт. Кривые: логарифмическая — точечные (закон обратных квадратов,
    // −6 дБ на удвоение расстояния); своя — гул витрины (плато у витрины, затем крутой спад до нуля к 5 м: гул
    // не должен расползаться по залу и маскировать якорь); линейная — широкий фон вентиляции (ровный «пол» шума).
    public static readonly AnimationCurve CoolerCurve = new AnimationCurve(
        new Keyframe(0f, 1f), new Keyframe(0.30f, 0.90f), new Keyframe(0.60f, 0.35f), new Keyframe(1f, 0f));

    public static List<Spec> Specs() => new List<Spec>
    {
        new Spec { id = "Music", role = StoreAudioSource.Role.Music, place = "потолок зала, все динамики (2D)", group = StoreAudioMixer.Group.Music,
            blend = 0f, rolloff = AudioRolloffMode.Linear, min = 1, max = 30, priority = 16, reverbMix = 0.3f, loop = true, volume = 0.45f,
            note = "2D: в зале музыка идёт из многих потолочных динамиков, направления у неё нет" },
        new Spec { id = "Anchor", role = StoreAudioSource.Role.Anchor, place = "кромка Shelf_03, 1.75 м (место по условию)", group = StoreAudioMixer.Group.Stimulus,
            blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 1f, max = 15f, priority = 0, reverbMix = 0.3f, steam = true, binaural = true, occlusion = true, air = true,
            clip = "stim_anchor_jingle", volume = 0.8f, note = "точечный, обратные квадраты; реверберация снижена — хвост не размывает направление" },
        new Spec { id = "Cooler", role = StoreAudioSource.Role.Cooler, place = "холодильная витрина, западная стена", group = StoreAudioMixer.Group.Ambience,
            blend = 0.75f, rolloff = AudioRolloffMode.Custom, curve = CoolerCurve, min = 1f, max = 5f, priority = 120, loop = true, playOnAwake = true,
            clip = "amb_cooler_hum", volume = 0.6f, note = "своя кривая: плато 1.5 м, к 5 м — ноль, гул остаётся у витрины" },
        new Spec { id = "VentA", role = StoreAudioSource.Role.Ventilation, place = "решётка потолка, юго-запад", group = StoreAudioMixer.Group.Ambience,
            blend = 0.6f, rolloff = AudioRolloffMode.Linear, min = 1f, max = 9f, priority = 140, loop = true, playOnAwake = true,
            clip = "amb_ventilation", volume = 0.35f, note = "линейная: широкий ровный фон" },
        new Spec { id = "VentB", role = StoreAudioSource.Role.Ventilation, place = "решётка потолка, северо-восток", group = StoreAudioMixer.Group.Ambience,
            blend = 0.6f, rolloff = AudioRolloffMode.Linear, min = 1f, max = 9f, priority = 140, loop = true, playOnAwake = true,
            clip = "amb_ventilation", volume = 0.35f, note = "линейная: широкий ровный фон" },
        new Spec { id = "Checkout", role = StoreAudioSource.Role.Checkout, place = "сканер на прилавке кассы", group = StoreAudioMixer.Group.Ambience,
            blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 0.8f, max = 12f, priority = 64, steam = true, binaural = true, occlusion = true, air = true,
            volume = 0.8f, note = "точечный: пикание и чек по каждой позиции" },
        new Spec { id = "Door", role = StoreAudioSource.Role.Door, place = "дверь тамбура (вход, ЛР7)", group = StoreAudioMixer.Group.Ambience,
            blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 1f, max = 10f, priority = 64, steam = true, binaural = true, air = true,
            volume = 0.8f, note = "точечный: дверь при открывании, колокольчик при входе в зону Entry" },
    };

    static void Configure(AudioSource a, Spec s, StoreAudioMixer bridge)
    {
        a.playOnAwake = s.playOnAwake; a.loop = s.loop;
        a.clip = s.clip != null ? Clip(s.clip) : null;
        a.volume = s.volume;
        a.spatialBlend = s.blend;
        a.rolloffMode = s.rolloff;
        a.minDistance = s.min; a.maxDistance = s.max;
        if (s.rolloff == AudioRolloffMode.Custom && s.curve != null) a.SetCustomCurve(AudioSourceCurveType.CustomRolloff, s.curve);
        a.dopplerLevel = 0f;                 // при телепорте допплер дал бы ложный скачок высоты
        a.spread = 0f;
        a.priority = s.priority;
        a.reverbZoneMix = s.reverbMix;
        a.spatialize = s.steam;
        a.spatializePostEffects = false;
        a.outputAudioMixerGroup = bridge != null ? bridge.For(s.group) : null;
        EditorUtility.SetDirty(a);
    }

    static void SteamSource(GameObject go, Spec s)
    {
        var t = FindType("SteamAudio.SteamAudioSource");
        if (t == null) { Debug.LogWarning("[NEUROSHELF] нет SteamAudioSource — меню 10.1"); return; }
        var c = go.GetComponent(t);
        if (c == null) c = Undo.AddComponent(go, t);
        SetField(c, "directBinaural", s.binaural);
        SetField(c, "interpolation", "Bilinear");      // широкополосный джингл: билинейная интерполяция HRTF без «ступенек»
        SetField(c, "distanceAttenuation", false);     // затухание по расстоянию — кривая AudioSource (таблица)
        SetField(c, "airAbsorption", s.air);
        SetField(c, "occlusion", s.occlusion);
        SetField(c, "occlusionInput", "SimulationDefined");
        SetField(c, "occlusionType", "Volumetric");
        SetField(c, "occlusionRadius", 0.25f);         // источник не точка: плавный переход «за стеллажом / в видимости»
        SetField(c, "occlusionSamples", 16);
        SetField(c, "transmission", false);
        SetField(c, "reflections", false);
        EditorUtility.SetDirty(c);
    }

    static StoreAudioSource Source(Transform parent, Spec s, Vector3 pos, StoreAudioMixer bridge)
    {
        var t = Child(parent, "Src_" + s.id);
        t.position = pos;
        var a = Ensure<AudioSource>(t.gameObject);
        Configure(a, s, bridge);
        if (s.steam) SteamSource(t.gameObject, s);
        var sa = Ensure<StoreAudioSource>(t.gameObject);
        sa.role = s.role; sa.sourceId = s.id; sa.place = s.place; sa.group = s.group; sa.curveNote = s.note;
        EditorUtility.SetDirty(sa);
        return sa;
    }

    static void Sources()
    {
        var root = Root();
        var bridge = Object.FindFirstObjectByType<StoreAudioMixer>();
        if (bridge == null) { Debug.LogError("[NEUROSHELF] Сначала 10.4 (микшер)"); return; }
        var specs = Specs().ToDictionary(s => s.id);

        // слушатель — только на камере XR Origin (бинауральный рендер считается от его позы)
        var cam = Origin()?.Camera;
        foreach (var l in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cam == null || l.gameObject != cam.gameObject) { Debug.LogWarning("[NEUROSHELF] лишний AudioListener на " + l.name + " — удалён"); Undo.DestroyObjectImmediate(l); }
        if (cam != null) Ensure<AudioListener>(cam.gameObject);

        var music = Source(root, specs["Music"], P.Music, bridge);
        var anchor = Source(root, specs["Anchor"], P.AnchorMounts["Shelf_03"], bridge);
        Source(root, specs["Cooler"], P.Cooler, bridge);
        Source(root, specs["VentA"], P.VentA, bridge);
        Source(root, specs["VentB"], P.VentB, bridge);
        var chk = Source(root, specs["Checkout"], P.Checkout, bridge);
        var ca = Ensure<CheckoutAudio>(chk.gameObject); ca.beep = Clip("chk_scanner_beep"); ca.receipt = Clip("chk_receipt_print"); EditorUtility.SetDirty(ca);
        var door = Source(root, specs["Door"], P.Door, bridge);
        var ea = Ensure<EntryAudio>(door.gameObject);
        ea.door = GameObject.Find("Scenario/Vestibule/EntryDoor")?.GetComponent<ProximityDoor>();
        ea.entryZone = Object.FindObjectsByType<ScenarioZone>(FindObjectsSortMode.None).FirstOrDefault(z => z.zoneId == "Entry");
        ea.doorOpen = Clip("env_door_open"); ea.chime = Clip("env_entry_chime");
        EditorUtility.SetDirty(ea);

        // точки крепления якоря на всех стеллажах: стеллаж выбирает условие (audioAnchorShelf)
        var mounts = Child(root, "AnchorMounts");
        var mountList = new List<Transform>();
        foreach (var kv in P.AnchorMounts)
        {
            var m = Child(mounts, "AnchorMount_" + kv.Key);
            m.position = kv.Value; m.rotation = Quaternion.identity;
            mountList.Add(m);
        }
        var dir = Ensure<StoreAudioDirector>(root.gameObject);
        dir.context = Object.FindFirstObjectByType<ExperimentContext>();
        dir.music = music.Source; dir.anchor = anchor.Source; dir.anchorMounts = mountList.ToArray();
        EditorUtility.SetDirty(dir);

        // NPC: шаги и бормотание — короткие, с малым радиусом, встроенная панорама Unity (без HRTF), низкий приоритет
        var steps = new[] { "npc_step_01", "npc_step_02", "npc_step_03", "npc_step_04" }.Select(Clip).Where(c => c != null).ToArray();
        int k = 0;
        foreach (var ag in Object.FindObjectsByType<ShopperAgent>(FindObjectsSortMode.None).OrderBy(a => a.name))
        {
            var st = Child(ag.transform, "Audio_Steps");
            st.localPosition = new Vector3(0f, 0.05f, 0f);
            var sa = Ensure<AudioSource>(st.gameObject);
            Configure(sa, new Spec { id = "NpcSteps", group = StoreAudioMixer.Group.Ambience, blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 0.5f, max = 6f, priority = 200, volume = 1f }, bridge);
            var meta = Ensure<StoreAudioSource>(st.gameObject);
            meta.role = StoreAudioSource.Role.NpcSteps; meta.sourceId = "Steps_" + ag.name; meta.place = "ступни " + ag.name; meta.group = StoreAudioMixer.Group.Ambience;
            meta.curveNote = "точечный, радиус 6 м, приоритет 200 — первым уходит в виртуальные голоса";
            var na = Ensure<NpcAudio>(st.gameObject);
            na.agent = ag.GetComponent<UnityEngine.AI.NavMeshAgent>(); na.steps = steps;
            na.murmur = null;
            if (k % 2 == 0)   // бормочут двое из четырёх: голосов меньше, «шум людей» всё равно есть
            {
                var mu = Child(ag.transform, "Audio_Murmur");
                mu.localPosition = new Vector3(0f, 1.6f, 0f);
                var ma = Ensure<AudioSource>(mu.gameObject);
                Configure(ma, new Spec { id = "NpcMurmur", group = StoreAudioMixer.Group.Ambience, blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 0.6f, max = 5f, priority = 210, loop = true, clip = "npc_murmur", volume = 0.35f }, bridge);
                var mm = Ensure<StoreAudioSource>(mu.gameObject);
                mm.role = StoreAudioSource.Role.NpcMurmur; mm.sourceId = "Murmur_" + ag.name; mm.place = "голова " + ag.name; mm.group = StoreAudioMixer.Group.Ambience;
                mm.curveNote = "точечный, радиус 5 м, приоритет 210";
                na.murmur = ma;
            }
            EditorUtility.SetDirty(na);
            k++;
        }

        Blockers();
        Debug.Log($"[NEUROSHELF] 10.5 Источники: {Object.FindObjectsByType<StoreAudioSource>(FindObjectsSortMode.None).Length}, слушатель на {cam?.name}");
    }

    // Акустические коробки: стеллажи (во всю высоту и глубину) и прилавок кассы на слое Acoustic.
    // Steam Audio пускает по ним лучи окклюзии; физика их не видит (слой ни с чем не сталкивается).
    static void Blockers()
    {
        var root = Child(Root(), "AcousticBlockers");
        foreach (var n in new[] { "Shelf_01", "Shelf_02", "Shelf_03", "Shelf_04", "Environment/Checkout" })
        {
            var g = GameObject.Find(n);
            if (g == null) continue;
            var b = Bounds(g);
            var t = Child(root, "Block_" + g.name);
            t.position = b.center; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
            t.gameObject.layer = LayerAcoustic;
            var bc = Ensure<BoxCollider>(t.gameObject);
            bc.size = b.size; bc.center = Vector3.zero; bc.isTrigger = false;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
    }

    // ===================== 10.6 РЕВЕРБЕРАЦИЯ =====================
    // Акустически это одно помещение — время реверберации у всех зон одно (по Сэбину для зала).
    // Зоны отличаются тем, что меняется от места к месту: уровнем и задержкой ранних отражений
    // (у стен и прилавка они сильнее и раньше) и долей поздней реверберации.

    public class Surface { public string name, material; public float area; public float[] alpha; }

    // коэффициенты поглощения — материалы из пакета Steam Audio 4.8.1 (низкие / средние / высокие частоты)
    public static List<Surface> HallSurfaces() => new List<Surface>
    {
        new Surface { name = "пол, кафель", material = "Ceramic", area = 96f, alpha = new[] { 0.01f, 0.01f, 0.02f } },
        new Surface { name = "потолок, штукатурка", material = "Plaster", area = 96f, alpha = new[] { 0.12f, 0.06f, 0.04f } },
        new Surface { name = "стены, штукатурка", material = "Plaster", area = 120f, alpha = new[] { 0.12f, 0.06f, 0.04f } },
        new Surface { name = "фасады стеллажей с товаром (4 × 2.4 × 1.8 × 2)", material = "Default", area = 34.6f, alpha = new[] { 0.10f, 0.20f, 0.30f } },
        new Surface { name = "прилавок, витрина (металл)", material = "Metal", area = 8f, alpha = new[] { 0.20f, 0.07f, 0.06f } },
    };

    public const float HallVolume = 12f * 8f * 3f;

    public static float Sabine(int band) => 0.161f * HallVolume / HallSurfaces().Sum(s => s.area * s.alpha[band]);

    public class ZoneSpec { public string id, purpose; public Vector3 pos; public float min, max; public int room, reflections; public float reflDelay, reverbDelay; public int reverb; }

    public static List<ZoneSpec> Zones() => new List<ZoneSpec>
    {
        new ZoneSpec { id = "Hall", purpose = "торговый зал, проходы между стеллажами", pos = new Vector3(-1.0f, 1.5f, 0f), min = 4.5f, max = 7.0f,
            room = -1000, reflections = -1100, reflDelay = 0.010f, reverb = 0, reverbDelay = 0.030f },
        new ZoneSpec { id = "Checkout", purpose = "прикассовая зона: прилавок и угол восточной и южной стен", pos = new Vector3(4.6f, 1.5f, -2.3f), min = 1.2f, max = 2.6f,
            room = -1100, reflections = -700, reflDelay = 0.008f, reverb = -200, reverbDelay = 0.025f },
        new ZoneSpec { id = "Entry", purpose = "тамбур входа: перила, дверь, северо-восточный угол", pos = new Vector3(4.8f, 1.5f, 3.1f), min = 1.0f, max = 2.2f,
            room = -900, reflections = -400, reflDelay = 0.005f, reverb = 100, reverbDelay = 0.020f },
    };

    static void Reverb()
    {
        var root = Child(Root(), "ReverbZones");
        float rtMid = Sabine(1), rtHigh = Sabine(2);
        foreach (var z in Zones())
        {
            var t = Child(root, "Reverb_" + z.id);
            t.position = z.pos;
            var r = Ensure<AudioReverbZone>(t.gameObject);
            r.minDistance = z.min; r.maxDistance = z.max;
            r.reverbPreset = AudioReverbPreset.User;
            r.decayTime = rtMid;
            r.decayHFRatio = Mathf.Clamp(rtHigh / rtMid, 0.1f, 2f);
            r.room = z.room; r.roomHF = -300; r.roomLF = 0;
            r.reflections = z.reflections; r.reflectionsDelay = z.reflDelay;
            r.reverb = z.reverb; r.reverbDelay = z.reverbDelay;
            r.HFReference = 5000f; r.LFReference = 250f;
            r.diffusion = 100f; r.density = 100f;
            EditorUtility.SetDirty(r);
        }
        Debug.Log(string.Format(ci, "[NEUROSHELF] 10.6 Зоны реверберации: 3, RT60 по Сэбину {0:0.00} / {1:0.00} / {2:0.00} с (низ/сред/верх)", Sabine(0), rtMid, rtHigh));
    }

    // ===================== 10.7 ИНТЕРФЕЙСЫ =====================

    static UIFeedback Feedback(GameObject canvas, StoreAudioMixer bridge)
    {
        // старый вариант держал источник на самом холсте — перенос источника двигал бы холст
        var steamT = FindType("SteamAudio.SteamAudioSource");
        if (steamT != null && canvas.GetComponent(steamT) != null) Undo.DestroyObjectImmediate(canvas.GetComponent(steamT));
        if (canvas.GetComponent<StoreAudioSource>() != null) Undo.DestroyObjectImmediate(canvas.GetComponent<StoreAudioSource>());
        if (canvas.GetComponent<AudioSource>() != null) Undo.DestroyObjectImmediate(canvas.GetComponent<AudioSource>());
        var owner = canvas;
        var child = canvas.transform.Find("UIAudio");
        if (child == null) { child = new GameObject("UIAudio").transform; Undo.RegisterCreatedObjectUndo(child.gameObject, "ui audio"); child.SetParent(canvas.transform, false); }
        child.localPosition = Vector3.zero; child.localRotation = Quaternion.identity; child.localScale = Vector3.one;
        canvas = child.gameObject;
        var a = Ensure<AudioSource>(canvas);
        Configure(a, new Spec { id = "UI", group = StoreAudioMixer.Group.Interface, blend = 1f, rolloff = AudioRolloffMode.Logarithmic, min = 0.3f, max = 6f, priority = 32, reverbMix = 0.5f, steam = true, binaural = true }, bridge);
        SteamSource(canvas, new Spec { binaural = true });
        var meta = Ensure<StoreAudioSource>(canvas);
        meta.role = StoreAudioSource.Role.Interface; meta.sourceId = "UI_" + (owner.name == "Canvas" ? owner.transform.parent.name : owner.name.Replace("Canvas_", "")); meta.place = "холст " + owner.transform.parent?.name; meta.group = StoreAudioMixer.Group.Interface;
        meta.curveNote = "точечный от холста: щелчок слышен со стороны панели";
        var f = Ensure<UIFeedback>(owner);
        f.source = a;
        f.hover = Clip("ui_hover"); f.press = Clip("ui_press"); f.deny = Clip("ui_deny");
        EditorUtility.SetDirty(f); EditorUtility.SetDirty(meta);
        return f;
    }

    static void HideOldButton(ScenarioButton b)
    {
        if (b.face != null) b.face.enabled = false;
        foreach (var c in b.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
        var col = b.GetComponent<BoxCollider>(); if (col != null) col.enabled = false;
        EditorUtility.SetDirty(b);
    }

    // кнопка ЛР7 на холсте: холст на месте старого куба, на 4 мм ближе к зрителю
    static XRCanvasButton CanvasFor(ScenarioButton b, StoreAudioMixer bridge)
    {
        var col = b.GetComponent<BoxCollider>();
        var size = col != null ? col.size : new Vector3(0.5f, 0.16f, 0.06f);
        var parent = b.transform.parent;
        var cv = UIKit.WorldCanvas(parent, "Canvas_" + b.name, new Vector2(size.x * 1000f, size.y * 1000f),
            b.transform.localPosition + new Vector3(0f, 0f, -size.z * 0.5f - 0.004f), Quaternion.identity, true);
        var view = UIKit.Button(cv.transform, b.buttonId, b.label, new Rect(0, 0, size.x * 1000f, size.y * 1000f), Mathf.RoundToInt(size.y * 1000f * 0.40f));
        view.feedback = Feedback(cv.gameObject, bridge);
        var link = Ensure<ScenarioCanvasButton>(cv.gameObject);
        link.logic = b; link.view = view;
        HideOldButton(b);
        EditorUtility.SetDirty(view); EditorUtility.SetDirty(link);
        return view;
    }

    static void Interfaces()
    {
        var bridge = Object.FindFirstObjectByType<StoreAudioMixer>();
        var cam = Origin()?.Camera;

        // ---- EventSystem + XR UI Input Module ----
        var es = Object.FindFirstObjectByType<EventSystem>();
        if (es == null) { es = new GameObject("EventSystem").AddComponent<EventSystem>(); Undo.RegisterCreatedObjectUndo(es.gameObject, "es"); }
        foreach (var m in es.GetComponents<BaseInputModule>()) if (!(m is XRUIInputModule)) Undo.DestroyObjectImmediate(m);
        var xui = Ensure<XRUIInputModule>(es.gameObject);
        xui.enableXRInput = true;
        EditorUtility.SetDirty(xui);

        // ---- левая рука: Near-Far только для интерфейса ----
        var leftCtl = GameObject.Find("Left Controller")?.transform;
        NearFarInteractor leftNF = leftCtl != null ? leftCtl.GetComponentsInChildren<NearFarInteractor>(true).FirstOrDefault() : null;
        if (leftCtl != null && leftNF == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeftNearFarPrefab);
            if (prefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, leftCtl);
                inst.name = "Left NearFar (UI)";
                Undo.RegisterCreatedObjectUndo(inst, "left nf");
                leftNF = inst.GetComponentInChildren<NearFarInteractor>(true);
            }
        }
        var rightNF = Object.FindFirstObjectByType<LocomotionRig>()?.rightNearFar;
        if (leftNF != null)
        {
            // левая рука остаётся полноценной (товары рукой и лучом, как в ЛР5); здесь только включается её UI-луч
            leftNF.enableUIInteraction = true;
            leftNF.blockUIOnInteractableSelection = true;
            EditorUtility.SetDirty(leftNF);
        }
        if (rightNF != null) { rightNF.enableUIInteraction = true; rightNF.blockUIOnInteractableSelection = true; EditorUtility.SetDirty(rightNF); }

        var ui = UIRoot();
        var arb = Ensure<HandRoleArbiter>(ui.gameObject);
        arb.productHand = rightNF; arb.uiHand = leftNF; arb.rig = Object.FindFirstObjectByType<LocomotionRig>();
        EditorUtility.SetDirty(arb);

        // ---- табло сценария: фон-панель под текстом, кнопки — на холстах ----
        var main = GameObject.Find("Scenario/MainBoard")?.transform;
        if (main != null)
        {
            var cv = main.Find("Canvas");
            if (cv != null)
            {
                var panel = UIKit.Panel(cv, "Panel", new Rect(0, 0, 1600, 900), UIKit.PanelDark); panel.transform.SetAsFirstSibling();
                var band = UIKit.Panel(cv, "Band", new Rect(0, 0, 1600, 10), UIKit.Accent); band.transform.SetSiblingIndex(1);
                var sep = UIKit.Panel(cv, "Separator", new Rect(40, 140, 1520, 3), new Color(1, 1, 1, 0.15f)); sep.transform.SetSiblingIndex(2);
            }
        }
        foreach (var b in Object.FindObjectsByType<ScenarioButton>(FindObjectsSortMode.None)) CanvasFor(b, bridge);
        var deb = GameObject.Find("Scenario/DebriefBoard/Canvas");
        if (deb != null) { var p = UIKit.Panel(deb.transform, "Panel", new Rect(0, 0, 2200, 1100), UIKit.PanelDark); p.transform.SetAsFirstSibling(); }
        var ent = GameObject.Find("Scenario/EntranceBoard/Canvas");
        if (ent != null) { var p = UIKit.Panel(ent.transform, "Panel", new Rect(0, 0, 800, 450), UIKit.PanelDark); p.transform.SetAsFirstSibling(); }

        // ---- наручное меню (левый контроллер) 120 × 90 мм ----
        if (leftCtl != null)
        {
            var wcv = UIKit.WorldCanvas(leftCtl, "WristMenu", new Vector2(120, 90), new Vector3(0f, 0.07f, -0.11f), Quaternion.Euler(60f, 0f, 0f), false, 8f);
            UIKit.Panel(wcv.transform, "Panel", new Rect(0, 0, 120, 90), UIKit.PanelDark);
            UIKit.Panel(wcv.transform, "Band", new Rect(0, 0, 120, 3), UIKit.Accent);
            var stage = UIKit.Label(wcv.transform, "Stage", new Rect(5, 5, 110, 14), 9, TextAnchor.MiddleLeft, FontStyle.Bold);
            var list = UIKit.Label(wcv.transform, "List", new Rect(5, 21, 110, 36), 7, TextAnchor.UpperLeft);
            var sum = UIKit.Label(wcv.transform, "Sum", new Rect(5, 58, 110, 20), 7, TextAnchor.UpperLeft);
            UIKit.Panel(wcv.transform, "BarBack", new Rect(8, 81, 104, 4), UIKit.Off);
            var bar = UIKit.Panel(wcv.transform, "Bar", new Rect(8, 81, 0, 4), UIKit.Ok);
            var wm = Ensure<WristMenu>(wcv.gameObject);
            wm.head = cam != null ? cam.transform : null; wm.group = wcv.GetComponent<CanvasGroup>();
            wm.stage = stage; wm.list = list; wm.sum = sum; wm.sumBar = bar;
            wm.card = Object.FindFirstObjectByType<ShoppingListCard>(); wm.basket = Object.FindFirstObjectByType<BasketCarry>();
            EditorUtility.SetDirty(wm);
        }

        // ---- чек на корзине 190 × 100 мм ----
        var basket = Object.FindFirstObjectByType<BasketCarry>();
        if (basket != null)
        {
            // шрифт 10 мм: с расстояния ~0.65 м до корзины высота прописной ≈ 7 мм ≈ 37′ (≈ 6 пикселей шлема класса Vive)
            var rcv = UIKit.WorldCanvas(basket.transform, "ReceiptCanvas", new Vector2(190, 100), new Vector3(0f, 0.27f, 0.17f), Quaternion.Euler(45f, 0f, 0f), false, 8f);
            UIKit.Panel(rcv.transform, "Panel", new Rect(0, 0, 190, 100), new Color(0.97f, 0.96f, 0.90f));
            var hdr = UIKit.Label(rcv.transform, "Header", new Rect(6, 4, 178, 13), 9, TextAnchor.UpperLeft, FontStyle.Bold, new Color(0.1f, 0.1f, 0.12f)); hdr.text = "ЧЕК КОРЗИНЫ";
            var lines = UIKit.Label(rcv.transform, "Lines", new Rect(6, 18, 178, 62), 10, TextAnchor.UpperLeft, FontStyle.Normal, new Color(0.1f, 0.1f, 0.12f));
            var total = UIKit.Label(rcv.transform, "Total", new Rect(6, 82, 178, 15), 11, TextAnchor.MiddleLeft, FontStyle.Normal, new Color(0.1f, 0.1f, 0.12f));
            var br = Ensure<BasketReceipt>(rcv.gameObject);
            br.basket = basket; br.lines = lines; br.total = total; br.card = Object.FindFirstObjectByType<ShoppingListCard>();
            EditorUtility.SetDirty(br);
        }

        // ---- табличка у входа 500 × 300 мм ----
        var sign = Child(ui, "SessionSign");
        sign.position = P.SignPos; sign.rotation = Quaternion.Euler(0f, 270f, 0f);
        var scv = UIKit.WorldCanvas(sign, "Canvas", new Vector2(500, 300), Vector3.zero, Quaternion.identity, false);
        UIKit.Panel(scv.transform, "Panel", new Rect(0, 0, 500, 300), UIKit.PanelDark);
        UIKit.Panel(scv.transform, "Band", new Rect(0, 0, 500, 8), UIKit.Accent);
        var sTitle = UIKit.Label(scv.transform, "Title", new Rect(20, 20, 460, 50), 24, TextAnchor.MiddleLeft, FontStyle.Bold);
        var sState = UIKit.Label(scv.transform, "State", new Rect(20, 95, 460, 110), 48, TextAnchor.MiddleLeft, FontStyle.Bold, new Color(0.49f, 1f, 0.6f));
        var sMeta = UIKit.Label(scv.transform, "Meta", new Rect(52, 235, 430, 45), 20, TextAnchor.MiddleLeft);
        var dot = UIKit.Panel(scv.transform, "RecDot", new Rect(20, 247, 20, 20), new Color(0.95f, 0.2f, 0.2f));
        var pm = sign.Find("PostMesh");
        if (pm == null)
        {
            var pr = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(pr.GetComponent<Collider>());
            pr.name = "PostMesh"; pr.transform.SetParent(sign, false);
            pm = pr.transform;
        }
        pm.localPosition = new Vector3(0f, -0.81f, 0.03f); pm.localScale = new Vector3(0.04f, 1.30f, 0.04f);
        pm.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Scenario/M_ScenarioBoard.mat");
        var ss = Ensure<SessionSign>(sign.gameObject);
        ss.title = sTitle; ss.state = sState; ss.meta = sMeta; ss.recDot = dot;
        EditorUtility.SetDirty(ss);

        // ---- панель настроек звука 600 × 420 мм ----
        var set = Child(ui, "AudioSettings");
        set.position = P.SettingsPos; set.rotation = Quaternion.Euler(0f, 90f, 0f);
        var acv = UIKit.WorldCanvas(set, "Canvas", new Vector2(600, 420), new Vector3(0f, 0f, -0.01f), Quaternion.identity, true);
        UIKit.Panel(acv.transform, "Panel", new Rect(0, 0, 600, 420), UIKit.PanelDark, true);   // фон — цель луча: клик мимо кнопок не проходит сквозь панель
        UIKit.Panel(acv.transform, "Band", new Rect(0, 0, 600, 8), UIKit.Accent);
        var at = UIKit.Label(acv.transform, "Title", new Rect(20, 15, 560, 45), 30, TextAnchor.MiddleLeft, FontStyle.Bold); at.text = "Звук — экспериментатор";
        var fb = Feedback(acv.gameObject, bridge);
        var panelC = Ensure<AudioSettingsPanel>(acv.gameObject);
        panelC.rows.Clear();
        int i = 0;
        foreach (StoreAudioMixer.Group g in Enum.GetValues(typeof(StoreAudioMixer.Group)))
        {
            float y = 75 + i * 68;
            var lbl = UIKit.Label(acv.transform, "Lbl_" + g, new Rect(20, y, 160, 60), 24, TextAnchor.MiddleLeft); lbl.text = g.ToString();
            var minus = UIKit.Button(acv.transform, g + "_minus", "−", new Rect(185, y, 60, 60), 36);
            UIKit.Panel(acv.transform, "BarBack_" + g, new Rect(255, y + 20, 200, 20), UIKit.Off);
            var bar = UIKit.Panel(acv.transform, "Bar_" + g, new Rect(255, y + 20, 200, 20), UIKit.Accent);
            var plus = UIKit.Button(acv.transform, g + "_plus", "+", new Rect(465, y, 60, 60), 36);
            var val = UIKit.Label(acv.transform, "Val_" + g, new Rect(528, y, 70, 60), 18, TextAnchor.MiddleLeft);
            minus.feedback = fb; plus.feedback = fb;
            panelC.rows.Add(new AudioSettingsPanel.Row { group = g, minus = minus, plus = plus, value = val, bar = bar });
            i++;
        }
        panelC.status = UIKit.Label(acv.transform, "Status", new Rect(20, 352, 560, 56), 16, TextAnchor.MiddleLeft);
        EditorUtility.SetDirty(panelC);
        if (set.Find("Back") == null)
        {
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(back.GetComponent<Collider>());
            back.name = "Back"; back.transform.SetParent(set, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.012f); back.transform.localScale = new Vector3(0.64f, 0.46f, 0.02f);
            back.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Materials/Scenario/M_ScenarioBoard.mat");
        }

        // все интерактивные холсты: камера событий — камера XR Origin
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.renderMode == RenderMode.WorldSpace && c.GetComponent<TrackedDeviceGraphicRaycaster>() != null && cam != null) { c.worldCamera = cam; EditorUtility.SetDirty(c); }

        Points();
        Debug.Log("[NEUROSHELF] 10.7 Интерфейсы: EventSystem + XR UI Input Module, кнопки на холстах, запястье, чек, табличка, настройки; левая рука — UI");
    }

    // точки съёмки для кадров отчёта
    static void Points()
    {
        var cams = Child(UIRoot(), "Cameras");
        void Look(string n, Vector3 pos, Vector3 at, float fov)
        {
            var t = Child(cams, n);
            t.position = pos; t.rotation = Quaternion.LookRotation(at - pos, Vector3.up);
            var sp = Ensure<ScenarioShotPoint>(t.gameObject); sp.fov = fov; EditorUtility.SetDirty(sp);
        }
        Look("Cam_Settings", P.SettingsPos + new Vector3(-0.9f, 0.05f, 0f), P.SettingsPos, 40f);
        Look("Cam_Sign", P.SignPos + new Vector3(1.0f, 0.10f, 0.15f), P.SignPos, 38f);
        Look("Cam_BoardUI", new Vector3(4.0f, 1.65f, 0.35f), new Vector3(5.97f, 1.55f, 0.35f), 52f);
        Look("Cam_Shelf03_Tags", new Vector3(-1.2f, 1.70f, 0.10f), new Vector3(-1.2f, 1.20f, -1.30f), 58f);
    }

    // ===================== 10.8 ВИБРАЦИЯ И МЕТРИКА =====================

    static void Haptics()
    {
        var actions = AssetDatabase.LoadAllAssetsAtPath(InputActions).OfType<InputActionReference>().ToList();
        HapticImpulsePlayer Player(Transform ctl, string actionName)
        {
            if (ctl == null) return null;
            var p = Ensure<HapticImpulsePlayer>(ctl.gameObject);
            var reference = actions.FirstOrDefault(a => a.action != null && $"{a.action.actionMap?.name}/{a.action.name}" == actionName);
            if (reference != null)
                p.hapticOutput = new UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.XRInputHapticImpulseProvider("Haptic", false,
                    UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics.XRInputHapticImpulseProvider.InputSourceMode.InputActionReference) { inputActionReference = reference };
            else Debug.LogWarning("[NEUROSHELF] нет действия " + actionName);
            EditorUtility.SetDirty(p);
            return p;
        }
        var ui = UIRoot();
        var h = Ensure<HapticFeedback>(ui.gameObject);
        h.left = Player(GameObject.Find("Left Controller")?.transform, "XRI Left/Haptic Device");
        h.right = Player(GameObject.Find("Right Controller")?.transform, "XRI Right/Haptic Device");
        EditorUtility.SetDirty(h);
        var ctl = GameObject.Find("Scenario/Controller");
        if (ctl != null) { var m = Ensure<SoundAnchorMetric>(ctl); EditorUtility.SetDirty(m); }
        Debug.Log($"[NEUROSHELF] 10.8 HapticImpulsePlayer: левая {(h.left != null ? "есть" : "НЕТ")}, правая {(h.right != null ? "есть" : "НЕТ")}; метрика якоря на контроллере сценария");
    }

    // ===================== 10.9 ПРОТОКОЛ =====================

    static string Pkg(Type t) => UnityEditor.PackageManager.PackageInfo.FindForAssembly(t.Assembly)?.version ?? "?";

    public static void Protocol()
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        void W(string s = "") => sb.AppendLine(s);
        W("NEUROSHELF — протокол настройки звука и интерфейсов " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        W("Unity " + Application.unityVersion + " · XR Interaction Toolkit " + Pkg(typeof(XRGrabInteractable)));
        W();

        W("1. ПРОЕКТ");
        var cfg = AudioSettings.GetConfiguration();
        W($"   Spatializer Plugin: «{AudioSettings.GetSpatializerPluginName()}»; доступные: {string.Join(", ", AudioSettings.GetSpatializerPluginNames())}");
        W($"   частота {cfg.sampleRate} Гц, буфер {cfg.dspBufferSize}, реальных голосов {cfg.numRealVoices}, виртуальных {cfg.numVirtualVoices}");
        var st = FindType("SteamAudio.SteamAudioSettings");
        if (st != null)
        {
            var s = st.GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            W($"   Steam Audio: сцена={GetField(s, "sceneType")} (в инспекторе «Unity»), слои окклюзии={(int)(LayerMask)GetField(s, "layerMask")} (Acoustic={LayerAcoustic}), макс. лучей окклюзии={GetField(s, "maxOcclusionSamples")}");
        }
        else W("   Steam Audio: НЕ ИМПОРТИРОВАН");
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cam = Origin()?.Camera;
        W($"   Audio Listener: {listeners.Length} шт. → {string.Join(", ", listeners.Select(l => HPath(l.transform)))}; камера XR Origin: {(cam != null ? HPath(cam.transform) : "НЕТ")}; на камере: {listeners.Any(l => cam != null && l.gameObject == cam.gameObject)}");
        W($"   XR Origin Camera Y Offset = {Origin()?.CameraYOffset:0.00} м (не менялся)");
        W();

        W("2. ИСТОЧНИКИ ЗВУКА");
        W("   источник            место                                          blend  кривая            мин–макс, м  группа     HRTF  окклюзия  reverbMix  приоритет");
        foreach (var a in Object.FindObjectsByType<StoreAudioSource>(FindObjectsSortMode.None).OrderBy(x => (int)x.role).ThenBy(x => x.sourceId))
        {
            var s = a.Source;
            W(string.Format(ci, "   {0,-19} {1,-46} {2,5:0.00}  {3,-17} {4,4:0.0}–{5,-5:0.0}  {6,-10} {7,-5} {8,-9} {9,9:0.00}  {10,9}",
                a.sourceId, Cut(a.place, 46), s.spatialBlend, a.CurveName, s.minDistance, s.maxDistance, s.outputAudioMixerGroup != null ? s.outputAudioMixerGroup.name : "—",
                a.Binaural ? "да" : "нет", a.OcclusionEnabled ? "да" : "нет", s.reverbZoneMix, s.priority));
        }
        W();

        W("3. МИКШЕР " + MixerPath);
        var mx = LoadMixer();
        if (mx != null)
            foreach (StoreAudioMixer.Group g in Enum.GetValues(typeof(StoreAudioMixer.Group)))
            {
                bool ok = mx.GetFloat(StoreAudioMixer.Param(g), out var v);
                W($"   {g,-10} группа {(mx.FindMatchingGroups("Master/" + g).Any() ? "есть" : "НЕТ")}, открытый параметр {StoreAudioMixer.Param(g)}: {(ok ? v.ToString("0.0", ci) + " дБ" : "НЕ НАЙДЕН")}");
            }
        W();

        W("4. РЕВЕРБЕРАЦИЯ: время по Сэбину T = 0.161·V / Σ(S·α), V = 12 × 8 × 3 = 288 м³");
        W("   поверхность                                        материал Steam Audio   S, м²   α низ/сред/верх");
        foreach (var s in HallSurfaces()) W(string.Format(ci, "   {0,-50} {1,-21} {2,6:0.0}   {3:0.00} / {4:0.00} / {5:0.00}", s.name, s.material, s.area, s.alpha[0], s.alpha[1], s.alpha[2]));
        W(string.Format(ci, "   T60 = {0:0.00} / {1:0.00} / {2:0.00} с (низ / сред / верх)", Sabine(0), Sabine(1), Sabine(2)));
        W("   зона       пресет  внутр.–внешн. радиус, м  decay, с  HF ratio  room, мБ  ранние отр., мБ / задержка, мс  назначение");
        foreach (var z in Object.FindObjectsByType<AudioReverbZone>(FindObjectsSortMode.None).OrderBy(z => z.name))
        {
            var zs = Zones().FirstOrDefault(x => "Reverb_" + x.id == z.name);
            W(string.Format(ci, "   {0,-10} {1,-6}  {2,4:0.0}–{3,-4:0.0}                {4,6:0.00}  {5,8:0.00}  {6,8}  {7,6} / {8,4:0}                   {9}",
                z.name.Replace("Reverb_", ""), z.reverbPreset, z.minDistance, z.maxDistance, z.decayTime, z.decayHFRatio, z.room, z.reflections, z.reflectionsDelay * 1000f, zs?.purpose));
        }
        W();

        W("5. ХОЛСТЫ (1 пиксель макета = 1 мм)");
        W("   холст                                          размер, см     масштаб  луч XRI (Tracked Device Graphic Raycaster)  камера событий");
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.renderMode == RenderMode.WorldSpace).OrderBy(c => HPath(c.transform)))
        {
            var rt = (RectTransform)c.transform;
            W(string.Format(ci, "   {0,-46} {1,5:0.0} × {2,-5:0.0}  {3:0.000}    {4,-44} {5}", Cut(HPath(c.transform), 46), rt.sizeDelta.x / 10f, rt.sizeDelta.y / 10f, rt.lossyScale.x,
                c.GetComponent<TrackedDeviceGraphicRaycaster>() != null ? "да" : "нет (пассивный)", c.worldCamera != null ? c.worldCamera.name : "—"));
        }
        var es = Object.FindFirstObjectByType<EventSystem>();
        W($"   EventSystem: {(es != null ? HPath(es.transform) : "НЕТ")}; модули: {(es != null ? string.Join(", ", es.GetComponents<BaseInputModule>().Select(m => m.GetType().Name)) : "")}");
        W();

        W("6. РОЛИ РУК");
        var arb = Object.FindFirstObjectByType<HandRoleArbiter>();
        foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
            W($"   {nf.handedness,-5} {HPath(nf.transform),-60} слои взаимодействия={nf.interactionLayers.value}  UI={nf.enableUIInteraction}  блок UI при выборе={nf.blockUIOnInteractableSelection}  ближний={nf.enableNearCasting}");
        W($"   HandRoleArbiter: товарная рука {arb?.productHand?.name}, рука интерфейса {arb?.uiHand?.name}");
        W();

        W("7. ВИБРАЦИЯ (HapticImpulsePlayer XRI " + Pkg(typeof(HapticImpulsePlayer)) + ")");
        var h = Object.FindFirstObjectByType<HapticFeedback>();
        if (h != null)
        {
            W($"   игрок левой: {(h.left != null ? HPath(h.left.transform) : "НЕТ")}, правой: {(h.right != null ? HPath(h.right.transform) : "НЕТ")}");
            W("   событие                                    амплитуда   длительность, мс   импульсов / пауза, мс");
            void R(string n, HapticFeedback.Spec s) => W(string.Format(ci, "   {0,-42} {1,9:0.00}   {2,14:0}   {3} / {4:0}", n, s.amplitude, s.duration * 1000f, s.pulses, s.gap * 1000f));
            R("наведение луча на элемент интерфейса", h.uiHover);
            R("нажатие кнопки", h.uiPress);
            R("нажатие недоступной кнопки (отказ)", h.uiDeny);
            W(string.Format(ci, "   {0,-42} {1:0.00}–{2:0.00}   {3,14:0}   1 / 0   (масса {4:0.00}–{5:0.00} кг)", "взятие товара", h.grabAmplitude.x, h.grabAmplitude.y, h.grabDuration * 1000f, h.grabMassRange.x, h.grabMassRange.y));
            R("товар лёг в корзину", h.basketPlace);
            W(string.Format(ci, "   {0,-42} как взятие, +{1:0} мс — ВЫКЛ. по умолчанию (поле условия tierEffortHaptic)", "метка усилия: нижний ярус 0.60 м", h.tierEffortExtra * 1000f));
        }
        W();

        W("8. УСЛОВИЯ ЭКСПЕРИМЕНТА: звуковые поля");
        foreach (var n in new[] { "Cond_A_Manipulated_VOLTA", "Cond_A_Manipulated_PURA", "Cond_C_Neutral", "Cond_A_AnchorShelf04", "Cond_A_NoAnchor" })
        {
            var c = Cond(n);
            W(c == null ? $"   {n}: НЕТ" : string.Format(ci, "   {0,-26} музыка={1,-5} трек={2,-16} темп={3,3:0} уд/мин  якорь={4,-9} метка усилия={5}",
                n, c.backgroundMusic, c.musicClip != null ? c.musicClip.name : "—", c.musicTempoBpm, c.audioAnchorShelf == "" ? "нет" : c.audioAnchorShelf, c.tierEffortHaptic));
        }
        var path = Path.Combine(OutDir, "00_Протокол_настройки_звука_и_интерфейсов.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] Протокол: " + path + "\n" + sb);
    }

    static string Cut(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…";
    static string HPath(Transform t) => t.parent == null ? t.name : HPath(t.parent) + "/" + t.name;

    // ===================== PLAY MODE =====================

    const string ModeKey = "ns_audio_mode";
    static object recorder;

    static NeuroshelfAudioUISetup()
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
        if (mode.StartsWith("fps:") || mode.StartsWith("fps0:"))
        {
            SessionState.EraseString(ModeKey);
            bool mute = mode.StartsWith("fps0:");
            mode = "fps:" + mode.Substring(mode.IndexOf(':') + 1);
            if (mute) foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) a.enabled = false;
            var xr = Origin();
            if (xr != null) xr.transform.SetPositionAndRotation(new Vector3(-0.6f, 0f, 0.1f), Quaternion.Euler(0f, 200f, 0f));
            var p = new GameObject("~FpsProbe").AddComponent<FpsProbe>();
            p.reportFolder = "ЛР8"; p.fileName = mode.Substring(4);
            new GameObject("~VoiceCounter").AddComponent<VoiceCounter>().file = Path.Combine(OutDir, Path.GetFileNameWithoutExtension(p.fileName) + "_голоса.txt");
            return;
        }
        AudioUIDemoDriver.OutDir = OutDir;
        var d = new GameObject("~NS_AudioDemo").AddComponent<AudioUIDemoDriver>();
        d.mode = mode;
        if (mode == "record") recorder = StartRecorder();
        Debug.Log("[NEUROSHELF] Прогон звука и интерфейсов: " + mode);
    }

    static void Watch()
    {
        if (!EditorApplication.isPlaying) return;
        var mode = SessionState.GetString(ModeKey, "");
        if (mode == "" || !AudioUIDemoDriver.Finished) return;
        SessionState.EraseString(ModeKey);
        if (recorder != null) { ((UnityEditor.Recorder.RecorderController)recorder).StopRecording(); recorder = null; }
        if (mode == "check")
        {
            var path = Path.Combine(OutDir, "41_Прогон_звука_и_интерфейсов_протокол.txt");
            File.WriteAllText(path, "NEUROSHELF — прогон звука и интерфейсов в Play Mode " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "\n\n" + AudioUIDemoDriver.Report, new UTF8Encoding(true));
            Debug.Log("[NEUROSHELF] 10.12 Протокол: " + path);
        }
        else Debug.Log("[NEUROSHELF] 10.13 Запись завершена → " + RecDir);
        EditorApplication.isPlaying = false;
    }

    static object StartRecorder()
    {
        Directory.CreateDirectory(RecDir);
        var cs = ScriptableObject.CreateInstance<UnityEditor.Recorder.RecorderControllerSettings>();
        var m = ScriptableObject.CreateInstance<UnityEditor.Recorder.MovieRecorderSettings>();
        m.name = "NEUROSHELF audio";
        m.Enabled = true;
        m.ImageInputSettings = new UnityEditor.Recorder.Input.CameraInputSettings
        {
            Source = UnityEditor.Recorder.ImageSource.MainCamera,
            OutputWidth = 1280,
            OutputHeight = 720,
        };
        m.AudioInputSettings.PreserveAudio = true;      // звук пишется вместе с картинкой (AudioRenderer)
        m.OutputFile = Path.Combine(RecDir, "ns_audio_ui").Replace('\\', '/');
        cs.AddRecorderSettings(m);
        cs.SetRecordModeToManual();
        cs.FrameRatePlayback = UnityEditor.Recorder.FrameRatePlayback.Constant;
        cs.FrameRate = 30f;
        cs.CapFrameRate = true;
        var rc = new UnityEditor.Recorder.RecorderController(cs);
        rc.PrepareRecording();
        rc.StartRecording();
        Debug.Log("[NEUROSHELF] Recorder со звуком: " + m.OutputFile + ".mp4");
        return rc;
    }
}

// Счётчик звучащих голосов во время замера FPS (сколько AudioSource играет и сколько из них настоящих).
public class VoiceCounter : MonoBehaviour
{
    public string file;
    int frames, maxPlaying, sumPlaying, maxSteam;
    float t;

    void Update()
    {
        t += Time.unscaledDeltaTime;
        if (t < 3f || t > 13f) { if (t > 13f && frames > 0) Write(); return; }
        var all = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        int playing = all.Count(a => a.isPlaying), steam = all.Count(a => a.isPlaying && a.spatialize);
        frames++; sumPlaying += playing; maxPlaying = Mathf.Max(maxPlaying, playing); maxSteam = Mathf.Max(maxSteam, steam);
    }

    void Write()
    {
        var cfg = AudioSettings.GetConfiguration();
        File.WriteAllText(file, string.Format(CultureInfo.InvariantCulture,
            "Звучащих AudioSource: в среднем {0:0.0}, максимум {1}; из них через спатиалайзер Steam Audio — максимум {2}.\nЛимит реальных голосов (Project Settings → Audio): {3}; DSP-буфер {4}, {5} Гц.\n",
            (float)sumPlaying / frames, maxPlaying, maxSteam, cfg.numRealVoices, cfg.dspBufferSize, cfg.sampleRate), new UTF8Encoding(true));
        frames = 0;
        enabled = false;
    }
}

// ===================== 10.10 ЗАМЕР HRTF =====================
// HRTF нельзя услышать без шлема и нельзя снять с выхода редактора. Поэтому измеряется сама обработка:
// белый шум (2 с, 48 кГц) пропускается через бинауральный эффект Steam Audio (тот же phonon.dll, что работает
// в спатиалайзере) и, для сравнения, через панораму Steam Audio (амплитудное панорамирование на стерео — тот же
// принцип, что у встроенного 3D-панорамирования Unity: громкость каналов зависит только от бокового угла).
// Для каждого направления считаются уровни левого и правого канала (широкополосно и по октавам),
// межушная разница уровней (ILD) и межушная задержка (ITD) — взаимной корреляцией каналов
// (поле peakDelays Steam Audio 4.8.1 в этом режиме возвращало нули, поэтому задержка считается только корреляцией).
// Ожидание: у панорамы «спереди» и «сзади» дают одинаковые каналы (разница 0 дБ во всех полосах),
// у HRTF — разный спектр (ушная раковина затеняет звук сзади на высоких частотах).
public static class HrtfProbe
{
    const string Lib = "phonon";
    const int SR = 48000, Frame = 1024;
    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ContextSettings { public uint version; public IntPtr log, alloc, free; public int simd; public int flags; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct AudioCfg { public int samplingRate, frameSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct HRTFSettings { public int type; public IntPtr sofaFileName, sofaData; public int sofaDataSize; public float volume; public int normType; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct V3 { public float x, y, z; public V3(float a, float b, float c) { x = a; y = b; z = c; } }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Buf { public int numChannels, numSamples; public IntPtr data; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct BinSettings { public IntPtr hrtf; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct BinParams { public V3 direction; public int interpolation; public float spatialBlend; public IntPtr hrtf; public IntPtr peakDelays; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct SpeakerLayout { public int type, numSpeakers; public IntPtr speakers; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct PanSettings { public SpeakerLayout layout; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct PanParams { public V3 direction; }

    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplContextCreate(ref ContextSettings s, out IntPtr ctx);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern void iplContextRelease(ref IntPtr ctx);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplHRTFCreate(IntPtr ctx, ref AudioCfg a, ref HRTFSettings s, out IntPtr hrtf);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern void iplHRTFRelease(ref IntPtr hrtf);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplAudioBufferAllocate(IntPtr ctx, int ch, int n, ref Buf b);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern void iplAudioBufferFree(IntPtr ctx, ref Buf b);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplBinauralEffectCreate(IntPtr ctx, ref AudioCfg a, ref BinSettings s, out IntPtr e);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplBinauralEffectApply(IntPtr e, ref BinParams p, ref Buf i, ref Buf o);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern void iplBinauralEffectRelease(ref IntPtr e);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplPanningEffectCreate(IntPtr ctx, ref AudioCfg a, ref PanSettings s, out IntPtr e);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern int iplPanningEffectApply(IntPtr e, ref PanParams p, ref Buf i, ref Buf o);
    [System.Runtime.InteropServices.DllImport(Lib)] static extern void iplPanningEffectRelease(ref IntPtr e);

    // направления в системе координат слушателя Steam Audio: x — вправо, y — вверх, −z — вперёд
    public static readonly (string name, V3 dir)[] Dirs =
    {
        ("спереди 0°", new V3(0, 0, -1)), ("сзади 180°", new V3(0, 0, 1)),
        ("слева 270°", new V3(-1, 0, 0)), ("справа 90°", new V3(1, 0, 0)),
        ("спереди-справа 30°", new V3(0.5f, 0, -0.8660254f)), ("сзади-справа 150°", new V3(0.5f, 0, 0.8660254f)),
    };
    public static readonly float[] Octaves = { 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public class Result
    {
        public string dir, method;
        public float L, R, ILD, ITDus, peakL = float.NaN, peakR = float.NaN;
        public float[] bandsL, bandsR;
    }

    public static List<Result> Results = new List<Result>();

    public static void Run(string outDir)
    {
        Results.Clear();
        IntPtr ctx = IntPtr.Zero, hrtf = IntPtr.Zero;
        var cs = new ContextSettings { version = (4u << 16) | (8u << 8) | 1u, simd = 3 };
        int e = iplContextCreate(ref cs, out ctx);
        if (e != 0) { Debug.LogError("[NEUROSHELF] 10.10 iplContextCreate = " + e); return; }
        var ac = new AudioCfg { samplingRate = SR, frameSize = Frame };
        var hs = new HRTFSettings { type = 0, volume = 1f, normType = 0 };
        e = iplHRTFCreate(ctx, ref ac, ref hs, out hrtf);
        if (e != 0) { Debug.LogError("[NEUROSHELF] 10.10 iplHRTFCreate = " + e); iplContextRelease(ref ctx); return; }

        var rng = new System.Random(2026);
        int n = SR * 2 + Frame * 4;
        var noise = new float[n];
        for (int i = 0; i < n; i++) noise[i] = (float)((rng.NextDouble() * 2 - 1) * 0.5);
        float inRms = Rms(noise, Frame * 4, n);

        try
        {
            foreach (var (name, dir) in Dirs)
            {
                Results.Add(Process(ctx, ac, hrtf, noise, name, dir, true, inRms));
                Results.Add(Process(ctx, ac, hrtf, noise, name, dir, false, inRms));
            }
        }
        catch (Exception ex) { Debug.LogException(ex); }
        finally { iplHRTFRelease(ref hrtf); iplContextRelease(ref ctx); }
        Write(outDir);
    }

    static Result Process(IntPtr ctx, AudioCfg ac, IntPtr hrtf, float[] x, string name, V3 dir, bool binaural, float inRms)
    {
        IntPtr eff = IntPtr.Zero;
        var bin = new BinSettings { hrtf = hrtf };
        var pan = new PanSettings { layout = new SpeakerLayout { type = 1 } };   // IPL_SPEAKERLAYOUTTYPE_STEREO
        int err = binaural ? iplBinauralEffectCreate(ctx, ref ac, ref bin, out eff) : iplPanningEffectCreate(ctx, ref ac, ref pan, out eff);
        if (err != 0) throw new Exception((binaural ? "iplBinauralEffectCreate" : "iplPanningEffectCreate") + " = " + err);
        var inB = new Buf(); var outB = new Buf();
        iplAudioBufferAllocate(ctx, 1, Frame, ref inB);
        iplAudioBufferAllocate(ctx, 2, Frame, ref outB);
        var peaks = new float[2] { float.NaN, float.NaN };
        var h = System.Runtime.InteropServices.GCHandle.Alloc(peaks, System.Runtime.InteropServices.GCHandleType.Pinned);
        int n = x.Length / Frame * Frame;
        var L = new float[n]; var R = new float[n];
        var tmp = new float[Frame];
        try
        {
            IntPtr in0 = System.Runtime.InteropServices.Marshal.ReadIntPtr(inB.data, 0);
            IntPtr o0 = System.Runtime.InteropServices.Marshal.ReadIntPtr(outB.data, 0);
            IntPtr o1 = System.Runtime.InteropServices.Marshal.ReadIntPtr(outB.data, IntPtr.Size);
            for (int f = 0; f < n; f += Frame)
            {
                System.Runtime.InteropServices.Marshal.Copy(x, f, in0, Frame);
                if (binaural)
                {
                    var p = new BinParams { direction = dir, interpolation = 1, spatialBlend = 1f, hrtf = hrtf, peakDelays = IntPtr.Zero };
                    iplBinauralEffectApply(eff, ref p, ref inB, ref outB);
                }
                else
                {
                    var p = new PanParams { direction = dir };
                    iplPanningEffectApply(eff, ref p, ref inB, ref outB);
                }
                System.Runtime.InteropServices.Marshal.Copy(o0, tmp, 0, Frame); Array.Copy(tmp, 0, L, f, Frame);
                System.Runtime.InteropServices.Marshal.Copy(o1, tmp, 0, Frame); Array.Copy(tmp, 0, R, f, Frame);
            }
        }
        finally
        {
            h.Free();
            iplAudioBufferFree(ctx, ref inB); iplAudioBufferFree(ctx, ref outB);
            if (binaural) iplBinauralEffectRelease(ref eff); else iplPanningEffectRelease(ref eff);
        }
        int s0 = Frame * 4;   // первые кадры — установление фильтра, в замер не входят
        var r = new Result
        {
            dir = name, method = binaural ? "HRTF (бинаурально)" : "панорама",
            L = Db(Rms(L, s0, n) / inRms), R = Db(Rms(R, s0, n) / inRms),
            bandsL = Bands(L, s0, n, inRms), bandsR = Bands(R, s0, n, inRms),
            ITDus = Itd(L, R, s0, n) * 1e6f,
        };
        r.ILD = r.L - r.R;
        if (binaural) { r.peakL = peaks[0]; r.peakR = peaks[1]; }
        return r;
    }

    static string D(float v) => v < -100f ? "−∞" : v.ToString("0.0", CultureInfo.InvariantCulture);

    static float Rms(float[] a, int s, int e) { double q = 0; for (int i = s; i < e; i++) q += a[i] * a[i]; return (float)Math.Sqrt(q / Math.Max(1, e - s)); }
    static float Db(float v) => 20f * Mathf.Log10(Mathf.Max(v, 1e-9f));

    // взаимная корреляция L и R в окне ±1 мс; ITD = t(лев) − t(прав): «+» — к правому уху звук приходит раньше
    static float Itd(float[] L, float[] R, int s, int e)
    {
        int maxLag = SR / 1000;
        double best = double.MinValue; int bl = 0;
        var c = new double[2 * maxLag + 1];
        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            double acc = 0;
            for (int i = s + maxLag; i < e - maxLag; i += 2) acc += L[i] * R[i - lag];
            c[lag + maxLag] = acc;
            if (acc > best) { best = acc; bl = lag; }
        }
        double frac = 0;
        int k = bl + maxLag;
        if (k > 0 && k < c.Length - 1) { double a = c[k - 1], b = c[k], d = c[k + 1]; double den = a - 2 * b + d; if (Math.Abs(den) > 1e-12) frac = 0.5 * (a - d) / den; }
        return (float)((bl + frac) / SR);
    }

    // уровни по октавам (дБ относительно того же шума на входе в той же полосе): Ханн 4096, усреднение мощности
    static float[] Bands(float[] y, int s, int e, float inRms)
    {
        const int N = 4096;
        var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
        var pow = new double[N / 2];
        int segs = 0;
        var re = new double[N]; var im = new double[N];
        for (int st = s; st + N <= e; st += N / 2)
        {
            for (int i = 0; i < N; i++) { re[i] = y[st + i] * win[i]; im[i] = 0; }
            Fft(re, im);
            for (int i = 0; i < N / 2; i++) pow[i] += re[i] * re[i] + im[i] * im[i];
            segs++;
        }
        // белый шум на входе: равная мощность на бин → опорная мощность полосы = число бинов × мощность одного бина
        double binRef = inRms * inRms * N * win.Sum(w => w * w) / N;
        var res = new float[Octaves.Length];
        for (int b = 0; b < Octaves.Length; b++)
        {
            double lo = Octaves[b] / Math.Sqrt(2), hi = Math.Min(Octaves[b] * Math.Sqrt(2), SR / 2.0 - 1);
            int i0 = (int)Math.Ceiling(lo * N / SR), i1 = (int)Math.Floor(hi * N / SR);
            double p = 0; for (int i = i0; i <= i1; i++) p += pow[i] / segs;
            double refP = binRef * (i1 - i0 + 1);
            res[b] = (float)(10 * Math.Log10(Math.Max(p, 1e-20) / refP));
        }
        return res;
    }

    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, cim = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * cim, ti = re[b] * cim + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti; re[a] += tr; im[a] += ti;
                    double nr = cr * wr - cim * wi; cim = cr * wi + cim * wr; cr = nr;
                }
            }
        }
    }

    public static Result Get(string dir, bool binaural) => Results.FirstOrDefault(r => r.dir == dir && r.method.StartsWith(binaural ? "HRTF" : "пан"));

    // спектральное различие двух направлений: среднеквадратичная разница уровней (среднее ушей) по полосам 1–16 кГц
    public static float SpectralDistance(Result a, Result b, out float[] diff)
    {
        diff = new float[Octaves.Length];
        double q = 0; int m = 0;
        for (int i = 0; i < Octaves.Length; i++)
        {
            float ea = 10 * Mathf.Log10((Mathf.Pow(10, a.bandsL[i] / 10) + Mathf.Pow(10, a.bandsR[i] / 10)) / 2);
            float eb = 10 * Mathf.Log10((Mathf.Pow(10, b.bandsL[i] / 10) + Mathf.Pow(10, b.bandsR[i] / 10)) / 2);
            diff[i] = ea - eb;
            if (Octaves[i] >= 1000) { q += diff[i] * diff[i]; m++; }
        }
        return (float)Math.Sqrt(q / Math.Max(1, m));
    }

    static void Write(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder();
        void W(string s = "") => sb.AppendLine(s);
        W("NEUROSHELF — замер HRTF без шлема: Steam Audio 4.8.1, бинауральный эффект против панорамы " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        W("Сигнал: белый шум 2 с, 48 кГц, кадр 1024, HRTF по умолчанию Steam Audio, интерполяция билинейная.");
        W("Уровни — дБ относительно того же шума на входе. ILD = L − R. ITD — взаимная корреляция каналов, «+» — правое ухо раньше.");
        W("«Панорама» — панорамирование Steam Audio на стерео: громкость каналов по боковому углу, без фильтрации (принцип встроенного 3D-панорамирования Unity).");
        W();
        W("ТАБЛИЦА 1. Уровни каналов по направлениям");
        W("   направление          метод                 L, дБ    R, дБ   ILD, дБ   ITD, мкс");
        foreach (var r in Results)
        {
            bool silent = Mathf.Min(r.L, r.R) < -100f;     // панорама сбоку: один канал полностью заглушён
            W(string.Format(ci, "   {0,-20} {1,-20} {2,7}  {3,7}  {4,7}  {5,8}", r.dir, r.method, D(r.L), D(r.R),
                silent ? (r.L > r.R ? "+∞" : "−∞") : r.ILD.ToString("0.0", ci), silent ? "—" : r.ITDus.ToString("0", ci)));
        }
        W("   «−∞» — канал заглушён полностью (тишина). ITD для такой пары не определена: взаимная корреляция с тишиной не имеет смысла.");
        W();
        W("ТАБЛИЦА 2. Уровни по октавам, дБ (L / R)");
        W("   направление          метод          " + string.Join("", Octaves.Select(o => (o >= 1000 ? (o / 1000).ToString("0", ci) + " кГц" : o.ToString("0", ci) + " Гц").PadLeft(13))));
        foreach (var r in Results)
            W(string.Format(ci, "   {0,-20} {1,-14} ", r.dir, r.method.StartsWith("HRTF") ? "HRTF" : "панорама") + string.Join("", r.bandsL.Select((v, i) => string.Format(ci, "{0,6}/{1,-6}", D(v), D(r.bandsR[i])))));
        W();
        W("ТАБЛИЦА 3. «Спереди» против «сзади»: разница уровней по октавам (среднее двух ушей), дБ");
        W("   пара                                метод          " + string.Join("", Octaves.Select(o => (o >= 1000 ? (o / 1000).ToString("0", ci) + "к" : o.ToString("0", ci)).PadLeft(8))) + "   RMS 1–16 кГц");
        foreach (var (a, b) in new[] { ("спереди 0°", "сзади 180°"), ("спереди-справа 30°", "сзади-справа 150°") })
            foreach (var bin in new[] { true, false })
            {
                var ra = Get(a, bin); var rb = Get(b, bin);
                if (ra == null || rb == null) continue;
                float d = SpectralDistance(ra, rb, out var diff);
                W(string.Format(ci, "   {0,-35} {1,-14} ", a + " − " + b, bin ? "HRTF" : "панорама") + string.Join("", diff.Select(v => string.Format(ci, "{0,8:0.0}", v))) + string.Format(ci, "   {0,8:0.0}", d));
            }
        W();
        var f = Get("спереди 0°", true); var bk = Get("сзади 180°", true); var pf = Get("спереди 0°", false); var pb = Get("сзади 180°", false);
        var rt = Get("справа 90°", true); var prt = Get("справа 90°", false);
        if (f != null && bk != null && pf != null && pb != null && rt != null && prt != null)
        {
            float dh = SpectralDistance(f, bk, out _), dp = SpectralDistance(pf, pb, out _);
            W("ВЫВОД (числа из таблиц выше):");
            W(string.Format(ci, "   • Спереди/сзади: у HRTF спектры различаются на {0:0.0} дБ (RMS по полосам 1–16 кГц), у панорамы — на {1:0.0} дБ.", dh, dp));
            W("     Панорама направления «вперёд» и «назад» не различает: каналы у неё одинаковые. HRTF различает — по спектру.");
            W(string.Format(ci, "   • Сбоку (справа 90°): у HRTF ILD {0:0.0} дБ и ITD {1:0} мкс; у панорамы левый канал заглушён полностью, задержки нет.", rt.ILD, rt.ITDus));
            W("     Панорама передаёт сторону только громкостью (и «выключает» дальнее ухо); HRTF — частотно-зависимой разницей уровней и задержкой между ушами.");
            var fr = Get("спереди-справа 30°", false); var br = Get("сзади-справа 150°", false);
            if (fr != null && br != null)
                W(string.Format(ci, "   • 30° и 150° справа: у панорамы каналы совпадают до десятых долей дБ ({0} / {1} и {2} / {3}) — это классическая «конусная» путаница вперёд-назад.", D(fr.L), D(fr.R), D(br.L), D(br.R)));
        }
        W();
        W("ЧТО ЭТИМ НЕ ПРОВЕРЕНО: восприятие человеком (без шлема и испытуемых), персональные HRTF (используется усреднённая),");
        W("доля ошибок «спереди/сзади» у слушателей. Замер доказывает, что обработка по HRTF в проекте работает и даёт признаки направления,");
        W("которых у панорамы нет; насколько хорошо по ним ориентируется конкретный участник — вопрос эксперимента со шлемом.");
        var path = Path.Combine(outDir, "30_Замер_HRTF_уровни_каналов.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        var csv = new StringBuilder("direction,method,band_hz,L_db,R_db\n");
        foreach (var r in Results) for (int i = 0; i < Octaves.Length; i++)
                csv.AppendLine(string.Format(ci, "{0},{1},{2:0},{3:0.00},{4:0.00}", r.dir, r.method.StartsWith("HRTF") ? "HRTF" : "pan", Octaves[i], r.bandsL[i], r.bandsR[i]));
        File.WriteAllText(Path.Combine(outDir, "31_Замер_HRTF_полосы.csv"), csv.ToString(), new UTF8Encoding(true));
        var sum = new StringBuilder("direction,method,L_db,R_db,ILD_db,ITD_us\n");
        foreach (var r in Results)
            sum.AppendLine(string.Format(ci, "{0},{1},{2:0.00},{3:0.00},{4:0.00},{5:0.0}", r.dir, r.method.StartsWith("HRTF") ? "HRTF" : "pan", r.L, r.R, r.ILD, r.ITDus));
        File.WriteAllText(Path.Combine(outDir, "32_Замер_HRTF_каналы.csv"), sum.ToString(), new UTF8Encoding(true));
        Debug.Log("[NEUROSHELF] 10.10 Замер HRTF: " + path + "\n" + sb);
    }
}
