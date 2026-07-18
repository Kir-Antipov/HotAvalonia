using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HotAvalonia.Helpers;

namespace HotAvalonia.Xaml;

/// <summary>
/// Represents a successfully compiled XAML document.
/// </summary>
[DebuggerDisplay($"{{{nameof(Uri)},nq}}")]
public sealed class CompiledXamlDocument : IEquatable<CompiledXamlDocument>
{
    private static readonly FieldInfo? s_childField = typeof(ContentPresenter).GetInstanceField("_child");

    private static readonly FieldInfo? s_createdChildField = typeof(ContentPresenter).GetInstanceField("_createdChild", typeof(bool));

    private static readonly FieldInfo? s_stylesAppliedField = typeof(StyledElement).GetInstanceField("_stylesApplied", typeof(bool));

    private static readonly FieldInfo? s_themeAppliedField = typeof(StyledElement).GetInstanceField("_themeApplied", typeof(bool));

    private static readonly FieldInfo? s_implicitThemeField = typeof(StyledElement).GetInstanceField("_implicitTheme");

    private static readonly Func<AvaloniaObject, AvaloniaObject?>? s_getInheritanceParent = typeof(AvaloniaObject).GetInstanceProperty("InheritanceParent")?.GetMethod?.CreateDelegate<Func<AvaloniaObject, AvaloniaObject?>>();

    internal readonly Uri _uri;

    internal readonly Type _rootType;

    internal readonly MethodBase _build;

    internal readonly Action<IServiceProvider?, object> _populate;

    internal readonly FieldInfo? _populateOverride;

    internal readonly Action<object>? _refresh;

