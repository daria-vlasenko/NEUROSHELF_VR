using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

// Применяет LocomotionProfileSO к штатным провайдерам XRI 3.x и маршрутизирует ввод.
//
// Почему не штатный ControllerInputActionManager из Starter Assets: он рассчитан на то, что
// на одном контроллере плавное движение, на другом — телепорт, одновременно. В эксперименте
// способ перемещения — один на сессию (это независимая переменная), поэтому режим переключается
// здесь целиком: в режиме Smooth телепорт недоступен вовсе, в режиме Teleport стик не двигает.
//
// Раскладка: левый стик — плавное движение (Smooth); правый стик вперёд — прицел телепорта
// (Teleport), отпустить — переместиться; правый стик влево/вправо — поворот (оба режима).
public class LocomotionRig : MonoBehaviour
{
    public LocomotionProfileSO profile;

    [Header("Провайдеры XRI")]
    public ContinuousMoveProvider move;
    public SnapTurnProvider snapTurn;
    public ContinuousTurnProvider continuousTurn;
    public TeleportationProvider teleportation;

    [Header("Телепорт")]
    public XRRayInteractor teleportInteractor;
    public NearFarInteractor rightNearFar;
    public InputActionReference teleportMode;
    public InputActionReference teleportCancel;

    [Header("Комфорт")]
    public SpeedVignetteProvider vignette;
    public BasketRestFrame basket;
    public RouteLogger route;

    public bool TeleportAiming { get; private set; }
    int deactivateFrame = -1;

    void Awake() { Apply(); }

    void OnEnable()
    {
        if (teleportMode != null && teleportMode.action != null)
        { teleportMode.action.performed += OnAim; teleportMode.action.canceled += OnRelease; }
        if (teleportCancel != null && teleportCancel.action != null) teleportCancel.action.performed += OnCancel;
    }

    void OnDisable()
    {
        if (teleportMode != null && teleportMode.action != null)
        { teleportMode.action.performed -= OnAim; teleportMode.action.canceled -= OnRelease; }
        if (teleportCancel != null && teleportCancel.action != null) teleportCancel.action.performed -= OnCancel;
    }

    void OnAim(InputAction.CallbackContext _) => SetTeleportAim(true);
    void OnRelease(InputAction.CallbackContext _) => SetTeleportAim(false);
    void OnCancel(InputAction.CallbackContext _) { if (TeleportAiming && teleportInteractor != null) teleportInteractor.gameObject.SetActive(false); TeleportAiming = false; ShowNearFar(true); }

    // Прицел телепорта: включить кривую, спрятать луч Near-Far той же руки (два луча на одной руке путают).
    public void SetTeleportAim(bool on)
    {
        if (profile == null || profile.mode != LocomotionMode.Teleport || teleportInteractor == null) { TeleportAiming = false; return; }
        if (on)
        {
            // если рука держит товар лучом, телепорт не включаем — стик занят манипуляцией
            if (rightNearFar != null && rightNearFar.hasSelection) return;
            deactivateFrame = -1;
            teleportInteractor.gameObject.SetActive(true);
            ShowNearFar(false);
            TeleportAiming = true;
        }
        else if (TeleportAiming)
        {
            // выключаем не сразу: на отпускании стика интерактор завершает выбор (Select Exited),
            // и именно в этот момент TeleportationArea отправляет запрос на перемещение
            deactivateFrame = Time.frameCount + 2;
            TeleportAiming = false;
            ShowNearFar(true);
        }
    }

    void ShowNearFar(bool on)
    {
        if (rightNearFar != null && !rightNearFar.hasSelection) rightNearFar.gameObject.SetActive(on);
    }

    void LateUpdate()
    {
        if (deactivateFrame > 0 && Time.frameCount >= deactivateFrame)
        {
            deactivateFrame = -1;
            if (!TeleportAiming && teleportInteractor != null) teleportInteractor.gameObject.SetActive(false);
        }
    }

    [ContextMenu("Применить профиль")]
    public void Apply()
    {
        var p = profile;
        if (p == null) { Debug.LogWarning("[NS-LOC] Нет профиля перемещения"); return; }
        bool smooth = p.mode == LocomotionMode.Smooth;

        if (move != null) { move.enabled = smooth; move.moveSpeed = p.moveSpeed; move.enableStrafe = p.strafe; move.enableFly = false; }
        if (teleportation != null) teleportation.enabled = !smooth;
        if (teleportInteractor != null)
        {
            teleportInteractor.velocity = p.curveVelocity;
            teleportInteractor.acceleration = p.curveAcceleration;
            if (smooth || !TeleportAiming) teleportInteractor.gameObject.SetActive(false);
        }
        NavMeshTeleportationArea.freeFloorEnabled = p.freeFloorTeleport;

        if (snapTurn != null) { snapTurn.enabled = p.turn == TurnMode.Snap; snapTurn.turnAmount = p.snapAngle; }
        if (continuousTurn != null) { continuousTurn.enabled = p.turn == TurnMode.Continuous; continuousTurn.turnSpeed = p.continuousTurnSpeed; }

        if (vignette != null) vignette.ApplyProfile(p);
        if (basket != null) basket.Configure(p.basketOffset, p.basketHeight);
        if (route != null) { route.profile = p; route.interval = p.logInterval; }
        Debug.Log($"[NS-LOC] Профиль {p.profileName}: режим {p.mode}, {p.moveSpeed:0.00} м/с, поворот {p.turn}, виньетка {(p.vignette ? p.vignetteStrength.ToString("0.00") : "выкл")}");
    }

    public void SetProfile(LocomotionProfileSO p) { profile = p; Apply(); }

    // Максимальная дальность кривой Projectile по горизонтали со старта на высоте h (без сопротивления):
    // R = v²/g · (sinθcosθ + cosθ·√(sin²θ + 2gh/v²)), максимум ищем перебором угла.
    public static float CurveRange(float v, float g, float h, out float bestAngle)
    {
        float best = 0f; bestAngle = 0f;
        for (float a = 0f; a <= 89f; a += 0.5f)
        {
            float r = a * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            float R = v * v / g * (s * c + c * Mathf.Sqrt(s * s + 2f * g * h / (v * v)));
            if (R > best) { best = R; bestAngle = a; }
        }
        return best;
    }
}
