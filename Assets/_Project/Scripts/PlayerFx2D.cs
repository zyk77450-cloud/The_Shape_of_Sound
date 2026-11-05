using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PixelFx;

/// <summary>
/// 玩家打击感三件套：冲刺震屏 + 落地压缩/起跳拉伸 + 尘土（PixelFX 移植版）。
/// 挂在 Player 上（和 PlayerController2D 同物体），由控制器在
/// 落地/起跳/冲刺的瞬间调用 NotifyLanded / NotifyJumped / NotifyDashed。
/// 尘土需要场景里有 PixelFxLayer2D；没有它，震屏和形变照常工作。
/// </summary>
[DisallowMultipleComponent]
public class PlayerFx2D : MonoBehaviour, IPixelFxDrawer
{
    [Header("冲刺震屏")]
    public bool dashShake = true;
    public float dashShakeDuration = 0.12f;
    public float dashShakeMagnitude = 0.10f;    // 单位：格

    [Header("落地压缩 / 起跳拉伸")]
    [Tooltip("形变作用对象。留空 = Player 根物体（碰撞体会跟着压 0.16 秒）；推荐把图挂在子物体后拖进来")]
    public Transform shapeTarget;
    public float landSquashY  = 0.62f;   // 落地压扁到
    public float landSquashX  = 1.30f;
    public float jumpStretchY = 1.28f;   // 起跳拉长到
    public float jumpStretchX = 0.82f;
    public float shapeTime    = 0.16f;   // 恢复时长

    [Header("尘土")]
    public DustWorldFx.Settings dust = new DustWorldFx.Settings();
    [Tooltip("跑步尘的地面层（和 PlayerController2D 的 Ground 一致）")]
    public LayerMask groundMask;

    [Header("声波特效（声形主题：二段跳涟漪）")]
    [Tooltip("拖 P_Ripple_Jump 变体（小圈快散）；留空 = 二段跳没有涟漪")]
    public SoundWave2D doubleJumpRipple;

    [Header("速度线（冲刺拖尾 / 二段跳托升气流）")]
    public StreakWorldFx.Settings streaks = new StreakWorldFx.Settings();

    Rigidbody2D rb;
    Collider2D col;
    SpriteRenderer sr;
    PlayerController2D pc;
    DustWorldFx fx;
    StreakWorldFx streakFx;
    float trailTimer;
    Coroutine shapeRoutine;
    float lastLandTime = -1f;

    void Awake()
    {
        rb  = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        sr  = GetComponent<SpriteRenderer>();
        pc  = GetComponent<PlayerController2D>();
        fx  = new DustWorldFx(dust);
        streakFx = new StreakWorldFx(streaks);
    }

    void Start()
    {
        if (groundMask.value == 0)
            Debug.LogWarning("[PlayerFx2D] Ground Mask 没设，跑步尘不会出现。把它选成 Ground 层。");
    }

    void OnEnable()  { if (PixelFxLayer2D.Instance != null) PixelFxLayer2D.Instance.Register(this); }
    void OnDisable() { if (PixelFxLayer2D.Instance != null) PixelFxLayer2D.Instance.Unregister(this); }

    Vector2 FeetPos => new Vector2(col.bounds.center.x, col.bounds.min.y);

    // ================= 由 PlayerController2D 调用的三个入口 =================

    /// 落地：strength 0~1（按落差折算），压扁 + 尘土
    public void NotifyLanded(float strength)
    {
        if (Time.unscaledTime - lastLandTime < 0.25f) return;   // 防抖：短时间二次落地不重复
        lastLandTime = Time.unscaledTime;

        float s = Mathf.Clamp01(strength);
        SetShape(Mathf.Lerp(1.1f, landSquashX, s), Mathf.Lerp(0.86f, landSquashY, s));
        fx.EmitLanding(FeetPos, 0.35f + 0.65f * s);
    }

    /// 起跳（普通/二段/超级/蹬墙共用）：拉长 + 轻尘（0.45 = 起跳强度）
    public void NotifyJumped()
    {
        SetShape(jumpStretchX, jumpStretchY);
        fx.EmitLanding(FeetPos, 0.45f);
    }

    /// 冲刺：震屏 + 速度线拖尾 + 朝反方向喷尘
    public void NotifyDashed(Vector2 dir)
    {
        if (dashShake) CameraShake2D.Shake(dashShakeDuration, dashShakeMagnitude);
        trailTimer = (pc != null ? pc.dashDuration : 0.15f) + 0.06f;   // 拖尾窗口跟着冲刺时长走
        fx.EmitDash(FeetPos, dir);
    }

