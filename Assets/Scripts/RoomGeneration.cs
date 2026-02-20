using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomGeneration : MonoBehaviour
{
    [Header("Generation Settings")]
    public int numberOfRooms = 5;
    public int numberOfFloors = 2;
    public bool identicalFloors = false;
    public bool roomAmountsDifferPerFloor = false;
    public bool destroyPreviousGeneration = true;
    public bool roomRoofsTransparent = false;

    [Header("House Dimensions")]
    public int maxHouseWidth = 20;
    public int maxHouseLength = 20;

    [Header("Room Dimensions")]
    public int minRoomWidth = 4;
    public int maxRoomWidth = 10;
    public int minRoomLength = 4;
    public int maxRoomLength = 10;
    public int wallHeight = 3;

    [Header("Shape Complexity")]
    [Tooltip("How many rectangles to combine to make a single room shape.")]
    public int minComplexity = 1;
    public int maxComplexity = 3;

    [Header("Room Materials")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;

    // Tracks EVERY tile in the entire house to prevent overlaps
    private HashSet<Vector2Int> allHouseOccupiedTiles = new HashSet<Vector2Int>();
    private List<PlacedRoom> placedRooms = new List<PlacedRoom>();

    // Vars to handle toggling roof transparency
    private bool lastTransparencyState = false; // To ensure we only change roof materials once when converting them to and from transparency
    private List<GameObject> allRoomRoofs = new List<GameObject>(); // To hold all roofs generated and make them transparent later

    // Simple class to track where rooms ended up
    private class PlacedRoom
    {
        public HashSet<Vector2Int> tiles; // Local coords
        public Vector2Int worldOffset;
        public Vector2Int size;
    }

    private void Start()
    {
        GenerateAllRooms();

        // FUTURE: Include an array of Material slots later to randomly assign materials
    }

    public void GenerateAllRooms()
    {
        // Clear previous generation (optional, if calling multiple times)
        if (destroyPreviousGeneration)
        {
            // Have to start at the end of the list so that we can read each object then destory it without going out of bounds
            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);
        }

        // Reset room roofs so that only new roofs being generated are checked for, in case past ones were deleted
        allRoomRoofs.Clear();
        // Reset map of occupied tiles/placed rooms
        allHouseOccupiedTiles.Clear();
        placedRooms.Clear();

        // Generate the overall layout of the house/house borders
        HashSet<Vector2Int> houseLayout = GenerateHouseLayout();
        allHouseOccupiedTiles = new HashSet<Vector2Int>(houseLayout); // Save the layout so the wall-spawning logic knows where the outside of the house is

        // Subdivide the house layout into the desired num of rooms
        List<HashSet<Vector2Int>> rooms = SubdivideHouse(houseLayout, numberOfRooms);

        // Start the base roof height at 0 (global 0)
        float roofHeight = 0;

        for (int floor = 0; floor < numberOfFloors; floor++)
        {
            // Determine how many rooms for THIS floor
            int roomsOnCurrentFloor = (floor > 0 && roomAmountsDifferPerFloor && !identicalFloors) // If this is NOT the first floor and room numbers on each floor should differ
                ? Random.Range(numberOfRooms, numberOfRooms + 3) // Some houses might have just one room (e.g., a warehouse) so minimum must always be numberOfRooms for now
                : numberOfRooms; // Else, we stick to the universal/base num of rooms

            Debug.Log("Number of rooms on each floor should be " + numberOfRooms);

            // Need to subdivide houseLayout differently to get different room arrangements - otherwise, we'll have identical floors
            List<HashSet<Vector2Int>> floorRooms = (!identicalFloors)
                ? SubdivideHouse(houseLayout, roomsOnCurrentFloor)
                : rooms; // If identicalFloors is true, we skip new subdivision so the layout remains the same on all floors

            // Build at the current roofHeight
            for (int i = 0; i < floorRooms.Count; i++)
                BuildRoomGeometry(i, floorRooms[i], Vector2Int.zero, roofHeight); // Offset is now 0 because the house layout is already globally placed, height is 0 at first since we start on ground level

            // Get the highest point in all room roofs and build off that for the next floor
            roofHeight = GetHighestRoofPoint();
            // Debug.Log($"Floor {floor} complete. Next floor will be at: {roofHeight}");
        }

        // Check transparency toggle after rooms are made and automatically toggle transparency if necessary
        lastTransparencyState = roomRoofsTransparent;
        ToggleRoofTransparency();
    }

    // Creates the basic outline of a house, with nooks and complexity as determined by our vars
    HashSet<Vector2Int> GenerateHouseLayout()
    {
        HashSet<Vector2Int> coords = new HashSet<Vector2Int>();
        int complexity = Random.Range(minComplexity, maxComplexity + 1);

        for (int i = 0; i < complexity; i++) // Complexity here is the num of rectangles we are potentially smashing together into this layout
        {
            // Scale up the rectangles to represent sections of the house
            int width = Random.Range(minRoomWidth * 2, maxHouseWidth);
            int length = Random.Range(minRoomLength * 2, maxHouseLength);

            // Offset to create the L-shapes and nooks
            int startX = (i == 0) ? 0 : Random.Range(-width / 2, width / 2);
            int startY = (i == 0) ? 0 : Random.Range(-length / 2, length / 2);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < length; y++)
                {
                    coords.Add(new Vector2Int(startX + x, startY + y));
                }
            }
        }
        return coords;
    }

    // The looping logic to continuously call for splitting the space in the house
    List<HashSet<Vector2Int>> SubdivideHouse(HashSet<Vector2Int> layout, int targetRoomCount)
    {
        List<HashSet<Vector2Int>> rooms = new List<HashSet<Vector2Int>> { layout };

        int attempts = 0;
        // Keep looping until we hit our target room count or get stuck
        while (rooms.Count < targetRoomCount && attempts < 100)
        {
            attempts++;

            // Find the largest room we currently have to split it
            HashSet<Vector2Int> largestRoom = rooms.OrderByDescending(r => r.Count).First();

            // Attempt to split it. We need a function that finds its bounding box, picks a random X or Y line, and divides the HashSet into two.
            if (TrySplitRoom(largestRoom, out HashSet<Vector2Int> roomA, out HashSet<Vector2Int> roomB))
            {
                rooms.Remove(largestRoom);
                rooms.Add(roomA);
                rooms.Add(roomB);
            }
        }

        return rooms;
    }

    // Utilizes binary space partitioning method for procedural generation
    // (splits a space in half, then continuously divides to create reasonable subspaces to alter within the area)
    bool TrySplitRoom(HashSet<Vector2Int> currentRoom, out HashSet<Vector2Int> roomA, out HashSet<Vector2Int> roomB)
    {
        roomA = new HashSet<Vector2Int>();
        roomB = new HashSet<Vector2Int>();

        // Find the Bounding Box of this specific room chunk (since the rooms are in irregular shapes, we grab the individual bounding box of their chunks)
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (var tile in currentRoom)
        {
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }

        int width = maxX - minX + 1;
        int length = maxY - minY + 1;

        // Decide split direction. We generally want to split the longest axis to avoid thin hallways.
        bool splitVertical = width > length;

        // Add a bit of randomness so it isn't completely predictable, provided both sides are big enough
        if (width >= minRoomWidth * 2 && length >= minRoomLength * 2)
        {
            splitVertical = Random.value > 0.5f;
        }

        // Perform the slice
        if (splitVertical)
        {
            // Calculate valid range for the slice to ensure minRoomWidth is respected on both sides
            int minSplit = minX + minRoomWidth;
            int maxSplit = maxX - minRoomWidth + 1;

            // If the room is too small to split, cancel it - we'll have a larger, open space/room as a result
            if (minSplit > maxSplit) return false;

            // Pick a random line to draw the knife through
            int splitLine = Random.Range(minSplit, maxSplit);

            // Sort tiles into Room A or Room B based on the line
            foreach (var tile in currentRoom)
            {
                if (tile.x < splitLine) roomA.Add(tile);
                else roomB.Add(tile);
            }
        }
        else
        {
            // Same logic, but slicing horizontally along the Y axis
            int minSplit = minY + minRoomLength;
            int maxSplit = maxY - minRoomLength + 1;

            if (minSplit > maxSplit) return false;

            int splitLine = Random.Range(minSplit, maxSplit);

            foreach (var tile in currentRoom)
            {
                if (tile.y < splitLine) roomA.Add(tile);
                else roomB.Add(tile);
            }
        }

        // Because the house layout is irregular, a straight slice might occasionally catch an empty corner and make an empty room.
        // If that happens, reject the split.
        if (roomA.Count == 0 || roomB.Count == 0) return false;

        return true;
    }

    void BuildRoomGeometry(int id, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos, float heightOffset)
    {
        // Create the Parent GameObject
        GameObject roomParent = new GameObject($"Room_{id}");
        roomParent.transform.parent = this.transform;
        roomParent.transform.position = new Vector3(worldPos.x, heightOffset, worldPos.y);

        // Create sub-groups for the Floors, Walls, and Ceiling tiles so we can combine them later
        GameObject floorGroup = new GameObject("Floors");
        GameObject ceilingGroup = new GameObject("Ceilings");
        GameObject wallGroup = new GameObject("Walls");
        floorGroup.transform.parent = roomParent.transform;
        ceilingGroup.transform.parent = roomParent.transform;
        wallGroup.transform.parent = roomParent.transform;

        // An "undo" option to undo the generation if we didn't like it
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(roomParent, "Generate Room");
#endif

        // Build the Room Geometry
        foreach (Vector2Int coord in normalizedCoords)
        {
            // Position relative to the room parent
            Vector3 tilePos = new Vector3(coord.x, heightOffset, coord.y); // z was 0

            // Spawn Floor
            SpawnPrimitive(PrimitiveType.Cube, floorGroup.transform, tilePos, Vector3.one, "Floor");

            // Spawn Ceiling
            GameObject ceiling = SpawnPrimitive(PrimitiveType.Cube, ceilingGroup.transform, tilePos + Vector3.up * wallHeight, Vector3.one, "Ceiling");

            // Spawn Walls (Check neighbors)
            CheckAndSpawnWalls(coord, worldPos, normalizedCoords, wallGroup.transform, tilePos);

            // Here later, potentially call a separate script to spawn items?
        }

        // Bake the different groups of primitive child tiles into single objects
        CombineChildrenMeshes(floorGroup, floorMaterial);
        CombineChildrenMeshes(ceilingGroup, ceilingMaterial);
        CombineChildrenMeshes(wallGroup, wallMaterial);

        // After baking, capture the default material of the roof through our RoofData tracker
        RoofData data = ceilingGroup.AddComponent<RoofData>();
        data.originalMaterial = ceilingGroup.GetComponent<MeshRenderer>().sharedMaterial;
        allRoomRoofs.Add(ceilingGroup);
    }

    // Placing walls on the floors of generated rooms
    void CheckAndSpawnWalls(Vector2Int localCoord, Vector2Int worldOffset, HashSet<Vector2Int> roomTiles, Transform parent, Vector3 pos)
    {
        // Directions: Up, Down, Left, Right
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var dir in directions)
        {
            Vector2Int neighbor = localCoord + dir;
            //Vector2Int neighborWorld = (localCoord + worldOffset) + dir;

            // Is the neighbor inside this same room?
            if (roomTiles.Contains(neighbor))
                continue; // No wall needed, it's open floor

            // Is the neighbor still inside the house, but in a different room?
            if (allHouseOccupiedTiles.Contains(neighbor))
            {
                // Spawn an interior wall to divide the rooms.
                // FUTURE: Can later work in doorways at this point!!
                SpawnWall(pos, dir, parent, isInterior: true);
                continue;
            }

            // If it's not in the room or in the house layout, this neighbor space is outside
            // We spawn an exterior wall to block off the outside
            SpawnWall(pos, dir, parent, isInterior: false);
        }
    }

    void SpawnWall(Vector3 tilePos, Vector2Int dir, Transform parent, bool isInterior)
    {
        // Calculate wall position (offset by 0.5 towards the empty space so it is offset to the tile)
        Vector3 wallPos = tilePos + new Vector3(dir.x * 0.5f, wallHeight / 2f, dir.y * 0.5f);

        // Check which direction we're facing to determine which way the wall stretches
        // (walls are thin on one axis and long on another - we can make exterior walls a bit beefier (0.2) and interior walls thinner (0.1) on the thinner axis
        float thickness = isInterior ? 0.1f : 0.2f;

        // Stretch the wall based on which direction it is facing
        Vector3 wallScale = new Vector3(
            Mathf.Abs(dir.y) + (Mathf.Abs(dir.x) * thickness), // If dir is X, make wall thin on X
            wallHeight,
            Mathf.Abs(dir.x) + (Mathf.Abs(dir.y) * thickness) // If dir is Y, make wall thin on Y
        );

        string wallName = isInterior ? "Interior_Wall" : "Exterior_Wall";
        GameObject wall = SpawnPrimitive(PrimitiveType.Cube, parent, wallPos, wallScale, wallName);

        // FUTURE: Apply different material to interior/exterior walls
        /*
        if (!isInterior && exteriorWallMaterial != null) 
            wall.GetComponent<MeshRenderer>().material = exteriorWallMaterial;
        */
    }

    // Helpers to spawn primitives - could be used later for spawning primitive furniture etc.
    GameObject SpawnPrimitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, string name)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localScale = scale;
        return obj;
    }

    void CombineChildrenMeshes(GameObject parent, Material targetMaterial)
    {
        MeshFilter[] meshFilters = parent.GetComponentsInChildren<MeshFilter>();
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];

        // Prepare the combination data
        for (int i = 0; i < meshFilters.Length; i++)
        {
            combine[i].mesh = meshFilters[i].sharedMesh;
            // This ensures the cubes stay in their relative positions
            combine[i].transform = parent.transform.worldToLocalMatrix * meshFilters[i].transform.localToWorldMatrix;
            meshFilters[i].gameObject.SetActive(false); // Hide the original cubes
        }

        // Create the new mesh
        Mesh combinedMesh = new Mesh();
        combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Allows for large rooms
        combinedMesh.CombineMeshes(combine);

        // Apply the mesh to the parent
        MeshFilter mf = parent.AddComponent<MeshFilter>();
        mf.sharedMesh = combinedMesh;

        MeshRenderer mr = parent.AddComponent<MeshRenderer>();

        // Default to a standard material assigned from the Editor
        // If you forgot to assign one, it falls back to a basic one
        if (targetMaterial != null)
            mr.sharedMaterial = targetMaterial;
        else
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // Remove the old individual cube objects
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(parent.transform.GetChild(i).gameObject);
    }

    // Helper class to get the highest roof in the floor layouts we make
    // Done like this instead of using wallHeight in case we build irregular roofs in the future
    float GetHighestRoofPoint()
    {
        float highestY = 0;
        foreach (GameObject roof in allRoomRoofs)
        {
            if (roof == null) continue;

            // Bounds.max.y gives the highest point of the mesh in world space
            float topPoint = roof.GetComponent<MeshRenderer>().bounds.max.y;
            if (topPoint > highestY) highestY = topPoint;
        }
        return highestY;
    }

    private void OnValidate()
    {
        // Only run this if the bool actually changed, to save performance
        if (roomRoofsTransparent != lastTransparencyState)
        {
            lastTransparencyState = roomRoofsTransparent;
            ToggleRoofTransparency();
        }
    }

    public void ToggleRoofTransparency()
    {
        Material transMat = Resources.Load<Material>("Materials/transparent");

        foreach (GameObject roof in allRoomRoofs)
        {
            if (roof == null) continue; // Safety check in case a room was deleted

            MeshRenderer renderer = roof.GetComponent<MeshRenderer>();
            RoofData data = roof.GetComponent<RoofData>();

            if (roomRoofsTransparent)
            {
                // If the current material is not transparent, save what it is then swap out the material with transparent
                if (renderer.sharedMaterial != transMat)
                    data.originalMaterial = renderer.sharedMaterial;

                if (transMat != null) renderer.sharedMaterial = transMat;
            }
            else
            {
                // Switch the material back to its original one
                if (data != null && data.originalMaterial != null)
                    renderer.sharedMaterial = data.originalMaterial;
            }
        }
    }

    // Enable "Gizmos" in Scene View to see the occupied tiles map
    private void OnDrawGizmos()
    {
        if (allHouseOccupiedTiles != null)
        {
            Gizmos.color = new Color(1, 0, 0, 0.3f); // Semi-transparent Red
            foreach (var tile in allHouseOccupiedTiles)
            {
                // Adjust for current script transform if needed, or assume 0,0
                Vector3 pos = transform.position + new Vector3(tile.x, 0.5f, tile.y);
                Gizmos.DrawCube(pos, Vector3.one * 0.9f);
            }
        }
    }
}