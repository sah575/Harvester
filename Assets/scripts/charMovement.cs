using Unity.Netcode;
using UnityEngine;

// Yaya karakter kontrolü. Pozisyon NetworkTransform (Owner), animasyon OwnerNetworkAnimator ile senkron.
public class CharMovement : NetworkBehaviour
{
    public float walkSpeed = 12f;
    public float runSpeed = 20f;
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
        if (IsOwner) Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if (!IsOwner) return;

        inputX = Input.GetAxis("Horizontal");
        inputZ = Input.GetAxis("Vertical");
        isRunning = Input.GetKey(KeyCode.LeftShift);
        if (Input.GetKeyDown(KeyCode.Space)) jumpRequested = true;

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

        float speed = isRunning ? runSpeed : walkSpeed;
        Vector3 move = (transform.right * inputX + transform.forward * inputZ) * speed;

        // Yerçekimini Rigidbody hallediyor, biz sadece yatay hızı ve zıplamayı ayarlıyoruz
        Vector3 velocity = rb.linearVelocity;
        velocity.x = move.x;
        velocity.z = move.z;

        if (jumpRequested && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        }
        jumpRequested = false;

        rb.linearVelocity = velocity;
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
