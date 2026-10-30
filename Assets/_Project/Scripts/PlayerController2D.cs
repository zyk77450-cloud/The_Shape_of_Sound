using System.Collections;
using System.Collections.Generic;
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
// ② PlayerController2D v19
//    v19：单向平台下穿改为"按间隙恢复碰撞"——0.35s 只是开始检查的
//         最早时间，和平台间隙 > 0.2 才恢复，否则 0.05s 后再查；
//         修复人还卡在板子里时恢复碰撞被弹飞（回弹 bug）。
//    v18：水中水阻修复（速度永远收敛）；水面单向平台 + 按 S 下穿。
//    v17：划水吃跳跃缓冲；浅水/池底按跳=满力正常跳。
//    v16：水中起跳上升期不掐可变跳高。
//    v15：水域判定改每帧重叠对账（死亡/传送丢事件不再卡游泳态）。
//    v14：水中八向游泳（零重力、划水、水里刷新能力、水中不识别墙）。
//    v13 及更早：蹬墙朝向锁/蹬墙解锁/F3、法线过滤的着地判定、
//         零摩擦材质、落差记录、MusicConductor 音乐钩子、
//         VarJump、超级跳、冲刺缓冲、顿帧、残影、能力解锁。
// ============================================================
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("能力解锁（默认锁定，用道具或 F1/F2/F3/F4 解锁）")]
    [Tooltip("二段跳。默认关；勾上 = 空中还能再跳一次。")]
    public bool canDoubleJump = false;
    [Tooltip("冲刺。默认关；勾上 = 可以横向或斜向冲一段。")]
    public bool canDash = false;
    [Tooltip("蹬墙跳 + 贴墙下滑。默认关；勾上 = 贴墙时下落变慢，并且能蹬着墙往外跳。\n注意：这一个开关同时管两件事，关掉时贴墙就是正常掉落。")]
    public bool canWallJump = false;

    [Tooltip("游泳 + 水下行走。默认关；勾上 = 在水里可以八向自由游，按跳跃键划水上浮。\n未解锁时：水只会让你下沉变慢、走路变慢，不能划水，也浮不上来。")]
    public bool canSwim = false;

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
    public float jumpHBoost = 4f;
    public float jumpCutMultiplier = 0.4f;
    public float jumpCutMinSpeed = 3f;
    public float gravityScale = 4.2f;
    public float fallGravityMultiplier = 1.7f;
    public float apexGravityMultiplier = 0.55f;
    public float apexThreshold = 3f;
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.1f;

    [Header("超级跳（地面冲刺接跳跃）")]
    public float superJumpWindow = 0.15f;
    public float superJumpH = 18f;

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
    public float dashBufferTime = 0.08f;
    public bool dashHitstop = true;
    public float hitstopTime = 0.03f;

    [Header("墙壁")]
    public bool enableWallSlide = true;
    public bool enableWallJump = true;
    public float wallCheckDist = 0.3f;
    public float wallSlideMaxFall = -3f;
    public float wallJumpHSpeed = 12f;
    public float wallJumpForceTime = 0.15f;
    public float wallJumpFacingTime = 0.2f;

    [Header("游泳")]
    public float swimSpeed = 5.5f;          // 游速
    public float swimAccel = 30f;           // 水中操控加减速
    public float waterDrag = 7f;            // 爆发段（划水/冲刺后）的柔和水阻
    public float swimStrokeSpeed = 11f;     // 跳跃键=划水的速度
    public float swimStrokeCooldown = 0.35f;
    public float enterWaterDamp = 0.5f;     // 入水瞬间速度衰减

    [Tooltip("游泳时的重力是陆地的百分之多少。\n游泳不等于失重 —— 0.35 = 还会慢慢往下沉，想浮住得一直往上按。\n调大 → 沉得快、更累；调到 1 = 和陆地一样沉。")]
    public float swimGravityScale = 0.35f;

    [Tooltip("游泳时最多能沉多快（格/秒，写负数）。\n-3.0 = 慢慢沉；-6 = 沉得很快。")]
    public float swimMaxFall = -3.0f;

    [Header("水下行走（还没解锁游泳时）")]
    [Tooltip("水里的重力是陆地的百分之多少。\n0.35 = 只保留 35% 重力，掉得很慢但还在掉。\n调大 → 沉得快；调到 1 = 和陆地一样沉。")]
    public float waterGravityScale = 0.35f;

    [Tooltip("水里最多能沉多快（格/秒，写负数）。\n-3.5 = 慢慢沉底；-8 = 沉得很快。")]
    public float waterMaxFall = -3.5f;

    [Tooltip("水里的水平速度是陆地上的百分之多少。\n0.65 = 走路明显变慢但还能走。\n调小 → 更费劲；调到 0.3 = 几乎挪不动。")]
    public float waterMoveMul = 0.65f;

    [Tooltip("水下行走的起跳力气倍率（乘在 Jump Velocity 上，默认 0.8 = 八成力）。\n想跳得高就调大；调到 1 = 和陆地一样。")]
    public float waterJumpMul = 0.8f;

    [Header("地面检测")]
    public float groundCastDistance = 0.08f;
    public LayerMask groundLayer;

    [Header("冲刺残影")]
    public bool dashTrail = true;
    public float trailInterval = 0.03f;
    public float trailFadeSpeed = 4f;

    [Header("临时测试键（正式版删除 Update 里对应几行）")]
    public KeyCode testUnlockJumpKey = KeyCode.F1;
    public KeyCode testUnlockDashKey = KeyCode.F2;
    public KeyCode testUnlockWallJumpKey = KeyCode.F3;
    public KeyCode testUnlockSwimKey = KeyCode.F4;

    // ---- 内部状态 ----
    Rigidbody2D rb;
    SpriteRenderer sr;
    Collider2D col;
    ContactFilter2D groundFilter;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];
    float airPeakY;                       // v11: 这一跳的空中最高点

    int lastDir = 1;
    float coyoteTimer, jumpBufferTimer;
    int airJumpsUsed;

    float varJumpTimer;
    float currentJumpSpeed;
    float wallForceTimer;
    float wallFacingTimer;
    float groundDashJumpTimer;
    int wallDir;

    bool isDashing;
    Vector2 dashDir;
    float dashTimer, dashCdTimer, dashRefillTimer, dashBufferTimer;
    bool dashAvailable;
    bool dashStartedOnGround;

    // ---- 水域状态（v15：每帧对账，不依赖触发事件）----
    int waterCount;
    float strokeCdTimer;
    bool InWater => waterCount > 0;
    readonly Collider2D[] overlapHits = new Collider2D[8];
    ContactFilter2D waterFilter;

    // ---- v19: 单向平台（水面）下穿 ----
    readonly List<Collider2D> ignoredPlatforms = new List<Collider2D>();
    float dropTimer;

    public void UnlockDoubleJump() { canDoubleJump = true;  Debug.Log("[能力] 解锁二段跳！"); }
    public void UnlockDash()       { canDash = true;        Debug.Log("[能力] 解锁冲刺！"); }
    public void UnlockWallJump()   { canWallJump = true;    Debug.Log("[能力] 解锁蹬墙跳！"); }
    public void UnlockSwim()       { canSwim = true;        Debug.Log("[能力] 解锁游泳！"); }

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

        // v10: 玩家碰撞体强制零摩擦（防贴墙被摩擦力"拽住"）
        if (col.sharedMaterial == null || col.sharedMaterial.friction != 0f)
        {
            var mat = new PhysicsMaterial2D("PlayerNoFriction");
            mat.friction = 0f;
            mat.bounciness = 0f;
            col.sharedMaterial = mat;
        }

        // v15: 水域对账过滤器（含触发器，不分层，靠 WaterZone2D 组件识别）
        waterFilter = new ContactFilter2D();
        waterFilter.useTriggers = true;
        waterFilter.useLayerMask = false;
    }

    // v9: 只在“面朝检测方向”的碰撞面算命中。
    bool CastHasSurface(Vector2 dir, float dist, float minDot)
    {
        int n = col.Cast(dir, groundFilter, hits, dist);
        for (int i = 0; i < n; i++)
            if (Vector2.Dot(hits[i].normal, -dir) >= minDot) return true;
        return false;
    }

    // ============================================================
    // v23: 水下行走
    //   没解锁游泳时：水里全程用这一套（水只减速，不能游）。
    //   解锁游泳后：只要脚踩池底也用这一套（于是"能走"和"能游"共用一个锁）。
    // 水的作用就两件事：重力变小、下沉有上限；走和跳照常，只是打折。
    // ============================================================
    void WaterWalk(float dt, int hInput, bool onGround, bool jumpHeld)
    {
        // 跳：池底能跳；空中还能补一次二段跳（都按 waterJumpMul 打折）
        if (jumpBufferTimer > 0f)
        {
            if (onGround)
            {
                jumpBufferTimer = 0f;
                DoJump(jumpVelocity * waterJumpMul, hInput);
            }
            else if (canDoubleJump && airJumpsUsed < 1)
            {
                jumpBufferTimer = 0f;
                airJumpsUsed++;
                DoJump(airJumpVelocity * waterJumpMul, 0);
            }
        }

        // v24: 按住跳变高（和陆地同一套）—— 上升期维持起跳速度，松手立刻截断
        if (varJumpTimer > 0f)
        {
            if (jumpHeld && rb.velocity.y > 0f)
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

        // 水平：陆地那套加减速，但目标速度和加速度都乘 waterMoveMul
        if (wallForceTimer <= 0f)
        {
            float target = hInput * moveSpeed * waterMoveMul;
            float rate = hInput != 0f ? (onGround ? accel : airAccel)
                                      : (onGround ? decel : airDecel);
            rb.velocity = new Vector2(
                Mathf.MoveTowards(rb.velocity.x, target, rate * waterMoveMul * dt),
                rb.velocity.y);
        }

        // 重力：减轻，但仍然往下沉
        rb.gravityScale = gravityScale * waterGravityScale;

        // 下沉上限：水再深也是慢慢沉底
        if (rb.velocity.y < waterMaxFall)
            rb.velocity = new Vector2(rb.velocity.x, waterMaxFall);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // ---- 0. 临时测试键 ----
        if (Input.GetKeyDown(testUnlockJumpKey)) UnlockDoubleJump();
        if (Input.GetKeyDown(testUnlockDashKey)) UnlockDash();
        if (Input.GetKeyDown(testUnlockWallJumpKey)) UnlockWallJump();

        jumpBufferTimer -= dt;
        dashCdTimer -= dt;
        dashRefillTimer -= dt;
        dashBufferTimer -= dt;
        wallForceTimer -= dt;
        wallFacingTimer -= dt;
        groundDashJumpTimer -= dt;
        strokeCdTimer -= dt;

        // ---- 1. 方向输入（A+D 同按取后按方向）----
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))  lastDir = -1;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) lastDir = 1;
        bool left  = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)  || TouchInput.left;
        bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || TouchInput.right;
        int dir = left && right ? lastDir : left ? -1 : right ? 1 : 0;
        float vRaw = Input.GetAxisRaw("Vertical");
        bool jumpHeld = Input.GetButton("Jump");

        // ---- 2. 冲刺输入：进缓冲 ----
        if (Input.GetKeyDown(dashKey) || TouchInput.dashPressed)
            dashBufferTimer = dashBufferTime;
        TouchInput.dashPressed = false;

        // ---- 2.5 v15: 水域对账（每帧以实际重叠为准）----
        {
            int n = col.OverlapCollider(waterFilter, overlapHits);
            int count = 0;
            for (int i = 0; i < n; i++)
                if (overlapHits[i].GetComponent<WaterZone2D>() != null) count++;

            if (count > 0 && waterCount == 0 && rb != null)
            {
                rb.velocity *= enterWaterDamp;      // 入水刹车（0→有水 的那一刻）
                varJumpTimer = 0f;                  // v24: 清掉陆地上残留的"按住跳变高"
            }

            waterCount = count;
        }

        // ---- 3. 检测：地面 / 墙壁 ----
        groundFilter.useLayerMask = true;
        groundFilter.layerMask = groundLayer;
        groundFilter.useTriggers = false;
        bool grounded = rb.velocity.y <= 0.5f
                        && CastHasSurface(Vector2.down, groundCastDistance, 0.5f);
        wallDir = 0;
        if (!grounded && !InWater && canWallJump && (enableWallSlide || enableWallJump))
        {
            if (CastHasSurface(Vector2.right, wallCheckDist, 0.5f))      wallDir = 1;
            else if (CastHasSurface(Vector2.left, wallCheckDist, 0.5f))  wallDir = -1;
        }

        // v11: 记录“这一跳的空中最高点”
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

        // ---- 4.5 v19: 单向平台下穿（按 S）----
        // 0.35s 只是"开始检查"的最早时间；和平台间隙 > 0.2 才恢复碰撞，
        // 否则 0.05s 后再查——人还卡在板子里时绝不恢复，就不会被弹飞。
        if (dropTimer > 0f)
        {
            dropTimer -= dt;
            if (dropTimer <= 0f)
            {
                for (int i = ignoredPlatforms.Count - 1; i >= 0; i--)
                {
                    var p = ignoredPlatforms[i];
                    if (p == null) { ignoredPlatforms.RemoveAt(i); continue; }

                    // col.Distance(p)：< 0 重叠，= 0 贴着，> 0 分开
                    if (col.Distance(p).distance > 0.2f)
                    {
                        Physics2D.IgnoreCollision(col, p, false);   // 真离开了才恢复
                        ignoredPlatforms.RemoveAt(i);
                    }
                }
                if (ignoredPlatforms.Count > 0)
                    dropTimer = 0.05f;   // 还有没离开的，稍后再查（不恢复、不弹人）
            }
        }
        else if (grounded && !InWater && vRaw < -0.5f)
        {
            int n = col.Cast(Vector2.down, groundFilter, hits, groundCastDistance + 0.1f);
            for (int i = 0; i < n; i++)
                if (hits[i].collider != null && hits[i].collider.GetComponent<PlatformEffector2D>() != null)
                {
                    Physics2D.IgnoreCollision(col, hits[i].collider, true);
                    ignoredPlatforms.Add(hits[i].collider);
                }
            if (ignoredPlatforms.Count > 0) dropTimer = 0.35f;   // 最短下穿窗口
        }

        // ---- 5. 冲刺 ----
        if (dashBufferTimer > 0f && canDash && dashAvailable && !isDashing && dashCdTimer <= 0f)
        {
            Vector2 d = new Vector2(dir, vRaw);   // 水里同样八向冲
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
        else if (InWater && (!canSwim || grounded))
        {
            // ================= v23：水下行走 =================
            // 没解锁游泳：水里全程走这一套（只是减速，不能游）。
            // 解锁后：只要脚踩池底也走这一套 —— 所以「能走」和「能游」是同一个锁给的。
            WaterWalk(dt, dir, grounded, jumpHeld);
        }
        else if (InWater)
        {
            // ================= v22：游泳（仍有重力，会慢慢下沉）=================
            if (rb.velocity.y <= 5f) varJumpTimer = 0f;   // 从水里起跳的上升期不掐可变跳高

            // 重力：游泳不会让你失重，只是沉得慢
            rb.gravityScale = gravityScale * swimGravityScale;

            Vector2 swimDir = new Vector2(dir, vRaw);
            if (swimDir.sqrMagnitude > 0.01f) swimDir.Normalize();

            // 划水 / 池底起跳（吃跳跃缓冲，按早了不吞键）
            if (strokeCdTimer <= 0f && jumpBufferTimer > 0f)
            {
                jumpBufferTimer = 0f;
                if (grounded)
                {
                    DoJump(jumpVelocity, dir);            // 浅水/池底：满力正常跳
                }
                else
                {
                    Vector2 sv = swimDir.sqrMagnitude > 0.01f ? swimDir : Vector2.up;
                    rb.velocity = sv * swimStrokeSpeed;   // 划水
                    strokeCdTimer = swimStrokeCooldown;
                    MusicConductor.Play(Instrument.Harp);
                }
            }
            else
            {
                // 水平：有输入就逼近 swimSpeed，没输入靠水阻停下来
                float newX = Mathf.Abs(dir) > 0.01f
                    ? Mathf.MoveTowards(rb.velocity.x, dir * swimSpeed, swimAccel * dt)
                    : Mathf.MoveTowards(rb.velocity.x, 0f, waterDrag * dt);

                // 垂直：有输入才主动游上/游下；没输入就让重力把人往下带
                float newY = rb.velocity.y;
                if (Mathf.Abs(vRaw) > 0.01f)
                    newY = Mathf.MoveTowards(newY, vRaw * swimSpeed, swimAccel * dt);

                // 下沉不加速：最多沉到 swimMaxFall
                if (newY < swimMaxFall) newY = swimMaxFall;

                rb.velocity = new Vector2(newX, newY);
            }
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

        // ---- 12. 起跳（水里不走这里，由「水下行走」或「游泳」分支接管）----
        if (jumpBufferTimer > 0f && !isDashing && !InWater)
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
                DoJump(airJumpVelocity, 0);
            }
        }

        // ---- 13. 朝向 ----
        if (dir != 0 && wallFacingTimer <= 0f && sr != null) sr.flipX = dir < 0;
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

        MusicConductor.Play(Instrument.Harp);
    }

    // 超级跳：地面冲刺后马上起跳（v6）
    void DoSuperJump(int dirAtJump)
    {
        rb.velocity = new Vector2(superJumpH * dirAtJump, jumpVelocity);
        currentJumpSpeed = jumpVelocity;
        varJumpTimer = varJumpTime;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        groundDashJumpTimer = 0f;
        MusicConductor.Play(Instrument.BassDrum);
        Debug.Log("[超级跳]");
    }

    // 蹬墙跳：只在按住方向时锁定水平（v6 修正）
    void DoWallJump(int wallDirSide, int dirAtJump)
    {
        rb.velocity = new Vector2(-wallDirSide * wallJumpHSpeed, jumpVelocity);
        currentJumpSpeed = jumpVelocity;
        varJumpTimer = varJumpTime;
        wallForceTimer = dirAtJump != 0 ? wallJumpForceTime : 0f;
        airJumpsUsed = 0;
        dashAvailable = true;
        jumpBufferTimer = 0f;
        if (sr != null) sr.flipX = wallDirSide == 1;
        wallFacingTimer = wallJumpFacingTime;

        MusicConductor.Play(Instrument.Violin);
    }

    void StartDash(Vector2 d)
    {
        isDashing = true;
        dashDir = d;
        dashTimer = dashDuration;
        dashCdTimer = dashDuration + dashCooldown;
        dashAvailable = false;
        dashRefillTimer = dashRefillDelay;
        dashBufferTimer = 0f;

        MusicConductor.Play(Instrument.Trumpet);
        dashStartedOnGround = coyoteTimer > 0f && !InWater;   // 水中冲刺不算地面冲刺
        airJumpsUsed = 0;
        varJumpTimer = 0f;
        rb.gravityScale = 0f;
        if (dashHitstop && !InWater) StartCoroutine(Hitstop());   // 水中不放顿帧
        if (dashTrail) StartCoroutine(TrailRoutine());
    }

    void EndDash()
    {
        isDashing = false;
        Vector2 end = dashDir * dashEndSpeed;
        if (dashDir.y > 0.5f) end.y *= endDashUpMult;
        rb.velocity = end;
        if (dashStartedOnGround) groundDashJumpTimer = superJumpWindow;
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