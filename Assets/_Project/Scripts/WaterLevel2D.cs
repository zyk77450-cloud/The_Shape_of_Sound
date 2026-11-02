using System.Collections;
using UnityEngine;

/// <summary>
/// 水位控制器：改水面高度时，同时同步【碰撞盒 / 水体贴图 / 水面条】三者，
/// 保证"看得见的水"和"真的能淹到人的水"永远一致。
/// 挂在 WaterZone 根物体上。
/// </summary>
[DisallowMultipleComponent]
public class WaterLevel2D : MonoBehaviour
{
    [Header("引用（留空会自动找）")]
    [Tooltip("水区的 BoxCollider2D（必须勾 Is Trigger）。留空 = 自动取本物体上的。")]
    public BoxCollider2D waterCollider;

    [Tooltip("水体贴图物体（一个 1x1 的方块，会被纵向拉伸）。留空 = 自动找名为 Square 的子物体。")]
    public Transform waterVisual;

    [Tooltip("水面条物体（可选，会贴着水面上下移动）。留空 = 自动找名为 Surface 的子物体；找不到就跳过。")]
    public Transform surfaceVisual;

    [Header("初始状态")]
    [Tooltip("开局时的水面高度（世界 Y）。填 0 = 使用当前碰撞盒的顶面（一般就是要这个）。")]
    public float initialTopY = 0f;

    [Tooltip("水面条比水面额外高出的距离（格）。让浪花尖刚好浮在水面上，一般填 0 ~ 0.5。")]
    public float surfaceLift = 0f;

    [Header("调试")]
    [Tooltip("勾上后，每次改水位都会在 Console 打印新的水面高度和深度。")]
    public bool debugLog = false;

    float bottomY;                 // 水底（世界 Y），整个过场里保持不变
    Vector3 visualBaseScale;       // 水体贴图的初始缩放（只借它的 x/z）

    /// <summary>当前水面高度（世界 Y）。</summary>
    public float WaterTopY { get; private set; }

    /// <summary>水底高度（世界 Y）。</summary>
    public float WaterBottomY { get { return bottomY; } }

    /// <summary>当前水深（格）。</summary>
    public float Depth { get { return WaterTopY - bottomY; } }

    void Awake()
    {
        ResolveRefs();

        if (waterVisual != null) visualBaseScale = waterVisual.localScale;

        if (waterCollider != null)
        {
            bottomY = waterCollider.bounds.min.y;
            float startY = Mathf.Approximately(initialTopY, 0f) ? waterCollider.bounds.max.y : initialTopY;
            SetLevel(startY);
        }
        else
        {
            Debug.LogWarning("[水位] 没找到 BoxCollider2D，脚本不起作用。", this);
        }
    }

    void ResolveRefs()
    {
        if (waterCollider == null) waterCollider = GetComponent<BoxCollider2D>();

        if (waterVisual == null)
        {
            Transform t = transform.Find("Square");
            if (t == null && transform.childCount > 0) t = transform.GetChild(0);
            waterVisual = t;
        }

        if (surfaceVisual == null) surfaceVisual = transform.Find("Surface");
    }

    /// <summary>立刻把水面设到某个世界 Y 高度。</summary>
    public void SetLevel(float topY)
    {
        if (waterCollider == null) return;

        float y = Mathf.Max(bottomY + 0.1f, topY);   // 至少 0.1 格深，避免尺寸变成 0
        WaterTopY = y;

        Transform ct = waterCollider.transform;
        float sy = Mathf.Max(0.0001f, ct.lossyScale.y);
        float h = y - bottomY;

        // 1) 碰撞盒：底边钉死，只把顶边往上推
        waterCollider.size = new Vector2(waterCollider.size.x, h / sy);
        waterCollider.offset = new Vector2(
            waterCollider.offset.x,
            ((bottomY + y) * 0.5f - ct.position.y) / sy);

        // 2) 水体贴图：纵向拉伸到同样高，中心对齐
        if (waterVisual != null)
        {
            float py = waterVisual.parent != null ? waterVisual.parent.lossyScale.y : 1f;
            py = Mathf.Max(0.0001f, py);
            waterVisual.localScale = new Vector3(visualBaseScale.x, h / py, visualBaseScale.z);
            Vector3 lp = waterVisual.localPosition;
            waterVisual.localPosition = new Vector3(lp.x, (bottomY + y) * 0.5f - ct.position.y, lp.z);
        }

        // 3) 水面条：贴着水面走
        if (surfaceVisual != null)
        {
            Vector3 sp = surfaceVisual.localPosition;
            surfaceVisual.localPosition = new Vector3(sp.x, y + surfaceLift - ct.position.y, sp.z);
        }

        if (debugLog)
            Debug.Log(string.Format("[水位] 水面 Y={0:F2}  水深 {1:F2} 格", y, h), this);
    }

    /// <summary>在 duration 秒内平滑升/降到某个水面高度。</summary>
    public IEnumerator RiseTo(float targetTopY, float duration)
    {
        float from = WaterTopY;
        if (duration <= 0.01f)
        {
            SetLevel(targetTopY);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            k = k * k * (3f - 2f * k);                       // SmoothStep：起步和收尾都柔
            SetLevel(Mathf.Lerp(from, targetTopY, k));
            yield return null;
        }
        SetLevel(targetTopY);
    }
}
