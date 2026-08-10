using UnityEngine;

/// <summary>
/// 깊이 평면 하나. 이 오브젝트의 자식으로 그 평면의 지형과 배경을 둔다.
///
/// 평면의 Z는 이 오브젝트의 Transform Z 값이 그대로 쓰인다.
/// 안쪽으로 갈수록 Z가 커진다. (0번 = 0, 1번 = 6, 2번 = 12 ...)
///
/// 플랫폼과 배경을 따로 물리는 이유:
/// 앞 평면에서 뒤를 볼 때 배경만 보이고 플랫폼은 안 보여야 하기 때문이다.
/// 플랫폼이 보이면 레벨 구조가 통째로 노출돼서, 게이트 앞에서 답을
/// 미리 읽게 된다. 배경만 보이면 분위기는 전해지되 구조는 감춰진다.
/// </summary>
[DisallowMultipleComponent]
public class DepthPlane : MonoBehaviour
{
    [Header("정체")]
    [Tooltip("0이 가장 앞. 숫자가 클수록 안쪽")]
    [SerializeField] int depthIndex = 0;

    [Tooltip("이 평면의 지형이 속한 Unity 레이어 이름")]
    [SerializeField] string layerName = "Depth0";

    [Header("구성")]
    [Tooltip("타일맵 등 이 평면의 지형. 현재 평면일 때만 보인다")]
    [SerializeField] GameObject platformsRoot;

    [Tooltip("배경 이미지. 한 겹 앞 평면에서도 보인다 (흐릿하게)")]
    [SerializeField] GameObject backgroundRoot;

    [Header("카메라 구도")]
    [Tooltip("이 방을 비출 때 화면 중심이 될 지점 (월드 좌표 X, Y)")]
    [SerializeField] Vector2 roomCenter = Vector2.zero;

    [Tooltip("카메라가 이 평면에서 뒤로 떨어질 거리. 클수록 넓게 보인다")]
    [SerializeField] float cameraDistance = 20f;

    Renderer[] platformRenderers;
    Renderer[] backgroundRenderers;

    public int DepthIndex => depthIndex;

    /// <summary>이 평면의 월드 Z.</summary>
    public float PlaneZ => transform.position.z;

    /// <summary>지형 레이어 번호. 이름이 잘못됐으면 -1.</summary>
    public int UnityLayer => LayerMask.NameToLayer(layerName);

    /// <summary>이 방을 비출 때 카메라가 서야 할 자리.</summary>
    public Vector3 CameraPosition =>
        new Vector3(roomCenter.x, roomCenter.y, PlaneZ - cameraDistance);

    /// <summary>세로로 보이는 유닛 수. 방 크기를 정할 때 참고한다.</summary>
    public float VisibleHeight(Camera cam) =>
        cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * cameraDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

    void Awake()
    {
        // 비활성 상태인 것도 포함해서 캐싱해 둔다.
        platformRenderers  = platformsRoot  != null ? platformsRoot.GetComponentsInChildren<Renderer>(true)  : new Renderer[0];
        backgroundRenderers = backgroundRoot != null ? backgroundRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
    }

    /// <summary>
    /// 지형을 보이거나 감춘다. 콜라이더는 건드리지 않는다 —
    /// 충돌 분리는 레이어가 이미 하고 있으므로 렌더러만 끄면 된다.
    /// </summary>
    public void SetPlatformsVisible(bool visible)
    {
        if (platformRenderers == null) return;
        foreach (var r in platformRenderers) if (r != null) r.enabled = visible;
    }

    public void SetBackgroundVisible(bool visible)
    {
        if (backgroundRenderers == null) return;
        foreach (var r in backgroundRenderers) if (r != null) r.enabled = visible;
    }

    void OnValidate()
    {
        // 레이어 이름을 잘못 적으면 조용히 충돌이 안 걸려서 원인 찾기가 어렵다.
        if (!string.IsNullOrEmpty(layerName) && LayerMask.NameToLayer(layerName) < 0)
            Debug.LogWarning($"[DepthPlane] '{layerName}' 레이어가 없습니다. " +
                             "Project Settings > Tags and Layers 에서 만들어 주세요.", this);
    }

    // 씬 뷰에 이 방의 화면 범위를 그려서, 방이 한 화면에 들어오는지 눈으로 본다.
    void OnDrawGizmos()
    {
        var cam = Camera.main;
        if (cam == null) return;

        float h = VisibleHeight(cam);
        float w = h * (cam.aspect > 0f ? cam.aspect : 16f / 9f);
        var center = new Vector3(roomCenter.x, roomCenter.y, PlaneZ);

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(center, new Vector3(w, h, 0.05f));

        // 화면 중심에 십자 표시 — Room Center 를 맞출 때 기준이 된다.
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.45f);
        Gizmos.DrawLine(center + Vector3.left * 0.6f,  center + Vector3.right * 0.6f);
        Gizmos.DrawLine(center + Vector3.down * 0.6f,  center + Vector3.up * 0.6f);

#if UNITY_EDITOR
        // 박스 왼쪽 위에 실제 크기를 적어 둔다. 이게 있어야 눈대중 대신
        // 숫자를 보면서 Camera Distance 를 맞출 수 있다.
        var style = new GUIStyle
        {
            normal = { textColor = new Color(1f, 0.85f, 0.2f) },
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        UnityEditor.Handles.Label(
            center + new Vector3(-w * 0.5f, h * 0.5f + 0.5f, 0f),
            $"깊이 {depthIndex}   화면 {w:0.0} × {h:0.0}   (Camera Distance {cameraDistance:0.#})",
            style);
#endif
    }
}
