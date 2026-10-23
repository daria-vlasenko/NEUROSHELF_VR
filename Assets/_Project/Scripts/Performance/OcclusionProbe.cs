using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;

// Проверка окклюзии числом, а не картинкой: из двух поз (вдоль прохода и поперёк рядов) считается,
// сколько объектов реально отрисовано камерой (Renderer.isVisible после кадра) с Occlusion Culling и без.
// Отдельно — по группам: статика окружения, товары (динамические, с Rigidbody), персонажи (скиннинг).
// Так экспериментально проверяется, отсекаются ли динамические объекты, которые не помечены Static.
public static class OcclusionProbe
{
    public struct Pose { public string name; public Vector2 xz; public float yaw, pitch; }
    public static readonly Pose[] Poses =
    {
        new Pose { name = "вдоль прохода (стартовая отметка → запад)", xz = new Vector2(4.30f, 0.35f), yaw = 270f, pitch = 5f },
        new Pose { name = "поперёк рядов (за Shelf_03/04 → север)",   xz = new Vector2(0.00f, -3.40f), yaw = 0f, pitch = 5f },
    };

    public class Row { public string pose; public bool occl; public int all, env, products, npc, other; public long draws; public List<string> visProducts, visNpc; public List<string> dump; }

    public static IEnumerator Measure(BenchRig.Rig rig, List<Row> rows)
    {
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => !r.transform.IsChildOf(rig.xr.transform) && !(r is LineRenderer)).ToArray();
        var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 4);
        bool keep = rig.cam.useOcclusionCulling;
        foreach (var p in Poses)
            foreach (var occl in new[] { false, true })
            {
                rig.cam.useOcclusionCulling = occl;
                for (int i = 0; i < 20; i++) { BenchRig.Pose(rig, p.xz, p.yaw, p.pitch); yield return null; }
                var vis = rs.Where(r => r != null && r.enabled && r.isVisible).ToArray();
                bool IsProduct(Renderer r) => r.GetComponentInParent<ProductItem>() != null;
                bool IsNpc(Renderer r) => r is SkinnedMeshRenderer || r.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null || r.GetComponentInParent<PromoterIK>() != null;
                bool IsEnv(Renderer r) => r.lightmapIndex >= 0 && r.lightmapIndex < 65534;   // запечённая статика окружения (isStatic в сборке недоступен)
                rows.Add(new Row
                {
                    pose = p.name, occl = occl, all = vis.Length,
                    env = vis.Count(IsEnv), products = vis.Count(IsProduct), npc = vis.Count(IsNpc),
                    other = vis.Count(r => !IsEnv(r) && !IsProduct(r) && !IsNpc(r)),
                    draws = draws.Valid ? draws.LastValue : -1,
                    visProducts = vis.Where(IsProduct).Select(r => r.GetComponentInParent<ProductItem>().productId).Distinct().OrderBy(x => x).ToList(),
                    visNpc = vis.Where(IsNpc).Select(r => r.transform.root.name == "NPC" ? r.transform.parent.name : r.transform.parent.name).Distinct().ToList(),
                    // для схемы сверху: габарит каждого объекта в плане и отрисован ли он
                    dump = rs.Where(r => r != null && r.enabled).Select(r => string.Format(CultureInfo.InvariantCulture, "{0};{1:0.00};{2:0.00};{3:0.00};{4:0.00};{5};{6}",
                        r.name, r.bounds.min.x, r.bounds.min.z, r.bounds.max.x, r.bounds.max.z, r.isVisible ? 1 : 0,
                        IsProduct(r) ? "product" : IsNpc(r) ? "npc" : IsEnv(r) ? "env" : "other")).ToList(),
                });
            }
        rig.cam.useOcclusionCulling = keep;
        draws.Dispose();
    }

    public static string Report(List<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("поза | Occlusion Culling | отрисовано объектов | окружение (Static) | товары (динамические) | персонажи | прочее | вызовов отрисовки");
        foreach (var r in rows)
            sb.AppendLine($"{r.pose} | {(r.occl ? "вкл" : "выкл")} | {r.all} | {r.env} | {r.products} | {r.npc} | {r.other} | {r.draws}");
        sb.AppendLine();
        foreach (var r in rows)
            sb.AppendLine($"{r.pose}, окклюзия {(r.occl ? "вкл" : "выкл")}: товары [{string.Join(", ", r.visProducts)}]; персонажи [{string.Join(", ", r.visNpc)}]");
        return sb.ToString();
    }

    public static string Dump(List<Row> rows)
    {
        var sb = new StringBuilder("pose;occl;name;minx;minz;maxx;maxz;visible;kind\n");
        for (int i = 0; i < rows.Count; i++)
            foreach (var d in rows[i].dump) sb.Append(i / 2).Append(';').Append(rows[i].occl ? 1 : 0).Append(';').Append(d).Append('\n');
        return sb.ToString();
    }
}
