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
        float horizontalInput = 0f;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                horizontalInput -= 1f;

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                horizontalInput += 1f;
        }

        transform.Rotate(Vector3.up, horizontalInput * delay);
    }
}
