using UnityEngine;

public class HazardTemperature : MonoBehaviour
{
    private FireProfileController fireController;
    private const float MAX_TEMP = 3000f;

    [Range(0f, 1f)] public float temperature = 0.0f;

    private Renderer myRenderer;
    private MaterialPropertyBlock propBlock;

    public float heatUpSpeed = 0.5f; // Controls how fast it turns red (higher = faster)
    public float coolDownSpeed = 0.5f; // Track dynamic cooling as the user sprays

    void Start()
    {
        fireController = GetComponent<FireProfileController>();
        myRenderer = GetComponentInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (fireController != null)
        {
            // Get current temperature and normalize it (0 to 1)
            temperature = Mathf.Clamp01(fireController.currentTemperature / MAX_TEMP);
            myRenderer.GetPropertyBlock(propBlock);
            propBlock.SetFloat("_Temperature", temperature);
            myRenderer.SetPropertyBlock(propBlock);
        }
    }

    // Call this when the simulation is ready for a full reset
    public void ResetTemperature()
    {
        temperature = 0.0f;

        // Force an immediate update to the renderer so it snaps to blue instantly
        if (myRenderer == null) myRenderer = GetComponent<Renderer>();
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        myRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat("_Temperature", 0.0f);
        myRenderer.SetPropertyBlock(propBlock);
    }
}