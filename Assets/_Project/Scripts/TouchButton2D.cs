using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 触屏虚拟按钮：按下写 TouchInput 总线，抬起还原。跳跃/回声/冲刺/交互通用。
/// 用法：UGUI Button 或 Image 上挂本脚本，Kind 选对应功能。
/// </summary>
public class TouchButton2D : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public enum Kind { Jump, Dash, Wave, Interact }
    public Kind kind;

    Image img;
    Color baseColor;

    void Awake()
    {
        img = GetComponent<Image>();
        if (img != null) baseColor = img.color;
    }

    public void OnPointerDown(PointerEventData e)
    {
        switch (kind)
        {
            case Kind.Jump:     TouchInput.jumpPressed = true; TouchInput.jumpHeld = true; break;
            case Kind.Dash:     TouchInput.dashPressed = true; break;
            case Kind.Wave:     TouchInput.wavePressed = true; break;
            case Kind.Interact: TouchInput.interactPressed = true; break;
        }
        if (img != null) img.color = new Color(baseColor.r, baseColor.g, baseColor.b,
                                               Mathf.Clamp01(baseColor.a + 0.3f)); // 按下亮一档
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (kind == Kind.Jump) TouchInput.jumpHeld = false;
        if (img != null) img.color = baseColor;
    }

    // 手指滑出按钮再松开：PointerUp 可能收不到，这里兜底，防止 jumpHeld 卡死
    public void OnPointerExit(PointerEventData e) { OnPointerUp(e); }
}
