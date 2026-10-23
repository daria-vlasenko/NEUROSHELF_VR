using UnityEngine;
using UnityEngine.Animations.Rigging;

// Уровни детализации персонажа по реальному расстоянию в зале и отключение риггинга, когда он не нужен.
//
// 1. Пороги LODGroup задаются в метрах (lod1Distance, lod2Distance), а Unity сравнивает экранную долю высоты.
//    Пересчёт метров в долю зависит от поля зрения камеры и от QualitySettings.lodBias, поэтому компонент
//    пересчитывает пороги под текущую камеру: на десктопе (fov 60°) и в шлеме (≈100–110°) персонаж
//    переключается на одном и том же расстоянии, а не «когда повезёт».
// 2. Two Bone IK и Multi-Aim промоутера (Animation Rigging) считаются каждый кадр, пока персонаж в кадре, даже с весом 0:
//    в условии C (жеста нет совсем) и на 7 м (жест включается только ближе 4.5 м, PromoterIK.noticeDistance).
//    Вне кадра их уже выключает Animator (Culling Mode) — это проверено замером. Слой риггинга выключается
//    (RigLayer.active = false — без перестройки графа), когда жест невозможен: условие без жеста, дальше
//    rigOffDistance или персонаж не в кадре.
// 3. Animator вне кадра не обновляется совсем (CullCompletely): позу невидимого персонажа никто не увидит,
//    перемещение по NavMesh от аниматора не зависит (applyRootMotion = false).
[DisallowMultipleComponent]
public class NpcLodGovernor : MonoBehaviour
{
    [Header("Пороги, м (от глаз до центра персонажа)")]
    public float lod1Distance = 4.2f;
    public float lod2Distance = 6.0f;
    [Tooltip("Дальше этого расстояния риггинг (IK, Multi-Aim) выключен")]
    public float rigOffDistance = 5.0f;
    [Tooltip("Гистерезис включения риггинга, м — чтобы на границе не мигал")]
    public float rigHysteresis = 0.3f;
    public bool cullAnimatorOffscreen = true;
    public bool rigGovernor = true;

    LODGroup lg; Animator anim; RigBuilder rb; Camera cam; PromoterIK pik;
    float lastFov = -1f, lastBias = -1f;
    Renderer[] all;

    public bool RigActive { get; private set; } = true;
    public bool Visible { get; private set; }
    public float Distance { get; private set; }
    public int RigSwitches { get; private set; }
    public LODGroup Group => lg;

    void Awake()
    {
        lg = GetComponent<LODGroup>();
        anim = GetComponent<Animator>();
        rb = GetComponent<RigBuilder>();
        pik = GetComponent<PromoterIK>();
        all = GetComponentsInChildren<Renderer>(true);
        if (anim != null && cullAnimatorOffscreen) anim.cullingMode = AnimatorCullingMode.CullCompletely;
    }

    void Update()
    {
        if (cam == null) { cam = Camera.main; if (cam == null) return; }
        if (lg != null && (!Mathf.Approximately(cam.fieldOfView, lastFov) || !Mathf.Approximately(QualitySettings.lodBias, lastBias))) ApplyThresholds();

        var c = lg != null ? lg.transform.TransformPoint(lg.localReferencePoint) : transform.position + Vector3.up;
        Distance = Vector3.Distance(cam.transform.position, c);
        bool vis = false;
        foreach (var r in all) if (r != null && r.isVisible && !(r is LineRenderer)) { vis = true; break; }
        Visible = vis;

        if (rb != null && rigGovernor)
        {
            // риггинг нужен, только когда жест может быть виден: персонаж в кадре, ближе rigOffDistance
            // и промоутер в этом условии действительно указывает (в условии C promoterPointing = false — жеста нет вовсе).
            // Вес IK плавно гаснет 0.4 с после ухода игрока — пока не погас, слой не выключается.
            bool gesture = pik == null || pik.dbgNear || (pik.armIK != null && pik.armIK.weight > 0.001f);
            bool want = vis && gesture && Distance < rigOffDistance + (RigActive ? rigHysteresis : 0f);
            if (want != RigActive)
            {
                RigActive = want; RigSwitches++;
                foreach (var l in rb.layers) l.active = want;
            }
        }
    }

    // доля высоты экрана, которую занимает объект размера size на расстоянии d (так считает LODGroup, с учётом lodBias)
    public static float ScreenHeight(float size, float d, float fovDeg, float lodBias) =>
        size * lodBias / (2f * d * Mathf.Tan(0.5f * fovDeg * Mathf.Deg2Rad));

    public void ApplyThresholds()
    {
        if (lg == null || cam == null) return;
        lastFov = cam.fieldOfView; lastBias = QualitySettings.lodBias;
        var lods = lg.GetLODs();
        if (lods.Length < 3) return;
        float size = lg.size * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        lods[0].screenRelativeTransitionHeight = ScreenHeight(size, lod1Distance, lastFov, lastBias);
        lods[1].screenRelativeTransitionHeight = ScreenHeight(size, lod2Distance, lastFov, lastBias);
        lods[2].screenRelativeTransitionHeight = 0f;   // дальний уровень не отсекается: в зале 12 м человек виден всегда
        lg.SetLODs(lods);
    }
}
