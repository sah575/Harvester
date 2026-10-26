using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Harvester (biçerdöver) kontrolü.
// Senkronizasyon:
//  - Gövde pozisyon/rotasyon: NetworkTransform (Owner authority) + NetworkRigidbody
//  - Biçme makarası: sadece dönüş HIZI ağdan gider, her client makarayı kendisi döndürür
//  - Direksiyon ve tekerlekler: owner'ın teker açısından her client'ta yeniden hesaplanır
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
    private readonly NetworkVariable<float> networkSteerAngle = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public float motorTorque = 10000;
    public float brakeTorque = 2000;
    public float engineBrakeTorque = 300; // Gaz bırakılınca hidrostatik şanzıman gibi yavaşlatır
    public float maxSpeed = 30;
    public float maxReverseSpeed = 8;
    [Range(0f, 1f)] public float torqueFadeStart = 0.6f; // Tork, hız limitinin bu oranından sonra azalmaya başlar
    public float steeringRange = 35;
    public float steeringRangeAtMaxSpeed = 10;
    public float steerSpeed = 70; // Tekerlerin dönüş hızı (derece/sn)
    public float steeringWheelRatio = 4; // Kabindeki direksiyon / teker açısı oranı
    public float centreOfGravityOffset = -1f;

    public Camera fpcam;

    // Makaranın şu anki dönüş hızı (derece/sn); sahibi için yerel değer, diğerleri için ağdan gelen
    public float ReelSpeed => currentSpeed;

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
    float steerAngle; // Arka tekerlerin şu anki açısı; uzak client'ta ağdan gelen değere yaklaşır

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
        // Modelin önü -Z yönünde: pozitif tork aracı geri götürür.
        // Bu yüzden hız ve gaz biçerdöverin önüne göre (ileri = pozitif) hesaplanıyor.
        float forwardSpeed = -Vector3.Dot(transform.forward, rigidBody.linearVelocity);
        float throttle = -vInput;

        float speedLimit = forwardSpeed >= 0f ? maxSpeed : maxReverseSpeed;
        float currentMotorTorque = Mathf.Lerp(motorTorque, 0, Mathf.InverseLerp(speedLimit * torqueFadeStart, speedLimit, Mathf.Abs(forwardSpeed)));
        float currentSteerRange = Mathf.Lerp(steeringRange, steeringRangeAtMaxSpeed, Mathf.InverseLerp(0, maxSpeed, Mathf.Abs(forwardSpeed)));

        // Direksiyon anında değil kademeli döner, bırakınca da kademeli ortalanır
        steerAngle = Mathf.MoveTowards(steerAngle, hInput * currentSteerRange, steerSpeed * Time.fixedDeltaTime);

        float wheelMotor = 0f;
        float wheelBrake = 0f;
        if (Mathf.Abs(throttle) < 0.05f)
        {
            // Gaz yok: motor freni aracı yavaşça durdurur
            wheelBrake = engineBrakeTorque;
        }
        else if (forwardSpeed * throttle < -0.5f)
        {
            // Gidiş yönünün tersine basılıyor: önce fren, durunca diğer yöne kalkar
            wheelBrake = Mathf.Abs(throttle) * brakeTorque;
        }
        else
        {
            wheelMotor = -throttle * currentMotorTorque;
        }

        foreach (var wheel in wheelList)
        {
            // Biçerdöverler arka tekerden döner
            if (wheel.axel == Axel.Rear)
            {
                wheel.wheelCollider.steerAngle = steerAngle;
            }

            wheel.wheelCollider.motorTorque = wheelMotor;
            wheel.wheelCollider.brakeTorque = wheelBrake;
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
    // Teker objeleri döndürülmüş ve negatif ölçekli; o yüzden süspansiyon ve direksiyon
    // ekseni olarak teker objesinin değil aracın yukarı yönü kullanılıyor.
    void AnimationWheelsRemote()
    {
        if (Time.deltaTime <= 0f) return;
        steerAngle = Mathf.Lerp(steerAngle, networkSteerAngle.Value, Time.deltaTime * 10f);
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

            float steer = wheel.axel == Axel.Rear ? steerAngle : 0f;
            Transform t = col.transform;
            float suspension = col.suspensionDistance * Mathf.Abs(t.lossyScale.y);
            Vector3 pos = t.TransformPoint(col.center) - transform.up * (suspension * (1f - col.suspensionSpring.targetPosition));
            Quaternion rot = Quaternion.AngleAxis(steer, transform.up) * t.rotation * Quaternion.Euler(remoteWheelRoll[i], 0, 0);
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
    }

    // Kabindeki direksiyon girdiyi değil tekerlerin gerçek açısını gösterir
    void UpdateSteering()
    {
        direksiyon.transform.localRotation = direksiyonBaseRotation * Quaternion.Euler(0, 0, -steerAngle * steeringWheelRatio);
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
