using System.Reflection;
using HotAvalonia.Helpers;
using HotAvalonia.IO;

namespace HotAvalonia;

/// <summary>
/// Provides methods to create hot reload contexts for Avalonia applications.
/// </summary>
public static class AvaloniaHotReloadContext
{
    /// <summary>
    /// Creates a hot reload context for all assemblies within the specified <see cref="AppDomain"/>.
    /// </summary>
    /// <remarks>
    /// This context will include all currently loaded assemblies and any of those that are loaded
    /// in the future, automatically determining if they contain Avalonia controls and if their
    /// source project directories can be located.
    /// </remarks>
    /// <param name="appDomain">The <see cref="AppDomain"/> to create the hot reload context from.</param>
    /// <returns>A hot reload context for the specified application domain.</returns>
    public static IHotReloadContext FromAppDomain(AppDomain appDomain)
    {
        _ = appDomain ?? throw new ArgumentNullException(nameof(appDomain));

        return HotReloadContext.FromAppDomain(appDomain, static (_, asm) => FromUnverifiedAssembly(asm));
    }

    /// <summary>
    /// Creates a hot reload context from the specified assembly, if it contains Avalonia controls;
    /// otherwise, returns <c>null</c>.
    /// </summary>
    /// <param name="assembly">The assembly to create the hot reload context from.</param>
    /// <returns>
    /// A hot reload context for the specified assembly, or <c>null</c> if the assembly
    /// does not contain Avalonia controls or if its source project cannot be located.
    /// </returns>
    private static IHotReloadContext? FromUnverifiedAssembly(Assembly assembly)
    {
        AvaloniaControlInfo[] controls = AvaloniaRuntimeXamlScanner.FindAvaloniaControls(assembly).ToArray();
        if (controls.Length == 0)
            return null;

        if (!AvaloniaProjectLocator.TryGetDirectoryName(assembly, controls, out string? rootPath))
        {
            return null;
        }

        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        return new AvaloniaProjectHotReloadContext(rootPath, controls);
    }

    /// <inheritdoc cref="FromAssembly(Assembly, string)"/>
    public static IHotReloadContext FromAssembly(Assembly assembly)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        AvaloniaControlInfo[] controls = AvaloniaRuntimeXamlScanner.FindAvaloniaControls(assembly).ToArray();
        string rootPath = AvaloniaProjectLocator.GetDirectoryName(assembly, controls);
        return new AvaloniaProjectHotReloadContext(rootPath, controls);
    }

    /// <summary>
    /// Creates a hot reload context from the specified assembly, representing a single Avalonia project.
    /// </summary>
    /// <param name="assembly">The assembly to create the hot reload context from.</param>
    /// <param name="rootPath">
    /// The root path associated with the specified assembly,
    /// which is the directory containing its source code.
    /// </param>
    /// <returns>A hot reload context for the specified assembly.</returns>
    public static IHotReloadContext FromAssembly(Assembly assembly, string rootPath)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        IEnumerable<AvaloniaControlInfo> controls = AvaloniaRuntimeXamlScanner.FindAvaloniaControls(assembly);
        return new AvaloniaProjectHotReloadContext(rootPath, controls);
    }

    /// <inheritdoc cref="FromControl(object, string)"/>
    public static IHotReloadContext FromControl(object control)
    {
        _ = control ?? throw new ArgumentNullException(nameof(control));

        return FromControl(control.GetType());
    }

    /// <inheritdoc cref="FromControl(Type, string)"/>
    public static IHotReloadContext FromControl(Type controlType)
    {
        _ = controlType ?? throw new ArgumentNullException(nameof(controlType));

        return FromAssembly(controlType.Assembly);
    }

    /// <summary>
    /// Creates a hot reload context for the assembly containing the specified control.
    /// </summary>
    /// <param name="control">The control to create the hot reload context from.</param>
    /// <param name="controlPath">The path to the control's XAML file.</param>
    /// <returns>A hot reload context for the specified control.</returns>
    public static IHotReloadContext FromControl(object control, string controlPath)
    {
        _ = control ?? throw new ArgumentNullException(nameof(control));

        return FromControl(control.GetType(), controlPath);
    }

    /// <summary>
    /// Creates a hot reload context for the assembly containing the specified control.
    /// </summary>
    /// <param name="controlType">The type of the control to create the hot reload context from.</param>
    /// <param name="controlPath">The path to the control's XAML file.</param>
    /// <returns>A hot reload context for the specified control type.</returns>
    public static IHotReloadContext FromControl(Type controlType, string controlPath)
    {
        _ = controlType ?? throw new ArgumentNullException(nameof(controlType));
        _ = controlPath ?? throw new ArgumentNullException(nameof(controlPath));
        _ = File.Exists(controlPath) ? controlPath : throw new FileNotFoundException(controlPath);

        controlPath = Path.GetFullPath(controlPath);
        if (!AvaloniaRuntimeXamlScanner.TryExtractControlUri(controlType, out string? controlUri))
            throw new ArgumentException("The provided control is not a valid user-defined Avalonia control. Could not determine its URI.", nameof(controlType));

        string rootPath = UriHelper.ResolveHostPath(controlUri, controlPath);
        return FromAssembly(controlType.Assembly, rootPath);
    }
}

/// <summary>
/// Manages the hot reload context for Avalonia controls.
/// </summary>
file sealed class AvaloniaProjectHotReloadContext : IHotReloadContext
{
    /// <summary>
    /// The Avalonia control managers, mapped by their respective file paths.
    /// </summary>
    private readonly Dictionary<string, AvaloniaControlManager> _controls;

    /// <summary>
    /// The file watcher responsible for observing changes in Avalonia control files.
    /// </summary>
    private readonly FileWatcher _watcher;

    /// <summary>
    /// Indicates whether hot reload is currently enabled.
    /// </summary>
    private bool _enabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaProjectHotReloadContext"/> class.
    /// </summary>
    /// <param name="rootPath">The root directory of the Avalonia project to watch.</param>
    /// <param name="controls">The list of Avalonia controls to manage.</param>
    public AvaloniaProjectHotReloadContext(string rootPath, IEnumerable<AvaloniaControlInfo> controls)
    {
        _ = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        _ = Directory.Exists(rootPath) ? rootPath : throw new DirectoryNotFoundException(rootPath);

        rootPath = Path.GetFullPath(rootPath);
        _controls = controls
            .Select(x => ResolveControlManager(x, rootPath))
            .ToDictionary(static x => x.FileName, FileHelper.FileNameComparer);

        _watcher = new(rootPath, _controls.Keys);
        _watcher.Changed += OnChanged;
        _watcher.Moved += OnMoved;
        _watcher.Error += OnError;
    }

    /// <inheritdoc/>
    public bool IsHotReloadEnabled => _enabled;

    /// <inheritdoc/>
    public void EnableHotReload() => _enabled = true;

    /// <inheritdoc/>
    public void DisableHotReload() => _enabled = false;

    /// <inheritdoc/>
    public void Dispose()
    {
        DisableHotReload();

        _watcher.Changed -= OnChanged;
        _watcher.Moved -= OnMoved;
        _watcher.Error -= OnError;
        _watcher.Dispose();

        foreach (AvaloniaControlManager control in _controls.Values)
            control.Dispose();

        _controls.Clear();
    }

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
