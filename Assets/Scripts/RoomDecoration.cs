using System.Collections.Generic;
using UnityEngine;

public class RoomDecoration : MonoBehaviour
{
    [Header("Decoration Settings")]
    [Range(0f, 1f)]
    public float clutterAmount = 0.1f;

    private string[] roomTypes = { "Generic", "Bedroom", "Bathroom", "Living Room" };

    public void DecorateRooms(List<RoomGeneration.RoomData> rooms)
    {
        // If clutter is completely disabled, bypass the entire decoration loop
        if (clutterAmount <= 0f) return;

        foreach (var room in rooms)
        {
            string assignedType;

            // Evaluate the shape. If it's a long, narrow strip, force it to be a Hallway
            if (IsRoomHallway(room.Tiles))
                assignedType = "Hallway";
            else
            {
                // Otherwise, assign a random standard room type
                assignedType = roomTypes[Random.Range(0, roomTypes.Length)];
            }

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
            // Pick a random wall for the bed to sit flush against
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(bedPrefab, edge.tile, edge.forward, room.RoomObject.transform);

            // Use the clutter slider to decide if we spawn a nightstand
            if (Random.value <= clutterAmount && nightstandPrefab != null)
            {
                // Calculate the tile directly to the side of the bed
                Vector2Int rightDir = new Vector2Int(Mathf.RoundToInt(edge.forward.z), Mathf.RoundToInt(-edge.forward.x));
                Vector2Int nightstandTile = edge.tile + rightDir;

                // Ensure we aren't spawning the nightstand outside the room boundaries
                if (room.Tiles.Contains(nightstandTile))
                {
                    SpawnFurniture(nightstandPrefab, nightstandTile, edge.forward, room.RoomObject.transform);
                }
            }
        }
    }

    private void SpawnBathroomFurniture(RoomGeneration.RoomData room)
    {
        GameObject toiletPrefab = Resources.Load<GameObject>("Interior Prefabs/Toilet");
        GameObject tubPrefab = Resources.Load<GameObject>("Interior Prefabs/Tub");
        GameObject sinkPrefab = Resources.Load<GameObject>("Interior Prefabs/Sink");

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Standard placement for core bathroom fixtures
        if (edges.Count >= 2)
        {
            if (tubPrefab != null) SpawnFurniture(tubPrefab, edges[0].tile, edges[0].forward, room.RoomObject.transform);
            if (toiletPrefab != null) SpawnFurniture(toiletPrefab, edges[1].tile, edges[1].forward, room.RoomObject.transform);
        }

        // Sink acts as the extra clutter item
        if (edges.Count >= 3 && Random.value <= clutterAmount && sinkPrefab != null)
        {
            SpawnFurniture(sinkPrefab, edges[2].tile, edges[2].forward, room.RoomObject.transform);
        }
    }

    private void SpawnLivingRoomFurniture(RoomGeneration.RoomData room)
    {
        GameObject couchPrefab = Resources.Load<GameObject>("Interior Prefabs/Couch");
        GameObject tvPrefab = Resources.Load<GameObject>("Interior Prefabs/TV");
        GameObject tablePrefab = Resources.Load<GameObject>("Interior Prefabs/Table");

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        if (edges.Count > 0 && tvPrefab != null && couchPrefab != null)
        {
            // Pick a wall for the TV
            var tvEdge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(tvPrefab, tvEdge.tile, tvEdge.forward, room.RoomObject.transform);

            // Translate the Vector3 forward direction into a Vector2Int for tile math
            Vector2Int tvDirection = new Vector2Int(Mathf.RoundToInt(tvEdge.forward.x), Mathf.RoundToInt(tvEdge.forward.z));

            // Push the sofa 2 tiles away from the TV, into the center of the room
            Vector2Int sofaTile = tvEdge.tile + (tvDirection * 2);

            if (room.Tiles.Contains(sofaTile))
            {
                // Invert the TV's forward vector so the sofa faces back at it
                Vector3 sofaForward = -tvEdge.forward;
                SpawnFurniture(couchPrefab, sofaTile, sofaForward, room.RoomObject.transform);

                // If clutter is high enough, place a coffee table between them
                if (Random.value <= clutterAmount && tablePrefab != null)
                {
                    Vector2Int tableTile = tvEdge.tile + tvDirection; // 1 tile away from TV
                    if (room.Tiles.Contains(tableTile))
                    {
                        Vector3 tablePos = new Vector3(tableTile.x, 0, tableTile.y);
                        Instantiate(tablePrefab, tablePos, Quaternion.identity, room.RoomObject.transform);
                    }
                }
            }
        }
    }

    private void SpawnHallwayClutter(RoomGeneration.RoomData room)
    {
        GameObject shelfPrefab = Resources.Load<GameObject>("Interior Prefabs/Shelf");
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Hallways should only have items if clutter is very high, as they block pathing
        if (Random.value <= (clutterAmount - 0.3f) && edges.Count > 0 && shelfPrefab != null)
        {
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(shelfPrefab, edge.tile, edge.forward, room.RoomObject.transform);
        }
    }

    private void SpawnGenericFurniture(RoomGeneration.RoomData room)
    {
        GameObject tablePrefab = Resources.Load<GameObject>("Interior Prefabs/Table");
        GameObject shelfPrefab = Resources.Load<GameObject>("Interior Prefabs/Shelf");

        // Base spawn
        Vector2Int centerTile = GetRoomCenter(room.Tiles);
        if (tablePrefab != null)
        {
            Vector3 centerPos = new Vector3(centerTile.x, 0, centerTile.y);
            Instantiate(tablePrefab, centerPos, Quaternion.identity, room.RoomObject.transform);
        }

        // Clutter spawn
        if (Random.value <= clutterAmount)
        {
            List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
            if (edges.Count > 0 && shelfPrefab != null)
            {
                var edge = edges[Random.Range(0, edges.Count)];
                SpawnFurniture(shelfPrefab, edge.tile, edge.forward, room.RoomObject.transform);
            }
        }
    }

    // --- Utility Methods ---

    // Calculates the bounding box to determine if the room is a narrow strip (a hallway)
    private bool IsRoomHallway(HashSet<Vector2Int> roomTiles)
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

        int width = maxX - minX + 1;
        int length = maxY - minY + 1;

        // If the room is 2 tiles wide or less, but fairly long, it's a hallway - might change later to be more certain
        return (width <= 2 && length >= 4) || (length <= 2 && width >= 4);
    }

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