using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Ignis
{
    public class FlameEventInvoker : MonoBehaviour
    {
        public bool maxBrightnessReachedInvoked = false;

        public bool burnOutStartedInvoked = false;

        // Called when flame is being extinguished currently
        public UnityEvent BeingExtinguished;

        // Called when object has been extinguished and is currently being burnt out
        public UnityEvent Extinguished;

        // Called when object has been burnt out
        public UnityEvent BurntOut;

        // Called When object is ignited.
        public UnityEvent Ignited;

        // Called When Max Brightness is reached. onFireTimer > achieveMaxBrightness_s
        public UnityEvent MaxBrightnessReached;

        // Called When Burn out has started. onFireTimer > burnOutStart_s
        public UnityEvent BurnOutStarted;

        /// <summary>
        /// Invokes MaxBrightnessReached only once per Init. Make maxBrightnessReachedInvoked = false to invoke again.
        /// </summary>
        public void InvokeMaxBrightnessReachedIfNotAlreadyInvoked()
        {
            if (!maxBrightnessReachedInvoked)
            {
                MaxBrightnessReached.Invoke();
                maxBrightnessReachedInvoked = true;
            }
        }

        /// <summary>
        /// Invokes BurnOutStarted only once per Init. Make burnOutStartedInvoked = false to invoke again.
        /// </summary>
        public void InvokeBurnOutStartedIfNotAlreadyInvoked()
        {
            if (!burnOutStartedInvoked)
            {
                BurnOutStarted.Invoke();
                burnOutStartedInvoked = true;
            }
        }
    }

}

