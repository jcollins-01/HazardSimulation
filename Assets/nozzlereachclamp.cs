using UnityEngine;

public class NozzleHoseAttach : MonoBehaviour
{
    public Transform hoseTipBone; // the same bone you assigned as "Tip" on the Chain IK Constraint
    public float snapThreshold = 0.05f; // small tolerance so it doesn't jitter over tiny gaps

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void LateUpdate()
    {
        float gap = Vector3.Distance(transform.position, hoseTipBone.position);
        if (gap > snapThreshold)
        {
            transform.position = hoseTipBone.position;
            if (rb != null && !rb.isKinematic)
                rb.linearVelocity = Vector3.zero;
        }
    }
}