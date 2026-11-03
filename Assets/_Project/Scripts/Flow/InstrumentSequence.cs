using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 乐器顺序收集中枢：必须按 id 0→1→2→3 的顺序触碰拾取。
/// 顺序不对：拒绝（onWrong 反馈），已捡的不清（不惩罚手滑）；
/// 全部按序捡完：onAllCollected（拖 GameFlowManager.GoToStage2）。
/// 进度自动进存档：重开游戏时已捡的道具自动消失。
/// 用法：空物体挂本脚本，length=4，On All Collected 拖 GameFlow 的 GoToStage2。
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

    public void Submit(InstrumentPickup2D pickup)
    {
        if (NextRequired != pickup.id)
        {
            onWrong.Invoke();               // 顺序不对：拒绝，等它排到再说
            return;
        }

        SaveSystem.SetInstrument(pickup.id, true);
        pickup.Collect();                   // 消失 + 拾取反馈
        onCollected.Invoke(pickup.id);

        if (NextRequired == -1)
            onAllCollected.Invoke();
    }
}
