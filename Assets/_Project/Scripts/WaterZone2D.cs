using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 水域标记：挂在勾了 Is Trigger 的 Collider2D 上。
/// 玩家（PlayerController2D）入水自动切换游泳模式。
/// onPlayerEnter/Exit 可接水花音效。
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class WaterZone2D : MonoBehaviour
{
    public UnityEvent onPlayerEnter;
    public UnityEvent onPlayerExit;

    void Reset() { GetComponent<Collider2D>().isTrigger = true; }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player")) onPlayerEnter.Invoke();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player")) onPlayerExit.Invoke();
    }
}