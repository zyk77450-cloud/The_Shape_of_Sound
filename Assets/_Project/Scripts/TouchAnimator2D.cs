using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 碰触才播放的逐帧动画（景物 / 机关 / 装饰用）。
///
/// 和 PlayerSpriteAnimator2D 的区别：
///   PlayerSpriteAnimator2D —— 一直播 / 按"物体在不在动"播（角色用）
///   TouchAnimator2D        —— 玩家碰到才开始，播满指定轮数后自己停（景物用）
///
/// 使用前提：
///   1. 物体上要有 SpriteRenderer
///   2. 物体上要有 Collider2D，并且勾了 Is Trigger（脚本会自动勾）
///   3. 玩家身上有 Tag = Player（你工程里已经有了）
///
/// 放在哪：Assets/_Project/Scripts/TouchAnimator2D.cs
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class TouchAnimator2D : MonoBehaviour
{
    [Header("① 帧序列：按播放顺序把图拖进来")]
    [Tooltip("按播放顺序拖入每一帧的图片。\n例如 6 帧就 Size 填 6，然后依次拖 Element 0~5。\n顺序 = 播放顺序，想倒着播就倒着拖。")]
    public Sprite[] frames;

    [Header("② 播放速度")]
    [Tooltip("每秒播放几帧。数字越大动得越快。\n建议：轻微呼吸 4~6；快速动作 10~14。")]
    [Min(0.1f)] public float framesPerSecond = 8f;

    [Header("③ 播放几轮后停下")]
    [Tooltip("播放多少「轮」后自动停止（1 轮 = 把 Frames 从头到尾播一遍）。\n1 = 只播一次就停；2 = 播两遍；0 = 一直循环，永不停止。\n一轮时长 = 帧数 ÷ Frames Per Second。")]
    [Min(0)] public int loopCount = 1;

    [Header("④ 停下之后")]
    [Tooltip("勾上 = 停下时回到第 1 帧（推荐，看起来像没被动过）；\n不勾 = 停在最后一帧（适合「被打开后就一直开着」的机关）。")]
    public bool resetAfterStop = true;

    [Tooltip("勾上 = 整局游戏只触发一次，之后玩家再碰也不会动；\n不勾 = 每碰一次都能重新播。")]
    public bool playOnlyOnce = false;

    [Header("⑤ 事件（可选）")]
    [Tooltip("玩家刚碰到的那一瞬间触发，可以拖音效、飘字、开机关的 UnityEvent。")]
    public UnityEvent onTriggered;

    [Tooltip("播放结束的那一瞬间触发（比如机关动完就开门）。")]
    public UnityEvent onFinished;

    SpriteRenderer sr;
    Collider2D myCol;

    float frameTimer;   // 距离下一帧还差多少秒
    float playTimer;    // 已经播了多少秒
    int index;          // 当前第几帧
    bool playing;
    bool used;          // playOnlyOnce 用

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        myCol = GetComponent<Collider2D>();

        // 自动勾上 Is Trigger：只做"碰触检测"，不挡住玩家
        if (myCol != null) myCol.isTrigger = true;

        // 一开始先显示第 1 帧
        if (sr != null && frames != null && frames.Length > 0)
            sr.sprite = frames[0];
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (playOnlyOnce && used) return;
        StartPlay();
    }

    /// <summary>
    /// 开始播放。播放中再碰一次 = 从头重新开始（不会叠加加速）。
    /// 也可以从别的脚本 / UnityEvent 里调用它。
    /// </summary>
    public void StartPlay()
    {
        if (sr == null || frames == null || frames.Length == 0) return;

        playing = true;
        used = true;
        index = 0;
        frameTimer = 0f;
        playTimer = 0f;
        sr.sprite = frames[0];

        onTriggered.Invoke();
    }

    /// <summary>
    /// 立刻停止（外部脚本也可以调）。
    /// </summary>
    public void StopPlay()
    {
        if (!playing) return;
        playing = false;

        if (resetAfterStop && frames != null && frames.Length > 0)
            sr.sprite = frames[0];

        onFinished.Invoke();
    }

    void Update()
    {
        if (!playing) return;

        playTimer += Time.deltaTime;
        frameTimer += Time.deltaTime;

        float frameTime = 1f / Mathf.Max(0.1f, framesPerSecond);

        // 用 while：万一某帧卡顿，能一次补上多帧，不会越播越慢
        while (frameTimer >= frameTime)
        {
            frameTimer -= frameTime;
            index = (index + 1) % frames.Length;   // 到末尾自动回到开头 = 循环
            sr.sprite = frames[index];
        }

        // loopCount = 0 表示无限循环，不检查停止
        if (loopCount > 0)
        {
            float totalTime = loopCount * frames.Length / Mathf.Max(0.1f, framesPerSecond);
            if (playTimer >= totalTime) StopPlay();
        }
    }

    // 在 Scene 视图里选中时，画出触发范围（绿色方框），方便确认碰撞体够大
    void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider2D>();
        if (c == null) return;
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.7f);
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