    /// <inheritdoc cref="CompiledXamlDocument(Uri, MethodBase, MethodInfo)"/>
    public CompiledXamlDocument(string uri, MethodBase build, MethodInfo populate)
        : this(new Uri(uri), AsBuildMethod(build), ToPopulateDelegate(populate), null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledXamlDocument"/> class.
    /// </summary>
    /// <param name="uri">The URI associated with the XAML document.</param>
    /// <param name="build">The method used to create a new instance of the root control.</param>
    /// <param name="populate">The method used to populate an existing root control.</param>
    public CompiledXamlDocument(Uri uri, MethodBase build, MethodInfo populate)
        : this(uri, AsBuildMethod(build), ToPopulateDelegate(populate), null, null)
    {
        ArgumentNullException.ThrowIfNull(uri);
    }

    /// <inheritdoc cref="CompiledXamlDocument(Uri, MethodBase, MethodInfo, CompiledXamlDocument)"/>
    [Obsolete("Use 'CompiledXamlDocument(string, MethodBase, MethodInfo)' instead.")]
    public CompiledXamlDocument(string? uri, MethodBase? build, MethodInfo? populate, CompiledXamlDocument baseDocument)
        : this(
            uri is null ? baseDocument._uri : new(uri),
            build is null ? baseDocument._build : AsBuildMethod(build),
            populate is null ? baseDocument._populate : ToPopulateDelegate(populate),
            baseDocument._populateOverride,
            baseDocument._refresh)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompiledXamlDocument"/> class,
    /// using values from the specified base document where necessary.
    /// </summary>
    /// <param name="uri">The URI associated with the XAML document.</param>
    /// <param name="build">The method used to create a new instance of the root control.</param>
    /// <param name="populate">The method used to populate an existing root control.</param>
    /// <param name="baseDocument">The base document from which default values are taken if any of the other parameters are <c>null</c>.</param>
    [Obsolete("Use 'CompiledXamlDocument(Uri, MethodBase, MethodInfo)' instead.")]
    public CompiledXamlDocument(Uri? uri, MethodBase? build, MethodInfo? populate, CompiledXamlDocument baseDocument)
        : this(
            uri ?? baseDocument._uri,
            build is null ? baseDocument._build : AsBuildMethod(build),
            populate is null ? baseDocument._populate : ToPopulateDelegate(populate),
            baseDocument._populateOverride,
            baseDocument._refresh)
    {
    }

    internal CompiledXamlDocument(Uri uri, MethodBase build, Action<IServiceProvider?, object> populate, FieldInfo? populateOverride, Action<object>? refresh)
    {
        _uri = uri;
        _build = build;
        _populate = populate;
        _populateOverride = populateOverride;
        _refresh = refresh;
        _rootType = build is MethodInfo buildInfo ? buildInfo.ReturnType : build.DeclaringType;
    }

    /// <summary>
    /// Gets the URI associated with the current document.
    /// </summary>
    public Uri Uri => _uri;

    /// <summary>
    /// Gets the type of the root element of the current document.
    /// </summary>
    public Type RootType => _rootType;

    /// <summary>
    /// Gets the method used to create a new instance of the root control.
    /// </summary>
    public MethodBase BuildMethod => _build;

    /// <summary>
    /// Gets the method used to populate an existing root control.
    /// </summary>
    public MethodInfo PopulateMethod => _populate.Method;

    /// <summary>
    /// Creates a new instance of the root control representing this document.
    /// </summary>
    /// <param name="serviceProvider">
    /// An optional service provider used to resolve dependencies during the creation process.
    /// </param>
    /// <returns>A newly created instance of the root control.</returns>
    [OverloadResolutionPriority(-1)]
    public object Build(IServiceProvider? serviceProvider = null)
        => Build([serviceProvider]);

    /// <inheritdoc cref="Build(IServiceProvider?)"/>
    /// <param name="services">A list of services required to construct the control.</param>
    public object Build(params ReadOnlySpan<object?> services)
    {
        IServiceProvider? serviceProvider = null;
        LinkedList<object?>? serviceList = null;
        foreach (object? service in services)
        {
            if (serviceProvider is null && service is IServiceProvider parentServiceProvider)
            {
                serviceProvider = XamlIlRuntimeHelpers.CreateRootServiceProviderV3(parentServiceProvider);
            }
            else
            {
                (serviceList ??= new()).AddLast(service);
            }
        }

        ParameterInfo[] parameters = _build.GetParameters();
        object?[] args = parameters.Length == 0 ? null! : new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type argType = parameters[i].ParameterType;
            bool serviceFound = false;
            LinkedListNode<object?>? serviceNode = serviceList?.First;
            while (serviceNode is not null)
            {
                object? service = serviceNode.Value;
                if (service is null ? !argType.IsValueType : argType.IsAssignableFrom(service.GetType()))
                {
                    args[i] = service;
                    serviceList!.Remove(serviceNode);
                    serviceFound = true;
                    break;
                }
            }
            if (serviceFound)
                continue;

            serviceProvider ??= XamlIlRuntimeHelpers.CreateRootServiceProviderV2();
            args[i] = argType.IsAssignableFrom(serviceProvider.GetType()) ? serviceProvider : serviceProvider.GetService(argType);
        }
        return _build.Invoke(null, args) ?? throw new InvalidOperationException();
    }

    /// <inheritdoc cref="Populate(IServiceProvider?, object)"/>
    public void Populate(object rootControl)
        => Populate(serviceProvider: null, rootControl);

    /// <summary>
    /// Populates the specified root control.
    /// </summary>
    /// <param name="serviceProvider">
    /// The service provider used to resolve dependencies during the population process.
    /// </param>
    /// <param name="rootControl">The root control to be populated.</param>
    public void Populate(IServiceProvider? serviceProvider, object rootControl)
    {
        ArgumentNullException.ThrowIfNull(rootControl);

        _populate(serviceProvider ?? XamlIlRuntimeHelpers.CreateRootServiceProviderV2(), rootControl);
    }

    /// <inheritdoc cref="Reload(IServiceProvider?, object)"/>
    public void Reload(object rootControl)
        => Reload(serviceProvider: null, rootControl);

