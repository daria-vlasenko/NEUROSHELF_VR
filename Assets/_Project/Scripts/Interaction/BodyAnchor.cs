using UnityEngine;

// Держит корзину у пояса покупателя: по горизонтали следует за головой,
// по высоте стоит на уровне пояса (наклон головы к нижней полке корзину не роняет),
// а поворачивается лениво — только когда человек развернулся больше чем на yawThreshold.
// Иначе корзина моталась бы за каждым поворотом головы.
[DefaultExecutionOrder(50)]
public class BodyAnchor : MonoBehaviour
{
    public Transform head;
    [Tooltip("Пол (обычно XR Origin) — от него отсчитывается высота пояса")]
    public Transform floor;
    [Tooltip("x — вправо, z — вперёд от головы, м")]
    public Vector2 offset = new Vector2(-0.32f, 0.12f);
    [Tooltip("Высота дна корзины над полом, м")]
    public float waistHeight = 0.85f;
    public float yawThreshold = 35f;
    public float turnSpeed = 4f;
    public float followSpeed = 10f;

    float yaw;
    bool turning;

    void OnEnable()
    {
        if (head == null && Camera.main != null) head = Camera.main.transform;
        SnapNow();
    }

    void LateUpdate()
    {
        if (head == null) return;
        float d = Mathf.DeltaAngle(yaw, head.eulerAngles.y);
        if (Mathf.Abs(d) > yawThreshold) turning = true;
        if (turning)
        {
            yaw = Mathf.LerpAngle(yaw, head.eulerAngles.y, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, head.eulerAngles.y)) < 2f) turning = false;
        }
        var p = Vector3.Lerp(transform.position, Target(), 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
    }

    public void SnapNow()
    {
        if (head == null) return;
        yaw = head.eulerAngles.y;
        transform.SetPositionAndRotation(Target(), Quaternion.Euler(0f, yaw, 0f));
    }

    Vector3 Target()
    {
        var r = Quaternion.Euler(0f, yaw, 0f);
        var p = head.position + r * new Vector3(offset.x, 0f, offset.y);
        p.y = (floor != null ? floor.position.y : 0f) + waistHeight;
        return p;
    }
}
