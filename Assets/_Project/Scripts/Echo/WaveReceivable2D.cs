using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 能被回声扫到的东西的基类：波前经过时 OnWaveHit 被调用。
/// 显形平台（RevealOnWave2D）、敌人（EnemyChaser2D）都继承它。
/// Inspector 的 On Wave Hit 事件可不写代码直接拖函数。
/// </summary>
public class WaveReceivable2D : MonoBehaviour
{
    public UnityEvent onWaveHit;

    public virtual void OnWaveHit(SoundWave2D wave)
    {
        onWaveHit?.Invoke();
    }
}
