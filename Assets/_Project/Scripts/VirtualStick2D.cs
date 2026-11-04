using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 触屏摇杆（左下"移动"圆盘）：按住拖动 → 写 TouchInput.left/right + v。
/// 垂直分量给游泳八向和单向平台下穿用。
/// 用法：Image 圆盘上挂本脚本，子物体小圆点拖进 knob 字段。
/// </summary>
public class VirtualStick2D : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform knob;       // 圆盘里的小圆点（子物体拖进来）
    public float radius = 52f;       // 小圆点最大活动半径
    public float deadZone = 0.25f;   // 死区：轻轻搭一下不算方向

    public void OnPointerDown(PointerEventData e) { UpdateStick(e); }
    public void OnDrag(PointerEventData e) { UpdateStick(e); }

    void UpdateStick(PointerEventData e)
    {
        // Overlay 模式相机传 null 即可
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)transform, e.position, e.pressEventCamera, out Vector2 local);

        Vector2 dir = local / radius;
        if (dir.magnitude > 1f) dir.Normalize();
        if (knob != null) knob.anchoredPosition = dir * radius;

        bool active = dir.magnitude > deadZone;
        TouchInput.left  = active && dir.x < -0.25f;
        TouchInput.right = active && dir.x >  0.25f;
        TouchInput.v     = active ? dir.y : 0f;
    }

    public void OnPointerUp(PointerEventData e)
    {
        TouchInput.left = TouchInput.right = false;
        TouchInput.v = 0f;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }
}
