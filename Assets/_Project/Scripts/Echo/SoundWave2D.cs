using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 扩散回声：半径增长，越过 WaveReceivable2D 就通知（等价于波前扫过），
/// 到最大半径后淡出销毁。材质自动兜底，零美术资产。
/// 由 SoundWaveEmitter2D 实例化（P_SoundWave 预制体），不要手动挂到别的物体。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class SoundWave2D : MonoBehaviour
{
    [Header("扩散参数")]
    public float speed = 14f;       // 扩散速度（格/秒）
    public float maxRadius = 18f;   // 最大半径
    public float fadeTime = 0.35f;  // 到达后的淡出秒数

    [Header("外观")]
    public int segments = 96;       // 圆环采样点数

    LineRenderer line;
    float radius, fadeTimer = -1f;
    Vector2 origin;
    List<WaveReceivable2D> pending = new List<WaveReceivable2D>();

    // 由 Emitter 在 Instantiate 之后立刻调用
    public void Init(Vector2 origin)
    {
        this.origin = origin;
        transform.position = origin;

        line = GetComponent<LineRenderer>();
        if (line.sharedMaterial == null)                       // 没拖材质也能画
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        line.loop = true;
        line.positionCount = segments;
        line.startWidth = 0.12f;
        line.endWidth = 0.04f;
        line.sortingOrder = 8;

        // 收集场景里所有"能被回声扫到"的东西，等波前越过
        pending.AddRange(FindObjectsOfType<WaveReceivable2D>());
        Draw();
    }

    void Update()
    {
        if (fadeTimer < 0f)
        {
            radius += speed * Time.deltaTime;

            // 波前"越过"谁就通知谁（一次性）
            for (int i = pending.Count - 1; i >= 0; i--)
                if (Vector2.Distance(pending[i].transform.position, origin) <= radius)
                {
                    pending[i].OnWaveHit(this);
                    pending.RemoveAt(i);
                }

            if (radius >= maxRadius) { radius = maxRadius; fadeTimer = fadeTime; }
            Draw();
        }
        else
        {
            fadeTimer -= Time.deltaTime;
            if (fadeTimer <= 0f) { Destroy(gameObject); return; }
        }

        float t = Mathf.Clamp01(radius / maxRadius);
        Color c = new Color(0.31f, 0.85f, 1f, 1f - t);   // 青色随扩散渐隐
        line.startColor = c;
        line.endColor = c;
    }

    void Draw()
    {
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }
}