    /// 二段跳：脚底扇形气流（被托起来）+ 身体中心声音涟漪
    public void NotifyDoubleJumped()
    {
        streakFx.EmitLift(FeetPos);
        SpawnRipple(doubleJumpRipple, rb.position);
    }

    // 纯装饰声波：无论预制体怎么配，绝不通知机关/敌人（防玩法泄漏——
    // 跳一下就点亮幽灵平台/标红敌人就穿帮了）
    void SpawnRipple(SoundWave2D prefab, Vector2 pos)
    {
        if (prefab == null) return;
        var w = Instantiate(prefab, pos, Quaternion.identity);
        w.notifyReceivers = false;
        w.Init(pos);
    }

    // ================= 自动档：跑步尘 =================

    void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 20f);

        // 地面探测：贴地才发跑步尘，顺便拿地面高度（尘土不沉地）
        var hit = Physics2D.Raycast(FeetPos + Vector2.up * 0.1f, Vector2.down, 0.25f, groundMask);
        bool grounded = hit.collider != null;
        if (grounded) fx.FloorY = hit.point.y + 0.02f;
        else fx.FloorY = float.MinValue;

        if (grounded && rb != null)
        {
            int facing = sr != null && sr.flipX ? -1 : 1;
            float maxSpeed = pc != null ? Mathf.Max(1f, pc.moveSpeed) : 8f;
            float speedRatio = Mathf.Clamp01(Mathf.Abs(rb.velocity.x) / maxSpeed);
            fx.UpdateRunning(FeetPos, facing, speedRatio, dt);
        }
        else fx.UpdateRunning(FeetPos, 1, 0f, dt);   // 空中重置步频

        // 冲刺拖尾：窗口内每帧在身后留两条速度线（原地留在世界 = 拖尾感）
        if (trailTimer > 0f)
        {
            trailTimer -= dt;
            streakFx.EmitTrail(rb.position, rb.velocity);
            streakFx.EmitTrail(rb.position, rb.velocity);
        }

        fx.Update(dt);
        streakFx.Update(dt);
    }

    // ================= 压缩/拉伸 =================

    void SetShape(float sx, float sy)
    {
        Transform t = shapeTarget != null ? shapeTarget : transform;
        if (shapeRoutine != null) StopCoroutine(shapeRoutine);
        shapeRoutine = StartCoroutine(ShapeRoutine(t, sx, sy, shapeTime));
    }

    static IEnumerator ShapeRoutine(Transform t, float sx, float sy, float dur)
    {
        t.localScale = new Vector3(sx, sy, 1f);
        float time = 0f;
        while (time < 1f)
        {
            time += Time.deltaTime / dur;
            float k = 1f - (1f - time) * (1f - time);      // ease-out 弹回
            t.localScale = new Vector3(Mathf.Lerp(sx, 1f, k), Mathf.Lerp(sy, 1f, k), 1f);
            yield return null;
        }
        t.localScale = Vector3.one;
    }

    // ================= 画到像素层 =================

    public void PixelDraw(PixelFrame frame, Camera cam)
    {
        fx.Draw(frame, cam);
        streakFx.Draw(frame, cam);
    }
}

/// <summary>
/// DustFx（PixelFX 会话版）的世界坐标移植：模拟在世界单位里跑，
/// 绘制时才转成帧像素 —— 相机怎么动，尘土都钉在原地。
/// 物理量已从帧像素换算成世界单位（÷16，PPU16）。
/// </summary>
public sealed class DustWorldFx
{
    [System.Serializable]
    public class Settings
    {
        [Header("颜色")]
        public Color32 dustColor   = new Color32(178, 156, 128, 255);
        public Color32 darkColor   = new Color32(112, 92, 70, 255);
        public Color32 debrisColor = new Color32(138, 114, 88, 255);

        [Header("落地尘")]
        public int   landingPuffs  = 14;
        public int   landingDebris = 6;
        public float landingSpeed  = 3.0f;   // 向两侧喷开（世界单位/秒）
        public float landingLift   = 1.7f;
        public float landingLife   = 0.8f;
        public float landingRadius = 4.0f;   // 绘制半径（帧像素）

        [Header("跑步尘")]
        public int   stepPuffs       = 4;
        public float stepSpeed       = 1.0f;
        public float stepLift        = 0.75f;
        public float stepLife        = 0.45f;
        public float stepRadius      = 2.6f;
        public float stepIntervalMin = 0.2f;
        public float stepIntervalMax = 0.4f;

