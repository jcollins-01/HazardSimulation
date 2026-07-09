using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    public class MoveNegativeX : MonoBehaviour
    {

        // Update is called once per frame
        void Update()
        {
            transform.Translate(new Vector3(-0.2f * Time.deltaTime, 0, 0));
        }
    }
}

