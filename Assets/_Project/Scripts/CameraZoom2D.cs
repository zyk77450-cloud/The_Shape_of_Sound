using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 运镜：第二个齿轮落入画面时，相机开始放大（正交尺寸 11.25 → 6，即纵向 360 → 192 像素），
/// 齿轮落地的那一刻正好缩完，然后画面黑屏（闭眼）并不再恢复——后续转场交给你自己或程序。
///
/// 为什么不直接改 Camera.orthographicSize：
///   Main Camera 上有 Pixel Perfect Camera，它每帧都会把正交尺寸强行写回 11.25。
///   所以放大期间必须临时把它关掉（方案 A），缩完再按需要恢复。
/// </summary>
public class CameraZoom2D : MonoBehaviour
{
    [Header("① 要盯的齿轮")]
    [Tooltip("第二个（右边）齿轮的物体。它一被激活（开始下落）就开始倒计时准备放大。")]
    public GameObject gear;

    [Header("② 缩放参数")]
    [Tooltip("目标正交尺寸。6 = 纵向 192 像素（192 ÷ 2 ÷ 16 = 6）；11.25 = 现在的 360 像素。")]
    public float targetOrthoSize = 6f;

    [Tooltip("提前多久开始缩（秒）。0.6 = 齿轮落地前 0.6 秒开始，落地时刚好缩完。")]
    public float leadTime = 0.6f;

    [Tooltip("负责显示的相机。留空 = 自动取 Camera.main。")]
    public Camera targetCamera;

    [Tooltip("Pixel Perfect Camera 组件（拖 Main Camera 上的那个）。放大期间会被临时关闭；留空则不处理。")]
    public Behaviour pixelPerfectCamera;

    [Tooltip("缩完之后是否重新打开 Pixel Perfect Camera。因为接下来就黑屏转场了，保持关闭也可以。")]
    public bool restorePixelPerfect = false;

    [Header("③ 黑屏（闭眼）")]
    [Tooltip("勾上 = 缩完之后画面变黑，并且不再恢复（用来接转场）。")]
    public bool blackoutOnFinish = true;

    [Tooltip("黑屏淡入时长（秒）。0.12 左右像「猛地闭眼」，0.4 像「慢慢昏过去」。")]
    public float blackoutFade = 0.12f;

    [Tooltip("黑屏颜色。纯黑 (0,0,0) 最像闭眼；深蓝 (0.02,0.03,0.06) 更贴你的场景色。")]
    public Color blackoutColor = new Color(0f, 0f, 0f, 1f);

    [Header("④ 事件")]
    [Tooltip("画面全黑时触发。可以在这里切场景 / 播音效 / 显示字幕。")]
    public UnityEvent onBlackout;

    [Header("⑤ 调试")]
    [Tooltip("勾上 = 在 Console 打印倒计时与缩放过程。")]
    public bool debugLog = true;

    // ---------- 运行时 ----------

    bool armed;          // 齿轮已经出现，开始倒计时
    bool zoomStarted;
    float zoomStartTime;

