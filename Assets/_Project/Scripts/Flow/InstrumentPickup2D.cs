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

    [Header("反馈")]
    public AudioClip pickupClip;          // 可空
    public UnityEvent onPicked;

    bool picked;

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
        if (InstrumentSequence.Instance == null)
        {
            Debug.LogWarning("[乐器] 场景里没有 InstrumentSequence，道具无法判序：" + name);
            return;
        }
        InstrumentSequence.Instance.Submit(this);   // 顺序对不对由中枢说了算
    }

    // 由中枢调用（= 顺序正确）
    public void Collect()
    {
        picked = true;
        onPicked.Invoke();
        if (pickupClip != null)
        {
            var au = GetComponent<AudioSource>();
            if (au != null) au.PlayOneShot(pickupClip);
            else AudioSource.PlayClipAtPoint(pickupClip, transform.position);
        }
        gameObject.SetActive(false);
    }
}
