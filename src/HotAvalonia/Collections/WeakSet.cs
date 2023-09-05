using System.Collections;
using System.Runtime.CompilerServices;

namespace HotAvalonia.Collections;

#if NETSTANDARD2_1_OR_GREATER
/// <summary>
/// Represents a dynamically resizable set of weak references.
/// </summary>
/// <typeparam name="T">The type of elements in the set. Must be a reference type.</typeparam>
internal sealed class WeakSet<T> : ICollection<T> where T : class
{
    /// <summary>
    /// The weak table this set wraps.
    /// </summary>
    private readonly ConditionalWeakTable<T, object?> _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeakSet{T}"/> class.
    /// </summary>
    public WeakSet()
    {
        _items = new();
    }

    /// <summary>
    /// The current number of items still accessible in the set.
    /// </summary>
    public int Count => _items.Count();

    /// <summary>
    /// Adds an item to the set.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        _items.AddOrUpdate(item, default);
    }

    /// <summary>
    /// Removes an item from the set.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns><c>true</c> if the item was successfully removed; otherwise, <c>false</c>.</returns>
    public bool Remove(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        return _items.Remove(item);
    }

    /// <summary>
    /// Removes all items from the set.
    /// </summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// Determines whether the set contains a specific item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><c>true</c> if the item is found in the set; otherwise, <c>false</c>.</returns>
    public bool Contains(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        return _items.TryGetValue(item, out _);
    }

    /// <summary>
    /// Copies the elements of the set to an array, starting at a particular index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        _ = array ?? throw new ArgumentNullException(nameof(array));
        _ = arrayIndex < 0 ? throw new ArgumentOutOfRangeException(nameof(arrayIndex)) : arrayIndex;

        foreach ((T item, _) in _items)
        {
            if (arrayIndex >= array.Length)
                throw new ArgumentException(nameof(array));

            array[arrayIndex++] = item;
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the set.
    /// </summary>
    /// <returns>An enumerator for the set.</returns>
    public IEnumerator<T> GetEnumerator() => _items.Select(static x => x.Key).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    bool ICollection<T>.IsReadOnly => false;
}
#else
/// <summary>
/// Represents a dynamically resizable set of weak references.
/// </summary>
/// <typeparam name="T">The type of elements in the set. Must be a reference type.</typeparam>
internal sealed class WeakSet<T> : ICollection<T> where T : class
{
    /// <summary>
    /// The default initial capacity of the set.
    /// </summary>
    private const int DefaultCapacity = 4;

    /// <summary>
    /// The factor by which the set grows when capacity is exhausted.
    /// </summary>
    private const int GrowthFactor = 2;

    /// <summary>
    /// The weak table that simplifies tracking references that remain accessible.
    /// </summary>
    private ConditionalWeakTable<T, object?> _accessibleItems;

    /// <summary>
    /// The items stored as weak references.
    /// </summary>
    private WeakReference<T?>?[] _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeakSet{T}"/> class.
    /// </summary>
    public WeakSet()
    {
        _accessibleItems = new();
        _items = Array.Empty<WeakReference<T?>?>();
    }

    /// <summary>
    /// The current number of items still accessible in the set.
    /// </summary>
    public int Count
    {
        get
        {
            int count = 0;

            foreach (WeakReference<T?>? x in _items)
                if (x is not null && x.TryGetTarget(out T? target) && target is not null)
                    ++count;

            return count;
        }
    }

    /// <summary>
    /// Adds an item to the set.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        if (_accessibleItems.TryGetValue(item, out _))
            return;

        for (int i = 0; i < _items.Length; ++i)
        {
            WeakReference<T?>? x = _items[i];
            if (x is null || !x.TryGetTarget(out _))
            {
                _items[i] = new(item);
                _accessibleItems.Add(item, default);
                return;
            }
        }

        GrowAndAdd(item);
    }

    /// <summary>
    /// Grows the set capacity and adds an item.
    /// </summary>
    /// <param name="item">The item to add.</param>
    private void GrowAndAdd(T item)
    {
        int oldCapacity = _items.Length;
        int newCapacity = oldCapacity == 0 ? DefaultCapacity : (_items.Length * GrowthFactor);

        WeakReference<T?>?[] newItems = new WeakReference<T?>?[newCapacity];
        Array.Copy(_items, newItems, _items.Length);
        _items = newItems;

        _items[oldCapacity] = new(item);
        _accessibleItems.Add(item, default);
    }

    /// <summary>
    /// Removes an item from the set.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns><c>true</c> if the item was successfully removed; otherwise, <c>false</c>.</returns>
    public bool Remove(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        if (!_accessibleItems.TryGetValue(item, out _))
            return false;

        for (int i = 0; i < _items.Length; ++i)
        {
            WeakReference<T?>? x = _items[i];
            if (x is null || !x.TryGetTarget(out T? target) || !ReferenceEquals(item, target))
                continue;

            _items[i] = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes all items from the set.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _items.Length; ++i)
            _items[i] = null;

        // Clear is not available in .NET Standard 2.0
        _accessibleItems = new();
    }

    /// <summary>
    /// Determines whether the set contains a specific item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><c>true</c> if the item is found in the set; otherwise, <c>false</c>.</returns>
    public bool Contains(T item)
    {
        _ = item ?? throw new ArgumentNullException(nameof(item));

        return _accessibleItems.TryGetValue(item, out _);
    }

    /// <summary>
    /// Copies the elements of the set to an array, starting at a particular index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        _ = array ?? throw new ArgumentNullException(nameof(array));
        _ = arrayIndex < 0 ? throw new ArgumentOutOfRangeException(nameof(arrayIndex)) : arrayIndex;

        for (int i = 0; i < _items.Length; ++i)
        {
            WeakReference<T?>? x = _items[i];
            if (x is null || !x.TryGetTarget(out T? target) || target is null)
                continue;

            if (arrayIndex >= array.Length)
                throw new ArgumentException(nameof(array));

            array[arrayIndex++] = target;
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the set.
    /// </summary>
    /// <returns>An enumerator for the set.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        foreach (WeakReference<T?>? x in _items)
            if (x is not null && x.TryGetTarget(out T? target) && target is not null)
                yield return target;
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    bool ICollection<T>.IsReadOnly => false;
}
#endif
