using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HouseLayoutData : MonoBehaviour
{
    [Header("Layout Information")]
    public string layoutName = "Custom House Layout";
    public int seed;

    [System.Serializable] // makes it so we can adjust/save these in the Inspector
    public struct HouseSettings
    {
        // Should contain every variable that we are able to adjust during RoomGeneration...
        public int maxHouseWidth;
        public int maxHouseLength;
        public int minRoomWidth;
        public int maxRoomWidth;
        public int minRoomLength;
        public int maxRoomLength;
        public int wallHeight;
        public int numberOfRooms;
        public int numberOfFloors;
        public bool identicalFloors;
        public bool roomAmountsDifferPerFloor;
        public bool heightenedNooks;
        public bool generateHallways;
        public bool spineHallways;
        public float hallwayChance;
        public int hallwayWidth;
        public int minComplexity;
        public int maxComplexity;
        public bool allowVoidSpaces;
        public int minViableWidth;
        public int minViableLength;
        public float windowChance;
        public float doorHeight;
        public float windowHeight;
        public float windowWidth;
        public int windowSpacing;
        public bool generateYard;
        public int minYardPadding;
        public int maxYardPadding;
        public bool chanceForOutbuilding;
        public float outbuildingSpawnChance;
        public int outbuildingOffset;
        public Material floorMaterial;
        public Material wallMaterial;
        public Material ceilingMaterial;
        public Material yardMaterial;
    }

    [HideInInspector]
    public HouseSettings settings; // holds them all

    // Allows us to grab the settings of a particular layout that's been passed along to us, given its gen info and seed
    public void CaptureSettings(RoomGeneration gen, int layoutSeed, string defaultName)
    {
        seed = layoutSeed;
        if (string.IsNullOrEmpty(layoutName) || layoutName == "Custom House Layout")
            layoutName = defaultName;

        settings = new HouseSettings
        {
            maxHouseWidth = gen.maxHouseWidth,
            maxHouseLength = gen.maxHouseLength,
            minRoomWidth = gen.minRoomWidth,
            maxRoomWidth = gen.maxRoomWidth,
            minRoomLength = gen.minRoomLength,
            maxRoomLength = gen.maxRoomLength,
            wallHeight = gen.wallHeight,
            numberOfRooms = gen.numberOfRooms,
            numberOfFloors = gen.numberOfFloors,
            identicalFloors = gen.identicalFloors,
            roomAmountsDifferPerFloor = gen.roomAmountsDifferPerFloor,
            heightenedNooks = gen.heightenedNooks,
            generateHallways = gen.generateHallways,
            spineHallways = gen.spineHallways,
            hallwayChance = gen.hallwayChance,
            hallwayWidth = gen.hallwayWidth,
            minComplexity = gen.minComplexity,
            maxComplexity = gen.maxComplexity,
            allowVoidSpaces = gen.allowVoidSpaces,
            minViableWidth = gen.minViableWidth,
            minViableLength = gen.minViableLength,
            windowChance = gen.windowChance,
            doorHeight = gen.doorHeight,
            windowHeight = gen.windowHeight,
            windowWidth = gen.windowWidth,
            windowSpacing = gen.windowSpacing,
            generateYard = gen.generateYard,
            minYardPadding = gen.minYardPadding,
            maxYardPadding = gen.maxYardPadding,
            chanceForOutbuilding = gen.chanceForOutbuilding,
            outbuildingSpawnChance = gen.outbuildingSpawnChance,
            outbuildingOffset = gen.outbuildingOffset,
            floorMaterial = gen.floorMaterial,
            wallMaterial = gen.wallMaterial,
            ceilingMaterial = gen.ceilingMaterial,
            yardMaterial = gen.yardMaterial
        };
    }

    // Passes all the necessary numbers and logic to make a particular house layout into the generation object
    public void ApplySettings(RoomGeneration gen)
    {
        gen.maxHouseWidth = settings.maxHouseWidth;
        gen.maxHouseLength = settings.maxHouseLength;
        gen.minRoomWidth = settings.minRoomWidth;
        gen.maxRoomWidth = settings.maxRoomWidth;
        gen.minRoomLength = settings.minRoomLength;
        gen.maxRoomLength = settings.maxRoomLength;
        gen.wallHeight = settings.wallHeight;
        gen.numberOfRooms = settings.numberOfRooms;
        gen.numberOfFloors = settings.numberOfFloors;
        gen.identicalFloors = settings.identicalFloors;
        gen.roomAmountsDifferPerFloor = settings.roomAmountsDifferPerFloor;
        gen.heightenedNooks = settings.heightenedNooks;
        gen.generateHallways = settings.generateHallways;
        gen.spineHallways = settings.spineHallways;
        gen.hallwayChance = settings.hallwayChance;
        gen.hallwayWidth = settings.hallwayWidth;
        gen.minComplexity = settings.minComplexity;
        gen.maxComplexity = settings.maxComplexity;
        gen.allowVoidSpaces = settings.allowVoidSpaces;
        gen.minViableWidth = settings.minViableWidth;
        gen.minViableLength = settings.minViableLength;
        gen.windowChance = settings.windowChance;
        gen.doorHeight = settings.doorHeight;
        gen.windowHeight = settings.windowHeight;
        gen.windowWidth = settings.windowWidth;
        gen.windowSpacing = settings.windowSpacing;
        gen.generateYard = settings.generateYard;
        gen.minYardPadding = settings.minYardPadding;
        gen.maxYardPadding = settings.maxYardPadding;
        gen.chanceForOutbuilding = settings.chanceForOutbuilding;
        gen.outbuildingSpawnChance = settings.outbuildingSpawnChance;
        gen.outbuildingOffset = settings.outbuildingOffset;
        gen.floorMaterial = settings.floorMaterial;
        gen.wallMaterial = settings.wallMaterial;
        gen.ceilingMaterial = settings.ceilingMaterial;
        gen.yardMaterial = settings.yardMaterial;
    }
}

