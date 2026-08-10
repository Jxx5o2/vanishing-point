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
        ApplyVisibility(currentDepth, currentDepth);
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

        // 전환 내내 출발 평면과 도착 평면을 둘 다 보여준다.
        // 중간에 갑자기 나타나거나 사라지면 "이동했다"가 아니라 "장면이 바뀌었다"로 읽힌다.
        ApplyVisibility(currentDepth, targetDepth);

        Vector3 fromPos = player.position;
        Vector3 toPos   = exitPoint.position;
        Vector3 fromCam = cam.transform.position;
        Vector3 toCam   = PlaneOf(targetDepth).CameraPosition;

        bool layerSwapped = false;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            float e = ease.Evaluate(Mathf.Clamp01(t));

            player.position         = Vector3.Lerp(fromPos, toPos, e);
            cam.transform.position  = Vector3.Lerp(fromCam, toCam, e);

            // 절반쯤 왔을 때 소속 평면을 바꾼다. 넘어가는 그 순간이다.
            if (!layerSwapped && e >= 0.5f)
            {
                ApplyDepth(targetDepth);
                layerSwapped = true;
            }
            yield return null;
        }

        player.position        = toPos;
        cam.transform.position = toCam;
        if (!layerSwapped) ApplyDepth(targetDepth);
        ApplyVisibility(targetDepth, targetDepth);

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

    /// <summary>
    /// 무엇을 보여줄지 정한다.
    ///
    ///   플랫폼 — 서 있는 평면만. 다음 평면의 지형이 보이면 레벨 구조가
    ///            노출돼서, 게이트 앞에서 답을 미리 읽게 된다.
    ///   배경   — 현재 평면과 그 한 겹 안쪽까지. 안쪽 배경이 앞 평면 배경의
    ///            뚫린 부분 너머로 보이면서 "저 안에 뭔가 있다"가 전달된다.
    ///
    /// a 와 b 는 전환 중일 때 출발/도착 깊이다. 전환 중이 아니면 둘이 같다.
    /// </summary>
    void ApplyVisibility(int a, int b)
    {
        if (planes == null) return;
        foreach (var p in planes)
        {
            if (p == null) continue;
            int d = p.DepthIndex;

            bool platforms  = d == a || d == b;
            bool background = d == a || d == a + 1 || d == b || d == b + 1;

            p.SetPlatformsVisible(platforms);
            p.SetBackgroundVisible(background);
        }
    }

    DepthPlane PlaneOf(int depth)
    {
        if (planes == null) return null;
        foreach (var p in planes)
            if (p != null && p.DepthIndex == depth) return p;
        return null;
    }
}
