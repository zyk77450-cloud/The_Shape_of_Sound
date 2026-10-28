using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 可砸碎的方块：角色从足够高的地方落下来踩到它 → 碎掉 → 过一会儿自己长回来。
///
/// 判定用的是「落差」（PlayerController2D.FallDistance），不是下落速度。
/// 原因：终端速度只有 16 格/秒，掉 0.6 格就到达上限了，
/// 从 1 格掉和从 50 格掉，撞上去的速度一模一样，没法区分。
///
/// v12：支持连锁——一块碎了会带动附近的同类砖块依次碎开，像多米诺。
/// 铺一排只需要把砖块并排放好，不用连任何线。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BreakableBlock2D : MonoBehaviour
{
    [Header("砸碎条件")]
    [Tooltip("需要多大的落差才能砸碎（单位：格）。\n调大 = 更难碎，要跳更高。\n调小 = 更容易碎。\n参考：角色平地满跳最高约 4.46 格。所以填 5 = 平地跳不碎、从 1 格高台跳下来才碎。")]
    public float minFallDistance = 5f;

    [Header("复原")]
    [Tooltip("碎掉后多久自己长回来（秒）。\n填 0 = 永久消失，直到重开游戏。")]
    public float respawnDelay = 3f;

    [Header("连锁（多米诺）")]
    [Tooltip("勾上 = 这块碎的时候，会带动附近的同类砖块一起碎。\n不勾 = 每块砖各碎各的。")]
    public bool chainReaction = true;

    [Tooltip("连锁波及范围（格）。\n1.3 = 能波及上下左右紧挨着的那 1~2 块，但不会波及斜角。\n调大 = 一次碎一大片。")]
    public float chainRadius = 1.3f;

    [Tooltip("每一跳的延迟（秒）。\n0.06 ≈ 每秒传 16 块，看起来像一波推过去。\n填 0 = 整排同一帧全碎。")]
    public float chainDelay = 0.06f;

    [Header("碎屑")]
    [Tooltip("碎的时候喷几个小方块。填 0 = 不喷碎屑，砖块直接消失。")]
    public int debrisCount = 6;

    [Tooltip("碎屑往外飞的速度（格/秒）。调大 = 炸得更凶，调小 = 更含蓄。")]
    public float debrisSpeed = 6f;

    [Tooltip("碎屑存活时间（秒）。到时间自动销毁。")]
    public float debrisLife = 0.5f;

    [Tooltip("碎屑的颜色。默认是土黄，想换成石头灰就改这里。")]
    public Color debrisColor = new Color(0.72f, 0.56f, 0.38f, 1f);

    [Header("可选事件")]
    [Tooltip("砸碎的那一刻额外触发的事，可以拖音效、震屏、弹提示。不填也没关系。")]
    public UnityEvent onBreak;

    SpriteRenderer sr;
    Collider2D col;
    bool broken;          // 已经碎了（防止重复触发 / 连锁死循环）
    bool pending;         // 已经被连锁"点名"，正在等延迟

    void Awake()
    {
        sr  = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
    }

    void OnCollisionEnter2D(Collision2D c) { TryBreak(c); }

    // ---------------- 被玩家砸 ----------------

    void TryBreak(Collision2D c)
    {
        if (broken || pending) return;

        // ① 撞上来的必须是玩家
        if (!c.collider.CompareTag("Player")) return;

        // ② 必须是从上面砸下来：角色脚底要高于砖块中心
        if (c.collider.bounds.min.y < transform.position.y) return;

        // ③ 必须真的在往下掉（站着不动时 relativeVelocity 接近 0）
        if (c.relativeVelocity.y > -0.5f) return;

        // ④ 落差够不够
        var pc = c.collider.GetComponent<PlayerController2D>();
        if (pc == null) return;
        if (pc.FallDistance < minFallDistance) return;

        Break();
    }

    // ---------------- 连锁 ----------------

    /// <summary>被旁边的砖块"点名"了：等一小会儿再碎，形成一波推过去的效果。</summary>
    public void BreakByChain(float delay)
    {
        if (broken || pending) return;
        pending = true;

        if (delay <= 0f) { pending = false; Break(); }
        else             Invoke(nameof(ChainFire), delay);
    }

    void ChainFire()
    {
        pending = false;
        Break();
    }

    // ---------------- 碎 / 长回来 ----------------

    void Break()
    {
        if (broken) return;
        broken = true;

        // 先找邻居（此时自己的碰撞体还没关，邻居的也都在）
        BreakableBlock2D[] neighbors = chainReaction ? FindNeighbors() : null;

        SpawnDebris();

        // 碎掉 = 关掉画面和碰撞体（不 Destroy，因为要能长回来）
        if (sr  != null) sr.enabled  = false;
        if (col != null) col.enabled = false;

        onBreak.Invoke();

        if (respawnDelay > 0f) Invoke(nameof(Respawn), respawnDelay);

        // 最后再通知邻居（自己已经关掉了，不会互相重复触发）
        if (neighbors != null)
            for (int i = 0; i < neighbors.Length; i++)
                neighbors[i].BreakByChain(chainDelay);
    }

    /// <summary>找出范围内还没碎的同类砖块。</summary>
    BreakableBlock2D[] FindNeighbors()
    {
        var cols = Physics2D.OverlapCircleAll(transform.position, chainRadius);
        var list = new System.Collections.Generic.List<BreakableBlock2D>(cols.Length);

        for (int i = 0; i < cols.Length; i++)
        {
            var b = cols[i].GetComponent<BreakableBlock2D>();
            if (b != null && b != this && !b.broken && !b.pending)
                list.Add(b);
        }
        return list.ToArray();
    }

    void Respawn()
    {
        broken  = false;
        pending = false;
        if (sr  != null) sr.enabled  = true;
        if (col != null) col.enabled = true;
    }

    // ---------------- 碎屑 ----------------

    /// <summary>碎屑：喷几个小方块出去，靠重力自然落下，到时间自动销毁。</summary>
    void SpawnDebris()
    {
        if (debrisCount <= 0 || sr == null || sr.sprite == null) return;

        for (int i = 0; i < debrisCount; i++)
        {
            var go = new GameObject("Debris");
            go.transform.position = transform.position + new Vector3(
                Random.Range(-0.4f, 0.4f), Random.Range(-0.4f, 0.4f), 0f);
            go.transform.localScale = Vector3.one * Random.Range(0.15f, 0.28f);

            var r = go.AddComponent<SpriteRenderer>();
            r.sprite       = sr.sprite;
            r.color        = debrisColor;
            r.sortingOrder = sr.sortingOrder + 1;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 6f;
            rb.velocity = new Vector2(
                Random.Range(-1f, 1f) * debrisSpeed,
                Random.Range(0.4f, 1f) * debrisSpeed);

            Destroy(go, debrisLife);
        }
    }
}
