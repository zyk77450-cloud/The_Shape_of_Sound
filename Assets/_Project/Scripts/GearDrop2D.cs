using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 让物体"从上方掉到原地"的程序化下落（不走物理，所以不会被 Trigger 穿透）。
/// 用法：把物体摆在"应该落地的位置"，加上这个组件，Play 时它会从上方掉下来。
/// 适合齿轮、落石这类"从天而降"的陷阱。
/// </summary>
public class GearDrop2D : MonoBehaviour
{
    [Header("触发")]
    [Tooltip("勾上 = 一 Play 就自动掉。不勾 = 等你手动调用 Drop()。")]
    public bool startOnPlay = true;

    [Tooltip("开始下落前等几秒。0 = 立刻掉。")]
    public float delay = 0f;

    [Header("下落")]
    [Tooltip("从落点上方多少格开始掉。8 格大约 0.6 秒落地。")]
    public float dropHeight = 8f;

    [Tooltip("下落加速度。越大掉得越猛，40 是比较有分量的写法。")]
    public float gravity = 40f;

    [Tooltip("最快下落速度上限。防止掉太快穿模，30 足够。")]
    public float maxSpeed = 30f;

    [Header("落地")]
    [Tooltip("勾上 = 落地瞬间震屏。")]
    public bool shakeOnLand = true;

    [Tooltip("震屏持续时长（秒）。0.15 是「顿一下」的感觉。")]
    public float shakeDuration = 0.15f;

    [Tooltip("震屏幅度（格）。0.12 明显但不晕。")]
    public float shakeStrength = 0.12f;

    [Tooltip("落地时触发的事件，可以拖音效（砸地声）。")]
    public UnityEvent onLanded;

    Vector3 landPos;        // 落点（= 物体摆放的位置）
    bool dropping;

    void Start()
    {
        landPos = transform.position;
        if (startOnPlay) Drop(delay);
    }

    /// <summary>立刻开始下落。</summary>
    public void Drop()
    {
        Drop(0f);
    }

    /// <summary>延迟 d 秒后开始下落。</summary>
    public void Drop(float d)
    {
        if (dropping) return;
        dropping = true;
        StartCoroutine(Run(d));
    }

    IEnumerator Run(float d)
    {
        // 先瞬移到落点正上方，再往下掉
        landPos = transform.position;
        transform.position = new Vector3(landPos.x, landPos.y + dropHeight, landPos.z);

        if (d > 0f) yield return new WaitForSeconds(d);

        float v = 0f;
        float y = transform.position.y;

        while (y > landPos.y)
        {
            v = Mathf.Min(maxSpeed, v + gravity * Time.deltaTime);
            y -= v * Time.deltaTime;
            if (y <= landPos.y) y = landPos.y;

            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, y, p.z);
            yield return null;
        }

        if (shakeOnLand) CameraShake2D.Shake(shakeDuration, shakeStrength);
        if (onLanded != null) onLanded.Invoke();

        dropping = false;
    }
}
