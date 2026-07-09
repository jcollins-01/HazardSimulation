using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ignis
{
    public class FireTrigger : MonoBehaviour
    {

        struct CurOnTouchMaterials
        {
            public GameObject obj;
            public Material mat;
            public OAVAShaderCompatibilitySO shaderComp;
        }

        struct CurFlammableObjs
        {
            public FlammableObject flamObj;
            public Vector3 pos;
        }

        struct CurCBs
        {
            public GameObject obj;
            public FlameCollisionCallbacks callBack;

        }

        public FlammableObject flameObj;
        public BoxCollider boundBoxCollider;
        public Vector3 size;
        public LayerMask mask = ~0;

        private readonly Dictionary<FlammableObject, CurFlammableObjs> flamsAndPositions = new Dictionary<FlammableObject, CurFlammableObjs>();
        private readonly Dictionary<GameObject, CurCBs> collisionCallbackObjs = new Dictionary<GameObject, CurCBs>();
        private readonly HashSet<IInteractWithFire> interactWithFires = new HashSet<IInteractWithFire>();
        private readonly Dictionary<GameObject, CurOnTouchMaterials> onTouchObjs = new Dictionary<GameObject, CurOnTouchMaterials>();

        private void FixedUpdate()
        {
            foreach(FlammableObject flam in flamsAndPositions.Keys.ToList())
            {
                if (flam)
                {
                    flam.TryToSetOnFireIgniteProgressIncrease(flamsAndPositions[flam].pos, Time.fixedDeltaTime);
                }
                else
                {
                    flamsAndPositions.Remove(flam);
                }
            }

            foreach (IInteractWithFire interactable in interactWithFires.ToList())
            {
                if (interactable != null && !interactable.Equals(null))
                {
                    interactable.OnCollisionWithFire(flameObj.gameObject);
                }
                else
                {
                    interactWithFires.Remove(interactable);
                }
            }

            foreach(GameObject cbGO in collisionCallbackObjs.Keys.ToList()) {
                if (cbGO)
                {
                    collisionCallbackObjs[cbGO].callBack.TriggerCollisionEvents();
                }
                else
                {
                    collisionCallbackObjs.Remove(cbGO);
                }
            }

            foreach(GameObject matGO in onTouchObjs.Keys.ToList())
            {
                if (matGO)
                {
                    CurOnTouchMaterials mats = onTouchObjs[matGO];
                    foreach (OAVAShaderCompatibilitySO.ShaderProperty prop in mats.shaderComp.onTouchChangeProperties)
                    {
                        if (mats.mat.HasProperty(prop.name))
                        {
                            mats.mat.SetFloat(prop.name, Mathf.MoveTowards(mats.mat.GetFloat(prop.name), prop.targetValue, Time.deltaTime * prop.speedMultiplier));
                        }
                    }
                }
                else
                {
                    onTouchObjs.Remove(matGO);
                }
                
            }
        }

        private void Update()
        {
            if (boundBoxCollider)
            {
                transform.position = boundBoxCollider.transform.TransformPoint(boundBoxCollider.center);
                transform.rotation = boundBoxCollider.transform.rotation;
            }
        }

        public void StartTrigger()
        {
            InvokeRepeating(nameof(TriggerUpdate), Random.Range(0, 1/FlameEngine.instance.FlameTriggerCollisionCheckFrequency), 1/FlameEngine.instance.FlameTriggerCollisionCheckFrequency);
        }

        public void TriggerUpdate()
        {
            if (!enabled) return;
            
            flamsAndPositions.Clear();
            collisionCallbackObjs.Clear();
            onTouchObjs.Clear();
            interactWithFires.Clear();
     
            if (flameObj.hasBurnedOut()) return;

            Collider[] cols = Physics.OverlapBox(transform.position, size / 2, transform.rotation, mask);
            foreach (Collider col in cols)
            {
                ProcessCollidedObject(col);
            }
        }

        private void ProcessCollidedObject(Collider other)
        {
            FlammableObject flam = other.gameObject.GetComponentInParent<FlammableObject>();

            if (flam != null && flam != flameObj && !flamsAndPositions.ContainsKey(flam))
            {
                Vector3 closestPoint = other.ClosestPointOnBounds(transform.position);
                if (Vector3.Distance(closestPoint, flameObj.GetFireOrigin()) <= flameObj.fireSpread)
                {
                    if (flameObj.GetPutOutRadius() < 0.05 || Vector3.Distance(closestPoint, flameObj.GetPutOutCenter()) > flameObj.GetPutOutRadius())
                    {
                        if (!flam.onFire || flam.isReignitable != FlammableObject.ReIgnitable.No)
                        {
                            flamsAndPositions.Add(flam, new CurFlammableObjs {flamObj = flam, pos = closestPoint });
                            return;
                        }
                    }
                }
            }

            IInteractWithFire interactable = other.gameObject.GetComponentInParent<IInteractWithFire>();
             
            if(interactable != null && !interactable.Equals(null) &&!interactWithFires.Contains(interactable))
            {
                Vector3 closestPoint = other.ClosestPointOnBounds(transform.position);
                if (Vector3.Distance(closestPoint, flameObj.GetFireOrigin()) <= flameObj.fireSpread)
                {
                    if (flameObj.GetPutOutRadius() < 0.05 || Vector3.Distance(closestPoint, flameObj.GetPutOutCenter()) > flameObj.GetPutOutRadius())
                    {
                        interactWithFires.Add(interactable);
                    }
                }
            }

            if (!collisionCallbackObjs.ContainsKey(other.gameObject))
            {
                foreach (FlameCollisionCallbacks cb in FlameEngine.instance.GetCollisionCallbacks())
                {
                    if (cb.HasCallback(other.gameObject))
                    {
                        Vector3 closestPoint = other.ClosestPointOnBounds(transform.position);
                        if (Vector3.Distance(closestPoint, flameObj.GetFireOrigin()) <= flameObj.fireSpread)
                        {
                            if (flameObj.GetPutOutRadius() < 0.05 || Vector3.Distance(closestPoint, flameObj.GetPutOutCenter()) > flameObj.GetPutOutRadius())
                            {
                                collisionCallbackObjs.Add(other.gameObject, new CurCBs { obj = other.gameObject, callBack = cb });
                            }
                        }
                    }
                }
            }
            

            if (FlameEngine.instance.VegetationStudioProCompatible)
            {
                FlameEngine.instance.MaskInstanceAndSpawnAPrefabIfNecessary(other.gameObject);
            }
           

            if (!FlameEngine.instance.enableOnTouchChecks)
            {
                return;
            }

            // If other object is a collider object for LOD group or flammable child use the parent instead
            GameObject supportedGameObject = other.gameObject;
            LODGroup parent = other.gameObject.GetComponentInParent<LODGroup>();
            if (parent)
            {
                if(parent.transform != other.gameObject.transform)
                    supportedGameObject = other.gameObject.transform.parent.gameObject;
            }
            else
            {
                FlammableObject parent2 = other.gameObject.GetComponentInParent<FlammableObject>();
                if (parent2)
                {
                    if (parent2.transform != other.gameObject.transform)
                        supportedGameObject = other.gameObject.transform.parent.gameObject;
                }
            }

            if(supportedGameObject == flameObj.gameObject)
            {
                return;
            }

            if (onTouchObjs.ContainsKey(supportedGameObject))
            {
                foreach (Renderer rend in supportedGameObject.GetComponentsInChildren<Renderer>())
                {
                    for (int a = 0; a < rend.materials.Length; a++)
                    {
                        List<OAVAShaderCompatibilitySO> compShaders = FlameEngine.instance.GetCompatibleShaders();
                        Material matRef = rend.materials[a];
                        foreach (OAVAShaderCompatibilitySO shaderComp in compShaders)
                        {
                            if (matRef.HasProperty(shaderComp.ShaderCheckProperty) ||
                                (shaderComp.ShaderCheckProperty.Equals(string.Empty) && rend.materials[a].shader.name.Equals(shaderComp.ShaderName)))
                            {
                                CurOnTouchMaterials onTouch = new CurOnTouchMaterials
                                {
                                    obj = supportedGameObject,
                                    mat = matRef,
                                    shaderComp = shaderComp
                                };
                                onTouchObjs.Add(supportedGameObject, onTouch);

                                break;
                            }
                        }
                    }
                }
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }

    

}