    /// <summary>
    /// Reloads an already populated control.
    /// </summary>
    /// <inheritdoc cref="Populate(IServiceProvider?, object)"/>
    public void Reload(IServiceProvider? serviceProvider, object rootControl)
    {
        Detach(rootControl, out ILogical? logicalParent, out AvaloniaObject? inheritanceParent);
        Clear(rootControl);
        Populate(serviceProvider, rootControl);
        Attach(rootControl, logicalParent, inheritanceParent);
        _refresh?.Invoke(rootControl);
    }

    /// <summary>
    /// Attempts to override the populate action.
    /// </summary>
    /// <param name="populate">The action to use for the populate override injection.</param>
    /// <param name="injection">
    /// When this method returns, contains the injection result if the override was successfully injected;
    /// otherwise, <c>null</c>.
    /// </param>
    /// <returns><c>true</c> if the populate override was successfully injected; otherwise, <c>false</c>.</returns>
    internal bool TryOverridePopulate(Action<IServiceProvider?, object> populate, [NotNullWhen(true)] out IDisposable? injection)
    {
        if (_populateOverride is null)
        {
            injection = null;
            return false;
        }
        injection = new OverridePopulateInjection(_populateOverride, populate);
        return true;
    }

    /// <summary>
    /// Refreshes the specified root control, if provided.
    /// </summary>
    /// <remarks>
    /// Some things (e.g., cached named control references) are not a part of
    /// the population routine, so we need to sort those out manually.
    /// </remarks>
    /// <param name="rootControl">The root control to refresh.</param>
    [Obsolete("Use 'Reload(object)' instead.")]
    public void Refresh(object rootControl)
        => _refresh?.Invoke(rootControl);

    /// <inheritdoc/>
    public override string ToString()
        => $"{nameof(CompiledXamlDocument)} {{ {nameof(Uri)} = {Uri}, {nameof(RootType)} = {RootType} }}";

    /// <inheritdoc/>
    public override bool Equals(object obj)
        => obj is CompiledXamlDocument other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(CompiledXamlDocument other)
        => other._uri == _uri && other._build == _build && other._populate == _populate;

    /// <inheritdoc/>
    public override int GetHashCode()
        => _rootType.GetHashCode();

    /// <summary>
    /// Clears resources, styles, and other data from an Avalonia control.
    /// </summary>
    /// <param name="control">The control to clear.</param>
    private static void Clear(object? control)
    {
        if (control is StyledElement)
        {
            s_stylesAppliedField?.SetValue(control, false);
            s_themeAppliedField?.SetValue(control, false);
            s_implicitThemeField?.SetValue(control, null);
        }

        (ICollection<KeyValuePair<object, object?>>? resources, ICollection<IStyle>? styles) = control switch
        {
            Application x => (x.Resources, x.Styles),
            StyledElement x => (x.Resources, x.Styles),
            Styles x => (x.Resources, x),
            StyleBase x => (x.Resources, x as ICollection<IStyle> ?? (x as IStyleHost)?.Styles),
            IStyleHost x => (x as ICollection<KeyValuePair<object, object?>>, x.Styles),
            _ => (control as ICollection<KeyValuePair<object, object?>>, control as ICollection<IStyle>),
        };
        resources?.Clear();
        styles?.Clear();

        foreach (ContentPresenter presenter in (control as Visual)?.GetVisualDescendants().OfType<ContentPresenter>().Reverse() ?? [])
        {
            if (presenter.Child is not Visual child)
                continue;

            (child.GetVisualParent()?.GetVisualChildren() as IList<Visual>)?.Remove(child);
            (child.GetLogicalParent()?.GetLogicalChildren() as IList<ILogical>)?.Remove(child);
            s_createdChildField?.SetValue(presenter, false);
            s_childField?.SetValue(presenter, null);
        }
    }

