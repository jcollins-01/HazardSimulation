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

    // States for hazards
    public bool isAttacking = false;
    public Vector3 originalScale;
    [HideInInspector] public Coroutine activeScaleCoroutine;

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
            // Pass movement updates to the controller
            controller.AlertedMovement(agent, this);

            // Triggers that only happen ONCE when first entering range
            if (!alreadyTriggeredByPlayer)
            {
                Debug.Log("Player entered hazard threat zone - activating.");
                alreadyTriggeredByPlayer = true;

                // Trigger the scaling/looming once here instead of continuously in Update
                if (looming)
                {
                    controller.Looming(agent, this);
                }

                // Start the "lose interest" timer safely
                StopAllCoroutines();
                StartCoroutine(countdownToResetHazard());
            }
        }
        // --- PASSIVE STATE ---
        else
        {
            // Only trigger the reset transition once when the player moves out of range
            if (alreadyTriggeredByPlayer)
            {
                Debug.Log("Player left hazard threat zone - returning to passive state.");
                alreadyTriggeredByPlayer = false;
                StopAllCoroutines(); // Stops the countdown timer safely
                controller.resetHazard(agent, this);
            }
            else
            {
                // Keep running normal passive idle behaviors if the player is absent
                controller.PassiveMovement(agent, this);
            }
        }
    }

    private IEnumerator countdownToResetHazard()
    {
        yield return new WaitForSeconds(timeToLoseInterestOrEffect);
        controller.resetHazard(agent, this);
    }
}
