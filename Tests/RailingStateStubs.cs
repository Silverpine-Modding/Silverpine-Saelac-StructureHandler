// Minimal types for exercising the production binary marker without a Unity player.
namespace UnityEngine { public class MonoBehaviour { } }
public interface ISerializableMonoBehavior
{
    void Serialize(System.IO.BinaryWriter writer);
    void Deserialize(System.IO.BinaryReader reader);
}
