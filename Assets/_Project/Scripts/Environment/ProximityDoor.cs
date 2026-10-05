using UnityEngine;

// Дверца витрины: игрок подошёл ближе openDistance → триггер Open, отошёл → триггер Close.
// Сама анимация — клипы Door_Open / Door_Close в Animator Controller AC_Door.
public class ProximityDoor : MonoBehaviour
{
    public Animator animator;
    public float openDistance = 1.8f;
    [Tooltip("Ручное управление (для кадров): расстояние до игрока игнорируется")]
    public bool manual;

    static readonly int Open = Animator.StringToHash("Open");
    static readonly int Close = Animator.StringToHash("Close");
    bool isOpen;

    public bool IsOpen => isOpen;

    void Update()
    {
        if (manual || Camera.main == null) return;
        var d = Camera.main.transform.position - transform.position; d.y = 0;
        SetOpen(d.magnitude < openDistance);
    }

    public void SetOpen(bool open)
    {
        if (open == isOpen) return;
        isOpen = open;
        animator.ResetTrigger(open ? Close : Open);
        animator.SetTrigger(open ? Open : Close);
    }
}
