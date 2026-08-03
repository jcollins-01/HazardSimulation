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
    [System.Serializable]
    public class SavedLayout
    {
        public string layoutName = "Custom House Layout";
        public int seed;
        public HouseLayoutData.HouseSettings settings;
    }

    [Header("Generator Reference")]
    public RoomGeneration roomGenerator; // assign this in inspector

    [Header("Save Target Layout")]
    [Tooltip("Drag a generated House GameObject here, then click 'Save Layout' to retain its data.")]
    public GameObject houseToSave;

    [Header("Saved Layout Storage")]
    [Tooltip("Options of previously saved house layouts which can be regenerated.")]
    public List<SavedLayout> savedLayouts = new List<SavedLayout>();

    [Header("Layout Selection")]
    [Tooltip("Index of the layout in savedLayouts list to regenerate.")]
    public int selectedLayoutIndex = 0;

    // Check for a current house to save and save it
    public void SaveCurrentHouse()
    {
        // Make sure no critical issues with the house
        if (houseToSave == null)
        {
            Debug.LogWarning("[HouseLayoutManager] No House GameObject assigned in 'houseToSave' to extract data from!");
            return;
        }

        HouseLayoutData data = houseToSave.GetComponent<HouseLayoutData>();
        if (data == null)
        {
            Debug.LogError($"[HouseLayoutManager] '{houseToSave.name}' does not have a HouseLayoutData component attached!");
            return;
        }

        // Save the new layout (settings, seed, name)
        SavedLayout newEntry = new SavedLayout
        {
            layoutName = string.IsNullOrEmpty(data.layoutName) ? houseToSave.name : data.layoutName,
            seed = data.seed,
            settings = data.settings
        };

        savedLayouts.Add(newEntry);
        Debug.Log($"[HouseLayoutManager] Successfully saved layout '{newEntry.layoutName}' (Seed: {newEntry.seed}). Total saved: {savedLayouts.Count}");

        // Clear slot so it's ready for the next drag-and-drop
        houseToSave = null;
    }

    public void RegenerateLayout()
    {
        // Check for generation-breaking issues that would prevent us from moving forward
        if (roomGenerator == null)
        {
            Debug.LogError("[HouseLayoutManager] RoomGenerator reference is missing!");
            return;
        }

        if (savedLayouts == null || savedLayouts.Count == 0)
        {
            Debug.LogWarning("[HouseLayoutManager] savedLayouts list is empty! Save a layout first.");
            return;
        }

        if (selectedLayoutIndex < 0 || selectedLayoutIndex >= savedLayouts.Count)
        {
            Debug.LogError($"[HouseLayoutManager] Selected index {selectedLayoutIndex} is out of bounds (0 to {savedLayouts.Count - 1}).");
            return;
        }

        // If we don't have ANY of the issues above, we should be set to regenerate
        SavedLayout targetLayout = savedLayouts[selectedLayoutIndex];

        // Apply stored settings back into the RoomGenerator
        ApplySettingsToGenerator(targetLayout.settings, roomGenerator);

        // Lock seed and disable random seed mode - we're passing it our specific seed
        roomGenerator.useRandomSeed = false;
        roomGenerator.currentSeed = targetLayout.seed;

        // Generate the exact same layout again
        roomGenerator.GenerateAllRooms();

        Debug.Log($"[HouseLayoutManager] Regenerated layout '{targetLayout.layoutName}' using Seed: {targetLayout.seed}.");
    }

    // Passes all the necessary numbers and logic to make a particular house layout into the generation object
    public void ApplySettingsToGenerator(HouseLayoutData.HouseSettings settings, RoomGeneration gen)
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

// For handling all the button calls so that we can regenerate the layout easily in inspector!
[CustomEditor(typeof(HouseLayoutManager))]
public class HouseLayoutManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        HouseLayoutManager manager = (HouseLayoutManager)target;
        EditorGUILayout.Space(15);

        // Save Layout button
        GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
        if (GUILayout.Button("Save Layout Data from Object", GUILayout.Height(30)))
        {
            Undo.RecordObject(manager, "Save House Layout Data");
            manager.SaveCurrentHouse();
            EditorUtility.SetDirty(manager);
        }

        EditorGUILayout.Space(5);

        // Regenerate Layout button
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button("Regenerate Selected Layout", GUILayout.Height(35)))
        {
            manager.RegenerateLayout();
        }

        GUI.backgroundColor = Color.white;
    }
}