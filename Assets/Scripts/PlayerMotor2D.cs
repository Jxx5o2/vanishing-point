using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2D 플랫포머 기본 이동. 조작감 4대 요소(코요테 타임, 점프 버퍼,
/// 가변 점프 높이, 낙하 중력 배수)가 전부 들어 있다.
///
/// 수치는 전부 인스펙터에 노출돼 있으니, 재생 중에 만지면서 맞출 것.
/// 마음에 드는 값이 나오면 정지 전에 컴포넌트 우클릭 → Copy Component,
/// 정지 후 Paste Component Values 로 살린다.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor2D : MonoBehaviour
{
    [Header("이동")]
    [Tooltip("최고 이동 속도 (유닛/초)")]
    [SerializeField] float moveSpeed = 8f;
    [Tooltip("가속. 클수록 즉각적으로 움직인다")]
    [SerializeField] float acceleration = 90f;
    [Tooltip("감속. 클수록 딱 멈춘다")]
    [SerializeField] float deceleration = 110f;

    [Header("점프")]
    [Tooltip("점프 시작 속도")]
    [SerializeField] float jumpSpeed = 15f;
    [Tooltip("기본 중력 배율. 플랫포머는 1보다 훨씬 커야 한다")]
    [SerializeField] float baseGravity = 4f;
    [Tooltip("떨어질 때 중력을 몇 배로 할지. 점프가 붕 뜨는 걸 막는다")]
    [SerializeField] float fallMultiplier = 2.2f;
    [Tooltip("점프 버튼을 떼면 상승 속도를 이 비율로 깎는다")]
    [SerializeField, Range(0f, 1f)] float jumpCutoff = 0.45f;
    [Tooltip("최대 낙하 속도. 너무 빨라지면 바닥을 뚫는다")]
    [SerializeField] float maxFallSpeed = 25f;

    [Header("조작 보조")]
    [Tooltip("발판에서 떨어진 뒤 이 시간 동안은 점프를 받아준다")]
    [SerializeField] float coyoteTime = 0.10f;
    [Tooltip("착지 전에 미리 누른 점프를 이 시간 동안 기억한다")]
    [SerializeField] float jumpBuffer = 0.12f;

    [Header("접지 판정")]
    [Tooltip("발밑에 둘 빈 오브젝트를 여기 연결한다")]
    [SerializeField] Transform groundCheck;
    [Tooltip("접지 검사 상자 크기. 캐릭터 폭보다 살짝 좁게")]
    [SerializeField] Vector2 groundCheckSize = new Vector2(0.8f, 0.12f);
    [Tooltip("지형이 속한 레이어만 체크한다. 플레이어 레이어는 절대 넣지 말 것")]
    [SerializeField] LayerMask groundLayers;

    Rigidbody2D rb;
    float inputX;
    float coyoteCounter;
    float bufferCounter;
    bool isGrounded;

    /// <summary>다른 스크립트에서 접지 상태를 읽을 때 쓴다.</summary>
    public bool IsGrounded => isGrounded;

    /// <summary>
    /// 깊이 평면이 바뀌면 밟을 수 있는 지형도 바뀐다.
    /// DepthDirector 가 전환할 때마다 현재 평면의 레이어로 갈아 끼운다.
    /// </summary>
    public void SetGroundLayers(LayerMask layers) => groundLayers = layers;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;   // 인스펙터에서 깜빡해도 여기서 잡아준다
    }

    void Update()
    {
        // 입력은 매 프레임 읽는다. FixedUpdate 에서 읽으면 입력이 씹힌다.
        var kb = Keyboard.current;
        if (kb == null) return;

        inputX = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  inputX -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) inputX += 1f;

        // 점프는 Space 전용. ↑ / W 는 게이트 진입에 쓰므로 여기서 빼둔다.
        bool jumpDown = kb.spaceKey.wasPressedThisFrame;
        bool jumpUp   = kb.spaceKey.wasReleasedThisFrame;

        // 점프 버퍼 — 누른 걸 잠깐 기억해 둔다
        if (jumpDown) bufferCounter = jumpBuffer;
        else          bufferCounter -= Time.deltaTime;

        // 가변 점프 높이 — 버튼을 떼면 상승을 깎는다
        if (jumpUp && rb.linearVelocity.y > 0f)
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, rb.linearVelocity.y * jumpCutoff);
    }

    void FixedUpdate()
    {
        // 접지 판정 — 점 하나가 아니라 발바닥 넓이만큼의 상자로 검사한다
        isGrounded = groundCheck != null
                  && Physics2D.OverlapBox(groundCheck.position, groundCheckSize, 0f, groundLayers);

        // 코요테 타임 — 발판을 떠난 직후에도 잠깐은 점프 가능
        if (isGrounded) coyoteCounter = coyoteTime;
        else            coyoteCounter -= Time.fixedDeltaTime;

        // 좌우 이동
        float target = inputX * moveSpeed;
        float rate   = Mathf.Abs(target) > 0.01f ? acceleration : deceleration;
        float vx     = Mathf.MoveTowards(rb.linearVelocity.x, target, rate * Time.fixedDeltaTime);

        // 점프 — 버퍼와 코요테가 둘 다 살아 있을 때만
        float vy = rb.linearVelocity.y;
        if (bufferCounter > 0f && coyoteCounter > 0f)
        {
            vy = jumpSpeed;
            bufferCounter = 0f;
            coyoteCounter = 0f;   // 이 줄이 없으면 공중에서 두 번 뛴다
        }

        rb.linearVelocity = new Vector2(vx, Mathf.Max(vy, -maxFallSpeed));

        // 낙하 중력 배수 — 올라갈 때보다 떨어질 때 무겁게
        rb.gravityScale = rb.linearVelocity.y < 0f ? baseGravity * fallMultiplier : baseGravity;
    }

    // 씬 뷰에서 접지 판정 범위를 눈으로 확인한다.
    // 재생 중에는 닿아 있으면 초록, 떠 있으면 빨강.
    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Application.isPlaying && isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireCube(groundCheck.position, groundCheckSize);
    }
}
