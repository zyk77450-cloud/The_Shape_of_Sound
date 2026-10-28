using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 能力道具：玩家碰到 → 解锁对应能力 → 播放 onPicked（接音效/UI）→ 消失。
/// 一个道具解锁一种能力（勾选其一）。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AbilityPickup2D : MonoBehaviour
{
    [Header("解锁哪种能力（勾一个就行）")]
    [Tooltip("勾上 = 捡到后学会二段跳（跳到空中还能再跳一次）。")]
    public bool unlockDoubleJump;

    [Tooltip("勾上 = 捡到后学会冲刺（横向或斜向冲一段，能撞开距离）。")]
    public bool unlockDash;

    [Tooltip("勾上 = 捡到后学会蹬墙跳 + 贴墙下滑（这两个能力是绑在一起的，一次全给）。")]
    public bool unlockWallJump;

    [Header("拾取反馈")]
    [Tooltip("捡到时额外触发的事，可以拖音效、动画、弹提示 UI。\n不填 = 捡到就静静消失。")]
    public UnityEvent onPicked;

    bool picked;

    void Reset() { GetComponent<Collider2D>().isTrigger = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (picked || !other.CompareTag("Player")) return;
        var pc = other.GetComponent<PlayerController2D>();
        if (pc == null) return;

        if (unlockDoubleJump) pc.UnlockDoubleJump();
        if (unlockDash)       pc.UnlockDash();
        if (unlockWallJump)   pc.UnlockWallJump();   // v8: 蹬墙跳道具

        onPicked.Invoke();
        picked = true;
        Destroy(gameObject);
    }
}
