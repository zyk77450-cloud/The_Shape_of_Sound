using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 触屏 UI 显隐：玩家控制器被锁（菜单/运镜/转场）时隐藏，可操作时显示。
/// 挂在 TouchCanvas 根上，Player 拖进 player 字段。
/// 顺带修一个全局 UI 坑：空格 = UGUI 的"确认键"，点过的按钮保持选中时，
/// 游戏里按空格跳会把那个按钮再点一次（比如暂停）。这里全局关掉键盘导航。
/// </summary>
public class TouchUIVisibility : MonoBehaviour
{
    public PlayerController2D player;
    Canvas canvas;
    EventSystem es;

    void Start()
    {
        canvas = GetComponent<Canvas>();
        es = FindObjectOfType<EventSystem>();
        if (es != null)
        {
            // 空格/回车不再"确认"最近点过的按钮（修跳跃触发暂停），
            // 方向键也不再乱选按钮。鼠标/触摸点击完全不受影响。
            es.sendNavigationEvents = false;
            es.SetSelectedGameObject(null);
        }
    }

    void Update()
    {
        canvas.enabled = player != null && player.enabled;

        // 兜底：任何时候都不让按钮保持选中（防其他途径再次选中）
        if (es != null && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);
    }
}
