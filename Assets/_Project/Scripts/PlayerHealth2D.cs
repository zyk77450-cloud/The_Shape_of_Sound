using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 玩家生命值 + 死亡重生。
/// 持续记录"最后一次站在地面上的位置"（= 上一次起跳的位置），
/// 死亡后黑幕重生到那里，满血复活并附带短暂无敌（闪烁）。
/// TakeDamage(int) 可被陷阱直接调用，也可在 Inspector 拖线。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class PlayerHealth2D : MonoBehaviour
{
    [Header("生命")]
    public int maxHealth = 3;
    public int Current { get; private set; }
    
    [Header("重生点安全检查")]
    [Tooltip("危险物所在的层，一般勾 Hazard。\n作用：重生前检查\"上次站的地方\"附近有没有陷阱，有就改回出生点，防止重生即死。")]
    public LayerMask hazardLayer;             // 只勾 Hazard
    public float hazardCheckRadius = 0.8f;    // 检查半径（约玩家半身）
    public Transform fallbackSpawn;           // 终极兜底出生点；留空自动找名为 Spawn 的物体

    [Header("无敌与重生")]
    public float invincibleTime = 1f;          // 受伤后的无敌时长（闪烁）
    public float respawnInvincibleTime = 1.5f; // 重生后的无敌时长
    public float deathDelay = 0.6f;            // 死亡定格多久后开始黑幕
    public float respawnLiftUp = 0.2f;         // 重生点向上抬一点，防卡地里

    [Header("地面记录（与 PlayerController 的设置保持一致）")]
    [Tooltip("地面所在的层，一般勾 Ground。\n留空也可以：会自动跟随 PlayerController2D 上已经配好的 Ground 层。\n⚠️ 如果两者都是空，重生点会一直停在出生点（表现为\"每次都重生在很远的地方\"）。")]
    public LayerMask groundLayer;              // 只勾 Ground
    public float groundCastDistance = 0.1f;

    [Header("事件（拖音效/UI）")]
    public UnityEvent onDamaged;
    public UnityEvent onDied;
    public UnityEvent onRespawned;

    Rigidbody2D rb;
    Collider2D col;
    SpriteRenderer sr;
    PlayerController2D pc;
    ContactFilter2D groundFilter;
    readonly RaycastHit2D[] hits = new RaycastHit2D[4];

    Vector3 lastGroundedPos;
    float iframesTimer;
    bool dead;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr = GetComponent<SpriteRenderer>();
        pc = GetComponent<PlayerController2D>();

        // ---- v14: Layer 自动兜底，避免忘记在 Inspector 里配 ----
        // 症状：Ground Layer 留空 → 每帧检测不到地面 → 重生点永远停在出生点
        if (groundLayer.value == 0 && pc != null)
            groundLayer = pc.groundLayer;                  // 跟随控制器的 Ground 层
        if (hazardLayer.value == 0)
            hazardLayer = LayerMask.GetMask("Hazard");      // 自动找名为 Hazard 的层
        if (groundLayer.value == 0)
            Debug.LogWarning("[重生] Ground Layer 为 None，重生点会一直停在出生点。" +
                             "请给 PlayerHealth2D 的 Ground Layer 勾上 Ground（或确认 PlayerController2D 里配了）。");

        groundFilter.useLayerMask = true;
        groundFilter.layerMask = groundLayer;
        groundFilter.useTriggers = false;
        Current = maxHealth;
        lastGroundedPos = transform.position;   // 出生点兜底
    }

    void Update()
    {
        // ---- 无敌计时 + 闪烁 ----
        if (iframesTimer > 0f)
        {
            iframesTimer -= Time.deltaTime;
            if (sr != null)
                sr.enabled = iframesTimer > 0f ? (Time.frameCount % 8) >= 4 : true;
        }

        // ---- 记录"最后一次站地"：着地、不在上升、且不在无敌期间 ----
        // （无敌 = 刚挨过打或刚重生，说明这附近有危险，不配当重生点）
        if (!dead && iframesTimer <= 0f && rb != null && rb.velocity.y <= 0.1f
            && col.Cast(Vector2.down, groundFilter, hits, groundCastDistance) > 0)
        {
            lastGroundedPos = transform.position;
        }
    }

    // ============ 对外接口：陷阱调用 / Inspector 拖线 ============

    public void TakeDamage(int amount) { TakeDamage(amount, Vector2.zero); }

    public void TakeDamage(int amount, Vector2 knockback)
    {
        if (dead || iframesTimer > 0f) return;

        Current -= amount;
        iframesTimer = invincibleTime;
        if (rb != null && knockback.sqrMagnitude > 0.01f) rb.velocity = knockback;
        onDamaged.Invoke();

        if (Current <= 0) Die();
    }

    public void Heal(int amount)
    {
        if (dead) return;
        Current = Mathf.Min(Current + amount, maxHealth);
    }
    
    // 重生点校验：在陷阱范围内就退回兜底出生点
    Vector3 PickRespawnPoint()
    {
        if (hazardLayer.value != 0
            && Physics2D.OverlapCircle(lastGroundedPos, hazardCheckRadius, hazardLayer) != null)
        {
            Debug.LogWarning("[重生] 上次起跳点在陷阱范围内，改用兜底出生点");
            var fb = fallbackSpawn != null ? fallbackSpawn : FindSpawn();
            if (fb != null) return fb.position;
        }
        return lastGroundedPos;
    }

    static Transform FindSpawn()
    {
        var go = GameObject.Find("Spawn");
        return go != null ? go.transform : null;
    }

    // ================= 死亡 → 重生 =================

    void Die()
    {
        dead = true;
        onDied.Invoke();
        if (pc != null) pc.enabled = false;      // 停操控
        if (rb != null) rb.simulated = false;    // 停物理
        if (sr != null) sr.enabled = true;       // 别闪烁着黑屏
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(deathDelay);
        
        // 回到校验过的重生点（在陷阱里则退回出生点）
        transform.position = PickRespawnPoint() + Vector3.up * respawnLiftUp;
        Current = maxHealth;
        dead = false;
        if (rb != null) { rb.simulated = true; rb.velocity = Vector2.zero; }
        if (pc != null)
        {
            pc.enabled = true;
            pc.ResetMovementState();   // v30: 重生后跳跃/二段跳/冲刺全部重置
        }
        iframesTimer = respawnInvincibleTime;

        onRespawned.Invoke();
    }
}