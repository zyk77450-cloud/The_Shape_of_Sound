using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 极简暂停：timeScale 冻结 + 暂停面板。
/// 挂在 TouchCanvas 根上；暂停按钮 On Click → Toggle；
/// 面板里"继续"→ Resume，"回到标题"→ BackToMenu。
/// </summary>
public class SimplePause2D : MonoBehaviour
{
    public CanvasGroup panel;   // 暂停面板（CanvasGroup，编辑器里 Alpha 设 0）

    bool paused;

    public void Toggle()
    {
        paused = !paused;
        Time.timeScale = paused ? 0f : 1f;
        panel.alpha = paused ? 1f : 0f;
        panel.interactable = paused;
        panel.blocksRaycasts = paused;
    }

    public void Resume() { if (paused) Toggle(); }

    public void BackToMenu()
    {
        Time.timeScale = 1f;    // 回标题前必须解冻，不然整个菜单也卡死
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
