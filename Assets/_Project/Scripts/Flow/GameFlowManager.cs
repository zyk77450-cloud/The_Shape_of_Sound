using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 游戏流程总控：菜单 →（10秒运镜 或 读档直达）→ 场景1
/// →（四乐器按序集齐）→ 转场1 → 场景2 →（登顶）→ 结束菜单（保留存档）。
/// 全程单场景：相机运镜 + 玩家传送。
/// 挂在空物体 GameFlow 上，Inspector 把各引用拖齐。
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    [Header("引用")]
    public PlayerController2D player;             // 拖 Player
    public CameraFollow2D followCam;              // 拖 Main Camera 上的 CameraFollow2D
    public Transform spawn1;                      // 场景1出生点（空物体）
    public Transform spawn2;                      // 场景2出生点（空物体）
    public Transform menuCamPos;                  // 菜单相机停点（世界最高处，空物体）

    [Header("运镜")]
    public float introDuration = 10f;             // 无存档的开场下推秒数

    [Header("UI")]
    public CanvasGroup menuCanvas;                // 开始菜单（Canvas 或其父物体上的 CanvasGroup）
    public CanvasGroup endCanvas;                 // 结束菜单（默认隐藏）
    public Image fadeImage;                       // 全屏黑幕（铺满、初始 Alpha 0、Raycast Target ☐）
    public Button continueButton;                 // "继续"按钮（没到过场景2时自动隐藏）

    [Header("开局自带")]
    [Tooltip("一出生背包里就有的乐器 Id（新档/读档/清档重开都会补发）；场景里同 Id 的道具自动隐藏")]
    public int[] startingInstruments = { 0 };
    [Tooltip("补发开局乐器后触发。要连带解锁能力就在这里接 Player 的 UnlockXxx")]
    public UnityEvent onStartingGranted;

    [Header("结束菜单选项")]
    public bool clearSaveOnFinish = false;        // 通关后是否清档（策划定：保留存档 = 不勾）

    Camera cam;
    bool running;

    void Awake()
    {
        Instance = this;
        GrantStartingInstruments();        // 越早越好：道具的 Start 要靠它隐藏同 Id 拾取物
        cam = Camera.main;
        endCanvas.alpha = 0f;
        endCanvas.interactable = false;
        endCanvas.blocksRaycasts = false;
    }

    // 补发开局乐器（已有了就不重复发）
    void GrantStartingInstruments()
    {
        bool changed = false;
        for (int i = 0; i < startingInstruments.Length; i++)
        {
            int id = startingInstruments[i];
            if (id >= 0 && !SaveSystem.HasInstrument(id))
            {
                SaveSystem.SetInstrument(id, true);
                changed = true;
            }
        }
        if (changed) onStartingGranted.Invoke();
    }

    void Start()
    {
        // 开局停在菜单：玩家锁输入摆在场景1出生点，相机停菜单高点
        player.enabled = false;
        player.transform.position = spawn1.position;
        cam.transform.position = new Vector3(menuCamPos.position.x, menuCamPos.position.y, -10f);
        followCam.enabled = false;

        menuCanvas.alpha = 1f;
        menuCanvas.interactable = true;
        menuCanvas.blocksRaycasts = true;

        // 没到过场景2 = 没有可继续的进度，隐藏"继续"
        if (continueButton != null)
            continueButton.gameObject.SetActive(SaveSystem.Stage >= 1);
    }

    // ================= 按钮调用 =================

    /// <summary>新的旅程：清档 + 运镜开场（按钮 On Click 拖这个）</summary>
    public void NewGameWithClear()
    {
        SaveSystem.ClearAll();
        GrantStartingInstruments();        // 清档重开也要有开局乐器
        StartCoroutine(BeginRoutine(false));
    }

    /// <summary>继续：有存档直接进场景2出生点</summary>
    public void ContinueGame()
    {
        StartCoroutine(BeginRoutine(true));
    }

    /// <summary>回标题：重载当前场景（所有状态归位，存档不动）</summary>
    public void RestartToMenu()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
    /// <summary>退出游戏（标题菜单"退出"按钮）</summary>
    public void QuitGame()
    {
        Application.Quit();
    }

    IEnumerator BeginRoutine(bool fromSave)
    {
        menuCanvas.interactable = false;
        menuCanvas.blocksRaycasts = false;

        if (fromSave && SaveSystem.Stage >= 1)
        {
            // 有存档：黑幕一闪，直接站场景2出生点
            yield return Fade(0f, 1f, 0.4f);
            player.transform.position = spawn2.position;
            cam.transform.position = new Vector3(spawn2.position.x, spawn2.position.y, -10f);
            yield return Fade(1f, 0f, 0.4f);
        }
        else
        {
            // 无存档：10 秒开场运镜（菜单相机 → 场景1玩家）
            Vector3 from = cam.transform.position;
            Vector3 to = new Vector3(spawn1.position.x, spawn1.position.y, -10f);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / introDuration;
                // EaseInOut：慢-快-慢，有"下降"的分量感
                float k = 1f - Mathf.Pow(1f - t, 3f);   // 三次 EaseOut：出发利落，落到玩家身上稳稳减速
                cam.transform.position = Vector3.Lerp(from, to, k);
                menuCanvas.alpha = Mathf.Clamp01(2f - t * 2f);   // 运镜前半段菜单淡出
                yield return null;
            }
        }

        menuCanvas.alpha = 0f;
        player.enabled = true;
        followCam.enabled = true;
    }

    // ============ 场景1 → 场景2（转场1）：InstrumentSequence.onAllCollected 拖这里 ============
    public void GoToStage2()
    {
        if (running) return;
        StartCoroutine(Stage2Routine());
    }

    IEnumerator Stage2Routine()
    {
        running = true;
        player.enabled = false;                     // 转场锁输入
        yield return Fade(0f, 1f, 0.6f);            // 转场动画1（黑幕）

        player.transform.position = spawn2.position;
        if (player.TryGetComponent<Rigidbody2D>(out var rb)) rb.velocity = Vector2.zero;
        SaveSystem.Stage = 1;                       // 存档：下次"继续"直达场景2

        followCam.enabled = false;
        cam.transform.position = new Vector3(spawn2.position.x, spawn2.position.y, -10f);

        yield return Fade(1f, 0f, 0.6f);
        followCam.enabled = true;
        player.enabled = true;
        running = false;
    }

    // ============ 登顶 → 结束菜单：TopEndTrigger2D 调用 ============
    public void FinishGame()
    {
        if (running) return;
        StartCoroutine(FinishRoutine());
    }

    IEnumerator FinishRoutine()
    {
        running = true;
        player.enabled = false;
        yield return Fade(0f, 1f, 1.2f);            // 动画2：黑幕

        if (clearSaveOnFinish) SaveSystem.ClearAll();

        endCanvas.alpha = 1f;
        endCanvas.interactable = true;
        endCanvas.blocksRaycasts = true;
        yield return Fade(1f, 0f, 1.2f);            // 黑幕退去亮出结束菜单
        running = false;
    }

    // 全屏黑幕淡入淡出
    IEnumerator Fade(float from, float to, float dur)
    {
        if (fadeImage == null) yield break;
        Color c = fadeImage.color;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / dur;
            c.a = Mathf.Lerp(from, to, t);
            fadeImage.color = c;
            yield return null;
        }
    }
}
