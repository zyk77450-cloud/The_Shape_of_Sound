using System.Collections;
using UnityEngine;

/// <summary>
/// 回声显形平台：被波扫到 → 精灵+碰撞显形 → 一段时间后隐没。
/// revealDuration = 0 则永久显形（做"永久点亮"的谜题用）。
/// 用法：Square 精灵（Layer=Ground）+ BoxCollider2D（非 Trigger）+ 本脚本，
/// Sprites/Colliders 数组拖它自己的组件，存成 P_GhostPlatform 预制体。
/// </summary>
public class RevealOnWave2D : WaveReceivable2D
{
    [Header("目标（通常拖自己的 SpriteRenderer 和 Collider2D）")]
    public SpriteRenderer[] sprites;
    public Collider2D[] colliders;

    [Header("显形规则")]
    public float ghostAlpha = 0.12f;     // 隐形残影透明度（0=完全看不见）
    public float revealDuration = 3f;    // 显形持续时间；0=永久
    public float fadeTime = 0.25f;       // 淡入淡出时长

    bool revealing;

    void Awake()
    {
        SetAlpha(ghostAlpha);
        SetColliders(false);
    }

    public override void OnWaveHit(SoundWave2D wave)
    {
        base.OnWaveHit(wave);
        if (revealing) return;
        StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        revealing = true;
        yield return Fade(ghostAlpha, 1f);
        SetColliders(true);                      // 现形才能踩

        if (revealDuration > 0f)
        {
            float t = revealDuration;
            while (t > 0f) { t -= Time.deltaTime; yield return null; }
            SetColliders(false);
            yield return Fade(1f, ghostAlpha);
        }
        revealing = false;
    }

    IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / fadeTime;
            SetAlpha(Mathf.Lerp(from, to, t));
            yield return null;
        }
    }

    void SetAlpha(float a)
    {
        foreach (var s in sprites)
            if (s != null) { var c = s.color; c.a = a; s.color = c; }
    }

    void SetColliders(bool on)
    {
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }
}
