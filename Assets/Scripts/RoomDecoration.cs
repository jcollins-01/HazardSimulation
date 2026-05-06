using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class RoomDecoration : MonoBehaviour
{
    [Header("Decoration Settings")]
    [Range(0f, 1f)]
    public float clutterAmount = 0.1f;

    [Header("Floor Materials")]
    public Material tileMaterial;
    public Material woodMaterial;
    public Material carpetMaterial;
    public Material concreteMaterial;

    private float lastClutterAmount; // Used to check if the slider was changed in the editor

    //private string[] roomTypes = { "Generic", "Bedroom", "Bathroom", "Living Room" };

    public void DecorateRooms(List<RoomGeneration.RoomData> rooms)
    {
        // If the list has no rooms in it, return
        if (rooms == null || rooms.Count == 0) return;

        // If clutter is completely disabled, bypass the decoration loop and clear any interior from the last generation
        if (clutterAmount <= 0f)
        {
            ClearAllDecoration(rooms);
            return;
        }

        // Clear old interior before placing new furniture/clutter to avoid stacking
        ClearAllDecoration(rooms);

        // Because RoomGeneration already determined what these rooms are, we just loop through and decorate them immediately
        foreach (var room in rooms)
        {
            if (room.RoomType == "Stairwell" || room.RoomType == "Closet")
                continue; // Skip rooms that don't need furniture

            ApplyFloorMaterial(room, room.RoomType);
            DecorateSpecificRoom(room, room.RoomType);
        }

        /*
        // Group the rooms by their floor level by reading the parent object's name
        var roomsByFloor = rooms.GroupBy(r => GetFloorLevel(r)).ToDictionary(g => g.Key, g => g.ToList());

        // Route the assignment logic based on the active preset
        if (generator.isSmallHouse) AssignSmallHouseLogic(roomsByFloor); // was smallHouse
        else if (generator.isTwoStoryHouse) AssignTwoStoryLogic(roomsByFloor); // was twoStoryHouse
        else if (generator.mansion) AssignMansionLogic(roomsByFloor);
        else if (generator.dormitory) AssignDormitoryLogic(roomsByFloor);
        else if (generator.warehouse) AssignWarehouseLogic(roomsByFloor);
        else if (generator.skyscraper) AssignSkyscraperLogic(roomsByFloor);
        else AssignCustomLogic(roomsByFloor); // Fallback if no preset is checked
        */

        // After rooms are assigned, reset the generator variables ourselves
        //generator.isSmallHouse = false;
        //generator.isTwoStoryHouse = false;
    }

    #region Room Type Assignment
    /*
    private int GetFloorLevel(RoomGeneration.RoomData room)
    {
        // Expecting the parent to be named "Floor_0", "Floor_1", etc.
        if (room.RoomObject != null && room.RoomObject.transform.parent != null)
        {
            string parentName = room.RoomObject.transform.parent.name;
            if (parentName.StartsWith("Floor_") && int.TryParse(parentName.Split('_')[1], out int floorNum))
            {
                return floorNum;
            }
        }
        return 0; // Default to ground floor
    }*/

    /*private void AssignSmallHouseLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor)
    {
        Debug.Log("Assigning a small house");
        // Small houses are 1 floor so all rooms should be on floor 0 - if they're not, return
        if (!roomsByFloor.TryGetValue(0, out List<RoomGeneration.RoomData> groundRooms)) return;

        // Essential rooms that every small house MUST have
        List<string> requiredRooms = new List<string> { "Kitchen", "Living Room", "Bathroom", "Bedroom" };
        // Small house is always set to 4 rooms, so these will never be accessed
        List<string> optionalRooms = new List<string> { "Office", "Dining Room", "Guest Room", "Storage" };

        AssignRoomsFromLists(groundRooms, requiredRooms, optionalRooms);
    }

    private void AssignTwoStoryLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor)
    {
        Debug.Log("Assigning a two story");
        // Ground Floor
        if (roomsByFloor.TryGetValue(0, out List<RoomGeneration.RoomData> groundRooms))
        {
            List<string> groundRequired = new List<string> { "Kitchen", "Living Room", "Dining Room" };
            List<string> groundOptional = new List<string> { "Half-Bath", "Office", "Mudroom", "Library" };
            AssignRoomsFromLists(groundRooms, groundRequired, groundOptional);
        }

        // Upper Floor
        if (roomsByFloor.TryGetValue(1, out List<RoomGeneration.RoomData> upperRooms))
        {
            AssignBedroomsAndBathrooms(upperRooms);
        }
    }

    private void AssignMansionLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor)
    {
        foreach (var kvp in roomsByFloor)
        {
            int floor = kvp.Key;
            List<RoomGeneration.RoomData> rooms = kvp.Value;

            if (floor == 0)
            {
                // Ground floor: Grand public spaces
                List<string> req = new List<string> { "Grand Foyer", "Kitchen", "Formal Dining", "Ballroom", "Library" };
                List<string> opt = new List<string> { "Conservatory", "Billiards Room", "Half-Bath", "Staff Quarters" };
                AssignRoomsFromLists(rooms, req, opt);
            }
            else if (floor == roomsByFloor.Keys.Max()) // Top floor
            {
                // Top floor: Storage, Theater, Servants
                List<string> req = new List<string> { "Home Theater", "Storage" };
                List<string> opt = new List<string> { "Guest Suite", "Observatory", "Attic" };
                AssignRoomsFromLists(rooms, req, opt);
            }
            else
            {
                // Middle floors: Suites and Bedrooms/Bathrooms
                // AssignRoomsFromLists(rooms, new List<string> { "Master Suite", "Master Bath" }, new List<string> { "Bedroom", "Bathroom", "Sitting Room" });
                AssignBedroomsAndBathrooms(rooms);
            }
        }
    }
    */
    // You can build out standard assignments for Warehouse, Skyscraper, and Dormitory here
    //private void AssignWarehouseLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor) { /* ... */ }
    //private void AssignDormitoryLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor) { /* ... */ }
    //private void AssignSkyscraperLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor) { /* ... */ }
    //private void AssignCustomLogic(Dictionary<int, List<RoomGeneration.RoomData>> roomsByFloor) { /* ... */ }
    
    /*
    // A special method to call for floors with bedrooms and bathrooms to ensure they spawn in a decent ratio to each other
    private void AssignBedroomsAndBathrooms(List<RoomGeneration.RoomData> bedAndBathRooms)
    {
        Debug.Log("Assigning portioned bedrooms and bathrooms");
        // Ensure at least one master bed and bath, fill the rest with beds/baths
        int roomCount = bedAndBathRooms.Count;
        int bathTarget = Mathf.Max(1, roomCount / 3); // 1 bath per 3 rooms, max

        int bathsAssigned = 0;

        // Since we have very strict rules for keeping only bedrooms and bathrooms on the upper floor
        string assignedType = "";

        foreach (var room in bedAndBathRooms)
        {
            if (IsRoomHallway(room.Tiles))
            {
                assignedType = "Hallway"; // Assuming Type is a property in RoomData
                continue;
            }

            if (bathsAssigned < bathTarget)
            {
                assignedType = "Bathroom";
                bathsAssigned++;
            }
            else
            {
                assignedType = "Bedroom";
            }

            // Apply floor materials based on the assigned type
            ApplyFloorMaterial(room, assignedType);

            // Rename the GameObject so it is easily identifiable in the hierarchy
            room.RoomObject.name += $" [{assignedType}]";

            // Decorate based on type
            DecorateSpecificRoom(room, assignedType);
        }
    }

    private void AssignRoomsFromLists(List<RoomGeneration.RoomData> floorRooms, List<string> required, List<string> optional)
    {
        Debug.Log("Assigning room from standard list");
        // Shuffle the physical rooms so the layout feels random
        var availableRooms = floorRooms.OrderBy(r => Random.value).ToList();

        int reqIndex = 0;

        string assignedType = "";

        foreach (var room in availableRooms)
        {
            // Always tag long, thin rooms as hallways immediately
            if (IsRoomHallway(room.Tiles))
            {
                assignedType = "Hallway";
                continue;
            }

            // Fulfill the required rooms list first
            if (reqIndex < required.Count)
            {
                assignedType = required[reqIndex];
                reqIndex++;
            }
            // Once required rooms are placed, pull randomly from the optional list
            else if (optional.Count > 0)
            {
                assignedType = optional[Random.Range(0, optional.Count)];
            }
            else
            {
                // Failsafe
                assignedType = "Empty Room"; 
            }

            // Apply floor materials based on the assigned type
            ApplyFloorMaterial(room, assignedType);

            // Rename the GameObject so it is easily identifiable in the hierarchy
            room.RoomObject.name += $" [{assignedType}]";

            // Decorate based on type
            DecorateSpecificRoom(room, assignedType);
        }
    }*/

#endregion

private void ClearAllDecoration(List<RoomGeneration.RoomData> rooms)
    {
        foreach (var room in rooms)
        {
            if (room.RoomObject == null) continue;

            // Look through the room and its sub-containers to find all the possible clutter objects
            Transform[] allChildren = room.RoomObject.GetComponentsInChildren<Transform>();

            // We loop backwards to safely destroy children in the editor
            for (int i = allChildren.Length - 1; i >= 0; i--)
            {
                GameObject child = allChildren[i].gameObject;

                if (child.name.Contains("(Clone)"))
                {
                    // Check if the name contains "Window" or "Door" to protect them
                    if (child.name.Contains("Window") || child.name.Contains("Door"))
                        continue; // Skip this one, it's structural and should be here no matter what interior settings we're on!

                    DestroyImmediate(child);
                }
            }
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

    // FUTURE: Incorporate special hallway clutter
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
    /*private bool IsRoomHallway(HashSet<Vector2Int> roomTiles)
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
    }*/

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
        // Get the local bottom of the mesh (distance from pivot to bottom)
        float meshBottomY = instance.GetComponent<Renderer>().bounds.min.y;
        // Get the pivot point at which the prefab is spawned/handled from
        float pivotY = instance.transform.position.y;

        // Return the distance from the pivot to the bottom with some funky math to make it spawn in just the right place
        return pivotY - meshBottomY;
    }

    private void ApplyFloorMaterial(RoomGeneration.RoomData room, string assignedType)
    {
        // Find the Floors game object
        Transform floorTransform = room.RoomObject.transform.Find("Floors");
        if (floorTransform == null) return;

        MeshRenderer renderer = floorTransform.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        switch (assignedType)
        {
            case "Bathroom":
                renderer.material = tileMaterial;
                break;
            case "Living Room":
                // Randomly choose between Wood and Carpet
                renderer.material = Random.value > 0.5f ? woodMaterial : carpetMaterial;
                break;
            case "Bedroom":
                renderer.material = carpetMaterial;
                break;
            default:
                renderer.material = woodMaterial;
                break;
        }
    }

    // This allows the slider to work in the Inspector
    private void OnValidate()
    {
        if (!Application.isPlaying && clutterAmount != lastClutterAmount)
        {
            lastClutterAmount = clutterAmount;

            // Get the rooms currently held by the generator
            RoomGeneration gen = GetComponent<RoomGeneration>();
            if (gen != null && gen.allGeneratedRooms != null)
            {
                DecorateRooms(gen.allGeneratedRooms);
            }
        }
    }
}