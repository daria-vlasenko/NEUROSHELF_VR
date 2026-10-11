using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;

// Виньетка, интенсивность которой пропорциональна скорости ИСКУССТВЕННОГО движения.
// Штатный TunnelingVignetteController (образец Starter Assets) работает как переключатель:
// провайдер начал движение — виньетка плавно закрылась до фиксированной апертуры, закончил — открылась.
// Здесь контроллер и шейдер штатные, а апертуру каждый кадр задаёт этот провайдер
// (точка расширения XRI — интерфейс ITunnelingVignetteProvider).
//
// Учитывается только движение XR Origin (стик, поворот), а не голова в room-scale:
// укачивание вызывает расхождение зрения и вестибулярного аппарата, а когда человек
// сам шагает или поворачивает голову, расхождения нет — затемнять незачем.
// Мгновенные скачки (телепорт, Snap Turn) виньетку не включают: в них нет видимого
// непрерывного движения (векции), которое и провоцирует укачивание.
[DefaultExecutionOrder(-50)]   // до TunnelingVignetteController.Update
public class SpeedVignetteProvider : MonoBehaviour, ITunnelingVignetteProvider
{
    public TunnelingVignetteController controller;
    public Transform origin;
    public LocomotionProfileSO profile;

    [Header("Пороги (копируются из профиля)")]
    public Vector2 linearSpeedRange = new Vector2(0.10f, 1.40f);
    public Vector2 angularSpeedRange = new Vector2(10f, 90f);
    [Range(0f, 1f)] public float strength = 1f;
    [Range(0.3f, 1f)] public float minAperture = 0.7f;
    [Range(-0.2f, 0.2f)] public float verticalShift = -0.12f;
    public float smoothing = 0.15f;
    public bool effectEnabled = true;

    [Header("Отладка (только чтение)")]
    public float linearSpeed;
    public float angularSpeed;
    public float intensity;
    public float aperture = 1f;
    public int jumpsIgnored;

    // Порог «скачка»: за один кадр плавное движение 1.4 м/с даже при 10 FPS — 0.14 м; Snap Turn — 45° за кадр
    const float JumpMeters = 0.35f, JumpDegrees = 20f;

    readonly VignetteParameters parameters = new VignetteParameters();
    Vector3 lastPos; float lastYaw; bool hasLast; float linF, angF;

    public VignetteParameters vignetteParameters => parameters;

    void OnEnable()
    {
        parameters.vignetteColor = Color.black;
        parameters.vignetteColorBlend = Color.black;
        parameters.apertureVerticalPosition = verticalShift;
        parameters.easeInTimeLock = false;
        parameters.easeOutDelayTime = 0f;
        parameters.easeInTime = 0f;      // сглаживание делаем сами, контроллер просто применяет апертуру
        parameters.easeOutTime = 0f;
        parameters.featheringEffect = 0.25f;
        parameters.apertureSize = 1f;
        hasLast = false;
        if (controller != null) controller.BeginTunnelingVignette(this);
    }

    void OnDisable()
    {
        if (controller != null) controller.EndTunnelingVignette(this);
    }

    public void ApplyProfile(LocomotionProfileSO p)
    {
        profile = p;
        if (p == null) return;
        effectEnabled = p.vignette;
        strength = p.vignetteStrength;
        minAperture = p.minAperture;
        linearSpeedRange = p.linearSpeedRange;
        angularSpeedRange = p.angularSpeedRange;
        smoothing = p.vignetteSmoothing;
        verticalShift = p.vignetteVerticalShift;
        parameters.apertureVerticalPosition = verticalShift;
    }

    // Интенсивность 0…1 для заданных скоростей — используется и в проверке порогов
    public float IntensityFor(float lin, float ang)
    {
        float a = Mathf.InverseLerp(linearSpeedRange.x, linearSpeedRange.y, lin);
        float b = Mathf.InverseLerp(angularSpeedRange.x, angularSpeedRange.y, ang);
        return effectEnabled ? Mathf.Clamp01(Mathf.Max(a, b)) * strength : 0f;
    }

    void Update()
    {
        if (origin == null) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var p = origin.position; float yaw = origin.eulerAngles.y;
        float lin = 0f, ang = 0f;
        if (hasLast)
        {
            var d = p - lastPos; d.y = 0f;
            float dy = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
            bool jump = d.magnitude > JumpMeters || dy > JumpDegrees;
            if (jump) jumpsIgnored++;
            else { lin = d.magnitude / dt; ang = dy / dt; }
        }
        lastPos = p; lastYaw = yaw; hasLast = true;

        // экспоненциальное сглаживание оценки скорости: убирает дрожь от неровной частоты кадров
        float k = smoothing > 0f ? 1f - Mathf.Exp(-dt / smoothing) : 1f;
        linF += (lin - linF) * k;
        angF += (ang - angF) * k;
        linearSpeed = linF; angularSpeed = angF;

        intensity = IntensityFor(linF, angF);
        if (intensity < 0.01f) intensity = 0f;            // в покое — полностью открыто
        aperture = Mathf.Lerp(1f, minAperture, intensity);
        parameters.apertureSize = aperture;

        if (controller != null) controller.BeginTunnelingVignette(this);  // идемпотентно: держит провайдер активным
    }
}
