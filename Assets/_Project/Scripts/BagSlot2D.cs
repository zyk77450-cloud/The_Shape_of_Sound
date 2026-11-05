using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包格子：Instrument Id 0~3 = 四件乐器（读存档，按序收集）；
/// -1 = 纯占位格（给以后的道具留着，永远显示空剪影）。
/// 挂在每个格子上，iconImage 拖格子里的子 Icon Image。
/// </summary>
public class BagSlot2D : MonoBehaviour
{
    [Tooltip("0~3 = 乐器编号；-1 = 占位空格")]
    public int instrumentId = 0;
    public Sprite icon;              // 策划的乐器图（conch/drum/lyre/triangle）
    public Image iconImage;          // 格子里的子 Image

    public void Refresh()
    {
        if (iconImage == null) return;
        bool owned = instrumentId >= 0 && SaveSystem.HasInstrument(instrumentId);
        iconImage.sprite = icon;
        iconImage.color = owned ? Color.white
                                : new Color(1f, 1f, 1f, 0.12f);   // 没拿到 = 隐约剪影
    }
}
