using System.Diagnostics;
using HotAvalonia.Helpers;

namespace HotAvalonia.IO;

/// <summary>
/// Monitors the file system events for a specified directory and its subdirectories.
/// </summary>
internal sealed class FileWatcher : IDisposable
{
    /// <summary>
    /// The minimum time difference required to consider a write operation as unique.
    /// </summary>
    /// <remarks>
    /// https://en.wikipedia.org/wiki/Mental_chronometry#Measurement_and_mathematical_descriptions
    /// </remarks>
    private const double MinWriteTimeDifference = 150;

    /// <summary>
    /// The time duration for which create and delete events are buffered before being processed.
    /// </summary>
    private const double EventBufferLifetime = 100;

    /// <summary>
    /// The set of tracked file paths.
    /// </summary>
    private readonly HashSet<string> _files;

    /// <summary>
    /// The last write times for the tracked files.
    /// </summary>
    private readonly Dictionary<string, DateTime> _lastWriteTimes;

    /// <summary>
    /// The list of buffered create and delete events awaiting processing.
    /// </summary>
    private readonly List<(FileSystemEventArgs Event, long Timestamp)> _eventBuffer;

    /// <summary>
    /// The object used for locking in thread-safe operations.
    /// </summary>
    private readonly object _lock;

    /// <summary>
    /// The native file system watcher used for monitoring.
    /// </summary>
    private FileSystemWatcher? _systemWatcher;

