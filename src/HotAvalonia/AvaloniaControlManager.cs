using System.Reflection;
using Avalonia.Threading;
using HotAvalonia.Collections;
using HotAvalonia.Helpers;
using HotAvalonia.Reflection.Inject;

namespace HotAvalonia;

/// <summary>
/// Manages the lifecycle and state of Avalonia controls.
/// </summary>
public sealed class AvaloniaControlManager
{
    /// <summary>
    /// The information about the Avalonia control being managed.
    /// </summary>
    private readonly AvaloniaControlInfo _controlInfo;

    /// <summary>
    /// The set of weak references to the controls managed by this instance.
    /// </summary>
    private readonly WeakSet<object> _controls;

    /// <summary>
    /// The name of the XAML file associated with the control.
    /// </summary>
    private string _fileName;

    /// <summary>
    /// The dynamically compiled populate method associated with the control.
    /// </summary>
    private MethodInfo? _dynamicPopulate;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaControlManager"/> class.
    /// </summary>
    /// <param name="controlInfo">The Avalonia control information.</param>
    /// <param name="fileName">The name of the XAML file.</param>
    public AvaloniaControlManager(AvaloniaControlInfo controlInfo, string fileName)
    {
        _ = controlInfo ?? throw new ArgumentNullException(nameof(controlInfo));
        _ = fileName ?? throw new ArgumentNullException(nameof(fileName));

        _controlInfo = controlInfo;
        _fileName = fileName;
        _controls = new();

        if (!TrySubscribeToPopulate(controlInfo, OnPopulate))
            LoggingHelper.Logger?.Log(this, "Failed to subscribe to the 'Populate' event for {Type} ({Uri}). The control won't be reloaded upon file changes.", controlInfo.ControlType, controlInfo.Uri);
    }

    /// <summary>
    /// The information about the Avalonia control being managed.
    /// </summary>
    public AvaloniaControlInfo Control => _controlInfo;

    /// <summary>
    /// The name of the XAML file associated with the control.
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set => _fileName = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Reloads the controls associated with this manager asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_fileName))
            return;

        string xaml = await FileHelper.ReadAllTextAsync(_fileName, cancellationToken: cancellationToken).ConfigureAwait(true);
        await Dispatcher.UIThread.InvokeAsync(() => ReloadAsync(xaml, cancellationToken), DispatcherPriority.Render);
    }

    /// <summary>
    /// Reloads the controls associated with this manager on the UI thread.
    /// </summary>
    /// <param name="xaml">The XAML markup to reload the control from.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    private async Task ReloadAsync(string xaml, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();

        using IEnumerator<object> controls = _controls.GetEnumerator();
        object? firstControl = controls.MoveNext() ? controls.Current : null;

        AvaloniaControlHelper.Load(xaml, _controlInfo.Uri, firstControl, out MethodInfo? newDynamicPopulate);
        _dynamicPopulate = newDynamicPopulate ?? _dynamicPopulate;
        if (_dynamicPopulate is null)
            return;

        while (controls.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();

            AvaloniaControlHelper.Populate(_dynamicPopulate, serviceProvider: null, controls.Current);
        }
    }

    /// <summary>
    /// Handles the population of a control.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="control">The control to be populated.</param>
    /// <returns><c>true</c> if the control was populated successfully; otherwise, <c>false</c>.</returns>
    private bool OnPopulate(IServiceProvider? provider, object control)
    {
        _controls.Add(control);
        if (_dynamicPopulate is null)
            return false;

        AvaloniaControlHelper.Populate(_dynamicPopulate, provider, control);
        return true;
    }

    /// <summary>
    /// Attempts to subscribe to the populate method of the given control.
    /// </summary>
    /// <param name="controlInfo">The Avalonia control information.</param>
    /// <param name="onPopulate">The callback to invoke when a control is populated.</param>
    /// <returns><c>true</c> if the subscription was successful; otherwise, <c>false</c>.</returns>
    private static bool TrySubscribeToPopulate(AvaloniaControlInfo controlInfo, Func<IServiceProvider?, object, bool> onPopulate)
    {
        if (MethodHelper.IsMethodSwappingAvailable())
        {
            CallbackInjector.Inject(controlInfo.PopulateMethod, onPopulate);
            return true;
        }

        return controlInfo.TryOverridePopulate(PopulateOverride);

        void PopulateOverride(IServiceProvider? provider, object control)
        {
            if (!onPopulate(provider, control))
                controlInfo.Populate(provider, control);
        }
    }
}
