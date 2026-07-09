using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class SphereIgnite : MonoBehaviour
    {

        [Tooltip("How many times in second to check")]
        public float checkFrequency = 10;

        [Tooltip("How large is the raycast area")]
        public float raycastRadius = 1f;

        [Tooltip("How much the particle will ignite the object on one collision. (If object has ignite time). 1 = ignite time in seconds by object props")]
        public float IgnitePowerMultiplier = 5f;

        [Tooltip("Raycast layermask")]
        public LayerMask mask = ~0;

        [Tooltip("Start invoking the repeating raycast on start ")]
        public bool repeatingRaycast = true;

        // Start is called before the first frame update
        void Start()
        {
            if (repeatingRaycast)
                InvokeRepeating(nameof(SphereIgniteCast), 1 / checkFrequency, 1 / checkFrequency);
        }

        /// <summary>
        /// Casts a raycast sphere once looking for ignite the flammable objects. Uses the public variables for the parameters.
        /// </summary>
        public void SphereIgniteCast()
        {
            if (!enabled || !gameObject.activeInHierarchy) return;
            Collider[] hits = Physics.OverlapSphere(transform.position, raycastRadius, mask);
            if (hits.Length > 0)
            {
                foreach(Collider hit in hits)
                {
                    Ignis.FlammableObject flam = hit.gameObject.GetComponentInParent<Ignis.FlammableObject>();
                    if (flam)
                    {
                        flam.TryToSetOnFire(hit.ClosestPointOnBounds(transform.position), IgnitePowerMultiplier);
                    }

                    if (FlameEngine.instance.VegetationStudioProCompatible)
                    {
                        FlameEngine.instance.MaskInstanceAndSpawnAPrefabIfNecessary(hit.gameObject);
                    }
                }

            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, raycastRadius);
        }
    }
}
