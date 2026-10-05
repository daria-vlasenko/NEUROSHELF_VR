using UnityEngine;

// Вращающийся рекламный стенд. Вращение — зацикленный клип AdStand_Spin в AC_AdStand,
// скорость задаётся параметром SpinSpeed. В нейтральном условии стенд плавно останавливается:
// движение — тоже независимая переменная (движущийся объект захватывает внимание).
public class AdStandMotion : MonoBehaviour
{
    public Animator animator;
    public float acceleration = 0.5f;   // доля полной скорости в секунду
    static readonly int SpinSpeed = Animator.StringToHash("SpinSpeed");
    ExperimentContext ctx;

    void Start() { ctx = ExperimentContext.Find(); }

    void Update()
    {
        var c = ctx != null ? ctx.condition : null;
        float target = c == null || c.adStandMotion ? 1f : 0f;
        float cur = animator.GetFloat(SpinSpeed);
        animator.SetFloat(SpinSpeed, Mathf.MoveTowards(cur, target, acceleration * Time.deltaTime));
    }
}
