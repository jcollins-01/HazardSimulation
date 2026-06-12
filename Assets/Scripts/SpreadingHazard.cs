using UnityEngine;

public class SpreadingHazard : MonoBehaviour
{
    private GameObject flamePrefab;
    private GameObject smokePrefab;

    // Fire growth vars
    private Vector3 targetScale;
    private float growthSpeed = 0.2f; // How fast the flame grows
    private float spreadRadius = 1.5f; // Starting spread radius for the first flame
    private float spreadInterval = 2.0f;
    private float spreadTimer;

    // Smoke growth vars
    private bool hasSpawnedSmoke = false;
    private GameObject spawnedSmokeInstance; // Keeps track of the smoke this fire created
    private float smokeGrowthSpeed = 0.3f;   // Smoke usually billows/grows faster than fire
    private Vector3 targetSmokeScale;        // How big the smoke gets

    public void Initialize(GameObject prefab, GameObject smoke, Vector3 fullScale)
    {
        flamePrefab = prefab;
        smokePrefab = smoke;
        targetScale = fullScale * 3f; // unsur eabout this...

        // Smoke becomes wide and flat (X and Z are large, Y is kept lower)
        targetSmokeScale = new Vector3(targetScale.x * 1.5f, targetScale.y * 0.6f, targetScale.z * 1.5f);

        spreadTimer = spreadInterval;
    }

    void Update()
    {
        // Gradually grow the small instance to full size
        if (transform.localScale.x < targetScale.x) // three times larger than original object
        {
            transform.localScale += Vector3.one * growthSpeed * Time.deltaTime;
            return; // Don't spread until fully grown
        }

        if (!hasSpawnedSmoke && smokePrefab != null)
        {
            SpawnSmoke();
            hasSpawnedSmoke = true; // Never spawn it again on this specific object
        }

        // Gradually grow smoke - check if it exists, and if it's smaller than its target scale
        if (spawnedSmokeInstance != null && spawnedSmokeInstance.transform.localScale.x < targetSmokeScale.x)
        {
            // Create a custom non-uniform growth vector for a billowing effect
            Vector3 smokeGrowthVector = new Vector3(1f, 0.2f, 1f) * smokeGrowthSpeed * Time.deltaTime;
            spawnedSmokeInstance.transform.localScale += smokeGrowthVector;
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
        // Position smoke right at the top ceiling of the fire block
        Vector3 smokePosition = transform.position + (Vector3.up * (transform.localScale.y * 0.5f));

        // Save the instance to our variable so we can control it in Update()
        spawnedSmokeInstance = Instantiate(smokePrefab, smokePosition, Quaternion.identity);

        // Shrink it down to zero immediately so we can watch it grow organically
        spawnedSmokeInstance.transform.localScale = Vector3.zero;

        if (transform.parent != null)
        {
            spawnedSmokeInstance.transform.SetParent(transform.parent);
        }
    }

    private void SpreadToNearbyObjects()
    {
        // Multiply base radius by our current fire scale
        //float dynamicRadius = spreadRadius * (transform.localScale.x * 0.5f);
        float dynamicRadius = Mathf.Max(spreadRadius, transform.localScale.x * 0.6f);

        // Look for any nearby colliders to catch fire
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, dynamicRadius);

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
            // Instead of passing the current fire's scale down, pass the new object scale to stop the exponential growth
            script.Initialize(flamePrefab, smokePrefab, col.transform.localScale);

            break; // Spread to one object at a time per interval
        }
    }
}
