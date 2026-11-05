using UnityEngine;

/// <summary>
/// 视差背景层 v2：纯相机跟随。
/// 每帧移动 = 相机位移 × 系数——菜单运镜、玩法、死亡回滑、转场全都连续，
/// 相机瞬移时按比例跟着平移（发生在黑幕后，屏幕上的相对位置不变）。
/// 挂在背景层根物体上；把层摆在菜单相机看到的构图处即可（开局相机停在哪，构图就以哪为准）。
/// </summary>
[DefaultExecutionOrder(40)]   // 在相机跟随之后取相机位置
public class ParallaxLayer2D : MonoBehaviour
{
    [Header("视差系数（0=锁死 1=全速跟相机）")]
    [Tooltip("远景 0.1~0.2，中景 0.3~0.5，近景 0.6~0.8")]
    public float factorX = 0.3f;
    [Tooltip("横版一般比 X 小；完全不许上下视差就 0")]
    public float factorY = 0.1f;

    [Header("无缝循环（贴图能平铺就开）")]
    public bool wrapX = false;
    public float wrapWidth = 20f;     // 一块贴图的世界宽度
    public bool wrapY = false;
    public float wrapHeight = 22.5f;  // 一块贴图的世界高度（360px÷16）

    Transform camT;
    Vector3 lastCam;
    bool seeded;

    void Start()
    {
        camT = Camera.main.transform;
    }

    void LateUpdate()
    {
        // 首帧只记相机位置不位移（吸收开局相机归位到菜单的瞬移）
        if (!seeded)
        {
            seeded = true;
            lastCam = camT.position;
            return;
        }

        Vector3 delta = camT.position - lastCam;
        lastCam = camT.position;

        Vector3 pos = transform.position + new Vector3(delta.x * factorX, delta.y * factorY, 0f);

        // 无缝循环：整层挪到离相机最近的一个周期
        if (wrapX)
        {
            while (pos.x - camT.position.x > wrapWidth * 0.5f) pos.x -= wrapWidth;
            while (camT.position.x - pos.x > wrapWidth * 0.5f) pos.x += wrapWidth;
        }
        if (wrapY)
        {
            while (pos.y - camT.position.y > wrapHeight * 0.5f) pos.y -= wrapHeight;
            while (camT.position.y - pos.y > wrapHeight * 0.5f) pos.y += wrapHeight;
        }

        transform.position = pos;
    }
}
