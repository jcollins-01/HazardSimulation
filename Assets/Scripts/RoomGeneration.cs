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


    // These vars specifically pass to RoomDecoration without being changed back...I don't know about this...
    //[HideInInspector] public bool dormitory = false;
    //[HideInInspector] public bool warehouse = false;
    [HideInInspector] public bool isSmallHouse = false;
    [HideInInspector] public bool isTwoStoryHouse = false;
    //[HideInInspector] public bool skyscraper = false;
    //[HideInInspector] public bool mansion = false;
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
    public float doorHeight = 2.0f;
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

    // Room variables to track privately
    private int stairDepth = 5; // Makes for an angle of 31 degrees, architectural height for a comfortable set of stairs
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
            numberOfRooms = 4;
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
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 4;
            generateHallways = true;
            hallwayChance = 5;
            hallwayWidth = 2; 

            smallHouse = false;
            isSmallHouse = true;
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
            wallHeight = 3;
            minComplexity = 1;
            maxComplexity = 4;
            generateHallways = true;
            hallwayChance = 60; // Average chance for the floor to have a hallway or not
            hallwayWidth = 2;

            twoStoryHouse = false;
            isTwoStoryHouse = true;
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

        // Ensure the RoomGeneration object is set to the original transform values (in case it was accidentally moved)
        RestoreGeneratorTransform();

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

            // Generate doorways for this specific floor layout (now that we have the full layout)
            HashSet<string> floorDoors = GenerateDoorsForFloor(floorRooms);

            // Spawn a ramp to connect floors if this is the stairwell (and not the top floor)
            if (floor < numberOfFloors - 1) 
                SpawnStairs(stairwellTile, roofHeight, floorParent.transform, stairDepth);

            // Determine the main door tile in the layout
            DetermineMainDoor(floorRooms);

            // Build at the current roofHeight
            for (int i = 0; i < floorRooms.Count; i++)
            {
                GameObject builtRoom = BuildRoomGeometry(i, floor, floorRooms[i], Vector2Int.zero, roofHeight, floorParent.transform, stairwellTile, floorDoors);

                // Add the room to our list of generated rooms so we can pass it to the decorator later
                allGeneratedRooms.Add(new RoomData { Tiles = floorRooms[i], RoomObject = builtRoom });
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
        BuildRoomGeometry(999, 0, outbuildingTiles, Vector2Int.zero, 0f, outbuildingParent.transform, new Vector2Int(-999, -999), noDoors);

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

        // LINGERING ISSUE: Yard teleport area child is still being spawned HUGE for some reason, though position is fine - need to work on matching it to the yard size
        // Load in new prefab variant - variant is set with the same settings as the working Teleport Areas, so no need to manually set below
        GameObject teleportPrefab = Resources.Load<GameObject>("Locomotion/Teleport Area Invisible");
        if (teleportPrefab != null)
        {
            GameObject teleportInstance = Instantiate(teleportPrefab, yard.transform);
            teleportInstance.name = "Teleport Area Invisible";

            // By making the prefab a child and setting its local scale to Vector3.one, it should inherit the exact size and position of the Yard
            teleportInstance.transform.localPosition = Vector3.zero;
            teleportInstance.transform.localRotation = Quaternion.identity;
            teleportInstance.transform.localScale = Vector3.one;
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

        // Decide split direction. We generally want to split the longest axis to avoid thin hallways.
        bool splitVertical = width > length;

        // Add a bit of randomness so it isn't completely predictable, provided both sides are big enough
        if (width >= currentMinWidth * 2 && length >= currentMinLength * 2)
        {
            splitVertical = Random.value > 0.5f;
        }

        // Perform the slice
        if (splitVertical)
        {
            // Calculate valid range for the slice to ensure minRoomWidth is respected on both sides
            int minSplit = minX + currentMinWidth;
            int maxSplit = maxX - currentMinWidth + 1;

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
            int minSplit = minY + currentMinLength;
            int maxSplit = maxY - currentMinLength + 1;

            if (minSplit > maxSplit) return false;

            int splitLine = Random.Range(minSplit, maxSplit);

            foreach (var tile in currentRoom)
            {
                if (tile.y < splitLine) roomA.Add(tile);
                else roomB.Add(tile);
            }
        }

        // Because the house layout is irregular, a straight slice might occasionally catch an empty corner and make an empty room
        // If that happens, reject the split
        if (roomA.Count == 0 || roomB.Count == 0) return false;

        // Apply strict validation to both newly generated spaces to prevent void spaces (will return immediately if we are allowing void spaces)
        if (!IsRoomViable(roomA) || !IsRoomViable(roomB)) return false;

        return true;
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
    #endregion

    #region Core Building Callers
    GameObject BuildRoomGeometry(int id, int floor, HashSet<Vector2Int> normalizedCoords, Vector2Int worldPos, float heightOffset, Transform parentFloor, Vector2Int stairTile, HashSet<string> floorDoors)
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
            CheckAndSpawnWalls(coord, normalizedCoords, wallGroup.transform, tilePos, floorDoors, stairTile, stairDepth, floor);

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

        return roomParent;
    }

    // Placing walls on the floors of generated rooms
    void CheckAndSpawnWalls(Vector2Int localCoord, HashSet<Vector2Int> roomTiles, Transform parent, Vector3 pos, HashSet<string> floorDoors, Vector2Int stairTile, int stairDepth, int floorLevel)
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
            }

            // Is the neighbor still inside the house, but in a different room?
            if (allHouseOccupiedTiles.Contains(neighbor))
            {
                // Spawn an interior wall to divide the rooms.
                SpawnWall(pos, dir, parent, isInterior: true);
                continue;
            }

            // Check if THIS specific wall segment is the designated Main Door
            bool isMainDoor = (floorLevel == 0 && localCoord == mainDoorTile && currentDir == mainDoorDirection);

            // Check if this is a safe, centered spot for a window
            bool safeFromCorners = IsValidWindowLocation(localCoord, dir, roomTiles); 

            // It can be a window if it's NOT a door and NOT up against an interior wall/exterior corner
            bool isWindow = !isMainDoor && (Random.value <= windowChance) && safeFromCorners; 

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
                }

                continue; // Prevent standard full wall from spawning
            }
            else if (isWindow)
            {
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
            }
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
        // Figure out the vertical bounds of the ramp
        // The floor we are standing on has a top surface at heightOffset + 0.5 - the next floor up is wallHeight + 1 unit
        float surfaceBottom = heightOffset - 1.5f; // + 0.5f;
        float surfaceTop = heightOffset + wallHeight - 1f; // + 1 unit for the floor thickness
        float rise = surfaceTop - surfaceBottom;

        // Figure out the horizontal bounds of the ramp
        // The hole ends at topTile.y - the ramp starts 'depth' tiles back. To center it perfectly, we find the middle of the 'run'.
        float run = (float)depth;
        float centerZ = (float)topTile.y - (run - 1f); // run - 1f- was run/2f, was + 0.5f

        // Geometry to determine the angle of the ramp
        float rampLength = Mathf.Sqrt((run * run) + (rise * rise));
        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

        // Alignment tweak - helps us to make the ramp flush with the upper floor
        // Lower the center slightly so the top surface of the ramp is what aligns with the floor, not the core center
        float thickness = 0.2f;
        float centerY = ((surfaceBottom + surfaceTop) / 2f) - ((thickness / 2f) * Mathf.Cos(angle * Mathf.Deg2Rad));

        float centerX = topTile.x; // was topFile.x - 1f;

        // Spawn the ramp - X must be exactly topTile.x to align with the hole
        Vector3 rampPos = new Vector3(centerX, centerY, centerZ);
        GameObject ramp = SpawnPrimitive(PrimitiveType.Cube, parent, rampPos, new Vector3(0.95f, thickness, rampLength), "Stair_Ramp");

        // Rotation - apply the angle rotation to the ramp's position so it connects the floors
        ramp.transform.localRotation = Quaternion.Euler(-angle, 0, 0);

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
    #endregion

    #region House Landmark Determination Helpers
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

    // Check if a tile is part of the stairwell layout
    bool IsInStairwell(Vector2Int coord, Vector2Int topTile, int depth)
    {
        // If X doesn't match, it's not the stairwell
        if (coord.x != topTile.x) return false;

        // The hole starts at the topTile and goes BACKWARDS for 'depth' tiles
        // Example: Top is 10, Depth is 4. Hole is 10, 9, 8, 7.
        return (coord.y <= topTile.y && coord.y > topTile.y - depth);
    }

    void DetermineMainDoor(List<HashSet<Vector2Int>> groundFloorRooms)
    {
        List<(Vector2Int tile, string dir)> validExteriorWalls = new List<(Vector2Int, string)>();

        foreach (var room in groundFloorRooms)
        {
            foreach (var tile in room)
            {
                // Check all 4 directions. If a neighbor is NOT in allHouseOccupiedTiles, it is an exterior wall and a candidate for the main door
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.up))
                    validExteriorWalls.Add((tile, "N"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.down))
                    validExteriorWalls.Add((tile, "S"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.right))
                    validExteriorWalls.Add((tile, "E"));
                if (!allHouseOccupiedTiles.Contains(tile + Vector2Int.left))
                    validExteriorWalls.Add((tile, "W"));
            }
        }

        if (validExteriorWalls.Count > 0)
        {
            // Pick a random exterior wall segment to be the door
            var chosen = validExteriorWalls[Random.Range(0, validExteriorWalls.Count)];
            mainDoorTile = chosen.tile;
            mainDoorDirection = chosen.dir;
            //Debug.Log("Chose a door");
        }
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

    #region Prefab + Mesh Helpers
    // A helper to look at where a prefabs pivot point is so we can better spawn it in place, not a little too low (use the bottom of the object for location)
    float CalculateVerticalOffset(GameObject instance)
    {
        // Get the local bottom of the mesh (distance from pivot to bottom)
        float meshBottomY = instance.GetComponent<Renderer>().bounds.min.y;
        // Get the pivot point at which the prefab is spawned/handled from
        float pivotY = instance.transform.position.y;

        // Return the distance from the pivot to the bottom with some funky math to make it spawn in just the right place
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

        // LINGERING ISSUE: floor teleport area variant is not spawning in for some reason
        // Load the new teleport prefab variant
        if (addTeleportationArea)
        {
            //Debug.Log("Trying to add Floors teleport area");
            GameObject teleportPrefab = Resources.Load<GameObject>("Locomotion/Teleport Area Invisible");
            if (teleportPrefab != null)
            {
                GameObject teleportInstance = Instantiate(teleportPrefab, parent.transform);
                teleportInstance.name = "Teleport Area Invisible";

                // Keep the prefab's scale and position at 0/1 so it perfectly overlays the parent
                teleportInstance.transform.localPosition = Vector3.zero;
                teleportInstance.transform.localRotation = Quaternion.identity;
                teleportInstance.transform.localScale = Vector3.one;

                // Destroy any default colliders on the prefab so they don't create phantom boundaries
                Collider[] defaultColliders = teleportInstance.GetComponents<Collider>();
                foreach (Collider col in defaultColliders)
                    DestroyImmediate(col);

                // Link the XR Teleportation Area to the custom Floor MeshCollider
                var teleportationArea = teleportInstance.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
                if (teleportationArea != null)
                {
                    teleportationArea.colliders.Clear();
                    teleportationArea.colliders.Add(mc); // Use the matching MeshCollider shape we made above

                    // Ensure it's on the right interaction layer
                    teleportationArea.interactionLayers = UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask.GetMask("Teleport");
                }
            }
            else
            {
                Debug.LogWarning("Prefab not found at Resources/Locomotion/Teleport Area Invisible.prefab");
            }
        }

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
    #endregion

}