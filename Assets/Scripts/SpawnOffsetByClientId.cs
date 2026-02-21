using UnityEngine;
using Unity.Netcode;

public class SpawnOffsetByClientId : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        if (!IsServer) return; //  Server decides spawn position
        transform.position = new Vector3(OwnerClientId * 2.0f, 0f, 0f);
    }
}