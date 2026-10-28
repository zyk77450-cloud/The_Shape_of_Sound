using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 幻影吊桥：玩家靠近后，一块一块往下塌。
///
/// 用法：
///   1. 建一个空物体当"父物体"，挂上这个脚本；
///   2. 在它下面摆 N 个小块，每块自己带 SpriteRenderer + BoxCollider2D（非 Trigger）；
///   3. 每块的 Layer 必须是 Ground，否则玩家站不住；
///   4. 父物体本身不要挂碰撞体（脚本只负责管理子物体）。
/// </summary>
public class PhantomBridge : MonoBehaviour
{
    [Header("感应")]
    [Tooltip("玩家进入这个半径（单位：格）就开始塌。\n调大=离得远就开始塌，调小=要贴近才塌。\n平台很宽时要大于它的一半宽度。推荐 3~5")]
    public float senseRadius = 3.5f;

    [Tooltip("感应圆心相对本物体的偏移（单位：格）。\n只移动那个青色圈，桥本身不动。\n往左挪填负数如 (-4,0)，往右挪填正数，往上挪第二个数填正数。\n推荐 (0,0)；想提前塌就往玩家来的方向挪 3~4 格")]
    public Vector2 senseOffset = Vector2.zero;

    [Tooltip("塌陷方向。\n0 = 自动（从玩家所在的那一侧开始塌）\n1 = 永远从左往右塌\n-1 = 永远从右往左塌")]
    public int forceDirection = 0;

    [Header("塌陷节奏")]
    [Tooltip("相邻两块塌陷之间隔多少秒。\n调大=塌得慢，玩家容易跑过去；调小=塌得快，更吓人。\n参考：0.07 秒 ≈ 14 格/秒，比玩家跑速(13格/秒)快一点。推荐 0.05~0.1")]
    public float stepDelay = 0.07f;

    [Tooltip("每一块消失前先抖动多少秒（预警）。\n调大=给玩家更多反应时间，调小=更突然。推荐 0.15~0.25")]
    public float segmentWarnTime = 0.18f;

    [Tooltip("抖动幅度（单位：格）。\n调大=抖得厉害，调小=轻微颤。推荐 0.03~0.06")]
    public float shakeMagnitude = 0.04f;

    [Header("恢复")]
    [Tooltip("整条桥塌完后，过多少秒全部重新出现（让玩家能重试）。\n调大=更久才恢复，调小=更快重来。推荐 2~3")]
    public float respawnDelay = 2.5f;

    // 一块"砖"的缓存（位置、渲染、碰撞）
    class Seg
    {
        public Transform t;
        public SpriteRenderer sr;
        public BoxCollider2D col;
        public Vector3 home;
    }

    readonly List<Seg> segs = new List<Seg>();
    Transform player;
    bool running;

    /// <summary>感应圆心的世界坐标 = 父物体位置 + 偏移</summary>
    Vector2 SenseCenter
    {
        get { return (Vector2)transform.position + senseOffset; }
    }

    void Awake()
    {
        // 自动收集所有子物体当作"砖块"，并按 x 从左到右排好序
        foreach (Transform child in transform)
        {
            SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
            BoxCollider2D col = child.GetComponent<BoxCollider2D>();
            if (sr == null || col == null) continue;   // 没有渲染或碰撞的跳过

            segs.Add(new Seg
            {
                t = child,
                sr = sr,
                col = col,
                home = child.position
            });
        }

        segs.Sort((a, b) => a.t.position.x.CompareTo(b.t.position.x));
    }

    void Start()
    {
        // 自动找 Tag 为 Player 的物体
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
    }

    void Update()
    {
        if (running || player == null || segs.Count == 0) return;

        // 用"感应圆心"到玩家的距离判断是否靠近
        if (Vector2.Distance(SenseCenter, player.position) <= senseRadius)
            StartCoroutine(CollapseRoutine());
    }

    IEnumerator CollapseRoutine()
    {
        running = true;

        // 决定塌陷方向
        bool fromLeft;
        if (forceDirection > 0)       fromLeft = true;    // 强制 左 → 右
        else if (forceDirection < 0)  fromLeft = false;   // 强制 右 → 左
        else                          fromLeft = player.position.x < SenseCenter.x; // 自动：从玩家那一侧开始

        for (int i = 0; i < segs.Count; i++)
        {
            int idx = fromLeft ? i : segs.Count - 1 - i;   // 取"离玩家最近的那一块"
            StartCoroutine(CollapseSegment(segs[idx]));
            yield return new WaitForSeconds(stepDelay);    // 一块接一块，错开时间
        }

        // 等所有块塌完 + 冷却
        yield return new WaitForSeconds(respawnDelay);

        // 整条一起回来（解谜可重试）
        for (int i = 0; i < segs.Count; i++)
        {
            Seg s = segs[i];
            s.t.position = s.home;
            s.col.enabled = true;
            s.sr.enabled = true;
        }

        running = false;
    }

    IEnumerator CollapseSegment(Seg s)
    {
        // 先抖一下：告诉玩家"这块要没了"
        float t = 0f;
        while (t < segmentWarnTime)
        {
            t += Time.deltaTime;
            s.t.position = s.home + (Vector3)(Random.insideUnitCircle * shakeMagnitude);
            yield return null;
        }
        s.t.position = s.home;

        // 消失：碰撞和显示一起关，踩在上面的人直接掉下去
        s.col.enabled = false;
        s.sr.enabled = false;
    }

    /// <summary>
    /// 强制复位（供外部调用，比如玩家重生时）。
    /// </summary>
    public void ResetBridge()
    {
        StopAllCoroutines();
        for (int i = 0; i < segs.Count; i++)
        {
            Seg s = segs[i];
            s.t.position = s.home;
            s.col.enabled = true;
            s.sr.enabled = true;
        }
        running = false;
    }

    // 选中父物体时，在 Scene 视图画出感应范围
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.35f);
        Gizmos.DrawWireSphere(SenseCenter, senseRadius);
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        Gizmos.DrawLine(transform.position, SenseCenter);
    }
}