    Image blackImage;
    Canvas blackCanvas;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null && debugLog)
        {
            Debug.LogWarning("[运镜] 没找到相机，缩放不会生效。请把 Main Camera 拖到 Target Camera。");
        }

        // Pixel Perfect Camera 自动识别（按类型名，避免依赖程序集引用）
        if (pixelPerfectCamera == null)
        {
            Behaviour[] all = GetComponents<Behaviour>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetType().Name == "PixelPerfectCamera")
                {
                    pixelPerfectCamera = all[i];
                    break;
                }
            }
        }
    }

    // 齿轮没拖 → 从 CutsceneFlood.gearRight 自动取（每 0.5 秒找一次，别每帧扫全场）
    float nextResolveTime;

    void ResolveGearIfNeeded()
    {
        if (gear != null) return;
        if (Time.time < nextResolveTime) return;
        nextResolveTime = Time.time + 0.5f;

        CutsceneFlood[] all = FindObjectsOfType<CutsceneFlood>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gearRight != null)
            {
                gear = all[i].gearRight;
                if (debugLog) Debug.Log("[运镜] 自动找到右齿轮：" + gear.name);
                return;
            }
        }
    }

    void Update()
    {
        // ① 齿轮一被激活（开始下落），算出落地时间，倒推该什么时候开始缩
        if (!armed)
        {
            ResolveGearIfNeeded();

            if (gear != null && gear.activeInHierarchy)
            {
                armed = true;
                float fallTime = EstimateFallTime();
                zoomStartTime = Time.time + Mathf.Max(0f, fallTime - leadTime);

                if (debugLog)
                {
                    Debug.Log("[运镜] 齿轮出现，预计 " + fallTime.ToString("0.00") + " 秒后落地，"
                              + Mathf.Max(0f, fallTime - leadTime).ToString("0.00") + " 秒后开始放大");
                }
            }
        }

        // ② 到点开始缩
        if (armed && !zoomStarted && Time.time >= zoomStartTime)
        {
            zoomStarted = true;
            StartCoroutine(ZoomRoutine());
        }
    }

    /// <summary>估算齿轮从出生点落到地面要多久。用 GearDrop2D 的 dropHeight / gravity 算。</summary>
    float EstimateFallTime()
    {
        GearDrop2D drop = gear.GetComponent<GearDrop2D>();
        if (drop != null && drop.gravity > 0.01f)
        {
            // 自由落体：h = 0.5 * g * t² → t = sqrt(2h / g)
            return Mathf.Sqrt(2f * Mathf.Max(0.01f, drop.dropHeight) / drop.gravity);
        }

        // 没有 GearDrop2D 时，按默认 8 格 / 重力 39 估算（约 0.64 秒）
        return 0.64f;
    }

    IEnumerator ZoomRoutine()
    {
        if (targetCamera == null) yield break;

        float from = targetCamera.orthographicSize;
        float dur = Mathf.Max(0.05f, leadTime);

        // 方案 A：先关掉 Pixel Perfect Camera，否则它每帧把正交尺寸写回去
        if (pixelPerfectCamera != null)
        {
            pixelPerfectCamera.enabled = false;
            if (debugLog) Debug.Log("[运镜] 已临时关闭 Pixel Perfect Camera");
        }

        if (debugLog)
        {
            Debug.Log("[运镜] 开始放大：" + from.ToString("0.00") + " → " + targetOrthoSize.ToString("0.00")
                      + "，用时 " + dur.ToString("0.00") + " 秒");
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // SmoothStep：起步和收尾都慢一点，中间快
            targetCamera.orthographicSize = Mathf.Lerp(from, targetOrthoSize, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }

        targetCamera.orthographicSize = targetOrthoSize;

        if (restorePixelPerfect && pixelPerfectCamera != null)
        {
            pixelPerfectCamera.enabled = true;
            if (debugLog) Debug.Log("[运镜] 已恢复 Pixel Perfect Camera（正交尺寸会被它写回 11.25）");
        }

        // 缩完 = 齿轮落地 = 闭眼
        if (blackoutOnFinish) yield return StartCoroutine(BlackoutRoutine());
    }

    IEnumerator BlackoutRoutine()
    {
        EnsureBlackOverlay();

        float t = 0f;
        float dur = Mathf.Max(0.01f, blackoutFade);
        Color c = blackoutColor;

        while (t < dur)
        {
            t += Time.deltaTime;
            c.a = Mathf.Clamp01(t / dur);
            blackImage.color = c;
            yield return null;
        }

        c.a = 1f;
        blackImage.color = c;

        if (debugLog) Debug.Log("[运镜] 画面已全黑，触发 onBlackout");

        if (onBlackout != null) onBlackout.Invoke();
    }

    // 运行时建一个覆盖整个屏幕的黑色 UI（不用手动建 Canvas）
    void EnsureBlackOverlay()
    {
        if (blackImage != null) return;

        GameObject canvasGo = new GameObject("BlackoutCanvas");
        canvasGo.transform.SetParent(transform, false);

        blackCanvas = canvasGo.AddComponent<Canvas>();
        blackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blackCanvas.sortingOrder = 999;

        GameObject imgGo = new GameObject("Black");
        imgGo.transform.SetParent(canvasGo.transform, false);

        blackImage = imgGo.AddComponent<Image>();
        blackImage.raycastTarget = false;
        blackImage.color = new Color(blackoutColor.r, blackoutColor.g, blackoutColor.b, 0f);

        RectTransform rt = imgGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>手动开始放大（想用别的条件触发时调它）。</summary>
    public void StartZoom()
    {
        if (zoomStarted) return;
        armed = true;
        zoomStarted = true;
        StartCoroutine(ZoomRoutine());
    }
}
