using UnityEngine;

/// <summary>
/// 按 Q / 鼠标左键 / 触屏"波"键发出回声。挂在 Player 上。
/// 可选挂 Audio Source 播发波音效（Play On Awake 取消）。
/// </summary>
public class SoundWaveEmitter2D : MonoBehaviour
{
    [Header("回声")]
    public SoundWave2D wavePrefab;       // 拖 P_SoundWave 预制体
    public KeyCode waveKey = KeyCode.Q;
    public bool alsoMouseLeft = true;
    public float cooldown = 0.6f;        // 两次回声最小间隔
    public float originYOffset = -1.2f;  // 波心相对身体中心的偏移（脚边）

    [Header("音效（可留空）")]
    public AudioClip waveClip;

    float cd;
    AudioSource au;

    void Awake() { au = GetComponent<AudioSource>(); }

    void Update()
    {
        cd -= Time.deltaTime;
        bool pressed = Input.GetKeyDown(waveKey)
                    || (alsoMouseLeft && Input.GetMouseButtonDown(0))
                    || TouchInput.wavePressed;
        TouchInput.wavePressed = false;

        if (pressed && cd <= 0f)
        {
            cd = cooldown;
            if (wavePrefab == null)
            {
                Debug.LogWarning("SoundWaveEmitter2D：Wave Prefab 没拖");
                return;
            }
            Vector2 o = (Vector2)transform.position + Vector2.up * originYOffset;
            Instantiate(wavePrefab, o, Quaternion.identity).Init(o);
            if (au != null && waveClip != null) au.PlayOneShot(waveClip);
        }
    }
}
