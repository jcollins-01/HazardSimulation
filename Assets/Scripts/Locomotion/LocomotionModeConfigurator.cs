using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Restricts the XR Origin (XR Rig) to a single locomotion technique so each locomotion
/// study scene only offers the technique it is testing.
///
/// Turning is always physical: every snap/continuous turn provider is disabled.
///  - <see cref="Mode.Natural"/>: every locomotion provider and every teleport ray is
///    disabled, so the only way to move is to physically walk around the play space.
///  - <see cref="Mode.Teleport"/>: only the <see cref="TeleportationProvider"/> stays on.
///    Controllers keep their thumbstick teleport ray, and hands use
///    <see cref="HandTeleportGesture"/>.
///
/// Components are disabled rather than GameObjects deactivated, because the starter rig's
/// ControllerInputActionManager re-activates the teleport GameObjects on its own.
/// Put this on the XR Origin root. It is applied in Awake and can also be applied from the
/// editor (the locomotion scene builder does this so the saved scene matches play mode).
/// </summary>
[DefaultExecutionOrder(-200)]
public class LocomotionModeConfigurator : MonoBehaviour
{
    public enum Mode { Natural, Teleport }

    [SerializeField] private Mode mode = Mode.Natural;

    public Mode CurrentMode => mode;

    private void Awake() => Apply(mode);

    public void Apply(Mode newMode)
    {
        mode = newMode;
        bool teleport = mode == Mode.Teleport;
        int teleportLayer = InteractionLayerMask.GetMask("Teleport");

        foreach (var provider in GetComponentsInChildren<LocomotionProvider>(true))
            provider.enabled = teleport && provider is TeleportationProvider;

        foreach (var climbTeleport in GetComponentsInChildren<ClimbTeleportInteractor>(true))
            climbTeleport.enabled = false;

        // Controller teleport rays only target the Teleport interaction layer; leave any other ray (UI, far grab) alone.
        // The line visual is toggled too, otherwise it keeps drawing a straight ray from a disabled interactor.
        foreach (var ray in GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (ray.GetComponentInParent<HandTeleportGesture>(true) != null)
                continue;
            bool isTeleportRay = teleportLayer != 0
                ? ray.interactionLayers.value == teleportLayer
                : ray.name.Contains("Teleport");
            if (!isTeleportRay)
                continue;

            ray.enabled = teleport;
            if (ray.TryGetComponent(out UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual lineVisual))
                lineVisual.enabled = teleport;
            if (ray.TryGetComponent(out LineRenderer lineRenderer))
                lineRenderer.enabled = teleport;
        }

        foreach (var gesture in GetComponentsInChildren<HandTeleportGesture>(true))
            gesture.enabled = teleport;
    }
}
