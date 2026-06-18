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

    // Managing model activation
    public bool active;

    // Constant hazard attributes
    private Vector3 currentHazardPosition;
    public Vector3 initialHazardPosition;
    public float distanceToPlayer;
    public bool alreadyTriggeredByPlayer;

    // States for hazards
    public bool isAttacking = false;

    public Vector3 originalScale;
    [HideInInspector] public Coroutine activeScaleCoroutine;

    public bool isTouchingSomething = false;
    public GameObject lastTouchedObject;
    [SerializeField] private float spreadRate = 1.5f; // Spawns a new flame every 1.5 seconds, maybe set it as a range that leans higher when aggression is high

    // Constant player attributes
    private Vector3 currentPlayerPosition;

    // Hazard behavioral vars
    public int aggressionLevel;
    public int cautionLevel;
    public int speedLevel;

    public int noticeDistance;
    public int touchingDistance;

    public bool patrolling;
    public bool approaching;
    public bool attacking;
    public bool looming;
    public bool spreading;

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

        agent.Warp(transform.position);
        // Set basic agent variables
        agent.speed = speedLevel;

        if (!gameObject.TryGetComponent<Rigidbody>(out Rigidbody rb))
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (gameObject.tag == "Active Hazard") // If the object was set as active via HazardTagging / manual testing
        {
            // Called from the start - always need to be checking where the hazard is compared to the player
            if (agent != null)
            {
                determineDistanceToPlayer();
                checkIfTriggered();
            }
        }
    }

    // Get all the behavior variables passed from the controller
    public void Initialize(
        UniversalHazardController control, GameObject play,
        int aggression, int caution, int speed, int notice, int touching, 
        bool isPatrolling, bool isApproaching, bool isAttacking, bool isLooming, bool isSpreading,
        int escape, int timeToLose, NavMeshSurface territory)
    {
        controller = control;
        player = play;
        aggressionLevel = aggression;
        cautionLevel = caution;
        speedLevel = speed;
        noticeDistance = notice;
        touchingDistance = touching;
        escapeNoticeDistance = escape;
        patrolling = isPatrolling;
        approaching = isApproaching;
        attacking = isAttacking;
        looming = isLooming;
        spreading = isSpreading;
        timeToLoseInterestOrEffect = timeToLose;
        hazardTerritory = territory;
    }

    private void determineDistanceToPlayer()
    {
        currentHazardPosition = this.gameObject.transform.position;
        currentPlayerPosition = player.transform.position;
        distanceToPlayer = Vector3.Distance(currentHazardPosition, currentPlayerPosition);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only interact with other objects that are possible hazards
        if (other.CompareTag("Possible Hazard"))
        {
            isTouchingSomething = true;
            lastTouchedObject = other.gameObject;
            Debug.Log($"Hazard is touching: {lastTouchedObject.name}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject == lastTouchedObject)
        {
            isTouchingSomething = false;
            lastTouchedObject = null;
        }
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
        else if (isTouchingSomething) // if we're touching another object and spreading is active
        {
            // If the hazard is capable of spreading
            if (spreading)
            {
                if (lastTouchedObject != null)
                {
                    controller.Spreading(this); 
                }
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
