using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;

public class UniversalHazardsController : MonoBehaviour
{
    // This script is meant to be added to each hazard whose behavior must be dynamic
    
    [Header("Impacted GameObjects")] // Possibly add more than the player in the future...
    public GameObject player;

    [Header("HAZARD ATTRIBUTES")]
    [Space(10)]

    [Header("Hazard Types")]
    public bool isPredator;
    public bool isEnvironment;

    [Header("Inciting Values")] // How likely are specific behaviors to happen
    [Range(0, 10)] public int aggressionLevel;
    [Range(0, 10)] public int cautionLevel;

    [Header("Trigger Values")] // What triggers specific behaviors
    [ShowIf("isPredator")] public int noticeDistance;
    public int touchingDistance;
    

    [Header("Behavior Bools")] // Is a specific behavior present or not
    [ShowIf("isPredator")] public bool patrolling;
    [ShowIf("isPredator")] public bool camping;
    [ShowIf("isPredator")] public bool circling;
    [ShowIf("isPredator")] public bool approaching;
    public bool obscuringVision;
    [ShowIf("isPredator")] public bool attacking;

    [Header("Hazard Boundaries")] // What limits or ends the hazard's behaviors
    [ShowIf("isPredator")] public int escapeNoticeDistance;
    public int timeToLoseInterestOrEffect;
    public NavMeshSurface hazardTerritory;

    // Constant hazard attributes
    private Vector3 currentHazardPosition;
    public Vector3 initialHazardPosition;
    public float distanceToPlayer;
    private bool alreadyTriggeredByPlayer;
    private bool approachingPlayer;

    // Constant player attributes
    private Vector3 currentPlayerPosition;

    // Possible behavior group scripts
    private PredatorBehaviors predator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Get hazard starting attributes
        initialHazardPosition = this.gameObject.transform.position;
        alreadyTriggeredByPlayer = false;

        // Determine hazard type and create an instance of the appropriate behaviors
        if (isPredator)
            predator = this.gameObject.AddComponent<PredatorBehaviors>();

        // Start this coroutine from the time a hazard notices or engages with the player
        StartCoroutine(countdownToResetHazard());
    }

    private IEnumerator countdownToResetHazard()
    {
        yield return new WaitForSeconds(timeToLoseInterestOrEffect);
    }

    public void resetHazard()
    {
        // Resets hazard in one way or another
        if (predator != null)
        {
            // Do a passive behavior
            predator.PassiveMovement();
            alreadyTriggeredByPlayer = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Called from the start - always need to be checking where the hazard is compared to the player
        determineDistanceToPlayer();
        checkIfTriggered();
    }

    private void determineDistanceToPlayer()
    {
        currentHazardPosition = this.gameObject.transform.position;
        currentPlayerPosition = player.transform.position;
        distanceToPlayer = Vector3.Distance(currentHazardPosition, currentPlayerPosition);
    }

    private void checkIfTriggered()
    {
        if (predator != null)
        {
            // --- ALERTED STATE ---
            if (distanceToPlayer < noticeDistance)
            {
                // FIX 2: We call movement EVERY FRAME so the agent follows the moving player.
                // We pass the triggering logic to the movement script.
                approachingPlayer = true;
                predator.AlertedMovement();

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
                predator.PassiveMovement();
            }
        }
    }
}