        [Header("物理（世界单位）")]
        public float gravity       = 3.8f;
        public float debrisGravity = 14.4f;
        public float drag = 3.2f;
        public float growth = 1.9f;

        [Header("光照")]
        public float glow = 0.12f;
        public float glowRadius = 4f;
    }

    struct Particle
    {
        public float x, y, vx, vy;
        public float radius, age, life;
        public bool debris;
    }

    readonly Settings _s;
    readonly List<Particle> _alive = new List<Particle>(256);
    float _stepTimer;

    public int Count => _alive.Count;

    /// 地面世界高度：设了之后尘土不沉进地面。默认不限制。
    public float FloorY { get; set; } = float.MinValue;

    public DustWorldFx(Settings settings) => _s = settings;

    public void EmitLanding(Vector2 feetPos, float strength = 1f)
    {
        strength = Mathf.Clamp01(strength);
        float power = 0.6f + 0.4f * strength;

        // 尘雾：左右交替向外喷，贴地的多、扬起的少
        int puffs = Mathf.Max(4, Mathf.RoundToInt(_s.landingPuffs * (0.6f + 0.6f * strength)));
        for (int i = 0; i < puffs; i++)
        {
            float side = (i % 2 == 0) ? 1f : -1f;
            float spd = _s.landingSpeed * power * Random.Range(0.45f, 1.1f);
            AddPuff(feetPos.x + side * Random.Range(0.03f, 0.2f),
                    feetPos.y + Random.Range(0f, 0.1f),
                    side * spd,
                    _s.landingLift * power * Random.Range(0.25f, 1f),
                    _s.landingRadius * Random.Range(0.65f, 1.35f) * (0.8f + 0.3f * strength),
                    _s.landingLife * Random.Range(0.7f, 1.25f));
        }

        // 碎屑：小硬块抛物线飞出
        int debrisCount = Mathf.RoundToInt(_s.landingDebris * (0.3f + 0.7f * strength));
        for (int i = 0; i < debrisCount; i++)
        {
            float side = (i % 2 == 0) ? 1f : -1f;
            AddDebris(feetPos.x, feetPos.y + 0.06f,
                      side * Random.Range(1.1f, 4.4f) * power,
                      Random.Range(2.8f, 6.9f) * power,
                      Random.Range(0.5f, 0.9f),
                      Random.Range(0.5f, 0.9f));
        }
    }

    /// 跑步尘：每迈一步调一次。facingDir = 角色朝向（1 右 / -1 左），尘土往身后踢。
    public void EmitFootstep(Vector2 feetPos, int facingDir)
    {
        float back = facingDir >= 0 ? -1f : 1f;
        for (int i = 0; i < _s.stepPuffs; i++)
            AddPuff(feetPos.x + back * Random.Range(0f, 0.13f),
                    feetPos.y + Random.Range(0f, 0.06f),
                    back * _s.stepSpeed * Random.Range(0.4f, 1.1f),
                    _s.stepLift * Random.Range(0.3f, 1f),
                    _s.stepRadius * Random.Range(0.7f, 1.25f),
                    _s.stepLife * Random.Range(0.7f, 1.2f));
    }

    /// 冲刺喷尘：朝冲刺反方向踢一团
    public void EmitDash(Vector2 feetPos, Vector2 dir)
    {
        Vector2 back = -dir.normalized;
        for (int i = 0; i < 8; i++)
            AddPuff(feetPos.x + back.x * Random.Range(0f, 0.2f) + Random.Range(-0.06f, 0.06f),
                    feetPos.y + Random.Range(0f, 0.1f),
                    back.x * _s.landingSpeed * Random.Range(0.6f, 1.2f),
                    _s.stepLift * Random.Range(0.5f, 1.2f),
                    _s.stepRadius * Random.Range(0.8f, 1.4f),
                    _s.stepLife * Random.Range(0.8f, 1.3f));
        for (int i = 0; i < 3; i++)
            AddDebris(feetPos.x, feetPos.y + 0.06f,
                      back.x * Random.Range(1f, 3f),
                      Random.Range(1.5f, 3.5f),
                      Random.Range(0.5f, 0.8f), Random.Range(0.4f, 0.7f));
    }

