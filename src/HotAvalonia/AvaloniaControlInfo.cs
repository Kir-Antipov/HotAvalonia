using System.Reflection;
using HotAvalonia.Helpers;

namespace HotAvalonia;

/// <summary>
/// Represents metadata information for a control within the Avalonia framework.
/// </summary>
public sealed class AvaloniaControlInfo
{
    /// <summary>
    /// The URI used to identify the control's XAML definition.
    /// </summary>
    private readonly Uri _uri;

    /// <summary>
    /// The type of the control.
    /// </summary>
    private readonly Type _controlType;

    /// <summary>
    /// The method responsible for building the control instance.
    /// </summary>
    private readonly MethodBase _build;

    /// <summary>
    /// The method responsible for populating the control instance with data.
    /// </summary>
    private readonly MethodInfo _populate;

    /// <summary>
    /// The field responsible for overriding the populate logic of the control.
    /// </summary>
    private readonly FieldInfo? _populateOverride;

    /// <inheritdoc cref="AvaloniaControlInfo(Uri, MethodBase, MethodInfo, FieldInfo?)"/>
    public AvaloniaControlInfo(string uri, MethodBase build, MethodInfo populate, FieldInfo? populateOverride = null)
        : this(new Uri(uri), build, populate, populateOverride)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaControlInfo"/> class.
    /// </summary>
    /// <param name="uri">The URI used to identify the control's XAML definition.</param>
    /// <param name="build">The method used to build the control instance.</param>
    /// <param name="populate">The method used to populate the control instance.</param>
    /// <param name="populateOverride">The field responsible for overriding the populate logic of the control.</param>
    public AvaloniaControlInfo(Uri uri, MethodBase build, MethodInfo populate, FieldInfo? populateOverride = null)
    {
        _ = uri ?? throw new ArgumentNullException(nameof(uri));
        _ = build ?? throw new ArgumentNullException(nameof(build));
        _ = populate ?? throw new ArgumentNullException(nameof(populate));

        if (!AvaloniaRuntimeXamlScanner.IsBuildMethod(build))
            throw new ArgumentException("The provided method does not meet the build method criteria.", nameof(build));

        if (!AvaloniaRuntimeXamlScanner.IsPopulateMethod(populate))
            throw new ArgumentException("The provided method does not meet the populate method criteria.", nameof(populate));

        if (populateOverride is not null && !AvaloniaRuntimeXamlScanner.IsPopulateOverrideField(populateOverride))
            throw new ArgumentException("The provided field does not meet the populate override criteria.", nameof(populateOverride));

        _uri = uri;
        _build = build;
        _populate = populate;
        _populateOverride = populateOverride;

        _controlType = build is MethodInfo buildInfo ? buildInfo.ReturnType : build.DeclaringType;
    }

    /// <summary>
    /// The URI used to identify the control's XAML definition.
    /// </summary>
    public Uri Uri => _uri;

    /// <summary>
    /// The type of the control.
    /// </summary>
    public Type ControlType => _controlType;

    /// <summary>
    /// The method responsible for building the control instance.
    /// </summary>
    public MethodBase BuildMethod => _build;

    /// <summary>
    /// The method responsible for populating the control instance with data.
    /// </summary>
    public MethodInfo PopulateMethod => _populate;

    /// <summary>
    /// The field responsible for overriding the populate logic of the control.
    /// </summary>
    public FieldInfo? PopulateOverrideProperty => _populateOverride;

    /// <summary>
    /// Builds the control instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider used in the build process.</param>
    /// <returns>The built control instance.</returns>
    public object Build(IServiceProvider? serviceProvider = null)
        => AvaloniaControlHelper.Build(_build, serviceProvider);

    /// <inheritdoc cref="Populate(IServiceProvider?, object)"/>
    public void Populate(object control)
        => AvaloniaControlHelper.Populate(_populate, serviceProvider: null, control);

    /// <summary>
    /// Populates the provided control instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider used in the populate process.</param>
    /// <param name="control">The control instance to populate.</param>
    public void Populate(IServiceProvider? serviceProvider, object control)
        => AvaloniaControlHelper.Populate(_populate, serviceProvider, control);

    /// <inheritdoc cref="TryOverridePopulate(Action{IServiceProvider, object})"/>
    public bool TryOverridePopulate(Action<object> populate)
        => _populateOverride is not null && AvaloniaControlHelper.TryOverridePopulate(_populateOverride, populate);

    /// <summary>
    /// Attempts to override the populate method with a specified populate action.
    /// </summary>
    /// <param name="populate">The populate action to override the original method with.</param>
    /// <returns><c>true</c> if the override was successful; otherwise, <c>false</c>.</returns>
    public bool TryOverridePopulate(Action<IServiceProvider?, object> populate)
        => _populateOverride is not null && AvaloniaControlHelper.TryOverridePopulate(_populateOverride, populate);
}
