using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 声波破墙 → 水柱喷出 → 水位上升（一次性演出）。
///
/// 用法：挂在一个空物体上（不要挂在墙本身上），把墙 / 水柱 / 水区拖进对应槽位。
/// 继承 WaveReceivable2D，所以声波自动就能扫到它，不需要手动注册。
///
/// 触发方式：
///   ① 回声（按 Q）扫到这里 —— 自动
///   ② 别的脚本调用 TriggerFlood()
///   ③ Inspector 右上角菜单「立刻触发（测试用）」
/// </summary>
[DisallowMultipleComponent]
public class WaveBreakFlood2D : WaveReceivable2D
{
    // ------------------------------------------------------------------ 墙

    [Header("墙（要被打碎的东西）")]
    [Tooltip("把墙的父物体拖进来。触发时会关掉它下面所有的 Collider2D 和 SpriteRenderer。\n" +
             "留空 = 不处理墙，只喷水（适合调试）。")]
    public GameObject wall;

    [Tooltip("碎块数量。0 = 不喷碎块。\n8 ~ 16 比较自然。")]
    [Range(0, 32)] public int debrisCount = 12;

    [Tooltip("碎块喷出力度（格/秒）。3 ~ 6 之间看着像石块崩落。")]
    public float debrisForce = 4.5f;

    [Tooltip("碎块存活时间（秒）。0.8 ~ 1.5 比较合适。")]
    public float debrisLife = 1.1f;

    [Tooltip("碎块用的贴图。留空 = 自动用墙身上第一个 SpriteRenderer 的图。")]
    public Sprite debrisSprite;

    [Header("破墙瞬间")]
    [Tooltip("破墙音效（可留空）。")]
    public AudioClip breakSound;

    [Tooltip("音效音量。0.8 左右比较合适，别盖过 BGM。")]
    [Range(0, 1)] public float breakVolume = 0.85f;

    [Tooltip("破墙时震一下屏。0.25 秒够用。")]
    public bool shakeOnBreak = true;

    [Tooltip("震屏时长（秒）。0.25 明显但不晕。")]
    public float shakeDuration = 0.25f;

    [Tooltip("震屏幅度（格）。0.18 已经有冲击力了。")]
    public float shakeStrength = 0.18f;

    // ------------------------------------------------------------------ 水柱

    [Header("水柱（从缺口喷出来）")]
    [Tooltip("水柱物体，可以拖多个。破墙后依次弹出（由小变大）。\n开局会自动隐藏它们。")]
    public GameObject[] jets;

    [Tooltip("破墙之后等多久开始喷水（秒）。0.25 让「石头先碎、水后到」更有层次。")]
    public float jetDelay = 0.25f;

    [Tooltip("每根水柱由小变大所用时间（秒）。0.3 ~ 0.5 比较自然。")]
    public float jetGrowTime = 0.35f;

    [Tooltip("多根水柱之间的间隔（秒）。0.08 左右像连续喷发。")]
    public float jetStagger = 0.08f;

    [Tooltip("开局就把水柱隐藏。建议勾上，否则玩家能提前看到水柱杵在那儿。")]
    public bool hideJetsOnStart = true;

    // ------------------------------------------------------------------ 水位

    [Header("水位（升到多高）")]
    [Tooltip("拖场景里的 WaterZone（上面必须挂了 Water Level 2D）。留空 = 不升水位。")]
    public WaterLevel2D water;

    [Tooltip("相对上升：勾上 = 从当前水面往上加 Rise Depth。\n不勾 = 直接升到下面的「绝对高度」。")]
    public bool relativeRise = true;

    [Tooltip("相对模式下，水面往上加多少格。6 格大约淹没一个房间高度的一半。")]
    public float riseDepth = 6f;

    [Tooltip("绝对模式下，水面最终停在世界 Y = 这个值。\n（数值 = 目标高度，不是「加多少」，1 格 = 1 单位）")]
    public float riseToY = 15f;

    [Tooltip("水位上升速度（格/秒）。2 ~ 3 有压迫感但不失控。")]
    public float riseSpeed = 2.5f;

    // ------------------------------------------------------------------ 事件

    [Header("事件（可拖音效 / 镜头 / UI）")]
    [Tooltip("墙碎的那一瞬间触发。")]
    public UnityEvent onBroken;

    [Tooltip("水柱开始喷、水开始涨的那一刻触发。")]
    public UnityEvent onFloodStarted;

    [Header("调试")]
    [Tooltip("勾上后在 Console 打印每一步的时间点，方便对齐节奏。")]
    public bool debugLog = false;

