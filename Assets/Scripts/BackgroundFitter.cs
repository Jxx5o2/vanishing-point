using UnityEngine;

/// <summary>
/// 배경 스프라이트를 자기 깊이에서의 화면 크기에 맞춰 자동으로 늘린다.
///
/// 배경은 평면보다 뒤에 있어서 화면을 채우려면 평면보다 커야 하는데,
/// 그 크기가 Camera Distance 를 만질 때마다 바뀐다. 매번 손으로 계산하는
/// 대신 이 컴포넌트가 맞춰 준다.
///
/// 배경 스프라이트 오브젝트에 붙인다. 붙이는 순간부터 Scale 과 X/Y 위치는
/// 이 컴포넌트가 관리하므로, 조정은 아래 항목들로만 하면 된다.
/// 스프라이트가 1×1 유닛인 것을 전제로 한다 (sq_Box 는 그렇게 되어 있다).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class BackgroundFitter : MonoBehaviour
{
    [Tooltip("어느 평면의 배경인지. 비워두면 부모에서 자동으로 찾는다")]
    [SerializeField] DepthPlane plane;

    [Tooltip("화면 세로의 몇 %를 덮을지. 1이면 꽉 채우고, 0.7이면 위쪽 30%가 뚫린다")]
    [SerializeField, Range(0.1f, 1f)] float heightFill = 0.7f;

    [Tooltip("가로 여유. 1.05면 화면보다 5% 넓게 잡아 가장자리 틈을 막는다")]
    [SerializeField, Range(1f, 1.5f)] float widthOverfill = 1.05f;

    [Tooltip("바닥 기준에서 위아래로 밀 값. 뚫린 높이를 미세 조정할 때 쓴다")]
    [SerializeField] float verticalNudge = 0f;

    void OnEnable()  { if (GuardWrongTarget()) return; Fit(); }
    void OnValidate(){ if (GuardWrongTarget()) return; Fit(); }
    void LateUpdate(){ if (!enabled) return; Fit(); }

    /// <summary>
    /// 지형이나 콜라이더가 있는 오브젝트에 잘못 붙이면 그것까지 늘려버린다.
    /// 컴포넌트를 떼도 바뀐 Scale 은 남기 때문에 원인을 찾기가 아주 어렵다.
    /// 그래서 아예 붙지 못하게 막는다.
    /// </summary>
    bool GuardWrongTarget()
    {
        if (GetComponent<UnityEngine.Tilemaps.Tilemap>() != null || GetComponent<Collider2D>() != null)
        {
            Debug.LogError(
                $"[BackgroundFitter] '{name}' 은 지형/콜라이더 오브젝트입니다. " +
                "배경 스프라이트에만 붙이세요. 이 컴포넌트를 제거하고, " +
                "Transform 의 Scale 을 1,1,1 로 되돌려 주세요.", this);
            enabled = false;
            return true;
        }
        return false;
    }

    void Fit()
    {
        if (plane == null) plane = GetComponentInParent<DepthPlane>();
        if (plane == null) return;

        var cam = Camera.main;
        if (cam == null) return;

        // 이 오브젝트가 놓인 Z에서 카메라까지의 실제 거리
        Vector3 camPos = plane.CameraPosition;
        float distance = transform.position.z - camPos.z;
        if (distance <= 0.01f) return;   // 카메라 뒤에 있으면 계산 의미 없음

        float screenH = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float screenW = screenH * (cam.aspect > 0f ? cam.aspect : 16f / 9f);

        float wallH = screenH * heightFill;
        float wallW = screenW * widthOverfill;

        // 화면 아래쪽에 붙인다. 그래서 남는 공간은 항상 위쪽이 되고,
        // 그 뚫린 위쪽으로 다음 평면의 배경이 보인다.
        float screenBottom = camPos.y - screenH * 0.5f;
        float centerY = screenBottom + wallH * 0.5f + verticalNudge;

        transform.localScale = new Vector3(wallW, wallH, 1f);
        transform.position   = new Vector3(camPos.x, centerY, transform.position.z);
    }
}
