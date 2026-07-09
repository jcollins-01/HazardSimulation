#if OAVA_IGNIS

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class RaycastExtinguish : MonoBehaviour
    {
        [Tooltip("Which direction to check")]
        public Vector3 direction = new Vector3(0, -1, 0);

        [Tooltip("Offset of ray starting point")]
        public Vector3 startOffset = new Vector3(0, 0, 0);

        [Tooltip("How many times in second to check")]
        public float checkFrequency = 10;

        [Tooltip("How large is the raycast area")]
        public float raycastRadius = 0.1f;

        [Tooltip("How much radius is incremented each consicutive hit")]
        public float radiusIncrement = 0.01f;

        [Tooltip("Max distance from the cast pos")]
        public float maxDist = 3f;

        [Tooltip("Raycast layermask")]
        public LayerMask mask = ~0;

        [Tooltip("Start invoking the repeating raycast on start ")]
        public bool repeatingRaycast = true;

        [Tooltip("Should raycast go through objects and extinguish everything on the path?")]
        public bool goThroughObjects = false;

        // Start is called before the first frame update
        void Start()
        {
            if(repeatingRaycast)
                InvokeRepeating(nameof(CastRayCastExtinguish), 1 / checkFrequency, 1 / checkFrequency);
        }

        /// <summary>
        /// Casts a raycast sphere looking for extinguishing the flammable objects. Uses the public variables for the parameters.
        /// </summary>
        public void CastRayCastExtinguish()
        {
            if (!enabled || !gameObject.activeInHierarchy) return;
            Vector3 castPoint = transform.position + startOffset;
            RaycastHit[] hits = Physics.SphereCastAll(castPoint, raycastRadius, direction, maxDist, mask);
            if (hits.Length > 0)
            {
                Ignis.FlammableObject hitFlam = null;
                Vector3 hitPoint = Vector3.zero;
                List<RaycastHit> otherHitObjects = new List<RaycastHit>();
                foreach (RaycastHit hit in hits)
                {
                    Ignis.FlammableObject flam = hit.collider.gameObject.GetComponentInParent<Ignis.FlammableObject>();
                    if (flam)
                    {
                        if (goThroughObjects)
                        {
                            flam.IncrementalExtinguish(hit.point, raycastRadius, radiusIncrement);
                        }
                        else
                        {
                            hitPoint = hit.point;
                            hitFlam = flam;
                        }
                        
                    }
                    else
                    {
                        otherHitObjects.Add(hit);
                    }
                }

                if (hitFlam != null && !goThroughObjects)
                {
                    float dist = Vector3.Distance(castPoint, hitPoint);

                    bool objectsInFront = false;
                    foreach (RaycastHit hit in otherHitObjects)
                    {
                        if (!hit.collider.isTrigger)
                        {
                            if (Vector3.Distance(hit.point, castPoint) < dist)
                            {
                                objectsInFront = true;
                                break;
                            }
                        }
                    }

                    if (!objectsInFront)
                    {
                        hitFlam.IncrementalExtinguish(hitPoint, raycastRadius, radiusIncrement);
                    }
                }

            }
        }

        private void OnDrawGizmos()
        {
            RaycastHit hit;

            Gizmos.color = Color.blue;
            if (Physics.SphereCast(transform.position, raycastRadius, direction, out hit, maxDist, mask))
            {
                Gizmos.DrawLine(transform.position, hit.point);
                Gizmos.DrawWireSphere(hit.point, raycastRadius);
            }
            else
            {
                Gizmos.DrawRay(transform.position, direction);
            }
        }
    }
}
#endif
