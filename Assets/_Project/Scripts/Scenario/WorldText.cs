using UnityEngine;
using UnityEngine.UI;

// Текст в мире: world-space Canvas + uGUI Text со встроенным динамическим шрифтом LegacyRuntime.ttf
// (Liberation Sans — в нём есть кириллица; штатный шрифт TextMesh Pro кириллицы не содержит).
// 1 пиксель холста = 1 мм: размер шрифта в пикселях равен высоте букв в миллиметрах.
public static class WorldText
{
    static Font font;
    public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    // pixelsPerUnit: глифы растрируются в размере fontSize × pixelsPerUnit, а uGUI ограничивает его 300 px —
    // выше шрифт молча обрезается до 300 / pixelsPerUnit. Поэтому для крупного текста табло — 2.5, для мелкого — 8.
    public static Canvas Canvas(Transform parent, string name, Vector2 sizeMeters, Vector3 localPos, float pixelsPerUnit = 2.5f)
    {
        var t = parent.Find(name);
        GameObject go;
        if (t != null) go = t.gameObject;
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
        }
        var rt = (RectTransform)go.transform;
        rt.localPosition = localPos;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one * 0.001f;
        rt.sizeDelta = sizeMeters * 1000f;
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        var sc = go.GetComponent<CanvasScaler>();
        sc.dynamicPixelsPerUnit = pixelsPerUnit;
        return c;
    }

    public static Text Label(Transform canvas, string name, Rect mm, int size, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        var t = canvas.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Text));
        if (t == null) go.transform.SetParent(canvas, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(mm.x, -mm.y);
        rt.sizeDelta = new Vector2(mm.width, mm.height);
        rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one;
        var tx = go.GetComponent<Text>();
        tx.font = Font;
        tx.fontSize = size;
        tx.fontStyle = style;
        tx.alignment = anchor;
        tx.color = Color.white;
        tx.horizontalOverflow = HorizontalWrapMode.Wrap;
        tx.verticalOverflow = VerticalWrapMode.Truncate;
        tx.supportRichText = true;
        tx.raycastTarget = false;
        return tx;
    }

    public static void Tint(Renderer r, Color c, MaterialPropertyBlock mpb)
    {
        if (r == null) return;
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        r.SetPropertyBlock(mpb);
    }
}
