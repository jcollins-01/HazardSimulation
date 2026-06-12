using UnityEngine;

public class SpreadingHazard : MonoBehaviour
{
    private GameObject flamePrefab;
    private GameObject smokePrefab;
    private Vector3 targetScale;
    private float growthSpeed = 0.2f; // How fast the flame grows
    private float spreadRadius = 1.5f;
    private float spreadInterval = 2.0f;
    private float spreadTimer;

    private bool hasSpawnedSmoke = false;

    public void Initialize(GameObject prefab, GameObject smoke, Vector3 fullScale)
    {
        flamePrefab = prefab;
        smokePrefab = smoke;
        targetScale = fullScale;
        spreadTimer = spreadInterval;
    }

    void Update()
    {
        // Gradually grow the small instance to full size
        if (transform.localScale.x < targetScale.x * 3) // three times larger than original object
        {
            transform.localScale += Vector3.one * growthSpeed * Time.deltaTime;
            return; // Don't spread until fully grown
        }

        if (!hasSpawnedSmoke && smokePrefab != null)
        {
            SpawnSmoke();
            hasSpawnedSmoke = true; // Never spawn it again on this specific object
        }

        // Once fully grown, act like a new fire source and spread to neighboring objects
        spreadTimer -= Time.deltaTime;
        if (spreadTimer <= 0f)
        {
            SpreadToNearbyObjects();
            spreadTimer = spreadInterval;
        }
    }

    private void SpawnSmoke()
    {
        // Spawn the smoke slightly above the fire so it doesn't clip directly inside it
        Vector3 smokePosition = transform.position + (Vector3.up * (targetScale.y * 0.5f));

        GameObject smokeInstance = Instantiate(smokePrefab, smokePosition, Quaternion.identity);

        // Parent the smoke to the original object (the parent of THIS flame)
        if (transform.parent != null)
        {
            smokeInstance.transform.SetParent(transform.parent);
        }

        // Optional: If you want the smoke to also grow, you can add a simple growth script to it here!
        // smokeInstance.AddComponent<GrowingSmoke>(); 
    }

    private void SpreadToNearbyObjects()
    {
        // Look for any nearby colliders to catch fire
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, spreadRadius);

        foreach (Collider col in nearbyColliders)
        {
            // Don't catch fire if it's already the parent, itself, or an active hazard, or actively listed as no hazard
            if (col.gameObject == gameObject || col.transform == transform.parent || col.CompareTag("Active Hazard") || col.CompareTag("No Hazard"))
                continue;

            // If it already has a SpreadingHazard on it or inside it, skip it!
            if (col.GetComponentInChildren<SpreadingHazard>() != null)
                continue;

            Debug.Log($"A burning object {this.gameObject.name} spread a fire to {col.gameObject.name}");
            // Spawn a new flame onto this nearby object
            Vector3 spawnPos = col.ClosestPoint(transform.position);
            GameObject childFlame = Instantiate(flamePrefab, spawnPos, Quaternion.identity);
            childFlame.transform.localScale = targetScale * 0.3f;
            childFlame.transform.SetParent(col.transform);

            SpreadingHazard script = childFlame.AddComponent<SpreadingHazard>();
            script.Initialize(flamePrefab, smokePrefab, targetScale);

            break; // Spread to one object at a time per interval
        }
    }
}
