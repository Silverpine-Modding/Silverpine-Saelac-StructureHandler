using System.IO;
using UnityEngine;

namespace StructureHandler;

// The native railing is decorative and has no serializable component. This
// marker lets imported copies participate in native save/load and tile unloads.
internal sealed class StructureRailingState : MonoBehaviour, ISerializableMonoBehavior
{
    public void Serialize(BinaryWriter writer) => writer.Write(1);

    public void Deserialize(BinaryReader reader)
    {
        if (reader.ReadInt32() != 1)
            throw new InvalidDataException("Unsupported Structure Handler railing state.");
    }
}
