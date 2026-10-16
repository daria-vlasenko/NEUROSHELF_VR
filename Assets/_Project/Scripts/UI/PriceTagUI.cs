using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// Ценник на кромке полки — World Space Canvas, а не куб с надписью. Это одновременно элемент интерфейса
// и экспериментальный стимул: зачёркнутая «старая» цена рядом с реальной — эффект якоря
// (Tversky & Kahneman, 1974). Содержимое приходит из планограммы условия (PlanogramApplier):
// в условии A у продвигаемого товара есть якорная цена, в условии C — только честная.
//
// Масштаб: 1 пиксель макета = 1 мм (localScale 0.001). Размер ярлыка 110 × 65 мм.
// Холст пассивный (без Graphic Raycaster): ценник не нажимают, а лишний рейкастер на 12 холстах
// стоил бы времени кадра.
public class PriceTagUI : MonoBehaviour
{
    public const float WidthMm = 110f, HeightMm = 65f;

    public Image back;
    public Text nameText, priceText, anchorText;
    public Image strike;

    public bool HasAnchor { get; private set; }
    public string PriceShown => priceText != null ? priceText.text : "";
    public string AnchorShown => anchorText != null ? anchorText.text : "";

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;
    static readonly Color Ink = new Color(0.08f, 0.08f, 0.10f);
    static readonly Color AnchorInk = new Color(0.55f, 0.10f, 0.10f);

    /// Строит ярлык как дочерний объект: корень — сам холст, поэтому дочерние «Back» и «Strike»
    /// находятся по имени прямо от корня (на это опираются проверки ЛР7).
    public static PriceTagUI Build(Transform parent, string productId)
    {
        var existing = parent.Find("Tag_" + productId);
        var go = existing != null ? existing.gameObject : new GameObject("Tag_" + productId, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.localScale = Vector3.one * 0.001f;
        rt.sizeDelta = new Vector2(WidthMm, HeightMm);
        var cv = Get<Canvas>(go);
        cv.renderMode = RenderMode.WorldSpace;
        var sc = Get<CanvasScaler>(go);
        sc.dynamicPixelsPerUnit = 8f;    // мелкий текст: глиф растрируется 8 px на мм

        var ui = Get<PriceTagUI>(go);
        ui.back = Img(rt, "Back", new Rect(0, 0, WidthMm, HeightMm), Color.white);
        var band = Img(rt, "Band", new Rect(0, 0, WidthMm, 3), new Color(0.2f, 0.2f, 0.22f));
        band.raycastTarget = false;
        ui.nameText = WorldText.Label(rt, "Name", new Rect(4, 5, 102, 16), 13, TextAnchor.UpperLeft, FontStyle.Bold);
        ui.priceText = WorldText.Label(rt, "Price", new Rect(36, 20, 70, 42), 30, TextAnchor.LowerRight, FontStyle.Bold);
        ui.anchorText = WorldText.Label(rt, "Anchor", new Rect(4, 33, 42, 24), 15, TextAnchor.MiddleLeft);
        foreach (var t in new[] { ui.nameText, ui.priceText }) t.color = Ink;
        ui.anchorText.color = AnchorInk;
        // перечёркивание поверх старой цены: центр поля Anchor (x 4…46, y 33…57 мм)
        ui.strike = Img(rt, "Strike", new Rect(4, 44, 38, 2.5f), new Color(0.75f, 0.10f, 0.10f));
        ui.strike.transform.SetAsLastSibling();
        ui.nameText.text = productId;
        return ui;
    }

    static T Get<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c != null ? c : g.AddComponent<T>(); }

    static Image Img(RectTransform parent, string name, Rect mm, Color c)
    {
        var t = parent.Find(name);
        var go = t != null && t.GetComponent<Image>() != null ? t.gameObject : null;
        if (go == null)
        {
            if (t != null) Destroy(t.gameObject);
            go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
        }
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(mm.x, -mm.y);
        rt.sizeDelta = new Vector2(mm.width, mm.height);
        rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one;
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    public void SetData(float price, float anchorPrice)
    {
        HasAnchor = anchorPrice > 0f;
        priceText.text = price > 0f ? price.ToString("0.00", ci) : "—";
        anchorText.text = HasAnchor ? anchorPrice.ToString("0.00", ci) : "";
        strike.gameObject.SetActive(HasAnchor);
    }

    public void SetColor(Color c) { if (back != null) back.color = c; }
}
