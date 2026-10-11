using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public enum SocketKind { Shelf, Basket }

// Точка фиксации товара: ячейка полки (место по планограмме) или ячейка корзины.
// Отличие от штатного XR Socket Interactor одно: товар ставится основанием на точку фиксации,
// а не точкой захвата. Точка захвата у бутылки — на горлышке, у коробки — в центре грани;
// без этой поправки бутылка в корзине «висела» бы на горлышке над дном.
public class ProductSocket : XRSocketInteractor
{
    [Header("NEUROSHELF")]
    public SocketKind kind = SocketKind.Basket;
    [Tooltip("Пусто — принимает любой товар. Иначе только товар с этим ID (ячейка полки).")]
    public string acceptOnlyId = "";
    public int slotIndex;

    readonly Dictionary<IXRInteractable, Transform> helpers = new Dictionary<IXRInteractable, Transform>();

    public override Transform GetAttachTransform(IXRInteractable interactable)
    {
        var rest = base.GetAttachTransform(interactable);
        var item = interactable != null && interactable.transform != null
            ? interactable.transform.GetComponent<ProductItem>() : null;
        if (item == null || item.Grip == null || item.Grip == item.transform) return rest;

        if (!helpers.TryGetValue(interactable, out var h) || h == null)
        {
            h = new GameObject("~Attach_" + item.productId).transform;
            h.SetParent(transform, false);
            helpers[interactable] = h;
        }
        // корень товара (основание) совпадает с rest → точка захвата смещена так же, как у товара
        h.SetPositionAndRotation(rest.TransformPoint(item.Grip.localPosition), rest.rotation * item.Grip.localRotation);
        return h;
    }

    public bool Accepts(IXRInteractable interactable)
    {
        var item = interactable != null && interactable.transform != null
            ? interactable.transform.GetComponent<ProductItem>() : null;
        return item != null && (string.IsNullOrEmpty(acceptOnlyId) || item.productId == acceptOnlyId);
    }

    public override bool CanHover(UnityEngine.XR.Interaction.Toolkit.Interactables.IXRHoverInteractable interactable) =>
        base.CanHover(interactable) && Accepts(interactable);

    public override bool CanSelect(IXRSelectInteractable interactable) =>
        base.CanSelect(interactable) && Accepts(interactable);
}
