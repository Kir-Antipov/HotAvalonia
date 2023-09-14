using System.Reflection;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Styling;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for manipulating and obtaining information about Avalonia controls.
/// </summary>
internal static class AvaloniaControlHelper
{
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

    /// <inheritdoc cref="TryOverridePopulate(FieldInfo, Action{IServiceProvider, object})"/>
    public static bool TryOverridePopulate(FieldInfo populateOverride, Action<object> populate)
    {
        _ = populateOverride ?? throw new ArgumentNullException(nameof(populateOverride));
        _ = populate ?? throw new ArgumentNullException(nameof(populate));

        if (populateOverride.FieldType == typeof(Action<object>))
        {
            populateOverride.SetValue(null, (Action<object>)PopulateAndOverride);
            return true;
        }
        else if (populateOverride.FieldType == typeof(Action<IServiceProvider?, object>))
        {
            populateOverride.SetValue(null, (Action<IServiceProvider?, object>)PopulateAndOverrideWithServiceProvider);
            return true;
        }

        return false;

        void PopulateAndOverride(object control)
        {
            populate(control);
            populateOverride.SetValue(null, (Action<object>)PopulateAndOverride);
        }

        void PopulateAndOverrideWithServiceProvider(IServiceProvider? serviceProvider, object control)
        {
            populate(control);
            populateOverride.SetValue(null, (Action<IServiceProvider?, object>)PopulateAndOverrideWithServiceProvider);
        }
    }

    /// <summary>
    /// Attempts to inject the given populate action into a given field.
    /// </summary>
    /// <param name="populateOverride">The field information to inject the new populate action into.</param>
    /// <param name="populate">The populate action to override the original one with.</param>
    /// <returns><c>true</c> if the override was successful; otherwise, <c>false</c>.</returns>
    public static bool TryOverridePopulate(FieldInfo populateOverride, Action<IServiceProvider, object> populate)
    {
        _ = populateOverride ?? throw new ArgumentNullException(nameof(populateOverride));
        _ = populate ?? throw new ArgumentNullException(nameof(populate));

        if (populateOverride.FieldType == typeof(Action<object>))
        {
            populateOverride.SetValue(null, (Action<object>)PopulateAndOverride);
            return true;
        }
        else if (populateOverride.FieldType == typeof(Action<IServiceProvider?, object>))
        {
            populateOverride.SetValue(null, (Action<IServiceProvider?, object>)PopulateAndOverrideWithServiceProvider);
            return true;
        }

        return false;

        void PopulateAndOverride(object control)
        {
            populate(XamlIlRuntimeHelpers.CreateRootServiceProviderV2(), control);
            populateOverride.SetValue(null, (Action<object>)PopulateAndOverride);
        }

        void PopulateAndOverrideWithServiceProvider(IServiceProvider? serviceProvider, object control)
        {
            serviceProvider ??= XamlIlRuntimeHelpers.CreateRootServiceProviderV2();

            populate(serviceProvider, control);
            populateOverride.SetValue(null, (Action<IServiceProvider?, object>)PopulateAndOverrideWithServiceProvider);
        }
    }

    /// <summary>
    /// Clears resources, styles, and other data from an Avalonia control.
    /// </summary>
    /// <param name="control">The control to clear.</param>
    public static void Clear(object? control)
    {
        if (control is null)
            return;

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
        const string staticResourceName = "{StaticResource ";
        const string dynamicResourceName = "{DynamicResource ";

        return xaml.Replace(staticResourceName, dynamicResourceName);
    }
}
