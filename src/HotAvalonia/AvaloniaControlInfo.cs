using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HotAvalonia.Helpers;
using HotAvalonia.Reflection.Inject;

namespace HotAvalonia;

/// <summary>
/// Represents metadata information for a control within the Avalonia framework.
/// </summary>
public sealed class AvaloniaControlInfo : IEquatable<AvaloniaControlInfo>
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

    /// <summary>
    /// A delegate that defines a refresh action for the control.
    /// </summary>
    private readonly Action<object>? _refresh;

    /// <inheritdoc cref="AvaloniaControlInfo(Uri, MethodBase, MethodInfo, FieldInfo?, Action{object}?)"/>
    public AvaloniaControlInfo(
        string uri,
        MethodBase build,
        MethodInfo populate)
        : this(new Uri(uri), build, populate, null, null)
    {
    }

    /// <inheritdoc cref="AvaloniaControlInfo(Uri, MethodBase, MethodInfo, FieldInfo?, Action{object}?)"/>
    public AvaloniaControlInfo(
        Uri uri,
        MethodBase build,
        MethodInfo populate)
        : this(uri, build, populate, null, null)
    {
    }

    /// <inheritdoc cref="AvaloniaControlInfo(Uri, MethodBase, MethodInfo, FieldInfo?, Action{object}?)"/>
    internal AvaloniaControlInfo(
        string uri,
        MethodBase build,
        MethodInfo populate,
        FieldInfo? populateOverride = null,
        Action<object>? refresh = null)
        : this(new Uri(uri), build, populate, populateOverride, refresh)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaControlInfo"/> class.
    /// </summary>
    /// <param name="uri">The URI used to identify the control's XAML definition.</param>
    /// <param name="build">The method used to build the control instance.</param>
    /// <param name="populate">The method used to populate the control instance.</param>
    /// <param name="populateOverride">The field responsible for overriding the populate logic of the control.</param>
    /// <param name="refresh">A delegate that defines a refresh action for the control.</param>
    internal AvaloniaControlInfo(
        Uri uri,
        MethodBase build,
        MethodInfo populate,
        FieldInfo? populateOverride = null,
        Action<object>? refresh = null)
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
        _refresh = refresh;

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
    internal MethodBase BuildMethod => _build;

    /// <summary>
    /// The method responsible for populating the control instance with data.
    /// </summary>
    internal MethodInfo PopulateMethod => _populate;

    /// <summary>
    /// The field responsible for overriding the populate logic of the control.
    /// </summary>
    internal FieldInfo? PopulateOverrideProperty => _populateOverride;

    /// <summary>
    /// Loads an Avalonia control from XAML markup and initializes it.
    /// </summary>
    /// <param name="xaml">The XAML markup to populate the control with.</param>
    /// <param name="control">The optional control object to be populated.</param>
    /// <param name="compiledPopulateMethod">The newly compiled populate method, if the compilation was successful.</param>
    public object? Load(string xaml, object? control, out MethodInfo? compiledPopulateMethod)
    {
        control = AvaloniaControlHelper.Load(xaml, _uri, control, _build.DeclaringType?.Assembly, out compiledPopulateMethod);
        if (control is not null)
            Refresh(control);

        return control;
    }

    /// <summary>
    /// Builds the control instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider used in the build process.</param>
    /// <returns>The built control instance.</returns>
    public object Build(IServiceProvider? serviceProvider = null)
        => AvaloniaControlHelper.Build(_build, serviceProvider);

    /// <inheritdoc cref="Populate(IServiceProvider?, object)"/>
    public void Populate(object control)
        => Populate(serviceProvider: null, control);

    /// <inheritdoc cref="Populate(IServiceProvider?, object, MethodBase)"/>
    public void Populate(IServiceProvider? serviceProvider, object control)
        => Populate(serviceProvider, control, _populate);

    /// <summary>
    /// Populates the provided control instance.
    /// </summary>
    /// <param name="serviceProvider">The service provider used in the populate process.</param>
    /// <param name="control">The control instance to populate.</param>
    /// <param name="populateMethod">The method used to populate the control.</param>
    internal void Populate(IServiceProvider? serviceProvider, object control, MethodBase populateMethod)
    {
        AvaloniaControlHelper.Populate(populateMethod, serviceProvider, control);
        Refresh(control);
    }

    /// <summary>
    /// Attempts to override the populate method with a specified populate action.
    /// </summary>
    /// <param name="populate">The populate action to override the original one with.</param>
    /// <param name="injection">
    /// When this method returns, contains the <see cref="IInjection"/> instance if the injection was successful;
    /// otherwise, <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the injection was successful;
    /// otherwise, <c>false</c>.
    /// </returns>
    internal bool TryInjectPopulateOverride(Action<IServiceProvider?, object> populate, [NotNullWhen(true)] out IInjection? injection)
    {
        injection = null;

        if (_populateOverride is null)
            return false;

        return AvaloniaControlHelper.TryInjectPopulateOverride(_populateOverride, populate, out injection);
    }

    /// <summary>
    /// Refreshes the inner state of the given control after it has been populated.
    /// </summary>
    /// <remarks>
    /// Some things (e.g., cached named control references) are not a part of
    /// the population routine, so we need to sort those out manually.
    /// </remarks>
    /// <param name="control">The control to refresh.</param>
    public void Refresh(object control)
    {
        LoggingHelper.Log(control, "Refreshing...");
        _refresh?.Invoke(control);
    }

    /// <inheritdoc/>
    public override string ToString()
        => $"{nameof(AvaloniaControlInfo)} {{ {nameof(Uri)} = {Uri}, {nameof(ControlType)} = {ControlType} }}";

    /// <inheritdoc/>
    public override bool Equals(object obj)
        => obj is AvaloniaControlInfo other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(AvaloniaControlInfo other)
        => other._uri == _uri && other._build == _build && other._populate == _populate;

    /// <inheritdoc/>
    public override int GetHashCode()
        => _controlType.GetHashCode();
}
