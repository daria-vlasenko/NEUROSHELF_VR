using System;
using UnityEngine;

// Текущее условие эксперимента — единственный источник правды для NPC и манипуляторов.
// Кто должен реагировать на смену условия, подписывается на ConditionChanged.
[DefaultExecutionOrder(-100)]
public class ExperimentContext : MonoBehaviour
{
    public ExperimentConditionSO condition;

    public static ExperimentContext Instance { get; private set; }
    public event Action<ExperimentConditionSO> ConditionChanged;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public static ExperimentContext Find() =>
        Instance != null ? Instance : FindFirstObjectByType<ExperimentContext>();

    public void SetCondition(ExperimentConditionSO c)
    {
        condition = c;
        ConditionChanged?.Invoke(c);
    }

    public Transform PromotedProduct => Named(condition != null ? condition.promotedProduct : null);
    public Transform PromotedShelf => Named(condition != null ? condition.promotedShelf : null);

    // Центр товара в мире. У моделей из FBX точка привязки не совпадает с геометрией,
    // поэтому берём центр габаритов меша, а не transform.position.
    public static Vector3 PointOf(Transform t)
    {
        if (t == null) return Vector3.zero;
        var r = t.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center : t.position;
    }

    static Transform Named(string n)
    {
        if (string.IsNullOrEmpty(n)) return null;
        var g = GameObject.Find(n);
        return g != null ? g.transform : null;
    }
}
