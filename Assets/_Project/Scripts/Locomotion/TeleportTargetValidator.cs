using UnityEngine;
using UnityEngine.AI;

// Единое правило проходимости для телепорта. Второй системы не заводим:
// используется NavMesh, запечённый в ЛР4 для покупателей (агент Humanoid, радиус 0.30 м).
// NavMesh уже «знает», где стеллажи, прилавок, витрина и стены, и отступает от них на радиус агента.
public enum TeleportVerdict { Ok, OffNavMesh, OccupiedByNpc, NoHit }

public static class TeleportTargetValidator
{
    public const int LayerNPC = 9;            // из ЛР5
    // Допуск поиска точки на NavMesh. NavMesh отступает от препятствий на 0.30 м (радиус агента),
    // капсула игрока — 0.25 м; допуск 0.05 м сводит обе границы к радиусу игрока.
    public const float NavMeshTolerance = 0.05f;
    public static float PlayerRadius = 0.25f;
    public static float PlayerHeight = 1.65f;

    public static TeleportVerdict Check(Vector3 point)
    {
        if (!NavMesh.SamplePosition(point, out var hit, NavMeshTolerance + 0.2f, NavMesh.AllAreas))
            return TeleportVerdict.OffNavMesh;
        var d = hit.position - point; d.y = 0f;
        if (d.magnitude > NavMeshTolerance || Mathf.Abs(hit.position.y - point.y) > 0.25f)
            return TeleportVerdict.OffNavMesh;
        if (Occupied(point)) return TeleportVerdict.OccupiedByNpc;
        return TeleportVerdict.Ok;
    }

    // Занято, если капсула игрока в точке приземления пересекается с капсулой NPC (слой NPC из ЛР5).
    public static bool Occupied(Vector3 floorPoint)
    {
        float r = PlayerRadius;
        var a = floorPoint + Vector3.up * (r + 0.05f);
        var b = floorPoint + Vector3.up * Mathf.Max(r + 0.05f, PlayerHeight - r);
        return Physics.CheckCapsule(a, b, r, 1 << LayerNPC, QueryTriggerInteraction.Ignore);
    }
}
