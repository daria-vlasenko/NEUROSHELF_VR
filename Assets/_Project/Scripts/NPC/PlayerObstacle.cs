using UnityEngine;

// Невидимое препятствие на месте игрока: NavMeshAgent-ы обходят его, а не проходят сквозь.
// Сам XR Origin не трогаем — объект просто повторяет позицию камеры по горизонтали.
public class PlayerObstacle : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var p = cam.transform.position;
        transform.position = new Vector3(p.x, transform.position.y, p.z);
    }
}
