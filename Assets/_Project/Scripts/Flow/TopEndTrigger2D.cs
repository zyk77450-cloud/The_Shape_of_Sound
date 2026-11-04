using UnityEngine;

/// <summary>
/// 登顶终点：玩家碰到 → 结束流程（GameFlowManager.FinishGame）。
/// 放在场景2最顶端的必经之处，BoxCollider2D 勾 Is Trigger 盖住通道。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TopEndTrigger2D : MonoBehaviour
{
    void Reset() { GetComponent<Collider2D>().isTrigger = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (GameFlowManager.Instance != null)
            GameFlowManager.Instance.FinishGame();
    }
}
