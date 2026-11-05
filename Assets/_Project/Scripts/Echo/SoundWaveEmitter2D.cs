using UnityEngine;
using UnityEngine.EventSystems;

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

    [Header("解锁（v52）")]
    [Tooltip("勾上 = 必须先在玩家身上解锁 Can Echo 才发得出波。\n不勾 = 无视解锁，随时能发（调试用）。")]
    public bool requireUnlock = true;

    float cd;
    AudioSource au;
    PlayerController2D pc;

    void Awake()
    {
        au = GetComponent<AudioSource>();
        pc = GetComponent<PlayerController2D>();
    }

    void Update()
    {
        cd -= Time.deltaTime;
        bool uiBlock = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool pressed = Input.GetKeyDown(waveKey)
                    || (alsoMouseLeft && Input.GetMouseButtonDown(0) && !uiBlock)   // 点在 UI 上不发波
                    || TouchInput.wavePressed;
        TouchInput.wavePressed = false;

        if (pressed && cd <= 0f)
        {
            // v52: 没解锁回声就什么都不做（也不吃冷却，解锁后立刻能按）
            if (requireUnlock && pc != null && !pc.canEcho) return;

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
