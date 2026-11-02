using UnityEngine;

/// <summary>
/// 循环播放一组 Sprite（逐帧动画）。
/// 不需要 Animator、不需要 Controller，把帧序列拖进来就能跑。
/// 挂在带 SpriteRenderer 的物体上。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LoopSpriteAnimator2D : MonoBehaviour
{
    [Header("帧序列")]
    [Tooltip("按顺序拖入所有帧。第 1 张是起始帧。")]
    public Sprite[] frames;

    [Tooltip("每秒播放几帧。12 = 每帧 0.083 秒；数字越大播得越快。")]
    public float framesPerSecond = 12f;

    [Tooltip("勾上 = 循环播放；不勾 = 播一遍就停在最后一帧。")]
    public bool loop = true;

    [Tooltip("随机起始帧。多个同款物体一起放时错开相位，看起来不会整齐划一。")]
    public bool randomStart = false;

    [Header("调试")]
    [Tooltip("勾上后播放时会在 Console 打印当前帧号（很吵，调试完记得关掉）。")]
    public bool debugLog = false;

    SpriteRenderer sr;
    float timer;
    float indexF;
    bool finished;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (frames != null && frames.Length > 0 && randomStart)
            indexF = Random.Range(0, frames.Length);
        Apply();
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;
        if (finished) return;

        float step = 1f / Mathf.Max(1f, framesPerSecond);
        timer += Time.deltaTime;

        while (timer >= step)
        {
            timer -= step;
            indexF += 1f;

            if (indexF >= frames.Length)
            {
                if (loop)
                {
                    indexF -= frames.Length;
                }
                else
                {
                    indexF = frames.Length - 1;
                    finished = true;
                    break;
                }
            }
            Apply();
        }
    }

    void Apply()
    {
        if (sr == null || frames == null || frames.Length == 0) return;
        int i = Mathf.Clamp(Mathf.RoundToInt(indexF), 0, frames.Length - 1);
        sr.sprite = frames[i];
        if (debugLog) Debug.Log("[动画] 第 " + (i + 1) + " 帧", this);
    }

    /// <summary>从头重新播放（想手动触发时调用）。</summary>
    public void Restart()
    {
        finished = false;
        indexF = 0f;
        timer = 0f;
        Apply();
    }
}
