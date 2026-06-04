using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class HazardBehaviorModel : MonoBehaviour
{
    // Core hazard components
    NavMeshAgent agent;
    public GameObject player;
    public UniversalHazardController controller;

    // Constant hazard attributes
    private Vector3 currentHazardPosition;
    public Vector3 initialHazardPosition;
    public float distanceToPlayer;
    public bool alreadyTriggeredByPlayer;
    public bool isAttacking = false;
    public Vector3 originalScale;

    // Constant player attributes
    private Vector3 currentPlayerPosition;

    // Hazard behavioral vars
    public int aggressionLevel;
    public int cautionLevel;

    public int noticeDistance;
    public int touchingDistance;

    public bool patrolling;
    public bool approaching;
    public bool attacking;
    public bool looming;

    public int escapeNoticeDistance;
    public int timeToLoseInterestOrEffect;
    public NavMeshSurface hazardTerritory;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Get hazard starting attributes
        initialHazardPosition = gameObject.transform.position;
        alreadyTriggeredByPlayer = false;
        originalScale = gameObject.transform.localScale;

        if (!gameObject.TryGetComponent<NavMeshAgent>(out NavMeshAgent ag))
            agent = gameObject.AddComponent<NavMeshAgent>();
        else
            agent = ag;
    }

    // Update is called once per frame
    void Update()
    {
        // Called from the start - always need to be checking where the hazard is compared to the player
        determineDistanceToPlayer();
        checkIfTriggered();
    }

    // Get all the behavior variables passed from the controller
    public void Initialize(
        UniversalHazardController control, GameObject play,
        int aggression, int caution, int notice, int touching, 
        bool isPatrolling, bool isApproaching, bool isAttacking, bool isLooming,
        int escape, int timeToLose, NavMeshSurface territory)
    {
        controller = control;
        player = play;
        aggressionLevel = aggression;
        cautionLevel = caution;
        noticeDistance = notice;
        touchingDistance = touching;
        escapeNoticeDistance = escape;
        patrolling = isPatrolling;
        approaching = isApproaching;
        attacking = isAttacking;
        looming = isLooming;
        timeToLoseInterestOrEffect = timeToLose;
        hazardTerritory = territory;
    }

    private void determineDistanceToPlayer()
    {
        currentHazardPosition = this.gameObject.transform.position;
        currentPlayerPosition = player.transform.position;
        distanceToPlayer = Vector3.Distance(currentHazardPosition, currentPlayerPosition);
    }

    private void checkIfTriggered()
    {
        // --- ALERTED STATE ---
        if (distanceToPlayer < noticeDistance)
        {
            Debug.Log("Player moved close to hazard - acting active");
            // We pass the triggering logic to the movement script.
            controller.AlertedMovement(agent, this); // pass our attached agent and this model instance with its defined vars

            // Triggers that only happen ONCE (like playing a sound or starting a timer) go here
            if (!alreadyTriggeredByPlayer)
            {
                alreadyTriggeredByPlayer = true;
                // Start the "lose interest" timer only when first spotted
                StopAllCoroutines(); // Safety check
                StartCoroutine(countdownToResetHazard());
            }
        }
        // --- PASSIVE STATE ---
        else
        {
            // If the player is far away, go back to patrolling
            controller.PassiveMovement(agent, this);
            Debug.Log("Player is far from hazard - acting passive");
        }
    }

    private IEnumerator countdownToResetHazard()
    {
        yield return new WaitForSeconds(timeToLoseInterestOrEffect);
        controller.resetHazard(agent, this);
    }
}
