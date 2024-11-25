using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;
using HotAvalonia.Reflection.Inject;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for manipulating and obtaining information about Avalonia controls.
/// </summary>
internal static class AvaloniaControlHelper
{
    /// <summary>
    /// The `_stylesApplied` field of the <see cref="StyledElement"/> class.
    /// </summary>
    private static readonly FieldInfo? s_stylesAppliedField;

    /// <summary>
    /// The `Type` property of the <c>XamlX.IL.SreTypeSystem.SreType</c> class.
    /// </summary>
    private static readonly PropertyInfo? s_xamlTypeProperty;

    /// <summary>
    /// The <see cref="IInjection"/> instance responsible for injecting the
    /// <see cref="OnNewSreMethodBuilder(MethodBuilder, IEnumerable{object})"/>
    /// callback.
    /// </summary>
    [SuppressMessage("CodeQuality", "IDE0052", Justification = "Injections must be kept alive.")]
    private static readonly IInjection? s_sreMethodBuilderInjection;

    /// <summary>
    /// Initializes static members of the <see cref="AvaloniaControlHelper"/> class.
    /// </summary>
    static AvaloniaControlHelper()
    {
        FieldInfo? stylesAppliedField = typeof(StyledElement).GetField("_stylesApplied", BindingFlags.NonPublic | BindingFlags.Instance);
        s_stylesAppliedField = stylesAppliedField?.FieldType == typeof(bool) ? stylesAppliedField : null;

        Assembly xamlLoaderAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
        Type? sreType = xamlLoaderAssembly.GetType("XamlX.IL.SreTypeSystem+SreType");
        s_xamlTypeProperty = sreType?.GetProperty("Type");

        BindingFlags ctorFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Type? sreMethodBuilder = xamlLoaderAssembly.GetType("XamlX.IL.SreTypeSystem+SreTypeBuilder+SreMethodBuilder");
        ConstructorInfo? sreMethodBuilderCtor = sreMethodBuilder?.GetConstructors(ctorFlags).FirstOrDefault(x => x.GetParameters().Length > 1);
        if (sreMethodBuilderCtor is not null)
            s_sreMethodBuilderInjection = CallbackInjector.Inject(sreMethodBuilderCtor, OnNewSreMethodBuilder);
    }

    /// <summary>
    /// Fixes newly created <c>XamlX.IL.SreTypeSystem.SreTypeBuilder.SreMethodBuilder</c>
    /// instances.
    /// </summary>
    /// <param name="methodBuilder">
    /// The <see cref="MethodBuilder"/> used to create the
    /// <c>SreMethodBuilder</c> instance.
    /// </param>
    /// <param name="parameters">
    /// The parameters used to create the <c>SreMethodBuilder</c>
    /// instance represented by a collection of <c>SreType</c> objects.
    /// </param>
    private static void OnNewSreMethodBuilder(MethodBuilder methodBuilder, IEnumerable<object> parameters)
    {
        // `Avalonia.Markup.Xaml.Loader` does not handle scenarios where
        // the control population logic needs to reference private members,
        // which are commonly used for subscribing to events (e.g., `Click`,
        // `TextChanged`, etc.). To circumvent this problem, we need this
        // callback to track the creation of dynamic `Populate` methods and
        // patch their containing assembly with `IgnoresAccessChecksToAttribute`.
        if (!AvaloniaRuntimeXamlScanner.IsDynamicPopulateMethod(methodBuilder))
            return;

        if (methodBuilder.DeclaringType?.Assembly is not AssemblyBuilder assembly)
            return;

        IEnumerable<Type> parameterTypes = parameters
            .Where(static x => x is not null && x.GetType() == s_xamlTypeProperty?.DeclaringType)
            .Select(static x => (Type?)s_xamlTypeProperty?.GetValue(x))
            .Where(static x => x is not null)!;

        foreach (Type parameterType in parameterTypes)
            assembly.AllowAccessTo(parameterType);
    }

    /// <summary>
    /// Loads an Avalonia control from XAML markup and initializes it.
    /// </summary>
    /// <param name="xaml">The XAML markup to load the control from.</param>
    /// <param name="uri">The URI that identifies the XAML source.</param>
    /// <param name="control">The optional control object to be populated.</param>
    /// <param name="compiledPopulateMethod">The newly compiled populate method, if the compilation was successful.</param>
    /// <returns>An object representing the loaded Avalonia control.</returns>
    /// <remarks>
    /// This method replaces static resources with their dynamic counterparts before loading the control.
    /// </remarks>
    public static object Load(string xaml, Uri uri, object? control, out MethodInfo? compiledPopulateMethod)
    {
        _ = xaml ?? throw new ArgumentNullException(nameof(xaml));
        _ = uri ?? throw new ArgumentNullException(nameof(uri));

