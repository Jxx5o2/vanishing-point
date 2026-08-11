using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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

    [Header("배경 초점")]
    [Tooltip("이 평면에 서 있을 때 쓸 선명한 배경")]
    [SerializeField] Sprite backgroundSharp;

    [Tooltip("한 겹 앞에서 볼 때 쓸 흐린 배경. 비워두면 교체하지 않는다")]
    [SerializeField] Sprite backgroundBlurred;

    [Header("카메라 구도")]
    [Tooltip("이 방을 비출 때 화면 중심이 될 지점 (월드 좌표 X, Y)")]
    [SerializeField] Vector2 roomCenter = Vector2.zero;

    [Tooltip("카메라가 이 평면에서 뒤로 떨어질 거리. 클수록 넓게 보인다")]
    [SerializeField] float cameraDistance = 20f;

    /// <summary>
    /// 알파를 조절할 수 있는 대상 하나. 스프라이트는 SpriteRenderer 의 색으로,
    /// 타일맵은 Tilemap 컴포넌트의 색으로 알파가 조절된다 (렌더러가 아니다).
    /// 원래 색을 기억해 두고 알파만 곱하므로, 깊이별로 넣어둔 색조가 유지된다.
    /// </summary>
    class Fadeable
    {
        public Renderer renderer;
        public SpriteRenderer sprite;
        public Tilemap tilemap;
        public Color baseColor;

        public void SetAlpha(float a)
        {
            var c = baseColor;
            c.a = baseColor.a * a;
            if (sprite  != null) sprite.color  = c;
            if (tilemap != null) tilemap.color = c;
            if (renderer != null) renderer.enabled = a > 0.002f;   // 완전 투명이면 그리지 않는다
        }
    }

    List<Fadeable> platformFades = new List<Fadeable>();
    List<Fadeable> backgroundFades = new List<Fadeable>();
    SpriteRenderer backgroundSprite;

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
        Collect(platformsRoot,  platformFades);
        Collect(backgroundRoot, backgroundFades);
        if (backgroundRoot != null) backgroundSprite = backgroundRoot.GetComponentInChildren<SpriteRenderer>(true);
    }

    static void Collect(GameObject root, List<Fadeable> into)
    {
        into.Clear();
        if (root == null) return;

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var f = new Fadeable { renderer = r };
            f.sprite  = r as SpriteRenderer;
            f.tilemap = r.GetComponent<Tilemap>();

            if (f.sprite  != null) f.baseColor = f.sprite.color;
            else if (f.tilemap != null) f.baseColor = f.tilemap.color;
            else continue;   // 색을 못 만지는 렌더러는 건너뛴다

            into.Add(f);
        }
    }

    /// <summary>
    /// 배경을 선명한 것과 흐린 것 중 하나로 바꾼다.
    ///
    /// URP 2D Renderer 는 카메라 피사계 심도를 지원하지 않아서, 흐림은
    /// 미리 흐리게 만들어 둔 그림을 갈아 끼우는 방식으로 낸다. 흐린 정도를
    /// 그림에서 직접 정할 수 있다는 게 오히려 장점이다.
    /// </summary>
    public void SetBackgroundSharp(bool sharp)
    {
        if (backgroundSprite == null) return;
        var target = sharp ? backgroundSharp : backgroundBlurred;
        if (target != null && backgroundSprite.sprite != target)
            backgroundSprite.sprite = target;
    }

    /// <summary>
    /// 지형의 불투명도. 콜라이더는 건드리지 않는다 —
    /// 충돌 분리는 레이어가 이미 하고 있으므로 그림만 조절하면 된다.
    ///
    /// 켜고 끄는 대신 알파를 쓰는 이유: 한 프레임에 사라지면 그건 컷이고,
    /// 뇌가 "이동했다"가 아니라 "장면이 바뀌었다"로 읽는다.
    /// </summary>
    public void SetPlatformsAlpha(float a)
    {
        foreach (var f in platformFades) f.SetAlpha(a);
    }

    public void SetBackgroundAlpha(float a)
    {
        foreach (var f in backgroundFades) f.SetAlpha(a);
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
