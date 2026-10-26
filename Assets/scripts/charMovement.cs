using Unity.Netcode;
using UnityEngine;

// Yaya karakter kontrolü. Pozisyon NetworkTransform (Owner), animasyon OwnerNetworkAnimator ile senkron.
// Depar stamina harcar. Denge: Harvester'ın son hızı (~17.5 m/s) yürümeden hızlı, depardan yavaş.
// Düz kaçışta robotun ortalaması (~15 m/s) Harvester'dan düşük kalır, yani robot
// sonsuza kadar kaçamaz; staminayı makaradan yana sıyrılmak ve ekine dalmak için saklaması gerekir.
public class CharMovement : NetworkBehaviour
{
    // Sahibi olduğumuz robot (HUD stamina barı için)
    public static CharMovement Local { get; private set; }

    public float walkSpeed = 12f;
    public float runSpeed = 20f;

    [Header("Stamina (0-1)")]
    public float sprintDrain = 0.2f; // Tam depar 5 sn sürer
    public float jumpCost = 0.1f;
    public float regenDelay = 1f; // Depar/zıplama bittikten sonra dolmaya başlama gecikmesi
    public float regenMoving = 0.125f; // Yürürken 8 sn'de dolar
    public float regenIdle = 0.2f; // Dururken (saklanırken) 5 sn'de dolar
    public float exhaustedUntil = 0.35f; // Sıfırlanınca bu seviyeye kadar depar atılamaz

    public float Stamina { get; private set; } = 1f;
    public bool Exhausted { get; private set; }
    public bool IsSprinting { get; private set; }
    private float regenBlockedUntil;

    private float jumpHeight = 3f;
    public Transform groundCheck;
    private float groundDistance = 0.4f;
    public LayerMask groundLayerMask;
    private bool isGrounded;
    private Animator animator;
    public Camera cam;
    private float mouseSensitivity = 150f;
    private float xRotation = 0f;
    private Rigidbody rb;

    // Input Update'te okunur, fizik FixedUpdate'te uygulanır
    private float inputX, inputZ;
    private bool jumpRequested;
    private bool isRunning;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
        cam.gameObject.SetActive(IsOwner);
        if (IsOwner)
        {
            Local = this;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Geri sayım ve bitiş ekranında yürümek kilitli, etrafa bakmak serbest
        bool canMove = GameManager.InputAllowed;
        inputX = canMove ? Input.GetAxis("Horizontal") : 0f;
        inputZ = canMove ? Input.GetAxis("Vertical") : 0f;
        isRunning = Input.GetKey(KeyCode.LeftShift);
        if (canMove && Input.GetKeyDown(KeyCode.Space) && Stamina >= jumpCost) jumpRequested = true;

        animator.SetFloat("run", Mathf.Abs(inputZ));
        Look();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        Move();
    }

    private void Move()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundLayerMask);

        // Çapraz giderken hız √2 katına çıkmasın
        Vector3 input = Vector3.ClampMagnitude(transform.right * inputX + transform.forward * inputZ, 1f);
        bool moving = input.sqrMagnitude > 0.01f;
        IsSprinting = isRunning && moving && !Exhausted && Stamina > 0f;
        UpdateStamina(moving);

        Vector3 move = input * (IsSprinting ? runSpeed : walkSpeed);

        // Yerçekimini Rigidbody hallediyor, biz sadece yatay hızı ve zıplamayı ayarlıyoruz
        Vector3 velocity = rb.linearVelocity;
        velocity.x = move.x;
        velocity.z = move.z;

        if (jumpRequested && isGrounded && Stamina >= jumpCost)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
            Stamina -= jumpCost;
            regenBlockedUntil = Time.time + regenDelay;
        }
        jumpRequested = false;

        rb.linearVelocity = velocity;
    }

    private void UpdateStamina(bool moving)
    {
        float dt = Time.fixedDeltaTime;
        if (IsSprinting)
        {
            Stamina = Mathf.Max(0f, Stamina - sprintDrain * dt);
            regenBlockedUntil = Time.time + regenDelay;
            if (Stamina <= 0f) Exhausted = true;
        }
        else if (Time.time >= regenBlockedUntil)
        {
            Stamina = Mathf.Min(1f, Stamina + (moving ? regenMoving : regenIdle) * dt);
        }

        if (Exhausted && Stamina >= exhaustedUntil) Exhausted = false;
    }

    private void Look()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        cam.transform.localRotation = Quaternion.Euler(xRotation, 0, 0);
        transform.Rotate(Vector3.up * mouseX);
    }
}
