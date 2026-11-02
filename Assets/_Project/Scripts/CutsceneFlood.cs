using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 洪水过场（精简版）。
/// 只有 4 个节拍：左水柱开 → 右水柱开 → 左齿轮落 → 右齿轮落。
/// 水柱：激活后播完一整轮动画就自己关掉。
/// 齿轮：开局隐藏，轮到它时才激活并从上往下掉，落地那一刻玩家转身 + 震屏。
/// 挂在场景里一个空物体上即可，不需要 Timeline 包。
/// </summary>
public class CutsceneFlood : MonoBehaviour
{
    [Header("① 水位（可选，留空则不涨水）")]
    [Tooltip("场景里 WaterZone 上的 WaterLevel2D 组件。留空 = 不涨水。")]
    public WaterLevel2D water;

    [Tooltip("水位最终升到多高（世界 Y）。填你想淹到的那一行。")]
    public float waterRiseToY = 8f;

    [Tooltip("水位上升速度（格/秒）。1.2 = 每秒涨 1.2 格；想慢就 0.5，想急就 3。")]
    public float waterRiseSpeed = 1.2f;

    [Tooltip("勾上 = 右水柱喷出的同时开始涨水。不勾 = 不涨水。")]
    public bool riseWaterWithJets = true;

    [Header("② 水柱")]
    [Tooltip("左边水柱（拖 WaterJet 的实例）。")]
    public GameObject jetLeft;

    [Tooltip("右边水柱（拖 WaterJet 的实例）。")]
    public GameObject jetRight;

    [Tooltip("水柱上既没有 Animator 也没有逐帧动画时，用它决定水柱停留多久（秒）。")]
    public float jetFallbackDuration = 0.6f;

    [Tooltip("勾上 = 关键引用没拖时在 Console 打印警告，方便排查。")]
    public bool debugLog = true;

    [Tooltip("水柱从多小长到正常大小用的时间（秒）。0 = 瞬间出现；0.3 左右像「涌出来」。")]
    public float jetGrowDuration = 0.3f;

    [Tooltip("勾上 = 过场一开始先把两根水柱藏起来（推荐）。")]
    public bool hideJetsOnStart = true;

    [Tooltip("转身延迟（秒）。动作触发后等这么久玩家才转身，0 = 同一瞬间。")]
    public float turnDelay = 0.15f;

    [Header("③ 齿轮")]
    [Tooltip("左边那个齿轮（场景里已经摆好位置的）。开局会被隐藏。")]
    public GameObject gearLeft;

    [Tooltip("右边那个齿轮（场景里已经摆好位置的）。开局会被隐藏。")]
    public GameObject gearRight;

    [Tooltip("勾上 = 过场一开始就把两个齿轮藏起来（推荐）。")]
    public bool hideGearsOnStart = true;

    [Tooltip("齿轮落地震屏时长（秒）。")]
    public float gearShakeDuration = 0.22f;

    [Tooltip("齿轮落地震屏幅度（格）。")]
    public float gearShakeStrength = 0.18f;

    [Header("④ 时间轴（秒，必须从小到大填 4 个）")]
    [Tooltip("依次是：左水柱开 / 右水柱开 / 左齿轮落 / 右齿轮落")]
    public float[] times = new float[] { 0.3f, 1.1f, 3.0f, 3.5f };

    [Header("⑤ 选项")]
    [Tooltip("勾上 = 过场期间锁死玩家。")]
    public bool lockPlayer = true;

    [Tooltip("勾上 = 整段过场只播一次。")]
    public bool playOnce = true;

    [Tooltip("勾上 = 玩家跟着动作转身（水柱朝左朝右，齿轮落地时朝左朝右）。")]
    public bool turnPlayerWithEvents = true;

    [Tooltip("勾上 = 物体一启用就自动开始播。")]
    public bool autoStart = true;

    [Header("⑥ 事件（可选，接音效/字幕）")]
    [Tooltip("左水柱喷出时触发。")]
    public UnityEvent onJetLeft;

    [Tooltip("右水柱喷出时触发。")]
    public UnityEvent onJetRight;

    [Tooltip("左齿轮落地时触发。")]
    public UnityEvent onGearLeft;

    [Tooltip("右齿轮落地时触发。")]
    public UnityEvent onGearRight;

    [Tooltip("整段过场播完时触发。")]
    public UnityEvent onFinished;

    // ---------- 运行时状态 ----------

    bool playing;
    bool played;

