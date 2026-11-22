#nullable enable

using System;
using System.Collections.Generic;

namespace StructureHandler;

[Serializable]
internal sealed class PendingTileImport
{
    public int x;
    public int y;
    public StructureFile structure = new();
    // Display names are not operation IDs: different files can share one.
    public string importId = "";
    // Only for old, ambiguous boundary fragments. Each record is stored once.
    public List<StructureFile>? boundaryTail;
}

[Serializable]
internal sealed class StructureWorldState
{
    public List<StructurePosition> protectedTiles = new();
    public List<StructurePosition> buildableTiles = new();
    public List<RegeneratingResourceMarker> resources = new();
    public List<PendingTileImport> pending = new();
    public List<StructureAppearance> appearances = new();
    public List<StructureObject> supplementalObjects = new();
    public List<StructureObject> clearedScenery = new();
    public List<StructurePosition> importedCells = new();
    public SharedMarketState? sharedMarket;
}

[Serializable]
internal sealed class SharedMarketState
{
    public int lastDay = -1;
    public int lastHour = -1;
    public List<SharedMarketPrice> prices = new();
}

[Serializable]
internal sealed class SharedMarketPrice
{
    public string itemName = "";
    public float price;
    public float minPrice;
    public float maxPrice;
    public float maxChange;
}

[Serializable]
internal sealed class StructureAppearance
{
    public string prefabName = "";
    public float x;
    public float y;
    public float z;
    public int spriteVariantIndex = -1;
    public bool lockSpriteVariant;
    public bool extender;
}

[Serializable]
internal sealed class StructureFile
{
    public int formatVersion = 3;
    public string name = "";
    public string gameVersion = "";
    public string exportedUtc = "";
    public List<StructurePosition> occupiedPositions = new();
    public List<SupportingTerrain> supportingTerrain = new();
    public List<StructureObject> objects = new();
    // Editor-only annotations, never occupied terrain or importable objects.
    public List<PlanningTile> planningTiles = new();
    public string serializedObjectsBase64 = "";

    // Saving is synchronous. Share the unmodified object/terrain lists, but
    // keep derived metadata out of the editable undo/redo history.
    internal StructureFile WithSaveHeader(string fileName, List<StructurePosition> occupied)
    {
        var copy = (StructureFile)MemberwiseClone();
        copy.name = fileName;
        copy.occupiedPositions = occupied;
        copy.serializedObjectsBase64 = "";
        return copy;
    }
}

[Serializable]
internal sealed class PlanningTile
{
    public int x;
    public int y;
    public string color = "#FFD54F";
}

[Serializable]
internal sealed class StructurePosition
{
    public int x;
    public int y;
}

[Serializable]
internal sealed class SupportingTerrain
{
    // Placement proposals edit scalars only. Never mutate the shared component
    // payload; keeping it avoids deserializing inventories while painting.
    internal SupportingTerrain PlacementDraft() => (SupportingTerrain)MemberwiseClone();

    public string prefabName = "";
    public int x;
    public int y;
    public float z;
    public int spriteVariantIndex = -1;
    public bool lockSpriteVariant;
    public bool hasMapZoneName;
    public string mapZoneName = "";
    public List<StructureComponent> components = new();
}

[Serializable]
internal sealed class StructureObject
{
    // Replace the footprint list on rotation; never edit the shared list in place.
    internal StructureObject PlacementDraft() => (StructureObject)MemberwiseClone();

    public string prefabName = "";
    public float x;
    public float y;
    public float z;
    public bool npcInteractionRangeExtender;
    public int turnableIndex = -1;
    public int spriteVariantIndex = -1;
    public bool lockSpriteVariant;
    public bool hasEditableSignMessage;
    public string signMessage = "";
    public bool hasMapZoneName;
    public string mapZoneName = "";
    public List<StructurePosition> occupiedOffsets = new();
    public List<StructureComponent> components = new();
}

[Serializable]
internal sealed class StructureComponent
{
    public string type = "";
    public string hierarchyPath = "";
    public int componentIndex = -1;
    public string dataBase64 = "";
}

[Serializable]
internal sealed class RegeneratingResourceFile
{
    public int formatVersion = 1;
    public List<RegeneratingResourceMarker> resources = new();
}

[Serializable]
internal sealed class RegeneratingResourceMarker
{
    public int lastCheckedDay = -1;
    public string prefabName = "";
    public float x;
    public float y;
    public float z;
}
