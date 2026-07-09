using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class IgnisUnityTerrain : MonoBehaviour
    {
        [Header("Remember to backup your terrain data before using! Ignis modifies terrain data and data can be lost in case of blue screen!")]
        [Tooltip("How often do we update the active flammable objects. Smaller = more realistic, Larger = Better performance")]
        public float updateInterval_seconds = 1;

        [Tooltip("Smallest distance to activate the flammable object inside a tree. Smaller = better performance, but trees may not light up if your other objects are big, Larger = More safe.")]
        public float minCheckDistance_m = 5;
        private Dictionary<(int,int), GameObject> closeEnoughObjs = new Dictionary<(int,int), GameObject>();

        Terrain[] terrains;
        // Start is called before the first frame update
        void Start()
        {
            terrains = FindObjectsOfType<Terrain>();
            InvokeRepeating(nameof(UpdateTrees), updateInterval_seconds, updateInterval_seconds);
        }

        void UpdateTrees()
        {
            FireTrigger[] flamTriggers = GetComponentsInChildren<FireTrigger>();
            int a = 0;
            foreach (Terrain terrain in terrains)
            {
                TerrainData data = terrain.terrainData;
                float width = data.size.x;
                float height = data.size.z;
                float y = data.size.y;

                int i = 0;
                foreach (TreeInstance tree in data.treeInstances)
                {

                    if (tree.prototypeIndex >= data.treePrototypes.Length)
                        continue;

                    var _tree = data.treePrototypes[tree.prototypeIndex].prefab;

                    if (!_tree.GetComponentInChildren<FlammableObject>()) continue;

                    Vector3 position = new Vector3(
                        tree.position.x * width,
                        tree.position.y * y,
                        tree.position.z * height) + terrain.transform.position;

                    bool isCloseEnough = false;

                    foreach (FireTrigger flamTrigger in flamTriggers)
                    {
                        if (!flamTrigger.enabled) continue;

                        if (Vector3.Distance(flamTrigger.transform.position, position) < minCheckDistance_m)
                        {
                            if (!closeEnoughObjs.ContainsKey((a, i)))
                            {
                                Vector3 scale = new Vector3(tree.widthScale, tree.heightScale, tree.widthScale);
                                GameObject go = Instantiate(_tree, position, Quaternion.Euler(0f, Mathf.Rad2Deg * tree.rotation, 0f), FlameEngine.instance.runTimeTreeParent) as GameObject;
                                go.transform.localScale = scale;

                                TreeInstance newInst = tree;
                                newInst.widthScale = 0;
                                newInst.heightScale = 0;

                                data.SetTreeInstance(i, newInst);
                                closeEnoughObjs.Add((a, i), go);
                            }

                            isCloseEnough = true;
                        }
                    }

                    if (!isCloseEnough)
                    {
                        if (closeEnoughObjs.ContainsKey((a, i)))
                        {
                            if (closeEnoughObjs[(a, i)])
                            {
                                FlammableObject flam = closeEnoughObjs[(a, i)].GetComponentInChildren<FlammableObject>();
                                if (!flam.onFire)
                                {
                                    TreeInstance newInst = tree;
                                    newInst.widthScale = closeEnoughObjs[(a, i)].transform.localScale.x;
                                    newInst.heightScale = closeEnoughObjs[(a, i)].transform.localScale.y;
                                    data.SetTreeInstance(i, newInst);

                                    Destroy(closeEnoughObjs[(a, i)]);
                                    closeEnoughObjs.Remove((a, i));


                                }

                                if (flam.onFireTimer > (flam.burnOutStart_s + flam.burnOutLength_s + 5) && flam.isReignitable != FlammableObject.ReIgnitable.Always)
                                {
                                    Destroy(closeEnoughObjs[(a, i)]);
                                }
                            }
                        }
                    }

                    i++;
                }
                a++;
            }
        }

        private void OnApplicationQuit()
        {
            foreach((int,int) treeindex in closeEnoughObjs.Keys)
            {
                Terrain terrain = terrains[treeindex.Item1];

                TreeInstance newInst = terrain.terrainData.treeInstances[treeindex.Item2];
                newInst.widthScale = closeEnoughObjs[treeindex].transform.localScale.x;
                newInst.heightScale = closeEnoughObjs[treeindex].transform.localScale.y;
                terrain.terrainData.SetTreeInstance(treeindex.Item2, newInst);
            }
            
        }
    }
}
