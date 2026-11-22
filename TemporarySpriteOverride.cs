#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

// Keep the most recent underlying native sprite. Releasing our paint may
// not undo a later native/other-mod change or touch a destroyed renderer.
internal sealed class TemporarySpriteOverride<TTarget, TSprite> where TTarget : class
{
    private TTarget? target;
    private TSprite original = default!, applied = default!;
    private readonly Func<TTarget, bool> alive;
    private readonly Func<TTarget, TSprite> read;
    private readonly Action<TTarget, TSprite> write;
    internal TemporarySpriteOverride(Func<TTarget,bool> alive, Func<TTarget,TSprite> read, Action<TTarget,TSprite> write)
    { this.alive = alive; this.read = read; this.write = write; }
    internal void Paint(TTarget next, TSprite value)
    {
        if (!ReferenceEquals(target, next)) Release();
        var current = read(next);
        if (target == null || !EqualityComparer<TSprite>.Default.Equals(current, applied)) original = current;
        target = next;
        applied = value;
        write(next, value);
    }
    internal void Release()
    {
        if (target != null && alive(target) && EqualityComparer<TSprite>.Default.Equals(read(target), applied))
            write(target, original);
        target = null; original = applied = default!;
    }
}