    // ------------------------------------------------------------------ 内部

    bool triggered;
    Vector3[] jetBaseScales;
    bool[] jetBaseCaptured;

    // v53: 记录初始状态，供「恢复（测试用）」还原
    Collider2D[] wallColliders;
    bool[] wallColliderState;
    Renderer[] wallRenderers;
    bool[] wallRendererState;
    float waterStartTopY;
    bool waterStartCaptured;

    void Awake()
    {
        CaptureJetScales();
        CaptureWallState();

        if (hideJetsOnStart && jets != null)
        {
            for (int i = 0; i < jets.Length; i++)
                if (jets[i] != null) jets[i].SetActive(false);
        }
    }

    void Start()
    {
        // 放在 Start 里取：确保 WaterLevel2D 的 Awake 已经算好初始水面
        if (water != null)
        {
            waterStartTopY = water.WaterTopY;
            waterStartCaptured = true;
        }
    }

    void CaptureJetScales()
    {
        if (jets == null) return;

        if (jetBaseScales == null || jetBaseScales.Length != jets.Length)
        {
            jetBaseScales = new Vector3[jets.Length];
            jetBaseCaptured = new bool[jets.Length];
        }

        for (int i = 0; i < jets.Length; i++)
        {
            if (jets[i] == null) continue;
            jetBaseScales[i] = jets[i].transform.localScale;   // 未激活物体也能读
            jetBaseCaptured[i] = true;
        }
    }

    // ------------------------------------------------------------------ 触发

    public override void OnWaveHit(SoundWave2D wave)
    {
        base.OnWaveHit(wave);            // 保留 Inspector 里的 On Wave Hit 事件
        TriggerFlood();
    }

    /// <summary>外部也能调用（比如踩到压力板）。重复调用无效。</summary>
    public void TriggerFlood()
    {
        if (triggered) return;
        triggered = true;
        StartCoroutine(FloodRoutine());
    }

    [ContextMenu("立刻触发（测试用）")]
    void TriggerFloodFromMenu()
    {
        triggered = false;
        TriggerFlood();
    }

    // ------------------------------------------------------------------ 恢复

    void CaptureWallState()
    {
        if (wall == null) return;

        wallColliders = wall.GetComponentsInChildren<Collider2D>(true);
        wallColliderState = new bool[wallColliders.Length];
        for (int i = 0; i < wallColliders.Length; i++)
            wallColliderState[i] = wallColliders[i].enabled;

        wallRenderers = wall.GetComponentsInChildren<Renderer>(true);
        wallRendererState = new bool[wallRenderers.Length];
        for (int i = 0; i < wallRenderers.Length; i++)
            wallRendererState[i] = wallRenderers[i].enabled;
    }

    /// <summary>
    /// 把墙 / 水柱 / 水位一次性还原到开局状态，方便在 Play 里反复测试。
    /// 用法：Inspector 右上角菜单「恢复（测试用）」。
    /// 注意：这只是给调试用的，游戏里破了就是破了。
    /// </summary>
    [ContextMenu("恢复（测试用）")]
    public void RestoreAll()
    {
        StopAllCoroutines();
        triggered = false;

        // 1) 墙：碰撞体和渲染器按开局记录还原
        if (wallColliders != null)
        {
            for (int i = 0; i < wallColliders.Length; i++)
                if (wallColliders[i] != null) wallColliders[i].enabled = wallColliderState[i];
        }
        if (wallRenderers != null)
        {
            for (int i = 0; i < wallRenderers.Length; i++)
                if (wallRenderers[i] != null) wallRenderers[i].enabled = wallRendererState[i];
        }

        // 2) 水柱：缩放回到原尺寸，并按开局的显隐规则复位
        if (jets != null)
        {
            for (int i = 0; i < jets.Length; i++)
            {
                if (jets[i] == null) continue;
                if (jetBaseCaptured != null && jetBaseCaptured[i])
                    jets[i].transform.localScale = jetBaseScales[i];
                jets[i].SetActive(!hideJetsOnStart);
            }
        }

        // 3) 水位：停掉正在进行的 RiseTo，直接设回开局高度
        if (water != null && waterStartCaptured)
        {
            water.StopAllCoroutines();
            water.SetLevel(waterStartTopY);
        }

        if (debugLog) Debug.Log("[破墙] 已恢复到初始状态", this);
    }

