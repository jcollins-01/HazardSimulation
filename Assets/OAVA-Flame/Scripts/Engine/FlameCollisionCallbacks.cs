using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Ignis
{
    /// <summary>
    /// Deprecated. Please implement interface IInteractWithFire interface for better experience. Instructions in user_instructions.pdf -> Interact when an object touches the flame.
    /// </summary>
    public class FlameCollisionCallbacks : MonoBehaviour
    {
        [Tooltip("What gameobjects trigger this event. Can be in any layer or tag.")]
        public List<GameObject> affectedGameObjects;

        [Tooltip("What tags trigger this event. Can be in any layer.")]
        public List<string> affectedTags;

        [Tooltip("Functions to execute")]
        public UnityEvent CallbackFunctions;

        public void TryToTriggerCollisionEvents(GameObject other)
        {
            if(affectedGameObjects.Contains(other) || affectedTags.Contains(other.tag))
                CallbackFunctions.Invoke();
        }

        public void TriggerCollisionEvents()
        {
            CallbackFunctions.Invoke();
        }

        public bool HasCallback(GameObject other)
        {
            if (affectedGameObjects.Contains(other) || affectedTags.Contains(other.tag))
            {
                return true;
            }

            return false;
                
        }
    }
}
