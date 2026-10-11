using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

// Фиксированная точка обзора перед стеллажом / у кассы. Приземление всегда в одну позицию
// и лицом к стеллажу: у всех участников одинаковая дистанция и угол обзора полок.
// Для эксперимента воспроизводимость обзора важнее свободы шага.
public class ShelfTeleportationAnchor : TeleportationAnchor
{
    [Tooltip("Идентификатор точки в журнале маршрута: Shelf_01…04, Checkout")]
    public string anchorId;

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        if (!base.IsSelectableBy(interactor)) return false;
        var p = teleportAnchorTransform != null ? teleportAnchorTransform.position : transform.position;
        return !TeleportTargetValidator.Occupied(p);
    }
}
