using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// NPC-промоутер. Когда игрок подходит ближе noticeDistance:
//   1) корпус поворачивается к игроку (но не дальше maxBodyToProductAngle от товара);
//   2) голова смотрит на игрока — Multi-Aim Constraint;
//   3) правая рука указывает на продвигаемый товар — Two Bone IK Constraint.
// Цель IK не задана руками: она каждый кадр вычисляется из текущего условия эксперимента
// (ExperimentContext → promotedProduct). Сменили условие — рука сама показывает на другой товар.
// Вес ограничений плавно меняет Animator Controller (слой «IK Weights»), а не этот скрипт.
public class PromoterIK : MonoBehaviour
{
    [Header("Ссылки (заполняет NEUROSHELF → Анимация)")]
    public Animator animator;
    public TwoBoneIKConstraint armIK;
    public MultiAimConstraint headAim;
    public Transform ikTarget, ikHint, lookTarget;
    public LineRenderer pointerRay;

    [Header("Поведение")]
    public float noticeDistance = 4.5f;
    public float maxBodyToProductAngle = 75f;
    public float turnSpeed = 120f;      // град/с
    [Range(0.8f, 1f)] public float reach = 0.97f;   // доля длины руки: почти прямая рука
    public bool showDebug;              // куб-цель IK и луч от кисти к товару (для кадров)
    public Transform playerOverride;

    static readonly int PlayerNear = Animator.StringToHash("PlayerNear");
    static readonly int PointTrigger = Animator.StringToHash("Point");
    const int GestureLayer = 1;

    Transform shoulder, elbow, hand, finger;
    float armLength;
    Quaternion handOffset = Quaternion.identity;
    bool hasOffset, wasNear;
    ExperimentContext ctx;
    Renderer targetRenderer;

    // диагностика для протокола
    [NonSerialized] public bool dbgNear, dbgActive;
    [NonSerialized] public float dbgDist, dbgSetWeight;
    [NonSerialized] public string dbgProduct;
    [NonSerialized] public Vector3 dbgSetTarget;
    [NonSerialized] public float dbgUpdateRead, dbgLateRead;

    void LateUpdate() { if (armIK != null) dbgLateRead = armIK.weight; }

    public Transform Hand => hand;
    public Transform Finger => finger;
    public float ArmLength => armLength;

    void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        elbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        finger = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal)
              ?? animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        armLength = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, hand.position);
        ComputeHandOffset();
        if (ikTarget != null) targetRenderer = ikTarget.GetComponent<Renderer>();
        animator.SetLayerWeight(GestureLayer, 0f);
    }

    void Start() { ctx = ExperimentContext.Find(); }

    // Как повернуть кисть, чтобы пальцы смотрели вдоль луча.
    // Берём позу привязки (T-pose) из bindposes меша: в ней пальцы вытянуты вдоль руки,
    // ладонь вниз. Запоминаем поворот кисти относительно направления «кисть → палец».
    void ComputeHandOffset()
    {
        if (finger == null) return;
        var smr = GetComponentsInChildren<SkinnedMeshRenderer>()
            .FirstOrDefault(s => Array.IndexOf(s.bones, hand) >= 0 && Array.IndexOf(s.bones, finger) >= 0);
        if (smr == null) return;
        var bp = smr.sharedMesh.bindposes;
        var mh = bp[Array.IndexOf(smr.bones, hand)].inverse;
        var mf = bp[Array.IndexOf(smr.bones, finger)].inverse;
        var meshRot = smr.transform.rotation;
        Vector3 dir = meshRot * ((Vector3)mf.GetColumn(3) - (Vector3)mh.GetColumn(3));
        Quaternion handRot = meshRot * mh.rotation;
        if (dir.sqrMagnitude < 1e-8f) return;
        handOffset = Quaternion.Inverse(Quaternion.LookRotation(dir.normalized, transform.up)) * handRot;
        hasOffset = true;
    }

    void Update()
    {
        var player = playerOverride != null ? playerOverride : (Camera.main != null ? Camera.main.transform : null);
        var c = ctx != null ? ctx.condition : null;
        var product = ctx != null ? ctx.PromotedProduct : null;

        bool active = c != null && c.promoterPointing && product != null && player != null;
        bool near = active && Flat(player.position - transform.position).magnitude < noticeDistance;

        // Animator: параметр-булева и триггер
        animator.SetBool(PlayerNear, near);
        if (near && !wasNear) animator.SetTrigger(PointTrigger);
        wasNear = near;
        dbgActive = active; dbgNear = near; dbgProduct = product != null ? product.name : "null";
        dbgDist = player != null ? Flat(player.position - transform.position).magnitude : -1f;

        // 1. Корпус к игроку, не дальше допустимого угла от товара
        if (near)
        {
            Vector3 face = Flat(player.position - transform.position);
            Vector3 toProd = Flat(ExperimentContext.PointOf(product) - transform.position);
            float a = Vector3.SignedAngle(toProd, face, Vector3.up);
            if (Mathf.Abs(a) > maxBodyToProductAngle)
                face = Quaternion.AngleAxis(Mathf.Sign(a) * maxBodyToProductAngle, Vector3.up) * toProd;
            if (face.sqrMagnitude > 1e-4f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(face), turnSpeed * Time.deltaTime);
        }

        // 2. Взгляд — на голову игрока
        if (player != null && lookTarget != null) lookTarget.position = player.position;

        // 3. Цель IK — точка на луче «плечо → товар» на длине почти прямой руки
        if (product != null)
        {
            Vector3 p = ExperimentContext.PointOf(product);
            Vector3 s = shoulder.position;
            Vector3 dir = (p - s).normalized;
            ikTarget.position = s + dir * armLength * reach;
            if (hasOffset) ikTarget.rotation = Quaternion.LookRotation(dir, transform.up) * handOffset;
            // подсказка локтя: чуть вниз и наружу, чтобы локоть не выворачивался
            ikHint.position = s + dir * armLength * 0.5f - transform.up * 0.25f + transform.right * 0.2f;
        }

        // 4. Вес IK задаёт сам Animator (слой «IK Weights», параметр PlayerNear, переход 0.4 с).
        //    Здесь только синхронизируем с ним вес слоя жеста (пальцы из клипа Point).
        float w = armIK.weight;
        dbgUpdateRead = w; dbgSetWeight = w; dbgSetTarget = ikTarget.position;
        animator.SetLayerWeight(GestureLayer, w);

        if (targetRenderer != null) targetRenderer.enabled = showDebug;
        if (pointerRay != null)
        {
            pointerRay.enabled = showDebug && product != null && w > 0.5f;
            if (pointerRay.enabled)
            {
                pointerRay.SetPosition(0, hand.position);
                pointerRay.SetPosition(1, ExperimentContext.PointOf(product));
            }
        }
    }

    // Метрики для протокола: насколько точно рука показывает на товар
    public void Measure(out float handToTarget, out float pointingErrorDeg)
    {
        var product = ctx != null ? ctx.PromotedProduct : null;
        handToTarget = Vector3.Distance(hand.position, ikTarget.position);
        pointingErrorDeg = -1f;
        if (product == null) return;
        Vector3 armDir = (hand.position - shoulder.position).normalized;
        Vector3 toProd = (ExperimentContext.PointOf(product) - shoulder.position).normalized;
        pointingErrorDeg = Vector3.Angle(armDir, toProd);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
