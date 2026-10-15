using UnityEngine;

// Точка съёмки кадра для отчёта (используется прогоном сценария, меню 9.7). На игру не влияет.
public class ScenarioShotPoint : MonoBehaviour
{
    [Range(20f, 100f)] public float fov = 60f;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawFrustum(Vector3.zero, fov, 0.6f, 0.05f, 16f / 9f);
    }
}
