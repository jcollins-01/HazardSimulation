using UnityEngine;
using UnityEngine.InputSystem;

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
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float horizontalInput = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            horizontalInput -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            horizontalInput += 1f;

        transform.Rotate(Vector3.up, horizontalInput * delay);
    }
}
