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
    public float roomSpacing = 5f;
    public bool destroyPreviousGeneration = true;
    public bool roomRoofsTransparent = false;

    [Header("House Dimensions")]
    public int maxHouseWidth = 40;
    public int maxHouseLength = 40;

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

        // Include an array of Material slots later to randomly assign materials
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

        // Generate each of the rooms sequentially
        for (int i = 0; i < numberOfRooms; i++)
            PlaceRoom(i);

        // Check transparency toggle after rooms are made and automatically toggle transparency if necessary
        lastTransparencyState = roomRoofsTransparent;
        ToggleRoofTransparency();
    }

    void PlaceRoom(int id)
    {
        // Generate the Floor Plan (HashSet handles duplicates)
        HashSet<Vector2Int> floorCoordinates = GenerateFloorPlan();

        // Moves the source of the room to 0,0 inside the house plan
        HashSet<Vector2Int> normalizedCoords = NormalizeCoords(floorCoordinates, out Vector2Int roomSize);

        // Try to find a valid spot in the house plan
        Vector2Int finalOffset = Vector2Int.zero;
        bool foundSpot = false;

        if (placedRooms.Count == 0)
        {
            // Place first room in the local center 0,0
            finalOffset = new Vector2Int(0, 0);
            foundSpot = true;
        }
        else
        {
            // Try to snap new room onto an existing room
            foundSpot = FindSnapPosition(normalizedCoords, roomSize, out finalOffset);
        }

        if (foundSpot)
        {
            // Record tiles globally
            foreach (var coord in normalizedCoords) allHouseOccupiedTiles.Add(coord + finalOffset);

            // Track this room for future attachments
            placedRooms.Add(new PlacedRoom { tiles = normalizedCoords, worldOffset = finalOffset, size = roomSize });

            // Create the visual rooms
            BuildRoomGeometry(id, normalizedCoords, finalOffset);
        }
        else
        {
            Debug.LogWarning($"Could not find a valid spot for Room {id}. House might be too crowded.");
        }
    }

    bool FindSnapPosition(HashSet<Vector2Int> newRoomTiles, Vector2Int newRoomSize, out Vector2Int foundOffset)
    {
        foundOffset = Vector2Int.zero;

        // Create a random order list of existing rooms so we don't always build in a straight line
        List<PlacedRoom> potentialAnchors = placedRooms.OrderBy(x => Random.value).ToList();

        foreach (PlacedRoom anchor in potentialAnchors)
        {
            // Try all 4 sides of this anchor room
            // Order: North, South, East, West (shuffled for variety)
            int[] sides = { 0, 1, 2, 3 };
            ShuffleArray(sides);

            foreach (int side in sides)
            {
                // We scan along the edge of the anchor room to find a fit
                // This ensures we try every possible "Lego click" position

                // Determine the range we can slide the new room along the anchor
                int scanStart = 0;
                int scanEnd = 0;

                if (side == 0 || side == 1) // North/South: Slide along X
                {
                    scanStart = -newRoomSize.x + 1; // Start with the new room barely touching the left corner
                    scanEnd = anchor.size.x - 1;    // End with it barely touching the right corner
                }
                else // East/West: Slide along Y
                {
                    scanStart = -newRoomSize.y + 1;
                    scanEnd = anchor.size.y - 1;
                }

                // Randomize the scan direction so we don't always stack to the left
                List<int> scanOffsets = new List<int>();
                for (int k = scanStart; k <= scanEnd; k++) scanOffsets.Add(k);
                scanOffsets = scanOffsets.OrderBy(x => Random.value).ToList();

                foreach (int slide in scanOffsets)
                {
                    Vector2Int testOffset = Vector2Int.zero;

                    if (side == 0) // North
                        testOffset = anchor.worldOffset + new Vector2Int(slide, anchor.size.y);
                    else if (side == 1) // South
                        testOffset = anchor.worldOffset + new Vector2Int(slide, -newRoomSize.y);
                    else if (side == 2) // East
                        testOffset = anchor.worldOffset + new Vector2Int(anchor.size.x, slide);
                    else if (side == 3) // West
                        testOffset = anchor.worldOffset + new Vector2Int(-newRoomSize.x, slide);

                    // Check if this specific spot is valid
                    if (!CheckOverlap(newRoomTiles, testOffset))
                    {
                        // Check house bounds (Optional: Center the house around 0,0 conceptually)
                        if (IsInsideBounds(newRoomTiles, testOffset))
                        {
                            foundOffset = testOffset;
                            return true; // Found a spot! Stop looking.
                        }
                    }
                }
            }
        }
        return false;
    }

    void ShuffleArray<T>(T[] arr)
    {
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T temp = arr[i];
            arr[i] = arr[j];
            arr[j] = temp;
        }
    }

    bool IsInsideBounds(HashSet<Vector2Int> tiles, Vector2Int offset)
    {
        // Simple bounding box check centered around World 0,0
        int limitX = maxHouseWidth / 2;
        int limitZ = maxHouseLength / 2;

        foreach (var t in tiles)
        {
            Vector2Int pos = t + offset;
            if (pos.x < -limitX || pos.x > limitX || pos.y < -limitZ || pos.y > limitZ) return false;
        }
        return true;
    }

    // Checks if a room at a specific offset hits any existing tiles
    bool CheckOverlap(HashSet<Vector2Int> roomTiles, Vector2Int offset)
    {
        foreach (var tile in roomTiles)
        {
            // We check if the global map already has a tile at this specific world-coord
            if (allHouseOccupiedTiles.Contains(tile + offset)) return true;
        }
        return false;
    }

    // Generate different shapes of rooms
    HashSet<Vector2Int> GenerateFloorPlan()
    {
        HashSet<Vector2Int> coords = new HashSet<Vector2Int>();
        int complexity = Random.Range(minComplexity, maxComplexity + 1);

        for (int roomSections = 0; roomSections < complexity; roomSections++)
        {
            // Create a random rectangle
            int width = Random.Range(minRoomWidth, maxRoomWidth);
            int length = Random.Range(minRoomLength, maxRoomLength);

            // Offset the rectangle slightly to create overlaps (nooks/corners)
            // We keep the first rectangle at (0,0)
            int startX = (roomSections == 0) ? 0 : Random.Range(-width / 2, width / 2);
            int startY = (roomSections == 0) ? 0 : Random.Range(-length / 2, length / 2);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < length; y++)
                    coords.Add(new Vector2Int(startX + x, startY + y));
            }
        }
        return coords;
    }

    // Moves any raw set of coordinates to start at 0,0 and returns the room's size
    HashSet<Vector2Int> NormalizeCoords(HashSet<Vector2Int> input, out Vector2Int size)
    {
        Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
        Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
        foreach (var c in input)
        {
            if (c.x < min.x) min.x = c.x; if (c.y < min.y) min.y = c.y;
            if (c.x > max.x) max.x = c.x; if (c.y > max.y) max.y = c.y;
        }
        size = new Vector2Int(max.x - min.x + 1, max.y - min.y + 1);
        HashSet<Vector2Int> output = new HashSet<Vector2Int>();
        foreach (var c in input) output.Add(c - min);
        return output;
    }

    void BuildRoomGeometry(int id, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos)
    {
        // Create the Parent GameObject
        GameObject roomParent = new GameObject($"Room_{id}");
        roomParent.transform.parent = this.transform;
        roomParent.transform.position = new Vector3(worldPos.x, 0, worldPos.y);

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
            Vector3 tilePos = new Vector3(coord.x, 0, coord.y);

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
            Vector2Int neighborLocal = localCoord + dir;
            Vector2Int neighborWorld = (localCoord + worldOffset) + dir;

            // If a neighbor is in this current room, no wall is needed
            if (roomTiles.Contains(neighborLocal)) continue;

            // If neighbor is in another room, we don't spawn a wall (this creates a doorway/opening)
            // If we want a wall between rooms, remove this line
            if (allHouseOccupiedTiles.Contains(neighborWorld)) continue;

            // Otherwise, we hit the edge of the house, so we spawn a wall
            // Calculate wall position (offset by 0.5 towards the empty space)
            Vector3 wallPos = pos + new Vector3(dir.x * 0.5f, wallHeight / 2f, dir.y * 0.5f);

            // Scale the wall to fill the gap - check which direction we're facing to determine which way the wall stretches
            // (walls are thin on one axis and long on another - all our walls should be 0.1f thin)
            Vector3 wallScale = new Vector3(
                Mathf.Abs(dir.y) + (Mathf.Abs(dir.x) * 0.1f), // If dir is X, make wall thin on X
                wallHeight,
                Mathf.Abs(dir.x) + (Mathf.Abs(dir.y) * 0.1f)  // If dir is Y, make wall thin on Y
            );

            SpawnPrimitive(PrimitiveType.Cube, parent, wallPos, wallScale, "Wall");
        }
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