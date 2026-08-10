using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 다른 깊이 평면으로 통하는 입구. 트리거 콜라이더 위에 올린다.
///
/// 플레이어가 안에 서서 ↑ 또는 W 를 누르면 넘어간다.
/// (점프는 Space 전용이라 서로 겹치지 않는다)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DepthGate : MonoBehaviour
{
    [Header("목적지")]
    [Tooltip("넘어갈 평면의 Depth Index")]
    [SerializeField] int targetDepth = 1;

    [Tooltip("도착할 지점. 목적지 평면의 자식으로 두어야 Z가 맞는다")]
    [SerializeField] Transform exitPoint;

    [Header("동작")]
    [Tooltip("끄면 닿기만 해도 바로 넘어간다")]
    [SerializeField] bool requireInput = true;

    bool playerInside;

    void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMotor2D>() != null) playerInside = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMotor2D>() != null) playerInside = false;
    }

    void Update()
    {
        if (!playerInside) return;

        if (requireInput)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.wKey.wasPressedThisFrame && !kb.upArrowKey.wasPressedThisFrame) return;
        }

        var director = DepthDirector.Instance;
        if (director == null || !director.CanTravelTo(targetDepth)) return;

        playerInside = false;   // 전환 중 중복 발동 방지
        director.Travel(targetDepth, exitPoint);
    }

    // 게이트와 도착 지점을 씬 뷰에서 선으로 이어 보여준다.
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);

        if (exitPoint == null) return;
        Gizmos.DrawLine(transform.position, exitPoint.position);
        Gizmos.DrawWireSphere(exitPoint.position, 0.3f);
    }
}
