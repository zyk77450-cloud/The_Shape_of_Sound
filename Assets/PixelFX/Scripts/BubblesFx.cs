using System.Collections.Generic;
using UnityEngine;

namespace PixelFx
{
    /// <summary>
    /// pixelfx /fx/water/bubbles 的 Unity 移植版：
    /// 从一个点持续冒出像素气泡，上浮 + 左右摇摆，随寿命膨胀，最后淡出破裂。
    ///
    /// 用法与 JS 版一一对应：
    ///   var fx = new BubblesFx(settings);  // ≈ createFx(bubbles, { x: 160, y: 40 })
    ///   fx.Update(dt);                     // ≈ fx.update(dt)
    ///   fx.Draw(frame);                    // ≈ fx.draw(frame)（同时写颜色层和光照层）
    /// </summary>
    public sealed class BubblesFx
    {
        [System.Serializable]
        public class Settings
        {
            [Header("发射")]
            [Tooltip("发射点（帧像素坐标，左下角为原点）")]
            public Vector2 origin = new Vector2(160f, 40f);
            [Tooltip("发射点随机散布半径（像素）")]
            public float originJitter = 12f;
            [Tooltip("每秒生成气泡数")]
            public float spawnPerSecond = 28f;

            [Header("运动")]
            [Tooltip("上浮速度下限（像素/秒）")]
            public float riseSpeedMin = 22f;
            [Tooltip("上浮速度上限（像素/秒）")]
            public float riseSpeedMax = 46f;
            [Tooltip("左右摆动幅度（像素）")]
            public float wobbleAmplitude = 6f;
            [Tooltip("左右摆动频率（Hz）")]
            public float wobbleFrequency = 2.2f;

            [Header("外观")]
            [Tooltip("气泡初始半径下限（像素）")]
            public float radiusMin = 1.0f;
            [Tooltip("气泡初始半径上限（像素）")]
            public float radiusMax = 2.6f;
            [Tooltip("寿命下限（秒）")]
            public float lifeMin = 2.5f;
            [Tooltip("寿命上限（秒）")]
            public float lifeMax = 5.5f;

            [Header("颜色")]
            public Color32 body    = new Color32( 70, 150, 190, 110); // 半透明内芯
            public Color32 outline = new Color32(185, 235, 255, 215); // 描边
            public Color32 shine   = new Color32(255, 255, 255, 255); // 左上高光点

            [Header("光照")]
            [Tooltip("自发光强度（写入光照层，0~1）")]
            public float glow = 0.5f;
            [Tooltip("光照衰减半径（像素）")]
            public float glowRadius = 5f;
        }

        struct Bubble
        {
            public float x, baseX, y;
            public float radius, riseSpeed, phase;
            public float age, life, fade;
        }

        readonly Settings _s;
        readonly List<Bubble> _alive = new List<Bubble>(256);
        float _spawnAccumulator;
        float _time;

        /// <summary>存活气泡数。</summary>
        public int Count => _alive.Count;

        /// <summary>超过这个高度（帧像素 y）直接破裂；默认不限制。</summary>
        public float KillHeight { get; set; } = float.MaxValue;

        public BubblesFx(Settings settings) => _s = settings;

        /// <summary>一次性在某个位置喷出一团气泡（null = 默认发射点）。可用来做"爆泡"交互。</summary>
        public void Burst(int count, Vector2? at = null)
        {
            for (int i = 0; i < count; i++) Spawn(at);
        }

        void Spawn(Vector2? at)
        {
            Vector2 o = at ?? _s.origin;
            float ang = Random.value * Mathf.PI * 2f;
            float r = Random.value * _s.originJitter;

            _alive.Add(new Bubble
            {
                baseX = o.x + Mathf.Cos(ang) * r,
                x = o.x,
                y = o.y + Mathf.Sin(ang) * r * 0.5f, // 散布成扁椭圆，更像水下气源
                radius = Mathf.Lerp(_s.radiusMin, _s.radiusMax, Random.value),
                riseSpeed = Mathf.Lerp(_s.riseSpeedMin, _s.riseSpeedMax, Random.value),
                phase = Random.value * Mathf.PI * 2f,
                age = 0f,
                life = Mathf.Lerp(_s.lifeMin, _s.lifeMax, Random.value),
                fade = 0f,
            });
        }

