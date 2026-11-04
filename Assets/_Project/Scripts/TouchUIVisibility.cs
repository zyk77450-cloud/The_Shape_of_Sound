using UnityEngine;

/// <summary>
/// 触屏 UI 显隐：玩家控制器被锁（菜单/运镜/转场）时隐藏，可操作时显示。
/// 挂在 TouchCanvas 根上，Player 拖进 player 字段。
/// </summary>
public class TouchUIVisibility : MonoBehaviour
{
    public PlayerController2D player;
    Canvas canvas;

    void Start() { canvas = GetComponent<Canvas>(); }
    void Update() { canvas.enabled = player != null && player.enabled; }
}
