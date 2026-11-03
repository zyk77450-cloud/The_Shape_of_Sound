using System;
using UnityEngine;

namespace PixelFx
{
    /// <summary>
    /// 等价于 pixelfx 的 createFrame(width, height)：
    /// 一块低分辨率的离屏缓冲，由「颜色层 + 光照层」组成。
    /// 特效只负责往两层里写数据；最终由 PixelComposite（CPU）或 PixelFX/Composite shader（GPU）合成。
    /// </summary>
    public sealed class PixelFrame : IDisposable
    {
        public readonly int Width;
        public readonly int Height;

        /// <summary>颜色层：特效直接写入/混合的 RGBA 像素。</summary>
        public readonly Color32[] Color;

        /// <summary>光照层：0~1 的单通道光强，特效做加法累积。</summary>
        public readonly float[] Light;

        /// <summary>颜色层上传 GPU 后的贴图（供 GPU 合成用）。</summary>
        public Texture2D ColorTex { get; private set; }

        /// <summary>光照层上传 GPU 后的贴图（r 通道 = 光强）。</summary>
        public Texture2D LightTex { get; private set; }

        readonly Color32[] _lightUpload;

        public PixelFrame(int width, int height)
        {
            Width = width;
            Height = height;
            Color = new Color32[width * height];
            Light = new float[width * height];
            _lightUpload = new Color32[width * height];

            ColorTex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,   // 像素风的关键：放大不做插值
                wrapMode = TextureWrapMode.Clamp
            };
            LightTex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        /// <summary>每帧 draw 之前清空：颜色层填背景色，光照层归零。</summary>
        public void Clear(Color32 backgroundColor)
        {
            for (int i = 0; i < Color.Length; i++) Color[i] = backgroundColor;
            Array.Clear(Light, 0, Light.Length);
        }

        public bool Contains(int x, int y) =>
            x >= 0 && x < Width && y >= 0 && y < Height;

        public int Index(int x, int y) => y * Width + x;

        /// <summary>把两层 CPU 缓冲上传到 GPU 贴图。</summary>
        public void Upload()
        {
            for (int i = 0; i < Light.Length; i++)
                _lightUpload[i] = new Color32((byte)(Mathf.Min(Light[i], 1f) * 255f), 0, 0, 255);

            ColorTex.SetPixels32(Color);
            ColorTex.Apply(false);
            LightTex.SetPixels32(_lightUpload);
            LightTex.Apply(false);
        }

        /// <summary>手动 alpha 混合一个像素（src over dst）。</summary>
        public static void Blend(Color32[] buffer, int index, Color32 src)
        {
            Color32 dst = buffer[index];
            int a = src.a;
            int ia = 255 - a;
            buffer[index] = new Color32(
                (byte)((src.r * a + dst.r * ia) / 255),
                (byte)((src.g * a + dst.g * ia) / 255),
                (byte)((src.b * a + dst.b * ia) / 255),
                (byte)Mathf.Max(dst.a, a));
        }

        public void Dispose()
        {
            if (ColorTex != null) { UnityEngine.Object.Destroy(ColorTex); ColorTex = null; }
            if (LightTex != null) { UnityEngine.Object.Destroy(LightTex); LightTex = null; }
        }
    }
}