    IEnumerator FloodRoutine()
    {
        // ---------- ① 墙碎 ----------
        BreakWall();

        if (debugLog) Debug.Log("[破墙] 墙已碎，等待 " + jetDelay + " 秒喷水", this);

        // ---------- ② 等一拍 ----------
        if (jetDelay > 0f) yield return new WaitForSeconds(jetDelay);

        // ---------- ③ 水柱喷出 ----------
        StartJetSpray();
        onFloodStarted.Invoke();

        if (debugLog) Debug.Log("[破墙] 水柱已喷，开始升水位", this);

        // ---------- ④ 水位上升 ----------
        if (water != null)
        {
            float target = relativeRise ? water.WaterTopY + riseDepth : riseToY;
            float dist = Mathf.Abs(target - water.WaterTopY);
            float dur = riseSpeed > 0.01f ? dist / riseSpeed : 0.01f;

            if (debugLog)
                Debug.Log(string.Format("[破墙] 水位 {0:F2} → {1:F2}，共 {2:F2} 格，预计 {3:F2} 秒",
                          water.WaterTopY, target, dist, dur), this);

            yield return water.RiseTo(target, dur);
        }
        else if (debugLog)
        {
            Debug.Log("[破墙] 没拖 Water Level 2D，跳过升水位", this);
        }

        if (debugLog) Debug.Log("[破墙] 演出结束", this);
    }

    // ------------------------------------------------------------------ 墙

    void BreakWall()
    {
        if (wall != null)
        {
            // 先取图（关掉渲染器之前），碎块要用
            Sprite src = debrisSprite;
            Color col = Color.white;
            if (src == null)
            {
                var sr = wall.GetComponentInChildren<SpriteRenderer>(true);
                if (sr != null) { src = sr.sprite; col = sr.color; }
            }

            // 关碰撞体（玩家能穿过去了）
            foreach (var c in wall.GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;

            // 关渲染（墙消失）。用 Renderer 而不是 SpriteRenderer，
            // 这样墙是 Tilemap（TilemapRenderer）时也能一起隐藏。
            foreach (var r in wall.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;

            if (debrisCount > 0 && src != null)
                SpawnDebris(src, col, wall.transform.position);
        }

        if (breakSound != null)
        {
            var au = GetComponent<AudioSource>();
            if (au == null) au = gameObject.AddComponent<AudioSource>();
            au.playOnAwake = false;
            au.PlayOneShot(breakSound, breakVolume);
        }

        if (shakeOnBreak)
            CameraShake2D.Shake(shakeDuration, shakeStrength);

        onBroken.Invoke();
    }

    void SpawnDebris(Sprite sprite, Color color, Vector3 origin)
    {
        for (int i = 0; i < debrisCount; i++)
        {
            var go = new GameObject("Debris");
            go.transform.position = origin + new Vector3(
                Random.Range(-0.6f, 0.6f), Random.Range(-0.4f, 0.6f), 0f);
            go.transform.localScale = Vector3.one * Random.Range(0.18f, 0.38f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = 5;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.2f;
            rb.velocity = new Vector2(Random.Range(-debrisForce, debrisForce),
                                      Random.Range(debrisForce * 0.6f, debrisForce * 1.5f));

            Destroy(go, debrisLife);
        }
    }

    // ------------------------------------------------------------------ 水柱

    void StartJetSpray()
    {
        if (jets == null || jets.Length == 0) return;

        CaptureJetScales();

        for (int i = 0; i < jets.Length; i++)
        {
            if (jets[i] == null) continue;
            StartCoroutine(JetRoutine(jets[i], i));
        }
    }

    IEnumerator JetRoutine(GameObject jet, int index)
    {
        if (jetStagger > 0f && index > 0)
            yield return new WaitForSeconds(jetStagger * index);

        jet.SetActive(true);

        Vector3 full = (jetBaseCaptured != null && jetBaseCaptured[index])
                     ? jetBaseScales[index] : jet.transform.localScale;

        if (jetGrowTime <= 0.01f)
        {
            jet.transform.localScale = full;
            yield break;
        }

        float t = 0f;
        while (t < jetGrowTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / jetGrowTime);
            k = k * k * (3f - 2f * k);                        // SmoothStep
            jet.transform.localScale = full * Mathf.Lerp(0.12f, 1f, k);
            yield return null;
        }
        jet.transform.localScale = full;
    }

    // ------------------------------------------------------------------ Gizmos

    void OnDrawGizmosSelected()
    {
        if (water == null) return;

        float y = relativeRise ? water.WaterTopY + riseDepth : riseToY;
        Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.8f);
        Vector3 a = new Vector3(transform.position.x - 8f, y, 0f);
        Vector3 b = new Vector3(transform.position.x + 8f, y, 0f);
        Gizmos.DrawLine(a, b);
    }
}