    PlayerController2D player;
    Rigidbody2D playerRb;

    int gearsExpected;
    int gearsLanded;

    // 水柱的原始尺寸/位置（生长动画要还原）
    Vector3 jetLScale, jetLPos; float jetLBelow;
    Vector3 jetRScale, jetRPos; float jetRBelow;

    void Start()
    {
        CacheJet(jetLeft, out jetLScale, out jetLPos, out jetLBelow);
        CacheJet(jetRight, out jetRScale, out jetRPos, out jetRBelow);

        if (hideJetsOnStart)
        {
            if (jetLeft != null) jetLeft.SetActive(false);
            if (jetRight != null) jetRight.SetActive(false);
        }

        if (hideGearsOnStart)
        {
            if (gearLeft != null) gearLeft.SetActive(false);
            if (gearRight != null) gearRight.SetActive(false);
        }

        if (autoStart) Play();
    }

    // 记下水柱的原始缩放、位置、以及「轴心到底边」的距离（用来让它从底部往上长）
    void CacheJet(GameObject jet, out Vector3 baseScale, out Vector3 basePos, out float below)
    {
        baseScale = Vector3.one;
        basePos = Vector3.zero;
        below = 0f;
        if (jet == null) return;

        baseScale = jet.transform.localScale;
        basePos = jet.transform.localPosition;

        SpriteRenderer sr = jet.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            // 轴心到底边有多少个世界单位：pivot 像素 / PPU，再乘上缩放
            below = (sr.sprite.pivot.y / sr.sprite.pixelsPerUnit) * baseScale.y;
        }
    }

    /// <summary>开始播过场。</summary>
    public void Play()
    {
        if (playing) return;
        if (playOnce && played) return;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        playing = true;
        played = true;

        FindPlayer();
        if (lockPlayer) FreezePlayer(true);

        float t0 = Time.time;

        // ① 左水柱开
        yield return WaitT(0, t0);
        Face(true);
        PlayJet(jetLeft, jetLScale, jetLPos, jetLBelow);
        if (onJetLeft != null) onJetLeft.Invoke();

        // ② 右水柱开
        yield return WaitT(1, t0);
        Face(false);
        PlayJet(jetRight, jetRScale, jetRPos, jetRBelow);
        if (onJetRight != null) onJetRight.Invoke();

        if (riseWaterWithJets && water != null)
        {
            float dist = Mathf.Abs(waterRiseToY - water.WaterTopY);
            float dur = dist / Mathf.Max(0.05f, waterRiseSpeed);
            if (debugLog) Debug.Log("[过场] 涨水：上升 " + dist.ToString("0.0") + " 格，预计 " + dur.ToString("0.0") + " 秒");
            StartCoroutine(water.RiseTo(waterRiseToY, dur));
        }

        // ③ 左齿轮落
        yield return WaitT(2, t0);
        DropGear(gearLeft, true);

        // ④ 右齿轮落
        yield return WaitT(3, t0);
        DropGear(gearRight, false);

        // 等齿轮都落地，再放开玩家
        while (gearsExpected > 0 && gearsLanded < gearsExpected) yield return null;

        if (lockPlayer) FreezePlayer(false);
        if (onFinished != null) onFinished.Invoke();

        playing = false;
    }

    IEnumerator WaitT(int index, float t0)
    {
        if (times == null || index >= times.Length) yield break;
        float target = t0 + times[index];
        while (Time.time < target) yield return null;
    }

    // ---------- 水柱 ----------

    void PlayJet(GameObject jet, Vector3 baseScale, Vector3 basePos, float below)
    {
        if (jet == null)
        {
            if (debugLog) Debug.LogWarning("[过场] 水柱引用没拖，已跳过。");
            return;
        }

        // 先算长度（只用于日志，方便你对时间轴）
        float dur = JetLength(jet);

        LoopSpriteAnimator2D anim = jet.GetComponent<LoopSpriteAnimator2D>();
        if (anim != null) anim.Restart();

        Animator an = jet.GetComponent<Animator>();
        if (an != null && an.runtimeAnimatorController != null) an.Play(0, -1, 0f);

        jet.SetActive(true);
        if (debugLog) Debug.Log("[过场] 水柱开：" + jet.name + "（不自动关，动画约 " + dur.ToString("0.00") + " 秒一轮）");

        StartCoroutine(GrowJet(jet, baseScale, basePos, below));
    }

    // 从很小长到正常大小（底边不动，从下往上涌）
    IEnumerator GrowJet(GameObject jet, Vector3 baseScale, Vector3 basePos, float below)
    {
        if (jet == null) yield break;

        if (jetGrowDuration <= 0.01f)
        {
            jet.transform.localScale = baseScale;
            jet.transform.localPosition = basePos;
            yield break;
        }

        float t = 0f;
        while (t < jetGrowDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0.08f, 1f, Mathf.Clamp01(t / jetGrowDuration));

            jet.transform.localScale = baseScale * k;

            // 缩放以轴心为原点：变小后底边会往上跑，
            // 所以往下补 (1-k) × (轴心到底边的距离)，底边才钉在原地
            jet.transform.localPosition = basePos - Vector3.up * ((1f - k) * below);
            yield return null;
        }

        jet.transform.localScale = baseScale;
        jet.transform.localPosition = basePos;
    }

    // 水柱这一整段动画有多长（秒）
    float JetLength(GameObject jet)
    {
        if (jet == null) return jetFallbackDuration;

        // ① 用 Animator + Animation Clip 做的
        Animator an = jet.GetComponent<Animator>();
        if (an != null && an.runtimeAnimatorController != null)
        {
            AnimationClip[] clips = an.runtimeAnimatorController.animationClips;
            if (clips != null && clips.Length > 0)
            {
                float max = 0f;
                for (int i = 0; i < clips.Length; i++)
                    if (clips[i] != null && clips[i].length > max) max = clips[i].length;
                if (max > 0f) return max;
            }
        }

        // ② 用 LoopSpriteAnimator2D 逐帧播的
        LoopSpriteAnimator2D a = jet.GetComponent<LoopSpriteAnimator2D>();
        if (a != null && a.frames != null && a.frames.Length > 0 && a.framesPerSecond > 0f)
            return a.frames.Length / a.framesPerSecond;

        // ③ 都没有，用兜底值
        return jetFallbackDuration;
    }

    // ---------- 齿轮 ----------

    void DropGear(GameObject gear, bool faceLeft)
    {
        if (gear == null)
        {
            if (debugLog) Debug.LogWarning("[过场] 齿轮引用没拖（" + (faceLeft ? "左" : "右") + "），已跳过。");
            return;
        }

        GearDrop2D drop = gear.GetComponent<GearDrop2D>();
        if (drop == null) drop = gear.AddComponent<GearDrop2D>();

        drop.startOnPlay = false;          // 别在激活的瞬间自己掉
        drop.shakeOnLand = false;          // 震屏统一在这里做
        if (drop.onLanded == null) drop.onLanded = new UnityEvent();
        drop.onLanded.RemoveAllListeners();
        drop.onLanded.AddListener(() => OnGearLanded(faceLeft));

        gearsExpected++;
        gear.SetActive(true);              // 这一刻才出现
        if (debugLog) Debug.Log("[过场] 齿轮激活：" + gear.name);
        StartCoroutine(DropNextFrame(drop));
    }

    IEnumerator DropNextFrame(GearDrop2D drop)
    {
        yield return null;                 // 等 Start() 记录好落点
        if (drop != null) drop.Drop(0f);
    }

    void OnGearLanded(bool faceLeft)
    {
        gearsLanded++;
        Face(faceLeft);
        CameraShake2D.Shake(gearShakeDuration, gearShakeStrength);

        if (faceLeft && onGearLeft != null) onGearLeft.Invoke();
        if (!faceLeft && onGearRight != null) onGearRight.Invoke();
    }

    // ---------- 玩家 ----------

    void Face(bool left)
    {
        if (!turnPlayerWithEvents) return;
        if (player == null) return;

        if (turnDelay <= 0.01f) player.SetFacing(left);
        else StartCoroutine(FaceDelayed(left, turnDelay));
    }

    IEnumerator FaceDelayed(bool left, float d)
    {
        yield return new WaitForSeconds(d);
        if (player != null) player.SetFacing(left);
    }

    void FindPlayer()
    {
        if (player != null) return;
        player = FindObjectOfType<PlayerController2D>();
        if (player != null) playerRb = player.GetComponent<Rigidbody2D>();
    }

    void FreezePlayer(bool freeze)
    {
        if (player == null) return;
        player.enabled = !freeze;
        if (playerRb != null)
        {
            if (freeze) playerRb.velocity = Vector2.zero;
            playerRb.simulated = !freeze;
        }
    }
}
