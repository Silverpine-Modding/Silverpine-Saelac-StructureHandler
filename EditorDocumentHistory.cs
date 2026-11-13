#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;

namespace StructureHandler;

// Field metadata is cached once, including future additive model fields. These
// DTOs contain only scalar values, immutable strings and lists of other DTOs.
// No serializers, Unity calls or item/component deserializers run for undo.
internal static class EditorDocumentCopy
{
    private sealed class Operations
    {
        internal Func<object, object> Clone = null!;
        internal Func<object, object, bool> Equal = null!;
        internal Func<object, long> Size = null!;
    }
    private static readonly Dictionary<Type, Operations> Cached = new();
    private static readonly Dictionary<Type, Func<int, System.Collections.IList>> ListFactories = new();
    private static System.Collections.IList NewList(Type type, int capacity)
    {
        if (!ListFactories.TryGetValue(type, out var factory))
        {
            var count = Expression.Parameter(typeof(int), "capacity");
            factory = Expression.Lambda<Func<int, System.Collections.IList>>(
                Expression.Convert(Expression.New(type.GetConstructor(new[] { typeof(int) })!, count), typeof(System.Collections.IList)), count).Compile();
            ListFactories[type] = factory;
        }
        return factory(capacity);
    }
    private static Operations For(Type type)
    {
        if (Cached.TryGetValue(type, out var cached)) return cached;
        // Compile field access once, not reflection GetValue/SetValue (and boxed
        // floats/ints) for every field of every tile on every edit. New public DTO
        // fields automatically participate in copying, equality and the budget.
        var input = Expression.Parameter(typeof(object), "input");
        var other = Expression.Parameter(typeof(object), "other");
        var output = Expression.Variable(type, "output");
        var copy = new List<Expression> { Expression.Assign(output, Expression.New(type)) };
        Expression equal = Expression.Constant(true);
        Expression size = Expression.Constant(32L);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = Expression.Field(Expression.Convert(input, type), field);
            var right = Expression.Field(Expression.Convert(other, type), field);
            bool scalar = field.FieldType.IsValueType || field.FieldType == typeof(string);
            copy.Add(Expression.Assign(Expression.Field(output, field), scalar ? value :
                Expression.Convert(Expression.Call(typeof(EditorDocumentCopy), nameof(Copy), null,
                    Expression.Convert(value, typeof(object))), field.FieldType)));
            Expression same;
            if (scalar)
            {
                var comparerType = typeof(EqualityComparer<>).MakeGenericType(field.FieldType);
                var comparer = Expression.Property(null, comparerType, "Default");
                same = Expression.Call(comparer, comparerType.GetMethod("Equals", new[] { field.FieldType, field.FieldType })!, value, right);
            }
            else same = Expression.Call(typeof(EditorDocumentCopy), nameof(Equal), null,
                Expression.Convert(value, typeof(object)), Expression.Convert(right, typeof(object)));
            equal = Expression.AndAlso(equal, same);
            size = Expression.Add(size, field.FieldType.IsValueType ? Expression.Constant(16L) :
                Expression.Call(typeof(EditorDocumentCopy), nameof(Size), null, Expression.Convert(value, typeof(object))));
        }
        copy.Add(Expression.Convert(output, typeof(object)));
        cached = new Operations
        {
            Clone = Expression.Lambda<Func<object, object>>(Expression.Block(new[] { output }, copy), input).Compile(),
            Equal = Expression.Lambda<Func<object, object, bool>>(equal, input, other).Compile(),
            Size = Expression.Lambda<Func<object, long>>(size, input).Compile()
        };
        Cached[type] = cached;
        return cached;
    }
    internal static StructureFile Clone(StructureFile file) => (StructureFile)Copy(file)!;
    private static object? Copy(object? value)
    {
        if (value == null || value is string || value.GetType().IsValueType) return value;
        if (value is System.Collections.IList list)
        {
            var clone = NewList(value.GetType(), list.Count);
            foreach (var item in list) clone.Add(Copy(item));
            return clone;
        }
        return For(value.GetType()).Clone(value);
    }
    internal static bool Equal(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.GetType() != right.GetType()) return false;
        if (left is string || left.GetType().IsValueType) return left.Equals(right);
        if (left is System.Collections.IList a && right is System.Collections.IList b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Equal(a[i], b[i])) return false;
            return true;
        }
        return For(left.GetType()).Equal(left, right);
    }
    // Conservative retained-memory bound; shared immutable strings are counted
    // in each snapshot, so sharing cannot make the cap accidentally too lenient.
    internal static long Size(object? value)
    {
        if (value == null) return 0;
        if (value is string text) return 24 + text.Length * 2L;
        if (value.GetType().IsValueType) return 16;
        long size = 32;
        if (value is System.Collections.IList list)
        { foreach (var item in list) size += 8 + Size(item); }
        else return For(value.GetType()).Size(value);
        return size;
    }
}

internal sealed class EditorDocumentHistory
{
    private readonly List<(StructureFile File, long Bytes)> entries = new();
    private int cursor = -1;
    private long bytes;
    internal StructureFile? Current => cursor < 0 ? null : entries[cursor].File;
    internal bool CanUndo => cursor > 0;
    internal bool CanRedo => cursor >= 0 && cursor + 1 < entries.Count;
    internal void Reset(StructureFile? file)
    { entries.Clear(); cursor = -1; bytes = 0; if (file != null) Record(file); }
    internal void Record(StructureFile file)
    {
        if (EditorDocumentCopy.Equal(file, Current)) return;
        for (int i = entries.Count - 1; i > cursor; i--) { bytes -= entries[i].Bytes; entries.RemoveAt(i); }
        var copy = EditorDocumentCopy.Clone(file);
        long size = EditorDocumentCopy.Size(copy);
        entries.Add((copy, size)); bytes += size; cursor = entries.Count - 1;
        while (entries.Count > 1 && (entries.Count > 32 || bytes > 32 * 1024 * 1024))
        { bytes -= entries[0].Bytes; entries.RemoveAt(0); cursor--; }
    }
    internal StructureFile? Undo() => CanUndo ? entries[--cursor].File : null;
    internal StructureFile? Redo() => CanRedo ? entries[++cursor].File : null;
}
