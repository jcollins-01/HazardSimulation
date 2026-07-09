using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class SphereExtinguish : MonoBehaviour
    {

        [Tooltip("How many times in second to check")]
        public float checkFrequency = 10;

        [Tooltip("How large is the raycast area")]
        public float raycastRadius = 1f;

        [Tooltip("Raycast layermask")]
        public LayerMask mask = ~0;

        [Tooltip("Start invoking the repeating raycast on start ")]
        public bool repeatingRaycast = true;

        // Start is called before the first frame update
        void Start()
        {
            if (repeatingRaycast)
                InvokeRepeating(nameof(SphereExtinguishCast), 1 / checkFrequency, 1 / checkFrequency);
        }

        /// <summary>
        /// Casts a raycast sphere once looking for ignite the flammable objects. Uses the public variables for the parameters.
        /// </summary>
        public void SphereExtinguishCast()
        {
            if (!enabled || !gameObject.activeInHierarchy) return;
            Collider[] hits = Physics.OverlapSphere(transform.position, raycastRadius, mask);
            if (hits.Length > 0)
            {
                foreach (Collider hit in hits)
                {
                    Ignis.FlammableObject flam = hit.gameObject.GetComponentInParent<Ignis.FlammableObject>();
                    if (flam)
                    {
                        flam.IncrementalExtinguish(hit.ClosestPointOnBounds(transform.position), raycastRadius, 0);
                    }
                }

            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, raycastRadius);
        }
    }
}
