using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// Регрессионный тест бюджета кадра. Падает, если в сценарии среднее время кадра выходит за порог
// или разрыв между условиями A и C снова становится большим. Так «мы оптимизировали» превращается
// в «оптимизация зафиксирована»: следующая работа, которая вернёт SSAO или забудет про LOD, уронит тест.
//
// Где и с каким порогом (подробно — 25_ЛР9_ход_работы.md, этап 9):
//  • Настоящая проверка идёт в СБОРКЕ Windows (меню 11.9b): Unity Test Framework собирает плеер с тестами,
//    запускает его и возвращает результат в редактор. Проверять бюджет в редакторе нельзя: в Play Mode
//    одна и та же сцена «после» стоила 10.5 мс 20.10 и 5.5 мс 23.10, а «до» — 10.4 мс: оверхед редактора
//    больше и нестабильнее, чем эффект оптимизации.
//    Окно плеера ставится 2160 × 1200 (пиксели двух экранов Vive), как в замерах.
//  • В Play Mode редактора (общий прогон 11.9 вместе с ЛР7 и ЛР8) тот же тест — грубая страховка с порогом
//    редактора: ловит только катастрофу (сцена в полтора раза тяжелее).
//  • Маршрут один и тот же (ячейка «проход вдоль стеллажей» автопилота), первые 2 с — прогрев, не учитываются.
public class PerformanceBudgetTests
{
    const string ScenePath = "Assets/_Project/Scenes/02_Store.unity";
#if UNITY_EDITOR
    // Редактор: среднее в Play Mode после оптимизации 5.5–10.5 мс в разные дни (протокол 40; зависит от раскладки окон
    // и фона редактора). Порог 16 мс — +50% к худшему: ловит только катастрофу.
    public const float MeanThresholdMs = 16f;
    public const float GapThresholdMs = 1.5f;
#else
    // Сборка: после оптимизации среднее 3.3–4.0 мс (замер 50_…, окно 2160 × 1200); ноутбук в нагретом состоянии
    // медленнее в ~1.65 раза (одна и та же сборка «до»: 10.4 мс 19.10 против 6.1 мс 23.10) → худший ожидаемый ≈ 6.6 мс.
    // Порог 8.0 мс = доля GPU в бюджете кадра (этап 1): запас ≥ 1.4 мс до случайного провала, а возврат к конвейеру
    // «до» в нагретом состоянии (10–10.8 мс) тест роняет. Разрыв A − C: 1.0 мс = две границы эквивалентности.
    public const float MeanThresholdMs = 8.0f;
    public const float GapThresholdMs = 1.0f;
#endif

