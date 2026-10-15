using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Табло разбора внутри зала (упрощённая замена отдельной сцены 03_DebriefLab).
// Показывает собственные данные участника: маршрут на плане зала, покупки и приёмы, которые
// действовали на каждую покупку, время у стеллажей; в итогах — сравнение захода A и захода C.
// Данные берёт у слоя аналитики (ScenarioSessionRecorder) и из чека сценария (RunRecord).
public class DebriefBoard : MonoBehaviour
{
    public ScenarioController controller;
    public Text title;
    public Text body;
    public Text legend;
    [Header("Таблица итогов: 4 колонки (шрифт пропорциональный — выравниваем колонками, а не пробелами)")]
    public Text colName, colA, colC, colD;
    [Header("План зала")]
    public Transform map;                   // локальные оси: x — вправо, y — вверх; 1 ед. = 1 м плана
    public LineRenderer routeA;
    public LineRenderer routeC;
    public float mapScale = 0.05f;          // 1 м зала = 5 см на табло
    public Vector2 hallCenter = Vector2.zero;
    public string promotedShelf = "Shelf_03";

    public string CurrentTitle => title != null ? title.text : "";
    public string CurrentBody => body != null ? body.text : "";

    static readonly CultureInfo ci = CultureInfo.InvariantCulture;

    void Awake() { if (controller == null) controller = FindFirstObjectByType<ScenarioController>(); }
    void OnEnable() { ScenarioController.StateChanged += OnState; }
    void OnDisable() { ScenarioController.StateChanged -= OnState; }
    void Start() => Redraw();
    void OnState(ScenarioState a, ScenarioState b) => Redraw();

