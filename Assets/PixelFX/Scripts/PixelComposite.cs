using UnityEngine;

namespace PixelFx
{
    /// <summary>
    /// 等价于 JS 版 composite(frame, imageData.data, palette)：
    /// 颜色 × (环境光 + 光照层) → 可选色阶量化 → 写入目标像素数组。
    /// 这是 CPU 路线；GPU 路线见 Shaders/PixelComposite.shader。
    /// </summary>
    public static class PixelComposite
    {
        /// <param name="frame">帧缓冲</param>
        /// <param name="dst">输出数组（长度 = width*height），可直接 Texture2D.SetPixels32</param>
        /// <param name="ambient">环境光（0~1）</param>
        /// <param name="colorLevels">色阶量化：0=关闭；8/16/32 会得到复古限色感（≈ 传调色板）</param>
        public static void ToPixels(PixelFrame frame, Color32[] dst, float ambient = 0.35f, int colorLevels = 0)
        {
            Color32[] color = frame.Color;
            float[] light = frame.Light;
            int n = color.Length;

            if (colorLevels >= 2)
            {
                float step = 255f / (colorLevels - 1);
                for (int i = 0; i < n; i++)
                {
                    Color32 c = color[i];
                    float l = Mathf.Min(1f, ambient + light[i]);
                    dst[i] = new Color32(
                        Quantize(c.r, l, step),
                        Quantize(c.g, l, step),
                        Quantize(c.b, l, step),
                        c.a);
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    Color32 c = color[i];
                    float l = Mathf.Min(1f, ambient + light[i]);
                    dst[i] = new Color32((byte)(c.r * l), (byte)(c.g * l), (byte)(c.b * l), c.a);
                }
            }
        }

        static byte Quantize(byte channel, float light, float step)
        {
            float v = channel * light;
            v = Mathf.Round(v / step) * step;
            return (byte)Mathf.Clamp(v, 0f, 255f);
        }
    }
}
