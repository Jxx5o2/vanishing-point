using UnityEngine;

/// <summary>
/// 배경 스프라이트를 화면 크기에 맞춰 자동으로 늘린다.
///
/// 중요한 점 하나 — 이 배경은 <b>두 위치에서 보인다.</b>
///   ① 자기 평면에 서 있을 때 (가까움)
///   ② 한 겹 앞 평면에서 구멍 너머로 볼 때 (멂)
/// 원근 때문에 ②가 더 넓은 범위를 요구하므로, <b>먼 쪽 기준으로</b> 크기를
/// 잡아야 가장자리에 빈 공간이 생기지 않는다.
///
/// 벽의 구멍은 이 컴포넌트가 아니라 <b>그림의 투명 영역</b>으로 만든다.
/// 판을 작게 줄여서 뚫으면, 그 구멍 뒤에 있어야 할 안쪽 배경까지 같이
/// 줄어들어서 정작 구멍으로는 아무것도 안 보인다.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class BackgroundFitter : MonoBehaviour
{
    [Tooltip("어느 평면의 배경인지. 비워두면 부모에서 자동으로 찾는다")]
    [SerializeField] DepthPlane plane;

    [Tooltip("화면 세로의 몇 %를 덮을지. 구멍은 그림의 투명 영역으로 내므로 보통 1")]
    [SerializeField, Range(0.1f, 1f)] float heightFill = 1f;

    [Tooltip("여유 배율. 1.03이면 화면보다 3% 크게 잡아 가장자리 틈을 막는다")]
    [SerializeField, Range(1f, 1.5f)] float overfill = 1.03f;

    [Tooltip("위아래 미세 조정")]
    [SerializeField] float verticalNudge = 0f;

    DepthPlane frontPlane;      // 이 배경을 멀리서 보게 될 한 겹 앞 평면

    void OnEnable()   { if (Guard()) return; CacheFrontPlane(); Fit(); }
    void OnValidate() { if (Guard()) return; CacheFrontPlane(); Fit(); }
    void LateUpdate() { if (!enabled) return; Fit(); }

    /// <summary>
    /// 지형이나 콜라이더가 있는 오브젝트에 잘못 붙이면 그것까지 늘려버린다.
    /// 컴포넌트를 떼도 바뀐 Scale 은 남기 때문에 원인을 찾기가 아주 어렵다.
    /// </summary>
    bool Guard()
    {
        if (GetComponent<UnityEngine.Tilemaps.Tilemap>() != null || GetComponent<Collider2D>() != null)
        {
            Debug.LogError(
                $"[BackgroundFitter] '{name}' 은 지형/콜라이더 오브젝트입니다. " +
                "배경 스프라이트에만 붙이세요. 이 컴포넌트를 제거하고 " +
                "Transform 의 Scale 을 1,1,1 로 되돌려 주세요.", this);
            enabled = false;
            return true;
        }
        return false;
    }

    void CacheFrontPlane()
    {
        frontPlane = null;
        if (plane == null) plane = GetComponentInParent<DepthPlane>();
        if (plane == null) return;

        foreach (var p in FindObjectsByType<DepthPlane>(FindObjectsSortMode.None))
            if (p.DepthIndex == plane.DepthIndex - 1) { frontPlane = p; break; }
    }

    void Fit()
    {
        if (plane == null) plane = GetComponentInParent<DepthPlane>();
        if (plane == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = plane.CameraPosition;

        // 이 배경을 보게 될 카메라 중 가장 먼 것을 기준으로 크기를 잡는다.
        // 앞 평면에서 볼 때가 더 멀고, 더 넓은 범위를 요구한다.
        float viewerZ = camPos.z;
        if (frontPlane != null) viewerZ = Mathf.Min(viewerZ, frontPlane.CameraPosition.z);

        float distance = transform.position.z - viewerZ;
        if (distance <= 0.01f) return;

        float screenH = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float screenW = screenH * (cam.aspect > 0f ? cam.aspect : 16f / 9f);

        float wallH = screenH * heightFill * overfill;
        float wallW = screenW * overfill;

        // 화면 아래쪽에 붙인다. heightFill 이 1이면 결과적으로 화면 중앙에 온다.
        float screenBottom = camPos.y - screenH * 0.5f;
        float centerY = screenBottom + wallH * 0.5f + verticalNudge;

        // 스프라이트가 몇 유닛짜리인지 실제로 물어본다.
        // 1x1 이라고 가정하면 PPU 나 비율이 다른 그림에서 어긋난다.
        Vector2 unit = Vector2.one;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            Vector3 b = sr.sprite.bounds.size;
            if (b.x > 0.0001f) unit.x = b.x;
            if (b.y > 0.0001f) unit.y = b.y;
        }

        transform.localScale = new Vector3(wallW / unit.x, wallH / unit.y, 1f);
        transform.position   = new Vector3(camPos.x, centerY, transform.position.z);
    }
}
