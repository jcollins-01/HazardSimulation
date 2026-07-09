using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class VegetationStudioProTreeUnMasker : MonoBehaviour
    {
        public GameObject mask;
        public FlammableObject flammable;
        public string VegetationInstanceItemId;
        private float extraTimer = 5;

        void Update()
        {
            if(!flammable.onFire && flammable.GetCurrentIgnitionProgress() <= 0)
            {
                extraTimer -= Time.deltaTime;

                if(extraTimer < 0)
                {
                    Unmask();
                }
                
            } 
            else
            {
                extraTimer = 5;
            }
        }

        public void Unmask()
        {
            Destroy(mask);
            Destroy(flammable.gameObject);
        }
    }
}

