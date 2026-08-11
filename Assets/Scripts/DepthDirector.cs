using System.Collections;
using UnityEngine;

/// <summary>
/// 깊이 전환의 유일한 주인. 씬에 하나만 둔다.
///
/// 카메라를 조종하는 주체를 하나로 묶어두는 게 이 클래스의 핵심이다.
/// 평소에는 현재 평면의 구도로 카메라를 고정해 두고, 전환할 때만 움직인다.
/// 그래서 "카메라가 움직인다 = 깊이가 바뀐다"가 성립한다.
/// </summary>
[DefaultExecutionOrder(-50)]
public class DepthDirector : MonoBehaviour
{
    public static DepthDirector Instance { get; private set; }

    [Header("연결")]
    [Tooltip("깊이 순서대로 넣는다. 0번 칸이 가장 앞 평면")]
    [SerializeField] DepthPlane[] planes;
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

    [Tooltip("앞뒤 평면이 서로 넘어가는 데 걸리는 비율. 1이면 전환 내내 서서히 바뀐다")]
    [SerializeField, Range(0.3f, 1f)] float crossfade = 1f;

    int currentDepth;
    bool transitioning;

    public int CurrentDepth => currentDepth;
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
        var start = PlaneOf(currentDepth);
        if (start == null)
        {
            Debug.LogError("[DepthDirector] 시작 평면을 찾지 못했습니다. Planes 배열을 확인하세요.", this);
            enabled = false;
            return;
        }
        ApplyDepth(currentDepth);
        SettleVisibility(currentDepth);
        cam.transform.position = start.CameraPosition;   // 첫 프레임부터 제자리에
    }

    void LateUpdate()
    {
        // 전환 중이 아니면 카메라는 현재 방의 구도에 고정된다.
        if (transitioning) return;
        var plane = PlaneOf(currentDepth);
        if (plane != null) cam.transform.position = plane.CameraPosition;
    }

    /// <summary>지금 이 깊이로 넘어갈 수 있는지. 게이트가 물어본다.</summary>
    public bool CanTravelTo(int targetDepth)
        => !transitioning && targetDepth != currentDepth && PlaneOf(targetDepth) != null;

    /// <summary>게이트가 호출한다. exitPoint 는 도착 평면 위에 놓인 지점.</summary>
    public void Travel(int targetDepth, Transform exitPoint)
    {
        if (!CanTravelTo(targetDepth)) return;
        if (exitPoint == null)
        {
            Debug.LogError("[DepthDirector] 게이트에 Exit Point 가 비어 있습니다.", this);
            return;
        }
        StartCoroutine(TravelRoutine(targetDepth, exitPoint));
    }

    IEnumerator TravelRoutine(int targetDepth, Transform exitPoint)
    {
        transitioning = true;

        // 전환 중에는 물리를 멈춘다. 안 그러면 이동하는 동안 중력에 끌려 내려간다.
        playerMotor.enabled = false;
        playerBody.simulated = false;

        int fromDepth = currentDepth;

        Vector3 fromPos = player.position;
        Vector3 toPos   = exitPoint.position;
        Vector3 fromCam = cam.transform.position;
        Vector3 toCam   = PlaneOf(targetDepth).CameraPosition;

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

            // 앞 평면은 서서히 사라지고 뒤 평면은 서서히 나타난다.
            BlendVisibility(fromDepth, targetDepth, Mathf.Clamp01(t / crossfade));

            // 소속 평면(충돌)은 절반쯤 왔을 때 바꾼다. 넘어가는 그 순간이다.
            if (!swapped && t >= 0.5f)
            {
                ApplyDepth(targetDepth);
                ApplySharpness(targetDepth);
                swapped = true;
            }
            yield return null;
        }

        player.position        = toPos;
        cam.transform.position = toCam;
        if (!swapped) { ApplyDepth(targetDepth); ApplySharpness(targetDepth); }
        SettleVisibility(targetDepth);

        playerBody.simulated = true;
        playerBody.linearVelocity = Vector2.zero;   // 도착 직후 관성으로 튀지 않게
        playerMotor.enabled = true;

        transitioning = false;
    }

    /// <summary>
    /// 소속 평면을 바꾼다. 다른 평면의 지형과는 충돌하지 않게 만들고,
    /// 접지 판정이 볼 레이어도 현재 평면으로 갈아 끼운다.
    /// </summary>
    void ApplyDepth(int depth)
    {
        currentDepth = depth;
        var plane = PlaneOf(depth);
        if (plane == null) return;

        int allDepthLayers = 0;
        foreach (var p in planes)
        {
            if (p == null) continue;
            int layer = p.UnityLayer;
            if (layer >= 0) allDepthLayers |= 1 << layer;
        }

        int selfLayer = plane.UnityLayer >= 0 ? 1 << plane.UnityLayer : 0;

        // 내 평면을 뺀 나머지 깊이 레이어는 전부 무시한다.
        // 이렇게 하면 Physics 2D 충돌 매트릭스를 손으로 칠할 필요가 없다.
        playerBody.excludeLayers = allDepthLayers & ~selfLayer;
        playerMotor.SetGroundLayers(selfLayer);
    }

    // --- 무엇을 얼마나 보여줄 것인가 -------------------------------------
    //
    //   지형 — 서 있는 평면만. 다음 평면의 지형이 보이면 레벨 구조가
    //          노출돼서, 게이트 앞에서 답을 미리 읽게 된다.
    //   배경 — 현재 평면과 그 한 겹 안쪽까지. 안쪽 배경이 앞 평면 배경의
    //          뚫린 부분 너머로 보이면서 "저 안에 뭔가 있다"가 전달된다.

    static float PlatformAlphaAt(DepthPlane p, int depth)
        => p.DepthIndex == depth ? 1f : 0f;

    static float BackgroundAlphaAt(DepthPlane p, int depth)
        => (p.DepthIndex == depth || p.DepthIndex == depth + 1) ? 1f : 0f;

    /// <summary>전환이 끝났거나 시작 전. 목표 상태로 확정한다.</summary>
    void SettleVisibility(int depth)
    {
        if (planes == null) return;
        foreach (var p in planes)
        {
            if (p == null) continue;
            p.SetPlatformsAlpha(PlatformAlphaAt(p, depth));
            p.SetBackgroundAlpha(BackgroundAlphaAt(p, depth));
        }
        ApplySharpness(depth);
    }

    /// <summary>전환 중. 두 상태 사이를 섞는다.</summary>
    void BlendVisibility(int from, int to, float e)
    {
        if (planes == null) return;
        foreach (var p in planes)
        {
            if (p == null) continue;
            p.SetPlatformsAlpha (Mathf.Lerp(PlatformAlphaAt (p, from), PlatformAlphaAt (p, to), e));
            p.SetBackgroundAlpha(Mathf.Lerp(BackgroundAlphaAt(p, from), BackgroundAlphaAt(p, to), e));
        }
    }

    /// <summary>서 있는 평면의 배경만 선명하게, 나머지는 흐린 그림으로.</summary>
    void ApplySharpness(int depth)
    {
        if (planes == null) return;
        foreach (var p in planes)
            if (p != null) p.SetBackgroundSharp(p.DepthIndex == depth);
    }

    DepthPlane PlaneOf(int depth)
    {
        if (planes == null) return null;
        foreach (var p in planes)
            if (p != null && p.DepthIndex == depth) return p;
        return null;
    }
}
