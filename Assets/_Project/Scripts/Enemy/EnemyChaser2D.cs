using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 地面敌人（v4：追击智能）。
/// 巡逻：撞墙（含矮台阶）或前脚悬崖 → 调头（带冷却）。
/// 追击（v4）：
///   - 遇大沟起跳跨越（小落差直接走下去）；
///   - 同一面墙最多跳 maxHopTries 次，翻不过去不再当弹簧；
///   - 卡住 giveUpStuckTime 秒 → 放弃追击，冷却 chaseCooldown 秒回巡逻。
/// 回声显红：被波扫到全身变红 revealedDuration 秒。
/// 选中敌人时 Scene 视图画出所有探测射线。
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class EnemyChaser2D : WaveReceivable2D
{
    [Header("移动")]
    public float patrolSpeed = 2.5f;
    public float chaseSpeed = 5.5f;
    public float detectRadius = 9f;          // 追击探测半径
    public bool useLineOfSight = false;      // true 时墙后看不到玩家
    public float turnCooldown = 0.25f;       // 巡逻调头最小间隔

    [Header("追击智能（v4）")]
    public bool canHop = true;               // 追击撞墙跳（翻矮障碍）
    public float hopVelocity = 11f;
    public int maxHopTries = 2;              // 同一面墙最多跳几次，翻不过去就放弃
    public bool leapGaps = true;             // 追击遇大沟起跳跨越
    public float leapX = 7f;                 // 跨沟水平速度
    public float giveUpStuckTime = 1.2f;     // 卡住多久放弃追击
    public float chaseCooldown = 2f;         // 放弃后多久才重新追

    [Header("接触伤害")]
    public int damage = 1;
    public float knockbackX = 9f;
    public float knockbackY = 7f;

    [Header("回声显形（策划重点）")]
    public Color normalColor = new Color(0.45f, 0.30f, 0.35f);   // 平时：暗紫红
    public float normalAlpha = 1f;            // 暗关卡改成 0.25 → 平时几乎隐形
    public Color revealedColor = new Color(1f, 0.15f, 0.2f);     // 显形：鲜红
    public float revealedDuration = 3f;
    public UnityEvent onEchoRevealed;         // 接音效

    [Header("探测")]
    public LayerMask groundLayer;             // 勾 Ground（墙/边缘/视线都用它）
    public float wallCheckDist = 0.4f;        // 超出身体多少格算"撞墙"
    public float ledgeCheckDist = 0.25f;      // 前脚往外多少格开始探悬崖
    public float ledgeDepth = 0.35f;          // 脚下多少格内没地 = 悬崖

    Rigidbody2D rb;
    SpriteRenderer sr;
    Collider2D col;
    Transform player;
    PlayerHealth2D playerHealth;
    int facing = 1;
    float revealTimer;
    float turnCdTimer;
    float chaseCdTimer;      // v4：放弃后的追击冷却
    float stuckTimer;        // v4：卡住计时
    int hopTries;            // v4：这面墙已跳次数
    bool chasing;

    // ---- 以碰撞体边界为基准的基准点 ----
    Vector2 Center => transform.position;
    Vector2 BottomCenter => (Vector2)transform.position
                            + Vector2.down * (col.bounds.extents.y + 0.05f);
    float HalfWidth => col.bounds.extents.x;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        rb.freezeRotation = true;

        // 静默错配置自检：Layer 没勾时所有探测全瞎，但不报错——这里把它变成显式报错
        if (groundLayer.value == 0)
            Debug.LogError("[敌人] " + name + " 的 Ground Layer 没有勾任何层！墙/悬崖/着地检测全部失效。选中它，把 Ground Layer 勾上 Ground。");

        var pc = FindObjectOfType<PlayerController2D>();
        if (pc != null)
        {
            player = pc.transform;
            playerHealth = pc.GetComponent<PlayerHealth2D>();
        }
        SetLook(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ---- 计时 ----
        if (revealTimer > 0f)
        {
            revealTimer -= dt;
            if (revealTimer <= 0f) SetLook(false);
        }
        turnCdTimer -= dt;
        chaseCdTimer -= dt;

        // ---- 是否追击（含放弃冷却）----
        chasing = chaseCdTimer <= 0f
                  && player != null
                  && Vector2.Distance(transform.position, player.position) <= detectRadius
                  && (!useLineOfSight || SightClear());

        if (chasing)
            facing = player.position.x >= transform.position.x ? 1 : -1;

        bool wallAhead = WallAhead(facing);
        bool ledgeAhead = LedgeAhead(facing);
        bool grounded = Grounded();
        float speed = chasing ? chaseSpeed : patrolSpeed;

        if (chasing)
        {
            // ---- v4：卡住检测（想走却走不动 = 顶着翻不过的墙/卡角落）----
            if (Mathf.Abs(rb.velocity.x) > 1f) stuckTimer = 0f;
            else stuckTimer += dt;

            if (stuckTimer >= giveUpStuckTime)
            {
                chaseCdTimer = chaseCooldown;      // 放弃，冷却后才会再追
                stuckTimer = 0f;
                hopTries = 0;
                chasing = false;
            }
        }
        else stuckTimer = 0f;

        if (chasing)
        {
            // 撞墙：限额内起跳翻越；超次数不跳（贴墙累积 stuckTimer → 放弃）
            if (wallAhead)
            {
                if (canHop && grounded && rb.velocity.y <= 0.1f && hopTries < maxHopTries)
                {
                    rb.velocity = new Vector2(rb.velocity.x, hopVelocity);
                    hopTries++;
                }
            }
            else if (grounded) hopTries = 0;       // 翻过来了/没墙了，重置次数

            // 悬崖：大沟起跳跨越，小落差直接走下去
            if (ledgeAhead && leapGaps && grounded && rb.velocity.y <= 0.1f
                && DeepLedgeAhead(facing))
            {
                rb.velocity = new Vector2(facing * leapX, hopVelocity);
            }

            rb.velocity = new Vector2(facing * speed, rb.velocity.y);
        }
        else
        {
            // 巡逻：着地 + 不在冷却中 + 撞墙/悬崖 → 调头
            if (grounded && turnCdTimer <= 0f && (wallAhead || ledgeAhead))
            {
                facing = -facing;
                turnCdTimer = turnCooldown;
            }
            rb.velocity = new Vector2(facing * speed, rb.velocity.y);
        }

        if (sr != null) sr.flipX = facing == -1;
    }

    // ================= 探测 =================
    // v5 修复：竖向探针原来从"脚底再往下 0.05"（地砖内部）发射，
    // 遇到 Queries Start In Colliders 被关闭的工程就全部失明（地:False 崖:True）。
    // 现在一律从身体中心（空气层）往下打，穿过自身后从上方进入地面，不受该设置影响。

    bool Grounded()
    {
        return Physics2D.Raycast(Center, Vector2.down, col.bounds.extents.y + 0.2f, groundLayer);
    }

    // 撞墙：身体中部 + 膝部两条横射线（1 格高台阶也能发现）
    bool WallAhead(int dir)
    {
        float len = HalfWidth + wallCheckDist;
        Vector2 knee = BottomCenter + Vector2.up * 0.3f;
        return Physics2D.Raycast(Center, Vector2.right * dir, len, groundLayer)
            || Physics2D.Raycast(knee, Vector2.right * dir, len, groundLayer);
    }

    // 悬崖：前脚外侧、往下探到脚底以下 ledgeDepth
    bool LedgeAhead(int dir)
    {
        Vector2 probe = Center + Vector2.right * dir * (HalfWidth + ledgeCheckDist);
        return Physics2D.Raycast(probe, Vector2.down, col.bounds.extents.y + ledgeDepth, groundLayer) == false;
    }

    // 大沟：前脚外侧往下 1.6 格都没有地 = 掉下去很深，值得跳一下跨过去
    bool DeepLedgeAhead(int dir)
    {
        Vector2 probe = Center + Vector2.right * dir * (HalfWidth + ledgeCheckDist);
        return Physics2D.Raycast(probe, Vector2.down, col.bounds.extents.y + 1.6f, groundLayer) == false;
    }

    bool SightClear()
    {
        return Physics2D.Linecast(transform.position, player.position, groundLayer).collider == null;
    }

    // ---- 接触伤害（玩家无敌帧自动节流）----
    void OnCollisionStay2D(Collision2D c)
    {
        if (!c.collider.CompareTag("Player")) return;
        if (playerHealth == null) return;
        float dir = Mathf.Sign(c.collider.transform.position.x - transform.position.x);
        if (dir == 0f) dir = 1f;
        playerHealth.TakeDamage(damage, new Vector2(dir * knockbackX, knockbackY));
    }

    // ---- 回声扫到：显红 ----
    public override void OnWaveHit(SoundWave2D wave)
    {
        base.OnWaveHit(wave);
        revealTimer = revealedDuration;
        SetLook(true);
        onEchoRevealed.Invoke();
    }

    void SetLook(bool revealed)
    {
        if (sr == null) return;
        Color c = revealed ? revealedColor : normalColor;
        c.a = revealed ? 1f : normalAlpha;
        sr.color = c;
    }

    // 选中时画出探测射线：红=墙探，黄=着地/悬崖探（都从身体中心往下），白=追击半径
    void OnDrawGizmosSelected()
    {
        if (col == null) return;
        Gizmos.color = Color.red;
        float len = col.bounds.extents.x + wallCheckDist;
        Vector2 knee = BottomCenter + Vector2.up * 0.3f;
        Gizmos.DrawRay(Center, Vector2.right * facing * len);
        Gizmos.DrawRay(knee, Vector2.right * facing * len);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(Center, Vector2.down * (col.bounds.extents.y + 0.2f));
        Gizmos.DrawRay(Center + Vector2.right * facing * (HalfWidth + ledgeCheckDist),
                       Vector2.down * (col.bounds.extents.y + ledgeDepth));
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
