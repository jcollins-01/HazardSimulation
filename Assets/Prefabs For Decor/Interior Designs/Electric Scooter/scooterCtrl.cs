using UnityEngine;

public class scooterCtrl : MonoBehaviour
{
    float delay = 1.0f;
    private void Start()
    {
        delay = Random.Range(0.0f, 1.0f);
    }

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(Vector3.up, Input.GetAxis("Horizontal") * delay);
    }
}
