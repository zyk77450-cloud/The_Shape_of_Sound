using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 走位→链条声→过场 的顺序演出。
/// 由 ClockDial 的 On Completed 触发 Play()：
///   事件1  延迟 delayBeforeMove 秒后，喂方向信号让角色自己走过去（播移动动画）
///   事件2  到位后播链条声（连响 chainBurstCount 下，播完才继续）
///   事件3  到位后触发 onArrived（接 CutsceneFlood.Play 开过场）
/// 角色用原本的移动速度/动画/碰撞，不做平移。
/// </summary>
public class ApproachSequence2D : MonoBehaviour
{
    [Header("① 走位目标")]
    [Tooltip("要走到哪（世界坐标）。只用 X，Y 由地形决定。")]
    public Vector2 targetPosition = new Vector2(-7f, -3.503931f);

    [Tooltip("触发后先等多久再开始走（秒）。")]
    public float delayBeforeMove = 1f;

    [Tooltip("到多近算「到位」（格）。0.15 差不多是脚尖贴线。")]
    public float arriveTolerance = 0.15f;

    [Tooltip("最长走多久（秒）。超时没到就强制结束，防止被地形卡死。")]
    public float maxWalkTime = 8f;

    [Tooltip("勾上 = 超时没走到就把角色瞬移补齐到目标 X。不勾 = 停在原地继续。")]
    public bool snapIfTimeout = true;

    [Tooltip("勾上 = 到位后朝左。")]
    public bool faceLeft = true;

    [Tooltip("到位后停多久再开过场（秒）。留 0.3 让玩家看清他站定了。")]
    public float holdAfterArrive = 0.3f;

    [Header("② 链条声")]
    [Tooltip("播放链条声的 AudioSource。留空 = 自动在本物体上新建。")]
    public AudioSource chainAudio;

    [Tooltip("链条咔哒声（0.1 秒左右的短音效）。留空 = 不播声音。")]
    public AudioClip chainClip;

    [Tooltip("链条声连响几下（独立事件，播完才开过场）。")]
    public int chainBurstCount = 3;

    [Tooltip("每一声之间隔多久（秒）。0.12 就是短促的「咔咔咔」。")]
    public float chainBurstInterval = 0.12f;

    [Tooltip("链条声音量（0~1）。")]
    [Range(0f, 1f)]
    public float tickVolume = 0.55f;

    [Header("③ 触发")]
    [Tooltip("勾上 = 组件一启用就自动开始（调试用）。正式流程关掉，由 ClockDial.On Completed 调 Play()。")]
    public bool autoStart = false;

    [Tooltip("勾上 = 只播一次，之后调 Play() 无效。")]
    public bool playOnce = true;

    [Tooltip("到位后触发。把 CutsceneFlood.Play 拖进来。")]
    public UnityEvent onArrived;

    [Header("④ 调试")]
    [Tooltip("勾上 = Console 打印走位进度。")]
    public bool debugLog = true;

    bool playing;
    bool played;

    PlayerController2D player;

    void Awake()
    {
        if (chainAudio == null)
        {
            chainAudio = GetComponent<AudioSource>();
            if (chainAudio == null) chainAudio = gameObject.AddComponent<AudioSource>();
        }
        chainAudio.playOnAwake = false;
        chainAudio.loop = false;
    }

    void Start()
    {
        if (autoStart) Play();
    }

    /// <summary>开始走位演出。由 ClockDial.On Completed 调用。</summary>
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

        player = FindObjectOfType<PlayerController2D>();
        if (player == null)
        {
            if (debugLog) Debug.LogWarning("[走位] 找不到 PlayerController2D，跳过走位直接开过场。");
            playing = false;
            if (onArrived != null) onArrived.Invoke();
            yield break;
        }

        // 接管输入：角色照常运行（重力/碰撞/动画都在），只是按键失效
        player.externalInput = true;
        player.externalMove = 0f;

        // 事件1（前段）：延迟
        if (delayBeforeMove > 0f) yield return new WaitForSeconds(delayBeforeMove);

        // 事件1（后段）：朝目标走
        float t0 = Time.time;

        if (debugLog)
        {
            Debug.Log("[走位] 起点 " + player.transform.position.ToString("0.00")
                      + " → 目标 X " + targetPosition.x.ToString("0.00")
                      + "，距离 " + Mathf.Abs(targetPosition.x - player.transform.position.x).ToString("0.00") + " 格");
        }

        while (Mathf.Abs(targetPosition.x - player.transform.position.x) > arriveTolerance)
        {
            float dx = targetPosition.x - player.transform.position.x;
            player.externalMove = Mathf.Sign(dx);   // -1 左 / 1 右

            if (Time.time - t0 > maxWalkTime)
            {
                if (debugLog) Debug.LogWarning("[走位] 超过 " + maxWalkTime.ToString("0.0") + " 秒没走到，强制结束。");
                if (snapIfTimeout)
                {
                    Vector3 p = player.transform.position;
                    p.x = targetPosition.x;
                    player.transform.position = p;
                }
                break;
            }

            yield return null;
        }

        // 停 + 转身
        player.externalMove = 0f;
        if (faceLeft) player.SetFacing(true);

        if (debugLog)
        {
            Debug.Log("[走位] 到位，用时 " + (Time.time - t0).ToString("0.00") + " 秒，位置 "
                      + player.transform.position.ToString("0.00"));
        }

        // 事件2：链条声（独立事件，播完才开过场）
        if (chainClip != null)
        {
            for (int i = 0; i < chainBurstCount; i++)
            {
                chainAudio.PlayOneShot(chainClip, tickVolume);
                yield return new WaitForSeconds(chainBurstInterval);
            }
        }

        if (holdAfterArrive > 0f) yield return new WaitForSeconds(holdAfterArrive);

        // 事件3：开过场（过场物体常显，Auto Start 不勾，这里直接触发）
        if (onArrived != null) onArrived.Invoke();

        // 交还（若 onArrived 已把玩家交给 CutsceneFlood 锁住，这里无害）
        player.externalInput = false;
        player.externalMove = 0f;
        playing = false;
    }
}