    /// <summary>
    /// List of extensions to watch (including dot)
    /// </summary>
    private IEnumerable<string>? _extensionsToWatch;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileWatcher"/> class.
    /// </summary>
    /// <param name="rootPath">The root directory to be watched.</param>
    public FileWatcher(string rootPath, IEnumerable<string>? extensionsToWatch = null)
    {
        _ = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _ = Directory.Exists(rootPath) ? rootPath : throw new DirectoryNotFoundException(rootPath);
        _extensionsToWatch = extensionsToWatch;

        DirectoryName = rootPath;
        _systemWatcher = CreateFileSystemWatcher(rootPath);
        _files = new(FileHelper.FileNameComparer);
        _lastWriteTimes = new(FileHelper.FileNameComparer);
        _eventBuffer = new();
        _lock = new();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileWatcher"/> class.
    /// </summary>
    /// <param name="rootPath">The root directory to be watched.</param>
    /// <param name="fileNames">The initial list of file names to be tracked.</param>
    public FileWatcher(string rootPath, IEnumerable<string> fileNames, IEnumerable<string> extensionsToWatch)
        : this(rootPath, extensionsToWatch)
    {
        _ = fileNames ?? throw new ArgumentNullException(nameof(fileNames));

        foreach (string fileName in fileNames)
        {
            _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

            string fullFileName = Path.GetFullPath(fileName);
            _files.Add(fullFileName);
        }
    }

    /// <summary>
    /// Occurs when a change in a file is detected.
    /// </summary>
    public event FileSystemEventHandler? Changed;

    /// <summary>
    /// Occurs when a move operation for a file is detected.
    /// </summary>
    public event MovedEventHandler? Moved;

    /// <summary>
    /// Occurs when an error is encountered during file monitoring.
    /// </summary>
    public event ErrorEventHandler? Error;

    /// <summary>
    /// The root directory being watched.
    /// </summary>
    public string DirectoryName { get; }

    /// <summary>
    /// The list of tracked files.
    /// </summary>
    public IEnumerable<string> FileNames
    {
        get
        {
            lock (_lock)
                return _files.ToArray();
        }
    }

    /// <summary>
    /// The sequence of buffered create and delete events awaiting processing.
    /// </summary>
    private IEnumerable<FileSystemEventArgs> BufferedEvents
    {
        get
        {
            CleanupEventBuffer();

            return _eventBuffer.Select(static x => x.Event);
        }
    }

    /// <summary>
    /// Starts watching a specified file.
    /// </summary>
    /// <param name="fileName">The name of the file to be watched.</param>
    public void Watch(string fileName)
    {
        _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

        fileName = Path.GetFullPath(fileName);

        lock (_lock)
            _files.Add(fileName);
    }

    /// <summary>
    /// Stops watching a specified file.
    /// </summary>
    /// <param name="fileName">The name of the file to stop watching.</param>
    public void Unwatch(string fileName)
    {
        _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

        fileName = Path.GetFullPath(fileName);

        lock (_lock)
            _files.Remove(fileName);
    }

    /// <summary>
    /// Handles the events of file creation and deletion.
    /// Determines if a file move has taken place.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing information about the file change.</param>
    private void OnCreatedOrDeleted(object sender, FileSystemEventArgs args)
    {
        var fullPath = args.FullPath;
        if (!IsFileIncluded(fullPath))
        {
            return;
        }
        if (args.ChangeType == WatcherChangeTypes.Created)
        {
            LoggingHelper.Logger?.Log(this, "File created: {0}", GetRelativePath(fullPath));
        }
        if (args.ChangeType == WatcherChangeTypes.Deleted)
        {
            LoggingHelper.Logger?.Log(this, "File deleted: {0}", GetRelativePath(fullPath));
        }

        StringComparer fileNameComparer = FileHelper.FileNameComparer;
        WatcherChangeTypes oppositeChangeType = args.ChangeType is WatcherChangeTypes.Created ? WatcherChangeTypes.Deleted : WatcherChangeTypes.Created;
        string fileName = Path.GetFileName(fullPath);
        string? newFullPath = null;
        string? oldFullPath = null;

        lock (_lock)
        {
            FileSystemEventArgs? bufferedArgs = BufferedEvents.FirstOrDefault(x => x.ChangeType == oppositeChangeType && fileNameComparer.Equals(Path.GetFileName(x.FullPath), fileName));
            if (bufferedArgs is null)
            {
                BufferEvent(args);
                return;
            }

            (newFullPath, oldFullPath) = args.ChangeType is WatcherChangeTypes.Created
                ? (args.FullPath, bufferedArgs.FullPath)
                : (bufferedArgs.FullPath, args.FullPath);
        }

        OnMoved(newFullPath, oldFullPath);
    }

    /// <summary>
    /// Processes the event of a file change..
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing information about the file change.</param>
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        var fullPath = args.FullPath;
        if(!IsFileIncluded(fullPath))
        {
            return;
        }
        LoggingHelper.Logger?.Log(this, "File changed: {0}, change type: {1}", GetRelativePath(fullPath), args.ChangeType);

        string path = Path.GetFullPath(fullPath);
        lock (_lock)
        {
            if (!IsWatchingFile(path) || !TryUpdateLastWriteTime(path))
                return;
        }

        Changed?.Invoke(this, args);
    }

    /// <summary>
    /// Handles the event when a file is renamed.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">Event arguments containing the old and new name of the file.</param>
    private void OnRenamed(object sender, RenamedEventArgs args)
        => OnMoved(args.FullPath, args.OldFullPath);

    /// <summary>
    /// Processes the movement of a file and updates the internal list of files being watched.
    /// </summary>
    /// <param name="newFullPath">The new full path of the file.</param>
    /// <param name="oldFullPath">The old full path of the file.</param>
    private void OnMoved(string newFullPath, string oldFullPath)
    {
        if (!IsFileIncluded(newFullPath) || !IsFileIncluded(oldFullPath))
        {
            return;
        }
        LoggingHelper.Logger?.Log(this, "File moved: {0} -> {1}", GetRelativePath(oldFullPath), GetRelativePath(newFullPath));

        newFullPath = Path.GetFullPath(newFullPath);
        oldFullPath = Path.GetFullPath(oldFullPath);

        lock (_lock)
        {
            if (!IsWatchingFile(oldFullPath) || !TryUpdateLastWriteTime(newFullPath))
                return;

            _files.Remove(oldFullPath);
            _lastWriteTimes.Remove(oldFullPath);
            _files.Add(newFullPath);
        }

        Moved?.Invoke(this, new(newFullPath, oldFullPath));
    }

