using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Harvester (biçerdöver) kontrolü.
// Senkronizasyon:
//  - Gövde pozisyon/rotasyon: NetworkTransform (Owner authority) + NetworkRigidbody
//  - Biçme makarası: sadece dönüş HIZI ağdan gider, her client makarayı kendisi döndürür
//  - Direksiyon ve tekerlekler: owner'ın girdisinden/açısından her client'ta yeniden hesaplanır
public class frankMovement : NetworkBehaviour
{
    #region spins
    public GameObject spinns, direksiyon, driver;
    private float maxRotationSpeed = 150f; // Maksimum rotasyon hızı (derece/sn)
    private float acceleration = 10f; // Rotasyon hızının artış oranı
    private float deceleration = 1f; // Bırakınca yavaşlama oranı
    private float currentSpeed = 0f; // Şu anki rotasyon hızı
    #endregion

    #region kamera açısı
    public float lookSpeed = 2f; // Fare hareket hızını ayarlama

    // Dikey bakış açısının sınırları
    public float minVerticalAngle = -40f;
    public float maxVerticalAngle = 40f;

    // Yatay bakış açısının sınırları
    public float minHorizontalAngle = -60f;
    public float maxHorizontalAngle = 60f;

    private float rotationX = 0f; // Kameranın dikey rotasını tutar
    private float rotationY = 0f; // Kameranın yatay rotasını tutar
    #endregion

    // Owner yazar, herkes okur. Değerler sadece değişince gönderilir.
    private readonly NetworkVariable<float> networkReelSpeed = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> networkSteerInput = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> networkSteerAngle = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public float motorTorque = 10000;
    public float brakeTorque = 2000;
    public float maxSpeed = 30;
    public float steeringRange = 35;
    public float steeringRangeAtMaxSpeed = 10;
    public float centreOfGravityOffset = -1f;

    public Camera fpcam;

    public enum Axel
    {
        Front,
        Rear
    }

    [Serializable]
    public struct Wheel
    {
        public GameObject wheelModel;
        public WheelCollider wheelCollider;
        public Axel axel;
    }

    public List<Wheel> wheelList;

    float vInput;
    float hInput;
    float mouseX;
    float mouseY;
    Rigidbody rigidBody;
    Quaternion direksiyonBaseRotation;

    // Uzak client'ta tekerlek dönüşünü hesaplamak için
    float[] remoteWheelRoll;
    Vector3 lastPosition;

    private void Awake()
    {
        rigidBody = GetComponent<Rigidbody>();
        // Adjust center of mass vertically, to help prevent the car from rolling
        rigidBody.centerOfMass = Vector3.up * centreOfGravityOffset;
        direksiyonBaseRotation = direksiyon.transform.localRotation;
        remoteWheelRoll = new float[wheelList.Count];
    }

