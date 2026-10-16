using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Разведение ролей рук.
// Обе руки — Near-Far Interactor (ЛР5): берут товар рукой и лучом, и этим же лучом работают с интерфейсом.
// Конфликт: рука, которая что-то держит (товар, корзину), не должна одновременно «кликать» по табло —
// иначе отпускание курка над кнопкой роняет товар и нажимает кнопку одним движением. XRI по умолчанию
// блокирует UI-луч выбирающей руки (blockUIOnInteractableSelection); здесь это сделано явно и симметрично:
//   • держит предмет → UI-луч этой руки выключен (enableUIInteraction = false);
//   • свободна        → UI-луч включён;
//   • левая прицеливается телепортом (ЛР6) → её UI-луч тоже выключен: два луча на одной руке путают.
// Итог: интерфейс всегда нажимает свободная рука. Типичный случай — товар в правой, «Оплатить» левой.
// Переключения пишутся в журнал и проверяются автотестом.
public class HandRoleArbiter : MonoBehaviour
{
    public NearFarInteractor productHand;   // правая
    public NearFarInteractor uiHand;        // левая (у неё же телепорт ЛР6 и меню на запястье)
    public LocomotionRig rig;

    public bool ProductHandHolding { get; private set; }
    public bool UIHandHolding { get; private set; }
    public bool ProductHandUI => productHand != null && productHand.enableUIInteraction;
    public bool UIHandUI => uiHand != null && uiHand.enableUIInteraction;
    public readonly List<string> Journal = new List<string>();

    void Awake() { if (rig == null) rig = FindFirstObjectByType<LocomotionRig>(); }

    static bool Holding(NearFarInteractor i)
    {
        if (i == null) return false;
        foreach (var s in i.interactablesSelected) if (s is XRGrabInteractable) return true;
        return false;
    }

    void Update()
    {
        bool r = Holding(productHand), l = Holding(uiHand);
        bool aiming = rig != null && rig.TeleportAiming;
        Apply(productHand, !r, ref r, ProductHandHolding, "правая", r ? "держит предмет" : "свободна");
        ProductHandHolding = r;
        bool lBlocked = l || aiming;
        Apply(uiHand, !lBlocked, ref l, UIHandHolding, "левая", l ? "держит предмет" : aiming ? "прицеливается телепортом" : "свободна");
        UIHandHolding = l;
    }

    void Apply(NearFarInteractor i, bool ui, ref bool holding, bool wasHolding, string name, string why)
    {
        if (i == null || i.enableUIInteraction == ui) return;
        i.enableUIInteraction = ui;
        Log($"{name} {why} → UI-луч {(ui ? "включён" : "выключен")}");
    }

    void Log(string s)
    {
        Journal.Add($"t={Time.time,7:0.00}  {s}");
        Debug.Log("[NS-UI] " + s);
    }
}
