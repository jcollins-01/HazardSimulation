using UnityEngine;

public class HazardTemperature : MonoBehaviour
{
    [Range(0f, 1f)] public float temperature = 1.0f; // 0 = cold, 1 = hot
    private Renderer myRenderer;
    private MaterialPropertyBlock propBlock;

    void Start()
    {
        myRenderer = GetComponent<Renderer>();
        propBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        // Continuously update the property block with the current heat level
        myRenderer.GetPropertyBlock(propBlock);
        propBlock.SetFloat("_Temperature", temperature);
        myRenderer.SetPropertyBlock(propBlock);
    }
}