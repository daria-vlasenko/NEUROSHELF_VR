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

    [Header("Планограмма и цены (сценарий: смена A → C = смена планограммы)")]
    public PlanogramSO planogram;

    [Header("Эффект 7 — музыка в зале (North, Hargreaves & McKendrick, 1999; Milliman, 1982)")]
    [Tooltip("Фоновая музыка включена в этом условии")]
    public bool backgroundMusic;
    public AudioClip musicClip;
    [Tooltip("Темп трека, уд/мин — для протокола и анализа (темп — отдельная переменная по Milliman, 1982)")]
    public float musicTempoBpm;

    [Header("Звуковой якорь — пространственный стимул у стеллажа")]
    [Tooltip("Имя стеллажа, у которого звучит якорь (Shelf_03 …). Пусто — якоря нет.")]
    public string audioAnchorShelf = "";

    [Header("Тактильная метка усилия на нижнем ярусе 0.60 м")]
    [Tooltip("По умолчанию выключено: в настоящем магазине такой метки нет, она смешалась бы с эффектом яруса.")]
    public bool tierEffortHaptic;
}
