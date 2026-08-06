using UnityEngine;
using Ignis;
using System.Collections;

/*#if UNITY_EDITOR
using UnityEditor.Events;
#endif*/

// Forces Unity to add these in the Editor automatically once the script is applied
[RequireComponent(typeof(FlammableObject))]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(HazardTemperature))]
[RequireComponent(typeof(NetworkedFireState))]
public class FireProfileController : MonoBehaviour
{
    public enum FireProfile
    {
        MaxResilience,
        InstantExtinguish,
        SlowBurn,
        ClassA, // Ordinary combustibles
        ClassB, // Flammable liquids
        ClassC, // Electrical
        ClassD, // Combustible metals
        ClassK // Grease fires
    }

    [Header("Fire Health")]
    [Tooltip("Maximum fire health/temperature. This is filled automatically by the selected profile.")]
    public int maxTemperature;
    [Tooltip("Current fire health/temperature. Water lowers it and regeneration raises it.")]
    public float currentTemperature;

    [Header("Profile Selection")]
    [Tooltip("Selects the preset controlling ignition, resilience, flame appearance, smoke, and embers.")]
    public FireProfile currentProfile = FireProfile.SlowBurn;

    [Header("Water and Regeneration")]
    [Tooltip("Fire health recovered per second after water has stopped.")]
    public float tempRegenRate; // Degrees recovered per second when not sprayed
    [Tooltip("Fire health removed by each colliding water particle.")]
    public float tempDrainPerParticle; // Degrees lost per single water particle
    [Tooltip("Seconds after the last water hit before fire health starts regenerating.")]
    public float regenDelay;
    // Vars to handle the visual state during extinguish/regeneration
    private float baseFlameLength;
    private float baseVFXMultiplier;
    private float baseParticleSize;

    [Header("Smoldering and Reignition")]
    [Tooltip("The temperature below which the fire enters the uncertainty phase.")]
    public float smolderThreshold = 100f;
    [Tooltip("Chance (0.0 to 1.0) that the fire will reignite when left alone in the smolder zone.")]
    [Range(0f, 1f)]
    public float reigniteChance = 0.5f;
    [Tooltip("How fast the temperature drops to 0 if it fails the reignite roll.")]
    public float autoFizzleRate = 0.01f; // very low fizzle rate
    [HideInInspector] public bool readyForSmolder = false; // flag to keep track of if the system has visually burnt-out or not
    private Coroutine reigniteCoroutine;

    // State trackers for the smolder phase
    private bool hasRolledSmolder = false;
    private bool willReignite = true;
    private float reigniteRegenRate = 1.0f; // a slow regrowth rate 

    [Header("Automatic Component References")]
    [Tooltip("Ignis component controlled by this fire profile. Assigned automatically.")]
    public FlammableObject flammableObject;
    [Tooltip("Local fire volume used by hazard tracking and Ignis VFX placement. Assigned automatically.")]
    public BoxCollider collider; // parent collider that determines ability to be touched/tracked in our hazard system
    private float lastWaterHitTime = 0f;

    // Automatically runs in the Editor when the script is added
    void Reset()
    {
        SetupHitbox();
        /*
#if UNITY_EDITOR
        SetupPersistentFlameEvents();
#endif*/
    }

    void Awake()
    {
        // Grab the reference early during runtime initialization
        flammableObject = GetComponent<FlammableObject>();
        collider = GetComponent<BoxCollider>();
    }

    void Start()
    {
        ApplyProfile();

        // Automatically set up event bindings to allow for a smolder/reignite
        //SetupFlameEvents();
    }

    void Update()
    {
        // Adjust the temperature each frame based on water spray + chance for smolder/reignite if < 100
        CheckSprayAndSmolder();

        // Call a fire scaling function every frame based on temperature
        UpdateFireVisuals();

        // Update the tag
        CheckIfBurning();
    }

    // This allows us to see the profile changes without entering Play Mode whenever we switch enums
    void OnValidate()
    {
        if (flammableObject == null)
            flammableObject = GetComponent<FlammableObject>();

        if (collider == null)
            collider = GetComponent<BoxCollider>();

        if (flammableObject != null)
            ApplyProfile();
    }