    public override void OnNetworkSpawn()
    {
        lastPosition = transform.position;
        fpcam.gameObject.SetActive(IsOwner);

        // Ayna kameralarındaki AudioListener'lar "birden fazla listener" uyarısı veriyor
        foreach (var listener in GetComponentsInChildren<AudioListener>(true))
        {
            if (listener.gameObject != fpcam.gameObject) listener.enabled = false;
        }

        // Başkasının aracındaki ayna kameralarını render etmeye gerek yok
        if (!IsOwner)
        {
            foreach (var cam in GetComponentsInChildren<Camera>(true))
            {
                if (cam != fpcam) cam.enabled = false;
            }
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if (IsOwner)
        {
            GetInput();
            UpdateReelSpeed();
            LookAroundWithMouse();
        }
        else
        {
            // Ağdan gelen hıza yumuşakça yaklaş, adım adım atlamasın
            currentSpeed = Mathf.Lerp(currentSpeed, networkReelSpeed.Value, Time.deltaTime * acceleration);
        }

        spinns.transform.Rotate(currentSpeed * Time.deltaTime, 0, 0);
        UpdateSteering();
    }

    private void FixedUpdate()
    {
        if (!IsOwner) return;
        Move();
    }

    private void LateUpdate()
    {
        if (IsOwner) AnimationWheels();
        else AnimationWheelsRemote();
    }

    void Move()
    {
        float forwardSpeed = Vector3.Dot(transform.forward, rigidBody.linearVelocity);
        float speedFactor = Mathf.InverseLerp(0, maxSpeed, forwardSpeed);
        float currentMotorTorque = Mathf.Lerp(motorTorque, 0, speedFactor);
        float currentSteerRange = Mathf.Lerp(steeringRange, steeringRangeAtMaxSpeed, speedFactor);
        bool isAccelerating = Mathf.Sign(vInput) == Mathf.Sign(forwardSpeed);
        float steerAngle = hInput * currentSteerRange;

        foreach (var wheel in wheelList)
        {
            // Biçerdöverler arka tekerden döner
            if (wheel.axel == Axel.Rear)
            {
                wheel.wheelCollider.steerAngle = steerAngle;
            }

            if (isAccelerating)
            {
                wheel.wheelCollider.motorTorque = vInput * currentMotorTorque;
                wheel.wheelCollider.brakeTorque = 0;
            }
            else
            {
                wheel.wheelCollider.brakeTorque = Mathf.Abs(vInput) * brakeTorque * 100f;
                wheel.wheelCollider.motorTorque = 0;
            }
        }

        SetIfChanged(networkSteerAngle, steerAngle, 0.25f);
    }

    void AnimationWheels()
    {
        foreach (var wheel in wheelList)
        {
            wheel.wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
            wheel.wheelModel.transform.SetPositionAndRotation(pos, rot);
        }
    }

    // Uzak client'ta araç kinematic olduğu için WheelCollider çalışmaz,
    // tekerlek dönüşünü aracın hareketinden hesaplıyoruz.
    void AnimationWheelsRemote()
    {
        if (Time.deltaTime <= 0f) return;
        Vector3 velocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;
        float forwardSpeed = Vector3.Dot(transform.forward, velocity);

        for (int i = 0; i < wheelList.Count; i++)
        {
            var wheel = wheelList[i];
            var col = wheel.wheelCollider;
            if (col.radius > 0f)
            {
                remoteWheelRoll[i] = Mathf.Repeat(remoteWheelRoll[i] + forwardSpeed / col.radius * Mathf.Rad2Deg * Time.deltaTime, 360f);
            }

            float steer = wheel.axel == Axel.Rear ? networkSteerAngle.Value : 0f;
            Transform t = col.transform;
            Vector3 pos = t.TransformPoint(col.center) - t.up * (col.suspensionDistance * (1f - col.suspensionSpring.targetPosition));
            Quaternion rot = t.rotation * Quaternion.Euler(0, steer, 0) * Quaternion.Euler(remoteWheelRoll[i], 0, 0);
            wheel.wheelModel.transform.SetPositionAndRotation(pos, rot);
        }
    }

    void GetInput()
    {
        // Geri sayım ve bitiş ekranında sürüş kilitli, etrafa bakmak serbest
        bool canDrive = GameManager.InputAllowed;
        vInput = canDrive ? -Input.GetAxis("Vertical") : 0f;
        hInput = canDrive ? -Input.GetAxis("Horizontal") : 0f;
        mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        mouseY = Input.GetAxis("Mouse Y") * lookSpeed;
        SetIfChanged(networkSteerInput, hInput, 0.02f);
    }

    void UpdateSteering()
    {
        float steer = IsOwner ? hInput : networkSteerInput.Value;
        float rotationAmount = -steer * steeringRange * 4f;
        direksiyon.transform.localRotation = direksiyonBaseRotation * Quaternion.Euler(0, 0, rotationAmount);
    }

    void UpdateReelSpeed()
    {
        if (Input.GetMouseButton(0) && GameManager.InputAllowed)
        {
            currentSpeed = Mathf.Lerp(currentSpeed, maxRotationSpeed, Time.deltaTime * acceleration);
        }
        else
        {
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, Time.deltaTime * deceleration);
            if (currentSpeed < 0.5f) currentSpeed = 0f;
        }
        SetIfChanged(networkReelSpeed, currentSpeed, 1f);
    }

    void LookAroundWithMouse()
    {
        rotationY += mouseX;
        rotationY = Mathf.Clamp(rotationY, minHorizontalAngle, maxHorizontalAngle);
        rotationX += mouseY;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
        driver.transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
    }

    // Her karede ufak değişimleri göndermemek için eşik kullan, ama 0'a dönüşü her zaman gönder
    static void SetIfChanged(NetworkVariable<float> variable, float value, float threshold)
    {
        if (Mathf.Abs(variable.Value - value) >= threshold || (value == 0f && variable.Value != 0f))
        {
            variable.Value = value;
        }
    }
}
