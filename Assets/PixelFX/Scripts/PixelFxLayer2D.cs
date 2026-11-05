using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PixelFx
{
    /// <summary>所有想画到像素层的特效实现这个接口（如 PlayerFx2D）。</summary>
    public interface IPixelFxDrawer
    {
        void PixelDraw(PixelFrame frame, Camera cam);
    }

    /// <summary>
    /// 全屏像素特效层：一块透明的 640×360 帧缓冲 + 合成显示。
    /// 场景里建一个空物体挂上即可；所有 IPixelFxDrawer 自动注册到这里来画。
    /// 渲染顺序 sort 3：在游戏画面之上、触屏UI(5)/菜单(10)/黑幕(100) 之下。
    /// </summary>
    [DefaultExecutionOrder(50)]   // 相机跟随(LateUpdate)之后再画，不迟到一帧
    public class PixelFxLayer2D : MonoBehaviour
    {
        public static PixelFxLayer2D Instance { get; private set; }

        [Header("帧")]
        public Vector2Int frameSize = new Vector2Int(640, 360);
        [Tooltip("1 = 保持特效原色，glow 只负责增亮")]
        public float ambient = 1f;
        [Tooltip("0 = 关闭；16 出复古限色")]
        public int colorLevels = 0;
        public bool gpuComposite = true;

        PixelFrame _frame;
        Material _mat;
        Texture2D _outTex;
        Color32[] _outPx;
        readonly List<IPixelFxDrawer> _drawers = new List<IPixelFxDrawer>();
        Camera _cam;

        void Awake()
        {
            Instance = this;
            _cam = Camera.main;
            _frame = new PixelFrame(frameSize.x, frameSize.y);

            var canvasGo = new GameObject("PixelFX Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3;

            var rawGo = new GameObject("Frame", typeof(RawImage));
            rawGo.transform.SetParent(canvasGo.transform, false);
            var raw = rawGo.GetComponent<RawImage>();
            raw.raycastTarget = false;                        // 不挡点击
            var rt = raw.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;       // 铺满全屏

            Shader sh = Shader.Find("PixelFX/Composite");
            if (gpuComposite && sh != null)
            {
                _mat = new Material(sh);
                _mat.SetTexture("_LightTex", _frame.LightTex);
                _mat.SetFloat("_LightIntensity", 1f);
                raw.material = _mat;
                raw.texture = _frame.ColorTex;
            }
            else
            {
                _outTex = new Texture2D(frameSize.x, frameSize.y, TextureFormat.RGBA32, false)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _outPx = new Color32[frameSize.x * frameSize.y];
                raw.texture = _outTex;
            }
        }

        void OnEnable()
        {
            // 场景里已存在的特效全部收编（解决挂载先后顺序问题）
            foreach (var mb in FindObjectsOfType<MonoBehaviour>())
                if (mb is IPixelFxDrawer d) Register(d);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_frame != null) _frame.Dispose();
            // 修复：这里原本会 Destroy(_mat) / Destroy(_outTex)。
            // 但全屏 RawImage 是子物体，销毁顺序在后，同一帧还会再渲染一次，
            // 于是取到"已销毁的材质"报 MissingReferenceException（切场景时报两条）。
            // 运行时创建的材质/贴图交给 Unity 随场景卸载回收即可，不必手动 Destroy。
        }

        public void Register(IPixelFxDrawer d)
        {
            if (!_drawers.Contains(d)) _drawers.Add(d);
        }

        public void Unregister(IPixelFxDrawer d)
        {
            _drawers.Remove(d);
        }

        void LateUpdate()
        {
            _frame.Clear(new Color32(0, 0, 0, 0));            // 透明底，只看见特效

            for (int i = 0; i < _drawers.Count; i++)
                if (_drawers[i] != null)
                    _drawers[i].PixelDraw(_frame, _cam);

            if (_mat != null)
            {
                _frame.Upload();
                _mat.SetFloat("_Ambient", ambient);
                _mat.SetFloat("_ColorLevels", colorLevels);
            }
            else
            {
                PixelComposite.ToPixels(_frame, _outPx, ambient, colorLevels);
                _outTex.SetPixels32(_outPx);
                _outTex.Apply(false);
            }
        }

        /// <summary>世界坐标 → 帧像素（屏幕左下原点，和帧缓冲一致）。</summary>
        public static Vector2 WorldToPixel(Camera cam, PixelFrame frame, Vector3 world)
        {
            if (cam == null) cam = Camera.main;
            Vector3 s = cam.WorldToScreenPoint(world);
            return new Vector2(s.x / Screen.width * frame.Width, s.y / Screen.height * frame.Height);
        }
    }
}