    // Creates the child object, assigns the layer, and adds the collider
    private void SetupHitbox()
    {
        // Check if "Hitbox" already exists to prevent duplicates if the component is reset
        Transform existingHitbox = transform.Find("Hitbox");
        if (existingHitbox == null)
        {
            GameObject hitboxObj = new GameObject("Hitbox");
            // Set parent to this object and keep local position/rotation at 0
            hitboxObj.transform.SetParent(this.transform, false);

            hitboxObj.layer = 7; // Set to Layer 7

            BoxCollider newCollider = hitboxObj.AddComponent<BoxCollider>();
            BoxCollider parentCollider = GetComponent<BoxCollider>();

            if (parentCollider != null)
            {
                // Copy the properties directly
                newCollider.center = parentCollider.center;
                newCollider.size = parentCollider.size;
            }
        }
    }

    // Adds the FlameEventInvoker if missing and wires up the UnityEvents
    private void SetupFlameEvents()
    {
        // Get or add the FlameEventInvoker component as instructed by the Ignis API
        FlameEventInvoker eventInvoker = GetComponent<FlameEventInvoker>();
        if (eventInvoker == null)
        {
            eventInvoker = gameObject.AddComponent<FlameEventInvoker>();
        }

        // Subscribe to the events programmatically (remove the listener first to make sure it doesn't get double-added)
        if (eventInvoker != null)
        {
            eventInvoker.Extinguished.RemoveListener(RollForSmolder);
            eventInvoker.Extinguished.AddListener(RollForSmolder);

            eventInvoker.BurntOut.RemoveListener(RollForSmolder);
            eventInvoker.BurntOut.AddListener(RollForSmolder);
        }
    }

    /*
    // This block only compiles in the Unity Editor to physically populate the Inspector slots
#if UNITY_EDITOR
    private void SetupPersistentFlameEvents()
    {
        FlameEventInvoker eventInvoker = GetComponent<FlameEventInvoker>();
        if (eventInvoker == null)
        {
            eventInvoker = gameObject.AddComponent<FlameEventInvoker>();
        }

        // Clear existing to avoid duplicate entries in the Inspector if you click "Reset" multiple times
        UnityEventTools.RemovePersistentListener(eventInvoker.Extinguished, RollForSmolder);
        UnityEventTools.RemovePersistentListener(eventInvoker.BurntOut, RollForSmolder);

        // Add persistent listeners (these WILL show up in the Unity Inspector GUI)
        UnityEventTools.AddPersistentListener(eventInvoker.Extinguished, RollForSmolder);
        UnityEventTools.AddPersistentListener(eventInvoker.BurntOut, RollForSmolder);

        // Tells Unity to save the changes we just made to the Inspector
        UnityEditor.EditorUtility.SetDirty(eventInvoker);
    }
#endif
    */

