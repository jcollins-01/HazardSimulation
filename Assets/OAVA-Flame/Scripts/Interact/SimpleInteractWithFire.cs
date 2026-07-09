using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    /// <summary>
    /// A template for interacting with fire using IInteractWithFire interface.
    /// </summary>
    public class SimpleInteractWithFire : MonoBehaviour, IInteractWithFire
    {
        public void OnCollisionWithFire(GameObject burningObject)
        {
            Debug.Log("Object: " + gameObject.name + " Interacted with fire object: " + burningObject.name + " Which had a tag: " + burningObject.tag);
        }
    }
}

