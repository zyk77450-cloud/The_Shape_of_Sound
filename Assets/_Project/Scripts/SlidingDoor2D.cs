using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 平移门：玩家进入触发区后，门体（zhizi2）平滑平移到目标点（zhizi3）的位置，
/// 同时播放开门音效 + 屏幕颤抖。
/// 平移完成后：两个碰撞体一起关闭（玩家可以通过），并把门体压到目标点图层后面。
///
/// 图层规则：目标点（zhizi3）的绘制顺序 = 主角 - 1（刚好站在主角身后）。
///
/// 用法：挂在一个带 BoxCollider2D（勾 Is Trigger）的空物体上，放在门前。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SlidingDoor2D : MonoBehaviour
{
    [Header("门与目标")]
    [Tooltip("要平移的门体（例：zhizi2）。必须有碰撞体、会挡人的那个。")]
    public Transform door;

    [Tooltip("目标位置（例：zhizi3）。门体会平移到这个物体的位置。")]
    public Transform openTarget;

    [Header("开合")]
    [Tooltip("开门用时（秒）：门滑到目标点要多久。屏幕颤抖也用这个时长。\n0.5 像沉重的石门，0.2 像快速闸门。")]
    public float openTime = 0.5f;

    [Tooltip("只开一次，开完就不再响应玩家。建议保持勾选。")]
    public bool openOnce = true;

    [Header("碰撞体")]
    [Tooltip("门体的碰撞体。留空则自动从 Door 上取。")]
    public Collider2D doorCollider;

    [Tooltip("目标点的碰撞体。留空则自动从 Open Target 上取。")]
    public Collider2D targetCollider;

    [Tooltip("开门完成后，把两个碰撞体一起关掉，让玩家能通过。")]
    public bool disableCollidersOnOpen = true;

    [Header("图层")]
    [Tooltip("把目标点（zhizi3）的绘制顺序设为「主角 - 1」，让它刚好在主角身后。")]
    public bool setTargetBehindPlayer = true;

    [Tooltip("主角的 Sprite Renderer。留空则自动按 Tag = Player 查找。")]
    public SpriteRenderer playerRenderer;

    [Tooltip("平移完成后，把门体的绘制顺序再压低一层，让它被目标点遮住。")]
    public bool moveBehindTargetLayer = true;

    [Header("调试")]
    [Tooltip("勾上会在 Console 打印每一步（触发、关闭了几个碰撞体、门滑到哪）。排查问题时很有用。")]
    public bool logOpen = true;

    [Header("到位之后")]
    [Tooltip("到位时额外触发的事（接别的机关、再播一段音效都可以）。")]
    public UnityEvent onOpened;

    [Header("音效")]
    [Tooltip("开门音效。留空则只有画面效果、没有声音。")]
    public AudioClip openSound;

    [Tooltip("音效音量（0~1）。")]
    [Range(0f, 1f)] public float volume = 0.8f;

    [Header("屏幕颤抖")]
    [Tooltip("开门瞬间让镜头抖一下（需要场景里 Main Camera 挂着 CameraShake2D）。")]
    public bool shakeWithDoor = true;

    [Tooltip("颤抖幅度（格）。0.1 约等于 1.6 像素，0.3 已经比较夸张。")]
    public float shakeMagnitude = 0.12f;

    Collider2D triggerZone;
    AudioSource audioSource;
    bool opened;

    void Reset()
    {
        // 挂上去时自动勾成触发器，省得忘记
        GetComponent<Collider2D>().isTrigger = true;
    }

    void Awake()
    {
        triggerZone = GetComponent<Collider2D>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;   // 2D 音效，不随距离衰减
    }

    void Start()
    {
        ResolveReferences();
        ApplyTargetSorting();       // 一开场：zhizi3 = 主角 - 1
        PushBehindTargetLayer();    // 一开场：zhizi2 = zhizi3 - 1（始终在它后面）
    }

    void ResolveReferences()
    {
        if (door == null || openTarget == null) return;

        if (doorCollider == null) doorCollider = door.GetComponent<Collider2D>();
        if (targetCollider == null) targetCollider = openTarget.GetComponent<Collider2D>();

        if (playerRenderer == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) playerRenderer = p.GetComponent<SpriteRenderer>();
        }
    }

    void ApplyTargetSorting()
    {
        if (!setTargetBehindPlayer || openTarget == null || playerRenderer == null) return;

        var targetSr = openTarget.GetComponent<SpriteRenderer>();
        if (targetSr == null) return;

        targetSr.sortingLayerID = playerRenderer.sortingLayerID;
        targetSr.sortingOrder = playerRenderer.sortingOrder - 1;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (opened && openOnce) return;
        if (!other.CompareTag("Player")) return;
        if (door == null || openTarget == null) return;

        opened = true;
        if (logOpen) Debug.Log("[门] 玩家进入触发区，开始开门：" + name);

        // ① 音效、② 平移、③ 颤抖：同一帧一起发生
        if (openSound != null) audioSource.PlayOneShot(openSound, volume);
        if (shakeWithDoor) CameraShake2D.Shake(openTime, shakeMagnitude);   // 抖动时长 = 开门时长

        if (logOpen) Debug.Log("[门] 开门 " + openTime.ToString("0.00") + " 秒（抖动同步）");
        StartCoroutine(MoveDoorRoutine());

        if (openOnce && triggerZone != null) triggerZone.enabled = false;
    }

    IEnumerator MoveDoorRoutine()
    {
        Vector3 from = door.position;
        Vector3 to = openTarget.position;
        float t = 0f;
        float useTime = Mathf.Max(0.05f, openTime);

        while (t < useTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / useTime);
            // SmoothStep：起步和收尾都慢，中间快，像真有重量
            door.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }

        door.position = to;

        // 到位：把两端所有碰撞体都关掉（含子物体），玩家可以通过
        if (disableCollidersOnOpen) DisableBlockingColliders();

        // 到位再压一次（防止中途被别的脚本改过顺序）
        if (moveBehindTargetLayer) PushBehindTargetLayer();

        if (onOpened != null) onOpened.Invoke();
    }

    void DisableBlockingColliders()
    {
        int count = 0;
        count += DisableAllCollidersUnder(door);
        count += DisableAllCollidersUnder(openTarget);

        if (logOpen)
        {
            if (count == 0)
                Debug.LogWarning("[门] 没有关闭任何碰撞体：Door / Open Target 上找不到可关的 Collider2D。");
            else
                Debug.Log("[门] 已关闭 " + count + " 个碰撞体，通道开启。");
        }
    }

    int DisableAllCollidersUnder(Transform root)
    {
        if (root == null) return 0;
        int count = 0;
        var cols = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            var c = cols[i];
            if (c == null) continue;
            if (c == triggerZone) continue;      // 别把触发区自己关掉
            if (!c.enabled) continue;
            c.enabled = false;
            count++;
        }
        return count;
    }

    void PushBehindTargetLayer()
    {
        var doorSr = door.GetComponent<SpriteRenderer>();
        var targetSr = (openTarget != null) ? openTarget.GetComponent<SpriteRenderer>() : null;
        if (doorSr == null || targetSr == null) return;

        doorSr.sortingLayerID = targetSr.sortingLayerID;
        doorSr.sortingOrder = targetSr.sortingOrder - 1;   // 比目标点还低一层 = 被它盖住
    }

    void OnDrawGizmosSelected()
    {
        var zone = GetComponent<Collider2D>();
        if (zone != null)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.7f);
            var b = zone.bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }

        if (door == null || openTarget == null) return;
        Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
        Gizmos.DrawLine(door.position, openTarget.position);
        Gizmos.DrawWireCube(openTarget.position, new Vector3(0.5f, 0.5f, 0f));
    }
}
