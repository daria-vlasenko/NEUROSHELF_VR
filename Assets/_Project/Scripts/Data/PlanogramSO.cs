using System;
using System.Collections.Generic;
using UnityEngine;

// Планограмма — какой товар на каком стеллаже и ярусе и по какой цене. Часть условия эксперимента:
// переход от захода A к заходу C — это подстановка другой планограммы, а не вторая сцена.
// Раскладку выполняет PlanogramApplier по событию ExperimentContext.ConditionChanged.
[CreateAssetMenu(menuName = "Neuroshelf/Planogram")]
public class PlanogramSO : ScriptableObject
{
    public string planogramName = "A_Manipulated";
    [TextArea] public string description;

    // Высоты ярусов из ЛР1: 0.60 / 1.10 / 1.60 м; толщина полки 0.05 м.
    public static readonly float[] TierY = { 0.60f, 1.10f, 1.60f };
    public const float BoardHalf = 0.025f;

    [Serializable]
    public class Row
    {
        public string shelf = "Shelf_03";
        [Range(0, 2)] public int tier;
        [Tooltip("Левый край полки по X и ширина, м. Товары делят ширину поровну.")]
        public float xStart = -2.4f;
        public float width = 2.4f;
        [Tooltip("Z центра товара (0.25 м от кромки полки)")]
        public float z = -1.45f;
        [Tooltip("Имена объектов товаров в сцене, слева направо")]
        public string[] products = new string[0];
    }

    [Serializable]
    public class Price
    {
        public string productId;
        public float price;
        [Tooltip("Зачёркнутая «старая» цена — эффект якоря. 0 — якоря нет.")]
        public float anchorPrice;
    }

    public List<Row> rows = new List<Row>();
    public List<Price> prices = new List<Price>();

    public Vector3 PositionOf(Row r, int index) =>
        new Vector3(r.xStart + r.width * (index + 1) / (r.products.Length + 1), TierY[r.tier] + BoardHalf, r.z);

    public bool Find(string objectName, out Row row, out int index)
    {
        foreach (var r in rows)
            for (int i = 0; i < r.products.Length; i++)
                if (r.products[i] == objectName) { row = r; index = i; return true; }
        row = null; index = -1; return false;
    }

    public Price PriceOf(string productId) => prices.Find(p => p.productId == productId);
}