        /// <summary>等价于 fx.update(dt)：只推进模拟，不画图。</summary>
        public void Update(float dt)
        {
            _time += dt;

            // 1) 按速率持续生成
            _spawnAccumulator += _s.spawnPerSecond * dt;
            while (_spawnAccumulator >= 1f)
            {
                _spawnAccumulator -= 1f;
                Spawn(null);
            }

            // 2) 积分 + 摆动 + 淡入淡出
            float omega = _s.wobbleFrequency * Mathf.PI * 2f;
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                Bubble b = _alive[i];
                b.age += dt;

                bool dead = b.age >= b.life || b.y > KillHeight;
                if (dead)
                {
                    _alive[i] = _alive[_alive.Count - 1]; // swap-back 删除，O(1)
                    _alive.RemoveAt(_alive.Count - 1);
                    continue;
                }

                b.y += b.riseSpeed * dt;
                b.x = b.baseX + Mathf.Sin(_time * omega + b.phase) * _s.wobbleAmplitude;

                b.fade = Mathf.Min(
                    Mathf.Clamp01(b.age / 0.15f),               // 出生淡入
                    Mathf.Clamp01((b.life - b.age) / 0.35f));   // 破裂前淡出

                _alive[i] = b;
            }
        }

        /// <summary>等价于 fx.draw(frame)：气泡画进颜色层，辉光加进光照层。</summary>
        public void Draw(PixelFrame frame)
        {
            for (int i = 0; i < _alive.Count; i++)
            {
                Bubble b = _alive[i];
                float grow = 0.85f + 0.45f * (b.age / b.life); // 随寿命缓慢膨胀
                float r = b.radius * grow;
                DrawBubble(frame, b.x, b.y, r, b.fade);
                AddGlow(frame, b.x, b.y, r, b.fade);
            }
        }

        // 画一个像素气泡：描边圆环 + 半透明内芯 + 左上高光点
        void DrawBubble(PixelFrame frame, float cx, float cy, float r, float fade)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r - 1f));
            int x1 = Mathf.Min(frame.Width  - 1, Mathf.CeilToInt(cx + r + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r - 1f));
            int y1 = Mathf.Min(frame.Height - 1, Mathf.CeilToInt(cy + r + 1f));
            if (x1 < x0 || y1 < y0) return;

            Color32 body = _s.body;
            Color32 outline = _s.outline;
            body.a = (byte)(body.a * fade);
            outline.a = (byte)(outline.a * fade);

            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - cx;
                    float dy = py + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    int idx = py * frame.Width + px;

                    if (d >= r - 0.9f && d <= r + 0.4f)   PixelFrame.Blend(frame.Color, idx, outline);
                    else if (d < r - 0.9f)                PixelFrame.Blend(frame.Color, idx, body);
                }
            }

            // 左上高光点
            int hx = Mathf.RoundToInt(cx - r * 0.4f);
            int hy = Mathf.RoundToInt(cy + r * 0.4f);
            if (frame.Contains(hx, hy))
            {
                Color32 shine = _s.shine;
                shine.a = (byte)(shine.a * fade);
                PixelFrame.Blend(frame.Color, frame.Index(hx, hy), shine);
            }
        }

        // 气泡向光照层加一团辉光（随距离线性衰减）
        void AddGlow(PixelFrame frame, float cx, float cy, float r, float fade)
        {
            float strength = _s.glow * fade;
            if (strength <= 0f) return;

            float gr = Mathf.Max(_s.glowRadius, r + 1f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - gr));
            int x1 = Mathf.Min(frame.Width  - 1, Mathf.CeilToInt(cx + gr));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - gr));
            int y1 = Mathf.Min(frame.Height - 1, Mathf.CeilToInt(cy + gr));

            for (int py = y0; py <= y1; py++)
            {
                for (int px = x0; px <= x1; px++)
                {
                    float dx = px + 0.5f - cx;
                    float dy = py + 0.5f - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d >= gr) continue;

                    int idx = py * frame.Width + px;
                    frame.Light[idx] = Mathf.Min(1f, frame.Light[idx] + strength * (1f - d / gr));
                }
            }
        }
    }
}
