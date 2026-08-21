using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class RoomDecoration : MonoBehaviour
{
    #region Decor Settings
    [Header("Decoration Settings")]
    [Range(0f, 1f)]
    public float clutterAmount = 0.1f;
    public HazardTagging hazardTagging;

    [Header("Floor Materials")]
    public Material tileMaterial;
    public Material woodMaterial;
    public Material carpetMaterial;
    public Material concreteMaterial;

    private float lastClutterAmount;

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
    public GameObject rug1Prefab;
    public GameObject rug2Prefab;
    public GameObject rug3Prefab;
    public GameObject longDresserPrefab;
    public GameObject tallDresserPrefab;

    [Header("Lighting & Small Decor")]
    public GameObject lampPrefab;
    public GameObject smallLampPrefab;
    public GameObject wallLampPrefab;
    public GameObject vasePrefab;

    [Header("Wall Art")]
    public GameObject painting1Prefab;
    public GameObject painting2Prefab;
    public GameObject painting3Prefab;

    [HideInInspector]
    public bool defaultsLoaded = false;

    private void Reset()
    {
        LoadDefaultPrefabs();
        defaultsLoaded = true;
    }

    [ContextMenu("Restore Default Resource Prefabs")]
    private void LoadDefaultPrefabs()
    {
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

        lampPrefab = Resources.Load<GameObject>("Interior Prefabs/Lamp");
        smallLampPrefab = Resources.Load<GameObject>("Interior Prefabs/Small Lamp");
        wallLampPrefab = Resources.Load<GameObject>("Interior Prefabs/Wall Lamp");
        vasePrefab = Resources.Load<GameObject>("Interior Prefabs/Vase");

        rug1Prefab = Resources.Load<GameObject>("Interior Prefabs/Rug1");
        rug2Prefab = Resources.Load<GameObject>("Interior Prefabs/Rug2");
        rug3Prefab = Resources.Load<GameObject>("Interior Prefabs/Rug3");

        longDresserPrefab = Resources.Load<GameObject>("Interior Prefabs/Long Dresser");
        tallDresserPrefab = Resources.Load<GameObject>("Interior Prefabs/Tall Dresser");

        painting1Prefab = Resources.Load<GameObject>("Interior Prefabs/Painting1");
        painting2Prefab = Resources.Load<GameObject>("Interior Prefabs/Painting2");
        painting3Prefab = Resources.Load<GameObject>("Interior Prefabs/Painting3");
    }
    #endregion

    public void DecorateRooms(List<RoomGeneration.RoomData> rooms, GameObject house)
    {
        if (rooms == null || rooms.Count == 0) return;

        if (clutterAmount <= 0f)
        {
            ClearAllDecoration(rooms);
            return;
        }

        ClearAllDecoration(rooms);

        bool isCarpetHouse = Random.value > 0.5f;

        foreach (var room in rooms)
        {
            if (room.RoomType == "Stairwell" || room.RoomType == "Closet")
                continue;

            ApplyFloorMaterial(room, room.RoomType, isCarpetHouse);
            DecorateSpecificRoom(room, room.RoomType);
            SpawnPaintingsForRoom(room);
        }

        // Once decor is spawned, trigger the hazard assignment
        if (hazardTagging != null)
            hazardTagging.ProcessRoomHazards(house);
    }

    private void ClearAllDecoration(List<RoomGeneration.RoomData> rooms)
    {
        foreach (var room in rooms)
        {
            if (room.RoomObject == null) continue;

            Transform[] allChildren = room.RoomObject.GetComponentsInChildren<Transform>();

            for (int i = allChildren.Length - 1; i >= 0; i--)
            {
                GameObject child = allChildren[i].gameObject;

                if (child.name.Contains("(Clone)"))
                {
                    if (child.name.Contains("Window") || child.name.Contains("Door"))
                        continue;

                    DestroyImmediate(child);
                }
            }
        }
    }

    private void DecorateSpecificRoom(RoomGeneration.RoomData room, string type)
    {
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

    #region Respawn Helpers

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int rnd = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[rnd];
            list[rnd] = temp;
        }
    }

    private GameObject TrySpawnOnAnyEdge(GameObject prefab, List<(Vector2Int tile, Vector3 forward)> edges, Transform parent, HashSet<Vector2Int> roomTiles, out (Vector2Int tile, Vector3 forward) successfulEdge)
    {
        successfulEdge = default;
        if (prefab == null) return null;

        for (int i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];

            // Ensure we have a solid wall behind us and not a hole leading outside or into another room
            if (!IsWallSolid(edge.tile, edge.forward)) continue;

            GameObject spawnedInstance = SpawnFurniture(prefab, edge.tile, edge.forward, parent, roomTiles);
            if (spawnedInstance != null)
            {
                successfulEdge = edge;

                // IMPORTANT: Remove the edge so the next item doesn't spawn perfectly inside this one
                edges.RemoveAt(i);

                return spawnedInstance;
            }
        }
        return null;
    }

    #endregion

    #region Room Layout Spawners

    private void SpawnBedroomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        ShuffleList(edges);

        if (TrySpawnOnAnyEdge(bedPrefab, edges, room.RoomObject.transform, room.Tiles, out var bedEdge) != null)
        {
            if (Random.value <= clutterAmount && nightstandPrefab != null)
            {
                Vector2Int rightDir = new Vector2Int(Mathf.RoundToInt(bedEdge.forward.z), Mathf.RoundToInt(-bedEdge.forward.x));
                Vector2Int nightstandTile = bedEdge.tile + rightDir;

                if (room.Tiles.Contains(nightstandTile))
                {
                    GameObject nightstand = SpawnFurniture(nightstandPrefab, nightstandTile, bedEdge.forward, room.RoomObject.transform, room.Tiles);

                    if (nightstand != null && smallLampPrefab != null && Random.value <= clutterAmount)
                    {
                        SpawnOnSurface(smallLampPrefab, nightstand);
                    }
                }
            }
        }

        GameObject dresserToSpawn = Random.value > 0.5f ? tallDresserPrefab : longDresserPrefab;
        if (dresserToSpawn != null)
        {
            TrySpawnOnAnyEdge(dresserToSpawn, edges, room.RoomObject.transform, room.Tiles, out _);
        }

        if (lampPrefab != null && Random.value <= clutterAmount)
        {
            TrySpawnOnAnyEdge(lampPrefab, edges, room.RoomObject.transform, room.Tiles, out _);
        }

        SpawnRugCenter(room, new[] { rug1Prefab, rug2Prefab });
    }

    private void SpawnBathroomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        ShuffleList(edges);

        TrySpawnOnAnyEdge(tubPrefab, edges, room.RoomObject.transform, room.Tiles, out _);

        if (TrySpawnOnAnyEdge(sinkAndToiletPrefab, edges, room.RoomObject.transform, room.Tiles, out var sinkEdge) != null)
        {
            if (rug3Prefab != null)
            {
                Vector2Int forwardDir = new Vector2Int(Mathf.RoundToInt(sinkEdge.forward.x), Mathf.RoundToInt(sinkEdge.forward.z));
                Vector2Int rugTile = sinkEdge.tile + forwardDir;

                if (room.Tiles.Contains(rugTile))
                {
                    SpawnFurniture(rug3Prefab, rugTile, sinkEdge.forward, room.RoomObject.transform, room.Tiles);
                }
            }
        }
    }

    private void SpawnLivingRoomFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        ShuffleList(edges);

        GameObject tvStandPrefab = longDresserPrefab != null ? longDresserPrefab : tablePrefab;
        GameObject spawnedStand = TrySpawnOnAnyEdge(tvStandPrefab, edges, room.RoomObject.transform, room.Tiles, out var standEdge);

        if (spawnedStand != null)
        {
            if (tvPrefab != null)
            {
                SpawnOnSurface(tvPrefab, spawnedStand);
            }

            if (couchPrefab != null)
            {
                Vector2Int forwardDir = new Vector2Int(Mathf.RoundToInt(standEdge.forward.x), Mathf.RoundToInt(standEdge.forward.z));
                Vector2Int sofaTile = standEdge.tile + (forwardDir * 2);

                if (room.Tiles.Contains(sofaTile))
                {
                    Vector3 sofaForward = -standEdge.forward;

                    if (SpawnFurniture(couchPrefab, sofaTile, sofaForward, room.RoomObject.transform, room.Tiles) != null)
                    {
                        if (Random.value <= clutterAmount && tablePrefab != null)
                        {
                            Vector2Int tableTile = standEdge.tile + forwardDir;
                            if (room.Tiles.Contains(tableTile))
                            {
                                SpawnFurniture(tablePrefab, tableTile, standEdge.forward, room.RoomObject.transform, room.Tiles);
                                // Purposely leaving the regular table clear of items
                            }
                        }
                    }
                }
            }
        }

        if (lampPrefab != null && Random.value <= clutterAmount)
        {
            TrySpawnOnAnyEdge(lampPrefab, edges, room.RoomObject.transform, room.Tiles, out _);
        }

        SpawnRugCenter(room, new[] { rug1Prefab, rug2Prefab });
    }

    private void SpawnKitchenFurniture(RoomGeneration.RoomData room)
    {
        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        ShuffleList(edges);

        TrySpawnOnAnyEdge(ovenPrefab, edges, room.RoomObject.transform, room.Tiles, out _);
        TrySpawnOnAnyEdge(fridgePrefab, edges, room.RoomObject.transform, room.Tiles, out _);

        if (counterPrefab != null)
        {
            int counterAttempts = Mathf.CeilToInt(clutterAmount * 4);
            for (int i = 0; i < counterAttempts; i++)
            {
                TrySpawnOnAnyEdge(counterPrefab, edges, room.RoomObject.transform, room.Tiles, out _);
            }
        }
    }

    private void SpawnDiningRoomFurniture(RoomGeneration.RoomData room)
    {
        if (diningTablePrefab == null) return;

        Vector2Int center = GetRoomCenter(room.Tiles);
        if (SpawnFurniture(diningTablePrefab, center, Vector3.forward, room.RoomObject.transform, room.Tiles) != null)
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
        ShuffleList(edges);

        if (clutterAmount > 0.4f && sideTablePrefab != null)
        {
            GameObject sideTable = TrySpawnOnAnyEdge(sideTablePrefab, edges, room.RoomObject.transform, room.Tiles, out _);

            if (sideTable != null && Random.value <= clutterAmount)
            {
                // Grab whichever of these are populated in the inspector and pick one randomly
                GameObject[] possibleDecor = new GameObject[] { smallLampPrefab, vasePrefab }.Where(x => x != null).ToArray();
                if (possibleDecor.Length > 0)
                {
                    GameObject chosenDecor = possibleDecor[Random.Range(0, possibleDecor.Length)];
                    SpawnOnSurface(chosenDecor, sideTable);
                }
            }
        }

        if (wallLampPrefab != null && clutterAmount > 0.1f)
        {
            int wallLampAttempts = Mathf.CeilToInt(clutterAmount * 4);
            for (int i = 0; i < wallLampAttempts; i++)
            {
                if (edges.Count > 0)
                {
                    int randIdx = Random.Range(0, edges.Count);
                    var edge = edges[randIdx];
                    if (SpawnWallItem(wallLampPrefab, edge.tile, edge.forward, room.RoomObject.transform))
                    {
                        edges.RemoveAt(randIdx);
                    }
                }
            }
        }
    }

    private void SpawnGenericFurniture(RoomGeneration.RoomData room)
    {
        Vector2Int centerTile = GetRoomCenter(room.Tiles);
        if (tablePrefab != null)
        {
            Vector3 centerPos = new Vector3(centerTile.x, 0, centerTile.y);
            GameObject table = Instantiate(tablePrefab, centerPos, Quaternion.identity, room.RoomObject.transform);
            AddFireProfileCollider(table);
            // Purposely leaving the regular table clear of items
        }

        if (Random.value <= clutterAmount)
        {
            List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
            ShuffleList(edges);

            if (shelfPrefab != null)
            {
                TrySpawnOnAnyEdge(shelfPrefab, edges, room.RoomObject.transform, room.Tiles, out _);
            }
        }
    }

    private void SpawnRugCenter(RoomGeneration.RoomData room, GameObject[] possibleRugs)
    {
        var validRugs = possibleRugs.Where(r => r != null).ToArray();
        if (validRugs.Length == 0) return;

        GameObject chosenRug = validRugs[Random.Range(0, validRugs.Length)];
        Vector2Int center = GetRoomCenter(room.Tiles);

        SpawnFurniture(chosenRug, center, Vector3.forward, room.RoomObject.transform, room.Tiles);
    }

    private void SpawnPaintingsForRoom(RoomGeneration.RoomData room)
    {
        /*GameObject[] paintings = { painting1Prefab, painting2Prefab, painting3Prefab };
        var validPaintings = paintings.Where(p => p != null).ToArray();
        if (validPaintings.Length == 0) return;

        List<(Vector2Int tile, Vector3 forward)> edges = GetRoomEdges(room.Tiles);
        ShuffleList(edges);

        // Spawn 1 to 2 paintings per room
        int paintingCount = Random.Range(1, 3);
        for (int i = 0; i < paintingCount; i++)
        {
            if (edges.Count == 0) break;

            int randIdx = Random.Range(0, edges.Count);
            var edge = edges[randIdx];
            GameObject chosenPainting = validPaintings[Random.Range(0, validPaintings.Length)];

            // SpawnWallItem already checks for overlaps with tall furniture
            if (SpawnWallItem(chosenPainting, edge.tile, edge.forward, room.RoomObject.transform))
            {
                // Prevent multiple paintings or wall fixtures sharing the exact same tile
                edges.RemoveAt(randIdx);
            }
        }*/
    }

    #endregion

    #region Core Physics and Spawn Helpers

    private bool IsWallSolid(Vector2Int tile, Vector3 forward)
    {
        Vector3 rayStart = new Vector3(tile.x, 1f, tile.y);

        if (Physics.Raycast(rayStart, -forward, out RaycastHit hit, 1.0f))
        {
            if (hit.collider.name.Contains("Door") || hit.collider.name.Contains("Window"))
                return false;

            if (((1 << hit.collider.gameObject.layer) & LayerMask.GetMask("Furniture")) != 0)
                return false;

            return true;
        }
        return false;
    }

    private void SpawnOnSurface(GameObject prefab, GameObject surfaceObj)
    {
        if (prefab == null || surfaceObj == null) return;

        Renderer[] surfaceRenderers = surfaceObj.GetComponentsInChildren<Renderer>();
        if (surfaceRenderers.Length == 0) return;

        float surfaceMaxY = float.MinValue;
        foreach (var r in surfaceRenderers)
        {
            if (r.bounds.max.y > surfaceMaxY)
            {
                surfaceMaxY = r.bounds.max.y;
            }
        }

        Vector3 pos = surfaceObj.transform.position;
        pos.y = surfaceMaxY;

        GameObject instance = Instantiate(prefab, pos, surfaceObj.transform.rotation, surfaceObj.transform.parent);
        AddFireProfileCollider(instance);

        Renderer[] instanceRenderers = instance.GetComponentsInChildren<Renderer>();
        if (instanceRenderers.Length > 0)
        {
            float instanceMinY = float.MaxValue;
            foreach (var r in instanceRenderers)
            {
                if (r.bounds.min.y < instanceMinY)
                {
                    instanceMinY = r.bounds.min.y;
                }
            }
            float offset = instance.transform.position.y - instanceMinY;
            instance.transform.position = new Vector3(pos.x, pos.y + offset, pos.z);
        }
    }

    private bool SpawnWallItem(GameObject prefab, Vector2Int tile, Vector3 forward, Transform parent, float height = 1.7f)
    {
        if (prefab == null) return false;

        if (!IsWallSolid(tile, forward)) return false;

        Vector3 pos = new Vector3(tile.x, height, tile.y);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        GameObject instance = Instantiate(prefab, pos, rotation, parent);
        AddFireProfileCollider(instance);

        BoxCollider boxCol = instance.GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            float localBackZ = boxCol.center.z - (boxCol.size.z / 2f);
            float padding = 0.02f;
            float requiredZOffset = -0.5f - localBackZ + padding;
            instance.transform.position += forward * requiredZOffset;

            Collider[] allColliders = instance.GetComponentsInChildren<Collider>();
            foreach (Collider col in allColliders) col.enabled = false;

            Vector3 worldCenter = instance.transform.TransformPoint(boxCol.center);
            Vector3 checkExtents = (boxCol.size / 2f) * 0.95f;

            bool isBlocked = false;
            Collider[] hits = Physics.OverlapBox(worldCenter, checkExtents, instance.transform.rotation);
            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(instance.transform)) continue;

                if (((1 << hit.gameObject.layer) & LayerMask.GetMask("Furniture")) != 0)
                {
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
        }

        return true;
    }

    private void AddFireProfileCollider(GameObject furniture)
    {
        Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        FireProfileController fireController = furniture.GetComponent<FireProfileController>();
        if (fireController == null)
            fireController = furniture.AddComponent<FireProfileController>();

        BoxCollider fireBox = fireController.collider;
        if (fireBox == null)
        {
            fireBox = furniture.GetComponent<BoxCollider>();
            fireController.collider = fireBox;
        }

        if (fireBox == null) return;

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

    private GameObject SpawnFurniture(GameObject prefab, Vector2Int tile, Vector3 forward, Transform parent, HashSet<Vector2Int> roomTiles)
    {
        Vector3 pos = new Vector3(tile.x, 0f, tile.y);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);

        GameObject instance = Instantiate(prefab, pos, rotation, parent);

        AddFireProfileCollider(instance);
        BoxCollider boxCol = instance.GetComponent<BoxCollider>();

        if (boxCol != null)
        {
            float localBackZ = boxCol.center.z - (boxCol.size.z / 2f);
            float padding = 0.02f;
            float requiredZOffset = -0.5f - localBackZ + padding;
            instance.transform.position += forward * requiredZOffset;

            int furnitureLayer = LayerMask.GetMask("Furniture");

            Collider[] allColliders = instance.GetComponentsInChildren<Collider>();
            foreach (Collider col in allColliders) col.enabled = false;

            Vector3 worldCenter = instance.transform.TransformPoint(boxCol.center);
            Vector3 checkExtents = (boxCol.size / 2f) * 0.95f;

            Collider[] hits = Physics.OverlapBox(worldCenter, checkExtents, instance.transform.rotation);
            bool isBlocked = false;

            foreach (Collider hit in hits)
            {
                if (hit.transform.IsChildOf(instance.transform)) continue;

                if (((1 << hit.gameObject.layer) & furnitureLayer) != 0 ||
                    hit.name.Contains("Door") || hit.name.Contains("Window"))
                {
                    isBlocked = true;
                    break;
                }
            }

            if (isBlocked)
            {
                DestroyImmediate(instance);
                return null;
            }

            foreach (Collider col in allColliders) col.enabled = true;

            Vector3 coreExtents = checkExtents * 0.5f;

            Vector3[] localCorners = {
            new Vector3(boxCol.center.x - coreExtents.x, boxCol.center.y, boxCol.center.z - coreExtents.z),
            new Vector3(boxCol.center.x + coreExtents.x, boxCol.center.y, boxCol.center.z - coreExtents.z),
            new Vector3(boxCol.center.x - coreExtents.x, boxCol.center.y, boxCol.center.z + coreExtents.z),
            new Vector3(boxCol.center.x + coreExtents.x, boxCol.center.y, boxCol.center.z + coreExtents.z)
            };

            foreach (Vector3 localCorner in localCorners)
            {
                Vector3 worldCorner = instance.transform.TransformPoint(localCorner);
                Vector2Int gridPos = new Vector2Int(Mathf.RoundToInt(worldCorner.x), Mathf.RoundToInt(worldCorner.z));

                if (!roomTiles.Contains(gridPos))
                {
                    DestroyImmediate(instance);
                    return null;
                }
            }
        }

        float yOffset = CalculateVerticalOffset(instance);
        instance.transform.position = new Vector3(instance.transform.position.x, yOffset, instance.transform.position.z);

        return instance;
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
        float meshBottomY = instance.GetComponentInChildren<Renderer>().bounds.min.y;
        float pivotY = instance.transform.position.y;
        return pivotY - meshBottomY;
    }

    private void ApplyFloorMaterial(RoomGeneration.RoomData room, string assignedType, bool isCarpetHouse)
    {
        Transform floorTransform = room.RoomObject.transform.Find("Floors");
        if (floorTransform == null) return;

        MeshRenderer renderer = floorTransform.GetComponentInChildren<MeshRenderer>();
        if (renderer == null) return;

        switch (assignedType)
        {
            case "Bathroom":
            case "Kitchen":
                renderer.material = Random.value > 0.5f ? tileMaterial : woodMaterial;
                break;
            default:
                renderer.material = isCarpetHouse ? carpetMaterial : woodMaterial;
                break;
        }
    }

    private void OnValidate()
    {
        if (!Application.isPlaying && clutterAmount != lastClutterAmount)
        {
            lastClutterAmount = clutterAmount;

            RoomGeneration gen = GetComponent<RoomGeneration>();
            if (gen != null && gen.allGeneratedRooms != null)
            {
                DecorateRooms(gen.allGeneratedRooms, gen.houseParent);
            }
        }
    }

    #endregion
}