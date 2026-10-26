using System.Collections.Generic;
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
    // Sahnedeki bütün robotlar (makara tespiti ve izleyici kamerası için)
    public static readonly List<CharMovement> All = new List<CharMovement>();

    public float walkSpeed = 12f;
    public float runSpeed = 20f;

    [Header("Stamina (0-1)")]
    public float sprintDrain = 0.2f; // Tam depar 5 sn sürer
    public float jumpCost = 0.1f;
    public float regenDelay = 1f; // Depar/zıplama bittikten sonra dolmaya başlama gecikmesi
    public float regenMoving = 0.125f; // Yürürken 8 sn'de dolar
    public float regenIdle = 0.2f; // Dururken (saklanırken) 5 sn'de dolar
    public float exhaustedUntil = 0.35f; // Sıfırlanınca bu seviyeye kadar depar atılamaz

    [Header("Parçalanma")]
    public float shatterSpeed = 9f; // Parçaların Harvester'dan uzağa fırlama hızı
    public float shatterLift = 6f; // Yukarı fırlama hızı (en fazla)
    public float debrisLifetime = 8f;

    // Sadece sunucu yazar. Ölen robot gizlenir, sahibi izleyici kamerasına geçer.
    private readonly NetworkVariable<bool> dead = new NetworkVariable<bool>(false);
    public bool IsDead => dead.Value;
    public float DiedAt { get; private set; }
    private Collider[] colliders;
    private Renderer[] renderers;

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
        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    public override void OnNetworkSpawn()
    {
        cam.gameObject.SetActive(IsOwner);
        if (IsOwner)
        {
            Local = this;
            Cursor.lockState = CursorLockMode.Locked;
        }
        All.Add(this);
        dead.OnValueChanged += OnDeadChanged;
        if (dead.Value) ApplyDeath();
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
        All.Remove(this);
        dead.OnValueChanged -= OnDeadChanged;
    }

    // Sunucu çağırır (makara robotu biçti). Robot sayacını GameManager düşürür.
    public void Kill()
    {
        if (!IsServer || dead.Value) return;
        dead.Value = true;
    }

    private void OnDeadChanged(bool previous, bool current)
    {
        if (current) ApplyDeath();
    }

    private void ApplyDeath()
    {
        DiedAt = Time.time;
        IsSprinting = false;
        Shatter();
        foreach (var c in colliders) c.enabled = false;
        foreach (var bender in GetComponentsInChildren<CropBender>()) bender.enabled = false;
        if (TryGetComponent(out BoundaryGuard guard)) guard.enabled = false;
        animator.enabled = false;

        if (!IsOwner) return;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;

        // Kamerayı gövdeden ayırıp izleyiciye çevir
        cam.transform.SetParent(null, true);
        cam.gameObject.AddComponent<SpectatorCamera>();
    }

    // Robotun parçalarını kemiklerden koparıp fizikle saçar. Sadece görsel: her client kendi
    // ekranında, o anki animasyon pozundan başlatır; ağdan senkronlanmaz.
    private void Shatter()
    {
        var harvester = FindFirstObjectByType<frankMovement>();
        Vector3 from = harvester != null ? harvester.transform.position : transform.position - transform.forward;

        foreach (var r in renderers)
        {
            if (!(r is MeshRenderer))
            {
                r.enabled = false;
                continue;
            }

            Transform piece = r.transform;
            piece.SetParent(null, true);
            piece.gameObject.AddComponent<BoxCollider>(); // Mesh sınırına göre boyutlanır
            var body = piece.gameObject.AddComponent<Rigidbody>();
            body.mass = 2f;

            Vector3 away = piece.position - from;
            away.y = 0f;
            body.linearVelocity = away.normalized * shatterSpeed * Random.Range(0.6f, 1.4f)
                                + Vector3.up * Random.Range(shatterLift * 0.4f, shatterLift);
            body.angularVelocity = Random.insideUnitSphere * 12f;
            Destroy(piece.gameObject, debrisLifetime + Random.Range(0f, 2f));
        }
    }

    // Collider'ların birleşik dünya sınırı (makara tespiti için)
    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var c in colliders)
        {
            if (!c.enabled) continue;
            if (any) bounds.Encapsulate(c.bounds);
            else bounds = c.bounds;
            any = true;
        }
        return any;
    }

    private void Update()
    {
        if (!IsOwner || IsDead) return;

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
        if (!IsOwner || IsDead) return;
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
