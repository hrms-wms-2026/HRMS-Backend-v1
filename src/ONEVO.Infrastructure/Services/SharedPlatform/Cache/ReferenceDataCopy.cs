using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace ONEVO.Infrastructure.Services.SharedPlatform;

/// <summary>
/// Fresh copies of cached entities (task 22). Several requests read the same cached rows at once and some callers change
/// what they read (a reorder handler edits the categories it listed, then saves them), so a cached entity is never handed
/// out: each read gets its own copy. Scalar properties and arrays are copied; collection navigations (for example a clock-in
/// policy's late-deduction rules) get a new list of copied children whose back-reference points at the new parent.
/// Private list fields (pending domain events) get a new list. Single reference navigations are not loaded by the cached
/// queries and are cleared.
/// </summary>
public static class ReferenceDataCopy
{
    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly ConcurrentDictionary<Type, TypeShape> Shapes = new();

    public static IReadOnlyList<T> All<T>(IReadOnlyList<T> source) where T : class
    {
        var copies = new List<T>(source.Count);
        foreach (var item in source)
            copies.Add(One(item));
        return copies;
    }

    public static T? OneOrNull<T>(T? source) where T : class => source is null ? null : One(source);

    public static T One<T>(T source) where T : class => (T)Copy(source, parent: null);

    private static object Copy(object source, object? parent)
    {
        var shape = Shapes.GetOrAdd(source.GetType(), Describe);
        var copy = MemberwiseCloneMethod.Invoke(source, null)!;

        foreach (var array in shape.Arrays)
        {
            if (array.GetValue(source) is Array items)
                array.SetValue(copy, items.Clone());
        }

        foreach (var reference in shape.References)
        {
            // The back-reference of a child points at its new parent; any other navigation was not loaded.
            reference.SetValue(copy, parent is not null && reference.PropertyType.IsInstanceOfType(parent) ? parent : null);
        }

        // Private lists such as BaseEntity's pending domain events: a new list, so an event raised on one copy is not
        // seen by every other copy (and the cache).
        foreach (var field in shape.PrivateCollections)
        {
            var value = field.GetValue(source);
            field.SetValue(copy, value is null ? null : Activator.CreateInstance(field.FieldType, value));
        }

        foreach (var collection in shape.Collections)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(collection.ElementType))!;
            if (collection.Property.GetValue(source) is IEnumerable children)
            {
                foreach (var child in children)
                    list.Add(child is null ? null : Copy(child, copy));
            }
            collection.Property.SetValue(copy, list);
        }

        return copy;
    }

    private static TypeShape Describe(Type type)
    {
        var references = new List<PropertyInfo>();
        var arrays = new List<PropertyInfo>();
        var collections = new List<CollectionShape>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                continue;

            var propertyType = property.PropertyType;
            if (propertyType == typeof(string) || propertyType.IsValueType)
                continue;

            if (propertyType.IsArray)
            {
                arrays.Add(property);
                continue;
            }

            var element = CollectionElementType(propertyType);
            if (element is not null)
            {
                if (!element.IsValueType && element != typeof(string))
                    collections.Add(new CollectionShape(property, element));
                continue;
            }

            if (propertyType.IsClass && propertyType.Namespace?.StartsWith("ONEVO.Domain", StringComparison.Ordinal) == true)
                references.Add(property);
        }

        var privateCollections = new List<FieldInfo>();
        for (var level = type; level is not null && level != typeof(object); level = level.BaseType)
        {
            foreach (var field in level.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.Name.Contains("k__BackingField", StringComparison.Ordinal) || !field.FieldType.IsGenericType)
                    continue;
                var definition = field.FieldType.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(HashSet<>) || definition == typeof(Dictionary<,>))
                    privateCollections.Add(field);
            }
        }

        return new TypeShape(references, arrays, collections, privateCollections);
    }

    /// <summary>The element type when <paramref name="type"/> is ICollection&lt;T&gt;, IList&lt;T&gt;, List&lt;T&gt; or similar.</summary>
    private static Type? CollectionElementType(Type type)
    {
        if (!type.IsGenericType)
            return null;
        var definition = type.GetGenericTypeDefinition();
        if (definition != typeof(ICollection<>) && definition != typeof(IList<>) && definition != typeof(List<>)
            && definition != typeof(IEnumerable<>) && definition != typeof(IReadOnlyCollection<>)
            && definition != typeof(IReadOnlyList<>) && definition != typeof(HashSet<>) && definition != typeof(ISet<>))
            return null;
        var element = type.GetGenericArguments()[0];
        // Only lists can take a List<T>; leave sets and other shapes alone (none of the cached entities uses them).
        return typeof(List<>).MakeGenericType(element).IsAssignableTo(type) ? element : null;
    }

    private sealed record CollectionShape(PropertyInfo Property, Type ElementType);

    private sealed record TypeShape(
        IReadOnlyList<PropertyInfo> References,
        IReadOnlyList<PropertyInfo> Arrays,
        IReadOnlyList<CollectionShape> Collections,
        IReadOnlyList<FieldInfo> PrivateCollections);
}
