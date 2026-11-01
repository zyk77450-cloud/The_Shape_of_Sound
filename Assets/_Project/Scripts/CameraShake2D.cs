using UnityEngine;

/// <summary>
/// 相机颤抖。挂在 Main Camera 上，任何脚本都可以用
/// CameraShake2D.Shake(时长, 幅度) 直接调用。
///
/// 执行顺序设为 100（晚于 CameraFollow2D），这样每帧先把相机跟到玩家身上，
/// 再叠加颤抖偏移；下一帧跟随会重新覆盖位置，所以长期不会累积漂移。
/// </summary>
[DefaultExecutionOrder(100)]
public class CameraShake2D : MonoBehaviour
{
    [Header("默认值（不传参数时使用）")]
    [Tooltip("默认颤抖时长（秒）。")]
    public float defaultDuration = 0.18f;

    [Tooltip("默认颤抖幅度（格）。0.12 约等于 2 像素。")]
    public float defaultMagnitude = 0.12f;

    [Header("时间")]
    [Tooltip("勾上 = 用不受慢动作影响的时间（子弹时间时依然会抖）。一般保持勾选。")]
    public bool useUnscaledTime = true;

    static CameraShake2D instance;

    float timer;
    float duration = 0.18f;
    float magnitude = 0.12f;

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    /// <summary>用默认参数抖一下。</summary>
    public static void Shake()
    {
        if (instance == null) return;
        instance.Play(instance.defaultDuration, instance.defaultMagnitude);
    }

    /// <summary>指定时长和幅度抖一下。幅度单位是「格」。</summary>
    public static void Shake(float duration, float magnitude)
    {
        if (instance == null) return;
        instance.Play(duration, magnitude);
    }

    void Play(float d, float m)
    {
        duration = Mathf.Max(0.02f, d);
        magnitude = Mathf.Max(0f, m);
        timer = duration;
    }

    void LateUpdate()
    {
        if (timer <= 0f) return;

        timer -= useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (timer <= 0f)
        {
            timer = 0f;
            return;
        }

        // 越接近结束幅度越小，收得干脆一点
        float k = timer / duration;
        float amp = magnitude * k * k;

        Vector2 offset = Random.insideUnitCircle * amp;
        transform.position += new Vector3(offset.x, offset.y, 0f);
    }
}