    /// 跑步尘自动档：每帧调用，内部按步频自动发 EmitFootstep。
    public void UpdateRunning(Vector2 feetPos, int facingDir, float speedRatio, float dt)
    {
        if (speedRatio <= 0.05f) { _stepTimer = 0f; return; }   // 一停下就攒好下一步
        _stepTimer -= dt;
        if (_stepTimer <= 0f)
        {
            EmitFootstep(feetPos, facingDir);
            _stepTimer = Mathf.Lerp(_s.stepIntervalMax, _s.stepIntervalMin, Mathf.Clamp01(speedRatio));
        }
    }

    public void Update(float dt)
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            Particle p = _alive[i];
            p.age += dt;
            if (p.age >= p.life)
            { _alive[i] = _alive[_alive.Count - 1]; _alive.RemoveAt(_alive.Count - 1); continue; }

            float drag = Mathf.Exp(-_s.drag * dt);
            p.vx *= drag;
            p.vy = p.vy * drag - (p.debris ? _s.debrisGravity : _s.gravity) * dt;
            p.x += p.vx * dt;
            p.y += p.vy * dt;

            // 不让尘土沉进地面：贴地滑行 + 摩擦
            if (FloorY > float.MinValue && p.y < FloorY)
            { p.y = FloorY; p.vy = 0f; p.vx *= p.debris ? 0.4f : 0.7f; }

            _alive[i] = p;
        }
    }

    public void Draw(PixelFrame frame, Camera cam)
    {
        for (int i = 0; i < _alive.Count; i++)
        {
            Particle p = _alive[i];
            Vector2 f = PixelFxLayer2D.WorldToPixel(cam, frame, new Vector3(p.x, p.y, 0f));
            float t = p.age / p.life;
            float fade = Mathf.Clamp01(p.age / 0.07f) * (1f - t);  // 快速淡入 → 线性淡出

            if (p.debris) DrawDebris(frame, f, fade);
            else
            {
                float r = p.radius * (1f + (_s.growth - 1f) * t);  // 越散越大
                Color32 c = Color32.Lerp(_s.dustColor, _s.darkColor, t);
                DrawPuff(frame, f.x, f.y, r, c, fade);
                AddGlow(frame, f.x, f.y, r, fade);
            }
        }
    }

    void AddPuff(float x, float y, float vx, float vy, float radius, float life) =>
        _alive.Add(new Particle { x = x, y = y, vx = vx, vy = vy, radius = radius, age = 0f, life = life, debris = false });

    void AddDebris(float x, float y, float vx, float vy, float radius, float life) =>
        _alive.Add(new Particle { x = x, y = y, vx = vx, vy = vy, radius = radius, age = 0f, life = life, debris = true });

    // 软圆尘雾：中心实、边缘虚；低透明度棋盘抖动保留像素颗粒感
    void DrawPuff(PixelFrame f, float cx, float cy, float r, Color32 c, float fade)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1f));
        int x1 = Mathf.Min(f.Width - 1, Mathf.CeilToInt(cx + r + 1f));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1f));
        int y1 = Mathf.Min(f.Height - 1, Mathf.CeilToInt(cy + r + 1f));

        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                float d2 = dx * dx + dy * dy;
                if (d2 > r * r) continue;

                float a = 1f - Mathf.Sqrt(d2) / r;
                a *= a;              // 边缘快速变虚
                a *= fade;

                int alpha = Mathf.RoundToInt(a * 255f);
                if (alpha < 64)      // 半透明棋盘抖动 = 像素颗粒
                { if (((px + py) & 1) == 0) continue; alpha = Mathf.Min(255, alpha * 2 + 40); }

                Color32 src = c; src.a = (byte)alpha;
                PixelFrame.Blend(f.Color, py * f.Width + px, src);
            }
    }

    void DrawDebris(PixelFrame f, Vector2 pos, float fade)
    {
        int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y);
        Color32 c = _s.debrisColor; c.a = (byte)(fade * 255f);
        if (f.Contains(x, y)) PixelFrame.Blend(f.Color, f.Index(x, y), c);
    }

    void AddGlow(PixelFrame f, float cx, float cy, float r, float fade)
    {
        float strength = _s.glow * fade;
        if (strength <= 0f) return;
        float gr = Mathf.Max(_s.glowRadius, r + 1f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - gr));
        int x1 = Mathf.Min(f.Width - 1, Mathf.CeilToInt(cx + gr));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - gr));
        int y1 = Mathf.Min(f.Height - 1, Mathf.CeilToInt(cy + gr));

        for (int py = y0; py <= y1; py++)
            for (int px = x0; px <= x1; px++)
            {
                float dx = px + 0.5f - cx, dy = py + 0.5f - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d >= gr) continue;
                int idx = py * f.Width + px;
                f.Light[idx] = Mathf.Min(1f, f.Light[idx] + strength * (1f - d / gr));
            }
    }
}

