using UnityEngine;

public class SafetyPin : MonoBehaviour
{
    public float pullDistance = 0.05f;
    public bool IsRemoved { get; private set; }

    Vector3 startPosition;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        if (!IsRemoved && Vector3.Distance(transform.position, startPosition) > pullDistance)
            IsRemoved = true;
    }
}