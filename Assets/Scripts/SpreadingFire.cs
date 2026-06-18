using UnityEngine;

public class SpreadingFire : MonoBehaviour
{
    private GameObject flamePrefab;
    private GameObject smokePrefab;

    // Fire growth vars
    private Vector3 startScale;
    private Vector3 targetScale;
    private float growthSpeed = 0.2f; // base speed, modulated by curve
    private float spreadRadius = 1.5f;
    private float spreadInterval = 2.0f;
    private float spreadTimer;
    private float growthElapsed = 0f;
    private float growthDuration = 4f; // how long it takes to reach full size

    // Non-linear growth curve: fast start, slows near the end (ease-out)
    private AnimationCurve growthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // Smoke growth vars
    private bool hasSpawnedSmoke = false;
    private float smokeSpawnDelay; // staggered delay before smoke appears
    private float smokeSpawnTimer = 0f;
    private GameObject spawnedSmokeInstance;
    private float smokeGrowthSpeed = 0.3f;
    private Vector3 targetSmokeScale;

    // Burnout / decay vars
    private bool isFullyGrown = false;
    private bool isBurningOut = false;
    private float lifetimeAfterFull; // time spent "fully on fire" before burning out
    private float lifetimeTimer = 0f;
    private float burnoutDuration = 3f; // time to shrink down and char
    private float burnoutElapsed = 0f;
    private Vector3 burnoutStartScale;
    private Material charredMaterial;

    public void Initialize(GameObject prefab, GameObject smoke, Vector3 fullScale)
    {
        flamePrefab = prefab;
        smokePrefab = smoke;

        startScale = transform.localScale;
        targetScale = fullScale * 3f;

        targetSmokeScale = new Vector3(targetScale.x * 1.5f, targetScale.y * 0.6f, targetScale.z * 1.5f);

        spreadTimer = spreadInterval * Random.Range(0.85f, 1.15f);

        // Stagger smoke appearance so it doesn't pop in the instant fire finishes growing
        smokeSpawnDelay = Random.Range(0.5f, 2.0f);

        // Randomize lifetime a bit so fires don't all burn out in sync
        lifetimeAfterFull = Random.Range(10f, 20f);

        // Load the charred/burnt material once
        charredMaterial = Resources.Load<Material>("Materials/Black");
    }

    void Update()
    {
        if (isBurningOut)
        {
            HandleBurnout();
            return;
        }

        if (!isFullyGrown)
        {
            HandleGrowth();
            return; // Don't spread or spawn smoke until fully grown
        }

        HandleSmoke();

        // Once fully grown, count down toward burnout
        lifetimeTimer += Time.deltaTime;
        if (lifetimeTimer >= lifetimeAfterFull)
        {
            StartBurnout();
            return;
        }

        // Spread to neighbors while actively burning
        spreadTimer -= Time.deltaTime;
        if (spreadTimer <= 0f)
        {
            SpreadToNearbyObjects();
            spreadTimer = spreadInterval * Random.Range(0.85f, 1.15f);
        }
    }

    private void HandleGrowth()
    {
        growthElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(growthElapsed / growthDuration);
        float curveT = growthCurve.Evaluate(t);

        transform.localScale = Vector3.Lerp(startScale, targetScale, curveT);

        if (t >= 1f)
        {
            transform.localScale = targetScale;
            isFullyGrown = true;
        }
    }

    private void HandleSmoke()
    {
        if (!hasSpawnedSmoke && smokePrefab != null)
        {
            smokeSpawnTimer += Time.deltaTime;
            if (smokeSpawnTimer >= smokeSpawnDelay)
            {
                SpawnSmoke();
                hasSpawnedSmoke = true;
            }
            return;
        }

        // Gradually grow smoke with a billowing, non-uniform shape
        if (spawnedSmokeInstance != null && spawnedSmokeInstance.transform.localScale.x < targetSmokeScale.x)
        {
            Vector3 smokeGrowthVector = new Vector3(1f, 0.2f, 1f) * smokeGrowthSpeed * Time.deltaTime;
            spawnedSmokeInstance.transform.localScale += smokeGrowthVector;
        }
    }

    private void SpawnSmoke()
    {
        Vector3 smokePosition = transform.position + (Vector3.up * (transform.localScale.y * 0.5f));
        spawnedSmokeInstance = Instantiate(smokePrefab, smokePosition, Quaternion.identity);
        spawnedSmokeInstance.transform.localScale = Vector3.zero;

        if (transform.parent != null)
        {
            spawnedSmokeInstance.transform.SetParent(transform.parent);
        }
    }

    private void StartBurnout()
    {
        isBurningOut = true;
        burnoutElapsed = 0f;
        burnoutStartScale = transform.localScale;
    }

    private void HandleBurnout()
    {
        burnoutElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(burnoutElapsed / burnoutDuration);

        // Flame shrinks down to nothing as it dies out
        transform.localScale = Vector3.Lerp(burnoutStartScale, Vector3.zero, t);

        // Smoke thins out and fades away too
        if (spawnedSmokeInstance != null)
        {
            Vector3 smokeCurrentScale = spawnedSmokeInstance.transform.localScale;
            spawnedSmokeInstance.transform.localScale = Vector3.Lerp(smokeCurrentScale, Vector3.zero, t * 0.5f * Time.deltaTime * 10f);
        }

        if (t >= 1f)
        {
            // Char the surface this fire was on
            CharSurface();

            if (spawnedSmokeInstance != null)
            {
                Destroy(spawnedSmokeInstance);
            }

            Destroy(gameObject);
        }
    }

    private void CharSurface()
    {
        if (charredMaterial == null || transform.parent == null)
            return;

        Renderer parentRenderer = transform.parent.GetComponent<Renderer>();
        if (parentRenderer != null)
        {
            parentRenderer.material = charredMaterial;
        }

        // Mark the object as already burnt so fire can't re-spread to it
        transform.parent.tag = "No Hazard";
    }

    private void SpreadToNearbyObjects()
    {
        float dynamicRadius = Mathf.Max(spreadRadius, transform.localScale.x * 0.6f);

        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, dynamicRadius);

        // Allow spreading to multiple objects per interval, scaling with current fire size.
        // Bigger fires can ignite more neighbors at once.
        int maxSpreadsThisInterval = Mathf.Clamp(Mathf.FloorToInt(transform.localScale.x / targetScale.x * 3f) + 1, 1, 3);
        int spreadsDone = 0;

        foreach (Collider col in nearbyColliders)
        {
            if (spreadsDone >= maxSpreadsThisInterval)
                break;

            if (col.gameObject == gameObject || col.transform == transform.parent || col.CompareTag("Active Hazard") || col.CompareTag("No Hazard"))
                continue;

            if (col.GetComponentInChildren<SpreadingFire>() != null)
                continue;

            Debug.Log($"A burning object {this.gameObject.name} spread a fire to {col.gameObject.name}");

            Vector3 spawnPos = col.ClosestPoint(transform.position);
            GameObject childFlame = Instantiate(flamePrefab, spawnPos, Quaternion.identity);
            childFlame.transform.localScale = Vector3.one * 0.01f; // start tiny, grow via curve
            childFlame.transform.SetParent(col.transform);

            SpreadingFire script = childFlame.AddComponent<SpreadingFire>();
            script.Initialize(flamePrefab, smokePrefab, col.transform.localScale);

            spreadsDone++;
        }
    }
}