using System.Collections;
using UnityEngine;

/// <summary>
/// 破碎平台：玩家从【上方】踩上 → 停顿 → 抖动 → 碎掉（关碰撞关显示）→ 定时重生。
/// 侧面撞、从下面顶，都不算踩，不会触发。
/// 需要实心 BoxCollider2D（非 Trigger），Layer 建议 Ground。
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CrumblingPlatform : MonoBehaviour
{
    [Header("时序")]
    [Tooltip("踩上后停顿多久才开始抖动（秒）。调大 = 玩家有更多反应时间。推荐 0.3~0.8。")]
    public float shakeDelay = 0.5f;

    [Tooltip("抖动持续多久后碎掉（秒）。这就是玩家仅有的逃跑时间。推荐 0.3~0.6。")]
    public float shakeDuration = 0.45f;

    [Tooltip("碎掉后多久重新出现（秒）。设 0 = 永不重生。推荐 2~3。")]
    public float respawnDelay = 3f;

    [Header("抖动")]
    [Tooltip("抖动幅度（格）。0.06 ≈ 1 像素。调大更有危机感，但太大会看不清平台位置。推荐 0.04~0.10。")]
    public float shakeMagnitude = 0.06f;

    [Header("触发条件")]
    [Tooltip("判定\"踩在上面\"的容差（格）。玩家脚底只要不低于平台顶面超过这个值就算踩上。推荐 0.1~0.2。")]
    public float topContactTolerance = 0.15f;

    [Tooltip("勾上 = 玩家正在向上运动（从下面顶上来）时不触发。一般保持勾选。")]
    public bool ignoreWhenMovingUp = true;

    BoxCollider2D col;
    SpriteRenderer sr;
    Vector3 homePos;
    Coroutine breaking;

    void Awake()
    {
        col = GetComponent<BoxCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        homePos = transform.position;
    }

    void OnCollisionStay2D(Collision2D c)
    {
        if (breaking != null) return;                        // 已经在碎的过程里
        if (!c.collider.CompareTag("Player")) return;
        if (!IsStandingOnTop(c.collider)) return;            // 只有从上面踩才算

        if (ignoreWhenMovingUp && c.rigidbody != null && c.rigidbody.velocity.y > 0.5f)
            return;                                          // 正在上升（顶到下面）→ 不算踩

        breaking = StartCoroutine(BreakRoutine());
    }

    /// <summary>
    /// 玩家是不是站在平台【顶面】上：
    /// 用脚底（碰撞体下沿）和平台顶面比，而不是用两个物体的中心比。
    /// </summary>
    bool IsStandingOnTop(Collider2D playerCol)
    {
        float platformTop = col.bounds.max.y;      // 平台顶面
        float playerFeet = playerCol.bounds.min.y; // 玩家脚底
        return playerFeet >= platformTop - topContactTolerance;
    }

    IEnumerator BreakRoutine()
    {
        yield return new WaitForSeconds(shakeDelay);

        // 抖动：随机小偏移，视觉预警
        float t = 0f;
        while (t < shakeDuration)
        {
            t += Time.deltaTime;
            transform.position = homePos + (Vector3)(Random.insideUnitCircle * shakeMagnitude);
            yield return null;
        }
        transform.position = homePos;

        // 碎：碰撞和显示一起关，玩家踩空
        col.enabled = false;
        sr.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        // 重生：解谜可重试
        col.enabled = true;
        sr.enabled = true;
        breaking = null;
    }
}
