using UnityEngine;

/// <summary>
/// 背包面板：开 = 冻结时间 + 刷新格子，关 = 恢复。
/// 挂在 BagCanvas 根上；BagPanel 的 CanvasGroup 编辑器里初始 Alpha=0、两个勾取消。
/// BtnBag → Toggle，BtnClose → Close。
/// </summary>
public class BagPanel2D : MonoBehaviour
{
    public CanvasGroup panel;        // BagPanel 的 CanvasGroup
    public BagSlot2D[] slots;        // 6 个格子按顺序拖进来

    bool open;

    public void Toggle() { if (open) Close(); else Open(); }

    public void Open()
    {
        open = true;
        Time.timeScale = 0f;                    // 翻背包时不挨打
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] != null) slots[i].Refresh();
        panel.alpha = 1f;
        panel.interactable = true;
        panel.blocksRaycasts = true;
    }

    public void Close()
    {
        open = false;
        Time.timeScale = 1f;
        panel.alpha = 0f;
        panel.interactable = false;
        panel.blocksRaycasts = false;
    }

    void OnDestroy() { Time.timeScale = 1f; }   // 兜底：场景重载前解冻
}
