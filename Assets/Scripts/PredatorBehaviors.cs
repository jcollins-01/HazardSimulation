using System.Collections;
using Unity.AI.Navigation;
using UnityEngine.AI; // Required for NavMeshAgent
using UnityEngine;

[RequireComponent(typeof(NavMeshAgent))] // Ensures the component exists
public class PredatorBehaviors : MonoBehaviour
{
    // Hazard controller
    UniversalHazardsController controller;

    // Movement variables
    NavMeshSurface territory;
    NavMeshAgent agent;

    // State tracking
    private bool isAttackingRoutineRunning = false;

    void Start()
    {
        // Grab the controller and the NavMeshAgent
        controller = this.GetComponent<UniversalHazardsController>();
        territory = controller.hazardTerritory;
        agent = GetComponent<NavMeshAgent>();

        // Optional: Set agent settings based on controller if needed
        // agent.speed = 3.5f; 
    }

    // You likely want to call this from Update() so it adjusts constantly
    void Update()
    {

    }

    public void AlertedMovement()
    {
        if (controller.circling)
        {
            Circling();
        }
        else
        {
            Approaching();
        }
    }

    public void PassiveMovement()
    {
        if (controller.camping)
        {
            Camping();
        }
        else
        {
            Patrolling();
        }
    }

    public void Circling()
    {
        // 1. Get direction from player to enemy
        Vector3 directionFromPlayer = transform.position - controller.player.transform.position;
        directionFromPlayer.y = 0; // Keep it flat on the ground

        // 2. Rotate that vector by a small angle (e.g., 10 degrees) to find a point "sideways"
        // This makes the agent constantly aim slightly to the side of where it currently is relative to the player
        float angle = 15f;
        Quaternion rotation = Quaternion.Euler(0, angle, 0);
        Vector3 nextPositionOffset = rotation * directionFromPlayer.normalized * 5.0f; // 5.0f is the circle radius

        // 3. Set destination
        Vector3 circleDest = controller.player.transform.position + nextPositionOffset;
        agent.SetDestination(circleDest);
    }

    public void Approaching()
    {
        // Simply set destination to player
        agent.SetDestination(controller.player.transform.position);

        if (controller.distanceToPlayer < controller.touchingDistance)
        {
            TouchingMovement();
        }
    }

    public void TouchingMovement()
    {
        if (controller.obscuringVision)
        {
            ObscuringVision();
        }
        else
        {
            if (controller.attacking)
            {
                // We only start the attack routine if it isn't already running
                if (!isAttackingRoutineRunning)
                {
                    StartCoroutine(AttackSequence());
                }
            }
            else
            {
                controller.resetHazard();
            }
        }
    }

    public void Patrolling()
    {
        // Check if we've reached our destination or don't have one
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            // Find a random point on the NavMesh
            Vector3 randomPoint = GetRandomPointOnNavMesh(controller.hazardTerritory.transform.position, 10f);
            agent.SetDestination(randomPoint);
        }
    }

    public void Camping()
    {
        // Use the agent to move instead of snapping transform (looks smoother)
        // If you want it instant, keep your old line. If you want it to walk back, use this:
        agent.SetDestination(controller.initialHazardPosition);
    }

    public void ObscuringVision()
    {
        // Calculate a point directly in front of the player's face/camera
        Transform playerTx = controller.player.transform;

        // "forward * 1.0f" puts the point 1 meter in front of the player
        Vector3 targetPoint = playerTx.position + (playerTx.forward * 1.0f);

        agent.SetDestination(targetPoint);

        // Note: You might want a timer here in Update to call resetHazard() 
        // after X seconds of obscuring.
    }

    // --- UTILITIES & COROUTINES ---

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

    // The Attack Sequence Coroutine
    IEnumerator AttackSequence()
    {
        isAttackingRoutineRunning = true;

        // Cache original speed to restore later
        float originalSpeed = agent.speed;
        float lungeSpeed = originalSpeed * 3f; // Move fast!

        for (int rounds = 0; rounds < 3; rounds++)
        {
            // 1. Lunge AT the player
            agent.speed = lungeSpeed;
            agent.SetDestination(controller.player.transform.position);

            // Wait until we are very close or 1 second has passed (timeout)
            float timer = 0f;
            while (Vector3.Distance(transform.position, controller.player.transform.position) > 1.5f && timer < 1.0f)
            {
                timer += Time.deltaTime;
                agent.SetDestination(controller.player.transform.position); // Keep tracking
                yield return null; // Wait for next frame
            }

            // 2. Back up / Retreat slightly
            Vector3 retreatDir = (transform.position - controller.player.transform.position).normalized;
            Vector3 retreatPos = transform.position + (retreatDir * 3.0f); // Back up 3 meters

            agent.speed = originalSpeed; // Back up at normal speed
            agent.SetDestination(retreatPos);

            // Wait for 0.5 seconds while backing up
            yield return new WaitForSeconds(0.5f);
        }

        // Reset state
        agent.speed = originalSpeed;
        isAttackingRoutineRunning = false;
        controller.resetHazard();
    }
}