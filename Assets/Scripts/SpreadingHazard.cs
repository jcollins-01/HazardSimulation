using UnityEngine;

public class SpreadingHazard : MonoBehaviour
{
    private GameObject flamePrefab;
    private Vector3 targetScale;
    private float growthSpeed = 0.2f; // How fast the flame grows
    private float spreadRadius = 1.5f;
    private float spreadInterval = 2.0f;
    private float spreadTimer;

    public void Initialize(GameObject prefab, Vector3 fullScale)
    {
        flamePrefab = prefab;
        targetScale = fullScale;
        spreadTimer = spreadInterval;
    }

    void Update()
    {
        // Gradually grow the small instance to full size
        if (transform.localScale.x < targetScale.x)
        {
            transform.localScale += Vector3.one * growthSpeed * Time.deltaTime;
            return; // Don't spread until fully grown
        }

        // Once fully grown, act like a new fire source and spread to neighboring objects
        spreadTimer -= Time.deltaTime;
        if (spreadTimer <= 0f)
        {
            SpreadToNearbyObjects();
            spreadTimer = spreadInterval;
        }
    }

    private void SpreadToNearbyObjects()
    {
        // Look for any nearby colliders to catch fire
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, spreadRadius);

        foreach (Collider col in nearbyColliders)
        {
            // Don't catch fire if it's already the parent, itself, or an active hazard
            if (col.gameObject == gameObject || col.transform == transform.parent || col.CompareTag("Active Hazard"))
                continue;

            // Spawn a new flame onto this nearby object
            Vector3 spawnPos = col.ClosestPoint(transform.position);
            GameObject childFlame = Instantiate(flamePrefab, spawnPos, Quaternion.identity);
            childFlame.transform.localScale = targetScale * 0.3f;
            childFlame.transform.SetParent(col.transform);

            SpreadingHazard script = childFlame.AddComponent<SpreadingHazard>();
            script.Initialize(flamePrefab, targetScale);

            break; // Spread to one object at a time per interval
        }
    }
}
