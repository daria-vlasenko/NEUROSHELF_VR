using UnityEngine;

// Корзина покупателя (ЛР5) как фиксированная точка отсчёта (rest frame) против укачивания.
//
// Работает поверх BodyAnchor из ЛР5 (его не меняем): BodyAnchor ставит корзину у пояса и лениво
// поворачивает за телом, а этот компонент выполняется после него и уточняет позицию:
//  1. Привязка к XR Origin, а не к миру. Когда XR Origin едет (стик), корзина едет с ним без
//     запаздывания — в поле зрения остаётся объект, неподвижный относительно тела. Сглаживается
//     только смещение головы в room-scale, чтобы корзина не дрожала за каждым движением головы.
//  2. Корзина вынесена вперёд, в нижнее поле зрения (параметры — в LocomotionProfileSO).
//  3. Если впереди стеллаж или стена, корзина отъезжает назад и не входит в геометрию.
[DefaultExecutionOrder(60)]   // после BodyAnchor (50)
public class BasketRestFrame : MonoBehaviour
{
    public Transform head;
    public Transform origin;
    public BodyAnchor bodyAnchor;
    [Tooltip("x — влево(−)/вправо(+), y — вперёд от головы, м")]
    public Vector2 offset = new Vector2(-0.22f, 0.42f);
    public float height = 0.95f;
    [Tooltip("Полуразмер корзины в плане (0.42 × 0.30 м из ЛР5) + зазор")]
    public Vector3 halfExtents = new Vector3(0.23f, 0.12f, 0.17f);
    public LayerMask obstacles = 1;   // Default: стены, стеллажи, прилавок
    public float headFollow = 12f;

    [Header("Отладка (только чтение)")]
    public float retracted;           // на сколько корзина отодвинута от препятствия, м

    Vector3 bodyLocal; bool init;

    void OnEnable() { init = false; }

    public void Configure(Vector2 off, float h)
    {
        offset = off; height = h;
        if (bodyAnchor != null) { bodyAnchor.offset = off; bodyAnchor.waistHeight = h; }
    }

    void LateUpdate()
    {
        if (head == null || origin == null) return;
        // проекция головы на пол в координатах XR Origin: движение XR Origin её не меняет
        var hl = origin.InverseTransformPoint(head.position); hl.y = 0f;
        if (!init) { bodyLocal = hl; init = true; }
        bodyLocal = Vector3.Lerp(bodyLocal, hl, 1f - Mathf.Exp(-headFollow * Time.deltaTime));
        var body = origin.TransformPoint(bodyLocal);

        // поворот берём у BodyAnchor (ленивый поворот за телом из ЛР5)
        var rot = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        var want = body + rot * new Vector3(offset.x, 0f, offset.y);
        want.y = origin.position.y + height;

        // отступ от препятствий: ведём «коробку» корзины от тела к желаемой точке
        var from = new Vector3(body.x, want.y + halfExtents.y, body.z);
        var to = want + Vector3.up * halfExtents.y;
        var dir = to - from; float len = dir.magnitude;
        retracted = 0f;
        if (len > 1e-3f && Physics.BoxCast(from, halfExtents, dir / len, out var hit, rot, len, obstacles, QueryTriggerInteraction.Ignore))
        {
            retracted = len - hit.distance;
            want = from + dir / len * Mathf.Max(0f, hit.distance - 0.01f) - Vector3.up * halfExtents.y;
        }
        transform.SetPositionAndRotation(want, rot);
    }
}
