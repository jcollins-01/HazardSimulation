#if OAVA_IGNIS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class ParticleExtinguish : MonoBehaviour
    {
        [Tooltip("Select the desired fire profiles these particles can extinquish")]
        public FireProfileController.FireProfile currentProfile = FireProfileController.FireProfile.ClassA;

        [Tooltip("How large area can one particle extinquish")]
        public float particleExtinquishRadius = 0.1f;

        [Tooltip("How much the area is incremented if new area is not hit. (simulates water puddling/sliding on ground)")]
        public float incrementalPower = 0.0005f;

        private ParticleSystem part;
        private List<ParticleCollisionEvent> collisionEvents;

        void Start()
        {
            part = GetComponent<ParticleSystem>();
            collisionEvents = new List<ParticleCollisionEvent>();
        }

        void OnParticleCollision(GameObject other)
        {
            int numCollisionEvents = part.GetCollisionEvents(other, collisionEvents);

            Ignis.FlammableObject flamObj = other.GetComponentInParent<Ignis.FlammableObject>();
            FireProfileController profile = other.GetComponentInParent<FireProfileController>();

            if (flamObj)
            {
                // NETWORKING GATE: particle collisions run locally on every client, but
                // only the fire's authority (NetworkedFireState owner) may turn them into
                // permanent extinguish progress. Fires without NetworkedFireState keep
                // the legacy local behavior. This prevents divergent fire states.
                if (!NetworkedFireState.LocalClientMayAffectFire(flamObj))
                    return;

                // Default to 100% power if there is no custom profile controller attached
                float powerMultiplier = 1f;

                // Only extinguish if extinguisher profile matches fire
                if (profile != null && currentProfile == profile.currentProfile) 
                {
                    // Ping the controller. If temp > 0, it returns a heavily nerfed multiplier.
                    powerMultiplier = profile.ProcessWaterHit(numCollisionEvents);
                }

                int i = 0;
                while (i < numCollisionEvents)
                {
                    Vector3 pos = collisionEvents[i].intersection;

                    // Apply the multiplier to BOTH the starting radius and the increment
                    float effectiveRadius = particleExtinquishRadius * powerMultiplier;
                    float effectiveIncrement = incrementalPower * powerMultiplier;

                    // Only process the visual extinguish if the fire is taking damage
                    if (powerMultiplier > 0f)
                    {
                        flamObj.IncrementalExtinguish(pos, effectiveRadius, effectiveIncrement);
                        if (powerMultiplier == 1f && profile != null)
                        {
                            Debug.Log("Calling incremental extinguish for final extinguish at full power");
                            // Notify the profile that the final extinguish phase has started
                            profile.readyForSmolder = true;
                        }
                    }
                    i++;
                }
            }
        }
    }
}
#endif