    // Adds a FlammableObject component if missing and applies the selected profile settings.
    public void ApplyProfile()
    {
        if (flammableObject == null) return;
        if (collider == null) return;

        collider.isTrigger = true; // for the tagging system

        switch (currentProfile)
        {
            case FireProfile.MaxResilience:
                // Our pre-programmed temperature that the fire will reach at max
                // basically, an HP that will reduce slowly as water is sprayed and increase back to max when it's not
                maxTemperature = 3000;
                tempRegenRate = 500f; // Degrees recovered per second when not sprayed
                tempDrainPerParticle = 0.1f; // Degrees lost per single water particle
                regenDelay = 0.1f; // time to start regenerating between water sprays

                // An intense fire that burns for a very long time and is extremely tough to extinguish
                flammableObject.ignitionTime = 2f; // usually 2, 0 for testing
                flammableObject.burnOutStart_s = 9999f; // High value to ensure it burns for a very long time
                // 1 = Whole object needs to be extinguished before the burn out will start
                flammableObject.fullExtinguishToughness = 1f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always; // at any point in its lifecycle, it can reignite, including post-extinguish
                flammableObject.maxSpread = 10000; // as far as possible
                flammableObject.backSpreadCoolDown_s = 0.01f; // short amount of time before it tries to reignite

                // Flame Visuals
                flammableObject.flameLength = 5f; // long lifespan of fire particles going up
                flammableObject.flameVFXMultiplier = 2f;
                flammableObject.flameEnvironmentalSpeed = 2f;
                flammableObject.flameLiveliness = 2f;
                flammableObject.flameParticleSize = 1f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(1f, 1.5f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0f, 0.1f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 5f;
                flammableObject.smokeVFXMultiplier = 2f;
                flammableObject.smokeParticleSize = 2f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 2f;
                flammableObject.embersBurstVFXMultiplier = 2f;
                flammableObject.embersBurstDelayMinMax = new Vector2(0.5f, 6f);
                break;

            case FireProfile.InstantExtinguish:
                maxTemperature = 350;
                tempRegenRate = 50f; // Degrees recovered per second when not sprayed
                tempDrainPerParticle = 10.0f; // Degrees lost per single water particle
                regenDelay = 1.0f; // time to start regenerating between water sprays

                // A smaller fire that will go out very quickly and will be extinguished easily
                flammableObject.ignitionTime = 1f;
                flammableObject.burnOutStart_s = 5f; // Short time before flame starts burning out
                // 0 = A single drop of water will start the burn out in the whole object
                flammableObject.fullExtinguishToughness = 0f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 1; // barely spreads
                flammableObject.backSpreadCoolDown_s = 2f; // short amount of time before it tries to reignite

                // Flame Visuals
                flammableObject.flameLength = 0.5f; // short lifespan = short flames
                flammableObject.flameVFXMultiplier = 0.4f;
                flammableObject.flameEnvironmentalSpeed = 1f;
                flammableObject.flameLiveliness = 1f;
                flammableObject.flameParticleSize = 0.8f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.9f, 1f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0f, 0f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 1f;
                flammableObject.smokeVFXMultiplier = 0.2f;
                flammableObject.smokeParticleSize = 0.5f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 0f;
                flammableObject.embersBurstVFXMultiplier = 0f;
                flammableObject.embersBurstDelayMinMax = new Vector2(5f, 6f);
                break;

            case FireProfile.SlowBurn:
                maxTemperature = 1000;
                tempRegenRate = 250f; // Degrees recovered per second when not sprayed
                tempDrainPerParticle = 0.5f; // Degrees lost per single water particle
                regenDelay = 1.0f; // time to start regenerating between water sprays

                // Takes a while to catch on fire, holds a fire for a while, mid-level difficulty to extinguish
                // High ignition time delays how long it takes for the object to catch fire
                flammableObject.ignitionTime = 15f;
                flammableObject.burnOutStart_s = 120f;
                // Mid-level difficulty to fully extinguish
                flammableObject.fullExtinguishToughness = 0.5f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 20f; // cap spread to a contained 20 m area
                flammableObject.backSpreadCoolDown_s = 5f; // some amount of time before it tries to reignite/can be reignited
                flammableObject.fireCrawlSpeed = 0.05f;

                // Flame Visuals
                flammableObject.flameLength = 2f; // avg lifespan
                flammableObject.flameVFXMultiplier = 1f;
                flammableObject.flameEnvironmentalSpeed = 1f;
                flammableObject.flameLiveliness = 1f;
                flammableObject.flameParticleSize = 1f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.9f, 1.0f); // was 0.9, 1.0  0.05f, 0.1f
                flammableObject.flameBurstDelayMinMax = new Vector2(0f, 0f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 2.5f;
                flammableObject.smokeVFXMultiplier = 1f;
                flammableObject.smokeParticleSize = 1f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 1f;
                flammableObject.embersBurstVFXMultiplier = 1f;
                flammableObject.embersBurstDelayMinMax = new Vector2(2f, 4f);
                break;

            case FireProfile.ClassA:
                // Class A: Wood, paper, fabric. 
                // Mid-level heat, takes time to ignite, extinguishes reliably with water.
                maxTemperature = 800;
                tempRegenRate = 100f;
                tempDrainPerParticle = 5.0f; // Water is highly effective
                regenDelay = 1.5f;

                flammableObject.ignitionTime = 15f; // Needs sustained heat to catch
                flammableObject.burnOutStart_s = 60f; // Burns steadily for a moderate time
                flammableObject.fullExtinguishToughness = 0.5f; // Mid-level difficulty to extinguish the whole primitive
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always; // allow it to reignite if still touching other burning objects
                flammableObject.maxSpread = 1000; // all of these fires should spread as far as possible, given the chance
                flammableObject.backSpreadCoolDown_s = 4f;
                flammableObject.fireCrawlSpeed = 0.2f;

                // Visuals: Standard flames, moderate smoke, leaves embers
                // Flame Visuals
                flammableObject.flameLength = 1.5f;
                flammableObject.flameVFXMultiplier = 1f;
                flammableObject.flameEnvironmentalSpeed = 1f;
                flammableObject.flameLiveliness = 1f;
                flammableObject.flameParticleSize = 1f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.8f, 1.2f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0f, 0f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 2f;
                flammableObject.smokeVFXMultiplier = 1f;
                flammableObject.smokeParticleSize = 1f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 1.5f;
                flammableObject.embersBurstVFXMultiplier = 1.5f;
                flammableObject.embersBurstDelayMinMax = new Vector2(2f, 5f);
                break;

            case FireProfile.ClassB:
                // Class B: Flammable liquids (gasoline, oil). 
                // Ignites instantly, burns very hot, water is highly ineffective (and often dangerous).
                maxTemperature = 1500;
                tempRegenRate = 400f; // Vapors reignite very quickly if not completely smothered
                tempDrainPerParticle = 0.1f; // Water barely dents the temperature
                regenDelay = 0.2f;

                flammableObject.ignitionTime = 5f; // Ignites immediately upon contact with trigger
                flammableObject.burnOutStart_s = 45f;
                flammableObject.fullExtinguishToughness = 1f; // The entire object must be smothered
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 1000; // Liquids spread rapidly across the primitive's surface
                flammableObject.backSpreadCoolDown_s = 0.5f;

                // Visuals: Lively, erratic flames with heavy, dark smoke
                // Flame Visuals
                flammableObject.flameLength = 2.5f;
                flammableObject.flameVFXMultiplier = 1.5f;
                flammableObject.flameEnvironmentalSpeed = 1.5f;
                flammableObject.flameLiveliness = 2.5f; // Highly erratic simulated airflow
                flammableObject.flameParticleSize = 1.2f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(1f, 1.5f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0.1f, 0.3f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 4f; // Thick smoke
                flammableObject.smokeVFXMultiplier = 2f;
                flammableObject.smokeParticleSize = 1.5f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 0.5f; // Liquids produce fewer physical embers
                flammableObject.embersBurstVFXMultiplier = 0.5f;
                flammableObject.embersBurstDelayMinMax = new Vector2(4f, 8f);
                break;

            case FireProfile.ClassC:
                // Class C: Electrical fires. 
                // Fast ignition, persistent heat source (simulating live current), heavy sparking.
                maxTemperature = 1200;
                tempRegenRate = 600f; // Extremely fast regen simulating uninterrupted electrical power
                tempDrainPerParticle = 0.5f;
                regenDelay = 0.05f; // Reheats almost immediately when water spray stops

                flammableObject.ignitionTime = 10f;
                flammableObject.burnOutStart_s = 120f; // Persistent as long as "power" is simulated
                flammableObject.fullExtinguishToughness = 0.8f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 5; // Generally contained to the electrical component itself
                flammableObject.backSpreadCoolDown_s = 1f;

                // Visuals: Lower flame profile but extreme ember/spark bursts
                // Flame Visuals
                flammableObject.flameLength = 1f;
                flammableObject.flameVFXMultiplier = 0.8f;
                flammableObject.flameEnvironmentalSpeed = 1f;
                flammableObject.flameLiveliness = 1.5f;
                flammableObject.flameParticleSize = 0.8f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.5f, 0.8f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0.2f, 0.6f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 1.5f;
                flammableObject.smokeVFXMultiplier = 1f;
                flammableObject.smokeParticleSize = 0.8f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 3f; // Heavy sparking
                flammableObject.embersBurstVFXMultiplier = 4f; // Large, erratic spark bursts
                flammableObject.embersBurstDelayMinMax = new Vector2(0.1f, 1.5f);
                break;

            case FireProfile.ClassD:
                // Class D: Combustible metals (Magnesium, Lithium). 
                // Requires high heat to ignite, burns at extreme temperatures, water makes it violently worse.
                maxTemperature = 3000;
                tempRegenRate = 1000f; // Nearly impossible to cool down once ignited
                tempDrainPerParticle = 0.01f; // Water is completely ineffective
                regenDelay = 0.0f;
                flammableObject.flameCatchAreaAddition = new Vector3(1f, 1f, 1f);

                flammableObject.ignitionTime = 30f; // Requires a lot of energy/time to initially catch
                flammableObject.burnOutStart_s = 300f; // Burns for a very long time
                flammableObject.fullExtinguishToughness = 1f; // Must be entirely smothered by a specialized agent
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 500; // Tends to burn intensely in one spot rather than spreading far
                flammableObject.backSpreadCoolDown_s = 0.1f;

                // Visuals: Intense, bright, localized fire. Low smoke, massive slag/ember ejection.
                // Flame Visuals
                flammableObject.flameLength = 1.2f;
                flammableObject.flameVFXMultiplier = 2f;
                flammableObject.flameEnvironmentalSpeed = 0.5f; // Doesn't draft upward as much, burns heavy
                flammableObject.flameLiveliness = 0.2f; // Very steady, intense simulation of airflow
                flammableObject.flameParticleSize = 1.5f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(1.2f, 1.5f); // 0.2, 0.5
                flammableObject.flameBurstDelayMinMax = new Vector2(0f, 0f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 0.5f; // Metal fires often produce bright light but thinner white smoke
                flammableObject.smokeVFXMultiplier = 0.5f;
                flammableObject.smokeParticleSize = 1f;

                // Embers Visuals
                flammableObject.embersVFXMultiplier = 4f; // Extreme slag and particle ejection
                flammableObject.embersBurstVFXMultiplier = 5f;
                flammableObject.embersBurstDelayMinMax = new Vector2(0.5f, 2f);
                break;

            case FireProfile.ClassK:
                // Class K: Cooking oils, grease, and animal fats (large grease fires). 
                // Ignites after sustained heat, burns extremely hot, and produces thick dark smoke, while water is ineffective.
                maxTemperature = 1500;
                tempRegenRate = 500f; // Bounces back aggressively if not fully smothered
                tempDrainPerParticle = 0.5f; // Water barely does anything to grease
                regenDelay = 0.2f;

                flammableObject.ignitionTime = 5f; // Oil needs to boil/reach auto-ignition temp first
                flammableObject.burnOutStart_s = 180f; // Large amount of grease fuels the fire for a long time
                flammableObject.fullExtinguishToughness = 1f; // Must be smothered or hit with chemical agent
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 3; // Splatters and spreads nearby, but doesn't run across the floor like gasoline
                flammableObject.backSpreadCoolDown_s = 2f;
                flammableObject.fireCrawlSpeed = 0.05f; // Crawls slowly, forcing the danger to remain localized to the kitchen

                // Visuals: Angry, erratic flames, choking dark smoke, and popping oil splatter.
                // Flame Visuals
                flammableObject.flameLength = 1.0f; // Large and scary, but short enough to not instantly clip ceilings
                flammableObject.flameVFXMultiplier = 1.5f;
                flammableObject.flameEnvironmentalSpeed = 1.2f;
                flammableObject.flameLiveliness = 2.0f; // Very aggressive and erratic movement
                flammableObject.flameParticleSize = 1.0f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.8f, 1.5f);
                flammableObject.flameBurstDelayMinMax = new Vector2(0.2f, 0.4f);

                // Smoke Visuals
                flammableObject.smokeAlpha = 4.0f; // Extremely thick, heavy smoke characteristic of burning fat
                flammableObject.smokeVFXMultiplier = 2.0f;
                flammableObject.smokeParticleSize = 1.5f;

                // Embers Visuals (Simulates popping, spitting grease)
                flammableObject.embersVFXMultiplier = 2.0f;
                flammableObject.embersBurstVFXMultiplier = 3.0f; // Sharp bursts representing grease pockets popping
                flammableObject.embersBurstDelayMinMax = new Vector2(1f, 3f);
                break;
        }

        // Initialize current temperature to the max allowed by the profile
        currentTemperature = maxTemperature;

        // Cache the base visual settings right after the switch statement determines them
        baseFlameLength = flammableObject.flameLength;
        baseVFXMultiplier = flammableObject.flameVFXMultiplier;
        baseParticleSize = flammableObject.flameParticleSize;

        //Debug.Log($"Applied {currentProfile} profile to {gameObject.name}");
    }

    public void RollForSmolder()
    {
        Debug.Log("Fire visually extinguished by Ignis. Rolling for smolder.");
        readyForSmolder = true;
    }

    private void CheckSprayAndSmolder()
    {
        // Determine if we are in the smolder zone
        bool inSmolderZone = (currentTemperature > 0 && currentTemperature <= smolderThreshold);
        bool isSprayedRecently = (Time.time - lastWaterHitTime <= regenDelay);

        //Debug.Log($"Current Temp: {currentTemperature}, InSmolderZone: {inSmolderZone}, Sprayed: {isSprayedRecently}");

        if (inSmolderZone && !isSprayedRecently)
        {
            if (readyForSmolder)
            {
                // We are in the uncertainty zone. Roll the dice once!
                if (!hasRolledSmolder)
                {
                    hasRolledSmolder = true;
                    willReignite = Random.value <= reigniteChance;
                    Debug.Log("Rolled to reignite");
                }

                // Execute the result of the roll
                if (willReignite)
                {
                    // Stop any existing coroutine if already running
                    if (reigniteCoroutine != null) StopCoroutine(reigniteCoroutine);
                    reigniteCoroutine = StartCoroutine(ReigniteRoutine());

                    //currentTemperature += reigniteRegenRate * Time.deltaTime;
                    //flammableObject.TryToSetOnFire(transform.position, reigniteRegenRate);
                }
                else
                {
                    // Artificially drain the fire to 0 to simulate it dying out on its own
                    currentTemperature -= autoFizzleRate * Time.deltaTime;
                    if (currentTemperature < 0) currentTemperature = 0;
                    Debug.Log("Slowly drain temperature");
                }
            }
        }
        // ONLY if we are NOT in the smolder zone do we allow normal regen
        else if (!isSprayedRecently && currentTemperature < maxTemperature)
        {
            currentTemperature += tempRegenRate * Time.deltaTime;

            // Reset flags only when we successfully climb out of the smolder zone
            hasRolledSmolder = false;
            readyForSmolder = false;
        }

        // Clamp it back to max and min
        if (currentTemperature > maxTemperature)
            currentTemperature = maxTemperature;

        if (currentTemperature < 0)
            currentTemperature = 0;
    }

    private IEnumerator ReigniteRoutine()
    {
        // Wait for a random amount of time between 10 and 30 seconds
        float waitTime = Random.Range(10f, 30f);
        yield return new WaitForSeconds(waitTime);
        Debug.Log("Done waiting should reignite");
        // Only proceed if the temperature is still above 0
        if (currentTemperature > 0)
        {
            Debug.Log("Reigniting");
            currentTemperature += reigniteRegenRate * Time.deltaTime;
            flammableObject.TryToSetOnFire(transform.position, reigniteRegenRate);
        }
    }

    public float ProcessWaterHit(int particleCount)
    {
        lastWaterHitTime = Time.time;
        currentTemperature -= (particleCount * tempDrainPerParticle);

        // Reset the smolder roll if the player starts spraying again while the fire is currently deciding its fate
        hasRolledSmolder = false;

        if (currentTemperature == 0)
        {
            currentTemperature = 0; // lock it to 0 so it doesn't increase again after being extinguished
            return 1.0f; // still full power
        }
        else if (currentTemperature <= 100)
        {
            //Debug.Log("Spraying at full power!");
            return 1.0f; // Grant the water 100% of its extinguish power.
        }

        //Debug.Log("Spraying at reduced power to artificially maintain fire life.");
        // HP > 100. Nerf the extinguish power so the fire visually shrinks but doesn't easily die.
        return 0.02f;
    }

    // Dynamically scale fire visuals based on fire health/temperature
    private void UpdateFireVisuals()
    {
        if (flammableObject == null || maxTemperature == 0) return;
        //Debug.Log("Looking at fire visuals");
        // Calculate the percentage of temperature remaining (0.0f to 1.0f)
        float tempRatio = currentTemperature / maxTemperature;

        // Clamp this so the fire doesn't become COMPLETELY microscopic 
        // before hitting the 100-degree extinguish threshold
        float visualRatio = Mathf.Max(tempRatio, 0.5f);

        // Apply the ratio to the current visual parameters
        flammableObject.flameLength = baseFlameLength * visualRatio;
        flammableObject.flameVFXMultiplier = baseVFXMultiplier * visualRatio;
        flammableObject.flameParticleSize = baseParticleSize * visualRatio;
    }

    // Check if this object is on fire, update hazard tag in hazard layer if so
    private void CheckIfBurning()
    {
        if (flammableObject == null) return;

        // Setting the tag to Active Hazard
        if (flammableObject.onFire)
        {
            // This helps us catch any object that catches on fire without labeling all walls, ceilings, floors etc.
            // as possible hazards that can be set as fire sources
            flammableObject.gameObject.tag = "Active Hazard";
        }
    }
}
