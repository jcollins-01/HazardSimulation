#if OAVA_IGNIS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class ParticleIgnite : MonoBehaviour
    {

        [Tooltip("How much the particle will ignite the object on one collision. (If object has ignite time). 1 = ignite time in seconds by object props")]
        public float IgnitePowerMultiplier = 5f;

        private ParticleSystem part;
        private List<ParticleCollisionEvent> collisionEvents;
        private HashSet<GameObject> collidedAlready = new HashSet<GameObject>();

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
                    flamObj.TryToSetOnFire(pos, IgnitePowerMultiplier);
                    i++;
                }
            }

            if (FlameEngine.instance.VegetationStudioProCompatible)
            {
                if(!collidedAlready.Contains(other))
                {
                    if(!other.transform.parent || other.transform.parent && !collidedAlready.Contains(other.transform.parent.gameObject))
                    {
                        GameObject maskedObj = FlameEngine.instance.MaskInstanceAndSpawnAPrefabIfNecessary(other);
                        if (maskedObj)
                        {
                            collidedAlready.Add(maskedObj);
                            StartCoroutine(RemoveFromCollidedAlready(maskedObj));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Because VS PRO shuffles the colliders (I guess for effiency) we need to blacklist a masked collider for a while to not randomly ignite other vegetation far away
        /// </summary>
        /// <param name="go"></param>
        /// <returns></returns>
        IEnumerator RemoveFromCollidedAlready(GameObject go)
        {
            yield return new WaitForSeconds(0.5f);
            collidedAlready.Remove(go);
        }
    }
}
#endif
