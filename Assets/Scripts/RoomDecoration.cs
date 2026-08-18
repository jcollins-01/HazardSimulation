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

    [Header("Prefabs for Decor")]
    [Tooltip("Right-click this component and view options to restore all prefabs to their defaults in Resources.")]
    public GameObject bedPrefab;
    public GameObject nightstandPrefab;
    public GameObject sinkAndToiletPrefab;
    public GameObject tubPrefab;
    public GameObject couchPrefab;
    public GameObject tvPrefab;
    public GameObject tablePrefab;
    public GameObject ovenPrefab;
    public GameObject fridgePrefab;
    public GameObject counterPrefab;
    public GameObject diningTablePrefab;
    public GameObject chairPrefab;
    public GameObject sideTablePrefab;
    public GameObject shelfPrefab;

    [HideInInspector]
    public bool defaultsLoaded = false; // Ensures we only auto-load defaults once

    // This is called automatically when the script is first attached to a GameObject,
    // or when we click "Reset" in the component's context menu.
    private void Reset()
    {
        LoadDefaultPrefabs();
        defaultsLoaded = true;
    }

    // Adds a right-click option on the component to manually reload the defaults at any time
    [ContextMenu("Restore Default Resource Prefabs")]
    private void LoadDefaultPrefabs()
    {
        // Do it regardless of it being null so that we can reset them back, even if we change them
        bedPrefab = Resources.Load<GameObject>("Interior Prefabs/Bed");
        nightstandPrefab = Resources.Load<GameObject>("Interior Prefabs/Nightstand");
        sinkAndToiletPrefab = Resources.Load<GameObject>("Interior Prefabs/Sink and Toilet");
        tubPrefab = Resources.Load<GameObject>("Interior Prefabs/Tub");
        couchPrefab = Resources.Load<GameObject>("Interior Prefabs/Couch");
        tvPrefab = Resources.Load<GameObject>("Interior Prefabs/TV");
        tablePrefab = Resources.Load<GameObject>("Interior Prefabs/Table");
        ovenPrefab = Resources.Load<GameObject>("Interior Prefabs/Stove");
        fridgePrefab = Resources.Load<GameObject>("Interior Prefabs/Fridge+Oven");
        counterPrefab = Resources.Load<GameObject>("Interior Prefabs/Counter");
        diningTablePrefab = Resources.Load<GameObject>("Interior Prefabs/Dining Table");
        chairPrefab = Resources.Load<GameObject>("Interior Prefabs/Chair");
        sideTablePrefab = Resources.Load<GameObject>("Interior Prefabs/Small Table");
        shelfPrefab = Resources.Load<GameObject>("Interior Prefabs/Shelf");
    }

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
    }

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
            case "Kitchen":
                SpawnKitchenFurniture(room);
                break;
            case "Dining Room":
                SpawnDiningRoomFurniture(room);
                break;
            case "Hallway":
                SpawnHallwayClutter(room);
                break;
            case "Generic":
            default:
                SpawnGenericFurniture(room);
                break;
        }
    }

    private void SpawnBedroomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        if (edges.Count > 0 && bedPrefab != null)
        {
            // Pick a random wall for the bed to sit flush against
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(bedPrefab, edge.tile, edge.forward, room.RoomObject.transform, room.Tiles);

            // Use the clutter slider to decide if we spawn a nightstand
            if (Random.value <= clutterAmount && nightstandPrefab != null)
            {
                // Calculate the tile directly to the side of the bed
                Vector2Int rightDir = new Vector2Int(Mathf.RoundToInt(edge.forward.z), Mathf.RoundToInt(-edge.forward.x));
                Vector2Int nightstandTile = edge.tile + rightDir;

                // Ensure we aren't spawning the nightstand outside the room boundaries
                if (room.Tiles.Contains(nightstandTile))
                {
                    SpawnFurniture(nightstandPrefab, nightstandTile, edge.forward, room.RoomObject.transform, room.Tiles);
                }
            }
        }
    }

    private void SpawnBathroomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Standard placement for core bathroom fixtures
        if (edges.Count >= 2)
        {
            if (tubPrefab != null) SpawnFurniture(tubPrefab, edges[0].tile, edges[0].forward, room.RoomObject.transform, room.Tiles);
            if (sinkAndToiletPrefab != null) SpawnFurniture(sinkAndToiletPrefab, edges[1].tile, edges[1].forward, room.RoomObject.transform, room.Tiles);
        }
    }

    private void SpawnLivingRoomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        if (edges.Count > 0 && tvPrefab != null && couchPrefab != null)
        {
            // Pick a wall for the TV
            var tvEdge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(tvPrefab, tvEdge.tile, tvEdge.forward, room.RoomObject.transform, room.Tiles);

            // Translate the Vector3 forward direction into a Vector2Int for tile math
            Vector2Int tvDirection = new Vector2Int(Mathf.RoundToInt(tvEdge.forward.x), Mathf.RoundToInt(tvEdge.forward.z));

            // Push the sofa 2 tiles away from the TV, into the center of the room
            Vector2Int sofaTile = tvEdge.tile + (tvDirection * 2);

            if (room.Tiles.Contains(sofaTile))
            {
                // Invert the TV's forward vector so the sofa faces back at it
                Vector3 sofaForward = -tvEdge.forward;
                SpawnFurniture(couchPrefab, sofaTile, sofaForward, room.RoomObject.transform, room.Tiles);

                // If clutter is high enough, place a coffee table between them
                if (Random.value <= clutterAmount && tablePrefab != null)
                {
                    Vector2Int tableTile = tvEdge.tile + tvDirection; // 1 tile away from TV
                    if (room.Tiles.Contains(tableTile))
                    {
                        Vector3 tablePos = new Vector3(tableTile.x, 0, tableTile.y);
                        GameObject table = Instantiate(tablePrefab, tablePos, Quaternion.identity, room.RoomObject.transform);
                        AddFireProfileCollider(table);
                    }
                }
            }
        }
    }

    private void SpawnKitchenFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Kitchens need at least 3 walls to feel functional
        if (edges.Count >= 3)
        {
            // Oven against one wall
            SpawnFurniture(ovenPrefab, edges[0].tile, edges[0].forward, room.RoomObject.transform, room.Tiles);

            // Fridge against another
            SpawnFurniture(fridgePrefab, edges[1].tile, edges[1].forward, room.RoomObject.transform, room.Tiles);

            // Counter against the third
            if (Random.value <= clutterAmount)
                SpawnFurniture(counterPrefab, edges[2].tile, edges[2].forward, room.RoomObject.transform, room.Tiles);
        }
    }

    private void SpawnDiningRoomFurniture(RoomGeneration.RoomData room)
    {
        if (diningTablePrefab == null) return;
        
        // Place table in center
        Vector2Int center = GetRoomCenter(room.Tiles);
        // Only spawn chairs if the table spawned successfully
        if (SpawnFurniture(diningTablePrefab, center, Vector3.forward, room.RoomObject.transform, room.Tiles))
        {
            Vector2Int[] chairOffsets = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            foreach (var offset in chairOffsets)
            {
                Vector2Int chairTile = center + offset;
                if (room.Tiles.Contains(chairTile))
                {
                    Vector3 lookDir = new Vector3(-offset.x, 0, -offset.y);
                    SpawnFurniture(chairPrefab, chairTile, lookDir, room.RoomObject.transform, room.Tiles);
                }
            }
        }
    }

    private void SpawnHallwayClutter(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);

        // Only spawn if enough room and high enough clutter setting
        if (clutterAmount > 0.4f && edges.Count > 0 && sideTablePrefab != null)
        {
            var edge = edges[Random.Range(0, edges.Count)];
            SpawnFurniture(sideTablePrefab, edge.tile, edge.forward, room.RoomObject.transform, room.Tiles);
        }
    }

    private void SpawnGenericFurniture(RoomGeneration.RoomData room)
    {
        // Base spawn
        Vector2Int centerTile = GetRoomCenter(room.Tiles);
        if (tablePrefab != null)
        {
            Vector3 centerPos = new Vector3(centerTile.x, 0, centerTile.y);
            GameObject table = Instantiate(tablePrefab, centerPos, Quaternion.identity, room.RoomObject.transform);
            AddFireProfileCollider(table);
        }

        // Clutter spawn
        if (Random.value <= clutterAmount)
        {
            List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
            if (edges.Count > 0 && shelfPrefab != null)
            {
                var edge = edges[Random.Range(0, edges.Count)];
                SpawnFurniture(shelfPrefab, edge.tile, edge.forward, room.RoomObject.transform, room.Tiles);
            }
        }
    }

    private void AddFireProfileCollider(GameObject furniture)
    {
        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"Cannot add a fire profile to {furniture.name}: no renderer was found.", furniture);
            return;
        }

        // FireProfileController requires the Ignis, temperature, and box-collider
        // components, so adding it completes the runtime fire setup.
        FireProfileController fireController = furniture.GetComponent<FireProfileController>();
        if (fireController == null)
            fireController = furniture.AddComponent<FireProfileController>();

        BoxCollider fireBox = fireController.collider;
        if (fireBox == null)
        {
            fireBox = furniture.GetComponent<BoxCollider>();
            fireController.collider = fireBox;
        }

        if (fireBox == null)
        {
            Debug.LogError($"Cannot configure the fire volume for {furniture.name}: no BoxCollider was created.", furniture);
            return;
        }

        // Renderer.bounds is in world space while BoxCollider center/size are local.
        // Convert every renderer-bound corner into the furniture root's local space
        // so Ignis places its flame VFX on the visible object.
        Bounds localBounds = CalculateLocalRendererBounds(furniture.transform, renderers);
        fireBox.center = localBounds.center;
        fireBox.size = localBounds.size;
    }

    private static Bounds CalculateLocalRendererBounds(Transform root, Renderer[] renderers)
    {
        Bounds combinedBounds = default;
        bool hasBounds = false;

        foreach (Renderer rendererToMeasure in renderers)
        {
            if (rendererToMeasure == null || rendererToMeasure is ParticleSystemRenderer)
                continue;

            Bounds rendererBounds = rendererToMeasure.localBounds;
            Vector3 center = rendererBounds.center;
            Vector3 extents = rendererBounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 rendererLocalCorner = center + Vector3.Scale(
                            extents,
                            new Vector3(x, y, z));
                        Vector3 rootLocalCorner = root.InverseTransformPoint(
                            rendererToMeasure.transform.TransformPoint(rendererLocalCorner));

                        if (!hasBounds)
                        {
                            combinedBounds = new Bounds(rootLocalCorner, Vector3.zero);
                            hasBounds = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(rootLocalCorner);
                        }
                    }
                }
            }
        }

        return hasBounds ? combinedBounds : new Bounds(Vector3.zero, Vector3.one);
    }

    private bool SpawnFurniture(GameObject prefab, Vector2Int tile, Vector3 forward, Transform parent, HashSet<Vector2Int> roomTiles)
    {
        // Start at ground level (Y = 0)
        Vector3 pos = new Vector3(tile.x, 0f, tile.y);

        // Explicitly define Vector3.up to prevent upside-down flipping
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        // Instantiate FIRST, so we can measure its actual bounds before finalizing placement
        GameObject instance = Instantiate(prefab, pos, rotation, parent);

        // Generate a BoxCollider based on the renderers of the prefab
        AddFireProfileCollider(instance);
        BoxCollider boxCol = instance.GetComponent<BoxCollider>();

        if (boxCol != null)
        {
            // Find where the back of the bounding box is in the object's local space
            float localBackZ = boxCol.center.z - (boxCol.size.z / 2f);

            // Align that local back edge to the edge of the 1x1 tile (-0.5f from center)
            float padding = 0.02f; // Pulls the object 2cm away from the wall
            float requiredZOffset = -0.5f - localBackZ + padding;
            instance.transform.position += forward * requiredZOffset;

            // Check collisions with furniture and blocking doorways
            int furnitureLayer = LayerMask.GetMask("Furniture");

            Collider[] allColliders = instance.GetComponentsInChildren<Collider>();
            foreach (Collider col in allColliders) col.enabled = false;

            Vector3 worldCenter = instance.transform.TransformPoint(boxCol.center);
            Vector3 checkExtents = (boxCol.size / 2f) * 0.95f; // Shrink slightly to avoid micro-overlaps

            Collider[] hits = Physics.OverlapBox(worldCenter, checkExtents, instance.transform.rotation);
            bool isBlocked = false;

            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(instance.transform)) continue;

                // Reject if it hits other furniture OR if it detects a doorway/window structure
                if (((1 << hit.gameObject.layer) & furnitureLayer) != 0 ||
                    hit.name.Contains("Door") || hit.name.Contains("Window"))
                {
                    Debug.LogWarning($"[Spawn Blocked] {prefab.name} blocked by {hit.name} at {tile}.");
                    isBlocked = true;
                    break;
                }
            }

            if (isBlocked)
            {
                DestroyImmediate(instance);
                return false;
            }

            foreach (Collider col in allColliders) col.enabled = true;

            // Project the 4 corners of the collider onto the grid to ensure wide objects don't clip side walls
            Vector3[] localCorners = {
            new Vector3(boxCol.center.x - checkExtents.x, boxCol.center.y, boxCol.center.z - checkExtents.z),
            new Vector3(boxCol.center.x + checkExtents.x, boxCol.center.y, boxCol.center.z - checkExtents.z),
            new Vector3(boxCol.center.x - checkExtents.x, boxCol.center.y, boxCol.center.z + checkExtents.z),
            new Vector3(boxCol.center.x + checkExtents.x, boxCol.center.y, boxCol.center.z + checkExtents.z)
            };

            foreach (Vector3 localCorner in localCorners)
            {
                Vector3 worldCorner = instance.transform.TransformPoint(localCorner);
                Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(worldCorner.x), Mathf.RoundToInt(worldCorner.z));

                // If any corner of the mesh extends into a tile that isn't part of the room, reject it
                if (!roomTiles.Contains(gridPos))
                {
                    Debug.LogWarning($"[Spawn Blocked] {prefab.name} clips into walls at grid {gridPos}.");
                    DestroyImmediate(instance);
                    return false;
                }
            }
        }

        // Move the object so its lowest visual point rests exactly on the floor (Y = 0) - prevents floating furniture
        float yOffset = CalculateVerticalOffset(instance);
        instance.transform.position = new Vector3(instance.transform.position.x, yOffset, instance.transform.position.z);

        return true;
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
        float meshBottomY = instance.GetComponentInChildren<Renderer>().bounds.min.y;
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

        MeshRenderer renderer = floorTransform.GetComponentInChildren<MeshRenderer>();
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
