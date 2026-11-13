#nullable enable
using System;
using System.Collections.Generic;

namespace StructureHandler;

// Unity previews must run on the main thread. Budget between jobs, never move
// Unity work onto a background thread or repeat it for each IMGUI event.
internal sealed class EditorWorkQueue<T> where T : notnull
{
    private readonly Queue<T> queue = new();
    private readonly HashSet<T> pending = new();
    internal int Count => queue.Count;
    internal void Enqueue(T item) { if (pending.Add(item)) queue.Enqueue(item); }
    internal void Clear() { queue.Clear(); pending.Clear(); }
    internal void Retain(HashSet<T> live)
    {
        int count = queue.Count;
        while (count-- > 0)
        {
            T item = queue.Dequeue();
            if (live.Contains(item)) queue.Enqueue(item);
            else pending.Remove(item);
        }
    }
    internal int Process(Action<T> action, Func<double> milliseconds, double budget, int maximum)
    {
        double start = milliseconds();
        int count = 0;
        while (queue.Count > 0 && count < maximum && (count == 0 || milliseconds() - start < budget))
        {
            T item = queue.Dequeue();
            pending.Remove(item);
            action(item);
            count++;
        }
        return count;
    }
}
