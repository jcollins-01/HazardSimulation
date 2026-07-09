using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Ignis
{
    /// <summary>
    /// With this class you can for example set barrel to explode after 5 seconds
    /// </summary>
    public class FlameTriggerCallbacks : MonoBehaviour
    {
        [Tooltip("How long after the ignition we wait before executing")]
        public float delaySeconds = 5f;

        [Tooltip("Functions to execute")]
        public UnityEvent CallbackFunctions;

        public void TriggerEvents()
        {
            CallbackFunctions.Invoke();
        }
    }
}
