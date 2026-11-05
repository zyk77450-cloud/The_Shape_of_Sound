using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包：6 格收集展示。挂在 BackpackPanel 物体上（自动成单例）。
/// 空格 = 图标 Image 没图；AddById 填进第一个空格并点亮。
/// 图标数组下标 = 物品 id（0=海螺 1=鼓……和 InstrumentPickup2D 的 Id 一致）。
/// 背包按钮 On Click → Toggle；乐器拾到自动入包（InstrumentPickup2D 里已挂钩）。
/// </summary>
public class Backpack2D : MonoBehaviour
{
    public static Backpack2D Instance { get; private set; }

    [Header("格子：拖 6 个格子的【图标 Image】（按显示顺序）")]
    public Image[] slots = new Image[6];

    [Header("图标表：下标 = 物品 id，把策划的图标按顺序拖进来")]
    public Sprite[] icons;

    [Header("面板")]
    public CanvasGroup panel;        // BackpackPanel 自己的 CanvasGroup
    public AudioClip pickupClip;     // 入包音效（可空）

    readonly HashSet<int> owned = new HashSet<int>();
    bool open;

    void Awake()
    {
        Instance = this;
        Close();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>入包：id 超范围或已拥有就跳过；填进第一个空格。</summary>
    public void AddById(int id)
    {
        if (id < 0 || id >= icons.Length || owned.Contains(id)) return;
        owned.Add(id);

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue;
            if (slots[i].sprite == null)
            {
                slots[i].sprite = icons[id];
                slots[i].color = Color.white;   // 空格透明，入包点亮
                break;
            }
        }
        if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, transform.position);
    }

    public bool Has(int id) => owned.Contains(id);

    // ---- 背包按钮 On Click 接这里 ----
    public void Toggle() { if (open) Close(); else Open(); }

    void Open()
    {
        open = true;
        panel.alpha = 1f;
        panel.interactable = true;
        panel.blocksRaycasts = true;
    }

    void Close()
    {
        open = false;
        panel.alpha = 0f;
        panel.interactable = false;
        panel.blocksRaycasts = false;
    }
}
