using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Зона телепорта на полу. Отличие от штатной TeleportationArea: точка приземления
// дополнительно проверяется по NavMesh из ЛР4 и по капсулам NPC.
// Если проверка не пройдена, IsSelectableBy = false — кривая и прицел переходят
// в «заблокированный» вид (штатный механизм XRI), телепорт не выполняется.
public class NavMeshTeleportationArea : TeleportationArea
{
    public static bool freeFloorEnabled = true;     // выставляет LocomotionRig из профиля

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        if (!freeFloorEnabled) return false;
        if (!base.IsSelectableBy(interactor)) return false;
        if (interactor is XRRayInteractor ray && ray.TryGetCurrent3DRaycastHit(out var hit))
            return TeleportTargetValidator.Check(hit.point) == TeleportVerdict.Ok;
        return true;
    }

    protected override bool GenerateTeleportRequest(UnityEngine.XR.Interaction.Toolkit.Interactors.IXRInteractor interactor,
        RaycastHit raycastHit, ref TeleportRequest teleportRequest)
    {
        if (TeleportTargetValidator.Check(raycastHit.point) != TeleportVerdict.Ok) return false;
        return base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest);
    }
}
