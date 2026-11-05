using UnityEngine;

/// <summary>
/// BGM「前奏 + 中段循环」播放器。
///
/// 【为什么需要脚本】
/// Unity 的 AudioSource 只能整首循环（Loop 勾上就是从头再来），
/// 没法设「从第 56 秒循环到第 2 分 39 秒」。
/// 所以这里用两段音频：
///   introClip —— 前奏，播一次
///   loopClip  —— 循环段，一直循环
/// 再用 PlayScheduled 把循环段的起点，精确排在「前奏结束的那个采样」上，
/// 这样接缝是采样级对齐的，听不出来。
///
/// 【怎么用】
///   1. 物体上要有 AudioSource（Awake 会自动配好，不用手调）
///   2. Intro Clip / Loop Clip 各拖一段
///   3. 任何 UnityEvent 里调 Play()
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BgmLoopSection : MonoBehaviour
{
    [Header("音频")]
    [Tooltip("前奏。只在开头播一次。")]
    public AudioClip introClip;

    [Tooltip("循环段。前奏播完后无限循环。")]
    public AudioClip loopClip;

    [Header("音量")]
    [Tooltip("BGM 总音量。要比音效小 8~12dB，否则音效会被埋掉。推荐 0.45~0.55。")]
    [Range(0f, 1f)] public float volume = 0.55f;

    [Header("行为")]
    [Tooltip("勾上 = 游戏一开始就自动播；不勾 = 等外部调用 Play()（比如点「新的旅程」按钮）。")]
    public bool playOnStart = false;

    [Tooltip("勾上 = 切场景时这个物体不被销毁，音乐不断。")]
    public bool keepAcrossScenes = true;

    [Tooltip("勾上 = 已经在播时，再调用 Play() 不会从头重来。\n防止反复进出水域把音乐重置到前奏。")]
    public bool ignoreIfPlaying = true;

    [Header("调试")]
    [Tooltip("勾上 = 播放时在 Console 打印日志，方便排查为什么没响。")]
    public bool logPlayback = true;

    AudioSource introSource;    // 播前奏
    AudioSource loopSource;     // 播循环段

    void Awake()
    {
        introSource = GetComponent<AudioSource>();
        introSource.playOnAwake = false;
        introSource.loop = false;
        introSource.spatialBlend = 0f;      // 0 = 纯 2D
        introSource.volume = volume;

        // 循环段单独一个 AudioSource，才能真正做到「前奏一结束就接上」
        var go = new GameObject("Voice_Loop");
        go.transform.SetParent(transform, false);
        loopSource = go.AddComponent<AudioSource>();
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.spatialBlend = 0f;
        loopSource.volume = volume;

        // DontDestroyOnLoad 只对「根物体」有效。
        // 所以先把自己从父物体上摘下来（保留世界位置），否则会报警告、切场景还会被销毁。
        if (keepAcrossScenes)
        {
            if (transform.parent != null) transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
        }
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>
    /// 开始播：先放前奏，再把循环段排在前奏结束的那一刻。
    /// 重复调用不会叠加（会先停掉再重来）。
    /// </summary>
    public void Play()
    {
        if (introClip == null || loopClip == null)
        {
            if (logPlayback) Debug.LogWarning("[BGM] 缺少 Intro Clip 或 Loop Clip，没播。");
            return;
        }

        // 已经在播就不从头重来（防止反复进出水域把音乐重置）
        if (ignoreIfPlaying && (introSource.isPlaying || loopSource.isPlaying)) return;

        introSource.Stop();
        loopSource.Stop();

        double start = AudioSettings.dspTime + 0.05;   // 留 50ms 缓冲

        introSource.clip = introClip;
        introSource.volume = volume;
        introSource.PlayScheduled(start);

        loopSource.clip = loopClip;
        loopSource.volume = volume;
        loopSource.PlayScheduled(start + introClip.length);   // 采样级接上

        if (logPlayback)
            Debug.Log("[BGM] 前奏 " + introClip.length.ToString("0.00") + " 秒 → 进入循环 " +
                      loopClip.length.ToString("0.00") + " 秒");
    }

    /// <summary>立刻停（没有淡出）。</summary>
    public void Stop()
    {
        introSource.Stop();
        loopSource.Stop();
    }

    /// <summary>运行时可调音量（0~1）。</summary>
    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        introSource.volume = volume;
        loopSource.volume = volume;
    }
}
