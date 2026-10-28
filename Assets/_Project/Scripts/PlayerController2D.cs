using System.Collections;
using UnityEngine;

// ============================================================
// ① TouchInput：触屏输入总线（不挂物体；虚拟按钮写、本脚本读）
// ============================================================
public static class TouchInput
{
    public static bool left;
    public static bool right;
    public static bool jumpPressed;
    public static bool wavePressed;
    public static bool interactPressed;
    public static bool dashPressed;
}

// ============================================================
// ② PlayerController2D v6：蔚蓝式手感
//    v6 相比 v5 新增（来自参考 PlayerController 的 4 件套）：
//    - 跳跃水平冲量（移动中起跳带惯性）
//    - 冲刺输入缓冲（按早 0.08s 不吞键）
//    - 超级跳：地面冲刺后 0.15s 内起跳 = 大水平速度
//    - 蹬墙锁定仅在按住方向时生效
//    v5 已有：VarJump 窗口、下落上限、加/减速分离、冲刺动量保留、
//    冲刺填充延迟、蹭墙下滑+蹬墙跳、顿帧、残影、能力解锁。
// ============================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("能力解锁（默认锁定，道具或 F1/F2/F3 解锁）")]
    [Tooltip("二段跳。默认关；勾上 = 空中还能再跳一次。")]
    public bool canDoubleJump = false;
    [Tooltip("冲刺。默认关；勾上 = 可以横向或斜向冲一段。")]
    public bool canDash = false;
    [Tooltip("蹬墙跳 + 贴墙下滑。默认关；勾上 = 贴墙时下落变慢，并且能蹬着墙往外跳。\n注意：这一个开关同时管两件事，关掉时贴墙就是正常掉落。")]
    public bool canWallJump = false;

    [Header("移动")]
    public float moveSpeed = 8f;
    public float accel = 220f;
    public float airAccel = 150f;
    public float decel = 130f;
    public float airDecel = 50f;

    [Header("跳跃（VarJump 窗口式）")]
    public float jumpVelocity = 14f;
    public float airJumpVelocity = 12.5f;
    public float varJumpTime = 0.2f;
    public float jumpHBoost = 4f;            // v6: 移动中起跳附赠的水平冲量
    public float jumpCutMultiplier = 0.4f;
    public float jumpCutMinSpeed = 3f;
    public float gravityScale = 4.2f;
    public float fallGravityMultiplier = 1.7f;
    public float apexGravityMultiplier = 0.55f;
    public float apexThreshold = 3f;
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.1f;

    [Header("超级跳（地面冲刺接跳跃）")]
    public float superJumpWindow = 0.15f;    // v6: 地面冲刺结束后这窗口内起跳=超级跳
    public float superJumpH = 18f;           // v6: 超级跳水平速度

    [Header("下落速度上限")]
    public float maxFall = -16f;
    public float fastMaxFall = -24f;

    [Header("冲刺")]
    public KeyCode dashKey = KeyCode.LeftShift;
    public float dashSpeed = 24f;
    public float dashDuration = 0.15f;
    public float dashEndSpeed = 13f;
    public float endDashUpMult = 0.75f;
    public float dashCooldown = 0.2f;
    public float dashRefillDelay = 0.1f;
    public float dashUpBias = 0.8f;
    public float dashBufferTime = 0.08f;     // v6: 冲刺输入缓冲
    public bool dashHitstop = true;
    public float hitstopTime = 0.03f;

    [Header("墙壁")]
    public bool enableWallSlide = true;
    public bool enableWallJump = true;
    public float wallCheckDist = 0.3f;
    public float wallSlideMaxFall = -3f;
    public float wallJumpHSpeed = 12f;
    public float wallJumpForceTime = 0.15f;
    public float wallJumpFacingTime = 0.2f;   // v7: 蹬墙跳后朝向锁定时长（秒），期间按键不改朝向

    [Header("地面检测")]
    public float groundCastDistance = 0.08f;
    public LayerMask groundLayer;

    [Header("冲刺残影")]
    public bool dashTrail = true;
    public float trailInterval = 0.03f;
    public float trailFadeSpeed = 4f;

    [Header("临时测试键（正式版删除 Update 里对应两行）")]
    public KeyCode testUnlockJumpKey = KeyCode.F1;
    public KeyCode testUnlockDashKey = KeyCode.F2;
    public KeyCode testUnlockWallJumpKey = KeyCode.F3;

    // ---- 内部状态 ----
    Rigidbody2D rb;
    SpriteRenderer sr;
    Collider2D col;
    ContactFilter2D groundFilter;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];   // v9: 4 → 8，贴住多格时够用
    float airPeakY;                       // v11: 这一跳在空中的最高点（世界 y），用来算落差

    int lastDir = 1;
    float coyoteTimer, jumpBufferTimer;
    int airJumpsUsed;

    float varJumpTimer;
    float currentJumpSpeed;
    float wallForceTimer;
    float wallFacingTimer;                  // v7: 朝向锁剩余时间（>0 时按键不改朝向）
    float groundDashJumpTimer;      // v6: >0 = 刚结束一次地面冲刺（超级跳窗口）
    int wallDir;

    bool isDashing;
    Vector2 dashDir;
    float dashTimer, dashCdTimer, dashRefillTimer, dashBufferTimer;
    bool dashAvailable;
    bool dashStartedOnGround;

    public void UnlockDoubleJump() { canDoubleJump = true;  Debug.Log("[能力] 解锁二段跳！"); }
    public void UnlockDash()       { canDash = true;        Debug.Log("[能力] 解锁冲刺！"); }
    public void UnlockWallJump()   { canWallJump = true;    Debug.Log("[能力] 解锁蹬墙跳！"); }

    /// <summary>
    /// 这一跳的落差（单位：格）= 空中最高点 - 当前位置。
    /// 刚落地的那一瞬间读它，就是“这次从多高掉下来”。
    /// </summary>
    public float FallDistance { get { return Mathf.Max(0f, airPeakY - rb.position.y); } }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        rb.freezeRotation = true;
        dashAvailable = true;

        // v10: 玩家碰撞体强制用“0 摩擦”的物理材质。
        // 为什么：默认摩擦 0.4，按住朝墙的方向下落时，摩擦力会把下落“拽住”，
        // 看起来就是粘在墙上/挂在墙上。摩擦设 0 之后贴墙跌落就干净了。
        if (col.sharedMaterial == null || col.sharedMaterial.friction != 0f)
        {
            var mat = new PhysicsMaterial2D("PlayerNoFriction");
            mat.friction = 0f;      // 不打滑的元凶
            mat.bounciness = 0f;    // 不要弹
            col.sharedMaterial = mat;
        }
    }

    // v9: 只在“面朝检测方向”的碰撞面算命中。
    // 为什么需要它：胶囊碰撞体底下是个圆弧，蹭到方块边缘或墙顶角时，
    // 圆弧的斜侧面也会被 Cast 命中，于是“明明没踩实”却被当成站在地上。
    // 这里要求法线跟检测方向相反、且夹角够正（dot ≥ minDot），斜侧面就会被丢掉。
    bool CastHasSurface(Vector2 dir, float dist, float minDot)
    {
        int n = col.Cast(dir, groundFilter, hits, dist);
        for (int i = 0; i < n; i++)
            if (Vector2.Dot(hits[i].normal, -dir) >= minDot) return true;
        return false;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ---- 0. 临时测试键 ----
        if (Input.GetKeyDown(testUnlockJumpKey)) UnlockDoubleJump();
        if (Input.GetKeyDown(testUnlockDashKey)) UnlockDash();
        if (Input.GetKeyDown(testUnlockWallJumpKey)) UnlockWallJump();   // v8: 测试键 F3

        jumpBufferTimer -= dt;
        dashCdTimer -= dt;
        dashRefillTimer -= dt;
        dashBufferTimer -= dt;          // v6: 冲刺缓冲计时
        wallForceTimer -= dt;
        wallFacingTimer -= dt;              // v7: 朝向锁倒计时
        groundDashJumpTimer -= dt;

        // ---- 1. 方向输入（A+D 同按取后按方向）----
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))  lastDir = -1;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) lastDir = 1;
        bool left  = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)  || TouchInput.left;
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || TouchInput.right;
        int dir = left && right ? lastDir : left ? -1 : right ? 1 : 0;
        float vRaw = Input.GetAxisRaw("Vertical");
        bool jumpHeld = Input.GetButton("Jump");

        // ---- 2. 冲刺输入：进缓冲（v6），不直接判定 ----
        if (Input.GetKeyDown(dashKey) || TouchInput.dashPressed)
            dashBufferTimer = dashBufferTime;
        TouchInput.dashPressed = false;

        // ---- 3. 检测：地面 / 墙壁 ----
        groundFilter.useLayerMask = true;
        groundFilter.layerMask = groundLayer;
        groundFilter.useTriggers = false;
        bool grounded = rb.velocity.y <= 0.5f
                        && CastHasSurface(Vector2.down, groundCastDistance, 0.5f);   // v9: 只认朝上的面
        wallDir = 0;
        if (!grounded && canWallJump && (enableWallSlide || enableWallJump))   // v8: 未解锁则不识别墙面
        {
            if (CastHasSurface(Vector2.right, wallCheckDist, 0.5f))      wallDir = 1;   // v9: 只认竖直墙面
            else if (CastHasSurface(Vector2.left, wallCheckDist, 0.5f))  wallDir = -1;
        }

        // v11: 记录“这一跳的空中最高点”。
        // 用途：可打碎的砖块要判断“你是不是从很高的地方砸下来”。
        // 注意不能直接用下落速度——终端速度 16 格/秒在掉 0.6 格时就到了，
        // 从 1 格掉和从 50 格掉，速度完全一样。落差才是可靠的量。
        if (grounded) airPeakY = rb.position.y;
        else airPeakY = Mathf.Max(airPeakY, rb.position.y);

        // ---- 4. 落地：刷新土狼/二段跳；冲刺延迟后填充 ----
        if (grounded && !isDashing)
        {
            coyoteTimer = coyoteTime;
            airJumpsUsed = 0;
            if (!dashAvailable && dashRefillTimer <= 0f) dashAvailable = true;
        }
        else coyoteTimer -= dt;

        // ---- 5. 冲刺：用缓冲值判定（按早了会在可冲瞬间自动触发）----
        if (dashBufferTimer > 0f && canDash && dashAvailable && !isDashing && dashCdTimer <= 0f)
        {
            Vector2 d = new Vector2(dir, vRaw);
            if (d.sqrMagnitude < 0.01f)
                d = new Vector2(sr != null && sr.flipX ? -1f : 1f, 0f);
            d.Normalize();
            if (Mathf.Abs(d.x) < 0.01f && d.y > 0f) d.y = dashUpBias;
            StartDash(d);
        }

        if (isDashing)
        {
            dashTimer -= dt;
            rb.velocity = dashDir * dashSpeed;
            if (dashTimer <= 0f) EndDash();
        }
        else
        {
            // ---- 6. VarJump：按住维持起跳速度，松手狠截 ----
            if (varJumpTimer > 0f)
            {
                if (jumpHeld)
                {
                    rb.velocity = new Vector2(rb.velocity.x, currentJumpSpeed);
                    varJumpTimer -= dt;
                }
                else
                {
                    varJumpTimer = 0f;
                    if (rb.velocity.y > jumpCutMinSpeed)
                        rb.velocity = new Vector2(rb.velocity.x,
                            Mathf.Max(rb.velocity.y * jumpCutMultiplier, jumpCutMinSpeed));
                }
            }

            // ---- 7. 水平移动（蹬墙锁定期不吃输入；加/减速分离）----
            if (wallForceTimer <= 0f)
            {
                float target = dir * moveSpeed;
                float rate = dir != 0f ? (grounded ? accel : airAccel)
                                       : (grounded ? decel : airDecel);
                float vx = Mathf.MoveTowards(rb.velocity.x, target, rate * dt);
                rb.velocity = new Vector2(vx, rb.velocity.y);
            }

            // ---- 8. 蹭墙下滑 ----
            if (enableWallSlide && wallDir != 0 && rb.velocity.y < 0f
                && ((wallDir == 1 && right) || (wallDir == -1 && left)))
            {
                if (rb.velocity.y < wallSlideMaxFall)
                    rb.velocity = new Vector2(rb.velocity.x, wallSlideMaxFall);
            }

            // ---- 9. 重力：顶点悬浮 > 下落加重 > 常规 ----
            float g = gravityScale;
            if (Mathf.Abs(rb.velocity.y) < apexThreshold) g *= apexGravityMultiplier;
            else if (rb.velocity.y < 0f)                   g *= fallGravityMultiplier;
            rb.gravityScale = g;

            // ---- 10. 下落上限（按S快速下坠）----
            float floorV = vRaw < -0.5f ? fastMaxFall : maxFall;
            if (rb.velocity.y < floorV)
                rb.velocity = new Vector2(rb.velocity.x, floorV);
        }

        // ---- 11. 跳跃缓冲 ----
        if (Input.GetButtonDown("Jump") || TouchInput.jumpPressed)
            jumpBufferTimer = jumpBufferTime;
        TouchInput.jumpPressed = false;

        // ---- 12. 起跳优先级：地面(土狼/超级跳) > 蹬墙 > 二段 ----
        if (jumpBufferTimer > 0f && !isDashing)
        {
            if (coyoteTimer > 0f)
            {
                if (groundDashJumpTimer > 0f) DoSuperJump(dir != 0 ? dir : (sr != null && sr.flipX ? -1 : 1));
                else DoJump(jumpVelocity, dir);
            }
            else if (enableWallJump && wallDir != 0)
                DoWallJump(wallDir, dir);
            else if (canDoubleJump && airJumpsUsed < 1)
            {
                airJumpsUsed++;
                DoJump(airJumpVelocity, 0);   // 二段跳不吃水平冲量，防止无限加速
            }
        }

        // ---- 13. 朝向 ----
        if (dir != 0 && wallFacingTimer <= 0f && sr != null) sr.flipX = dir < 0;   // v7: 朝向锁期间不响应按键
    }

    // 地面普通跳：带水平冲量（v6）
    void DoJump(float v, int dirAtJump)
    {
        rb.velocity = new Vector2(rb.velocity.x + jumpHBoost * dirAtJump, v);
        currentJumpSpeed = v;
        varJumpTimer = varJumpTime;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        groundDashJumpTimer = 0f;

        MusicConductor.Play(Instrument.Harp);       // v13: 跳跃 → 里拉琴（v2 配器对调：最频繁的动作用最柔的音）
    }

    // 超级跳：地面冲刺后马上起跳，水平速度远超跑速（v6）
    void DoSuperJump(int dirAtJump)
    {
        rb.velocity = new Vector2(superJumpH * dirAtJump, jumpVelocity);
        currentJumpSpeed = jumpVelocity;
        varJumpTimer = varJumpTime;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        groundDashJumpTimer = 0f;
        MusicConductor.Play(Instrument.BassDrum);   // v12: 超级跳 → 大鼓
        Debug.Log("[超级跳]");
    }

    // 蹬墙跳：只在按住方向时锁定水平（v6 修正）
    void DoWallJump(int wallDirSide, int dirAtJump)
    {
        rb.velocity = new Vector2(-wallDirSide * wallJumpHSpeed, jumpVelocity);
        currentJumpSpeed = jumpVelocity;
        varJumpTimer = varJumpTime;
        wallForceTimer = dirAtJump != 0 ? wallJumpForceTime : 0f;   // 没按方向就不锁
        airJumpsUsed = 0;
        dashAvailable = true;
        jumpBufferTimer = 0f;
        if (sr != null) sr.flipX = wallDirSide == 1;
        wallFacingTimer = wallJumpFacingTime;   // v7: 锁住朝向，防止同帧第 13 步把它改回去

        MusicConductor.Play(Instrument.Violin);   // v12: 蹬墙跳 → 小提琴
    }

    void StartDash(Vector2 d)
    {
        isDashing = true;
        dashDir = d;
        dashTimer = dashDuration;
        dashCdTimer = dashDuration + dashCooldown;
        dashAvailable = false;
        dashRefillTimer = dashRefillDelay;
        dashBufferTimer = 0f;               // 消费掉缓冲

        MusicConductor.Play(Instrument.Trumpet);   // v12: 冲刺 → 小号
        dashStartedOnGround = coyoteTimer > 0f;   // 记录：地面起冲才能接超级跳
        airJumpsUsed = 0;
        varJumpTimer = 0f;
        rb.gravityScale = 0f;
        if (dashHitstop) StartCoroutine(Hitstop());
        if (dashTrail) StartCoroutine(TrailRoutine());
    }

    void EndDash()
    {
        isDashing = false;
        Vector2 end = dashDir * dashEndSpeed;
        if (dashDir.y > 0.5f) end.y *= endDashUpMult;
        rb.velocity = end;
        if (dashStartedOnGround) groundDashJumpTimer = superJumpWindow;  // 开超级跳窗口
    }

    IEnumerator Hitstop()
    {
        if (Time.timeScale != 1f) yield break;
        Time.timeScale = 0.02f;
        yield return new WaitForSecondsRealtime(hitstopTime);
        if (Time.timeScale < 1f) Time.timeScale = 1f;
    }

    IEnumerator TrailRoutine()
    {
        while (isDashing)
        {
            if (sr != null && sr.sprite != null)
            {
                var ghost = new GameObject("DashGhost");
                var gr = ghost.AddComponent<SpriteRenderer>();
                gr.sprite = sr.sprite;
                gr.flipX = sr.flipX;
                Color c = sr.color; c.a = 0.5f; gr.color = c;
                gr.sortingOrder = sr.sortingOrder - 1;
                ghost.transform.SetPositionAndRotation(transform.position, transform.rotation);
                ghost.transform.localScale = transform.localScale;
                StartCoroutine(FadeGhost(gr));
            }
            yield return new WaitForSeconds(trailInterval);
        }
    }

    IEnumerator FadeGhost(SpriteRenderer r)
    {
        Color c = r.color;
        while (c.a > 0f)
        {
            c.a -= Time.deltaTime * trailFadeSpeed;
            r.color = c;
            yield return null;
        }
        Destroy(r.gameObject);
    }
}