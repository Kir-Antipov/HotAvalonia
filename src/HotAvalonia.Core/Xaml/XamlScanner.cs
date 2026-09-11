using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HotAvalonia.Helpers;
using HotAvalonia.Reflection;

namespace HotAvalonia.Xaml;

/// <summary>
/// Provides utility methods for identifying and extracting information about Avalonia controls.
/// </summary>
public static class XamlScanner
{
    /// <summary>
    /// Determines whether the specified assembly uses compiled bindings by default.
    /// </summary>
    /// <param name="assembly">The assembly to check for the compiled bindings metadata attribute.</param>
    /// <returns>
    /// <c>true</c> if the assembly specifies the <c>AvaloniaUseCompiledBindingsByDefault</c>
    /// metadata attribute and its value is set to <c>true</c>; otherwise, <c>false</c>.
    /// </returns>
    public static bool UsesCompiledBindingsByDefault(Assembly? assembly)
    {
        if (assembly is null)
            return false;

        foreach (AssemblyMetadataAttribute attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!"AvaloniaUseCompiledBindingsByDefault".Equals(attribute.Key, StringComparison.Ordinal))
                continue;

            return bool.TryParse(attribute.Value, out bool value) && value;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a method qualifies as a build method.
    /// </summary>
    /// <param name="method">The method to check.</param>
    /// <returns><c>true</c> if the method is a valid build method; otherwise, <c>false</c>.</returns>
    public static bool IsBuildMethod([NotNullWhen(true)] MethodBase? method)
        => method is { IsConstructor: true } || method is MethodInfo { ReturnType: { IsValueType: false } t } && t != typeof(void);

    /// <summary>
    /// Determines whether a method qualifies as a populate method.
    /// </summary>
    /// <param name="method">The method to check.</param>
    /// <returns><c>true</c> if the method is a valid populate method; otherwise, <c>false</c>.</returns>
    public static bool IsPopulateMethod([NotNullWhen(true)] MethodBase? method)
        => method?.GetParameters() is [{ ParameterType: Type t }, { ParameterType.IsValueType: false }] && t.IsAssignableFrom(typeof(IServiceProvider));

    /// <summary>
    /// Determines whether a field qualifies as a populate override.
    /// </summary>
    /// <param name="field">The field to check.</param>
    /// <returns><c>true</c> if the field is a valid populate override; otherwise, <c>false</c>.</returns>
    public static bool IsPopulateOverrideField([NotNullWhen(true)] FieldInfo? field)
        => field is { IsStatic: true, IsInitOnly: false, FieldType: Type t } && (t == typeof(Action<object>) || t == typeof(Action<IServiceProvider?, object>));

    /// <summary>
    /// Attempts to extract the URI associated with the XAML document
    /// represented by the given control instance.
    /// </summary>
    /// <param name="rootControl">The root control instance.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    public static bool TryExtractDocumentUri([NotNullWhen(true)] object? rootControl, [NotNullWhen(true)] out Uri? uri)
        => TryExtractDocumentUri(rootControl?.GetType(), out uri);

    /// <inheritdoc cref="TryExtractDocumentUri(object?, out Uri?)"/>
    [Obsolete("Use 'TryExtractDocumentUri(object?, out Uri?)' instead.")]
    public static bool TryExtractDocumentUri([NotNullWhen(true)] object? rootControl, [NotNullWhen(true)] out string? uri)
        => TryExtractDocumentUri(rootControl?.GetType(), out uri);

    /// <summary>
    /// Attempts to extract the URI associated with the XAML document
    /// represented by it's root control type.
    /// </summary>
    /// <param name="rootControlType">The root control type.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    public static bool TryExtractDocumentUri([NotNullWhen(true)] Type? rootControlType, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (rootControlType is null)
            return false;

        MethodInfo? populate = FindPopulateControlMethod(rootControlType);
        return populate is not null && TryExtractDocumentUri(populate, out uri);
    }

