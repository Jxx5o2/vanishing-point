using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// 다른 평면으로 통하는 입구. 트리거 콜라이더 위에 올린다.
///
/// 두 가지 용도로 쓴다.
///   진입 — 플레이어가 서서 ↑ 또는 W 를 누르면 넘어간다 (Require Input 켬)
///   배출 — 닿기만 해도 끌려 나간다 (Require Input 끔). 오답 루트 끝에
///          놓으면 "밀려났다"가 된다. 방향만 다를 뿐 같은 장치다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class DepthGate : MonoBehaviour
{
    [Header("목적지")]
    [Tooltip("넘어갈 평면. 오브젝트를 직접 드래그한다")]
    [SerializeField] DepthPlane targetPlane;

    [Tooltip("도착해서 서게 될 지점. 목적지 방의 자식으로 두어야 Z가 맞는다. " +
             "다른 게이트의 트리거 안에 두면 도착하자마자 또 끌려간다")]
    [FormerlySerializedAs("exitPoint")]
    [SerializeField] Transform landingPoint;

    [Header("동작")]
    [Tooltip("끄면 닿기만 해도 바로 넘어간다. 배출구는 꺼두는 게 맞다")]
    [SerializeField] bool requireInput = true;

    static readonly List<DepthGate> AllGates = new List<DepthGate>();

    bool playerInside;

    /// <summary>
    /// 이 게이트가 지금 발동할 수 있는 상태인지.
    ///
    /// 들어온 문으로 다시 나오게 하면 착지 지점이 그 문의 트리거 안이 된다.
    /// 그대로 두면 도착하자마자 또 빨려 들어가서 무한 반복이 된다.
    /// 그래서 전환이 끝나면 모든 게이트를 잠그고, 플레이어가 트리거 밖으로
    /// 걸어 나가야 다시 열린다.
    /// </summary>
    bool armed = true;

    void Reset()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    void OnEnable()  { AllGates.Add(this); }
    void OnDisable() { AllGates.Remove(this); }

    /// <summary>전환 직후 디렉터가 부른다. 제자리에 떨어져도 재발동하지 않게 잠근다.</summary>
    public static void DisarmAll()
    {
        foreach (var g in AllGates) if (g != null) g.armed = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMotor2D>() != null) playerInside = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerMotor2D>() == null) return;
        playerInside = false;
        armed = true;          // 밖으로 걸어 나갔으니 다시 쓸 수 있다
    }

    void Update()
    {
        if (!playerInside || !armed) return;

        if (requireInput)
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (!kb.wKey.wasPressedThisFrame && !kb.upArrowKey.wasPressedThisFrame) return;
        }

        var director = DepthDirector.Instance;
        if (director == null || !director.CanTravelTo(targetPlane)) return;

        armed = false;          // 전환 중 중복 발동 방지
        director.Travel(targetPlane, landingPoint);
    }

    // 게이트와 도착 지점을 씬 뷰에서 선으로 이어 보여준다.
    // 진입은 하늘색, 배출은 주황색으로 구분한다.
    void OnDrawGizmos()
    {
        Gizmos.color = requireInput ? new Color(0.3f, 0.9f, 1f, 0.9f)
                                    : new Color(1f, 0.6f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);

        if (landingPoint == null) return;
        Gizmos.DrawLine(transform.position, landingPoint.position);
        Gizmos.DrawWireSphere(landingPoint.position, 0.3f);
    }
}