    [UnitySetUp]
    public IEnumerator Load()
    {
#if UNITY_EDITOR
        yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        yield return SceneManager.LoadSceneAsync("02_Store");
#endif
        float t = 0f;
        while (ScenarioController.Instance == null && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
        Assert.IsNotNull(ScenarioController.Instance, "нет ScenarioController в сцене");
#if !UNITY_EDITOR
        Screen.SetResolution(2160, 1200, false);   // как в замерах: пиксели двух экранов Vive
        yield return new WaitForSeconds(1f);
#endif
        yield return null;
    }

    IEnumerator Measure(string cond, BenchRig.Rig rig, FrameMetricsRecorder rec, float[] outMean)
    {
        var sc = ScenarioController.Instance;
        var c = cond == "A" ? sc.conditionA : sc.conditionC;
        ExperimentContext.Find().SetCondition(c);
        BenchRig.Pose(rig, Cells.StartPos, Cells.StartYaw, Cells.StartPitch);
        yield return new WaitForSeconds(3f);                    // NPC и музыка входят в условие
        rec.Rescan();
        yield return Cells.Run("aisle", rig, default, 2f, 6f, rec);
        var s = FrameMetricsRecorder.Summarize(rec.Samples);
        outMean[0] = s.mean;
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[LR9] условие {0}: окно {7}×{8}, кадров {1}, среднее {2:0.00} мс, p99 {3:0.00} мс, сверх 11.1 мс {4:0.0}%, видимых {5:0}, персонажей в кадре {6:0.0}",
            cond, s.frames, s.mean, s.p99, s.overPct, s.visible, s.visibleSkinned, Screen.width, Screen.height));
    }

    [UnityTest, Category("LR9")]
    public IEnumerator Budget_MeanFrameTime_AandC_WithinThreshold_GapSmall()
    {
        var rig = BenchRig.Take();
        var rec = new FrameMetricsRecorder();
        var a = new float[1]; var c = new float[1];
        yield return Measure("A", rig, rec, a);
        yield return Measure("C", rig, rec, c);
        rec.Dispose();
        float gap = a[0] - c[0];
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[LR9] разрыв A − C = {0:+0.00;-0.00} мс (порог ±{1:0.0}); порог среднего {2:0.0} мс", gap, GapThresholdMs, MeanThresholdMs));
        Assert.Less(a[0], MeanThresholdMs, $"условие A: среднее время кадра {a[0]:0.00} мс выше порога {MeanThresholdMs} мс");
        Assert.Less(c[0], MeanThresholdMs, $"условие C: среднее время кадра {c[0]:0.00} мс выше порога {MeanThresholdMs} мс");
        Assert.Less(Mathf.Abs(gap), GapThresholdMs, $"разрыв A − C {gap:0.00} мс больше порога {GapThresholdMs} мс");
    }

    // Структурная часть: то, что держит бюджет, не должно молча исчезнуть.
    [UnityTest, Category("LR9")]
    public IEnumerator Budget_OptimizationsInPlace()
    {
        yield return null;
        var cam = Camera.main;
        Assert.IsTrue(cam.useOcclusionCulling, "Occlusion Culling у камеры выключен");
        Assert.LessOrEqual(cam.farClipPlane, 30f, "дальняя плоскость камеры снова больше зала");
        var npcs = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(x => x.isHuman).ToArray();
        Assert.Greater(npcs.Length, 0);
        foreach (var n in npcs)
        {
            var lg = n.GetComponent<LODGroup>();
            Assert.IsNotNull(lg, n.name + ": нет LODGroup");
            Assert.AreEqual(3, lg.lodCount, n.name + ": ожидается 3 уровня детализации");
            Assert.IsNotNull(n.GetComponent<NpcLodGovernor>(), n.name + ": нет NpcLodGovernor");
            Assert.AreEqual(AnimatorCullingMode.CullCompletely, n.cullingMode, n.name + ": аниматор обновляется вне кадра");
        }
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        Assert.IsFalse(rp.supportsCameraOpaqueTexture, "Opaque Texture снова включена (полноэкранная копия кадра без потребителя)");
        Debug.Log($"[LR9] на месте: окклюзия, far {cam.farClipPlane} м, LOD у {npcs.Length} персонажей, Opaque Texture выкл");
    }

    // Риггинг промоутера выключен там, где жеста нет: в условии C и вдали; включается, когда игрок подходит в условии A.
    [UnityTest, Category("LR9")]
    public IEnumerator Rig_OffInC_AndFar_OnNearInA()
    {
        var rig = BenchRig.Take();
        var sc = ScenarioController.Instance;
        var gov = Object.FindObjectsByType<NpcLodGovernor>(FindObjectsSortMode.None).First(g => g.GetComponent<PromoterIK>() != null);
        var promoter = gov.transform;
        // точка в 2 м перед промоутером, лицом к нему
        Vector3 near = promoter.position + promoter.forward * 2.0f; near.y = 0;
        float yaw = Quaternion.LookRotation(-promoter.forward).eulerAngles.y;

        ExperimentContext.Find().SetCondition(sc.conditionC);
        for (int i = 0; i < 60; i++) { BenchRig.Pose(rig, new Vector2(near.x, near.z), yaw, 10f); yield return null; }
        bool offInC = !gov.RigActive;
        ExperimentContext.Find().SetCondition(sc.conditionA);
        for (int i = 0; i < 60; i++) { BenchRig.Pose(rig, new Vector2(near.x, near.z), yaw, 10f); yield return null; }
        bool onNearA = gov.RigActive;
        Vector3 far = promoter.position + promoter.forward * 8.0f;
        for (int i = 0; i < 60; i++) { BenchRig.Pose(rig, new Vector2(Mathf.Clamp(far.x, -5.5f, 5.5f), Mathf.Clamp(far.z, -3.5f, 3.5f)), yaw, 10f); yield return null; }
        bool offFar = !gov.RigActive;
        Debug.Log($"[LR9] риггинг промоутера: C рядом — {(offInC ? "выкл" : "ВКЛ")}, A рядом ({gov.Distance:0.0} м сейчас) — {(onNearA ? "вкл" : "ВЫКЛ")}, A далеко — {(offFar ? "выкл" : "ВКЛ")}; переключений {gov.RigSwitches}");
        Assert.IsTrue(offInC, "в условии C риггинг промоутера считается, хотя жеста нет");
        Assert.IsTrue(onNearA, "в условии A рядом с промоутером риггинг не включился — жест не работает");
        Assert.IsTrue(offFar, "вдали риггинг не выключился");
    }
}