    /// <inheritdoc cref="TryExtractDocumentUri(Type?, out Uri?)"/>
    [Obsolete("Use 'TryExtractDocumentUri(Type?, out Uri?)' instead.")]
    public static bool TryExtractDocumentUri([NotNullWhen(true)] Type? rootControlType, [NotNullWhen(true)] out string? uri)
    {
        if (TryExtractDocumentUri(rootControlType, out Uri? wellFormedUri))
        {
            uri = wellFormedUri.ToString();
            return true;
        }
        else
        {
            uri = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to extract the URI from the given populate method.
    /// </summary>
    /// <param name="populateMethod">The populate method.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    private static bool TryExtractDocumentUri(MethodInfo populateMethod, [NotNullWhen(true)] out Uri? uri)
    {
        // "Populate" methods created by Avalonia usually start like this:
        // IL_0000: ldarg.0
        // IL_0001: ldc.i4.1
        // IL_0002: newarr [System.Runtime]System.Object
        // IL_0007: dup
        // IL_0008: ldc.i4.0
        // IL_0009: ldsfld class [Avalonia.Markup.Xaml]Avalonia.Markup.Xaml.XamlIl.Runtime.IAvaloniaXamlIlXmlNamespaceInfoProvider 'CompiledAvaloniaXaml.!AvaloniaResources'/'NamespaceInfo:/FILENAME'::Singleton
        // IL_000e: castclass [System.Runtime]System.Object
        // IL_0013: stelem.ref
        // IL_0014: ldstr "avares://uri" // <-- This is what we are looking for
        const int CommonLdstrLocation = 0x14;

        uri = null;
        byte[]? methodBody = populateMethod.GetMethodBody()?.GetILAsByteArray();
        if (methodBody is null)
            return false;

        int ldstrLocation = methodBody.Length > CommonLdstrLocation && methodBody[CommonLdstrLocation] == OpCodes.Ldstr.Value
            ? CommonLdstrLocation
            : MethodBodyReader.IndexOf(methodBody, OpCodes.Ldstr.Value);

        int uriTokenLocation = ldstrLocation + 1;
        if (uriTokenLocation is 0 || uriTokenLocation + sizeof(int) > methodBody.Length)
            return false;

        try
        {
            int uriToken = BitConverter.ToInt32(methodBody.AsSpan(uriTokenLocation));
            return Uri.TryCreate(populateMethod.Module.ResolveString(uriToken), UriKind.Absolute, out uri);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns compiled XAML documents located in the given assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan for pre-compiled XAML.</param>
    /// <returns>An enumerable containing compiled XAML documents.</returns>
    public static IEnumerable<CompiledXamlDocument> GetDocuments(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        Type? xamlLoader = assembly.GetType("CompiledAvaloniaXaml.!XamlLoader");
        MethodInfo? tryLoad = xamlLoader?.GetStaticMethods("TryLoad").OrderByDescending(x => x.GetParameters().Length).FirstOrDefault();
        byte[]? tryLoadBody = tryLoad?.GetMethodBody()?.GetILAsByteArray();
        if (tryLoad is null || tryLoadBody is null)
            return [];

        IEnumerable<CompiledXamlDocument> extractedDocuments = ExtractDocuments(tryLoadBody, tryLoad.Module);
        IEnumerable<CompiledXamlDocument> foundDocuments = FindDocuments(assembly);
        return extractedDocuments.Concat(foundDocuments).Distinct();
    }

    private static IEnumerable<CompiledXamlDocument> ExtractDocuments(ReadOnlyMemory<byte> methodBody, Module module)
    {
        MethodBodyReader reader = new(methodBody);
        string? str = null;
        Uri? uri = null;

        while (reader.Next())
        {
            if (reader.OpCode == OpCodes.Ret)
            {
                (str, uri) = (null, null);
                continue;
            }

            if (reader.OpCode == OpCodes.Ldstr)
            {
                str = reader.ResolveString(module);
                continue;
            }

            if (reader.OpCode != OpCodes.Call && reader.OpCode != OpCodes.Newobj)
                continue;

            MethodBase method = reader.ResolveMethod(module);
            if (method.DeclaringType == typeof(string) && method.Name is nameof(string.Equals))
            {
                uri = Uri.TryCreate(str, UriKind.Absolute, out uri) ? uri : null;
                str = null;
                continue;
            }

            if (uri is null || !IsBuildMethod(method) || !TryCreatePopulateDelegate(FindPopulateMethod(method), out Action<IServiceProvider?, object>? populateDelegate))
                continue;

            yield return new(uri, method, populateDelegate, FindPopulateOverrideField(method), GetControlRefreshCallback(method));
            (str, uri) = (null, null);
        }
    }

    private static IEnumerable<CompiledXamlDocument> FindDocuments(Assembly assembly)
    {
        foreach (Type type in assembly.GetLoadedTypes())
        {
            MethodInfo? populateMethod = FindPopulateControlMethod(type);
            if (!TryCreatePopulateDelegate(populateMethod, out Action<IServiceProvider?, object>? populateDelegate))
                continue;

            if (!TryExtractDocumentUri(populateMethod, out Uri? uri))
                continue;

            if (GetControlConstructor(type) is not ConstructorInfo buildMethod)
                continue;

            FieldInfo? populateOverrideField = FindPopulateOverrideField(buildMethod);
            Action<object> refresh = GetControlRefreshCallback(buildMethod);
            yield return new(uri, buildMethod, populateDelegate, populateOverrideField, refresh);
        }
    }

    private static IEnumerable<NamedControlReference> FindNamedControlReferences(MethodBase buildMethod)
    {
        if (buildMethod is not { IsConstructor: true, DeclaringType: Type declaringType })
            return [];

        MethodInfo? initializeComponent = declaringType
            .GetInstanceMethods("InitializeComponent")
            .OrderByDescending(static x => x.IsGeneratedByAvalonia())
            .ThenByDescending(static x => x.GetParameters().Length)
            .FirstOrDefault(static x => x.ReturnType == typeof(void));

        byte[]? initializeComponentBody = initializeComponent?.GetMethodBody()?.GetILAsByteArray();
        if (initializeComponent is null || initializeComponentBody is null)
            return [];

        return ExtractNamedControlReferences(initializeComponentBody, initializeComponent.Module);
    }

    private static IEnumerable<NamedControlReference> ExtractNamedControlReferences(ReadOnlyMemory<byte> methodBody, Module module)
    {
        ArgumentNullException.ThrowIfNull(module);

        MethodBodyReader reader = new(methodBody);
        while (reader.Next())
        {
            if (reader.OpCode != OpCodes.Ldstr)
                continue;

            string name = reader.ResolveString(module);
            if (!reader.Next() || reader.OpCode != OpCodes.Call)
                continue;

            MethodBase findMethod = reader.ResolveMethod(module);
            if (!reader.Next() || reader.OpCode != OpCodes.Stfld)
                continue;

            // T Avalonia.Controls.NameScopeExtensions.Find<T>(INameScope, string)
            FieldInfo field = reader.ResolveField(module);
            if (!"Find".Equals(findMethod.Name, StringComparison.Ordinal))
                continue;

            Type[] genericArguments = findMethod.IsGenericMethod ? findMethod.GetGenericArguments() : Type.EmptyTypes;
            Type controlType = genericArguments.Length > 0 && field.FieldType.IsAssignableFrom(genericArguments[^1])
                ? genericArguments[^1]
                : field.FieldType;

            yield return new(name, controlType, field);
        }
    }

    private static IEnumerable<MethodInfo> FindAvaloniaHotReloadCallbacks(MethodBase buildMethod)
    {
        if (buildMethod is not { IsConstructor: true, DeclaringType: Type declaringType })
            return [];

        return declaringType
            .GetInstanceMethods()
            .Where(static x => x.GetParameters().Length == 0)
            .Where(static x => x.Name == "InitializeComponentState" || x.GetCustomAttributes(inherit: true)
                .Any(static y => "HotAvalonia.AvaloniaHotReloadAttribute".Equals(y?.GetType().FullName, StringComparison.Ordinal)));
    }

    private static Action<object> GetControlRefreshCallback(MethodBase buildMethod)
    {
        Action<object>[] callbacks = FindNamedControlReferences(buildMethod)
            .Select(static x => (Action<object>)x.Refresh)
            .Concat(FindAvaloniaHotReloadCallbacks(buildMethod)
            .Select(static x => x.CreateUnsafeDelegate<Action<object>>()))
            .ToArray();

        if (callbacks.Length == 0)
            return static x => { };

        return (Action<object>)Delegate.Combine(callbacks);
    }

    private static FieldInfo? FindPopulateOverrideField(MethodBase buildMethod)
    {
        if (buildMethod.DeclaringType is not Type declaringType)
            return null;

        int separatorIndex = buildMethod.Name.IndexOf(':');
        string populateName = separatorIndex >= 0
            ? $"PopulateOverride{buildMethod.Name.Substring(separatorIndex)}"
            : "!XamlIlPopulateOverride";

        FieldInfo? field = declaringType.GetStaticField(populateName);
        return IsPopulateOverrideField(field) ? field : null;
    }

    private static MethodInfo? FindPopulateMethod(MethodBase buildMethod)
    {
        if (buildMethod.DeclaringType is not Type declaringType)
            return null;

        int separatorIndex = buildMethod.Name.IndexOf(':');
        if (separatorIndex < 0)
            return FindPopulateControlMethod(declaringType);

        string populateName = $"Populate{buildMethod.Name.Substring(separatorIndex)}";
        return declaringType.GetStaticMethods(populateName).FirstOrDefault(IsPopulateMethod);
    }

    private static MethodInfo? FindPopulateControlMethod(Type userControlType)
        => userControlType.GetStaticMethod("!XamlIlPopulate", [typeof(IServiceProvider), userControlType]);

    internal static ConstructorInfo? GetControlConstructor(Type userControlType)
        => userControlType.GetInstanceConstructor() ?? userControlType.GetInstanceConstructor([typeof(IServiceProvider)]) ?? userControlType.GetInstanceConstructors().OrderBy(static x => x.GetParameters().Length).FirstOrDefault();

    private static bool TryCreatePopulateDelegate([NotNullWhen(true)] MethodInfo? populateMethod, [NotNullWhen(true)] out Action<IServiceProvider?, object>? populateDelegate)
    {
        if (populateMethod is not null)
        {
            try
            {
                populateDelegate = populateMethod.CreateUnsafeDelegate<Action<IServiceProvider?, object>>();
                return true;
            }
            catch
            {
                // If the provided method contains malformed IL, attempting to create a delegate from it
                // will result in an exception. Simply ignore such methods, as controls that define them
                // cannot be used in a functioning codebase anyway.
            }
        }
        populateDelegate = null;
        return false;
    }

    /// <summary>
    /// Determines whether the specified member is generated by Avalonia.
    /// </summary>
    /// <param name="member">The member to check.</param>
    /// <returns>
    /// <c>true</c> if the specified member is generated by Avalonia;
    /// otherwise, <c>false</c>.
    /// </returns>
    public static bool IsGeneratedByAvalonia(this MemberInfo member)
    {
        GeneratedCodeAttribute? generatedCodeAttribute = member?.GetCustomAttribute<GeneratedCodeAttribute>();
        if (generatedCodeAttribute is not { Tool: not null })
            return false;

        return generatedCodeAttribute.Tool.StartsWith("Avalonia.Generators.", StringComparison.Ordinal);
    }
}
