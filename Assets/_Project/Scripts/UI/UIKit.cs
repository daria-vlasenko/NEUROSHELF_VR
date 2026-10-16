using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

// Построение World Space холстов в одном масштабе: 1 пиксель макета = 1 мм (localScale 0.001).
// Размеры в коде задаются в миллиметрах — они же идут в таблицу «размер панели, см» отчёта.
public static class UIKit
{
    public static readonly Color PanelDark = new Color(0.08f, 0.09f, 0.11f, 0.96f);
    public static readonly Color PanelMid = new Color(0.15f, 0.16f, 0.19f, 1f);
    public static readonly Color Accent = new Color(0.20f, 0.55f, 0.95f, 1f);
    public static readonly Color Ok = new Color(0.16f, 0.72f, 0.36f, 1f);
    public static readonly Color Off = new Color(0.30f, 0.31f, 0.33f, 1f);
    public static readonly Color Warn = new Color(1.00f, 0.55f, 0.12f, 1f);

    /// interactive = true: холст получает Tracked Device Graphic Raycaster (лучи XRI) и камеру событий.
    public static Canvas WorldCanvas(Transform parent, string name, Vector2 sizeMm, Vector3 localPos, Quaternion localRot, bool interactive, float pixelsPerUnit = 2.5f)
    {
        var t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        if (t == null) go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.localPosition = localPos; rt.localRotation = localRot;
        rt.localScale = Vector3.one * 0.001f;
        rt.sizeDelta = sizeMm;
        var c = Get<Canvas>(go);
        c.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) c.worldCamera = Camera.main;
        Get<CanvasScaler>(go).dynamicPixelsPerUnit = pixelsPerUnit;
        Get<CanvasGroup>(go);
        var ray = go.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (interactive && ray == null) go.AddComponent<TrackedDeviceGraphicRaycaster>();
        if (!interactive && ray != null) Object.DestroyImmediate(ray);
        return c;
    }

    public static Image Panel(Transform parent, string name, Rect mm, Color c, bool raycast = false)
    {
        var t = parent.Find(name);
        GameObject go = t != null && t.GetComponent<Image>() != null ? t.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (t == null || go != t.gameObject) go.transform.SetParent(parent, false);
        Place((RectTransform)go.transform, mm);
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = raycast;
        return img;
    }

    public static void Place(RectTransform rt, Rect mm)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(mm.x, -mm.y);
        rt.sizeDelta = new Vector2(mm.width, mm.height);
        rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one;
        var p = rt.localPosition; p.z = 0f; rt.localPosition = p;
    }

    public static Text Label(Transform parent, string name, Rect mm, int sizeMm, TextAnchor anchor, FontStyle style = FontStyle.Normal, Color? color = null)
    {
        var tx = WorldText.Label(parent, name, mm, sizeMm, anchor, style);
        if (color.HasValue) tx.color = color.Value;
        return tx;
    }

    /// Кнопка холста: фон-Image (цель лучей) + подпись. Логика — XRCanvasButton.
    public static XRCanvasButton Button(Transform parent, string id, string label, Rect mm, int fontMm)
    {
        var bg = Panel(parent, "Btn_" + id, mm, Ok, true);
        var tx = Label(bg.transform, "Label", new Rect(0, 0, mm.width, mm.height), fontMm, TextAnchor.MiddleCenter, FontStyle.Bold);
        tx.text = label;
        var b = Get<XRCanvasButton>(bg.gameObject);
        b.id = id; b.bg = bg; b.label = tx; b.text = label;
        return b;
    }

    public static T Get<T>(GameObject g) where T : Component
    {
        var c = g.GetComponent<T>();
        return c != null ? c : g.AddComponent<T>();
    }
}
