using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 陷阱（齿轮/尖刺/深坑）：碰到 → 走 PlayerHealth2D 的死亡流程
/// （掉血/定格/回到上一次起跳的位置重生/无敌帧/事件全部生效）。
/// damage 填 1 = 掉血刺（带击退），填 99 = 碰到即死（齿轮、深坑）。
/// 玩家身上没挂 PlayerHealth2D 时，退化为原版行为：直接传送回重生点。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Hazard2D : MonoBehaviour
{
    [Header("伤害")]
    [Tooltip("1 = 掉血刺（扣血 + 击退，玩家还能活）\n99 = 碰到即死（齿轮、深坑）\n推荐：尖刺填 1，齿轮和深坑填 99")]
    public int damage = 99;

    [Header("击退（只有 damage=1 的掉血型会用到）")]
    [Tooltip("被扎后往侧面弹开的速度（单位：格/秒）。\n调大 = 弹得更远；调小 = 只轻轻一推。\n推荐 6~10")]
    public float knockbackX = 8f;

    [Tooltip("被扎后往上弹的速度（单位：格/秒）。\n调大 = 弹得更高；调小 = 弹不起来。\n推荐 4~7")]
    public float knockbackY = 6f;

    [Header("兜底重生点（只在玩家没挂 PlayerHealth2D 时用）")]
    [Tooltip("留空即可。\n只有玩家身上没挂生命值脚本时，才会退回到这里指定的位置复活。\n一般不用填，脚本会自动去找名叫 Spawn 的物体。")]
    public Transform spawnPoint;

    [Header("事件")]
    [Tooltip("受伤时额外触发的事情，可以在这里拖入别的物体上的方法。\n例如拖 ClockProgress 的 ResetProgress，做成「被扎就清空进度」的惩罚。\n不填就什么都不额外发生。")]
    public UnityEvent onHit;

    [Header("兜底路径的防重复触发间隔")]
    [Tooltip("只有走「兜底传送」流程时才会用到，防止一帧触发好几次。\n调大 = 触发后更久才允许再次触发；调小 = 更容易连续触发。\n推荐保持 0.5")]
    public float cooldown = 0.5f;

    float timer;

    void Update() { if (timer > 0f) timer -= Time.deltaTime; }

    void OnTriggerEnter2D(Collider2D other) { TryHit(other); }
    void OnTriggerStay2D(Collider2D other)  { TryHit(other); }

    void TryHit(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // ---- 主路径：走生命系统（无敌帧自动节流，不需要自己的冷却）----
        var health = other.GetComponent<PlayerHealth2D>();
        if (health != null)
        {
            float dir = Mathf.Sign(other.transform.position.x - transform.position.x);
            if (dir == 0f) dir = 1f;
            health.TakeDamage(damage, new Vector2(dir * knockbackX, knockbackY));
            onHit.Invoke();
            return;
        }

        // ---- 兜底：没生命系统就按原版，传送回重生点 ----
        if (timer > 0f) return;
        Transform target = spawnPoint != null ? spawnPoint : FindSpawn();
        if (target == null)
        {
            Debug.LogWarning("Hazard2D：玩家没挂 PlayerHealth2D，也没找到重生点（spawnPoint 或名为 Spawn 的物体）。");
            return;
        }
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb != null) { rb.velocity = Vector2.zero; rb.position = target.position; }
        else other.transform.position = target.position;
        timer = cooldown;
    }

    static Transform FindSpawn()
    {
        GameObject go = GameObject.Find("Spawn");
        return go != null ? go.transform : null;
    }
}
