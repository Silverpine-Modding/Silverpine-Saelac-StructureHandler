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
