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
    public static bool jumpHeld;   // 跳跃按住（触屏可变跳高用）
    public static float v;         // 垂直轴 -1~1（触屏摇杆：游泳八向 / 下穿平台）
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

    [Tooltip("勾上 = 只要贴着墙（不用蹬出去）就立刻归还二段跳和冲刺。\n关掉 = 只有蹬墙跳时才归还。")]
    public bool wallTouchRefills = true;
    public float wallSlideMaxFall = -3f;
    public float wallJumpHSpeed = 12f;
    public float wallJumpForceTime = 0.15f;
    public float wallJumpFacingTime = 0.2f;

    [Header("游泳")]
    public float swimSpeed = 5.5f;          // 游速
    public float swimAccel = 30f;           // 水中操控加减速
    public float waterDrag = 7f;            // 爆发段（划水/冲刺后）的柔和水阻
    public float swimStrokeSpeed = 11f;     // 跳跃键=划水的速度
    [Tooltip("两次划水的间隔（秒）。0.05 基本等于随时可划，0.35 会明显顿一下。")]
    public float swimStrokeCooldown = 0.05f;
    public float enterWaterDamp = 0.5f;     // 入水瞬间速度衰减

    [Tooltip("游泳时的重力是陆地的百分之多少。\n游泳不等于失重 —— 0.35 = 还会慢慢往下沉，想浮住得一直往上按。\n调大 → 沉得快、更累；调到 1 = 和陆地一样沉。")]
    public float swimGravityScale = 0.35f;

    [Tooltip("游泳时最多能沉多快（格/秒，写负数）。\n-3.0 = 慢慢沉；-6 = 沉得很快。")]
    public float swimMaxFall = -3.0f;

    [Tooltip("按住 ↓ 时主动下潜的最高速度（格/秒）。建议和 Swim Speed 差不多，7~8；" +
             "调大 = 潜得更快，调小 = 潜得慢。\n不按 ↓ 的时候仍然用 Swim Max Fall 慢慢自然下沉。")]
    public float swimDownSpeed = 7.5f;

    [Tooltip("划水后的减速率（格/秒²）。越大 = 一冲就被水吃掉，越大阻力越强。建议 16~26。")]
    public float swimStrokeDrag = 18f;

    [Tooltip("划水后「大阻力」持续的时长（秒）。0.3 左右手感最好；越大越黏。")]
    public float swimStrokeDragTime = 0.3f;

    [Header("水下行走（还没解锁游泳时）")]
    [Tooltip("水里的重力是陆地的百分之多少。\n0.35 = 只保留 35% 重力，掉得很慢但还在掉。\n调大 → 沉得快；调到 1 = 和陆地一样沉。")]
    public float waterGravityScale = 0.35f;

    [Tooltip("水里最多能沉多快（格/秒，写负数）。\n-3.5 = 慢慢沉底；-8 = 沉得很快。")]
    public float waterMaxFall = -3.5f;

    [Tooltip("水里的水平速度是陆地上的百分之多少。\n0.65 = 走路明显变慢但还能走。\n调小 → 更费劲；调到 0.3 = 几乎挪不动。")]
    public float waterMoveMul = 0.65f;

    [Tooltip("水下行走的起跳力气倍率（乘在 Jump Velocity 上，默认 0.8 = 八成力）。\n想跳得高就调大；调到 1 = 和陆地一样。")]
    public float waterJumpMul = 0.8f;

    [Tooltip("水下冲刺的速度（格/秒）。\n和「冲刺」分组里的 Dash Speed 互不影响，改这个只改水里。\n冲刺距离 = 速度 × 时长，想保持距离不变就得两个一起调。")]
    public float waterDashSpeed = 32f;

    [Tooltip("水下冲刺的持续时间（秒）。\n距离 = 速度 × 时长。当前 32 × 0.1125 = 3.6 格（和陆地冲刺一样远）。\n不要低于 0.10，物理帧（50Hz）太少会抖。")]
    public float waterDashDuration = 0.1125f;

    [Tooltip("水下土狼时间（秒）。离开池底/平台边缘后仍能起跳的窗口。\n和陆地的 Coyote Time 完全独立，改这个不影响陆地。\n水下落慢，0.1 可能偏短，0.15 更宽容。")]
    public float waterCoyoteTime = 0.1f;

    [Tooltip("水下按住跳的维持时长（秒）。按住时这段时间内保持起跳速度不掉。\n和陆地的 Var Jump Time 完全独立。")]
    public float waterVarJumpTime = 0.18f;

    [Tooltip("水下松手截断倍率。松手时垂直速度乘这个数，越小掉得越急。\n和陆地的 Jump Cut Multiplier 完全独立。")]
    public float waterJumpCutMultiplier = 0.5f;

    [Tooltip("水下站在池底时的加速度（格/秒²）。越大起步越快。\n和陆地的 Accel 完全独立。")]
    public float waterAccel = 140f;

    [Tooltip("水下站在池底时的减速度（格/秒²）。越大刹车越快。\n和陆地的 Decel 完全独立。")]
    public float waterDecel = 90f;

    [Tooltip("水下离地（悬浮）时的加速度（格/秒²）。\n和陆地的 Air Accel 完全独立。")]
    public float waterAirAccel = 112f;

    [Tooltip("水下离地（悬浮）时的减速度（格/秒²）。\n和陆地的 Air Decel 完全独立。")]
    public float waterAirDecel = 35f;

    [Header("碰撞体跟随朝向")]
    [Tooltip("勾上 = 角色左右翻转时，碰撞体的水平偏移跟着镜像。\n解决「贴图翻了、碰撞体没翻」的问题。\n碰撞体本来居中（Offset X = 0）时，勾不勾都一样。")]
    public bool flipColliderWithFacing = true;

    [Tooltip("角色【朝右】时碰撞体的水平偏移（格），朝左自动取相反数。\n填 0 = 自动使用碰撞体 Inspector 里当前填的 Offset X。")]
    public float colliderFlipOffsetX = 0f;

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
    float colBaseOffsetY;   // v29: 碰撞体原始 Y 偏移
    float colFlipX;         // v29: 朝右时的 X 偏移（朝左取相反数）
    ContactFilter2D groundFilter;
    readonly RaycastHit2D[] hits = new RaycastHit2D[8];
    float airPeakY;                       // v11: 这一跳的空中最高点

    [Header("资源归属规则")]
    [Tooltip("勾上 = 冲刺会把二段跳还给你（旧行为，容易变成无限二段跳）\n不勾 = 冲刺不刷新二段跳（推荐）")]
    public bool dashRefillsJump = false;   // v35: 冲刺是否刷新二段跳

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
    float strokeDragTimer;   // v36: 划水后"大阻力"剩余时间
    bool InWater => waterCount > 0;
    readonly Collider2D[] overlapHits = new Collider2D[8];
    ContactFilter2D waterFilter;

    // ---- v19: 单向平台（水面）下穿 ----
    readonly List<Collider2D> ignoredPlatforms = new List<Collider2D>();
    float dropTimer;

    // ---- v48: 打击感三件套（震屏/压缩拉伸/尘土）----
    PlayerFx2D fx;
    bool wasGrounded;

    public void UnlockDoubleJump() { canDoubleJump = true;  Debug.Log("[能力] 解锁二段跳！"); }
    public void UnlockDash()       { canDash = true;        Debug.Log("[能力] 解锁冲刺！"); }
    public void UnlockWallJump()   { canWallJump = true;    Debug.Log("[能力] 解锁蹬墙跳！"); }
    public void UnlockSwim()       { canSwim = true;        Debug.Log("[能力] 解锁游泳！"); }

    /// <summary>
    /// 这一跳的落差（单位：格）= 空中最高点 - 当前位置。
    /// 刚落地的那一瞬间读它，就是“这次从多高掉下来”。
    /// </summary>
    public float FallDistance { get { return Mathf.Max(0f, airPeakY - rb.position.y); } }

    /// <summary>
    /// v30: 把「跳跃 / 二段跳 / 冲刺」相关的临时状态全部清零。
    /// 重生时由 PlayerHealth2D 调用，避免复活后还剩半个冲刺、二段跳已被用掉等情况。
    /// </summary>
    public void ResetMovementState()
    {
        airJumpsUsed = 0;          // 二段跳归还
        dashAvailable = true;      // 冲刺归还
        dashTimer = 0f;
        dashCdTimer = 0f;
        dashRefillTimer = 0f;
        dashBufferTimer = 0f;
        isDashing = false;

        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        varJumpTimer = 0f;
        wallForceTimer = 0f;
        wallFacingTimer = 0f;
        groundDashJumpTimer = 0f;

        dropTimer = 0f;
        strokeCdTimer = 0f;
        strokeDragTimer = 0f;   // v36

        if (rb != null) rb.velocity = Vector2.zero;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        fx = GetComponent<PlayerFx2D>();   // v48
        rb.freezeRotation = true;

        // v29: 记录碰撞体基础偏移，供「跟随朝向翻转」使用
        colBaseOffsetY = col.offset.y;
        colFlipX = (colliderFlipOffsetX != 0f) ? Mathf.Abs(colliderFlipOffsetX)
                                               : Mathf.Abs(col.offset.x);
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
            // v26: 水下也有土狼时间 —— 刚离开池底/平台边缘的 coyoteTime 秒内
            //      按跳仍按"第一跳"处理，不会掉进下面的二段跳分支
            if (onGround || coyoteTimer > 0f)
            {
                jumpBufferTimer = 0f;
                DoJump(jumpVelocity * waterJumpMul, hInput);
                varJumpTimer = waterVarJumpTime;   // v27: 水下按住跳时长独立
            }
            else if (canDoubleJump && airJumpsUsed < 1)
            {
                jumpBufferTimer = 0f;
                airJumpsUsed++;
                DoJump(airJumpVelocity * waterJumpMul, 0);
                varJumpTimer = waterVarJumpTime;   // v27
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
                        Mathf.Max(rb.velocity.y * waterJumpCutMultiplier, jumpCutMinSpeed));   // v27
            }
        }

        // 水平：陆地那套加减速，但目标速度和加速度都乘 waterMoveMul
        if (wallForceTimer <= 0f)
        {
            float target = hInput * moveSpeed * waterMoveMul;
            // v27: 水下加减速用独立字段，不再借陆地的 Accel/Decel
            float rate = hInput != 0f ? (onGround ? waterAccel : waterAirAccel)
                                      : (onGround ? waterDecel : waterAirDecel);
            rb.velocity = new Vector2(
                Mathf.MoveTowards(rb.velocity.x, target, rate * dt),
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
        if (Mathf.Abs(TouchInput.v) > 0.05f) vRaw = TouchInput.v;          // 触屏摇杆垂直分量优先
        bool jumpHeld = Input.GetButton("Jump") || TouchInput.jumpHeld;    // 触屏按住并入可变跳高

        // ---- 2. 冲刺输入：进缓冲 ----
        if (Input.GetKeyDown(dashKey) || TouchInput.dashPressed)
            dashBufferTimer = dashBufferTime;
        TouchInput.dashPressed = false;

        // ---- 2.5 v15: 水域对账（每帧以实际重叠为准）----
        {
            int n = col.OverlapCollider(waterFilter, overlapHits);
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                Collider2D w = overlapHits[i];
                if (w.GetComponent<WaterZone2D>() == null) continue;

                // v45：只有"整个人都泡在水盒里"才算入水；
                //      头/身体任何一部分露出水面 → 不算水里
                Bounds wb = w.bounds;
                Bounds pb = col.bounds;
                if (wb.Contains(pb.min) && wb.Contains(pb.max)) count++;
            }

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
        if (!grounded && canWallJump && (enableWallSlide || enableWallJump))   // v31: 水中也识别墙面
        {
            if (CastHasSurface(Vector2.right, wallCheckDist, 0.5f))      wallDir = 1;
            else if (CastHasSurface(Vector2.left, wallCheckDist, 0.5f))  wallDir = -1;

            // v46：贴着墙就把两种资源还满（不要求蹬出去）
            if (wallTouchRefills && wallDir != 0)
            {
                airJumpsUsed = 0;
                dashAvailable = true;
            }
        }

        // v48: 落地 → 尘土 + 压缩（落差越大越猛；必须在 airPeakY 重置前读落差）
        if (grounded && !wasGrounded && fx != null && !InWater)
            fx.NotifyLanded(Mathf.Clamp01(FallDistance / 6f));
        wasGrounded = grounded;

        // v11: 记录“这一跳的空中最高点”
        if (grounded) airPeakY = rb.position.y;
        else airPeakY = Mathf.Max(airPeakY, rb.position.y);

        // ---- 4. 落地：刷新土狼/二段跳；冲刺延迟后填充 ----
        if (grounded && !isDashing)
        {
            coyoteTimer = InWater ? waterCoyoteTime : coyoteTime;   // v27: 水下土狼独立
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
        // v38: 游泳中冲刺键交给"划水"，不再执行普通冲刺
        if (dashBufferTimer > 0f && canDash && dashAvailable && !isDashing && dashCdTimer <= 0f
            && !(InWater && canSwim))
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
            rb.velocity = dashDir * (InWater ? waterDashSpeed : dashSpeed);   // v25: 水下冲刺速度独立
            if (dashTimer <= 0f) EndDash();
        }
        else if (InWater && !canSwim && enableWallJump && wallDir != 0 && jumpBufferTimer > 0f)
        {
            // ================= v31：水下蹬墙跳 =================
            // 贴着墙按跳 = 蹬墙，优先于普通跳和划水。
            DoWallJump(wallDir, dir);
        }
        else if (InWater && !canSwim)
        {
            // ================= v23：水下行走 =================
            // 没解锁游泳：水里全程走这一套（只是减速，不能游）。
            WaterWalk(dt, dir, grounded, jumpHeld);
        }
        else if (InWater)
        {
            // ================= v22：游泳（仍有重力，会慢慢下沉）=================
            if (rb.velocity.y <= 5f) varJumpTimer = 0f;   // 从水里起跳的上升期不掐可变跳高

            // v45：站在池底时，别让水重力一直把人往地面里按（否则会被顶起来，看着像抽搐）
            if (grounded && rb.velocity.y < 0f)
                rb.velocity = new Vector2(rb.velocity.x, 0f);

            // v45：池底按跳跃键 = 起跳（空中不响应，空中用划水）
            if (grounded && jumpBufferTimer > 0f && strokeCdTimer <= 0f)
            {
                jumpBufferTimer = 0f;
                DoJump(jumpVelocity * waterJumpMul, dir);
                varJumpTimer = waterVarJumpTime;
                MusicConductor.Play(Instrument.Harp);
            }

            // 重力：游泳不会让你失重，只是沉得慢
            rb.gravityScale = gravityScale * swimGravityScale;

            Vector2 swimDir = new Vector2(dir, vRaw);
            if (swimDir.sqrMagnitude > 0.01f) swimDir.Normalize();

            // ===== v38：八方向划水，改由【冲刺键】触发 =====
            //   跳跃键不再划水；没按方向 = 向上划，按了 = 朝那个方向划
            if (strokeCdTimer <= 0f && dashBufferTimer > 0f)
            {
                dashBufferTimer = 0f;
                Vector2 sv = swimDir.sqrMagnitude > 0.01f ? swimDir : Vector2.up;
                rb.velocity = sv * swimStrokeSpeed;
                strokeCdTimer = Mathf.Max(0.02f, swimStrokeCooldown);
                strokeDragTimer = swimStrokeDragTime;       // 开启大阻力窗口
                MusicConductor.Play(Instrument.Harp);
            }
            else
            {
                // 大阻力窗口内用 swimStrokeDrag 减速，之后回到常规 swimAccel
                float rate = (strokeDragTimer > 0f) ? swimStrokeDrag : swimAccel;
                float brake = Mathf.Max(waterDrag, rate * 0.5f);

                float newX = Mathf.Abs(dir) > 0.01f
                    ? Mathf.MoveTowards(rb.velocity.x, dir * swimSpeed, rate * dt)
                    : Mathf.MoveTowards(rb.velocity.x, 0f, brake * dt);

                float newY = rb.velocity.y;
                if (Mathf.Abs(vRaw) > 0.01f)
                    newY = Mathf.MoveTowards(newY, vRaw * swimSpeed, rate * dt);
                else if (strokeDragTimer > 0f)
                    newY = Mathf.MoveTowards(newY, 0f, rate * dt);   // 划完迅速失速

                // v37：主动按 ↓ 时允许潜到 swimDownSpeed，不按才受 swimMaxFall 限制
                float downLimit = (vRaw < -0.01f) ? -Mathf.Abs(swimDownSpeed) : swimMaxFall;
                if (newY < downLimit) newY = downLimit;

                rb.velocity = new Vector2(newX, newY);
            }

            strokeDragTimer -= dt;
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
                if (fx != null) fx.NotifyDoubleJumped();   // v48: 二段跳涟漪
                DoJump(airJumpVelocity, 0);
            }
        }

        // ---- 13. 朝向 ----
        if (dir != 0 && wallFacingTimer <= 0f) SetFacing(dir < 0);   // v29
    }

    // v29: 统一设置朝向 —— 图和碰撞体偏移一起翻
    // v47: 改成 public，过场动画/剧情也能强制转身
    public void SetFacing(bool faceLeft)
    {
        if (sr != null) sr.flipX = faceLeft;

        if (!flipColliderWithFacing || col == null) return;
        col.offset = new Vector2(faceLeft ? -colFlipX : colFlipX, colBaseOffsetY);
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
        if (fx != null) fx.NotifyJumped();   // v48: 起跳拉长+踢尘
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
        if (fx != null) fx.NotifyJumped();   // v48
        Debug.Log("[超级跳]");
    }

    // 蹬墙跳：只在按住方向时锁定水平（v6 修正）
    void DoWallJump(int wallDirSide, int dirAtJump)
    {
        rb.velocity = new Vector2(-wallDirSide * wallJumpHSpeed, jumpVelocity);
        currentJumpSpeed = jumpVelocity;
        varJumpTimer = InWater ? waterVarJumpTime : varJumpTime;   // v31: 水中用独立时长
        wallForceTimer = dirAtJump != 0 ? wallJumpForceTime : 0f;
        airJumpsUsed = 0;
        dashAvailable = true;
        jumpBufferTimer = 0f;
        SetFacing(wallDirSide != 1);   // v29
        wallFacingTimer = wallJumpFacingTime;

        MusicConductor.Play(Instrument.Violin);
        if (fx != null) fx.NotifyJumped();   // v48: 蹬墙跳也拉一下
    }

    void StartDash(Vector2 d)
    {
        isDashing = true;
        dashDir = d;
        float useDur = InWater ? waterDashDuration : dashDuration;   // v25: 水下冲刺用独立时长
        dashTimer = useDur;
        dashCdTimer = useDur + dashCooldown;
        dashAvailable = false;
        dashRefillTimer = dashRefillDelay;
        dashBufferTimer = 0f;

        MusicConductor.Play(Instrument.Trumpet);
        if (fx != null) fx.NotifyDashed(d);  // v48: 冲刺震屏+喷尘
        dashStartedOnGround = coyoteTimer > 0f && !InWater;   // 水中冲刺不算地面冲刺
        if (dashRefillsJump) airJumpsUsed = 0;                // v35: 默认不刷新二段跳
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