    /// <summary>
    /// Handles any error that occurs during file watching.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing error details.</param>
    private void OnError(object sender, ErrorEventArgs args)
        => Error?.Invoke(this, args);

    /// <summary>
    /// Adds an event to the internal buffer for further processing.
    /// </summary>
    /// <param name="args">The event arguments to be buffered.</param>
    private void BufferEvent(FileSystemEventArgs args)
    {
        long currentTimestamp = StopwatchHelper.GetTimestamp();

        _eventBuffer.Add((args, currentTimestamp));
    }

    /// <summary>
    /// Removes stale events from the internal buffer.
    /// </summary>
    private void CleanupEventBuffer()
    {
        long currentTimestamp = StopwatchHelper.GetTimestamp();

        _eventBuffer.RemoveAll(x => StopwatchHelper.GetElapsedTime(x.Timestamp, currentTimestamp).TotalMilliseconds > EventBufferLifetime);
    }

    /// <summary>
    /// Attempts to update the last write time for the specified file and validates if it should trigger further actions.
    /// </summary>
    /// <param name="fileName">The name of the file to update.</param>
    /// <returns>
    /// <c>true</c> if the last write time was successfully updated and meets the criteria for an action;
    /// otherwise, <c>false</c>.
    /// </returns>
    private bool TryUpdateLastWriteTime(string fileName)
    {
        DateTime newWriteTime = string.IsNullOrEmpty(fileName) ? default : File.GetLastWriteTime(fileName);

        if (!_lastWriteTimes.TryGetValue(fileName, out DateTime lastWriteTime))
            lastWriteTime = default;

        if (newWriteTime == lastWriteTime)
            return false;

        _lastWriteTimes[fileName] = newWriteTime;
        return (newWriteTime - lastWriteTime).TotalMilliseconds >= MinWriteTimeDifference;
    }

    /// <summary>
    /// Determines if a file is being watched.
    /// </summary>
    /// <param name="fileName">The name of the file to check.</param>
    /// <returns><c>true</c> if the file is being watched; otherwise, <c>false</c>.</returns>
    private bool IsWatchingFile(string fileName)
        => _files.Contains(fileName);

    /// <summary>
    /// Disposes resources used by this file watcher.
    /// </summary>
    public void Dispose()
    {
        DisposeFileSystemWatcher(_systemWatcher);
        _systemWatcher = null;
    }

    /// <summary>
    /// Creates and configures a new <see cref="FileSystemWatcher"/> for the specified root path.
    /// </summary>
    /// <param name="rootPath">The root directory to watch.</param>
    /// <returns>The created and configured <see cref="FileSystemWatcher"/>.</returns>
    private FileSystemWatcher CreateFileSystemWatcher(string rootPath)
    {
        FileSystemWatcher watcher = new(rootPath)
        {
            EnableRaisingEvents = false,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
                | NotifyFilters.DirectoryName
                | NotifyFilters.FileName,
        };
        watcher.Created += OnCreatedOrDeleted;
        watcher.Deleted += OnCreatedOrDeleted;
        watcher.Changed += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private string GetRelativePath(string path)
    {
        if (path.StartsWith(DirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return path.Substring(DirectoryName.Length);
        }
        return path;
    }

    private bool IsFileIncluded(string fileName)
    {
        if (_extensionsToWatch == null)
        {
            return true;
        }
        return _extensionsToWatch.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Disposes a given <see cref="FileSystemWatcher"/> and detaches all its event handlers.
    /// </summary>
    /// <param name="watcher">The <see cref="FileSystemWatcher"/> to dispose.</param>
    private void DisposeFileSystemWatcher(FileSystemWatcher? watcher)
    {
        if (watcher is null)
            return;

        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnCreatedOrDeleted;
        watcher.Deleted -= OnCreatedOrDeleted;
        watcher.Changed -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }
}