/// <summary>
/// 线状粒子（策划参考：空洞骑士冲刺拖尾）：冲刺速度线 + 二段跳脚底托升气流。
/// 世界坐标模拟（线留在原地、人走开 = 拖尾/托起感），像素层绘制。
/// </summary>
public sealed class StreakWorldFx
{
    [System.Serializable]
    public class Settings
    {
        [Header("颜色")]
        public Color32 color = new Color32(225, 248, 255, 230);   // 青白，贴回声色

        [Header("冲刺拖尾")]
        public float dashStreakLenMin = 0.5f;   // 世界单位
        public float dashStreakLenMax = 1.0f;
        public float dashStreakLife  = 0.22f;
        [Range(1, 3)] public int thickness = 1;

        [Header("二段跳托升（脚底扇形气流）")]
        public int   liftCount = 7;
        public float liftLen = 0.45f;
        public float liftLife = 0.28f;
        public float liftSpread = 100f;         // 扇形张角（度）
    }

    struct Streak { public float x, y, dx, dy, len, age, life; }

    readonly Settings _s;
    readonly List<Streak> _alive = new List<Streak>(64);

    public StreakWorldFx(Settings s) => _s = s;

    /// 冲刺拖尾：沿运动方向一条细线（原地留在世界 = 人走线留）
    public void EmitTrail(Vector2 pos, Vector2 dir)
    {
        Vector2 d = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
        float len = Random.Range(_s.dashStreakLenMin, _s.dashStreakLenMax);
        _alive.Add(new Streak {
            x = pos.x + d.x * len * 0.4f,
            y = pos.y + d.y * len * 0.4f + Random.Range(-0.15f, 0.15f),
            dx = d.x, dy = d.y,
            len = len,
            age = 0f, life = _s.dashStreakLife * Random.Range(0.8f, 1.2f),
        });
    }

    /// 二段跳：脚底往斜下方喷的扇形气流（人往上走，气流留在原地 = 被托起来）
    public void EmitLift(Vector2 feetPos)
    {
        for (int i = 0; i < _s.liftCount; i++)
        {
            float k = _s.liftCount == 1 ? 0.5f : i / (float)(_s.liftCount - 1);
            float a = (-90f + Mathf.Lerp(-_s.liftSpread * 0.5f, _s.liftSpread * 0.5f, k)) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));   // 往下扇开
            _alive.Add(new Streak {
                x = feetPos.x + d.x * 0.1f,
                y = feetPos.y - 0.05f,
                dx = d.x, dy = d.y,
                len = _s.liftLen * Random.Range(0.7f, 1.2f),
                age = 0f, life = _s.liftLife * Random.Range(0.8f, 1.2f),
            });
        }
    }

    public void Update(float dt)
    {
        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            Streak s = _alive[i];
            s.age += dt;
            if (s.age >= s.life)
            { _alive[i] = _alive[_alive.Count - 1]; _alive.RemoveAt(_alive.Count - 1); continue; }
            _alive[i] = s;
        }
    }

    public void Draw(PixelFrame frame, Camera cam)
    {
        for (int i = 0; i < _alive.Count; i++)
        {
            Streak s = _alive[i];
            float fade = Mathf.Clamp01(s.age / 0.05f) * (1f - s.age / s.life);
            // 两端点世界坐标 → 帧像素，连线画
            Vector2 a = PixelFxLayer2D.WorldToPixel(cam, frame,
                new Vector3(s.x - s.dx * s.len * 0.5f, s.y - s.dy * s.len * 0.5f, 0));
            Vector2 b = PixelFxLayer2D.WorldToPixel(cam, frame,
                new Vector3(s.x + s.dx * s.len * 0.5f, s.y + s.dy * s.len * 0.5f, 0));
            DrawLine(frame, a, b, fade);
        }
    }

    void DrawLine(PixelFrame f, Vector2 a, Vector2 b, float fade)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Mathf.Max(2, Mathf.CeilToInt(dist * 2f));
        Color32 c = _s.color;
        c.a = (byte)(c.a * fade);
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
            int px = Mathf.RoundToInt(p.x), py = Mathf.RoundToInt(p.y);
            for (int t = 0; t < _s.thickness; t++)
                if (f.Contains(px, py + t))
                    PixelFrame.Blend(f.Color, f.Index(px, py + t), c);
        }
    }
}
