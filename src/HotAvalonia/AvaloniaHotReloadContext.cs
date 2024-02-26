using System.Reflection;
using HotAvalonia.Helpers;
using HotAvalonia.IO;

namespace HotAvalonia;

/// <summary>
/// Manages the hot reload context for Avalonia controls.
/// </summary>
public sealed class AvaloniaHotReloadContext : IDisposable
{
    /// <summary>
    /// File extensions to watch
    /// </summary>
    private static readonly IEnumerable<string> ExtensionsToWatch = [".axaml"];

    /// <summary>
    /// The Avalonia control managers, mapped by their respective file paths.
    /// </summary>
    private readonly Dictionary<string, AvaloniaControlManager> _controls;

    /// <summary>
    /// The file watcher responsible for observing changes in Avalonia control files.
    /// </summary>
    private readonly FileWatcher _watcher;

    /// <summary>
    /// Indicates whether the hot reload is currently enabled.
    /// </summary>
    private bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaHotReloadContext"/> class.
    /// </summary>
    /// <param name="rootPath">The root directory of the Avalonia project to watch.</param>
    /// <param name="controls">The list of Avalonia controls to manage.</param>
    private AvaloniaHotReloadContext(string rootPath, IEnumerable<AvaloniaControlInfo> controls)
    {
        _ = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _ = Directory.Exists(rootPath) ? rootPath : throw new DirectoryNotFoundException(rootPath);

        rootPath = Path.GetFullPath(rootPath);
        _controls = controls
            .Select(x => ResolveControlManager(x, rootPath))
            .ToDictionary(static x => x.FileName, FileHelper.FileNameComparer);

        _watcher = new(rootPath, _controls.Keys, ExtensionsToWatch);
        _watcher.Changed += OnChanged;
        _watcher.Moved += OnMoved;
        _watcher.Error += OnError;
    }

    /// <summary>
    /// Creates a hot reload context using the provided assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing Avalonia controls.</param>
    /// <param name="rootPath">The root directory of the Avalonia project.</param>
    /// <returns>A new instance of the <see cref="AvaloniaHotReloadContext"/> class.</returns>
    public static AvaloniaHotReloadContext FromAssembly(Assembly assembly, string rootPath)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        return new(rootPath, AvaloniaRuntimeXamlScanner.FindAvaloniaControls(assembly));
    }

    /// <summary>
    /// Creates a hot reload context using the provided control and its file path.
    /// </summary>
    /// <param name="control">The control belonging to the Avalonia project that needs to be managed.</param>
    /// <param name="controlPath">The full file path that leads to the XAML file defining the control.</param>
    /// <returns>A new instance of the <see cref="AvaloniaHotReloadContext"/> class.</returns>
    public static AvaloniaHotReloadContext FromControl(object control, string controlPath)
    {
        _ = control ?? throw new ArgumentNullException(nameof(control));
        _ = controlPath ?? throw new ArgumentNullException(nameof(controlPath));
        _ = File.Exists(controlPath) ? controlPath : throw new FileNotFoundException(controlPath);

        controlPath = Path.GetFullPath(controlPath);
        if (!AvaloniaRuntimeXamlScanner.TryExtractControlUri(control.GetType(), out string? controlUri))
            throw new ArgumentException("The provided control is not a valid user-defined Avalonia control. Could not determine its URI.", nameof(control));

        string rootPath = UriHelper.ResolveHostPath(controlUri, controlPath);
        return FromAssembly(control.GetType().Assembly, rootPath);
    }

    /// <summary>
    /// Indicates whether the hot reload is currently enabled.
    /// </summary>
    public bool IsHotReloadEnabled => _enabled;

    /// <summary>
    /// Enables the hot reload feature.
    /// </summary>
    public void EnableHotReload() => _enabled = true;

    /// <summary>
    /// Disables the hot reload feature.
    /// </summary>
    public void DisableHotReload() => _enabled = false;

    /// <summary>
    /// Handles the file changes by attempting to reload the corresponding Avalonia control.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing details of the changed file.</param>
    private async void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!_enabled)
            return;

        string path = Path.GetFullPath(args.FullPath);
        if (!_controls.TryGetValue(path, out AvaloniaControlManager? controlManager))
            return;

        try
        {
            await controlManager.ReloadAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            LoggingHelper.Logger?.Log(this, "Failed to reload {Type} ({Uri}): {Error}", controlManager.Control.ControlType, controlManager.Control.Uri, e);
        }
    }

    /// <summary>
    /// Handles the moved files by updating their corresponding <see cref="AvaloniaControlManager"/> entries.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing details of the moved file.</param>
    private void OnMoved(object sender, MovedEventArgs args)
    {
        string oldFullPath = Path.GetFullPath(args.OldFullPath);
        if (!_controls.TryGetValue(oldFullPath, out AvaloniaControlManager? controlManager))
            return;

        _controls.Remove(oldFullPath);

        controlManager.FileName = Path.GetFullPath(args.FullPath);
        _controls[controlManager.FileName] = controlManager;
    }

    /// <summary>
    /// Handles errors that occur during file monitoring.
    /// </summary>
    /// <param name="sender">The source of the event.</param>
    /// <param name="args">The event arguments containing the error details.</param>
    private void OnError(object sender, ErrorEventArgs args)
        => LoggingHelper.Logger?.Log(sender, "An unexpected error occurred while monitoring file changes: {Error}", args.GetException());

    /// <summary>
    /// Disposes the resources used by this context, effectively disabling the hot reload.
    /// </summary>
    public void Dispose()
    {
        _enabled = false;

        _watcher.Changed -= OnChanged;
        _watcher.Moved -= OnMoved;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }

    /// <summary>
    /// Resolves the control manager for the given Avalonia control.
    /// </summary>
    /// <param name="controlInfo">The information about the Avalonia control.</param>
    /// <param name="rootPath">The root directory of the Avalonia project.</param>
    /// <returns>The resolved <see cref="AvaloniaControlManager"/>.</returns>
    private static AvaloniaControlManager ResolveControlManager(AvaloniaControlInfo controlInfo, string rootPath)
    {
        string fileName = Path.GetFullPath(UriHelper.ResolvePathFromUri(rootPath, controlInfo.Uri));
        return new(controlInfo, fileName);
    }
}
