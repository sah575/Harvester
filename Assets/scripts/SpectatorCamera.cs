using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Ölen robotun kamerası. CharMovement öldüğünde kendi kamerasına ekler.
// Takip modu: hayattaki oyuncuların arkasından bakar (fareyle etrafında döner).
// Serbest mod: WASD ile uçar, Shift hızlandırır.
// Sol tık: sonraki oyuncu, Boşluk: takip / serbest geçişi.
public class SpectatorCamera : MonoBehaviour
{
    const float LookSpeed = 2f;
    const float FlySpeed = 15f;
    const float FastMultiplier = 3f;
    const float RobotDistance = 8f, RobotHeight = 3f;
    const float HarvesterDistance = 20f, HarvesterHeight = 8f;

    private float yaw, pitch;
    private bool free;
    private NetworkBehaviour target; // frankMovement ya da CharMovement
    private int targetIndex = -1;
    private readonly List<NetworkBehaviour> targets = new List<NetworkBehaviour>();

    private void Start()
    {
        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = Mathf.DeltaAngle(0f, euler.x);
        NextTarget();
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            free = false;
            NextTarget();
        }
        if (Input.GetKeyDown(KeyCode.Space)) free = !free;

        yaw += Input.GetAxis("Mouse X") * LookSpeed;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * LookSpeed, -80f, 80f);
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.rotation = rotation;

        // Takip edilen öldüyse ya da oyundan çıktıysa sıradakine geç
        if (!free && !IsAlive(target)) NextTarget();

        if (free || target == null)
        {
            Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            float speed = FlySpeed * (Input.GetKey(KeyCode.LeftShift) ? FastMultiplier : 1f);
            transform.position += rotation * Vector3.ClampMagnitude(input, 1f) * speed * Time.deltaTime;
            return;
        }

        bool harvester = target is frankMovement;
        float distance = harvester ? HarvesterDistance : RobotDistance;
        float height = harvester ? HarvesterHeight : RobotHeight;
        transform.position = target.transform.position + Vector3.up * height - rotation * Vector3.forward * distance;
    }

    private static bool IsAlive(NetworkBehaviour player)
    {
        if (player == null) return false;
        return !(player is CharMovement robot) || !robot.IsDead;
    }

    private void NextTarget()
    {
        targets.Clear();
        targets.AddRange(FindObjectsByType<frankMovement>(FindObjectsSortMode.None));
        foreach (var robot in CharMovement.All)
        {
            if (!robot.IsDead) targets.Add(robot);
        }

        // Her client'ta aynı sıra olsun, sol tık hep bir sonrakine geçsin
        targets.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));

        if (targets.Count == 0)
        {
            target = null;
            return;
        }
        targetIndex = (targetIndex + 1) % targets.Count;
        target = targets[targetIndex];
    }
}
