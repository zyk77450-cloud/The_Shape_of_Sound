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
    [Header("伤害：1=掉血刺（带击退），99=即死")]
    public int damage = 99;

    [Header("击退（掉血型用；即死型用不到）")]
    public float knockbackX = 8f;
    public float knockbackY = 6f;

    [Header("兜底重生点（仅玩家没挂 PlayerHealth2D 时使用）")]
    public Transform spawnPoint;

    [Header("事件（可拖 ClockProgress.ResetProgress 等惩罚）")]
    public UnityEvent onHit;

    [Header("兜底路径的防重复触发间隔")]
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