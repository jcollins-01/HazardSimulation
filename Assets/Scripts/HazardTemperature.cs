using UnityEngine;

public class HazardTemperature : MonoBehaviour
{
    [Range(0f, 1f)] public float temperature = 0.0f;

    private Renderer myRenderer;
    private MaterialPropertyBlock propBlock;

    private bool isIgnited = false;
    public float heatUpSpeed = 0.5f; // Controls how fast it turns red (higher = faster)

    void Start()
    {
        myRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        myRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat("_Temperature", temperature);
        myRenderer.SetPropertyBlock(propBlock);
    }

    // Call this when the hazard is set to active/catches fire
    public void Ignite()
    {
        isIgnited = true;
    }

    // Call this if the fire gets extinguished
    public void Extinguish()
    {
        isIgnited = false;
    }

    // Call this when the simulation is ready for a full reset
    public void ResetTemperature()
    {
        isIgnited = false;
        temperature = 0.0f;

        // Force an immediate update to the renderer so it snaps to blue instantly
        if (myRenderer == null) myRenderer = GetComponent<Renderer>();
        if (propBlock == null) propBlock = new MaterialPropertyBlock();

        myRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat("_Temperature", 0.0f);
        myRenderer.SetPropertyBlock(propBlock);
    }

    void FixedUpdate()
    {
        // Smoothly blend the temperature over time based on fire state
        if (isIgnited)
        {
            // Gradually climb toward 1 (Hot)
            temperature = Mathf.MoveTowards(temperature, 1.0f, heatUpSpeed * Time.fixedDeltaTime);
        }
        else
        {
            // Gradually cool back down toward 0 (Cold)
            temperature = Mathf.MoveTowards(temperature, 0.0f, heatUpSpeed * Time.fixedDeltaTime);
        }
    }
}