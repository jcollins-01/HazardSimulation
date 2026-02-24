using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomGeneration : MonoBehaviour
{
    [Header("Preset Generation Settings")]
    private bool dormitory = false;
    private bool warehouse = false;
    private bool smallHouse = false;
    private bool twoStoryHouse = false;
    private bool skyscraper = false;

    [Header("Custom Generation Settings")]
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

    // Room variables to track privately
    private int stairDepth = 5; // Makes for an angle of 31 degrees, architectural height for a comfortable set of stairs

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
        // Check for any preset values we want to follow
        //CheckPresetLayouts();
        
        // Generate houses
        GenerateAllRooms();

        // FUTURE: Include an array of Material slots later to randomly assign materials
    }

    private void CheckPresetLayouts()
    {
        if (dormitory)
        {
            numberOfRooms = 10;
            numberOfFloors = 4;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            maxHouseWidth = 20;
            maxHouseLength = 30;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 3;
        }

        if (warehouse)
        {
            numberOfRooms = 1;
            numberOfFloors = 1;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            maxHouseWidth = 20;
            maxHouseLength = 40;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 6;
            minComplexity = 1;
            maxComplexity = 1;
        }

        if (smallHouse)
        {
            numberOfRooms = 4;
            numberOfFloors = 1;
            identicalFloors = false;
            roomAmountsDifferPerFloor = false;
            maxHouseWidth = 15;
            maxHouseLength = 15;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 4;
        }

        if (twoStoryHouse)
        {
            numberOfRooms = 4;
            numberOfFloors = 2;
            identicalFloors = false;
            roomAmountsDifferPerFloor = true;
            maxHouseWidth = 15;
            maxHouseLength = 15;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 4;
        }

        if (skyscraper)
        {
            numberOfRooms = 10;
            numberOfFloors = 30;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            maxHouseWidth = 20;
            maxHouseLength = 20;
            minRoomWidth = 6;
            maxRoomWidth = 6;
            minRoomLength = 6;
            maxRoomLength = 6;
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 2;
        }
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

        // Create a master House parent to hold the layout in
        GameObject houseParent = new GameObject("House");
        houseParent.transform.SetParent(this.transform);
        houseParent.transform.localPosition = Vector3.zero;

        // Generate the overall layout of the house/house borders
        HashSet<Vector2Int> houseLayout = GenerateHouseLayout();
        allHouseOccupiedTiles = new HashSet<Vector2Int>(houseLayout); // Save the layout so the wall-spawning logic knows where the outside of the house is

        // Pick one random tile from the layout to serve as the stairwell for all floors
        //Vector2Int stairwellTile = houseLayout.ElementAt(Random.Range(0, houseLayout.Count));
        Vector2Int stairwellTile = FindStairwellTile(houseLayout);

        // Subdivide the house layout into the desired num of rooms
        List <HashSet<Vector2Int>> rooms = SubdivideHouse(houseLayout, numberOfRooms);

        // Start the base roof height at 0 (global 0)
        float roofHeight = 0;

        for (int floor = 0; floor < numberOfFloors; floor++)
        {
            // Create the iterative Floor parent to hold each generated floor in
            GameObject floorParent = new GameObject($"Floor_{floor}");
            floorParent.transform.SetParent(houseParent.transform);
            floorParent.transform.localPosition = Vector3.zero;

            // Determine how many rooms for THIS floor
            int roomsOnCurrentFloor = (floor > 0 && roomAmountsDifferPerFloor && !identicalFloors) // If this is NOT the first floor and room numbers on each floor should differ
                ? Random.Range(numberOfRooms, numberOfRooms + 3) // Some houses might have just one room (e.g., a warehouse) so minimum must always be numberOfRooms for now
                : numberOfRooms; // Else, we stick to the universal/base num of rooms

            // Need to subdivide houseLayout differently to get different room arrangements - otherwise, we'll have identical floors
            List<HashSet<Vector2Int>> floorRooms = (!identicalFloors)
                ? SubdivideHouse(houseLayout, roomsOnCurrentFloor)
                : rooms; // If identicalFloors is true, we skip new subdivision so the layout remains the same on all floors

            // Generate doorways for this specific floor layout (now that we have the full layout)
            HashSet<string> floorDoors = GenerateDoorsForFloor(floorRooms);

            // Spawn a ramp to connect floors if this is the stairwell (and not the top floor)
            if (floor < numberOfFloors - 1) 
                SpawnStairs(stairwellTile, roofHeight, floorParent.transform, stairDepth);

            // Build at the current roofHeight
            for (int i = 0; i < floorRooms.Count; i++)
                BuildRoomGeometry(i, floor, floorRooms[i], Vector2Int.zero, roofHeight, floorParent.transform, stairwellTile, floorDoors); // Offset is now 0 because the house layout is already globally placed, height is 0 at first since we start on ground level

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

    void BuildRoomGeometry(int id, int floor, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos, float heightOffset, Transform parentFloor, Vector2Int stairTile, HashSet<string> floorDoors)
    {
        // Create the Parent GameObject
        GameObject roomParent = new GameObject($"Room_{id}");
        roomParent.transform.SetParent(parentFloor);
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
            //bool isStair = (coord == stairTile);
            bool isStairArea = IsInStairwell(coord, stairTile, stairDepth);

            // Spawn Floor (skip if it's the stairwell, UNLESS it's the ground floor)
            if (!isStairArea || floor == 0)
                SpawnPrimitive(PrimitiveType.Cube, floorGroup.transform, tilePos, Vector3.one, "Floor");

            // Spawn Ceiling (skip if it's the stairwell, UNLESS it's the very top floor/roof)
            if (!isStairArea || floor == numberOfFloors - 1)
                SpawnPrimitive(PrimitiveType.Cube, ceilingGroup.transform, tilePos + Vector3.up * wallHeight, Vector3.one, "Ceiling");

            // Spawn Walls (Check neighbors)
            CheckAndSpawnWalls(coord, worldPos, normalizedCoords, wallGroup.transform, tilePos, floorDoors, stairTile, stairDepth);

            // FUTURE: call a separate script to spawn items in the spaces
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
    void CheckAndSpawnWalls(Vector2Int localCoord, Vector2Int worldOffset, HashSet<Vector2Int> roomTiles, Transform parent, Vector3 pos, HashSet<string> floorDoors, Vector2Int stairTile, int stairDepth)
    {
        // Directions: Up, Down, Left, Right
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var dir in directions)
        {
            Vector2Int neighbor = localCoord + dir;

            // Is the neighbor inside this same room?
            if (roomTiles.Contains(neighbor))
                continue; // No wall needed, it's open floor

            // Check if this is an exterior wall
            bool isOutsideHouse = !allHouseOccupiedTiles.Contains(neighbor);

            // Skip the walls if both tiles are in the stairwell layout and it's not an exterior wall
            // (prevents the outside wall from getting a hole punched in it)
            if (!isOutsideHouse)
            {
                if (IsInStairwell(localCoord, stairTile, stairDepth) && IsInStairwell(neighbor, stairTile, stairDepth))
                    continue; // Don't build walls inside the stairwell corridor!

                // Check if the specific interior wall tile is on the list of doors
                string edge = GetEdgeKey(localCoord, neighbor);
                if (floorDoors.Contains(edge))
                {
                    // FUTURE: Add the door prefab that we want to spawn later
                    continue; // Skip spawning the wall since this is a door
                }
            }

            // Is the neighbor still inside the house, but in a different room?
            if (allHouseOccupiedTiles.Contains(neighbor))
            {
                // Spawn an interior wall to divide the rooms.
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

    Vector2Int FindStairwellTile (HashSet<Vector2Int> houseLayout)
    {
        // Find a valid stairwell location with enough "runway" behind it
        // Convert HashSet to List to shuffle and find a spot
        var possibleTiles = houseLayout.OrderBy(t => Random.value).ToList();

        foreach (var tile in possibleTiles)
        {
            bool runwayClear = true;
            for (int i = 0; i < stairDepth; i++)
            {
                // Check if the tiles behind this one (where the ramp will be) exist in the house
                if (!houseLayout.Contains(new Vector2Int(tile.x, tile.y - i)))
                {
                    runwayClear = false;
                    break;
                }
            }

            if (runwayClear) return tile;
        }

        // Fallback if the house is too small/complex for a 4-tile ramp
        return houseLayout.First();
    }

    void SpawnStairs(Vector2Int topTile, float heightOffset, Transform parent, int depth)
    {
        // 1. Calculate the horizontal run
        // The ramp starts at (topTile.y - depth + 1) and ends at topTile.y
        float startZ = (float)topTile.y - depth + 1;
        float endZ = (float)topTile.y;
        float centerZ = (startZ + endZ) / 2f;

        // 2. Calculate the vertical rise
        // Floors are 1 unit thick. Surface is height + 0.5.
        // We want to go from the surface of this floor to the surface of the next.
        float surfaceBottom = heightOffset + 0.5f;
        float surfaceTop = heightOffset + wallHeight + 0.5f;
        float centerY = (surfaceBottom + surfaceTop) / 2f;

        // 3. Math for Scale and Angle
        float run = depth;
        float rise = wallHeight;
        float rampLength = Mathf.Sqrt((run * run) + (rise * rise));
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

        // 4. Spawn
        Vector3 rampPos = new Vector3(topTile.x, centerY, centerZ);
        GameObject ramp = SpawnPrimitive(PrimitiveType.Cube, parent, rampPos, new Vector3(0.9f, 0.1f, rampLength), "Stair_Ramp");

        // Rotate around the center
        ramp.transform.rotation = Quaternion.Euler(-angle, 0, 0);

        if (floorMaterial != null)
            ramp.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
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

    // Normalizes an edge between two tiles so Tile A -> Tile B is the same as Tile B -> Tile A (helps us find tiles touching both walls)
    string GetEdgeKey(Vector2Int a, Vector2Int b)
    {
        if (a.x < b.x || (a.x == b.x && a.y < b.y))
            return $"{a.x},{a.y}_{b.x},{b.y}";
        else
            return $"{b.x},{b.y}_{a.x},{a.y}";
    }

    // Finds all adjacent rooms on a floor and creates a REALISTIC path through the house using a minimum spanning tree method for procedural generation
    // (considers each room in the layout as one node, generates a map of all the routes necessary to have each node connected, WITHOUT drawing every possible line between them)
    HashSet<string> GenerateDoorsForFloor(List<HashSet<Vector2Int>> rooms)
    {
        HashSet<string> doors = new HashSet<string>();
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        // Gather all possible shared walls between all rooms (key: "room A index, room B index" , value: list of shared edge keys)
        Dictionary<string, List<string>> roomConnections = new Dictionary<string, List<string>>();

        // Compare every room against every other room
        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                // Check every tile in Room A to see if it touches Room B
                List<string> shared = GetSharedEdges(rooms[i], rooms[j]);

                if (shared.Count > 0)
                {
                    roomConnections.Add($"{i}_{j}", shared);
                }
            }
        }

        // Use a union to find all connected rooms (groups of rooms sharing the same walls) and ensure connections via MINIMUM necessary doors
        int[] parents = Enumerable.Range(0, rooms.Count).ToArray();
        int Find(int i) => parents[i] == i ? i : parents[i] = Find(parents[i]);

        // Shuffle the connections so the house layout feels random and organic
        var connectionKeys = roomConnections.Keys.OrderBy(x => Random.value).ToList();

        // Connect the rooms in their new paths
        foreach (var key in connectionKeys)
        {
            string[] parts = key.Split('_');
            int r1 = int.Parse(parts[0]);
            int r2 = int.Parse(parts[1]);

            List<string> possibleEdges = roomConnections[key];

            if (Find(r1) != Find(r2))
            {
                // Pick exactly one edge from the shared list to connect them
                doors.Add(possibleEdges[Random.Range(0, possibleEdges.Count)]);
                parents[Find(r1)] = Find(r2);
            }
            // If they are ALREADY connected (indirectly through other rooms), have a random 5% chance to add a door anyway to create a realistic loop
            else if (Random.value < 0.05f)
            {
                Debug.Log("[RARE EVENT]: Added a natural loop!");   
                doors.Add(possibleEdges[Random.Range(0, possibleEdges.Count)]);
            }
        }

        return doors;
    }

    // Check if a tile is part of the stairwell layout
    bool IsInStairwell(Vector2Int coord, Vector2Int topTile, int depth)
    {
        // The ramp covers 'depth' tiles, ending at topTile.y and going backwards
        for (int i = 0; i < depth; i++)
        {
            if (coord.x == topTile.x && coord.y == (topTile.y - i))
                return true;
        }
        return false;
    }

    // Helper to find where the rooms are touching
    List<string> GetSharedEdges(HashSet<Vector2Int> roomA, HashSet<Vector2Int> roomB)
    {
        List<string> edges = new List<string>();
        foreach (var tile in roomA)
        {
            if (roomB.Contains(tile + Vector2Int.up)) edges.Add(GetEdgeKey(tile, tile + Vector2Int.up));
            if (roomB.Contains(tile + Vector2Int.down)) edges.Add(GetEdgeKey(tile, tile + Vector2Int.down));
            if (roomB.Contains(tile + Vector2Int.left)) edges.Add(GetEdgeKey(tile, tile + Vector2Int.left));
            if (roomB.Contains(tile + Vector2Int.right)) edges.Add(GetEdgeKey(tile, tile + Vector2Int.right));
        }
        return edges;
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
}