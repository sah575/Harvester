using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;


public class frankMovement : NetworkBehaviour
{
    #region spins
    public GameObject spinns, direksiyon,driver;
    private float maxRotationSpeed = 150f; // Maksimum rotasyon hýzý
    private float acceleration = 10f; // Rotasyon hýzýnýn artýþ oraný
    private float currentSpeed = 0f; // Þu anki rotasyon hýzý
    #endregion

    #region kamera açýsý
    public float lookSpeed = 2f; // Fare hareket hýzýný ayarlama

    // Dikey bakýþ açýsýnýn sýnýrlarý
    public float minVerticalAngle = -40f;
    public float maxVerticalAngle = 40f;

    // Yatay bakýþ açýsýnýn sýnýrlarý
    public float minHorizontalAngle = -60f;
    public float maxHorizontalAngle = 60f;

    private float rotationX = 0f; // Kameranýn dikey rotasýný tutar
    private float rotationY = 0f; // Kameranýn yatay rotasýný tutar
    #endregion


    public NetworkVariable<float> networkRotationSpeed = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
    public static frankMovement Instance;

    float vInput;
    float hInput;
    float mouseX;
    float mouseY;
    Rigidbody rigidBody;

    // Start is called before the first frame update
    void Start()
    {
        rigidBody = GetComponent<Rigidbody>();
        
        // Adjust center of mass vertically, to help prevent the car from rolling
        rigidBody.centerOfMass = Vector3.up * centreOfGravityOffset;
        Cursor.lockState = CursorLockMode.Locked;
        // Find all child GameObjects that have the WheelControl script attached
       if (IsLocalPlayer)
        {
            fpcam.gameObject.SetActive(true);

        }

    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        GetInput();
        AnimationWheels();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;
        Move();
        spinRpc();
        UpdateSteering();
        LookAroundWithMouse();
    }


    void Move()
    {
        float forwardSpeed = Vector3.Dot(transform.forward, rigidBody.velocity);      
        float speedFactor = Mathf.InverseLerp(0, maxSpeed, forwardSpeed); 
        float currentMotorTorque = Mathf.Lerp(motorTorque, 0, speedFactor);
        float currentSteerRange = Mathf.Lerp(steeringRange, steeringRangeAtMaxSpeed, speedFactor);
        bool isAccelerating = Mathf.Sign(vInput) == Mathf.Sign(forwardSpeed);
        
        foreach (var wheel in wheelList)
        {
          
            if (wheel.axel == Axel.Rear)
            {
                wheel.wheelCollider.steerAngle = Mathf.Lerp(wheel.wheelCollider.steerAngle, hInput * currentSteerRange, 1f);
            }

            if (isAccelerating)
            {
                wheel.wheelCollider.motorTorque = vInput * currentMotorTorque;
                wheel.wheelCollider.brakeTorque = 0;
            }
            else
            {
                wheel.wheelCollider.brakeTorque = Mathf.Abs(vInput) * brakeTorque*100f;
                wheel.wheelCollider.motorTorque = 0;
            }
        }
    }
    void AnimationWheels()
    {
        foreach (var wheel in wheelList)
        {
            Quaternion rot;
            Vector3 pos;
            wheel.wheelCollider.GetWorldPose(out pos, out rot);
            wheel.wheelModel.transform.position = pos;
            wheel.wheelModel.transform.rotation = rot;
        }
    }

    void GetInput()
    {
        vInput = -Input.GetAxis("Vertical");
        hInput = -Input.GetAxis("Horizontal");
        mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        mouseY = Input.GetAxis("Mouse Y") * lookSpeed;
    }

    void UpdateSteering()
    {
        float rotationAmount = -hInput * steeringRange * 4f;
        float newZRotation = direksiyon.transform.eulerAngles.z + rotationAmount;
        // if (newZRotation > 180) newZRotation -= 360; lazým olursa açýyý normalleþtirme
        newZRotation = Mathf.Lerp(newZRotation, rotationAmount, 1f);
        direksiyon.transform.eulerAngles = new Vector3(direksiyon.transform.eulerAngles.x, direksiyon.transform.eulerAngles.y, newZRotation);
    }

    void spinRpc()
    {
        if (Input.GetMouseButton(0))
        {
            currentSpeed = Mathf.Lerp(currentSpeed, maxRotationSpeed, Time.deltaTime * acceleration);
        }
        else
        {
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, Time.deltaTime * 1);
        }
       float rotationAmount = currentSpeed * Time.deltaTime;
        networkRotationSpeed.Value = rotationAmount;
        spinns.transform.Rotate(rotationAmount, 0, 0);
    }

    /*public override void OnNetworkSpawn()
    {
        // Bu, her istemciye güncellemeyi saðlayacaktýr

        networkRotationSpeed.OnValueChanged += (oldValue, newValue) =>
        {
            // Burada diðer istemciler güncellenen dönüþ hýzýný kullanacak
            //float rotationAmount =  * Time.deltaTime;
            
            spinns.transform.Rotate(newValue*Time.deltaTime*10, 0, 0);
        };
    }*/

    void LookAroundWithMouse()
    {
        rotationY += mouseX;
        rotationY = Mathf.Clamp(rotationY, minHorizontalAngle, maxHorizontalAngle);
        rotationX += mouseY;
        rotationX = Mathf.Clamp(rotationX, minVerticalAngle, maxVerticalAngle);
        driver.transform.localRotation = Quaternion.Euler(rotationX, rotationY, 0);
    }
}