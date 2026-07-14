using UnityEngine;
using Ignis;

// Forces Unity to add these in the Editor automatically once the script is applied
[RequireComponent(typeof(FlammableObject))]
[RequireComponent(typeof(BoxCollider))]
public class FireProfileController : MonoBehaviour
{
    public enum FireProfile
    {
        MaxResilience,
        InstantExtinguish,
        SlowBurn
    }

    [Tooltip("Select the desired fire behavior profile.")]
    public FireProfile currentProfile = FireProfile.SlowBurn;
    private FlammableObject flammableObject;
    private BoxCollider collider; // parent collider that determines ability to be touched/tracked in our hazard system
    private BoxCollider fireCollider; // child collider that is used in extinguishing system

    // Automatically runs in the Editor when the script is added
    void Reset()
    {
        SetupHitbox();
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

            fireCollider = newCollider;
        }
    }

    // Adds a FlammableObject component if missing and applies the selected profile settings.
    public void ApplyProfile()
    {
        if (flammableObject == null) return;
        if (collider == null) return;

        collider.isTrigger = true; // for the tagging system

        switch (currentProfile)
        {
            case FireProfile.MaxResilience:
                // An intense fire that burns for a very long time and is extremely tough to extinguish
                flammableObject.ignitionTime = 0f; // usually 2, 0 for testing
                flammableObject.burnOutStart_s = 9999f; // High value to ensure it burns for a very long time
                // 1 = Whole object needs to be extinguished before the burn out will start
                flammableObject.fullExtinguishToughness = 1f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always; // at any point in its lifecycle, it can reignite, including post-extinguish
                flammableObject.maxSpread = 10000; // as far as possible
                flammableObject.backSpreadCoolDown_s = 2f; // short amount of time before it tries to reignite

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
                // A smaller fire that will go out very quickly and will be extinguished easily
                flammableObject.ignitionTime = 0f; // usually 1f
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
                // Takes a while to catch on fire, holds a fire for a while, mid-level difficulty to extinguish
                // High ignition time delays how long it takes for the object to catch fire
                flammableObject.ignitionTime = 0f; // usually 15f;
                flammableObject.burnOutStart_s = 120f;
                // Mid-level difficulty to fully extinguish
                flammableObject.fullExtinguishToughness = 0.5f;
                flammableObject.isReignitable = FlammableObject.ReIgnitable.Always;
                flammableObject.maxSpread = 5; // controlled area of spread, doesn't move as much
                flammableObject.backSpreadCoolDown_s = 5f; // some amount of time before it tries to reignite/can be reignited

                // Flame Visuals
                flammableObject.flameLength = 2f; // avg lifespan
                flammableObject.flameVFXMultiplier = 1f;
                flammableObject.flameEnvironmentalSpeed = 1f;
                flammableObject.flameLiveliness = 1f;
                flammableObject.flameParticleSize = 1f;
                flammableObject.flameAreaNoiseMinMaxMultiplier = new Vector2(0.9f, 1.0f);
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
        }

        Debug.Log($"Applied {currentProfile} profile to {gameObject.name}");
    }
}
