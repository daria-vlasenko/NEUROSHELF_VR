using UnityEngine;

// Условие эксперимента — набор независимых переменных одной фазы.
// Переключение фазы A / C = подстановка другого ассета, без правки кода и сцены.
// Здесь пока только переменные, которые используют NPC и анимированный стенд.
[CreateAssetMenu(menuName = "Neuroshelf/Experiment Condition")]
public class ExperimentConditionSO : ScriptableObject
{
    public string conditionName = "A_Manipulated";
    [TextArea] public string description;

    [Header("Что продвигается (имена объектов в сцене)")]
    public string promotedProduct = "Can_VOLTA";
    public string promotedShelf = "Shelf_03";

    [Header("Эффект 6 — социальное доказательство")]
    public bool socialProofCrowd = true;
    [Range(0f, 1f)] public float crowdShareAtPromo = 0.85f;

    [Header("Промоутер: жест на товар через Two Bone IK")]
    public bool promoterPointing = true;

    [Header("Движение рекламного стенда (захват внимания движением)")]
    public bool adStandMotion = true;
}
