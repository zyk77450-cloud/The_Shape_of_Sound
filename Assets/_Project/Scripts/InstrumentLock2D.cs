using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 乐器序列锁（最终机关）：集齐前置乐器后，在背包里按 0→1→2→3 的顺序点击乐器格子。
/// 按对一步 → onStepCorrect（接 ClockProgress.AddProgress）
/// 按错一步 → onWrong（接 ClockProgress.ResetProgress，进度清零）
/// 全部按对 → onUnlocked（先关背包再接过场，否则 timeScale 冻着过场不动）
/// 点击源：背包格 Button 的 On Click → Try(该格乐器Id)。
/// </summary>
public class InstrumentLock2D : MonoBehaviour
{
    [Header("序列")]
    [Tooltip("按对几下算激活")]
    public int length = 4;
    [Tooltip("哪些乐器收集齐了机关才生效（默认前四件 0~3）")]
    public int[] requiredInstruments = { 0, 1, 2, 3 };

    [Header("事件")]
    [Tooltip("按对一步（参数=第几步）→ 接 ClockProgress.AddProgress")]
    public UnityEvent<int> onStepCorrect;
    [Tooltip("按错 → 接 ClockProgress.ResetProgress")]
    public UnityEvent onWrong;
    [Tooltip("全部按对 → 最终机关激活（开门/动画2）")]
    public UnityEvent onUnlocked;

    int step;    // 当前该点的乐器 id（从 0 数）
    bool done;   // 激活后锁死，乱点不再清零

    /// 背包格 Button 的 On Click 调这个，参数填该格的乐器 Id
    public void Try(int instrumentId)
    {
        if (done) return;

        // 没拿到的乐器点了没反应（空格子不算按错，不惩罚乱点）
        if (!SaveSystem.HasInstrument(instrumentId)) return;

        for (int i = 0; i < requiredInstruments.Length; i++)
            if (!SaveSystem.HasInstrument(requiredInstruments[i]))
            {
                Debug.Log("[乐器锁] 乐器还没集齐，机关不理人");
                return;
            }

        if (instrumentId == step)
        {
            step++;
            onStepCorrect.Invoke(step);
            if (step >= length)
            {
                done = true;
                onUnlocked.Invoke();
            }
        }
        else
        {
            step = 0;
            onWrong.Invoke();
        }
    }
}