using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 乐器道具：玩家碰到 → 交给 InstrumentSequence 判顺序。
/// 开局时若存档里已捡过，自动隐藏（跨会话保留进度）。
/// 用法：Sprite（用策划的 conch/drum/lyre/triangle 等乐器图）
///       + BoxCollider2D(Trigger) + 本脚本，Id 填 0~3，做成预制体摆四件。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class InstrumentPickup2D : MonoBehaviour
{
    [Tooltip("收集顺序编号：必须按 0→1→2→3 摆放（0 = 玩家最先该遇到的那件）")]
    public int id;

    [Header("解锁哪种能力（顺序对了就给，勾一个）")]
    [Tooltip("二段跳")]
    public bool unlockDoubleJump;
    [Tooltip("冲刺")]
    public bool unlockDash;
    [Tooltip("蹬墙跳 + 贴墙下滑")]
    public bool unlockWallJump;
    [Tooltip("游泳")]
    public bool unlockSwim;

    [Header("反馈")]
    public AudioClip pickupClip;          // 可空
    public UnityEvent onPicked;

    bool picked;
    PlayerController2D pc;                // 触碰时记下玩家，Collect 里解锁能力用

    void Reset() { GetComponent<Collider2D>().isTrigger = true; }

    void Start()
    {
        // 读档：已捡过的直接消失
        if (SaveSystem.HasInstrument(id))
        {
            picked = true;
            gameObject.SetActive(false);
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (picked || !other.CompareTag("Player")) return;
        pc = other.GetComponent<PlayerController2D>();
        Collect();                          // 收集不限顺序：碰到就收
    }

    // 碰到即收（顺序自由）；存档/能力/反馈在这里一起给
    public void Collect()
    {
        if (picked) return;
        picked = true;

        SaveSystem.SetInstrument(id, true);   // 进存档：背包显示 + 重开自动消失

        onPicked.Invoke();

        // 能力解锁（勾了哪个给哪个）
        if (pc != null)
        {
            if (unlockDoubleJump) pc.UnlockDoubleJump();
            if (unlockDash)       pc.UnlockDash();
            if (unlockWallJump)   pc.UnlockWallJump();
            if (unlockSwim)       pc.UnlockSwim();
        }

        MusicConductor.PlayPickup();          // 拾取音
        if (pickupClip != null)
        {
            var au = GetComponent<AudioSource>();
            if (au != null) au.PlayOneShot(pickupClip);
            else AudioSource.PlayClipAtPoint(pickupClip, transform.position);
        }
        gameObject.SetActive(false);

        // 报数：中枢记事件；全齐触发 onAllCollected
        if (InstrumentSequence.Instance != null)
            InstrumentSequence.Instance.NotifyCollected(id);
        else
            Debug.LogWarning("[乐器] 场景里没有 InstrumentSequence，集齐事件不会触发：" + name);
    }
}
