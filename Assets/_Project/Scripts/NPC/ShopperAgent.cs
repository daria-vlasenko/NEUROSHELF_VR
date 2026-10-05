using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// NPC-покупатель. Ходит по NavMesh между «местами у полок» и подолгу стоит у них.
// В условии с социальным доказательством большинство мест выбирается у продвигаемого
// стеллажа — так у него образуется толпа. В нейтральном условии места равновероятны.
[RequireComponent(typeof(NavMeshAgent))]
public class ShopperAgent : MonoBehaviour
{
    public Animator animator;
    [Tooltip("Скорость, при которой шаг клипа Walk совпадает с перемещением, м/с")]
    public float walkClipSpeed = 1.2f;
    public Vector2 browseSeconds = new Vector2(4f, 9f);

    static readonly int IsMoving = Animator.StringToHash("IsMoving");
    static readonly int Speed = Animator.StringToHash("Speed");

    class Slot { public Vector3 pos, face; public Transform shelf; public ShopperAgent owner; }
    static List<Slot> slots;

    NavMeshAgent agent;
    ExperimentContext ctx;
    Slot current;
    bool browsing;
    float browseUntil;

    public bool IsBrowsing => browsing;
    public Transform CurrentShelf => current != null ? current.shelf : null;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        if (slots == null) BuildSlots();
        ctx = ExperimentContext.Find();
        if (ctx != null) ctx.ConditionChanged += OnCondition;
        if (animator != null) animator.Play("Idle", 0, Random.value);   // рассинхрон дыхания

        // стартуем «уже у полки»: занимаем ближайшее свободное место
        Slot best = null; float bd = float.MaxValue;
        foreach (var s in slots)
        {
            if (s.owner != null) continue;
            float d = (s.pos - transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = s; }
        }
        if (best != null && bd < 1f)
        {
            Claim(best);
            browsing = true;
            browseUntil = Time.time + Random.Range(browseSeconds.x, browseSeconds.y);
        }
        else ChooseNext();
    }

    void OnDestroy()
    {
        if (ctx != null) ctx.ConditionChanged -= OnCondition;
        Release();
        slots = null;   // при перезапуске сцены места пересобираются
    }

    void OnCondition(ExperimentConditionSO c)
    {
        // не уходят все одновременно — разброс до 2 с
        browsing = true;
        browseUntil = Time.time + Random.Range(0f, 2f);
    }

    void Update()
    {
        if (!agent.isOnNavMesh) return;
        float v = agent.velocity.magnitude;
        if (animator != null)
        {
            animator.SetBool(IsMoving, v > 0.08f);
            animator.SetFloat(Speed, Mathf.Clamp(v / walkClipSpeed, 0.5f, 1.5f));
        }

        if (!browsing)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
            {
                browsing = true;
                browseUntil = Time.time + Random.Range(browseSeconds.x, browseSeconds.y);
            }
            return;
        }

        if (current != null)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(current.face), 240f * Time.deltaTime);
        if (Time.time > browseUntil) ChooseNext();
    }

    void ChooseNext()
    {
        var c = ctx != null ? ctx.condition : null;
        var promo = ctx != null ? ctx.PromotedShelf : null;
        bool crowd = c != null && c.socialProofCrowd && promo != null;
        bool atPromo = crowd && Random.value < c.crowdShareAtPromo;

        var pool = slots.FindAll(s => s.owner == null && s != current &&
                                      (atPromo ? s.shelf == promo : (!crowd || s.shelf != promo)));
        if (pool.Count == 0) pool = slots.FindAll(s => s.owner == null && s != current);
        if (pool.Count == 0) { browseUntil = Time.time + 2f; return; }

        Release();
        Claim(pool[Random.Range(0, pool.Count)]);
        agent.SetDestination(current.pos);
        browsing = false;
    }

    void Claim(Slot s) { current = s; s.owner = this; }
    void Release() { if (current != null && current.owner == this) current.owner = null; }

    // 4 места перед лицевой стороной каждого стеллажа, 0.55 м от полок
    static void BuildSlots()
    {
        slots = new List<Slot>();
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!t.name.StartsWith("Shelf_0")) continue;
            foreach (var p in SlotPositions(t))
            {
                var q = p;
                if (NavMesh.SamplePosition(q, out var hit, 0.4f, NavMesh.AllAreas)) q = hit.position;
                slots.Add(new Slot { pos = q, face = -ShelfFront(t), shelf = t });
            }
        }
    }

    public static readonly float[] SlotOffsets = { -0.9f, -0.3f, 0.3f, 0.9f };
    public const float SlotDistance = 0.85f;   // от центра стеллажа (глубина 0.6 → 0.55 м от полок)

    public static IEnumerable<Vector3> SlotPositions(Transform shelf)
    {
        var front = ShelfFront(shelf);
        var right = Vector3.Cross(Vector3.up, front);
        foreach (var x in SlotOffsets) yield return shelf.position + front * SlotDistance + right * x;
    }

    public static Vector3 ShelfFront(Transform s)
    {
        var back = s.Find("Back");
        if (back != null && Mathf.Abs(back.localPosition.z) > 0.01f)
            return back.localPosition.z < 0 ? s.forward : -s.forward;
        return s.forward;
    }
}
