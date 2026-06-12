using System.Collections;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class UniversalHazardController : MonoBehaviour
{
    // Variables to set in the public editor -- all will be passed to the produced state model
    [Header("PLAYER TO THREATEN")]
    public GameObject player; // could change this later to an array to affect multiple things
    public GameObject flamePrefab;
    public GameObject smokePrefab;

    [Header("HAZARDS TO MODEL")]
    [Space(10)]

    public GameObject[] hazards;

    [Header("HAZARD ATTRIBUTES")]
    [Space(10)]

    [Header("Inciting Values")] // How likely are specific behaviors to happen
    [Range(0, 10)] public int aggressionLevel;
    [Range(0, 10)] public int cautionLevel;

    [Header("Trigger Values")] // What triggers specific behaviors
    public int noticeDistance;
    public int touchingDistance;

    [Header("Scale Settings")]
    public Vector3 targetMaxScale;
    public float transitionDuration;

    [Header("Behavior Bools")] // Is a specific behavior present or not
    public bool patrolling;
    public bool approaching;
    public bool attacking;
    public bool looming; // growing larger than the player so the player has to look up at it
    public bool spreading;

    [Header("Hazard Boundaries")] // What limits or ends the hazard's behaviors
    public int escapeNoticeDistance;
    public int timeToLoseInterestOrEffect;
    public NavMeshSurface hazardTerritory;

    // Vars for functions
    private Coroutine scaleCoroutine;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    private void SetHazardBehaviors()
    {
        if (hazards == null || hazards.Length == 0) return;

        foreach (GameObject haz in hazards)
        {
            if (haz == null) continue;

            // Check if it already has the component to avoid duplicates
            HazardBehaviorModel model = haz.GetComponent<HazardBehaviorModel>();

            if (model == null)
            {
#if UNITY_EDITOR
                // Ensures the operation is registered in the Editor's undo history and persists
                model = Undo.AddComponent<HazardBehaviorModel>(haz);
#else
                model = haz.AddComponent<HazardBehaviorModel>();
#endif
            }

            // Pass parameters safely
            model.Initialize(
                this, player,
                aggressionLevel, cautionLevel, noticeDistance, touchingDistance,
                patrolling, approaching, attacking, looming, spreading,
                escapeNoticeDistance, timeToLoseInterestOrEffect, hazardTerritory
            );

#if UNITY_EDITOR
            // Tell Unity the object's state changed so it saves the new variable values
            EditorUtility.SetDirty(model);
#endif
        }

        Debug.Log($"Successfully initialized hazard models for {hazards.Length} objects.");
    }

    // Allows us to set the behaviors on each hazard once we're done
    [CustomEditor(typeof(UniversalHazardController))]
    public class HazardControllerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw the default inspector variables (like the hazards array)
            DrawDefaultInspector();

            UniversalHazardController script = (UniversalHazardController)target;

            GUILayout.Space(10);

            // Match your styling preference
            GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
            if (GUILayout.Button("Set Hazard Behaviors", GUILayout.Height(30)))
            {
                // This ensures changes are recorded for saving
                Undo.RecordObject(script, "Set Hazard Behaviors");
                script.SetHazardBehaviors();
            }
        }
    }

    public void resetHazard(NavMeshAgent agent, HazardBehaviorModel model)
    {
        Debug.Log($"Resetting {model.gameObject.name} hazard behavior to passive movements");
        // Resets hazard to do a passive behavior
        PassiveMovement(agent, model);
        TriggerShrink(model);
        model.alreadyTriggeredByPlayer = false;
    }

    public void AlertedMovement(NavMeshAgent agent, HazardBehaviorModel model)
    {
        if (model.approaching)
        {
            Approaching(agent, model);
        }

        if (model.looming)
        {
            Looming(agent, model);
        }
    }

    public void PassiveMovement(NavMeshAgent agent, HazardBehaviorModel model)
    {
        /*if (camping)
        {
            Camping();
        }*/

        if (model.patrolling)
        {
            Patrolling(agent, model);
        }
    }

    public void Approaching(NavMeshAgent agent, HazardBehaviorModel model)
    {
        // Simply set destination to player
        //Debug.Log($"{model.gameObject.name} is approaching player");
        agent.SetDestination(model.player.transform.position);

        if (model.distanceToPlayer < model.touchingDistance)
        {
            TouchingMovement(agent, model);
        }
    }

    public void TouchingMovement(NavMeshAgent agent, HazardBehaviorModel model)
    {
        if (model.attacking)
        {
            // We only start the attack routine if it isn't already running
            //Debug.Log($"{model.gameObject.name} got close to player to attack");
            if (!model.isAttacking)
            {
                StartCoroutine(AttackSequence(agent, model));
            }
        }
        else
        {
            //Debug.Log($"{model.gameObject.name} got close to player but won't attack");
            resetHazard(agent, model);
        }
    }

    public void Looming(NavMeshAgent agent, HazardBehaviorModel model)
    {
        // Simply set destination to player
        //Debug.Log($"{model.gameObject.name} is looming over player");

        // Stop any currently running scale transition to prevent conflicts
        if (model.activeScaleCoroutine != null)
        {
            StopCoroutine(model.activeScaleCoroutine);
        }

        // Start the smooth scaling coroutine towards the target max scale
        model.activeScaleCoroutine = StartCoroutine(ScaleOverTime(model, targetMaxScale));
    }

    public void TriggerShrink(HazardBehaviorModel model)
    {
        if (model.activeScaleCoroutine != null)
        {
            StopCoroutine(model.activeScaleCoroutine);
        }

        // Start the smooth scaling coroutine back towards the original scale
        model.activeScaleCoroutine = StartCoroutine(ScaleOverTime(model, model.originalScale));
    }

    public void Spreading(HazardBehaviorModel model)
    {
        if (model.lastTouchedObject == null) return;

        // If the object is already burning
        if (model.lastTouchedObject.GetComponentInChildren<SpreadingHazard>() != null) return;

        // Determine a point on the surface of the touched object -- closest point on the target's collider to the hazard
        Vector3 spawnPosition = model.lastTouchedObject.GetComponent<Collider>().ClosestPoint(model.transform.position);

        // Spawn the smaller hazard instance (the "flame")
        GameObject newFlame = Instantiate(flamePrefab, spawnPosition, Quaternion.identity);
        Debug.Log($"Hazard itself spawned a flame on {model.lastTouchedObject.gameObject.name}");

        // Scale it down to make it a "small duplicate"
        newFlame.transform.localScale = model.originalScale * 0.3f; // 30% of original size

        // Attach it to the touched object so it moves WITH it (e.g., if a chair moves, the fire stays on it)
        newFlame.transform.SetParent(model.lastTouchedObject.transform);

        // Attach the growth behavior script dynamically so it can spawn its own copies
        SpreadingHazard spreadingScript = newFlame.AddComponent<SpreadingHazard>();
        spreadingScript.Initialize(flamePrefab, smokePrefab, model.originalScale);
    }

    // The Coroutine that handles the actual frame-by-frame interpolation
    private IEnumerator ScaleOverTime(HazardBehaviorModel model, Vector3 targetScale)
    {
        Debug.Log($"Scaling");
        Vector3 initialScale = model.gameObject.transform.localScale;
        float elapsedTime = 0f;

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.deltaTime;

            // Calculate progress (normalized between 0 and 1)
            float t = elapsedTime / transitionDuration;

            // Smoothstep creates an organic "ease-in-ease-out" motion 
            // instead of a rigid linear transition
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            model.gameObject.transform.localScale = Vector3.Lerp(initialScale, targetScale, smoothT);

            // Wait until the next frame before continuing the loop
            yield return null;
        }

        // Snap to exact target scale at the end to clean up precision errors
        model.gameObject.transform.localScale = targetScale;
        model.activeScaleCoroutine = null;
    }

    public void Patrolling(NavMeshAgent agent, HazardBehaviorModel model)
    {
        //Debug.Log($"{model.gameObject.name} is patrolling passively");
        // Check if we've reached our destination or don't have one
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            // Find a random point on the NavMesh
            Vector3 randomPoint = GetRandomPointOnNavMesh(model.hazardTerritory.transform.position, 10f);
            agent.SetDestination(randomPoint);
        }
    }

    public void Camping(NavMeshAgent agent, HazardBehaviorModel model)
    {
        //Debug.Log($"{model.gameObject.name} is camping passively");
        // Use the agent to move instead of snapping transform (looks smoother)
        // If you want it instant, keep your old line. If you want it to walk back, use this:
        agent.SetDestination(model.initialHazardPosition);
    }

    // The Attack Sequence Coroutine
    IEnumerator AttackSequence(NavMeshAgent agent, HazardBehaviorModel model)
    {
        Debug.Log($"{model.gameObject.name} is attacking");
        model.isAttacking = true;

        // Cache original speed to restore later
        float originalSpeed = agent.speed;
        float lungeSpeed = originalSpeed * 3f; // Move fast!

        for (int rounds = 0; rounds < 3; rounds++)
        {
            // Lunge AT the player
            agent.speed = lungeSpeed;
            agent.SetDestination(model.player.transform.position);

            // Wait until we are very close or 1 second has passed (timeout)
            float timer = 0f;
            while (Vector3.Distance(transform.position, model.player.transform.position) > 1.5f && timer < 1.0f)
            {
                timer += Time.deltaTime;
                agent.SetDestination(model.player.transform.position); // Keep tracking
                yield return null; // Wait for next frame
            }

            // Back up / Retreat slightly
            Vector3 retreatDir = (transform.position - model.player.transform.position).normalized;
            Vector3 retreatPos = transform.position + (retreatDir * 3.0f); // Back up 3 meters

            agent.speed = originalSpeed; // Back up at normal speed
            agent.SetDestination(retreatPos);

            // Wait for 0.5 seconds while backing up
            yield return new WaitForSeconds(0.5f);
        }

        // Reset state
        agent.speed = originalSpeed;
        model.isAttacking = false;
        resetHazard(agent, model);
    }

    // Standard helper to find a random point on a NavMesh within a radius
    Vector3 GetRandomPointOnNavMesh(Vector3 center, float range)
    {
        Vector3 randomPoint = center + Random.insideUnitSphere * range;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomPoint, out hit, 1.0f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return center; // Fallback
    }
}
