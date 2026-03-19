using System.Collections.Generic;
using UnityEngine;

public class RoomDecoration : MonoBehaviour
{
    private string[] roomTypes = { "Generic", "Bedroom", "Bathroom", "Living Room" };

    public void DecorateRooms(List<RoomGeneration.RoomData> rooms)
    {
        Debug.Log("Attempting to decorate rooms");
        foreach (var room in rooms)
        {
            // Assign a random room type
            string assignedType = roomTypes[Random.Range(0, roomTypes.Length)];

            // Rename the GameObject so it is easily identifiable in the hierarchy
            room.RoomObject.name += $" [{assignedType}]";

            // Available rooms: "Generic", "Bedroom", "Bathroom", and "Living Room" 
            room.RoomObject.tag = assignedType;

            // Decorate based on type
            DecorateSpecificRoom(room, assignedType);
        }
    }

    private void DecorateSpecificRoom(RoomGeneration.RoomData room, string type)
    {
        // Load prefabs based on the room type to save memory (only load what we need)
        switch (type)
        {
            case "Bedroom":
                SpawnBedroomFurniture(room);
                break;
            case "Bathroom":
                SpawnBathroomFurniture(room);
                break;
            case "Living Room":
                SpawnLivingRoomFurniture(room);
                break;
            case "Generic":
            default:
                SpawnGenericFurniture(room);
                break;
        }
    }

    private void SpawnBedroomFurniture(RoomGeneration.RoomData room)
    {
        GameObject bedPrefab = Resources.Load<GameObject>("Interior Prefabs/Bed");
        GameObject nightstandPrefab = Resources.Load<GameObject>("Interior Prefabs/Nightstand");

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        if (edges.Count > 0 && bedPrefab != null)
        {
            // Pick a random wall for the bed
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(bedPrefab, edge.tile, edge.forward, room.RoomObject.transform);

            // Spawn a nightstand next to it if we have one
            if (nightstandPrefab != null)
            {
                // This is a basic offset. In a robust system, you'd check if this adjacent tile is still in the room.
                Vector2Int nightstandTile = edge.tile + new Vector2Int(Mathf.RoundToInt(edge.forward.z), Mathf.RoundToInt(-edge.forward.x));
                SpawnFurniture(nightstandPrefab, nightstandTile, edge.forward, room.RoomObject.transform);
            }
        }
    }

    private void SpawnBathroomFurniture(RoomGeneration.RoomData room)
    {
        GameObject toiletPrefab = Resources.Load<GameObject>("Interior Prefabs/Toilet");
        GameObject tubPrefab = Resources.Load<GameObject>("Interior Prefabs/Tub");
        GameObject sinkPrefab = Resources.Load<GameObject>("Interior Prefabs/Sink");

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        if (edges.Count >= 2) // We need a few walls for a bathroom
        {
            if (tubPrefab != null) SpawnFurniture(tubPrefab, edges[0].tile, edges[0].forward, room.RoomObject.transform);
            if (toiletPrefab != null) SpawnFurniture(toiletPrefab, edges[1].tile, edges[1].forward, room.RoomObject.transform);
        }
    }

    private void SpawnLivingRoomFurniture(RoomGeneration.RoomData room)
    {
        GameObject couchPrefab = Resources.Load<GameObject>("Interior Prefabs/Couch");
        GameObject tvPrefab = Resources.Load<GameObject>("Interior Prefabs/TV");
        GameObject tablePrefab = Resources.Load<GameObject>("Interior Prefabs/Table");

        Vector2Int centerTile = GetRoomCenter(room.Tiles);
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Spawn Table in center
        if (tablePrefab != null)
        {
            Vector3 centerPos = new Vector3(centerTile.x, 0, centerTile.y);
            Instantiate(tablePrefab, centerPos, Quaternion.identity, room.RoomObject.transform);
        }

        // Spawn Couch against a wall
        if (edges.Count > 0 && couchPrefab != null)
        {
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(couchPrefab, edge.tile, edge.forward, room.RoomObject.transform);
        }
    }

    private void SpawnGenericFurniture(RoomGeneration.RoomData room)
    {
        GameObject tablePrefab = Resources.Load<GameObject>("Interior Prefabs/Table");
        GameObject shelfPrefab = Resources.Load<GameObject>("Interior Prefabs/Shelf");

        Vector2Int centerTile = GetRoomCenter(room.Tiles);
        if (tablePrefab != null)
        {
            Vector3 centerPos = new Vector3(centerTile.x, 0, centerTile.y);
            Instantiate(tablePrefab, centerPos, Quaternion.identity, room.RoomObject.transform);
        }

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        if (edges.Count > 0 && shelfPrefab != null)
        {
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(shelfPrefab, edge.tile, edge.forward, room.RoomObject.transform);
        }
    }

    // --- Utility Methods ---

    private void SpawnFurniture(GameObject prefab, Vector2Int tile, Vector3 forward, Transform parent)
    {
        // Spawns slightly above the ground, then drops down to calculate exact bottom
        Vector3 pos = new Vector3(tile.x, 2f, tile.y);
        GameObject instance = Instantiate(prefab, pos, Quaternion.LookRotation(forward), parent);

        float yOffset = CalculateVerticalOffset(instance);
        instance.transform.position = new Vector3(pos.x, yOffset, pos.z);
    }

    private Vector2Int GetRoomCenter(HashSet<Vector2Int> roomTiles)
    {
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var tile in roomTiles)
        {
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }
        return new Vector2Int(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2);
    }

    private List<(Vector2Int tile, Vector3 forward)> GetRoomEdges(HashSet<Vector2Int> roomTiles)
    {
        List<(Vector2Int, Vector3)> edges = new List<(Vector2Int, Vector3)>();
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (Vector2Int tile in roomTiles)
        {
            foreach (Vector2Int dir in dirs)
            {
                if (!roomTiles.Contains(tile + dir))
                {
                    Vector3 forwardDir = new Vector3(-dir.x, 0, -dir.y);
                    edges.Add((tile, forwardDir));
                    break;
                }
            }
        }
        return edges;
    }

    private float CalculateVerticalOffset(GameObject instance)
    {
        Renderer renderer = instance.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = instance.GetComponentInChildren<Renderer>();
        }

        if (renderer != null)
        {
            float meshBottomY = renderer.bounds.min.y;
            float pivotY = instance.transform.position.y;
            return (pivotY - (meshBottomY / 2)) * 2;
        }
        return 0f;
    }
}