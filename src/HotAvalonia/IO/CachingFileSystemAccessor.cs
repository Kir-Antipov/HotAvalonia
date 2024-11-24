using System.Collections.Concurrent;
using HotAvalonia.Helpers;

namespace HotAvalonia.IO;

/// <summary>
/// Provides a caching layer for file system operations.
/// </summary>
internal sealed class CachingFileSystemAccessor
{
    /// <summary>
    /// A cache to store file entries keyed by their file names.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingFileSystemAccessor"/> class.
    /// </summary>
    /// <param name="fileNameComparer">
    /// An optional equality comparer used for file name comparisons.
    /// Defaults to the system's default comparer if <c>null</c>.
    /// </param>
    public CachingFileSystemAccessor(IEqualityComparer<string>? fileNameComparer = null)
    {
        _cache = new(fileNameComparer ?? PathHelper.PathComparer);
    }

    /// <summary>
    /// Checks whether a file with the specified name exists.
    /// </summary>
    /// <param name="fileName">The name of the file to check.</param>
    /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
    public bool Exists(string fileName)
    {
        _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

        return _cache.ContainsKey(fileName) || File.Exists(fileName);
    }

    /// <summary>
    /// Opens a stream for reading the contents of the specified file.
    /// </summary>
    /// <param name="fileName">The name of the file to open.</param>
    /// <returns>A <see cref="Stream"/> for reading the file's contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist.</exception>
    public Stream Open(string fileName)
    {
        _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

        return _cache.GetOrAdd(fileName, CreateEntry).Open();
    }

    /// <summary>
    /// Creates a new cache entry for the specified file.
    /// </summary>
    /// <param name="fileName">The name of the file for which to create an entry.</param>
    /// <returns>An <see cref="Entry"/> representing the file.</returns>
    private static Entry CreateEntry(string fileName)
    {
        if (!File.Exists(fileName))
            throw new FileNotFoundException(fileName);

        return new(fileName);
    }

    /// <summary>
    /// Represents a cached file entry.
    /// </summary>
    private sealed class Entry
    {
        /// <summary>
        /// The name of the file represented by this entry.
        /// </summary>
        private readonly string _fileName;

        /// <summary>
        /// The last known write time of the file, used for cache validation.
        /// </summary>
        private long _lastWriteTime;

        /// <summary>
        /// The cached data of the file, if available.
        /// </summary>
        private byte[]? _data;

        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class.
        /// </summary>
        /// <param name="fileName">The name of the file represented by this entry.</param>
        public Entry(string fileName)
        {
            _fileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
            _lastWriteTime = DateTime.MinValue.Ticks;
        }

        /// <summary>
        /// Gets the name of the file represented by this entry.
        /// </summary>
        public string FileName => _fileName;

        /// <summary>
        /// Opens a stream to read the cached data of the file.
        /// </summary>
        /// <returns>A <see cref="Stream"/> for reading the file's cached data.</returns>
        public Stream Open()
        {
            byte[]? data = _data;
            DateTime lastWriteTime = new(Interlocked.Read(ref _lastWriteTime));
            DateTime currentLastWriteTime = File.GetLastWriteTimeUtc(_fileName);

            if (data is null || currentLastWriteTime != lastWriteTime && File.Exists(_fileName))
            {
                data = File.ReadAllBytes(_fileName);
                Interlocked.Exchange(ref _data, data);
                Interlocked.Exchange(ref _lastWriteTime, currentLastWriteTime.Ticks);
            }

            return new MemoryStream(data);
        }
    }
}
