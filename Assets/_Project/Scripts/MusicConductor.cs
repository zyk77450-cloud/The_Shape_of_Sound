using UnityEngine;

/// <summary>
/// 五种乐器（对应五种能力）
/// 顺序不能改：数组下标就是这里的顺序。
/// </summary>
public enum Instrument
{
    Triangle = 0,   // 三角铁 —— 跳跃
    BassDrum = 1,   // 大鼓   —— 超级跳
    Trumpet  = 2,   // 小号   —— 冲刺
    Violin   = 3,   // 小提琴 —— 蹬墙跳 / 贴墙滑
    Harp     = 4,   // 里拉琴 —— 水上行走（能力还没做，先留接口）
}

/// <summary>
/// 音乐指挥：所有"用能力就发声"的逻辑都走这里。
///
/// 【为什么要有它】
/// 如果每个能力各自写一份播放代码，会出现三个问题：
///   1. 连击计数器要放在哪儿？各写一份会互相对不上。
///   2. 同一个音效连着响三次，会互相打断（AudioSource 只有一个）。
///   3. 音量、变调参数散落在各处，调起来要翻五个文件。
/// 所以统一收口成一个"指挥"：别人只管喊一声 PlayNote(乐器)，剩下的它负责。
///
/// 【怎么用】
///    MusicConductor.Play(Instrument.Triangle);   // 一行，随便哪个脚本都能调
/// 没挂这个物体也不会报错（会自动静音跳过），方便你先删了它做测试。
/// </summary>
[DefaultExecutionOrder(-50)]   // 比 PlayerController2D 早一点初始化，保证 Awake 先跑
public class MusicConductor : MonoBehaviour
{
    // 单例：全局唯一，方便任何地方直接 MusicConductor.Play(...)
    public static MusicConductor Instance { get; private set; }

    [Header("音效文件（把 Audio/Instruments 里的 wav 拖进来）")]
    [Tooltip("三角铁：水上行走时响（v2 对调后归它）。文件：sfx_triangle_water.wav")]
    public AudioClip triangle;
    [Tooltip("大鼓：超级跳时响。文件：sfx_drum_jump.wav")]
    public AudioClip bassDrum;
    [Tooltip("小号：冲刺时响。文件：sfx_trumpet_dash.wav")]
    public AudioClip trumpet;
    [Tooltip("小提琴：蹬墙跳时响。文件：sfx_violin_wall.wav")]
    public AudioClip violin;
    [Tooltip("里拉琴：跳跃 / 二段跳时响（最频繁，所以用最柔的音）。文件：sfx_harp_jump.wav")]
    public AudioClip harp;

    [Header("音量（0 = 静音，1 = 原始音量）")]
    [Tooltip("三角铁音量（水上行走）。调小 → 更轻；调大 → 更清脆扎耳。推荐 0.35~0.45。")]
    [Range(0f, 1f)] public float volumeTriangle = 0.40f;
    [Tooltip("大鼓音量。它是低频，调太大容易糊住别的音。推荐 0.8~0.95。")]
    [Range(0f, 1f)] public float volumeBassDrum = 0.90f;
    [Tooltip("小号音量（冲刺）。推荐 0.4~0.5。")]
    [Range(0f, 1f)] public float volumeTrumpet = 0.45f;
    [Tooltip("小提琴音量。推荐 0.5~0.6。")]
    [Range(0f, 1f)] public float volumeViolin = 0.55f;
    [Tooltip("里拉琴音量（跳跃，出现最频繁，别太响）。推荐 0.30~0.35。")]
    [Range(0f, 1f)] public float volumeHarp = 0.32f;

    [Header("连击变调（同一个音连续触发会越来越高）")]
    [Tooltip("勾上 = 开启连击变调；不勾 = 每个乐器永远同一个音高。")]
    public bool enableCombo = true;
    [Tooltip("连击窗口（秒）。两次触发间隔小于这个值才算连击。\n调大 → 更容易连上、曲子更活跃；调小 → 只有快速操作才升调。推荐 1.0。")]
    public float comboWindow = 1.0f;
    [Tooltip("连击音高倍数，三级循环。\n1.0000 = 原始（F）\n1.1892 = 高 3 个半音（Ab）\n1.4983 = 高 7 个半音（C）\n这三个值让五件乐器永远落在同一个和弦里，随便按都好听。")]
    public float[] comboPitch = new float[] { 1.0000f, 1.1892f, 1.4983f };

    [Header("防连点")]
    [Tooltip("同一个乐器最短触发间隔（秒）。防止手指抖一下连出十几个音。推荐 0.06。")]
    public float minInterval = 0.06f;

