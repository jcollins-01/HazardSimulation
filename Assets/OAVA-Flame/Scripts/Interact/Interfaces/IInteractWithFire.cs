using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ignis
{
    /// <summary>
    /// Implement this interface and you can catch collisions/touchs with fire. Example: SimpleInteractWithFire.cs. Example in demoscene: Main camera, move to fire.
    /// </summary>
    public interface IInteractWithFire
    {
        void OnCollisionWithFire(GameObject burningObject);
    }
}

