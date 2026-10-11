using UnityEngine;

public enum LocomotionMode { Teleport, Smooth }
public enum TurnMode { Snap, Continuous }

// Профиль перемещения — параметр эксперимента, а не настройка удобства.
// Способ навигации меняет сами измеряемые величины (пройденное расстояние, время у стеллажа),
// поэтому он фиксируется отдельным ассетом и пишется в каждый CSV маршрута.
// Схема эксперимента 2 × 2: условие выкладки (ExperimentConditionSO: A / C) × профиль перемещения.
[CreateAssetMenu(menuName = "Neuroshelf/Locomotion Profile")]
public class LocomotionProfileSO : ScriptableObject
{
    public string profileName = "Teleport_Snap";
    [TextArea] public string description;

    [Header("Способ перемещения (один на сессию)")]
    public LocomotionMode mode = LocomotionMode.Teleport;

    [Header("Плавное перемещение")]
    [Tooltip("1.4 м/с — средняя скорость шага взрослого человека. Привязка к реальности: " +
             "только при ней пройденные метры сопоставимы с реальным магазином.")]
    [Range(0.5f, 3f)] public float moveSpeed = 1.4f;
    [Tooltip("Боковой шаг стиком. Включён: в магазине покупатель смещается вдоль полки боком.")]
    public bool strafe = true;

    [Header("Поворот (правый стик, в обоих режимах)")]
    public TurnMode turn = TurnMode.Snap;
    [Range(15f, 90f)] public float snapAngle = 45f;
    [Range(15f, 180f)] public float continuousTurnSpeed = 60f;

    [Header("Телепорт")]
    [Tooltip("Начальная скорость кривой Projectile. Задаёт максимальную дальность прицела.")]
    public float curveVelocity = 8.5f;
    [Tooltip("Ускорение кривой. Оставлено земным 9.8 м/с², чтобы дуга выглядела как бросок.")]
    public float curveAcceleration = 9.8f;
    [Tooltip("Свободное приземление на пол (кроме якорей). Выключить — только фиксированные точки обзора.")]
    public bool freeFloorTeleport = true;

    [Header("Виньетка: интенсивность по скорости (0 в покое)")]
    public bool vignette = true;
    [Tooltip("1 = максимальное сужение (апертура minAperture), 0 = виньетки нет")]
    [Range(0f, 1f)] public float vignetteStrength = 1f;
    [Tooltip("Апертура при полной интенсивности: доля поля зрения, остающаяся открытой. " +
             "0.7 — значение XRI по умолчанию; сильнее не сужаем: обзор полок — предмет измерения")]
    [Range(0.3f, 1f)] public float minAperture = 0.7f;
    [Tooltip("Сдвиг отверстия виньетки вниз (−) / вверх (+), м. Вниз — чтобы нижняя периферия, " +
             "где корзина-rest frame, оставалась открытой")]
    [Range(-0.2f, 0.2f)] public float vignetteVerticalShift = -0.12f;
    [Tooltip("Скорость перемещения, м/с: ниже — виньетки нет; на верхней границе — полная")]
    public Vector2 linearSpeedRange = new Vector2(0.10f, 1.40f);
    [Tooltip("Угловая скорость, °/с: ниже — виньетки нет; на верхней границе — полная")]
    public Vector2 angularSpeedRange = new Vector2(10f, 90f);
    [Tooltip("Время сглаживания нарастания и спада, с")]
    public float vignetteSmoothing = 0.15f;

    [Header("Корзина — фиксированная точка отсчёта (rest frame)")]
    [Tooltip("x — влево(−)/вправо(+), y — вперёд от головы, м")]
    public Vector2 basketOffset = new Vector2(-0.22f, 0.42f);
    [Tooltip("Высота дна корзины над полом, м")]
    public float basketHeight = 0.95f;

    [Header("Запись маршрута")]
    [Tooltip("Интервал записи позиции, с (концепт, §4.4: 0.2 с)")]
    public float logInterval = 0.2f;
}
