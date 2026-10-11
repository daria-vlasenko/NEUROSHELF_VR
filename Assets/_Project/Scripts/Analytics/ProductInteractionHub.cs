using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Точка расширения для слоя аналитики.
// Сейчас: каждое событие с товаром пишется в консоль с префиксом [NS-INT].
// Дальше: SessionRecorder подписывается на ProductInteractionHub.Raised и пишет CSV.
// Хаб ничего не знает об игровой логике и ничего не вызывает в обратную сторону:
// отключили запись — взаимодействие работает как прежде.

public enum PackagingType { Bottle, Can, Box }
public enum ProductEventType { Picked, Released, Placed, LandedOnFloor }
public enum GrabRegion { None, Near, Far }
public enum ProductPlace { None, Shelf, Basket, Floor, Air, Hand }
public enum ReleaseOutcome { None, Purchased, Returned, Thrown, Dropped, HandToHand }

[Serializable]
public struct ProductInteractionEvent
{
    public ProductEventType type;
    public float time;               // Time.time, с
    public string productId;         // VOLTA, PURA, …
    public PackagingType packaging;
    public int tier;                 // 0 — 0.60 м, 1 — 1.10 м, 2 — 1.60 м (как в планограмме)
    public float tierHeight;
    public string hand;              // Left / Right / имя сокета
    public GrabRegion region;        // Near — рукой, Far — лучом
    public ProductPlace from;        // откуда взят (Picked)
    public ProductPlace place;       // куда положен (Placed)
    public bool ownSlot;             // возвращён на своё место планограммы
    public int slot;                 // номер ячейки корзины
    public float holdSeconds;        // сколько держали в руке (Released)
    public float releaseSpeed;       // скорость товара после отпускания, м/с
    public float handSpeed;          // скорость руки в момент отпускания, м/с
    public float headHeight;         // высота головы в момент события — наклон виден как падение
    public float handHeight;         // высота руки в момент события
    public float distance;           // расстояние рука — товар при взятии
    public ReleaseOutcome outcome;

    public override string ToString()
    {
        var c = CultureInfo.InvariantCulture;
        string tierS = string.Format(c, "tier={0} ({1:0.00} м)", tier, tierHeight);
        switch (type)
        {
            case ProductEventType.Picked:
                return string.Format(c, "PICK     {0,-6} {1} {2} from={3} via={4} dist={5:0.00} м head={6:0.00} м hand={7:0.00} м",
                    productId, tierS, hand, from, region, distance, headHeight, handHeight);
            case ProductEventType.Released:
                return string.Format(c, "RELEASE  {0,-6} {1} {2} held={3:0.00} с v_item={4:0.00} м/с v_hand={5:0.00} м/с → {6}",
                    productId, tierS, hand, holdSeconds, releaseSpeed, handSpeed, outcome);
            case ProductEventType.Placed:
                return string.Format(c, "PLACE    {0,-6} {1} → {2}{3}", productId, tierS, place,
                    place == ProductPlace.Basket ? " slot " + slot : place == ProductPlace.Shelf ? (ownSlot ? " (своё место)" : " (чужое место)") : "");
            default:
                return string.Format(c, "FLOOR    {0,-6} {1} упал на пол", productId, tierS);
        }
    }
}

public static class ProductInteractionHub
{
    /// Подписка для будущего SessionRecorder.
    public static event Action<ProductInteractionEvent> Raised;

    public static bool logToConsole = true;

    /// Последние события сессии (для проверок и отладки), не больше 512.
    public static readonly List<ProductInteractionEvent> History = new List<ProductInteractionEvent>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { History.Clear(); Raised = null; logToConsole = true; }

    public static void Raise(ProductInteractionEvent e)
    {
        e.time = Time.time;
        if (History.Count >= 512) History.RemoveAt(0);
        History.Add(e);
        if (logToConsole) Debug.Log("[NS-INT] " + e);
        try { Raised?.Invoke(e); }
        catch (Exception ex) { Debug.LogException(ex); }
    }
}
