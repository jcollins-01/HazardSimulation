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
    public int minHouseWidth = 20;
    public int maxHouseWidth = 40;
    public int minHouseLength = 20;
    public int maxHouseLength = 40;

    [Header("Placement Settings")]
    [Tooltip("How many times to try finding a spot for a room before giving up.")]
    public int maxPlacementAttempts = 50;

    // Tracks EVERY tile in the entire house to prevent overlaps
    private HashSet<Vector2Int> allHouseOccupiedTiles = new HashSet<Vector2Int>();

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

    // Internal tracker to space rooms out
    private float currentWorldX = 0f;

    // Vars to handle toggling roof transparency
    private bool lastTransparencyState = false; // To ensure we only change roof materials once when converting them to and from transparency
    private List<GameObject> allRoomRoofs = new List<GameObject>(); // To hold all roofs generated and make them transparent later

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

        // Determine the actual house area for this generation
        int houseW = Random.Range(minHouseWidth, maxHouseWidth);
        int houseL = Random.Range(minHouseLength, maxHouseLength);

        // Reset the starting/spawn point of the rooms to 0
        currentWorldX = 0;

        for (int i = 0; i < numberOfRooms; i++)
            CreateRoom(i, houseW, houseL);

        // Check transparency toggle after rooms are made and automatically toggle transparency if necessary
        lastTransparencyState = roomRoofsTransparent;
        ToggleRoofTransparency();
    }

    void CreateRoom(int id, int houseW, int houseL)
    {
        // Generate the Floor Plan (HashSet handles duplicates)
        HashSet<Vector2Int> floorCoordinates = GenerateFloorPlan();

        // Moves the source of the room to 0,0 inside the house plan
        HashSet<Vector2Int> normalizedCoords = NormalizeCoords(floorCoordinates, out Vector2Int size);

        // Try to find a valid spot in the house plan
        Vector2Int finalOffset = Vector2Int.zero;
        bool foundSpot = false;

        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            // Pick a random spot within the house bounds
            // Subtract room size so it doesn't bleed outside the house area
            int x = Random.Range(0, Mathf.Max(1, houseW - size.x));
            int z = Random.Range(0, Mathf.Max(1, houseL - size.y));
            Vector2Int testOffset = new Vector2Int(x, z);

            if (!CheckOverlap(normalizedCoords, testOffset))
            {
                finalOffset = testOffset;
                foundSpot = true;
                break;
            }
        }

        // If we couldn't find a spot, we'll skip this room to keep the house clean
        if (!foundSpot)
        {
            Debug.LogWarning($"Could not find a spot for Room_{id} after {maxPlacementAttempts} tries.");
            return;
        }

        // Save the tile placements we chose in the global coords so the next room won't hit them
        foreach (var coord in normalizedCoords)
            allHouseOccupiedTiles.Add(coord + finalOffset);

        // Create the Parent GameObject
        GameObject roomParent = new GameObject($"Room_{id}");
        roomParent.transform.parent = this.transform;
        roomParent.transform.position = new Vector3(finalOffset.x, 0, finalOffset.y);

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
            GameObject ceiling = SpawnAndSavePrimitive(PrimitiveType.Cube, ceilingGroup.transform, tilePos + Vector3.up * wallHeight, Vector3.one, "Ceiling");

            // Spawn Walls (Check neighbors)
            CheckAndSpawnWalls(coord, normalizedCoords, wallGroup.transform, tilePos);

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

    // Placing walls on the floors of generated rooms
    void CheckAndSpawnWalls(Vector2Int current, HashSet<Vector2Int> allTiles, Transform parent, Vector3 pos)
    {
        // Directions: Up, Down, Left, Right
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var dir in directions)
        {
            Vector2Int neighbor = current + dir;

            // If the neighbor is NOT in our floor plan for the current room, then we're on the edge of our room
            // Place a wall to separate "stranger" neighbors from our room tiles
            if (!allTiles.Contains(neighbor))
            {
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
    }

    // Helpers to spawn primitives - could be used later for spawning primitive furniture etc.
    void SpawnPrimitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, string name)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localScale = scale;
    }

    GameObject SpawnAndSavePrimitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, string name)
    {
        GameObject obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localScale = scale;

        return obj;
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

    // Checks if a room at a specific offset hits any existing tiles
    bool CheckOverlap(HashSet<Vector2Int> roomTiles, Vector2Int offset)
    {
        foreach (var tile in roomTiles)
        {
            // We check if the global map already has a tile at this specific world-coord
            if (allHouseOccupiedTiles.Contains(tile + offset)) return true;

            // Could add a 1-tile "buffer" check here if we want space between rooms
            // if (allHouseOccupiedTiles.Contains(tile + offset + Vector2Int.up)... etc.
        }
        return false;
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
}