using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class SceneSwitcher : MonoBehaviour
{
    private XRSceneSwitchActions controls;

    [SerializeField]
    private string shipsScene = "Ships";

    [SerializeField]
    private string boilerRoomScene = "Boiler Room";

    private static SceneSwitcher instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        controls = new XRSceneSwitchActions();
    }

    private void OnEnable()
    {
        controls.Enable();

        controls.SceneControl.NextScene.performed += OnNextScene;
        controls.SceneControl.PreviousScene.performed += OnPreviousScene;
    }

    private void OnDisable()
    {
        controls.SceneControl.NextScene.performed -= OnNextScene;
        controls.SceneControl.PreviousScene.performed -= OnPreviousScene;

        controls.Disable();
    }

    private void OnNextScene(InputAction.CallbackContext context)
    {
        if (SceneManager.GetActiveScene().name == shipsScene)
        {
            SceneManager.LoadScene(boilerRoomScene);
        }
    }

    private void OnPreviousScene(InputAction.CallbackContext context)
    {
        if (SceneManager.GetActiveScene().name == boilerRoomScene)
        {
            SceneManager.LoadScene(shipsScene);
        }
    }
}
