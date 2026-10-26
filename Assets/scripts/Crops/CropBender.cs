using System.Collections.Generic;
using UnityEngine;

// İçinden geçerken mısırları iten obje (robotlar, Harvester gövdesi). Etki shader'da hesaplanır.
public class CropBender : MonoBehaviour
{
    public static readonly List<CropBender> Active = new List<CropBender>();

    [SerializeField] private float radius = 2.5f;

    public float Radius => radius;

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }
}
