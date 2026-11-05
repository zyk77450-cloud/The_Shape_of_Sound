using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 乐器收集报数中枢：道具随便什么顺序捡，捡到就报数；
/// 全部集齐时触发 onAllCollected（可接提示/演出）。
/// "按顺序使用"的解谜在最终机关 InstrumentLock2D 里，不在这里。
/// 进度自动进存档：重开游戏时已捡的道具自动消失。
/// 用法：空物体挂本脚本，length=6。
/// </summary>
public class InstrumentSequence : MonoBehaviour
{
    public static InstrumentSequence Instance { get; private set; }

    [Header("数量")]
    public int length = 4;

    [Header("事件")]
    public UnityEvent onAllCollected;   // 全齐 → 拖 GameFlowManager.GoToStage2
    public UnityEvent onWrong;          // 摸错顺序 → 接提示音/抖动
    public UnityEvent<int> onCollected; // 捡对一个（参数=第几个）→ 接音效/UI

    void Awake() { Instance = this; }

    /// <summary>下一个该被捡的编号（0~length-1；返回 -1 = 全齐）</summary>
    public int NextRequired
    {
        get
        {
            for (int i = 0; i < length; i++)
                if (!SaveSystem.HasInstrument(i)) return i;
            return -1;
        }
    }

    /// <summary>道具捡到后报数（收集不限顺序）；全齐触发 onAllCollected。</summary>
    public void NotifyCollected(int id)
    {
        Debug.Log($"[乐器] 收到 Id {id} ｜ Length={length} ｜ 还缺 Id: {NextRequired}");
        onCollected.Invoke(id);
        if (NextRequired == -1)             // -1 = 一件不剩，全齐了
            onAllCollected.Invoke();
    }
}
