#if OAVA_IGNIS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class ParticleExtinguish : MonoBehaviour
    {

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

            int i = 0;

            Ignis.FlammableObject flamObj = other.GetComponentInParent<Ignis.FlammableObject>();
            if (flamObj)
            {
                while (i < numCollisionEvents)
                {
                    Vector3 pos = collisionEvents[i].intersection;
                    flamObj.IncrementalExtinguish(pos, particleExtinquishRadius, incrementalPower);
                    i++;
                }
            }

        }
    }
}
#endif
