using UnityEngine;

/// <summary>
/// 让物体绕 Z 轴匀速旋转。
/// 挂在齿轮、风扇这类"要自己转"的物体上。
/// 只负责转，不负责伤害（伤害交给 Hazard2D）。
/// </summary>
public class Spin2D : MonoBehaviour
{
    [Header("转速")]
    [Tooltip("每秒转多少度。正数=逆时针，负数=顺时针。\n调大=转得更快，调小=转得更慢。\n参考：90 = 4 秒一圈，180 = 2 秒一圈。推荐 60~180")]
    public float degreesPerSecond = 90f;

    [Header("调试")]
    [Tooltip("勾上=停止旋转（只用来调试）。不勾=正常转")]
    public bool paused = false;

    void Update()
    {
        if (paused) return;
        transform.Rotate(0f, 0f, degreesPerSecond * Time.deltaTime);
    }
}
