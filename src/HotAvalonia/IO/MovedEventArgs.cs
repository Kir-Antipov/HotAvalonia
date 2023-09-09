using System.Diagnostics.CodeAnalysis;

namespace HotAvalonia.IO;

/// <summary>
/// Provides data for the <see cref="FileWatcher.Moved"/> event.
/// </summary>
internal sealed class MovedEventArgs : FileSystemEventArgs
{
    /// <inheritdoc cref="MovedEventArgs(WatcherChangeTypes, string, string)"/>
    public MovedEventArgs(string fullPath, string oldFullPath)
        : this(WatcherChangeTypes.Renamed, fullPath, oldFullPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MovedEventArgs"/> class.
    /// </summary>
    /// <param name="changeType">One of the <see cref="WatcherChangeTypes"/> values.</param>
    /// <param name="fullPath">The fully qualified path of the affected file or directory.</param>
    /// <param name="oldFullPath">The previous fully qualified path of the affected file or directory.</param>
    public MovedEventArgs(WatcherChangeTypes changeType, string fullPath, string oldFullPath)
        : base(changeType, Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath))
    {
        OldFullPath = oldFullPath;
    }

    /// <summary>
    /// The previous fully qualified path of the affected file or directory.
    /// </summary>
    public string OldFullPath { get; }

    /// <summary>
    /// The old name of the affected file or directory.
    /// </summary>
    public string OldName => Path.GetFileName(OldFullPath);
}
