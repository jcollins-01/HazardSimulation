using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

/// <summary>
/// Turns off local XR control for remote PUN player instances while leaving the
/// tracked transforms available for Photon-driven updates.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class VRPlayerSetup : MonoBehaviourPun
{
    private void Awake()
    {
        if (photonView.IsMine)
        {
            // Keep the owning player's XR rig unchanged so teleport locomotion
            // can continue using the existing CharacterController-backed setup.
            Debug.Log($"[VRPlayerSetup] Local player rig active for actor {photonView.OwnerActorNr}.");
            return;
        }

        Debug.Log($"[VRPlayerSetup] Disabling local XR control on remote rig owned by actor {photonView.OwnerActorNr}.");
        DisableRemoteRigControl();
    }

    private void DisableRemoteRigControl()
    {
        InputActionManager inputActionManager = GetComponent<InputActionManager>();
        if (inputActionManager != null)
        {
            inputActionManager.enabled = false;
        }

        CharacterController characterController = GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.enabled = false;
        }

        foreach (TrackedPoseDriver trackedPoseDriver in GetComponentsInChildren<TrackedPoseDriver>(true))
        {
            trackedPoseDriver.enabled = false;
        }

        DisableNodeBehaviours("Main Camera");
        DisableNodeBehaviours("Left Controller");
        DisableNodeBehaviours("Right Controller");

        Transform locomotionRoot = FindChildByName(transform, "Locomotion");
        if (locomotionRoot != null)
        {
            locomotionRoot.gameObject.SetActive(false);
        }
    }

    private void DisableNodeBehaviours(string nodeName)
    {
        Transform node = FindChildByName(transform, nodeName);
        if (node == null)
        {
            Debug.LogWarning($"[VRPlayerSetup] Missing tracked node '{nodeName}' on remote player rig.");
            return;
        }

        foreach (Behaviour behaviour in node.GetComponents<Behaviour>())
        {
            if (behaviour is PhotonTransformView)
            {
                continue;
            }

            behaviour.enabled = false;
        }
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        if (root.name == targetName)
        {
            return root;
        }

        foreach (Transform child in root)
        {
            Transform result = FindChildByName(child, targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
