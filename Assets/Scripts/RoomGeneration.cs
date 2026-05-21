using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomGeneration : MonoBehaviour
{
    #region Variables and Classes
    [Header("Rapid Generation Vars")]

    [Header("Preset Generation Settings")]
    public bool dormitory = false;
    public bool warehouse = false;
    public bool smallHouse = false;
    public bool twoStoryHouse = false;
    public bool skyscraper = false;
    public bool mansion = false;

    [Tooltip("You must re-click the preset generation setting above to stop generating code-breaking structures after clicking these settings.")]
    [Header("Code Violations")]
    public bool breakMinimumRoomWidths = false; // rooms cannot be under 7 feet (2.1336f) in any plan dimension
    public bool breakMinimumKitchenWalkway = false; // kitchens must have 3 feet (0.9144f) of walking space between appliances/counters/walls and whatever is opposite them
    public bool breakMinimumCeilingHeight = false; // ceilings of habitable spaces cannot be lower than 7.5 feet (2.286f), 7 feet for kitchens, bathrooms, etc.
    public bool breakMinimumUnitSize = false; // dwellings must have minimum of 190 square feet (17.65f) of habitable space
    public bool breakMinimumMainSpaceSize = false; // dwellings must have one room of 120 square feet (3.048 x 3.6576) minimum
    public bool breakMinimumBedroomSpaceSize = false; // bedrooms must be 70 square feet, about (2.4384 x 2.7432) or (2.1336 x 3.048) minimum
    public bool breakHallwayWidth = false; // hallways must be 3 feet (0.9144f) wide 
    public bool breakEgressDoorDimensions = false; // egress door must be 32 inches wide (0.81f) and 78 inches tall (1.98f)

    // These vars specifically pass to RoomDecoration
    [HideInInspector] public bool shrinkKitchenPassageway = false; // set to value of breakMinimumKitchenWalkway

    [Header("Customized Generation Vars")]

    [Header("House Dimensions")]
    public int maxHouseWidth = 20;
    public int maxHouseLength = 20;

    [Header("Room Dimensions")]
    public int minRoomWidth = 4;
    public int maxRoomWidth = 10;
    public int minRoomLength = 4;
    public int maxRoomLength = 10;
    public int wallHeight = 3;

    [Header("Layout Complexity")]
    public int numberOfRooms = 5;
    public int numberOfFloors = 2;
    public bool identicalFloors = false;
    public bool roomAmountsDifferPerFloor = false;
    public bool heightenedNooks = false;
    public bool generateHallways = true;
    [Range(0f, 1f)]
    public float hallwayChance = 0.6f;
    public int hallwayWidth = 2;

    [Header("Shape Complexity")]
    [Tooltip("How many rectangles to combine to make a single room shape. If void spaces are not allowed, then these complex shapes are required to follow minimum viable dimensions.")]
    public int minComplexity = 1;
    public int maxComplexity = 3;
    public bool allowVoidSpaces = false;
    public int minViableWidth = 3; // was 2, adjusted to 3 since 2.1336 is the minimum room dimension for habitable spaces
    public int minViableLength = 3;

    [Header("Doors and Windows")]
    [Range(0f, 1f)]
    public float windowChance = 0.3f;
    public float doorHeight = 2.9f; // was 2.0f
    public float windowSillHeight = 0.8f;
    public float windowTopHeight = 2.0f; 

    [Header("Yard")]
    public bool generateYard = true; // Generally, always generate a yard (except for when we are making non-explorable houses)
    public int minYardPadding = 2;
    public int maxYardPadding = 8;

    [Header("Outbuilding")]
    public bool chanceForOutbuilding = false;
    [Range(0f, 1f)]
    public float outbuildingSpawnChance = 0.5f;
    public int outbuildingOffset = 5;

    [Header("All Materials")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material ceilingMaterial;
    public Material yardMaterial;

    [Header("XR Locomotion")]
    public bool addCollidersToCombinedGeometry = true;
    public bool enableTeleportationOnFloors = true;

    [Header("Editor View Settings")]
    public bool destroyPreviousGeneration = true;
    public bool roomRoofsTransparent = false;

    // Tracks EVERY tile in the entire house to prevent overlaps
    private HashSet<Vector2Int> allHouseOccupiedTiles = new HashSet<Vector2Int>();
    private List<PlacedRoom> placedRooms = new List<PlacedRoom>();

    // Window variables to track
    Dictionary<string, int> lastWindowIndex = new Dictionary<string, int>();
    HashSet<HashSet<Vector2Int>> roomsWithWindows = new HashSet<HashSet<Vector2Int>>();
    private HashSet<string> floorZeroWindows = new HashSet<string>();
    private HashSet<string> frontWallFloor0Windows = new HashSet<string>();
    private string frontWallDirection = "";

    // Stairwell variables to track
    private int stairDepth = 5; // Makes for an angle of 31 degrees, architectural height for a comfortable set of stairs
    private bool stairwellIsOpenBottom = false;
    private int stairwellOpenBottomY = -999;
    private HashSet<Vector2Int> activeStairwellFootprint = null;

    // Main door variables
    private Vector2Int mainDoorTile;
    private string mainDoorDirection; // "N", "S", "E", or "W"

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

    // Class to track rooms that will be passed to the decorator
    public class RoomData
    {
        public HashSet<Vector2Int> Tiles;
        public GameObject RoomObject;
        public string RoomType;
    }

    [HideInInspector]
    public List<RoomData> allGeneratedRooms = new List<RoomData>();

    #endregion

    #region Presets and Restorations
    private void CheckPresetLayouts()
    {
        if (dormitory)
        {
            numberOfRooms = 10;
            numberOfFloors = 4;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            heightenedNooks = false;
            maxHouseWidth = 20;
            maxHouseLength = 30;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 4;
            minComplexity = 1;
            maxComplexity = 3;
            generateHallways = true;
            hallwayChance = 95; // nearly all dorm-style buildings should have hallways
            hallwayWidth = 2;

            dormitory = false; // Turn off so we can manually tweak values afterward
        }

        if (warehouse)
        {
            numberOfRooms = 1;
            numberOfFloors = 1;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            heightenedNooks = false;
            maxHouseWidth = 20;
            maxHouseLength = 40;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 6;
            minComplexity = 1;
            maxComplexity = 1;
            generateHallways = false;

            warehouse = false;
        }

        if (smallHouse) // Essentially like a cottage - very slim chance of having a hallway to connect the rooms
        {
            numberOfRooms = 5;
            numberOfFloors = 1;
            identicalFloors = false;
            roomAmountsDifferPerFloor = false;
            heightenedNooks = false;
            maxHouseWidth = 20;
            maxHouseLength = 20;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 4;
            minComplexity = 1;
            maxComplexity = 4;
            generateHallways = true;
            hallwayChance = 5;
            hallwayWidth = 2; 

            smallHouse = false;
        }

        if (twoStoryHouse)
        {
            numberOfRooms = 4;
            numberOfFloors = 2;
            identicalFloors = false;
            roomAmountsDifferPerFloor = true;
            heightenedNooks = false;
            maxHouseWidth = 20;
            maxHouseLength = 20;
            minRoomWidth = 4;
            maxRoomWidth = 10;
            minRoomLength = 4;
            maxRoomLength = 10;
            wallHeight = 4;
            minComplexity = 1;
            maxComplexity = 4;
            generateHallways = true;
            hallwayChance = 60; // Average chance for the floor to have a hallway or not
            hallwayWidth = 2;

            twoStoryHouse = false;
        }

        if (skyscraper)
        {
            numberOfRooms = 10;
            numberOfFloors = 30;
            identicalFloors = true;
            roomAmountsDifferPerFloor = false;
            heightenedNooks = false;
            maxHouseWidth = 20;
            maxHouseLength = 20;
            minRoomWidth = 6;
            maxRoomWidth = 6;
            minRoomLength = 6;
            maxRoomLength = 6;
            wallHeight = 4;
            minComplexity = 1;
            maxComplexity = 2;
            generateHallways = true;
            hallwayChance = 95; // Most skyscrapers should have hallways as well
            hallwayWidth = 3;

            skyscraper = false;
        }

        if (mansion)
        {
            numberOfRooms = 10;
            numberOfFloors = 4;
            identicalFloors = false;
            roomAmountsDifferPerFloor = true;
            heightenedNooks = true;
            maxHouseWidth = 30;
            maxHouseLength = 40;
            minRoomWidth = 10;
            maxRoomWidth = 20;
            minRoomLength = 10;
            maxRoomLength = 20;
            wallHeight = 6;
            minComplexity = 6;
            maxComplexity = 10;
            generateHallways = true;
            hallwayChance = 80; // Higher chance for hallways due to the large amount of rooms, but some layouts may be more maze-like
            hallwayWidth = 3;

            mansion = false;
        }
    }

    private void CheckIntentionalCodeViolations()
    {
        if (breakMinimumRoomWidths)
        {
            minViableWidth = 1; // 2.1336 is the minimum standard
            minViableLength = 1;

            breakMinimumRoomWidths = false;
        }
        else
        {
            // Need to set the minViable here since they are universal / not customized to other building presets after each reset
            minViableWidth = 3; // 2.1336 is the minimum standard
            minViableLength = 3;
        }

        if (breakEgressDoorDimensions)
        {
            doorHeight = 1.5f; // egress door must be 32 inches wide (0.81f) and 78 inches tall (1.98f)

            breakEgressDoorDimensions = false;
        }
        else
        {
            doorHeight = 2.9f;
        }

        if (breakMinimumCeilingHeight)
        {
            wallHeight = 2; // 2.286 is the minimum

            breakMinimumCeilingHeight = false;
        }

        if (breakMinimumUnitSize)
        {
            maxHouseLength = 15; // 17.65 is the minimum square footage
            maxHouseWidth = 15;

            breakMinimumUnitSize = false;
        }

        if (breakMinimumMainSpaceSize)
        {
            minRoomWidth = 3; // (3.048 x 3.6576) minimum
            minRoomLength = 3;

            breakMinimumMainSpaceSize = false;
        }

        if (breakMinimumBedroomSpaceSize)
        {
            minRoomWidth = 2; // about (2.4384 x 2.7432) or (2.1336 x 3.048) minimum
            minRoomLength = 2;

            breakMinimumMainSpaceSize = false;
        }

        if (breakHallwayWidth)
        {
            hallwayWidth = 1; // about 0.9144f minimum, this is the closest we'll get in int, which already feels quite tight

            breakHallwayWidth = false;
        }
    }

    // Copied in the values that the spawning logic is based around so we can preserve positioning if script object was moved
    public void RestoreGeneratorTransform()
    {
        this.gameObject.transform.position = new Vector3(-0.05f, 2.071f, 2.0136f);
        this.gameObject.transform.rotation = new Quaternion(0f, 0f, 0f, 0f);
        this.gameObject.transform.localScale = new Vector3(1.4419f, 1.4419f, 1.4419f);
    }

    private void OnValidate()
    {
        // Check for any preset values we want to follow
        CheckPresetLayouts();
        CheckIntentionalCodeViolations();

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
    #endregion

    #region Main Method
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
        // Clear the internal list of rooms used by the decorator
        allGeneratedRooms.Clear();
        // Clear the tracked list of window positions
        floorZeroWindows.Clear();
        frontWallFloor0Windows.Clear();
        frontWallDirection = "";
        // Reset the stairwell logic
        activeStairwellFootprint = null;
        bool localOpenBottomDecision = (Random.value > 0.5f);
        stairwellIsOpenBottom = localOpenBottomDecision;
        //stairwellIsOpenBottom = false;
        stairwellOpenBottomY = -999;

        // Ensure the RoomGeneration object is set to the original transform values (in case it was accidentally moved)
        RestoreGeneratorTransform();

        // Create a master House parent to hold the layout in
        GameObject houseParent = new GameObject("House");
        houseParent.transform.SetParent(this.transform);
        houseParent.transform.localPosition = Vector3.zero;

        // Generate the overall layout of the house/house borders
        HashSet<Vector2Int> houseLayout = GenerateHouseLayout();

        // Determine the load bearing walls that need to be replicated based on the layout
        HashSet<string> loadBearingWalls = (numberOfFloors > 1)
            ? DetectLoadBearingWalls(houseLayout)
            : new HashSet<string>();

        allHouseOccupiedTiles = new HashSet<Vector2Int>(houseLayout); // Save the layout so the wall-spawning logic knows where the outside of the house is

        // Lock in the stairwell BEFORE subdividing floors
        Vector2Int globalStairTile = new Vector2Int(-999, -999);
        HashSet<Vector2Int> globalStairwellFootprint = null;

        if (numberOfFloors > 1)
        {
            DetermineGlobalStairwell(houseLayout, out globalStairwellFootprint, out globalStairTile);
            activeStairwellFootprint = globalStairwellFootprint;
        }

        // Create a new floorplan and allow us to try generating a hallway on floor one
        List<HashSet<Vector2Int>> rooms = new List<HashSet<Vector2Int>>();

        // Subdivide the house layout into the desired num of rooms
        if (generateHallways && Random.value <= hallwayChance && GenerateHallway(houseLayout, out HashSet<Vector2Int> hallway, out HashSet<Vector2Int> chunkA, out HashSet<Vector2Int> chunkB))
        {
            rooms.Add(hallway);

            // Subdivide the remaining halves independently, splitting the target room count
            int halfRooms = Mathf.Max(1, numberOfRooms / 2);

            rooms.AddRange(SubdivideHouse(chunkA, halfRooms));
            rooms.AddRange(SubdivideHouse(chunkB, halfRooms));
        }
        else
        {
            rooms = SubdivideHouse(houseLayout, numberOfRooms); // Else, just subdivide the house in the standard fashion
        }

        // Start the base roof height at 0 (global 0)
        float roofHeight = 0;

        for (int floor = 0; floor < numberOfFloors; floor++)
        {
            stairwellIsOpenBottom = localOpenBottomDecision;

            // Create the iterative Floor parent to hold each generated floor in
            GameObject floorParent = new GameObject($"Floor_{floor}");
            floorParent.transform.SetParent(houseParent.transform);
            floorParent.transform.localPosition = Vector3.zero;

            // Determine how many rooms for THIS floor
            int roomsOnCurrentFloor = (floor > 0 && roomAmountsDifferPerFloor && !identicalFloors) // If this is NOT the first floor and room numbers on each floor should differ
                ? Random.Range(numberOfRooms, numberOfRooms + 3) // Some houses might have just one room (e.g., a warehouse) so minimum must always be numberOfRooms for now
                : numberOfRooms; // Else, we stick to the universal/base num of rooms

            List<HashSet<Vector2Int>> floorRooms = new List<HashSet<Vector2Int>>();

            // Need to subdivide houseLayout differently to get different room arrangements - otherwise, we'll have identical floors
            if (!identicalFloors)
            {
                // Attempt to carve out a hallway on this floor if the chance threshold was reached
                if(generateHallways && Random.value <= hallwayChance && GenerateHallway(houseLayout, out HashSet<Vector2Int> currentHallway, out HashSet<Vector2Int> currentChunkA, out HashSet<Vector2Int> currentChunkB))
                {
                    // Add the hallway to our final room list
                    floorRooms.Add(currentHallway);

                    // Subdivide the remaining halves independently, splitting the target room count
                    int halfRooms = Mathf.Max(1, roomsOnCurrentFloor / 2);

                    floorRooms.AddRange(SubdivideHouse(currentChunkA, halfRooms));
                    floorRooms.AddRange(SubdivideHouse(currentChunkB, halfRooms));

                    // Enforce load-bearing walls
                    if (floor > 0 && loadBearingWalls.Count > 0)
                        EnforceLoadBearingWalls(floorRooms, loadBearingWalls);
                }
                else
                {
                    // Standard generation if the hallway fails or is toggled off
                    floorRooms = SubdivideHouse(houseLayout, roomsOnCurrentFloor);
                }
            }
            else
            {
                floorRooms = rooms; // If identicalFloors is true, we skip new subdivision so the layout remains the same on all floors
            }

            // If we had created slim long hallways, chop them up into nooks
            if (heightenedNooks)
                SubdivideNooks(floorRooms);

            // Validation step for minimumz main space size
            if (!breakMinimumMainSpaceSize)
            {
                bool hasMainSpace = false;
                foreach (var room in floorRooms)
                {
                    if (room.Count >= 12) // 12 tiles = 12 m² = ~129 sq ft
                    {
                        hasMainSpace = true;
                        break;
                    }
                }

                if (!hasMainSpace)
                {
                    Debug.LogWarning("Generation failed Main Space code. Restarting generation.");
                    GenerateAllRooms(); // Recursive restart
                    return;
                }
            }

            // Carve the pre-determined global stairwell from this floor
            HashSet<Vector2Int> stairwellRoom = null;
            if (globalStairwellFootprint != null)
            {
                foreach (var room in floorRooms)
                {
                    room.ExceptWith(globalStairwellFootprint);
                }
                floorRooms.RemoveAll(r => r.Count == 0);

                stairwellRoom = new HashSet<Vector2Int>(globalStairwellFootprint);
                floorRooms.Add(stairwellRoom);
            }

            // Generate doorways for this specific floor layout (now that we have the full layout)
            HashSet<string> floorDoors = GenerateDoorsForFloor(floorRooms);

            // Force the stairwell doors to align with the ramps
            if (stairwellRoom != null)
            {
                ForceStairwellDoors(floor, globalStairTile, globalStairwellFootprint, floorDoors);
            }

            // Spawn the ramp using the globally locked tile
            if (floor < numberOfFloors - 1)
                SpawnStairs(globalStairTile, roofHeight, floorParent.transform, stairDepth);

            // Determine the main door tile in the layout
            DetermineMainDoor(floorRooms);

            // Plan the locations of front wall windows for the wall determined to hold the main door
            HashSet<string> frontWindows = PrecalculateFrontWallWindows(floorRooms, floor);

            // Validation step for egress door access condition
            if (!ValidateEgressPath(floorRooms, floorDoors, mainDoorTile, floor))
            {
                Debug.LogWarning("Generation failed to create unobstructed egress door. Restarting generation.");
                GenerateAllRooms(); // Recursive restart
                return;
            }

            // Determine room types before building them out
            Dictionary<HashSet<Vector2Int>, string> roomTypes = AssignRoomTypesBySize(floorRooms, floor, stairwellRoom);

            // Build at the current roofHeight
            for (int i = 0; i < floorRooms.Count; i++)
            {
                stairwellIsOpenBottom = localOpenBottomDecision;

                // Fetch the assigned type
                string assignedType = roomTypes[floorRooms[i]];

                // Merge with the pre-calculated front wall windows
                HashSet<string> roomWindows = PrecalculateWindows(floorRooms[i], assignedType, floor);
                roomWindows.UnionWith(frontWindows);

                GameObject builtRoom = BuildRoomGeometry(i, floor, floorRooms[i], Vector2Int.zero, roofHeight, floorParent.transform, globalStairTile, floorDoors, assignedType, stairwellRoom);

                // Rename object for easy hierarchy reading
                builtRoom.name = $"Room_{i} [{assignedType}]";

                // Add the room to our list of generated rooms so we can pass it to the decorator later
                allGeneratedRooms.Add(new RoomData { Tiles = floorRooms[i], RoomObject = builtRoom, RoomType = assignedType });
            }

            // Get the highest point in all room roofs and build off that for the next floor
            roofHeight = GetHighestRoofPoint();
            // Debug.Log($"Floor {floor} complete. Next floor will be at: {roofHeight}");
        }

        // Generate outbuilding if toggled and the random chance succeeds
        if (chanceForOutbuilding && Random.value <= outbuildingSpawnChance)
            GenerateOutbuilding(houseParent.transform);

        // Generate the yard to encompass all the present buildings
        GenerateYard(houseParent.transform);

        // Check transparency toggle after rooms are made and automatically toggle transparency if necessary
        lastTransparencyState = roomRoofsTransparent;
        ToggleRoofTransparency();

        // Decorate the house after it has been fully generated
        RoomDecoration decorator = GetComponent<RoomDecoration>();
        if (decorator != null)
            decorator.DecorateRooms(allGeneratedRooms);
    }
    #endregion

    #region Layout Generation Callers
    void GenerateOutbuilding(Transform parent)
    {
        // Set up the parent container
        GameObject outbuildingParent = new GameObject("Outbuilding");
        outbuildingParent.transform.SetParent(parent);
        outbuildingParent.transform.localPosition = Vector3.zero;

        // Temporarily save the main house's wall height, then set to Warehouse height
        int originalWallHeight = wallHeight;
        wallHeight = 6; // Your preset warehouse height

        // Define warehouse room dimensions (1 big room)
        int width = Random.Range(minRoomWidth, maxRoomWidth + 1);
        int length = Random.Range(minRoomLength, maxRoomLength + 1);

        // Calculate a safe offset to the right of the main house - we push it out past the max width of the main house plus the chosen offset
        int startX = maxHouseWidth + outbuildingOffset;
        int startY = 0;

        HashSet<Vector2Int> outbuildingTiles = new HashSet<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < length; y++)
            {
                // We bake the offset directly into the coordinates so the wall logic works
                Vector2Int tile = new Vector2Int(startX + x, startY + y);
                outbuildingTiles.Add(tile);

                // Add to the global tracker so the script knows to build exterior walls here
                allHouseOccupiedTiles.Add(tile);
            }
        }

        // Build the geometry - we pass an empty HashSet for doors, and a dummy Vector2Int(-999, -999) so it doesn't accidentally spawn stairs
        HashSet<string> noDoors = new HashSet<string>();
        BuildRoomGeometry(999, 0, outbuildingTiles, Vector2Int.zero, 0f, outbuildingParent.transform, new Vector2Int(-999, -999), noDoors, "Warehouse");

        // Restore the original wall height so the next time you hit generate, it's correct
        wallHeight = originalWallHeight;
    }

    void GenerateYard(Transform parent)
    {
        if (allHouseOccupiedTiles.Count == 0 || !generateYard) return;

        // Find the extreme bounds of ALL buildings (Main House + Outbuilding)
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        foreach (Vector2Int tile in allHouseOccupiedTiles)
        {
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }

        // Force a guaranteed minimum of 3 tiles of walking space
        int minWalkableSpace = 3;

        // Overall, padding is still variable so it looks like a random property lot (not a generic square yard)
        int paddingLeft = Random.Range(minWalkableSpace, maxYardPadding + 1);
        int paddingRight = Random.Range(minWalkableSpace, maxYardPadding + 1);
        int paddingTop = Random.Range(minWalkableSpace, maxYardPadding + 1);
        int paddingBottom = Random.Range(minWalkableSpace, maxYardPadding + 1);

        minX -= paddingLeft;
        maxX += paddingRight;
        minY -= paddingBottom;
        maxY += paddingTop;

        float yardWidth = (maxX - minX) + 1;
        float yardLength = (maxY - minY) + 1;

        // Calculate the center point of the new yard - use the parent's position as the base to ensure it aligns with the house
        Vector3 origin = parent.position;
        float centerX = origin.x + minX + (yardWidth / 2f) - 0.5f;
        float centerZ = origin.z + minY + (yardLength / 2f) - 0.5f;

        // Spawn the yard as a flattened cube (easier to scale precisely than a Plane)
        GameObject yard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        yard.name = "Yard";
        yard.transform.SetParent(parent);

        // FORCE the World Position to be slightly below the house origin
        MeshRenderer houseRenderer = parent.GetComponentInChildren<MeshRenderer>();
        float bottomY;

        // Get exact bottom point of the house generated
        if (houseRenderer != null)
            bottomY = houseRenderer.bounds.min.y;
        else
            bottomY = origin.y - (wallHeight / 2f);

        float yardThickness = 0.1f;
        yard.transform.position = new Vector3(centerX, bottomY - (yardThickness / 2f), centerZ);
        yard.transform.localScale = new Vector3(yardWidth, 0.1f, yardLength);

        // Apply the Yard Material
        if (yardMaterial != null)
            yard.GetComponent<MeshRenderer>().sharedMaterial = yardMaterial;
        else
            yard.GetComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // Add an instance of teleportation area + set it to the collider of the yard
        GameObject teleportPrefab = Resources.Load<GameObject>("Locomotion/Teleport Area Invisible");
        if (teleportPrefab != null)
        {
            GameObject teleportInstance = Instantiate(teleportPrefab, yard.transform);
            teleportInstance.name = "Teleport Area Invisible";

            BoxCollider yardCollider = yard.GetComponent<BoxCollider>();

            if (yardCollider != null)
            {
                TeleportationArea teleportScript = teleportInstance.GetComponent<TeleportationArea>();
                teleportScript.colliders.Clear();
                teleportScript.colliders.Add(yardCollider);
            }
        }
        else
        {
            Debug.LogWarning("Prefab not found at Resources/Locomotion/Teleport Area Invisible.prefab");
        }
    }

    bool GenerateHallway(HashSet<Vector2Int> footprint, out HashSet<Vector2Int> hallway, out HashSet<Vector2Int> chunkA, out HashSet<Vector2Int> chunkB)
    {
        //Debug.Log("[COMMON EVENT]: Attempting to generate hallway");

        hallway = new HashSet<Vector2Int>();
        // The chunks are the two separate sides of the house that the hallway connects
        chunkA = new HashSet<Vector2Int>();
        chunkB = new HashSet<Vector2Int>();

        // Find the bounding box of the footprint
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var tile in footprint)
        {
            if (tile.x < minX) minX = tile.x;
            if (tile.x > maxX) maxX = tile.x;
            if (tile.y < minY) minY = tile.y;
            if (tile.y > maxY) maxY = tile.y;
        }

        int width = maxX - minX + 1;
        int length = maxY - minY + 1;

        // If the house is too small, abort the hallway carve - needs to be at least four additional tiles on either side + the minimum width of our hallway
        // I.e., there need to be at least 4 tiles worth of rooms next to the hallway, and 4 tiles worth of space for the hallway to stretch down + our width
        if (width < hallwayWidth + 4 || length < hallwayWidth + 4)
        {
            //Debug.Log("Aborted hallway attempt");
            return false; // was &&
        }

        // Slice along the longest axis
        bool carveVertical = width > length;

        if (carveVertical)
        {
            int splitXStart = minX + (width / 2) - (hallwayWidth / 2); // Find the middle X in our hallway zone (space needed for a hallway of our width)
            int splitXEnd = splitXStart + hallwayWidth - 1;

            foreach (var tile in footprint)
            {
                if (tile.x >= splitXStart && tile.x <= splitXEnd) hallway.Add(tile); // Middle line is the hallway
                else if (tile.x < splitXStart) chunkA.Add(tile); // Left side
                else chunkB.Add(tile); // Right side (you are king)
            }
        }
        else
        {
            int splitYStart = minY + (length / 2) - (hallwayWidth / 2); // Find the middle Y
            int splitYEnd = splitYStart + hallwayWidth - 1; 

            foreach (var tile in footprint)
            {
                if (tile.y >= splitYStart && tile.y <= splitYEnd) hallway.Add(tile); // Middle line is the hallway
                else if (tile.y < splitYStart) chunkA.Add(tile); // Bottom side
                else chunkB.Add(tile); // Top side
            }
        }

        // Validate that the hallway didn't leave behind unviable slivers
        if (!IsRoomViable(hallway) || !IsRoomViable(chunkA) || !IsRoomViable(chunkB))
        {
            //Debug.Log("Hallway cut rejected: Resulting chunks were too narrow or irregular.");

            // Clear the sets to ensure no partial data is left behind
            hallway.Clear();
            chunkA.Clear();
            chunkB.Clear();

            return false;
        }

        return true;
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
            // If they are already connected and the mansion bool is on, have a large chance (60%) to create realistic, maze-like loops
            else if (Random.value < 0.6f && heightenedNooks)
            {
                //Debug.Log("[MANSION EVENT]: Added a natural loop!");
                doors.Add(possibleEdges[Random.Range(0, possibleEdges.Count)]);
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

        // Validation step for minimum unit size
        int areaSqMeters = coords.Count;
        float areaSqFeet = areaSqMeters * 10.764f; // one square meter is 10.764 square feet

        if (!breakMinimumUnitSize && areaSqFeet < 190f)
        {
            // Footprint is too small for code, run it again
            return GenerateHouseLayout();
        }

        return coords;
    }
    #endregion

    #region Room Division
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

    void SubdivideNooks(List<HashSet<Vector2Int>> rooms)
    {
        List<HashSet<Vector2Int>> newCubbies = new List<HashSet<Vector2Int>>();
        List<HashSet<Vector2Int>> roomsToRemove = new List<HashSet<Vector2Int>>();

        foreach (var room in rooms)
        {
            // Find the bounding box of this specific room
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var tile in room)
            {
                if (tile.x < minX) minX = tile.x;
                if (tile.x > maxX) maxX = tile.x;
                if (tile.y < minY) minY = tile.y;
                if (tile.y > maxY) maxY = tile.y;
            }

            int width = maxX - minX + 1;
            int length = maxY - minY + 1;

            // Define what a "long thin room" is
            bool isThinX = width <= 2 && length >= 4;
            bool isThinY = length <= 2 && width >= 4;

            // If it's awkwardly long, give it a high chance to be chopped into cubbies
            if (isThinX || isThinY)
            {
                if (Random.value < 0.7f) // 70% chance to chop it up
                {
                    //Debug.Log("[MANSION EVENT: Chopped up a nook!");
                    HashSet<Vector2Int> cubbyA = new HashSet<Vector2Int>();
                    HashSet<Vector2Int> cubbyB = new HashSet<Vector2Int>();

                    if (isThinX) // Chop horizontally across the long Y axis
                    {
                        int split = Random.Range(minY + 1, maxY);
                        foreach (var tile in room) { if (tile.y < split) cubbyA.Add(tile); else cubbyB.Add(tile); }
                    }
                    else // Chop vertically across the long X axis
                    {
                        int split = Random.Range(minX + 1, maxX);
                        foreach (var tile in room) { if (tile.x < split) cubbyA.Add(tile); else cubbyB.Add(tile); }
                    }

                    if (cubbyA.Count > 0 && cubbyB.Count > 0)
                    {
                        // If we are preventing void spaces, validate the cubbies before adding them
                        if (!IsRoomViable(cubbyA) || !IsRoomViable(cubbyB))
                            continue;

                        roomsToRemove.Add(room);
                        newCubbies.Add(cubbyA);
                        newCubbies.Add(cubbyB);
                    }
                }
            }
        }

        // Apply the cuts to the floor plan
        foreach (var r in roomsToRemove) rooms.Remove(r);
        rooms.AddRange(newCubbies);
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

        // Determine the minimum size for THIS specific slice - default to your standard minimums
        int currentMinWidth = minRoomWidth;
        int currentMinLength = minRoomLength;

        // If heightenedNooks is checked, give a chance to ignore the standard minimums and create a tiny space
        if (heightenedNooks && Random.value < 0.6f) // 60% chance for a nook when splitting
        {
            // Force the slice to be extremely narrow (1 or 2 tiles wide)
            //Debug.Log("[MANSION EVENT]: Added a narrow slice/nook!");
            currentMinWidth = Random.Range(1, 3);
            currentMinLength = Random.Range(1, 3);
        }

        // Constrain aspect ratio (balancing length and width of cut rooms to be more realistic)
        float maxAspectRatio = 2.0f;

        // Decide split direction - cut on the longest axis to make a square shape more likely
        bool splitVertical;
        if (width > length)
            splitVertical = true;
        else if (length > width)
            splitVertical = false;
        else
            splitVertical = Random.value > 0.5f; // make it a 50-50 random split if the axes are the exact same length

        // Try the preferred split direction first and fall back to the other
        for (int attempt = 0; attempt < 2; attempt++)
        {
            roomA = new HashSet<Vector2Int>();
            roomB = new HashSet<Vector2Int>();

            if (splitVertical)
            {
                // Calculate valid range for the slice to ensure minRoomWidth is respected on both sides
                int minSplit = minX + currentMinWidth;
                int maxSplit = maxX - currentMinWidth + 1;
                if (minSplit > maxSplit) { splitVertical = !splitVertical; continue; }

                // Bias the split toward the center: pick from the middle third of valid range
                int rangeSize = maxSplit - minSplit;
                int centerBias = rangeSize / 3;
                int biasedMin = minSplit + centerBias;
                int biasedMax = maxSplit - centerBias;
                if (biasedMin > biasedMax) { biasedMin = minSplit; biasedMax = maxSplit; }

                // Pick a random line to draw the knife through
                int splitLine = Random.Range(biasedMin, biasedMax + 1);

                // Sort tiles into Room A or Room B based on the line
                foreach (var tile in currentRoom)
                {
                    if (tile.x < splitLine) roomA.Add(tile);
                    else roomB.Add(tile);
                }

                // Reject if either resulting room is too sliver-like
                int aWidth = splitLine - minX;
                int bWidth = maxX - splitLine + 1;
                if ((float)length / aWidth > maxAspectRatio || (float)length / bWidth > maxAspectRatio)
                { splitVertical = !splitVertical; continue; }
            }
            else
            {
                // Same logic, but slicing horizontally along the Y axis
                int minSplit = minY + currentMinLength;
                int maxSplit = maxY - currentMinLength + 1;
                if (minSplit > maxSplit) { splitVertical = !splitVertical; continue; }

                int rangeSize = maxSplit - minSplit;
                int centerBias = rangeSize / 3;
                int biasedMin = minSplit + centerBias;
                int biasedMax = maxSplit - centerBias;
                if (biasedMin > biasedMax) { biasedMin = minSplit; biasedMax = maxSplit; }

                int splitLine = Random.Range(biasedMin, biasedMax + 1);

                foreach (var tile in currentRoom)
                {
                    if (tile.y < splitLine) roomA.Add(tile);
                    else roomB.Add(tile);
                }

                // Reject if either resulting room is too sliver-like
                int aLength = splitLine - minY;
                int bLength = maxY - splitLine + 1;
                if ((float)width / aLength > maxAspectRatio || (float)width / bLength > maxAspectRatio)
                { splitVertical = !splitVertical; continue; }
            }

            // Because the house layout is irregular, a straight slice might occasionally catch an empty corner and make an empty room
            // If that happens, reject the split
            if (roomA.Count == 0 || roomB.Count == 0) { splitVertical = !splitVertical; continue; }

            // Apply strict validation to both newly generated spaces to prevent void spaces (will return immediately if we are allowing void spaces)
            if (!IsRoomViable(roomA) || !IsRoomViable(roomB)) { splitVertical = !splitVertical; continue; }

            return true;
        }

        return false;
    }

    bool IsRoomViable(HashSet<Vector2Int> room)
    {
        // If void spaces are allowed, bypass the strict check
        if (allowVoidSpaces) return true;

        // Failsafe for completely empty rooms
        if (room.Count == 0) return false;

        // Check tile count to prevent chunks that have almost no floor off the bat
        if (room.Count < (minViableWidth * minViableLength))
        {
            //Debug.Log($"Rejected: Tile count in the room was less than the possible {minViableWidth} x {minViableLength} viable spaces we're checking.");
            return false;
        }

        // Ensure EVERY tile belongs to at least one valid block of minimum dimensions
        foreach (Vector2Int tile in room)
        {
            if (!IsTileInViableBlock(tile, room))
            {
                //Debug.Log($"Rejected: Found a narrow nook or void space at local tile {tile}.");
                return false;
            }
        }

        //Debug.Log("Accepted: Room chunk passed all strict viability checks.");
        return true;
    }

    // Checks if a specific tile is part of at least one valid WxL or LxW block inside the room
    bool IsTileInViableBlock(Vector2Int tile, HashSet<Vector2Int> room)
    {
        // Check both orientations to allow for horizontal and vertical rooms/hallways
        return CheckBlockAtTile(tile, room, minViableWidth, minViableLength) ||
               CheckBlockAtTile(tile, room, minViableLength, minViableWidth);
    }

    // Validates if a rectangle of size w x l containing the target tile exists entirely within the room (i.e., turning it into a rectangle does NOT overlap with other rooms/empty space outside the room)
    bool CheckBlockAtTile(Vector2Int tile, HashSet<Vector2Int> room, int w, int l)
    {
        // Iterate through every possible starting position for a w x l block that contains tile
        for (int startX = tile.x - w + 1; startX <= tile.x; startX++)
        {
            for (int startY = tile.y - l + 1; startY <= tile.y; startY++)
            {
                bool isValidBlock = true;

                // Verify if this specific theoretical block is entirely contained within the actual room footprint
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < l; y++)
                    {
                        if (!room.Contains(new Vector2Int(startX + x, startY + y)))
                        {
                            isValidBlock = false;
                            break; // Break the inner Y loop
                        }
                    }
                    if (!isValidBlock) break; // Break the inner X loop
                }

                // If we found even one valid block that fits perfectly, this tile is safe!
                if (isValidBlock) return true;
            }
        }

        // If we checked all possible blocks and none fit, this tile is an unviable nook/void space
        return false;
    }

    // Ensures rooms on upper floors respect the load-bearing wall positions from the ground floor
    // If a room straddles a load-bearing wall boundary, it is split at that boundary
    private void EnforceLoadBearingWalls(List<HashSet<Vector2Int>> floorRooms, HashSet<string> loadBearingWalls)
    {
        bool changed = true;
        int safetyLimit = 20;

        while (changed && safetyLimit-- > 0)
        {
            changed = false;
            for (int i = 0; i < floorRooms.Count; i++)
            {
                var room = floorRooms[i];
                if (room.Count == 0) continue;

                foreach (string wall in loadBearingWalls)
                {
                    string[] parts = wall.Split('_');
                    string axis = parts[0];
                    int val = int.Parse(parts[1]);

                    HashSet<Vector2Int> sideA = new HashSet<Vector2Int>();
                    HashSet<Vector2Int> sideB = new HashSet<Vector2Int>();

                    if (axis == "X")
                    {
                        sideA = new HashSet<Vector2Int>(room.Where(t => t.x < val));
                        sideB = new HashSet<Vector2Int>(room.Where(t => t.x >= val));
                    }
                    else // Y
                    {
                        sideA = new HashSet<Vector2Int>(room.Where(t => t.y < val));
                        sideB = new HashSet<Vector2Int>(room.Where(t => t.y >= val));
                    }

                    // If the room spans the load-bearing boundary, split it
                    if (sideA.Count > 0 && sideB.Count > 0)
                    {
                        floorRooms[i] = sideA;
                        floorRooms.Add(sideB);
                        changed = true;
                        break;
                    }
                }
                if (changed) break;
            }
        }

        // Remove any rooms emptied by the splits
        floorRooms.RemoveAll(r => r.Count == 0);
    }
    #endregion

    #region Core Building Callers
    //GameObject BuildRoomGeometry(int id, int floor, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos, float heightOffset, Transform parentFloor, Vector2Int stairTile, HashSet<string> floorDoors)
    GameObject BuildRoomGeometry(int id, int floor, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos, float heightOffset, Transform parentFloor, Vector2Int stairTile, HashSet<string> floorDoors, string roomType, HashSet<Vector2Int> stairwellRoom = null)
    {
        // Create the Parent GameObject
        GameObject roomParent = new GameObject($"Room_{id}");
        roomParent.transform.SetParent(parentFloor);
        roomParent.transform.position = new Vector3(worldPos.x, heightOffset, worldPos.y);

        // Create sub-groups for the Floors, Walls, and Ceiling tiles so we can combine them later
        GameObject floorGroup = new GameObject("Floors");
        GameObject ceilingGroup = new GameObject("Ceilings");
        GameObject wallGroup = new GameObject("Walls");
        floorGroup.transform.parent = roomParent.transform; // was roomParent.transform
        floorGroup.transform.position = new Vector3(floorGroup.transform.position.x, floorGroup.transform.position.y - 0.5f, floorGroup.transform.position.z);
        ceilingGroup.transform.parent = roomParent.transform;
        wallGroup.transform.parent = roomParent.transform;

        // An "undo" option to undo the generation if we didn't like it
#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(roomParent, "Generate Room");
#endif

        // A tile is in the stairwell if this room IS the stairwell room
        bool thisRoomIsStairwell = (stairwellRoom != null && stairwellRoom == normalizedCoords);

        // Calculate the windows to plan out for this specific room
        HashSet<string> plannedWindows = PrecalculateWindows(normalizedCoords, roomType, floor);

        // Build the Room Geometry
        foreach (Vector2Int coord in normalizedCoords)
        {
            // Position relative to the room parent
            Vector3 tilePos = new Vector3(coord.x, heightOffset, coord.y);
            bool isStairArea = thisRoomIsStairwell; // Every tile in the stairwell room is a stair tile

            // Spawn Floor (skip if it's the stairwell, UNLESS it's the ground floor)
            if (!isStairArea || floor == 0)
                SpawnPrimitive(PrimitiveType.Cube, floorGroup.transform, tilePos, Vector3.one, "Floor");

            // Spawn Ceiling (skip if it's the stairwell, UNLESS it's the very top floor/roof)
            if (!isStairArea || floor == numberOfFloors - 1)
                SpawnPrimitive(PrimitiveType.Cube, ceilingGroup.transform, tilePos + Vector3.up * wallHeight, Vector3.one, "Ceiling");

            // Spawn Walls (Check neighbors)
            CheckAndSpawnWalls(coord, normalizedCoords, wallGroup.transform, tilePos, floorDoors, stairTile, stairDepth, floor, plannedWindows);

            // FUTURE: call a separate script to spawn items in the spaces
        }

        // Bake the different groups of primitive child tiles into single objects
        CombineChildrenMeshes(
            floorGroup,
            floorMaterial,
            addCollider: addCollidersToCombinedGeometry,
            addTeleportationArea: enableTeleportationOnFloors);
        CombineChildrenMeshes(
            ceilingGroup,
            ceilingMaterial,
            addCollider: addCollidersToCombinedGeometry);
        CombineChildrenMeshes(
            wallGroup,
            wallMaterial,
            addCollider: addCollidersToCombinedGeometry);

        // After baking, capture the default material of the roof through our RoofData tracker
        RoofData data = ceilingGroup.AddComponent<RoofData>();
        data.originalMaterial = ceilingGroup.GetComponent<MeshRenderer>().sharedMaterial;
        allRoomRoofs.Add(ceilingGroup);

        // Add the teleportation capability to the floor
        GameObject teleportInstance = Instantiate(Resources.Load<GameObject>("Locomotion/Teleport Area Invisible"), floorGroup.transform);
        teleportInstance.name = "Teleport Area Invisible";

        MeshCollider floorCollider = floorGroup.GetComponent<MeshCollider>();

        if (floorCollider != null)
        {
            TeleportationArea teleportScript = teleportInstance.GetComponent<TeleportationArea>();
            teleportScript.colliders.Clear();
            teleportScript.colliders.Add(floorCollider);
        }

        return roomParent;
    }

    // Placing walls on the floors of generated rooms
    void CheckAndSpawnWalls(Vector2Int localCoord, HashSet<Vector2Int> roomTiles, Transform parent, Vector3 pos, HashSet<string> floorDoors, Vector2Int stairTile, int stairDepth, int floorLevel, HashSet<string> plannedWindows)
    {
        // Directions: Up, Down, Left, Right
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        string[] dirLabels = { "N", "S", "W", "E" };

        for (int i = 0; i < 4; i++)
        {
            Vector2Int dir = directions[i];
            string currentDir = dirLabels[i];
            Vector2Int neighbor = localCoord + dir;

            // Is the neighbor inside this same room?
            if (roomTiles.Contains(neighbor))
                continue; // No wall needed, it's open floor

            // We check this before doors or interior walls to ensure the opening is preserved.
            if (floorLevel == 0 && stairwellIsOpenBottom && stairwellOpenBottomY != -999)
            { 
                // Check if the CURRENT tile is the stairwell entry looking South (out)
                bool isStairwellEntryWall = (localCoord.y == stairwellOpenBottomY) &&
                                             activeStairwellFootprint != null &&
                                             activeStairwellFootprint.Contains(localCoord) &&
                                             dir == Vector2Int.down;

                // Check if the NEIGHBOR tile is the stairwell entry looking North (in)
                // This stops the hallway/neighbor room from spawning a wall against the stairs
                bool isNeighborStairwellEntry = (neighbor.y == stairwellOpenBottomY) &&
                                                 activeStairwellFootprint != null &&
                                                 activeStairwellFootprint.Contains(neighbor) &&
                                                 dir == Vector2Int.up;

                if (isStairwellEntryWall || isNeighborStairwellEntry)
                {
                    continue; // Suppress the wall/door for BOTH the stairwell and its neighbor
                }
            }
            else
            {
                bool floor = false;
                bool stairs = false;
                if (floorLevel == 0)
                    floor = true;
                if (stairwellOpenBottomY != -999)
                    stairs = true;
            }

            // Check if this is an exterior wall
            bool isOutsideHouse = !allHouseOccupiedTiles.Contains(neighbor);

            // Skip the walls if both tiles are in the stairwell layout and it's not an exterior wall
            // (prevents the outside wall from getting a hole punched in it)
            if (!isOutsideHouse)
            {
                // Check if the specific interior wall tile is on the list of doors
                string edge = GetEdgeKey(localCoord, neighbor);
                if (floorDoors.Contains(edge))
                {
                    float actualDoorHeight = wallHeight;

                    // Randomly choose between a floor-to-ceiling archway or a framed doorway
                    if (Random.value > 0.5f && wallHeight > doorHeight)
                    {
                        // Spawn a header above the doorway
                        SpawnWall(pos, dir, parent, isInterior: true, doorHeight, wallHeight - doorHeight);
                        actualDoorHeight = doorHeight;
                    }

                    // Spawn the actual interior door prefab
                    Vector3 prefabPos = pos + new Vector3(dir.x * 0.5f, 0f, dir.y * 0.5f); // was dir.y * 0.5f
                    GameObject interiorDoorPrefab = Resources.Load<GameObject>("Interior Prefabs/Door_Interior");

                    if (interiorDoorPrefab != null)
                    {
                        //Debug.Log("Should try to spawn interior door");
                        // Parent to parent.parent to escape the mesh combiner - prevents it from becoming the wall + same color/material as the wall
                        GameObject spawnedDoor = Instantiate(interiorDoorPrefab, prefabPos, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)), parent.parent);

                        // Fit it to the hole. Depth is 0.15f to slightly overlap the 0.1f interior wall thickness
                        FitPrefabToHole(spawnedDoor, 1f, actualDoorHeight, 0.15f, pos.y);
                    }

                    continue; // Skip the standard wall spawn
                }

                // If neighbor is in the house but not in this room (and wasn't a door/stair opening)
                SpawnWall(pos, dir, parent, isInterior: true);
                continue;
            }

            // Is the neighbor still inside the house, but in a different room?
            /*if (allHouseOccupiedTiles.Contains(neighbor))
            {
                // Spawn an interior wall to divide the rooms.
                SpawnWall(pos, dir, parent, isInterior: true);
                continue;
            }*/

            // Generate a Unique Key for this specific "Wall Run"
            string wallKey = (currentDir == "N" || currentDir == "S")
                ? $"{currentDir}_{localCoord.y}"
                : $"{currentDir}_{localCoord.x}";

            int currentIdx = (currentDir == "N" || currentDir == "S") ? localCoord.x : localCoord.y;

            // Check for Main Door Proximity
            bool nearMainDoor = false;
            // Only check proximity if we are on the ground floor and the wall direction matches the door
            if (floorLevel == 0 && currentDir == mainDoorDirection)
            {
                // Check if this specific wall run contains the main door
                bool isSameRun = (currentDir == "N" || currentDir == "S")
                    ? localCoord.y == mainDoorTile.y
                    : localCoord.x == mainDoorTile.x;

                if (isSameRun)
                {
                    int doorIdx = (currentDir == "N" || currentDir == "S") ? mainDoorTile.x : mainDoorTile.y;

                    // If distance is < 2, it's either the door itself or the immediate neighbor
                    if (Mathf.Abs(currentIdx - doorIdx) < 2)
                    {
                        nearMainDoor = true;
                    }
                }
            }

            // Check if THIS specific wall segment is the designated Main Door
            bool isMainDoor = (floorLevel == 0 && localCoord == mainDoorTile && currentDir == mainDoorDirection);

            // Check if our pre-calculator planned a window here
            string windowKey = $"{localCoord.x},{localCoord.y}_{currentDir}";
            bool isWindow = !isMainDoor && !nearMainDoor && plannedWindows.Contains(windowKey);

            if (isMainDoor)
            {
                // Spawn a header wall above the main door
                if (wallHeight > doorHeight)
                {
                    SpawnWall(pos, dir, parent, isInterior: false, doorHeight, wallHeight - doorHeight);
                }

                // Spawns placeholder prefab from looking like a broken wall chunk in the floor
                Vector3 prefabPos = pos + new Vector3(dir.x * 0.5f, 0f, dir.y * 0.5f); // pos + new Vector3(dir.x * 0.5f, 0f, dir.y * 0.5f);
                GameObject doorPrefab = Resources.Load<GameObject>("Interior Prefabs/Door");
                

                if (doorPrefab != null)
                {
                    // Parent to parent.parent to escape the mesh combiner
                    GameObject spawnedDoor = Instantiate(doorPrefab, prefabPos, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)), parent.parent);
                    //Debug.Log("Should try to spawn main door");
                    // Width is 1 tile, Height is doorHeight, Depth is 0.25f (slightly thicker than the 0.2f wall to prevent texture z-fighting)
                    FitPrefabToHole(spawnedDoor, 1f, doorHeight, 0.25f, pos.y);
                    spawnedDoor.GetComponent<BoxCollider>().enabled = false; // set the collider to false so we can teleport through
                }

                continue; // Prevent standard full wall from spawning
            }
            else if (isWindow)
            {
                // Record this window's position to block neighbors and satisfy room egress
                lastWindowIndex[wallKey] = currentIdx;
                roomsWithWindows.Add(roomTiles);

                // Define how tall the window actually is (based on your inspector variables)
                float actualWindowHeight = windowTopHeight - windowSillHeight;

                // Find the exact vertical middle of the current wall
                float middleOfWall = wallHeight / 2f;

                // Dynamically set the sill and top so the window sits perfectly in the center
                float dynamicSillHeight = middleOfWall - (actualWindowHeight / 2f);
                float dynamicTopHeight = middleOfWall + (actualWindowHeight / 2f);

                // Spawn the wall below the window (the sill)
                SpawnWall(pos, dir, parent, isInterior: false, 0f, dynamicSillHeight);

                // Spawn the wall above the window (the header)
                if (wallHeight > dynamicTopHeight)
                {
                    SpawnWall(pos, dir, parent, isInterior: false, dynamicTopHeight, wallHeight - dynamicTopHeight);
                }

                // Spawns placeholder prefab for window
                GameObject windowPrefab = Resources.Load<GameObject>("Interior Prefabs/Window");

                Vector3 prefabPos = pos + new Vector3(dir.x * 0.5f, dynamicSillHeight, dir.y * 0.5f);

                if (windowPrefab != null)
                {
                    // Parent to parent.parent to escape the mesh combiner
                    GameObject spawnedWindow = Instantiate(windowPrefab, prefabPos, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)));

                    // Set the parent while telling Unity NOT to change the world position
                    spawnedWindow.transform.SetParent(parent.parent, true);

                    // Pass the dynamic sill height as the "ground" level for the window to rest on!
                    FitPrefabToHole(spawnedWindow, 1f, actualWindowHeight, 0.25f, pos.y + dynamicSillHeight);
                }

                continue; // Prevent standard full wall from spawning
            }
            /*else if (isWindow)
            {
                // Record this window's position to block neighbors and satisfy room egress
                lastWindowIndex[wallKey] = currentIdx;
                roomsWithWindows.Add(roomTiles);

                // Spawn the wall below the window (the sill)
                SpawnWall(pos, dir, parent, isInterior: false, 0f, windowSillHeight);

                // Spawn the wall above the window (the header)
                if (wallHeight > windowTopHeight)
                {
                    SpawnWall(pos, dir, parent, isInterior: false, windowTopHeight, wallHeight - windowTopHeight);
                }

                // Spawns placeholder prefab for window

                // Assuming the spawn point is at the center of the object's transform, we need to figure out HALF the object's height to account for starting at the center
                GameObject windowPrefab = Resources.Load<GameObject>("Interior Prefabs/Window");
                
                // Need to fix the part where we position it with windowSillHeight - need to choose this based on sill height + an offset of where the prefab's pivot point is
                Vector3 prefabPos = pos + new Vector3(dir.x * 0.5f, windowSillHeight, dir.y * 0.5f);
                

                if (windowPrefab != null)
                {
                    // Parent to parent.parent to escape the mesh combiner
                    GameObject spawnedWindow = Instantiate(windowPrefab, prefabPos, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.y)));
                    //Debug.Log("Should try to spawn window");

                    // Set the parent while telling Unity NOT to change the world position
                    spawnedWindow.transform.SetParent(parent.parent, true);

                    // Height is the gap between the sill and the top
                    float actualWindowHeight = windowTopHeight - windowSillHeight;

                    // Pass the sill height as the "ground" level for the window to rest on! If we pass pos.y, it will try to rest on the floor and mesh with the lower wall
                    FitPrefabToHole(spawnedWindow, 1f, actualWindowHeight, 0.25f, pos.y + windowSillHeight);
                }

                continue; // Prevent standard full wall from spawning
            }*/
            else
            {
                // If it's not in the room or in the house layout, this neighbor space is outside
                // We spawn an exterior wall to block off the outside
                SpawnWall(pos, dir, parent, isInterior: false);
            }
        }
    }
    #endregion

    #region Spawning Helpers
    void SpawnWall(Vector3 tilePos, Vector2Int dir, Transform parent, bool isInterior, float startHeight = 0f, float customHeight = -1f)
    {
        // If no custom height is provided, use the default wallHeight
        float actualHeight = customHeight < 0 ? wallHeight : customHeight;

        // Calculate the center point on the Y axis for this specific wall chunk
        float centerHeight = startHeight + (actualHeight / 2f);

        // Apply the directional offset so the wall sits on the edge of the tile, not the center
        Vector3 wallPos = tilePos + new Vector3(dir.x * 0.5f, centerHeight, dir.y * 0.5f);

        float thickness = isInterior ? 0.1f : 0.2f;

        Vector3 wallScale = new Vector3(
            Mathf.Abs(dir.y) + (Mathf.Abs(dir.x) * thickness),
            actualHeight,
            Mathf.Abs(dir.x) + (Mathf.Abs(dir.y) * thickness)
        );

        string wallName = isInterior ? "Interior_Wall" : "Exterior_Wall";
        GameObject wall = SpawnPrimitive(PrimitiveType.Cube, parent, wallPos, wallScale, wallName);

        // FUTURE: Apply different material to interior/exterior walls
        /*
        if (!isInterior && exteriorWallMaterial != null) 
            wall.GetComponent<MeshRenderer>().material = exteriorWallMaterial;
        */
    }

    void SpawnStairs(Vector2Int topTile, float heightOffset, Transform parent, int depth)
    {
        if (topTile.x == -999) return;

        // Vertical Bounds
        float surfaceBottom = heightOffset - 1.5f;
        float surfaceTop = heightOffset + wallHeight - 1f;
        float rise = surfaceTop - surfaceBottom;

        // Horizontal Bounds - ADJUSTED FOR TOP LANDING
        int topLandingDepth = 2; // The number of flat tiles at the top
        float run = (float)depth; // The actual slanted part of the stairs

        // Position the ramp so it starts AFTER the top landing
        // We shift the centerZ further back by the landing depth
        float centerZ = (float)topTile.y - (run - 1f) - topLandingDepth;

        // Ramp Geometry
        float rampLength = Mathf.Sqrt((run * run) + (rise * rise));
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

        float thickness = 0.2f;
        float centerY = ((surfaceBottom + surfaceTop) / 2f) - ((thickness / 2f) * Mathf.Cos(angle * Mathf.Deg2Rad));
        float centerX = topTile.x + 0.5f;

        // Spawn the slanted ramp
        Vector3 rampPos = new Vector3(centerX, centerY, centerZ);
        GameObject ramp = SpawnPrimitive(PrimitiveType.Cube, parent, rampPos, new Vector3(1.95f, thickness, rampLength), "Stair_Ramp");
        ramp.transform.localRotation = Quaternion.Euler(-angle, 0, 0);

        // SPAWN THE TOP LANDING (The flat bridge)
        // Positioned at the topTile, flush with the upper floor
        float landingCenterZ = (float)topTile.y - (topLandingDepth * 1.5f) + 0.5f; // was (topLandingDepth/2f) + 0.5f
        Vector3 landingPos = new Vector3(centerX, surfaceTop - (thickness / 2f), landingCenterZ);
        GameObject landing = SpawnPrimitive(PrimitiveType.Cube, parent, landingPos, new Vector3(1.95f, thickness, topLandingDepth), "Stair_Top_Landing");

        if (floorMaterial != null)
        {
            ramp.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            landing.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        }
    }

    /*void SpawnStairs(Vector2Int topTile, float heightOffset, Transform parent, int depth)
    {
        // No stairwell was carved, skip silently
        if (topTile.x == -999) return;

        // Figure out the vertical bounds of the ramp
        // The floor we are standing on has a top surface at heightOffset + 0.5 - the next floor up is wallHeight + 1 unit
        float surfaceBottom = heightOffset - 1.5f; // + 0.5f;
        float surfaceTop = heightOffset + wallHeight - 1f; // + 1 unit for the floor thickness
        float rise = surfaceTop - surfaceBottom;

        // Figure out the horizontal bounds of the ramp
        // The hole ends at topTile.y - the ramp starts 'depth' tiles back. To center it perfectly, we find the middle of the 'run'.
        float run = (float)depth;
        float centerZ = (float)topTile.y - (run - 1f);   // was (float)topTile.y - (run / 2f) + 0.5f - 1.0f;

        // Geometry to determine the angle of the ramp
        float rampLength = Mathf.Sqrt((run * run) + (rise * rise));
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

        // Alignment tweak - helps us to make the ramp flush with the upper floor
        // Lower the center slightly so the top surface of the ramp is what aligns with the floor, not the core center
        float thickness = 0.2f;
        float centerY = ((surfaceBottom + surfaceTop) / 2f) - ((thickness / 2f) * Mathf.Cos(angle * Mathf.Deg2Rad));

        float centerX = topTile.x + 0.5f; // 0.5f offsets to the center of a 2-tile-wide well 

        // Spawn the ramp - X must be exactly topTile.x to align with the hole
        Vector3 rampPos = new Vector3(centerX, centerY, centerZ);
        GameObject ramp = SpawnPrimitive(PrimitiveType.Cube, parent, rampPos, new Vector3(1.95f, thickness, rampLength), "Stair_Ramp"); // was 0.95f

        // Rotation - apply the angle rotation to the ramp's position so it connects the floors
        ramp.transform.localRotation = Quaternion.Euler(-angle, 0, 0);

        if (floorMaterial != null)
            ramp.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
    }*/

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
    #endregion

    #region House Landmark Determination Helpers
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

    // Finds a guaranteed stairwell footprint from the master layout before subdivision
    void DetermineGlobalStairwell(HashSet<Vector2Int> layout, out HashSet<Vector2Int> footprint, out Vector2Int stairTile)
    {
        footprint = null;
        stairTile = new Vector2Int(-999, -999);
        int width = 2; // stairwellWidth
        // We increase this to 9: (2 tile bottom landing + 5 tiles ramp + 2 tile top landing)
        int totalLength = stairDepth + 4;

        // Scan from top to bottom, left to right
        var possibleTiles = layout.OrderByDescending(t => t.y).ThenBy(t => t.x).ToList();

        foreach (var tile in possibleTiles)
        {
            bool valid = true;
            HashSet<Vector2Int> testFootprint = new HashSet<Vector2Int>();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < totalLength; y++) // was y < stairDepth
                {
                    Vector2Int checkTile = new Vector2Int(tile.x + x, tile.y - y);
                    if (!layout.Contains(checkTile))
                    {
                        valid = false;
                        break;
                    }
                    testFootprint.Add(checkTile);
                }
                if (!valid) break;
            }

            // If we found a 2x5 block that fits entirely inside the master layout, commit to it
            if (valid)
            {
                footprint = testFootprint;
                stairTile = tile; // Top-left tile of the stairwell
                return;
            }
        }

        Debug.LogWarning("House layout too small/irregular to fit a stairwell!");
    }

    // Strips out randomized doors and forces the door to spawn exactly at the top/bottom of the ramp
    void ForceStairwellDoors(int floor, Vector2Int topTile, HashSet<Vector2Int> stairwellFootprint, HashSet<string> floorDoors)
    {
        // Strip ALL random doors that were assigned to the stairwell by the MST algorithm
        List<string> doorsToRemove = new List<string>();
        foreach (var door in floorDoors)
        {
            string[] parts = door.Split('_');
            string[] p1 = parts[0].Split(',');
            string[] p2 = parts[1].Split(',');
            Vector2Int t1 = new Vector2Int(int.Parse(p1[0]), int.Parse(p1[1]));
            Vector2Int t2 = new Vector2Int(int.Parse(p2[0]), int.Parse(p2[1]));

            bool t1In = stairwellFootprint.Contains(t1);
            bool t2In = stairwellFootprint.Contains(t2);

            if (t1In != t2In) doorsToRemove.Add(door); // One tile is inside, one is outside
        }
        foreach (var d in doorsToRemove) floorDoors.Remove(d);

        if (floor == 0) // Ground floor
        {
            // The very bottom edge of the entire stairwell room is (topTile.y - 8)
            int totalRoomLength = stairDepth + 4; // 9 tiles total to include the landings
            int bottomEdgeY = topTile.y - (totalRoomLength - 1);
            //Vector2Int bottomTile = new Vector2Int(topTile.x, topTile.y - (stairDepth + 1));
            stairwellOpenBottomY = bottomEdgeY; // was bottomTile.y

            if (!stairwellIsOpenBottom)
            {
                Debug.Log("[COMMON EVENT]: Generating stairwell behind door");

                // We ONLY want a door on the SOUTH edge of the two bottom-most landing tiles
                Vector2Int[] landingTiles = {new Vector2Int(topTile.x, bottomEdgeY), new Vector2Int(topTile.x + 1, bottomEdgeY)};

                // Standard: punch a door at the bottom of the stairwell into the adjacent room
                // We only look SOUTH (down) to ensure the door is at the front of the landing
                Vector2Int searchDir = Vector2Int.down;
                foreach (Vector2Int checkTile in landingTiles)
                {
                    Vector2Int neighbor = checkTile + searchDir;
                    // Ensure the neighbor is part of the house but NOT part of the stairwell
                    if (allHouseOccupiedTiles.Contains(neighbor) && !stairwellFootprint.Contains(neighbor))
                    {
                        floorDoors.Add(GetEdgeKey(checkTile, neighbor));
                        return; // Door placed successfully at the landing
                    }
                }

                // Fallback if there is no path directly south
                Vector2Int[] sideDirs = { Vector2Int.left, Vector2Int.right };
                foreach (Vector2Int checkTile in landingTiles)
                {
                    foreach (Vector2Int sDir in sideDirs)
                    {
                        Vector2Int neighbor = checkTile + sDir;
                        if (allHouseOccupiedTiles.Contains(neighbor) && !stairwellFootprint.Contains(neighbor))
                        {
                            floorDoors.Add(GetEdgeKey(checkTile, neighbor));
                            return;
                        }
                    }
                }

                /*Vector2Int[] searchDirs = { Vector2Int.down, Vector2Int.left, Vector2Int.right };
                foreach (Vector2Int dir in searchDirs)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Vector2Int checkTile = new Vector2Int(bottomTile.x + i, bottomTile.y);
                        Vector2Int neighbor = checkTile + dir;
                        if (allHouseOccupiedTiles.Contains(neighbor) && !stairwellFootprint.Contains(neighbor))
                        {
                            floorDoors.Add(GetEdgeKey(checkTile, neighbor));
                            return;
                        }
                    }
                }*/
            }
            // Open-bottom: no door added — the wall will be suppressed in BuildRoomGeometry
        }
        else // Upper floor
        {
            // Standard: punch a door at the top landing into the adjacent room
            Vector2Int[] searchDirs = { Vector2Int.up, Vector2Int.left, Vector2Int.right };
            foreach (Vector2Int dir in searchDirs)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector2Int checkTile = new Vector2Int(topTile.x + i, topTile.y);
                    Vector2Int neighbor = checkTile + dir;
                    if (allHouseOccupiedTiles.Contains(neighbor) && !stairwellFootprint.Contains(neighbor))
                    {
                        floorDoors.Add(GetEdgeKey(checkTile, neighbor));
                        return;
                    }
                }
            }
        }
    }

    // Returns a set of wall positions that are load-bearing (span the entire length or width of a house)
    private HashSet<string> DetectLoadBearingWalls(HashSet<Vector2Int> houseLayout)
    {
        HashSet<string> loadBearing = new HashSet<string>();

        int minX = houseLayout.Min(t => t.x), maxX = houseLayout.Max(t => t.x);
        int minY = houseLayout.Min(t => t.y), maxY = houseLayout.Max(t => t.y);

        // Check each X column: if every Y value in the house at this X exists, it spans the full depth
        for (int x = minX + 1; x < maxX; x++) // exclude exterior walls
        {
            bool fullColumn = true;
            for (int y = minY; y <= maxY; y++)
            {
                // If ANY tile at this X is missing from the layout, it's not a full-span wall
                if (!houseLayout.Contains(new Vector2Int(x, y)) && houseLayout.Any(t => t.y == y))
                {
                    fullColumn = false;
                    break;
                }
            }
            if (fullColumn) loadBearing.Add($"X_{x}");
        }

        // Check each Y row
        for (int y = minY + 1; y < maxY; y++)
        {
            bool fullRow = true;
            for (int x = minX; x <= maxX; x++)
            {
                if (!houseLayout.Contains(new Vector2Int(x, y)) && houseLayout.Any(t => t.x == x))
                {
                    fullRow = false;
                    break;
                }
            }
            if (fullRow) loadBearing.Add($"Y_{y}");
        }

        return loadBearing;
    }

    void DetermineMainDoor(List<HashSet<Vector2Int>> groundFloorRooms)
    {
        List<(Vector2Int tile, string dir)> candidates = new List<(Vector2Int, string)>();

        // Gather all raw exterior wall candidates
        foreach (var room in groundFloorRooms)
        {
            foreach (var tile in room)
            {
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.up)) candidates.Add((tile, "N"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.down)) candidates.Add((tile, "S"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.right)) candidates.Add((tile, "E"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.left)) candidates.Add((tile, "W"));
            }
        }

        if (candidates.Count == 0) return;

        // Identify contiguous wall segments (runs)
        List<List<(Vector2Int tile, string dir)>> wallSegments = new List<List<(Vector2Int, string)>>();

        foreach (string d in new[] { "N", "S", "E", "W" })
        {
            // Get all tiles facing this specific direction
            var dirTiles = candidates.Where(c => c.dir == d).ToList();
            if (dirTiles.Count == 0) continue;

            // Sort tiles to find sequences. 
            // For N/S walls, sort by Y then X (to find runs along X). 
            // For E/W walls, sort by X then Y (to find runs along Y).
            if (d == "N" || d == "S")
                dirTiles = dirTiles.OrderBy(c => c.tile.y).ThenBy(c => c.tile.x).ToList();
            else
                dirTiles = dirTiles.OrderBy(c => c.tile.x).ThenBy(c => c.tile.y).ToList();

            List<(Vector2Int tile, string dir)> currentRun = new List<(Vector2Int, string)> { dirTiles[0] };

            for (int i = 1; i < dirTiles.Count; i++)
            {
                bool isAdjacent = false;
                if (d == "N" || d == "S")
                    isAdjacent = dirTiles[i].tile.y == dirTiles[i - 1].tile.y && dirTiles[i].tile.x == dirTiles[i - 1].tile.x + 1;
                else
                    isAdjacent = dirTiles[i].tile.x == dirTiles[i - 1].tile.x && dirTiles[i].tile.y == dirTiles[i - 1].tile.y + 1;

                if (isAdjacent)
                {
                    currentRun.Add(dirTiles[i]);
                }
                else
                {
                    wallSegments.Add(new List<(Vector2Int, string)>(currentRun));
                    currentRun.Clear();
                    currentRun.Add(dirTiles[i]);
                }
            }
            wallSegments.Add(currentRun);
        }

        // Score the segments and pick the winner
        // We favor longer segments, and give a multiplier bonus to South-facing walls.
        var bestSegment = wallSegments
            .OrderByDescending(seg => {
                float score = seg.Count;
                if (seg[0].dir == "S") score *= 2.0f; // High priority for South (Front)
            return score;
            })
            .FirstOrDefault();

        if (bestSegment != null)
        {
            // Center the door in the chosen segment
            var chosen = bestSegment[bestSegment.Count / 2];
            mainDoorTile = chosen.tile;
            mainDoorDirection = chosen.dir;
        }
    }

    // Validates if the path to the main egress door is continuous and unobstructed
    bool ValidateEgressPath(List<HashSet<Vector2Int>> rooms, HashSet<string> doors, Vector2Int egressDoorTile, int floor)
    {
        HashSet<Vector2Int> walkableTiles = new HashSet<Vector2Int>();
        foreach (var room in rooms) walkableTiles.UnionWith(room);

        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        // Start the pathfinding at the egress door
        queue.Enqueue(egressDoorTile);
        visited.Add(egressDoorTile);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            foreach (Vector2Int dir in dirs)
            {
                Vector2Int neighbor = current + dir;

                if (walkableTiles.Contains(neighbor) && !visited.Contains(neighbor))
                {
                    // If they are in the same room, it's a clear path
                    bool sameRoom = IsInSameRoom(current, neighbor, rooms);

                    // If they are in different rooms, check if a door exists between them
                    bool hasDoor = doors.Contains(GetEdgeKey(current, neighbor));

                    bool isOpenStairwell = false;
                    if (floor == 0 && stairwellIsOpenBottom && stairwellOpenBottomY != -999)
                    {
                        // Is the current tile part of the stairwell's bottom landing?
                        bool currentIsLanding = activeStairwellFootprint.Contains(current) && current.y == stairwellOpenBottomY;

                        // Is the neighbor tile part of the stairwell's bottom landing?
                        bool neighborIsLanding = activeStairwellFootprint.Contains(neighbor) && neighbor.y == stairwellOpenBottomY;

                        // If we are moving TO or FROM the landing tiles, the "wall" is open
                        if (currentIsLanding || neighborIsLanding)
                        {
                            isOpenStairwell = true;
                        }
                    }

                    if (sameRoom || hasDoor || isOpenStairwell)
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // If the visited tiles equal the total walkable tiles, egress is guaranteed from every point
        return visited.Count == walkableTiles.Count;
    }

    // Helper method
    bool IsInSameRoom(Vector2Int a, Vector2Int b, List<HashSet<Vector2Int>> rooms)
    {
        foreach (var room in rooms)
        {
            if (room.Contains(a) && room.Contains(b)) return true;
        }
        return false;
    }

    // Checks if the tile has valid room tiles on both sides along the wall axis
    bool IsValidWindowLocation(Vector2Int tile, Vector2Int wallDir, HashSet<Vector2Int> roomTiles)
    {
        // Find the perpendicular directions to the wall
        Vector2Int left = new Vector2Int(-wallDir.y, wallDir.x);
        Vector2Int right = new Vector2Int(wallDir.y, -wallDir.x);

        // Check if the adjacent tiles along the wall are part of this SAME room
        bool leftInRoom = roomTiles.Contains(tile + left);
        bool rightInRoom = roomTiles.Contains(tile + right);

        // If both sides are in the room, it's not a corner or an edge!
        return leftInRoom && rightInRoom;
    }
    #endregion

    #region Room Assignment Logic
    private Dictionary<HashSet<Vector2Int>, string> AssignRoomTypesBySize(List<HashSet<Vector2Int>> floorRooms, int floorLevel, HashSet<Vector2Int> stairwellRoom)
    {
        Dictionary<HashSet<Vector2Int>, string> assignments = new Dictionary<HashSet<Vector2Int>, string>();
        List<HashSet<Vector2Int>> unassigned = new List<HashSet<Vector2Int>>(floorRooms);

        // Structural/Hallway passes
        if (stairwellRoom != null && unassigned.Contains(stairwellRoom))
        {
            assignments[stairwellRoom] = "Stairwell";
            unassigned.Remove(stairwellRoom);
        }

        foreach (var room in unassigned.ToList())
        {
            if (IsRoomHallway(room))
            {
                assignments[room] = "Hallway";
                unassigned.Remove(room);
            }
        }

        // Sort remaining rooms from largest to smallest
        unassigned = unassigned.OrderByDescending(r => r.Count).ToList();

        // Tracker state to enforce realistic limits
        bool assignedLivingRoom = false;
        bool assignedKitchen = false;
        bool assignedDiningRoom = false;
        int bedroomCount = 0;
        int bathroomCount = 0;

        // Assign absolute primary rooms first (Living Room & Kitchen) on Ground Floor
        if (floorLevel == 0)
        {
            if (unassigned.Count > 0)
            {
                assignments[unassigned[0]] = "Living Room";
                assignedLivingRoom = true;
                unassigned.RemoveAt(0);
            }
            if (unassigned.Count > 0)
            {
                assignments[unassigned[0]] = "Kitchen";
                assignedKitchen = true;
                unassigned.RemoveAt(0);
            }
        }

        // Process remaining rooms with unique-cap checks
        List<HashSet<Vector2Int>> remainingRooms = new List<HashSet<Vector2Int>>(unassigned);
        foreach (var room in remainingRooms)
        {
            int size = room.Count;

            if (size >= 12) // Large room (>= 120 sq ft)
            {
                // Only allow ONE dining room per house, and only on the ground floor
                if (floorLevel == 0 && !assignedDiningRoom)
                {
                    assignments[room] = "Dining Room";
                    assignedDiningRoom = true;
                }
                else
                {
                    // Extra large rooms become Bedrooms (or Master Bedrooms if upstairs)
                    assignments[room] = (floorLevel == 0) ? "Bedroom" : "Master Bedroom";
                    bedroomCount++;
                }
            }
            else if (size >= 7) // Medium room (>= 70 sq ft)
            {
                assignments[room] = "Bedroom";
                bedroomCount++;
            }
            else // Small room (< 70 sq ft)
            {
                // If we don't have a bathroom yet, prioritize it!
                if (bathroomCount == 0)
                {
                    assignments[room] = "Bathroom";
                    bathroomCount++;
                }
                else
                {
                    // If we already have a bathroom, make a small room a closet or secondary bathroom
                    assignments[room] = (Random.value > 0.4f) ? "Bathroom" : "Closet";
                    if (assignments[room] == "Bathroom") bathroomCount++;
                }
            }
            unassigned.Remove(room);
        }

        // Building codes require at least 1 bathroom per house!
        // If we finished processing Floor 0 and somehow assigned 0 bathrooms, we must convert the smallest available non-essential room into a Bathroom.
        if (floorLevel == 0 && bathroomCount == 0)
        {
            var eligibleCandidates = assignments
                .Where(kvp =>
                    kvp.Value != "Living Room" &&
                    kvp.Value != "Kitchen" &&
                    kvp.Value != "Stairwell" &&
                    kvp.Value != "Hallway"
                )
                .OrderBy(kvp => kvp.Key.Count) // Smallest candidate room first
                .ToList();

            if (eligibleCandidates.Count > 0)
            {
                var targetRoom = eligibleCandidates[0].Key;
                string originalType = eligibleCandidates[0].Value;

                assignments[targetRoom] = "Bathroom";
                bathroomCount++;

                // Correct our tracker counts
                if (originalType == "Bedroom" || originalType == "Master Bedroom")
                    bedroomCount--;
                else if (originalType == "Dining Room")
                    assignedDiningRoom = false;
            }
        }

        // If this is an upper floor and we failed to make a bedroom, make one.
        if (floorLevel > 0 && bedroomCount == 0)
        {
            var eligibleCandidates = assignments
                .Where(kvp => kvp.Value != "Stairwell" && kvp.Value != "Hallway" && kvp.Value != "Bathroom")
                .OrderByDescending(kvp => kvp.Key.Count) // Largest first
                .ToList();

            if (eligibleCandidates.Count > 0)
            {
                assignments[eligibleCandidates[0].Key] = "Master Bedroom";
                bedroomCount++;
            }
        }

        return assignments;
    }
    #endregion

    #region Prefab + Mesh Helpers
    // A helper to look at where a prefabs pivot point is so we can better spawn it in place, not a little too low (use the bottom of the object for location)
    /*float CalculateVerticalOffset(GameObject instance)
    {
        // Get the local bottom of the mesh (distance from pivot to bottom)
        float meshBottomY = instance.GetComponent<Renderer>().bounds.min.y;
        // Get the pivot point at which the prefab is spawned/handled from
        float pivotY = instance.transform.position.y;

        // Return the distance from the pivot to the bottom with some funky math to make it spawn in just the right place
        return pivotY - meshBottomY;
    }*/

    float CalculateVerticalOffset(GameObject instance)
    {
        // Use GetComponentInChildren to grab the actual mesh renderer, even if the root is empty
        Renderer renderer = instance.GetComponentInChildren<Renderer>();

        // Failsafe in case there's no renderer attached to the prefab at all
        if (renderer == null) return 0f;

        // Get the local bottom of the mesh (distance from pivot to bottom)
        float meshBottomY = renderer.bounds.min.y;
        // Get the pivot point at which the prefab is spawned/handled from
        float pivotY = instance.transform.position.y;

        // Return the distance from the pivot to the bottom
        return pivotY - meshBottomY;
    }

    void FitPrefabToHole(GameObject prefabInstance, float targetWidth, float targetHeight, float targetDepth, float groundY)
    {
        // Grab the mesh filter to get the raw, unscaled bounds of the model
        MeshFilter mf = prefabInstance.GetComponentInChildren<MeshFilter>();
        if (mf == null) return;

        Vector3 originalSize = mf.sharedMesh.bounds.size;

        // Calculate the scale multiplier needed to reach the target dimensions
        float scaleX = targetWidth / originalSize.x;
        float scaleY = targetHeight / originalSize.y;
        float scaleZ = targetDepth / originalSize.z;

        // Apply the new scale - local X is width, local Y is height, and local Z is depth (thickness)
        prefabInstance.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);

        // Move the object to groundY, then add the offset to bring the bottom up to the surface
        float yOffset = CalculateVerticalOffset(prefabInstance); // Should find exact prefab pivot point
        
        // Set the world position - groundY is the floor, yOffset is the adjustment needed to make it flush depending on if it's a window/door
        prefabInstance.transform.position = new Vector3(
            prefabInstance.transform.position.x,
            groundY + yOffset,
            prefabInstance.transform.position.z
        );
    }

    void CombineChildrenMeshes(GameObject parent, Material targetMaterial, bool addCollider = false,  bool addTeleportationArea = false)
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

        // Add colliders so that the house is walkable
        MeshCollider mc = parent.AddComponent<MeshCollider>();
        mc.sharedMesh = combinedMesh;

        // Remove the old individual cube objects
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(parent.transform.GetChild(i).gameObject);
    }
    #endregion

    #region Edge Determination Helpers
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

    // Helper to group exterior walls into runs so we know where we can mathematically place windows evenly
    private HashSet<string> PrecalculateWindows(HashSet<Vector2Int> roomTiles, string roomType, int floorLevel)
    {
        HashSet<string> plannedWindows = new HashSet<string>();
        bool isPriorityRoom = roomType == "Bedroom" || roomType == "Master Bedroom" || roomType == "Bathroom" || roomType == "Living Room";

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        string[] dirNames = { "N", "S", "W", "E" };

        for (int i = 0; i < 4; i++)
        {
            Vector2Int dir = dirs[i];
            string dName = dirNames[i];

            // Find all tiles facing outside in this direction
            List<Vector2Int> facingTiles = roomTiles.Where(t => !allHouseOccupiedTiles.Contains(t + dir)).ToList();
            if (facingTiles.Count == 0) continue;

            // Sort them to find continuous runs
            if (dir.y != 0) facingTiles = facingTiles.OrderBy(t => t.x).ToList();
            else facingTiles = facingTiles.OrderBy(t => t.y).ToList();

            List<List<Vector2Int>> runs = new List<List<Vector2Int>>();
            List<Vector2Int> currentRun = new List<Vector2Int> { facingTiles[0] };

            for (int j = 1; j < facingTiles.Count; j++)
            {
                bool isAdjacent = (dir.y != 0) ? facingTiles[j].x == facingTiles[j - 1].x + 1 : facingTiles[j].y == facingTiles[j - 1].y + 1;
                if (isAdjacent) currentRun.Add(facingTiles[j]);
                else
                {
                    runs.Add(currentRun);
                    currentRun = new List<Vector2Int> { facingTiles[j] };
                }
            }
            runs.Add(currentRun);

            // Process each continuous wall segment
            foreach (var run in runs)
            {
                // Strip corners for visual safety (validate the location)
                List<Vector2Int> safeRun = run.Where(t => IsValidWindowLocation(t, dir, roomTiles) && t != mainDoorTile).ToList();
                if (safeRun.Count == 0) continue;

                bool alignedWithFloorBelow = false;

                // Check the alignment with upper floors for multi-story layouts
                if (floorLevel > 0 && isPriorityRoom)
                {
                    foreach (var tile in safeRun)
                    {
                        string checkKey = $"{tile.x},{tile.y}_{dName}";
                        if (floorZeroWindows.Contains(checkKey))
                        {
                            plannedWindows.Add(checkKey);
                            alignedWithFloorBelow = true;
                        }
                    }
                }

                // Balance out the spacing of the windows, if we didn't directly align them with upper floors / on the ground floor
                if (!alignedWithFloorBelow)
                {
                    // Force spawn if priority, otherwise roll random chance for other rooms
                    if (isPriorityRoom || Random.value <= windowChance)
                    {
                        int length = safeRun.Count;
                        if (length == 1)
                        {
                            plannedWindows.Add($"{safeRun[0].x},{safeRun[0].y}_{dName}");
                        }
                        else if (length <= 3)
                        {
                            // Place 1 window perfectly in the center
                            Vector2Int w = safeRun[length / 2];
                            plannedWindows.Add($"{w.x},{w.y}_{dName}");
                        }
                        else
                        {
                            // Place 2 windows balanced along the wall (1/3rd and 2/3rd marks)
                            int index1 = length / 3;
                            int index2 = length - 1 - (length / 3);

                            plannedWindows.Add($"{safeRun[index1].x},{safeRun[index1].y}_{dName}");
                            if (index1 != index2)
                                plannedWindows.Add($"{safeRun[index2].x},{safeRun[index2].y}_{dName}");
                        }
                    }
                }
            }
        }

        // Save Ground Floor windows for the next floor to read
        if (floorLevel == 0)
        {
            foreach (var w in plannedWindows) floorZeroWindows.Add(w);
        }

        return plannedWindows;
    }

    // Specialty function to place windows symmetrically on the main door wall, balanced with upper floors
    private HashSet<string> PrecalculateFrontWallWindows(List<HashSet<Vector2Int>> floorRooms, int floorLevel)
    {
        HashSet<string> planned = new HashSet<string>();
        if (mainDoorTile == Vector2Int.zero) return planned;

        string dir = mainDoorDirection; // "N", "S", "E", or "W"
        bool isHorizontalWall = (dir == "N" || dir == "S");

        // For upper floors, mirror floor 0 positions if the tile is in bounds of a room (no windows on wall segments)
        if (floorLevel > 0 && frontWallFloor0Windows.Count > 0)
        {
            // Build the set of all tiles available on this floor
            HashSet<Vector2Int> allFloorTiles = new HashSet<Vector2Int>();
            foreach (var room in floorRooms) allFloorTiles.UnionWith(room);

            foreach (string key in frontWallFloor0Windows)
            {
                // Key format: "x,y_DIR" — parse the tile coordinate
                string[] parts = key.Split('_');
                string[] coords = parts[0].Split(',');
                Vector2Int tile = new Vector2Int(int.Parse(coords[0]), int.Parse(coords[1]));

                // Only mirror if this tile exists on the current floor and faces outside
                if (allFloorTiles.Contains(tile) && !allHouseOccupiedTiles.Contains(tile + DirectionToVector(dir)))
                    planned.Add(key);
            }
            return planned;
        }

        // On floor 0, compute balanced positions by collecting all exterior-facing tiles on the front wall direction
        List<Vector2Int> frontTiles = new List<Vector2Int>();
        foreach (var room in floorRooms)
        {
            Vector2Int dirVec = DirectionToVector(dir);
            foreach (var tile in room)
            {
                if (!allHouseOccupiedTiles.Contains(tile + dirVec) &&    // faces outside
                    tile != mainDoorTile &&                                // not the door itself
                    IsValidWindowLocation(tile, dirVec, room))             // not a corner
                {
                    frontTiles.Add(tile);
                }
            }
        }

        if (frontTiles.Count == 0) return planned;

        // Sort along the wall axis
        frontTiles = isHorizontalWall
            ? frontTiles.OrderBy(t => t.x).ToList()
            : frontTiles.OrderBy(t => t.y).ToList();

        // Find the door's position index in the wall axis
        int doorAxisVal = isHorizontalWall ? mainDoorTile.x : mainDoorTile.y;

        // Split into left and right of door
        List<Vector2Int> leftSide = frontTiles.Where(t => (isHorizontalWall ? t.x : t.y) < doorAxisVal).ToList();
        List<Vector2Int> rightSide = frontTiles.Where(t => (isHorizontalWall ? t.x : t.y) > doorAxisVal).ToList();

        // Place one window on each side, equidistant from the door.
        // Pick the tile closest to 1/2 of the available run on each side.
        if (leftSide.Count >= 2)
        {
            Vector2Int w = leftSide[leftSide.Count / 2];
            planned.Add($"{w.x},{w.y}_{dir}");
        }
        if (rightSide.Count >= 2)
        {
            Vector2Int w = rightSide[rightSide.Count / 2];
            planned.Add($"{w.x},{w.y}_{dir}");
        }

        // Save for upper floors to mirror
        frontWallFloor0Windows = new HashSet<string>(planned);
        frontWallDirection = dir;

        return planned;
    }

    // Converts a direction string to a Vector2Int
    private Vector2Int DirectionToVector(string dir)
    {
        return dir switch
        {
            "N" => Vector2Int.up,
            "S" => Vector2Int.down,
            "E" => Vector2Int.right,
            "W" => Vector2Int.left,
            _ => Vector2Int.zero
        };
    }

    #endregion

}