using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 表盘指针 + 半圆刻度：跟着 ClockProgress 的进度转，满格时触发事件。
///
/// 设计约定（本工程）：
///   · 刻度是「半圆」，由 Arc Start Angle（起点，默认 180°=正左）
///     和 Arc Span Angle（整个跨度，默认 180°）决定，5 个刻度按跨度均分
///   · 进度从 1 开始（第 1 格开场就是亮的），满格 = 5
///   · 只有玩家在指示器周围一定像素范围内弹奏才算数
///   · 5 秒没收到弹奏信号 → 自动回到第 1 格
///
/// 它不改 ClockProgress 的任何逻辑：
///   · 刻度块仍由 ClockProgress 生成与上色
///   · 本脚本只做两件事：把刻度块重新摆到半圆上、让指针指着当前格
/// </summary>
[DisallowMultipleComponent]
public class DialPointer2D : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("表盘：拖 ClockDial 物体（它身上挂着 Clock Progress 组件）。\n留空则自动从自己或父物体上找。")]
    public ClockProgress clock;

    [Tooltip("指针：拖 zhizhen 物体。要求指针在 0° 时朝正上方（12 点方向）。")]
    public Transform needle;

    [Tooltip("玩家：留空则自动按 Tag = Player 查找。用来判断弹奏时玩家是否在范围内。")]
    public Transform player;

    [Header("刻度排布（半圆）")]
    [Tooltip("勾上 = 把 ClockProgress 生成的刻度块重新摆成半圆。\n取消勾选则保持它原来的整圆排布。")]
    public bool layoutAsSemiCircle = true;

    [Tooltip("半圆从哪个角度起步（度）。180 = 正左方；150 = 往左上抬；210 = 往左下压。")]
    public float arcStartAngle = 180f;

    [Tooltip("整个半圆跨多少度（这是你要调的那个）。\n180 = 标准半圆；120 = 更扁；90 = 四分之一圆；270 = 大半个圆。\n刻度会在这一整段里自动均分。")]
    public float arcSpanAngle = 180f;

    [Tooltip("刻度点离表盘圆心的距离（格）。\n0 = 自动用 ClockProgress 上的 Dial Radius（现在是 1.6）；填 2.2 就推到更外圈。")]
    public float tickOrbitRadius = 0f;

    [Tooltip("刻度点的大小倍率。1 = 原始大小；0.7 更精致；1.5 更粗壮。")]
    public float tickSizeScale = 1f;

    [Header("进度规则")]
    [Range(0, 10)]
    [Tooltip("开局自动点亮到第几格。1 = 一开始就有一格是亮的；归零也回到这个值。")]
    public int baselineProgress = 1;

    [Header("弹奏范围")]
    [Tooltip("玩家离指示器多少像素以内弹奏才算数。500 像素 ÷ 16 = 31.25 格。\n0 = 不限制距离。")]
    public float rangeInPixels = 500f;

    [Tooltip("工程的 PPU（像素/格）。本工程是 16，别改。")]
    public float pixelsPerUnit = 16f;

    [Header("空闲归零")]
    [Tooltip("多久没有收到有效弹奏就自动归零（秒）。5 = 最后一次弹奏后 5 秒内没有新信号就掉回第 1 格。\n0 = 不自动归零。")]
    public float idleResetTime = 5f;

    [Tooltip("满格之后还要不要自动归零。\n不勾 = 满格后保持（等切场景或手动 Reset）；勾上 = 满格 5 秒后也会掉回第 1 格。")]
    public bool resetAfterComplete = false;

    [Header("转动手感")]
    [Tooltip("指针转一格用多少秒。0.35 有「咔哒」感，0.15 很急促，0.6 很拖沓。")]
    public float stepDuration = 0.35f;

    [Tooltip("勾上 = 缓入缓出；不勾 = 匀速转，更像机械表。")]
    public bool ease = true;

    [Tooltip("到位后多转一点再弹回来的角度。0 = 不回弹；3 左右有「咔哒」过冲感。")]
    public float overshoot = 3f;

    [Header("音效（可选）")]
    [Tooltip("播放滴答声的 AudioSource。留空则不响。")]
    public AudioSource audioSource;

    [Tooltip("指针每转完一格的滴答音效。留空则不响。")]
    public AudioClip tickClip;

    [Range(0f, 1f)]
    [Tooltip("滴答音量。0.6 比较自然。")]
    public float tickVolume = 0.6f;

    [Header("事件")]
    [Tooltip("每转完一格触发一次。可以接刻度音效、飘字提示。")]
    public UnityEvent onStepTurned;

    [Tooltip("指针转到最后一格（= 表盘满）时触发一次。\n把 Animation_1 的 CutsceneFlood.Play 拖到这里即可。")]
    public UnityEvent onReachedEnd;

    [Tooltip("玩家在范围外弹奏时触发。可以接「太远了」的提示音或文字。")]
    public UnityEvent onOutOfRange;

    [Header("调试")]
    [Tooltip("勾上 = 在 Console 打印每次转动、范围判定、自动归零，排查用。")]
    public bool debugLog = false;

    // ---------- 内部状态 ----------
    float baseZ;
    float currentZ;
    Coroutine turning;
    bool reachedEndFired;
    float lastInputTime;

    void Awake()
    {
        if (needle == null) needle = transform;
        baseZ = needle.localEulerAngles.z;
        currentZ = baseZ;

        if (clock == null) clock = GetComponent<ClockProgress>();
        if (clock == null) clock = GetComponentInParent<ClockProgress>();
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void Start()
    {
        if (clock == null)
        {
            Debug.LogWarning("[指针] 场景里找不到 ClockProgress，指针系统不工作。");
            return;
        }

        if (layoutAsSemiCircle) RelayoutSegments();

        clock.onProgressChanged.AddListener(OnProgressChanged);

        EnsureBaseline();
        SyncImmediate();
    }

    void OnDestroy()
    {
        if (clock != null) clock.onProgressChanged.RemoveListener(OnProgressChanged);
    }

    // ============ 对外接口（乐器系统调用） ============

    /// <summary>
    /// 弹奏一次乐器：玩家在范围内才有效 → 表盘 +1、指针转一格。
    /// 未来乐器系统做好后，直接调这个方法。
    /// </summary>
    public void PlayInstrument()
    {
        if (clock == null)
        {
            Debug.LogWarning("[指针] 没有 ClockProgress，弹奏无效。");
            return;
        }

        if (!IsPlayerInRange())
        {
            if (debugLog) Debug.Log("[指针] 玩家不在范围内（" + rangeInPixels + " 像素），此次弹奏无效。");
            onOutOfRange.Invoke();
            return;
        }

        if (clock.Current >= clock.targetProgress)
        {
            if (debugLog) Debug.Log("[指针] 表盘已满，不再增加。");
            return;
        }

        lastInputTime = Time.time;
        clock.AddProgress();
    }

    /// <summary>表盘回到 baselineProgress（默认第 1 格），指针转回对应刻度。</summary>
    public void ResetDial()
    {
        if (clock == null) return;
        clock.ResetProgress();
        EnsureBaseline();
        lastInputTime = Time.time;
    }

    /// <summary>玩家是否在指示器的弹奏范围内。</summary>
    public bool IsPlayerInRange()
    {
        if (rangeInPixels <= 0f) return true;
        if (player == null) return true;
        float worldRange = rangeInPixels / Mathf.Max(1f, pixelsPerUnit);
        return Vector2.Distance(player.position, transform.position) <= worldRange;
    }

    // ============ 刻度排布 ============

    Vector3[] segBaseScale;      // 记录刻度块原始大小，避免倍率相乘
    float lastRadius = -1f;
    float lastSize = -1f;
    float lastA0 = 999f;
    float lastStep = 999f;
    float lastSpan = 999f;

    [ContextMenu("重新排布刻度")]
    public void RelayoutTicks()
    {
        if (!layoutAsSemiCircle || clock == null) return;
        RelayoutSegments();
    }

    void RelayoutSegments()
    {
        var segs = new SpriteRenderer[clock.targetProgress];
        int found = 0;
        for (int i = 0; i < clock.transform.childCount && found < segs.Length; i++)
        {
            var child = clock.transform.GetChild(i);
            if (!child.name.StartsWith("Seg_")) continue;
            var sr = child.GetComponent<SpriteRenderer>();
            if (sr == null) continue;
            segs[found] = sr;
            found++;
        }

        if (found == 0)
        {
            if (debugLog) Debug.LogWarning("[指针] 没找到 Seg_ 开头的刻度块，跳过半圆排布。");
            return;
        }

        // 半径：0 = 跟随 ClockProgress；否则用本脚本填的值
        float radius = tickOrbitRadius > 0.001f ? tickOrbitRadius : clock.dialRadius;

        // 第一次排布时记下原始大小
        if (segBaseScale == null || segBaseScale.Length < found)
            segBaseScale = new Vector3[clock.targetProgress];

        for (int i = 0; i < found; i++)
        {
            float ang = TickAngleAt(i) * Mathf.Deg2Rad;
            segs[i].transform.localPosition =
                new Vector3(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;

            if (segBaseScale[i] == Vector3.zero) segBaseScale[i] = segs[i].transform.localScale;
            segs[i].transform.localScale = segBaseScale[i] * Mathf.Max(0.05f, tickSizeScale);
        }

        // 记下这次的参数，供运行时自动重排
        lastRadius = tickOrbitRadius;
        lastSize = tickSizeScale;
        lastA0 = arcStartAngle;
        lastStep = TickStep();
        lastSpan = arcSpanAngle;

        if (debugLog)
            Debug.Log(string.Format("[指针] 已把 {0} 个刻度摆成半圆：{1}° → {2}°，跨度 {3}°，半径 {4}",
                found, arcStartAngle, TickAngleAt(found - 1), arcSpanAngle, radius));
    }

    // 整个半圆的跨度，均分给 N 个刻度；起点 arcStartAngle 顺时针铺开
    float TickStep()
    {
        int n = (clock != null) ? clock.targetProgress : 5;
        if (n <= 1) return 0f;
        return -Mathf.Abs(arcSpanAngle) / (n - 1);
    }

    // 第 i 个刻度的角度（度）
    float TickAngleAt(int i)
    {
        return arcStartAngle + TickStep() * i;
    }

    // 运行时改了半径/角度/大小 → 立刻重排，不用重启
    void CheckLayoutChanged()
    {
        if (!layoutAsSemiCircle || clock == null) return;
        if (Mathf.Approximately(lastRadius, tickOrbitRadius) &&
            Mathf.Approximately(lastSize, tickSizeScale) &&
            Mathf.Approximately(lastA0, arcStartAngle) &&
            Mathf.Approximately(lastStep, TickStep()) &&
            Mathf.Approximately(lastSpan, arcSpanAngle)) return;

        RelayoutSegments();
    }


    // ============ 空闲检测 ============

    void Update()
    {
        if (clock == null) return;

        // 运行时改了半径 / 角度 / 大小 → 立刻重排刻度
        CheckLayoutChanged();

        if (idleResetTime <= 0f) return;
        if (clock.Current <= baselineProgress) return;
        if (!resetAfterComplete && reachedEndFired) return;
        if (Time.time - lastInputTime >= idleResetTime)
        {
            if (debugLog)
                Debug.Log(string.Format("[指针] {0:F1} 秒没有有效弹奏，自动归零。", idleResetTime));
            ResetDial();
        }
    }

    // ============ 内部逻辑 ============

    void EnsureBaseline()
    {
        if (clock == null) return;
        int guard = 0;
        while (clock.Current < baselineProgress && clock.Current < clock.targetProgress && guard++ < 32)
            clock.AddProgress();
    }

    void OnProgressChanged()
    {
        if (clock == null) return;

        // 兼容：ClockProgress 的测试键 Y（或外部脚本）直接清零时，自动补回 baseline
        if (clock.Current < baselineProgress)
        {
            EnsureBaseline();
            return;
        }

        // 关键：只要表盘往上走了（乐器弹奏 / 测试键 T / 别的脚本调 AddProgress 都算），
        // 就把「5 秒空闲计时」重置到这一刻 —— 计时永远从上一次有效输入算起。
        lastInputTime = Time.time;

        AnimateToCurrent();

        if (clock.Current >= clock.targetProgress && !reachedEndFired)
        {
            reachedEndFired = true;
            if (debugLog) Debug.Log("[指针] 到达最后一格，触发 onReachedEnd。");
            onReachedEnd.Invoke();
        }
        if (clock.Current < clock.targetProgress) reachedEndFired = false;
    }

    float AngleForProgress(int progress)
    {
        int maxIdx = clock != null ? clock.targetProgress - 1 : 0;
        int idx = Mathf.Clamp(progress - baselineProgress, 0, maxIdx);
        float tickAngle = TickAngleAt(idx);
        return tickAngle - 90f;
    }

    void AnimateToCurrent()
    {
        float target = baseZ + AngleForProgress(clock.Current);
        if (turning != null) StopCoroutine(turning);
        turning = StartCoroutine(TurnRoutine(target, true));
    }

    void SyncImmediate()
    {
        currentZ = baseZ + AngleForProgress(clock.Current);
        ApplyZ(currentZ);
        reachedEndFired = clock.Current >= clock.targetProgress;
    }

    IEnumerator TurnRoutine(float targetZ, bool playTick)
    {
        float from = currentZ;
        float delta = Mathf.DeltaAngle(from, targetZ);
        float to = from + delta;

        float dur = Mathf.Max(0.02f, Mathf.Abs(delta) > 0.01f ? stepDuration : 0.02f);
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            float k = Mathf.Clamp01(t);
            if (ease) k = k * k * (3f - 2f * k);

            float over = 0f;
            if (overshoot > 0.01f && Mathf.Abs(delta) > 1f)
                over = Mathf.Sin(k * Mathf.PI) * overshoot * Mathf.Sign(delta);

            currentZ = from + delta * k + over;
            ApplyZ(currentZ);
            yield return null;
        }

        currentZ = to;
        ApplyZ(currentZ);
        turning = null;

        if (playTick)
        {
            if (audioSource != null && tickClip != null)
                audioSource.PlayOneShot(tickClip, tickVolume);
            onStepTurned.Invoke();
        }
    }

    void ApplyZ(float z)
    {
        if (needle == null) return;
        var e = needle.localEulerAngles;
        needle.localEulerAngles = new Vector3(e.x, e.y, z);
    }

    void OnDrawGizmosSelected()
    {
        if (clock == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(clock.transform.position, clock.dialRadius);

        if (rangeInPixels > 0f)
        {
            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.5f);
            float r = rangeInPixels / Mathf.Max(1f, pixelsPerUnit);
            Gizmos.DrawWireSphere(transform.position, r);
        }
    }
}
