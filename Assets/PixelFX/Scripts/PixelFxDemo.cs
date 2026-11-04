using UnityEngine;
using UnityEngine.UI;

namespace PixelFx
{
    /// <summary>
    /// 一键演示：createFrame(320,180) → 气泡特效 update/draw → composite → 整数倍放大显示。
    /// 用法：空场景里建一个空物体，挂上本组件，Play。
    /// 交互：点击画面 = 在点击处爆出一团气泡。
    /// </summary>
    [DisallowMultipleComponent]
    public class PixelFxDemo : MonoBehaviour
    {
        [Header("帧缓冲（≈ createFrame）")]
        [SerializeField] Vector2Int frameSize = new Vector2Int(320, 180);
        [SerializeField] Color32 clearColor = new Color32(9, 34, 58, 255); // 深水底色

        [Header("特效参数（≈ createFx 的 config）")]
        [SerializeField] BubblesFx.Settings bubbles = new BubblesFx.Settings();

        [Header("合成（≈ composite）")]
        [Tooltip("环境光强度")]
        [SerializeField] float ambient = 0.4f;
        [Tooltip("色阶量化：0=关闭；8/16/32 出复古限色（≈ 传调色板）")]
        [SerializeField] int colorLevels = 0;
        [Tooltip("用 PixelFX/Composite shader 在 GPU 上合成；找不到 shader 时自动退回 CPU")]
        [SerializeField] bool gpuComposite = true;
        [Tooltip("整数倍放大，保证像素锐利")]
        [SerializeField] bool integerScale = true;

        PixelFrame _frame;
        BubblesFx _fx;
        Texture2D _outTex;      // CPU 合成输出
        Color32[] _outPixels;
        Material _mat;          // GPU 合成材质
        RawImage _raw;

        void Awake()
        {
            _frame = new PixelFrame(frameSize.x, frameSize.y);   // ≈ createFrame(320, 180)
            _fx = new BubblesFx(bubbles)                         // ≈ createFx(bubbles, {...})
            {
                KillHeight = frameSize.y + 6f                    // 漂出画面顶部就破裂
            };
            _fx.Burst(24);                                       // 开场先来一团

            _outTex = new Texture2D(frameSize.x, frameSize.y, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            _outPixels = new Color32[frameSize.x * frameSize.y];

            CreateDisplay();
        }

        void CreateDisplay()
        {
            var canvasGo = new GameObject("PixelFX Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var rawGo = new GameObject("Frame", typeof(RawImage));
            rawGo.transform.SetParent(canvasGo.transform, false);
            _raw = rawGo.GetComponent<RawImage>();
            _raw.raycastTarget = false;

            Shader shader = Shader.Find("PixelFX/Composite");
            if (gpuComposite && shader != null)
            {
                _mat = new Material(shader);
                _mat.SetTexture("_LightTex", _frame.LightTex);
                _mat.SetFloat("_LightIntensity", 1f);
                _raw.material = _mat;
                _raw.texture = _frame.ColorTex;   // RawImage 会把它绑到材质的 _MainTex
            }
            else
            {
                _raw.texture = _outTex;           // CPU 合成输出
            }

            LayoutDisplay();
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 1f / 20f); // 防卡顿尖峰

            // ---- 等价于：fx.update(dt); fx.draw(frame); ----
            _fx.Update(dt);
            _frame.Clear(clearColor);
            _fx.Draw(_frame);

            // ---- 等价于：composite(frame, imageData.data, null) ----
            if (_mat != null)
            {
                _frame.Upload();                          // GPU 合成：shader 里 color × light
                _mat.SetFloat("_Ambient", ambient);
                _mat.SetFloat("_ColorLevels", colorLevels);
            }
            else
            {
                PixelComposite.ToPixels(_frame, _outPixels, ambient, colorLevels);
                _outTex.SetPixels32(_outPixels);
                _outTex.Apply(false);
            }

            LayoutDisplay();
            HandleClick();
        }

        // 按窗口大小整数倍缩放，保持 16:9 并居中（letterbox）
        void LayoutDisplay()
        {
            if (_raw == null) return;

            float scale = Mathf.Min(
                (float)Screen.width / frameSize.x,
                (float)Screen.height / frameSize.y);
            if (integerScale && scale >= 1f) scale = Mathf.Floor(scale);

            RectTransform rt = _raw.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(frameSize.x * scale, frameSize.y * scale);
        }

        // 点击画面 → 换算成帧像素坐标 → 在该处爆一团气泡
        void HandleClick()
        {
            if (_raw == null || !Input.GetMouseButtonDown(0)) return;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _raw.rectTransform, Input.mousePosition, null, out local))
                return;

            Vector2 size = _raw.rectTransform.rect.size;
            if (Mathf.Abs(local.x) > size.x * 0.5f || Mathf.Abs(local.y) > size.y * 0.5f)
                return; // 点在画面外

            Vector2 pixel = new Vector2(
                (local.x + size.x * 0.5f) / size.x * frameSize.x,
                (local.y + size.y * 0.5f) / size.y * frameSize.y);

            _fx.Burst(24, pixel);
        }

        void OnDestroy()
        {
            if (_frame != null) _frame.Dispose();
            if (_outTex != null) Destroy(_outTex);
            if (_mat != null) Destroy(_mat);
        }
    }
}