        string xamlWithDynamicComponents = MakeStaticComponentsDynamic(xaml);
        HashSet<MethodInfo> oldPopulateMethods = new(AvaloniaRuntimeXamlScanner.FindDynamicPopulateMethods(uri));

        Clear(control);
        object loadedControl = AvaloniaRuntimeXamlLoader.Load(xamlWithDynamicComponents, null, control, uri, designMode: false);

        compiledPopulateMethod = AvaloniaRuntimeXamlScanner
                    .FindDynamicPopulateMethods(uri)
                    .FirstOrDefault(x => !oldPopulateMethods.Contains(x));

        return loadedControl;
    }

    /// <summary>
    /// Constructs a control using the provided build method.
    /// </summary>
    /// <param name="build">The method used to build the object.</param>
    /// <param name="serviceProvider">An optional service provider.</param>
    /// <returns>An object built using the specified method.</returns>
    public static object Build(MethodBase build, IServiceProvider? serviceProvider)
    {
        _ = build ?? throw new ArgumentNullException(nameof(build));

        object[]? args = build.IsConstructor ? null : new[] { serviceProvider ?? XamlIlRuntimeHelpers.CreateRootServiceProviderV2() };
        return build.Invoke(null, args) ?? throw new InvalidOperationException();
    }

    /// <summary>
    /// Populates an existing control with properties and elements.
    /// </summary>
    /// <param name="populate">The method used to populate the control.</param>
    /// <param name="serviceProvider">An optional service provider.</param>
    /// <param name="control">The control to populate.</param>
    public static void Populate(MethodBase populate, IServiceProvider? serviceProvider, object control)
    {
        _ = populate ?? throw new ArgumentNullException(nameof(populate));
        _ = control ?? throw new ArgumentNullException(nameof(control));

        object[] args = new[]
        {
            serviceProvider ?? XamlIlRuntimeHelpers.CreateRootServiceProviderV2(),
            control,
        };

        Clear(control);
        populate.Invoke(null, args);
    }

    /// <summary>
    /// Attempts to inject the given populate action into the specified field.
    /// </summary>
    /// <param name="populateOverride">The field to inject the new population logic into.</param>
    /// <param name="populate">The populate action to override the original one with.</param>
    /// <param name="injection">
    /// When this method returns, contains the <see cref="IInjection"/> instance if the injection was successful;
    /// otherwise, <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the injection was successful;
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool TryInjectPopulateOverride(
        FieldInfo populateOverride,
        Action<IServiceProvider, object> populate,
        [NotNullWhen(true)] out IInjection? injection)
    {
        _ = populateOverride ?? throw new ArgumentNullException(nameof(populateOverride));
        _ = populate ?? throw new ArgumentNullException(nameof(populate));

        if (!AvaloniaRuntimeXamlScanner.IsPopulateOverrideField(populateOverride))
        {
            injection = null;
            return false;
        }

        injection = new OverridePopulateInjection(populateOverride, populate);
        return true;
    }

    /// <summary>
    /// Clears resources, styles, and other data from an Avalonia control.
    /// </summary>
    /// <param name="control">The control to clear.</param>
    public static void Clear(object? control)
    {
        if (control is null)
            return;

        if (control is StyledElement)
            s_stylesAppliedField?.SetValue(control, false);

        if (control is IDictionary<object, object> dictionaryControl)
            dictionaryControl.Clear();

        if (control is ICollection<IStyle> styles)
            styles.Clear();

        if (control is Visual avaloniaControl)
        {
            avaloniaControl.Resources.Clear();
            avaloniaControl.Styles.Clear();
        }
    }

    /// <summary>
    /// Replaces all static resources with their dynamic counterparts within a XAML markup.
    /// </summary>
    /// <param name="xaml">The XAML markup containing static resources.</param>
    /// <returns>The XAML markup with static resources replaced by their dynamic counterparts.</returns>
    private static string MakeStaticComponentsDynamic(string xaml)
    {
        const string staticResourceName = "\"{StaticResource ";
        const string dynamicResourceName = "\"{DynamicResource ";

        return xaml.Replace(staticResourceName, dynamicResourceName);
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
file sealed class OverridePopulateInjection : IInjection
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
