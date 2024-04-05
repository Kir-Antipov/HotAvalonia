using System.Collections;
using HotAvalonia.Helpers;

namespace HotAvalonia.Collections;

/// <summary>
/// Represents a memory cache that stores items of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of items to be stored in the cache.</typeparam>
internal sealed class MemoryCache<T> : ICollection<T>, IReadOnlyCollection<T>
{
    /// <summary>
    /// The list of entries stored in the memory cache.
    /// </summary>
    private readonly List<Entry> _entries;

    /// <summary>
    /// The lifespan of items in the cache.
    /// </summary>
    private readonly double _lifespan;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCache{T}"/> class with the specified lifespan.
    /// </summary>
    /// <param name="lifespan">The lifespan of items in the cache.</param>
    public MemoryCache(TimeSpan lifespan)
    {
        _entries = new();
        _lifespan = lifespan.TotalMilliseconds;
    }

    /// <summary>
    /// Gets the lifespan of items in the cache.
    /// </summary>
    public TimeSpan Lifespan => TimeSpan.FromMilliseconds(_lifespan);

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            RemoveStale();
            return _entries.Count;
        }
    }

    /// <inheritdoc/>
    bool ICollection<T>.IsReadOnly => false;

    /// <inheritdoc/>
    public void Add(T item) => _entries.Add(new(item));

    /// <inheritdoc/>
    public bool Remove(T item) => _entries.RemoveAll(x => EqualityComparer<T>.Default.Equals(item, x.Value)) != 0;

    /// <summary>
    /// Removes stale entries from the cache based on their timestamp.
    /// </summary>
    private void RemoveStale()
    {
        long currentTimestamp = StopwatchHelper.GetTimestamp();
        _entries.RemoveAll(x => StopwatchHelper.GetElapsedTime(x.Timestamp, currentTimestamp).TotalMilliseconds > _lifespan);
    }

    /// <inheritdoc/>
    public void Clear() => _entries.Clear();

    /// <inheritdoc/>
    public bool Contains(T item) => _entries.Any(x => EqualityComparer<T>.Default.Equals(item, x.Value));

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex)
    {
        RemoveStale();
        _entries.ConvertAll(static x => x.Value).CopyTo(array, arrayIndex);
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        RemoveStale();
        return _entries.Select(static x => x.Value).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Represents an entry in the cache containing the value and its timestamp.
    /// </summary>
    private sealed class Entry
    {
        /// <summary>
        /// Gets the value stored in the cache entry.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Gets the timestamp when the cache entry was added.
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class with the specified value.
        /// </summary>
        /// <param name="value">The value to be stored in the cache entry.</param>
        public Entry(T value)
            : this(value, StopwatchHelper.GetTimestamp())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class with the specified value and timestamp.
        /// </summary>
        /// <param name="value">The value to be stored in the cache entry.</param>
        /// <param name="timestamp">The timestamp when the cache entry was added.</param>
        public Entry(T value, long timestamp)
        {
            Value = value;
            Timestamp = timestamp;
        }
    }
}
