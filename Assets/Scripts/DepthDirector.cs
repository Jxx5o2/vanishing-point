using System.Collections;
using UnityEngine;

/// <summary>
/// 깊이 전환의 유일한 주인. 씬에 하나만 둔다.
///
/// 카메라를 조종하는 주체를 하나로 묶어두는 게 이 클래스의 핵심이다.
/// 평소에는 현재 방의 구도로 카메라를 고정해 두고, 전환할 때만 움직인다.
/// 그래서 "카메라가 움직인다 = 깊이가 바뀐다"가 성립한다.
///
/// 방은 깊이 숫자가 아니라 오브젝트 자체로 구분한다. 갈림길에서는 같은
/// 깊이에 방이 여러 개 있기 때문이다.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DepthDirector : MonoBehaviour
{
    public static DepthDirector Instance { get; private set; }

    [Header("연결")]
    [Tooltip("씬에 있는 모든 평면. 표시를 켜고 끌 대상이라 빠짐없이 넣어야 한다")]
    [SerializeField] DepthPlane[] planes;

    [Tooltip("게임을 시작할 방")]
    [SerializeField] DepthPlane startPlane;

    [SerializeField] Transform player;
    [SerializeField] Rigidbody2D playerBody;
    [SerializeField] PlayerMotor2D playerMotor;
    [SerializeField] Camera cam;

    [Header("전환 연출")]
    [Tooltip("한 겹 넘어가는 데 걸리는 시간. 0.3보다 짧으면 컷처럼, 1보다 길면 지루하게 느껴진다")]
    [SerializeField, Range(0.2f, 1.2f)] float duration = 0.6f;

    [Tooltip("전환 가감속")]
    [SerializeField] AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("카메라가 플레이어보다 늦게 따라가는 정도. 0이면 같이 움직여서 " +
             "플레이어 크기가 전혀 안 변한다. 값을 올리면 플레이어가 잠깐 " +
             "멀어졌다가 카메라가 따라잡는다 — 빨려 들어가는 느낌이 강해진다")]
    [SerializeField, Range(0f, 3f)] float cameraLag = 1f;

    [Tooltip("앞뒤 방이 서로 넘어가는 데 걸리는 비율. 1이면 전환 내내 서서히 바뀐다")]
    [SerializeField, Range(0.3f, 1f)] float crossfade = 1f;

    DepthPlane currentPlane;
    bool transitioning;

    public DepthPlane CurrentPlane => currentPlane;
    public bool IsTransitioning => transitioning;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (cam == null) cam = Camera.main;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (startPlane == null)
        {
            Debug.LogError("[DepthDirector] Start Plane 이 비어 있습니다.", this);
            enabled = false;
            return;
        }
        currentPlane = startPlane;
        ApplyCollision(currentPlane);
        SettleVisibility(currentPlane);
        cam.transform.position = currentPlane.CameraPosition;   // 첫 프레임부터 제자리에
    }

    void LateUpdate()
    {
        // 전환 중이 아니면 카메라는 현재 방의 구도에 고정된다.
        if (transitioning || currentPlane == null) return;
        cam.transform.position = currentPlane.CameraPosition;
    }

    /// <summary>지금 이 방으로 넘어갈 수 있는지. 게이트가 물어본다.</summary>
    public bool CanTravelTo(DepthPlane target)
        => !transitioning && target != null && target != currentPlane;

    /// <summary>게이트가 호출한다. landingPoint 는 도착 방 위에 놓인 착지 지점.</summary>
    public void Travel(DepthPlane target, Transform landingPoint)
    {
        if (!CanTravelTo(target)) return;
        if (landingPoint == null)
        {
            Debug.LogError("[DepthDirector] 게이트에 Landing Point 가 비어 있습니다.", this);
            return;
        }
        StartCoroutine(TravelRoutine(target, landingPoint));
    }

    IEnumerator TravelRoutine(DepthPlane target, Transform landingPoint)
    {
        transitioning = true;

        // 전환 중에는 물리를 멈춘다. 안 그러면 이동하는 동안 중력에 끌려 내려간다.
        playerMotor.enabled = false;
        playerBody.simulated = false;

        DepthPlane from = currentPlane;

        Vector3 fromPos = player.position;
        Vector3 toPos   = landingPoint.position;
        Vector3 fromCam = cam.transform.position;
        Vector3 toCam   = target.CameraPosition;

        bool swapped = false;
        float t = 0f;

        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / duration);

            float e = ease.Evaluate(t);

            // 카메라는 일부러 늦게 출발했다가 따라잡는다.
            // 그 사이 카메라와 플레이어의 거리가 벌어져서 플레이어가 작아진다.
            float camT = ease.Evaluate(Mathf.Pow(t, 1f + cameraLag));

            player.position        = Vector3.Lerp(fromPos, toPos, e);
            cam.transform.position = Vector3.Lerp(fromCam, toCam, camT);

            // 떠나는 방은 서서히 사라지고 도착하는 방은 서서히 나타난다.
            BlendVisibility(from, target, Mathf.Clamp01(t / crossfade));

            // 소속 방(충돌)은 절반쯤 왔을 때 바꾼다. 넘어가는 그 순간이다.
            if (!swapped && t >= 0.5f)
            {
                currentPlane = target;
                ApplyCollision(target);
                ApplySharpness(target);
                swapped = true;
            }
            yield return null;
        }

        player.position        = toPos;
        cam.transform.position = toCam;
        if (!swapped)
        {
            currentPlane = target;
            ApplyCollision(target);
        }
        SettleVisibility(target);

        playerBody.simulated = true;
        playerBody.linearVelocity = Vector2.zero;   // 도착 직후 관성으로 튀지 않게
        playerMotor.enabled = true;

        // 착지 지점이 게이트 트리거 안일 수 있다 (들어온 문으로 다시 나오는 경우).
        // 걸어 나가기 전까지는 어떤 게이트도 발동하지 않게 잠근다.
        DepthGate.DisarmAll();

        transitioning = false;
    }

    /// <summary>
    /// 소속 방을 바꾼다. 다른 방의 지형과는 충돌하지 않게 만들고,
    /// 접지 판정이 볼 레이어도 현재 방으로 갈아 끼운다.
    /// </summary>
    void ApplyCollision(DepthPlane plane)
    {
        if (plane == null) return;

        int allDepthLayers = 0;
        foreach (var p in planes)
        {
            if (p == null) continue;
            int layer = p.UnityLayer;
            if (layer >= 0) allDepthLayers |= 1 << layer;
        }

        int selfLayer = plane.UnityLayer >= 0 ? 1 << plane.UnityLayer : 0;

        // 내 방을 뺀 나머지 깊이 레이어는 전부 무시한다.
        // 이렇게 하면 Physics 2D 충돌 매트릭스를 손으로 칠할 필요가 없다.
        playerBody.excludeLayers = allDepthLayers & ~selfLayer;
        playerMotor.SetGroundLayers(selfLayer);
    }

    // --- 무엇을 얼마나 보여줄 것인가 -------------------------------------
    //
    //   지형 — 서 있는 방만. 다른 방의 지형이 보이면 레벨 구조가 노출돼서,
    //          게이트 앞에서 답을 미리 읽게 된다.
    //   배경 — 현재 방과, 그 방이 지정한 배경 방 하나. 갈림길에서 목적지마다
    //          다른 배경을 보여주면 그게 곧 정답 표시가 되므로 하나만 쓴다.

    static float PlatformAlphaAt(DepthPlane p, DepthPlane current)
        => p == current ? 1f : 0f;

    static float BackgroundAlphaAt(DepthPlane p, DepthPlane current)
        => (current != null && (p == current || p == current.Backdrop)) ? 1f : 0f;

    /// <summary>전환이 끝났거나 시작 전. 목표 상태로 확정한다.</summary>
    void SettleVisibility(DepthPlane current)
    {
        if (planes == null) return;
        foreach (var p in planes)
        {
            if (p == null) continue;
            p.SetPlatformsAlpha(PlatformAlphaAt(p, current));
            p.SetBackgroundAlpha(BackgroundAlphaAt(p, current));
        }
        ApplySharpness(current);
    }

    /// <summary>전환 중. 두 상태 사이를 섞는다.</summary>
    void BlendVisibility(DepthPlane from, DepthPlane to, float e)
    {
        if (planes == null) return;
        foreach (var p in planes)
        {
            if (p == null) continue;
            p.SetPlatformsAlpha (Mathf.Lerp(PlatformAlphaAt (p, from), PlatformAlphaAt (p, to), e));
            p.SetBackgroundAlpha(Mathf.Lerp(BackgroundAlphaAt(p, from), BackgroundAlphaAt(p, to), e));
        }
    }

    /// <summary>서 있는 방의 배경만 선명하게, 나머지는 흐린 그림으로.</summary>
    void ApplySharpness(DepthPlane current)
    {
        if (planes == null) return;
        foreach (var p in planes)
            if (p != null) p.SetBackgroundSharp(p == current);
    }
}
