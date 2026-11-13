#nullable enable
using System;

namespace StructureHandler;

// Painter's order for the game's orthographic camera looking from negative Z.
// Layer VALUE (not its arbitrary ID), order, queue, then camera depth.
internal readonly struct PreviewSortKey : IComparable<PreviewSortKey>
{
    internal readonly int Layer, Order, Queue, Sequence;
    internal readonly float Z;
    internal PreviewSortKey(int layer, int order, int queue, float z, int sequence)
    { Layer = layer; Order = order; Queue = queue; Z = z; Sequence = sequence; }
    public int CompareTo(PreviewSortKey other)
    {
        int result = Layer.CompareTo(other.Layer);
        if (result == 0) result = Order.CompareTo(other.Order);
        if (result == 0) result = Queue.CompareTo(other.Queue);
        if (result == 0) result = other.Z.CompareTo(Z);
        return result != 0 ? result : Sequence.CompareTo(other.Sequence);
    }
    internal static int YOrder(float y, int offset, int rotationOffset) =>
        (int)Math.Round(-y * 10f, MidpointRounding.ToEven) + offset + rotationOffset;

    internal static int RotationOffset(string name, int rotation)
    {
        name = name.ToLowerInvariant();
        if (!name.Contains("chair") && !name.Contains("bench")) return 0;
        return rotation == (name.Contains("fancy") || name.Contains("bench") ? 2 : 1) ? 4 : 0;
    }
}