    public void Redraw()
    {
        if (controller == null) return;
        var rec = ScenarioSessionRecorder.Instance;
        var st = controller.State;
        var sb = new StringBuilder();
        string T;
        tbl = null;
        Route(routeA, null); Route(routeC, null);

        if (st == ScenarioState.Debrief && controller.Runs[1] != null)
        {
            var r = controller.Runs[1];
            var s = rec != null ? rec.Runs[1] : null;
            T = "Разбор: что происходило в заходе 1";
            sb.Append(string.Format(ci, "Время захода {0:0} с · пройдено {1:0.0} м · взято в руки {2} · возвращено {3}\n\n",
                r.Duration, s != null ? s.pathLength : 0f, s != null ? s.picks : 0, s != null ? s.returns : 0));
            foreach (var l in r.receipt)
            {
                sb.Append(string.Format(ci, "<b>{0}</b>  {1:0.00}", l.productId, l.price));
                if (l.anchorPrice > 0f) sb.Append(string.Format(ci, "  (зачёркнуто {0:0.00})", l.anchorPrice));
                sb.Append(string.Format(ci, " · {0}, ярус {1:0.00} м", l.shelf, PlanogramSO.TierY[Mathf.Clamp(l.tier, 0, 2)]));
                if (s != null) sb.Append(string.Format(ci, " · у стеллажа {0:0} с · брали {1}×", s.Dwell(l.shelf), s.PicksOf(l.productId)));
                sb.Append('\n');
                sb.Append(l.techniques.Count > 0
                    ? "   действовали: " + string.Join(", ", l.techniques) + "\n"
                    : "   <color=#8d939b>приёмы влияния не действовали</color>\n");
            }
            if (r.receipt.Count == 0) sb.Append("Покупок нет.\n");
            sb.Append(string.Format(ci, "\nПод воздействием приёмов: {0} из {1} покупок · чек {2:0.00}", r.Manipulated, r.receipt.Count, r.Total));
            if (r.pauses > 0) sb.Append(string.Format(ci, " · пауз {0}", r.pauses));
            sb.Append("\n<color=#8d939b>«Действовали» — включены в этом условии, а не доказанная причина выбора.</color>");
            if (s != null) Route(routeA, s.route);
            if (legend != null) legend.text = "<color=#ff8a3d>— маршрут, заход 1</color>";
        }
        else if (st == ScenarioState.Results && controller.Runs[1] != null && controller.Runs[2] != null)
        {
            var a = controller.Runs[1]; var c = controller.Runs[2];
            var sa = rec != null ? rec.Runs[1] : null; var sc = rec != null ? rec.Runs[2] : null;
            float budget = controller.shoppingList != null ? controller.shoppingList.budget : 0f;
            T = "Итоги: заход 1 (с приёмами) против захода 2 (нейтральный)";
            tbl = new[] { new StringBuilder("<b>Показатель</b>\n"), new StringBuilder("<b>Заход 1</b>\n"), new StringBuilder("<b>Заход 2</b>\n"), new StringBuilder("<b>Разница</b>\n") };
            Line(sb, "Сумма чека", a.Total, c.Total, "0.00");
            Line(sb, "Сверх бюджета", Mathf.Max(0f, a.Total - budget), Mathf.Max(0f, c.Total - budget), "0.00");
            Line(sb, "Покупок под воздействием", a.Manipulated, c.Manipulated, "0");
            Line(sb, "Время у продвигаемого стеллажа, с", sa?.Dwell(promotedShelf) ?? 0f, sc?.Dwell(promotedShelf) ?? 0f, "0");
            Line(sb, "Взято в руки", sa?.picks ?? 0, sc?.picks ?? 0, "0");
            Line(sb, "Возвращено на полку", sa?.returns ?? 0, sc?.returns ?? 0, "0");
            Line(sb, "Пройдено, м", sa?.pathLength ?? 0f, sc?.pathLength ?? 0f, "0.0");
            Line(sb, "Время захода, с", a.Duration, c.Duration, "0");
            sb.Append(new string('\n', 10)).Append("Заход 1: ").Append(string.Join(", ", a.receipt.Select(x => x.productId)));
            sb.Append("\nЗаход 2: ").Append(string.Join(", ", c.receipt.Select(x => x.productId)));
            sb.Append("\n\nРазница — ваш личный размер эффекта выкладки. Способ перемещения в обоих заходах один: ")
              .Append(controller.LockedProfile != null ? controller.LockedProfile.profileName : "—").Append('.');
            if (sa != null) Route(routeA, sa.route);
            if (sc != null) Route(routeC, sc.route);
            if (legend != null) legend.text = "<color=#ff8a3d>— заход 1</color>   <color=#4aa3ff>— заход 2</color>";
        }
        else
        {
            T = "Табло разбора";
            sb.Append("Здесь после первого захода появятся ваш маршрут, покупки\nи приёмы, которые на них действовали.");
            if (legend != null) legend.text = "";
        }
        if (title != null) title.text = T;
        if (body != null) body.text = sb.ToString();
        var cols = new[] { colName, colA, colC, colD };
        for (int i = 0; i < 4; i++) if (cols[i] != null) cols[i].text = tbl != null ? tbl[i].ToString() : "";
    }

    StringBuilder[] tbl;

    void Line(StringBuilder _, string name, float a, float c, string f)
    {
        float d = c - a;
        tbl[0].Append(name).Append('\n');
        tbl[1].Append(a.ToString(f, ci)).Append('\n');
        tbl[2].Append(c.ToString(f, ci)).Append('\n');
        tbl[3].Append(d > 0 ? "+" : "").Append(d.ToString(f, ci)).Append('\n');
    }

    void Route(LineRenderer lr, List<Vector3> pts)
    {
        if (lr == null) return;
        if (pts == null || pts.Count < 2) { lr.positionCount = 0; return; }
        lr.useWorldSpace = false;
        // прореживание до ≤ 400 точек
        int step = Mathf.Max(1, pts.Count / 400);
        var list = new List<Vector3>();
        for (int i = 0; i < pts.Count; i += step) list.Add(ToMap(pts[i]));
        list.Add(ToMap(pts[pts.Count - 1]));
        lr.positionCount = list.Count;
        lr.SetPositions(list.ToArray());
    }

    // мир (x, z) → плоскость табло (x, y); табло смотрит в зал, поэтому ось x развёрнута, как у зрителя
    public Vector3 ToMap(Vector3 w) => new Vector3((w.x - hallCenter.x) * mapScale * mapSign.x, (w.z - hallCenter.y) * mapScale * mapSign.y, -0.004f);
    public Vector2 mapSign = new Vector2(1f, 1f);
}