    [Header("调试")]
    [Tooltip("勾上 = 每次发声在 Console 打印日志，方便排查为什么没响。")]
    public bool logPlayback = false;

    // ------------------------------------------------------------------
    // 内部状态
    // ------------------------------------------------------------------
    const int VOICES_PER_INSTRUMENT = 3;   // 每个乐器 3 个声道，允许声音叠在一起不打断

    AudioSource[,] voices;      // [乐器, 声道]
    int[] nextVoice;            // 每个乐器下一个要用哪个声道（轮流用）
    float[] lastPlayTime;       // 每个乐器上次发声的时间（用于 minInterval）

    float lastNoteTime = -999f; // 全局上一次发声时间（用于连击窗口）
    int comboLevel = 1;         // 当前连击等级 1~3

    void Awake()
    {
        Instance = this;

        int count = System.Enum.GetValues(typeof(Instrument)).Length;
        voices = new AudioSource[count, VOICES_PER_INSTRUMENT];
        nextVoice = new int[count];
        lastPlayTime = new float[count];

        for (int i = 0; i < count; i++)
        {
            lastPlayTime[i] = -999f;
            for (int v = 0; v < VOICES_PER_INSTRUMENT; v++)
            {
                var go = new GameObject("Voice_" + (Instrument)i + "_" + v);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;      // 0 = 纯 2D，不会因为距离变小声
                src.volume = 1f;            // 真正的音量在播放时按乐器设置
                voices[i, v] = src;
            }
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // 对外接口
    // ------------------------------------------------------------------

    /// <summary>
    /// 全局静态入口：任何脚本都能直接调，不用先找物体。
    /// 如果场景里没有 MusicConductor，会静默跳过（不会报错）。
    /// </summary>
    public static void Play(Instrument ins)
    {
        if (Instance != null) Instance.PlayNote(ins);
    }

    /// <summary>
    /// 播放一个乐器的音。会自己处理连击变调、音量、防连点、声道轮换。
    /// </summary>
    public void PlayNote(Instrument ins)
    {
        int idx = (int)ins;

        // --- 1. 防连点：同一个乐器太短时间内不重复触发 ---
        if (Time.unscaledTime - lastPlayTime[idx] < minInterval) return;
        lastPlayTime[idx] = Time.unscaledTime;

        // --- 2. 拿音效文件，没有就静静跳过 ---
        AudioClip clip = ClipOf(ins);
        if (clip == null)
        {
            if (logPlayback) Debug.LogWarning("[音乐] 乐器 " + ins + " 没有指定 AudioClip");
            return;
        }

        // --- 3. 算连击等级（用 unscaledTime，避免慢动作/暂停把节奏带乱）---
        if (enableCombo && Time.unscaledTime - lastNoteTime <= comboWindow)
        {
            comboLevel++;
            if (comboLevel > comboPitch.Length) comboLevel = 1;   // 3 级之后回到 1
        }
        else
        {
            comboLevel = 1;
        }
        lastNoteTime = Time.unscaledTime;

        float pitch = (comboPitch != null && comboPitch.Length > 0)
            ? comboPitch[Mathf.Clamp(comboLevel - 1, 0, comboPitch.Length - 1)]
            : 1f;

        // --- 4. 挑一个空闲声道（轮流用，这样连按不会互相掐断）---
        int v = nextVoice[idx];
        nextVoice[idx] = (v + 1) % VOICES_PER_INSTRUMENT;
        AudioSource src = voices[idx, v];

        // --- 5. 播放 ---
        src.Stop();                 // 复用前先停掉上一声，避免 pitch 变化时残留
        src.clip = clip;
        src.pitch = pitch;
        src.volume = VolumeOf(ins);
        src.Play();

        if (logPlayback)
            Debug.Log("[音乐] " + ins + "  连击 " + comboLevel + " 级  pitch=" + pitch.ToString("0.0000"));
    }

    // ------------------------------------------------------------------
    // 内部小工具：把"乐器"映射到"文件 / 音量"
    // ------------------------------------------------------------------
    AudioClip ClipOf(Instrument ins)
    {
        switch (ins)
        {
            case Instrument.Triangle: return triangle;
            case Instrument.BassDrum: return bassDrum;
            case Instrument.Trumpet:  return trumpet;
            case Instrument.Violin:   return violin;
            case Instrument.Harp:     return harp;
        }
        return null;
    }

    float VolumeOf(Instrument ins)
    {
        switch (ins)
        {
            case Instrument.Triangle: return volumeTriangle;
            case Instrument.BassDrum: return volumeBassDrum;
            case Instrument.Trumpet:  return volumeTrumpet;
            case Instrument.Violin:   return volumeViolin;
            case Instrument.Harp:     return volumeHarp;
        }
        return 0.5f;
    }
}
