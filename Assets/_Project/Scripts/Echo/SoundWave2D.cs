using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 扩散回声（v2：撞墙反弹）。
/// 波前扩散时越过 WaveReceivable2D 就通知；波前到达墙面撞点时，
/// 在撞点生成一道更小更暗的回波（反弹只做视觉，防机关被重复触发）。
/// 反弹波自动关闭自身反弹（防无限套娃）。
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class SoundWave2D : MonoBehaviour
{
    [Header("扩散参数")]
    public float speed = 14f;       // 扩散速度（格/秒）
    public float maxRadius = 18f;   // 最大半径
    public float fadeTime = 0.35f;  // 到达后的淡出秒数
    public int segments = 96;       // 圆环采样点数

    [Header("反弹（v2）")]
    [Tooltip("主波开；反弹波会自动关掉自己的反弹（防套娃）")]
    public bool reflectOnWalls = true;
    [Tooltip("什么层算墙。勾 Ground")]
    public LayerMask wallLayer;
    [Tooltip("圆周打多少条探测射线。越多反弹点越密（墙上开花越好看），24 够用")]
    public int reflectRays = 24;
    [Tooltip("离波心太近的墙不反弹——免得脚下的地面每波都冒泡")]
    public float minReflectDist = 0.8f;
    [Tooltip("反弹波的最大半径 = 波的剩余距离 × 此值")]
    [Range(0.1f, 1f)] public float reflectStrength = 0.5f;
    [Tooltip("反弹波是否也能触发机关/敌人。默认关：反弹先做纯视觉。注意打开后一个回声可能让水晶 +2（直射+反弹各一次）")]
    public bool reflectionsTriggerReceivers = false;

    [Header("亮度")]
    [Range(0.1f, 1f)] public float alphaScale = 1f;   // 反弹波自动调暗用

    /// <summary>反弹波按需关闭通知（默认 true = 主波通知机关）</summary>
    public bool notifyReceivers = true;

    static Material defaultLineMat;

    LineRenderer line;
    float radius, fadeTimer = -1f;
    Vector2 origin;
    List<WaveReceivable2D> pending = new List<WaveReceivable2D>();

    // 反弹探测：每个方向一次，记录撞点距离和位置
    struct WallHit { public float dist; public Vector2 point; }
    List<WallHit> wallHits;

    // 由 Emitter 在 Instantiate 之后立刻调用
    public void Init(Vector2 origin)
    {
        this.origin = origin;
        transform.position = origin;

        if (reflectOnWalls && wallLayer.value == 0)
            Debug.LogWarning("[回声] " + name + " 开了反弹但 Wall Layer 没勾任何层——反弹不会生效。在 P_SoundWave 上把 Wall Layer 勾上 Ground。");

        if (defaultLineMat == null)
            defaultLineMat = new Material(Shader.Find("Sprites/Default"));

        line = GetComponent<LineRenderer>();
        if (line.sharedMaterial == null) line.sharedMaterial = defaultLineMat;
        line.loop = true;
        line.positionCount = segments;
        line.startWidth = 0.12f * alphaScale;
        line.endWidth = 0.04f * alphaScale;
        line.sortingOrder = 8;

        if (notifyReceivers)
            pending.AddRange(FindObjectsOfType<WaveReceivable2D>());

        if (reflectOnWalls) PrecomputeWallHits();
        Draw();
    }

    // 开局一次性朝圆周打射线，记下所有会撞到的墙点
    void PrecomputeWallHits()
    {
        wallHits = new List<WallHit>(reflectRays);
        float step = 360f / reflectRays;
        for (int i = 0; i < reflectRays; i++)
        {
            Vector2 d = new Vector2(Mathf.Cos(i * step * Mathf.Deg2Rad),
                                    Mathf.Sin(i * step * Mathf.Deg2Rad));
            var h = Physics2D.Raycast(origin, d, maxRadius, wallLayer);
            if (h.collider != null && h.distance > minReflectDist)
                wallHits.Add(new WallHit { dist = h.distance, point = h.point });
        }
    }

    void Update()
    {
        if (fadeTimer < 0f)
        {
            radius += speed * Time.deltaTime;

            // 反弹：波前到达墙点 → 生成回波（每点只一次）
            if (wallHits != null)
                for (int i = wallHits.Count - 1; i >= 0; i--)
                    if (wallHits[i].dist <= radius)
                    {
                        SpawnReflection(wallHits[i].point, maxRadius - wallHits[i].dist);
                        wallHits.RemoveAt(i);
                    }

            // 通知接收者（波前"越过"谁就通知谁，一次性）
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
        Color c = new Color(0.31f, 0.85f, 1f, (1f - t) * alphaScale);
        line.startColor = c;
        line.endColor = c;
    }

    // 在墙撞点生成一道更小更暗的回波
    void SpawnReflection(Vector2 point, float remaining)
    {
        var go = new GameObject("EchoReflect");
        go.transform.position = point;
        go.AddComponent<LineRenderer>();
        var w = go.AddComponent<SoundWave2D>();
        w.speed = speed;
        w.maxRadius = Mathf.Max(2f, remaining * reflectStrength);
        w.fadeTime = fadeTime;
        w.segments = Mathf.Max(32, segments / 2);
        w.reflectOnWalls = false;                      // 反弹波不再反弹
        w.notifyReceivers = reflectionsTriggerReceivers;
        w.alphaScale = alphaScale * 0.65f;
        w.wallLayer = wallLayer;
        w.Init(point);
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