    private static void Detach(object? control, out ILogical? logicalParent, out AvaloniaObject? inheritanceParent)
    {
        logicalParent = (control as ILogical)?.GetLogicalParent();
        inheritanceParent = control is AvaloniaObject obj ? s_getInheritanceParent?.Invoke(obj) : null;
        (control as ISetLogicalParent)?.SetParent(null);
        (control as ISetInheritanceParent)?.SetParent(null);
    }

    private static void Attach(object? control, ILogical? logicalParent, AvaloniaObject? inheritanceParent)
    {
        if (logicalParent is not null && control is ISetLogicalParent logical)
            logical.SetParent(logicalParent);

        if (inheritanceParent is not null && control is ISetInheritanceParent inheritance)
            inheritance.SetParent(inheritanceParent);
    }

    private static MethodBase AsBuildMethod(MethodBase build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (!XamlScanner.IsBuildMethod(build))
            ArgumentException.Throw(nameof(build), "The provided method does not meet the build method criteria.");

        return build;
    }

    private static Action<IServiceProvider?, object> ToPopulateDelegate(MethodInfo populate)
    {
        ArgumentNullException.ThrowIfNull(populate);
        if (!XamlScanner.IsPopulateMethod(populate))
            ArgumentException.Throw(nameof(populate), "The provided method does not meet the populate method criteria.");

        return populate.CreateUnsafeDelegate<Action<IServiceProvider?, object>>();
    }
}

/// <summary>
/// Provides functionality to override the population mechanism of Avalonia controls using a custom delegate.
/// </summary>
/// <remarks>
/// This class specifically targets the hidden <c>!XamlIlPopulateOverride</c> field to hijack
/// the logic of control population, allowing for a fallback mechanism whenever proper
/// injection techniques are not available.
/// </remarks>
file sealed class OverridePopulateInjection : IDisposable
{
    /// <summary>
    /// The field to inject the new population logic into.
    /// </summary>
    private readonly FieldInfo _populateOverride;

    /// <summary>
    /// The populate action to override the original one with.
    /// </summary>
    private readonly Delegate? _populate;

    /// <summary>
    /// The previous value of the <c>!XamlIlPopulateOverride</c> field before it was overridden.
    /// </summary>
    private object? _previousPopulateOverride;

    /// <summary>
    /// Initializes a new instance of the <see cref="OverridePopulateInjection"/> class.
    /// </summary>
    /// <param name="populateOverride">The field to inject the new population logic into.</param>
    /// <param name="populate">The populate action to override the original one with.</param>
    public OverridePopulateInjection(FieldInfo populateOverride, Action<IServiceProvider, object> populate)
    {
        _populateOverride = populateOverride;

        if (populateOverride.FieldType == typeof(Action<object>))
        {
            _populate = (object control) =>
            {
                populate(XamlIlRuntimeHelpers.CreateRootServiceProviderV2(), control);
                _populateOverride.SetValue(null, _populate);
            };
        }
        else if (populateOverride.FieldType == typeof(Action<IServiceProvider?, object>))
        {
            _populate = (IServiceProvider? serviceProvider, object control) =>
            {
                populate(serviceProvider ?? XamlIlRuntimeHelpers.CreateRootServiceProviderV2(), control);
                _populateOverride.SetValue(null, _populate);
            };
        }

        Apply();
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="OverridePopulateInjection"/> class.
    /// Reverts the method injection if not already done.
    /// </summary>
    ~OverridePopulateInjection()
    {
        Undo();
    }

    /// <summary>
    /// Applies the method injection.
    /// </summary>
    public void Apply()
    {
        _previousPopulateOverride = _populateOverride.GetValue(null);
        _populateOverride.SetValue(null, _populate);
    }

    /// <summary>
    /// Reverts all the effects caused by the method injection.
    /// </summary>
    public void Undo()
    {
        _populateOverride.SetValue(null, _previousPopulateOverride);
        _previousPopulateOverride = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Undo();
        GC.SuppressFinalize(this);
    }
}
