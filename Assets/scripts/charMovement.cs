using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

public class CharMovement : NetworkBehaviour
{
    public float speed = 12f;
    private float gravity = -9.81f;
    private Vector3 velocity;
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
    private GameObject spin;

    void Start()
    {
        
        animator = GetComponent<Animator>();
        Cursor.lockState = CursorLockMode.Locked;
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;
        if (IsOwner)  cam.gameObject.SetActive(true);
        spin=frankMovement.Instance.spinns;
    }

    void FixedUpdate()
    {
        if (!IsOwner) return;
        Move();
        Look();
        float rotationSpeed = frankMovement.Instance.networkRotationSpeed.Value;
        Debug.Log(rotationSpeed);
    }

    private void Move()
    {
        isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundLayerMask);

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        animator.SetFloat("run", Mathf.Abs(z));
        Vector3 move = transform.right * x + transform.forward * z;

        rb.MovePosition(rb.position + move * speed * Time.fixedDeltaTime);

        velocity.y += gravity * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + velocity * Time.fixedDeltaTime);

        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        if (Input.GetKey(KeyCode.LeftShift) )
        {
            speed = 20;
        }
        else
        {
            speed = 12f;
        }
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