public class HouseLayoutManager : MonoBehaviour
{
    [Header("Generator Reference")]
    public RoomGeneration roomGenerator; // assign this in inspector

    [Header("Layout Collection")]
    [Tooltip("Drag and drop generated House GameObjects into this list.")]
    public List<GameObject> houseLayouts = new List<GameObject>();

    [Header("Layout Selection")]
    [Tooltip("Index of the layout in houseLayouts list to regenerate.")]
    public int selectedLayoutIndex = 0;

    public void RegenerateLayout()
    {
        // Check for generation-breaking issues that would prevent us from moving forward
        if (roomGenerator == null)
        {
            Debug.LogError("[HouseLayoutManager] RoomGenerator reference is missing!");
            return;
        }

        if (houseLayouts == null || houseLayouts.Count == 0)
        {
            Debug.LogWarning("[HouseLayoutManager] houseLayouts list is empty!");
            return;
        }

        if (selectedLayoutIndex < 0 || selectedLayoutIndex >= houseLayouts.Count)
        {
            Debug.LogError($"[HouseLayoutManager] Selected index {selectedLayoutIndex} is out of bounds (0 - {houseLayouts.Count - 1}).");
            return;
        }

        GameObject selectedHouse = houseLayouts[selectedLayoutIndex];
        if (selectedHouse == null)
        {
            Debug.LogError($"[HouseLayoutManager] Selected GameObject at index {selectedLayoutIndex} is null or destroyed!");
            return;
        }

        HouseLayoutData data = selectedHouse.GetComponent<HouseLayoutData>();
        if (data == null)
        {
            Debug.LogError($"[HouseLayoutManager] '{selectedHouse.name}' missing HouseLayoutData component.");
            return;
        }

        // If we don't have ANY of the above issues, we should be set to generate
        // Restore layout settings to generator
        data.ApplySettings(roomGenerator);

        // Lock seed and turn off random seeding - we're swapping in the specific seed we want instead!
        roomGenerator.useRandomSeed = false;
        roomGenerator.currentSeed = data.seed;

        // Regenerate the layout exactly
        roomGenerator.GenerateAllRooms();

        Debug.Log($"[HouseLayoutManager] Successfully regenerated layout '{data.layoutName}' using Seed: {data.seed}.");
    }
}



// For handling all the button calls so that we can regenerate the layout easily in inspector!
[CustomEditor(typeof(HouseLayoutManager))]
public class HouseLayoutManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        HouseLayoutManager manager = (HouseLayoutManager)target;

        EditorGUILayout.Space(15);
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);

        if (GUILayout.Button("Regenerate Layout", GUILayout.Height(35)))
        {
            manager.RegenerateLayout();
        }

        GUI.backgroundColor = Color.white;
    }
}