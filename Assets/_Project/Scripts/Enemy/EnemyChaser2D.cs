using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 地面敌人：巡逻/追击 + 接触伤害 + 回声显红（继承 WaveReceivable2D）。
/// 巡逻：撞墙（含矮台阶）或前脚探到悬崖 → 调头；调头有冷却（v3：防窄通道/小平台每帧来回翻面）。
/// 追击：玩家进入半径后朝玩家走，撞墙跳一下翻矮障碍，允许跳下平台跟到底。
/// 探测全部以自身碰撞体边界为基准；悬崖探测点在前脚外侧（v3 修复：原来从中心量，小平台双向误判）。
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
    public bool canHop = true;               // 追击撞墙时跳一下（翻矮障碍）
    public float hopVelocity = 11f;

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
    public float ledgeCheckDist = 0.25f;      // 前脚往外多少格开始探悬崖（v3：从前脚外侧量）
    public float ledgeDepth = 0.35f;          // 脚下多少格内没地 = 悬崖
    public float turnCooldown = 0.25f;        // v3：两次调头的最小间隔（防来回翻面）

    Rigidbody2D rb;
    SpriteRenderer sr;
    Collider2D col;
    Transform player;
    PlayerHealth2D playerHealth;
    int facing = 1;
    float revealTimer;
    float turnCdTimer;
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

        // ---- 是否追击 ----
        chasing = player != null
                  && Vector2.Distance(transform.position, player.position) <= detectRadius
                  && (!useLineOfSight || SightClear());

        if (chasing)
            facing = player.position.x >= transform.position.x ? 1 : -1;

        bool wallAhead = WallAhead(facing);
        bool ledgeAhead = LedgeAhead(facing);
        float speed = chasing ? chaseSpeed : patrolSpeed;

        if (chasing)
        {
            // 撞墙 + 在地上 → 跳一下（翻矮障碍）
            if (wallAhead && canHop && rb.velocity.y <= 0.1f && Grounded())
                rb.velocity = new Vector2(rb.velocity.x, hopVelocity);
            // 追击允许冲下平台（跟到底）
            rb.velocity = new Vector2(facing * speed, rb.velocity.y);
        }
        else
        {
            // 巡逻：着地 + 不在冷却中 + 撞墙/悬崖 → 调头
            //（v3 冷却：窄通道两边都是墙、小平台两边都是崖时，最多 0.25 秒翻一次，不再每帧抽搐）
            if (Grounded() && turnCdTimer <= 0f && (wallAhead || ledgeAhead))
            {
                facing = -facing;
                turnCdTimer = turnCooldown;
            }
            rb.velocity = new Vector2(facing * speed, rb.velocity.y);
        }

        if (sr != null) sr.flipX = facing == -1;
    }

    // ================= 探测 =================

    // 脚下 0.2 内有地 = 站着
    bool Grounded()
    {
        return Physics2D.Raycast(BottomCenter, Vector2.down, 0.2f, groundLayer);
    }

    // 撞墙：身体中部 + 膝部两条横射线（任一被挡都算墙，1格高的台阶也能发现）
    bool WallAhead(int dir)
    {
        float len = HalfWidth + wallCheckDist;
        Vector2 knee = BottomCenter + Vector2.up * 0.3f;
        return Physics2D.Raycast(Center, Vector2.right * dir, len, groundLayer)
            || Physics2D.Raycast(knee, Vector2.right * dir, len, groundLayer);
    }

    // 悬崖：前脚外侧往下探（v3：从"半宽+偏移"量，保证探测点在身体外面）
    bool LedgeAhead(int dir)
    {
        Vector2 probe = BottomCenter + Vector2.right * dir * (HalfWidth + ledgeCheckDist);
        return Physics2D.Raycast(probe, Vector2.down, ledgeDepth, groundLayer) == false;
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

    // 选中时画出探测射线：红=墙探，黄=脚/悬崖探，白=追击半径
    void OnDrawGizmosSelected()
    {
        if (col == null) return;
        Gizmos.color = Color.red;
        float len = col.bounds.extents.x + wallCheckDist;
        Vector2 knee = BottomCenter + Vector2.up * 0.3f;
        Gizmos.DrawRay(Center, Vector2.right * facing * len);
        Gizmos.DrawRay(knee, Vector2.right * facing * len);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(BottomCenter, Vector2.down * 0.2f);
        Gizmos.DrawRay(BottomCenter + Vector2.right * facing * (HalfWidth + ledgeCheckDist),
                       Vector2.down * ledgeDepth);
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
