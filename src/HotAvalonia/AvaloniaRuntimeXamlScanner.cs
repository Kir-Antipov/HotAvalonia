using System.CodeDom.Compiler;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Markup.Xaml;
using HotAvalonia.Helpers;
using HotAvalonia.Reflection;

namespace HotAvalonia;

/// <summary>
/// Provides utility methods for identifying and extracting information about Avalonia controls.
/// </summary>
public static class AvaloniaRuntimeXamlScanner
{
    /// <summary>
    /// The expected parameter types for a valid build method.
    /// </summary>
    private static readonly Type[] s_buildSignature = [typeof(IServiceProvider)];

    /// <summary>
    /// The expected parameter types for a valid populate method.
    /// </summary>
    private static readonly Type[] s_populateSignature = [typeof(IServiceProvider), typeof(object)];

    /// <summary>
    /// The dynamically generated assembly containing compiled XAML.
    /// </summary>
    private static DynamicAssembly? s_dynamicXamlAssembly;

    /// <summary>
    /// The dynamically generated assembly containing compiled XAML.
    /// </summary>
    public static DynamicAssembly? DynamicXamlAssembly => s_dynamicXamlAssembly ??= GetDynamicXamlAssembly();

    /// <summary>
    /// Retrieves the dynamically generated assembly containing compiled XAML.
    /// </summary>
    /// <returns>The dynamically generated assembly, if any; otherwise, <c>null</c>.</returns>
    private static DynamicAssembly? GetDynamicXamlAssembly()
    {
        Assembly xamlLoaderAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
        Type xamlAssembly = xamlLoaderAssembly.GetType("XamlX.TypeSystem.IXamlAssembly") ?? typeof(object);
        Type? xamlIlRuntimeCompiler = xamlLoaderAssembly.GetType("Avalonia.Markup.Xaml.XamlIl.AvaloniaXamlIlRuntimeCompiler");

        MethodInfo? initializeSre = xamlIlRuntimeCompiler?.GetStaticMethod("InitializeSre", Type.EmptyTypes);
        initializeSre?.Invoke(null, null);

        object? sreAsm = xamlIlRuntimeCompiler?.GetStaticField("_sreAsm")?.GetValue(null);
        object? sreTypeSystem = xamlIlRuntimeCompiler?.GetStaticField("_sreTypeSystem")?.GetValue(null);
        if (sreAsm is not Assembly asm || sreTypeSystem is null)
            return null;

        DynamicAssembly dynamicAssembly = DynamicSreAssembly.Create(asm, sreTypeSystem);
        object? sreTypeSystemAssemblies = sreTypeSystem.GetType().GetInstanceField("_assemblies")?.GetValue(sreTypeSystem);
        MethodInfo? addAssembly = sreTypeSystemAssemblies?.GetType().GetMethod(nameof(List<string>.Add), [xamlAssembly]);
        if (xamlAssembly.IsAssignableFrom(dynamicAssembly.GetType()))
            addAssembly?.Invoke(sreTypeSystemAssemblies, [dynamicAssembly]);

        return dynamicAssembly;
    }

    /// <summary>
    /// Determines whether a method qualifies as a build method.
    /// </summary>
    /// <param name="method">The method to check.</param>
    /// <returns><c>true</c> if the method is a valid build method; otherwise, <c>false</c>.</returns>
    public static bool IsBuildMethod([NotNullWhen(true)] MethodBase? method)
    {
        if (method is null)
            return false;

        return method.IsConstructor && method.GetParameters().Length is 0 || MethodHelper.IsSignatureAssignableFrom(s_buildSignature, method);
    }

    /// <summary>
    /// Determines whether a method qualifies as a populate method.
    /// </summary>
    /// <param name="method">The method to check.</param>
    /// <returns><c>true</c> if the method is a valid populate method; otherwise, <c>false</c>.</returns>
    public static bool IsPopulateMethod([NotNullWhen(true)] MethodBase? method)
    {
        if (method is null)
            return false;

        return MethodHelper.IsSignatureAssignableFrom(s_populateSignature, method);
    }

    /// <summary>
    /// Determines whether a field qualifies as a populate override.
    /// </summary>
    /// <param name="field">The field to check.</param>
    /// <returns><c>true</c> if the field is a valid populate override; otherwise, <c>false</c>.</returns>
    public static bool IsPopulateOverrideField([NotNullWhen(true)] FieldInfo? field)
    {
        if (field is not { IsStatic: true, IsInitOnly: false })
            return false;

        return field.FieldType == typeof(Action<object>) || field.FieldType == typeof(Action<IServiceProvider?, object>);
    }

    /// <summary>
    /// Attempts to extract the URI associated with the given control instance.
    /// </summary>
    /// <param name="control">The control instance.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    public static bool TryExtractControlUri(object? control, [NotNullWhen(true)] out Uri? uri)
        => TryExtractControlUri(control?.GetType(), out uri);

    /// <inheritdoc cref="TryExtractControlUri(object?, out Uri?)"/>
    public static bool TryExtractControlUri(object? control, [NotNullWhen(true)] out string? uri)
        => TryExtractControlUri(control?.GetType(), out uri);

    /// <summary>
    /// Attempts to extract the URI associated with the given control type.
    /// </summary>
    /// <param name="controlType">The control type.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    public static bool TryExtractControlUri(Type? controlType, [NotNullWhen(true)] out Uri? uri)
    {
        if (TryExtractControlUri(controlType, out string? uriStr))
        {
            uri = new(uriStr);
            return true;
        }
        else
        {
            uri = null;
            return false;
        }
    }

    /// <inheritdoc cref="TryExtractControlUri(Type?, out Uri?)"/>
    public static bool TryExtractControlUri(Type? controlType, [NotNullWhen(true)] out string? uri)
    {
        uri = null;
        if (controlType is null)
            return false;

        MethodInfo? populate = FindPopulateControlMethod(controlType);
        return populate is not null && TryExtractControlUri(populate, out uri);
    }

    /// <summary>
    /// Attempts to extract the URI from the given populate method.
    /// </summary>
    /// <param name="populateMethod">The populate method.</param>
    /// <param name="uri">The output parameter that receives the associated URI.</param>
    /// <returns><c>true</c> if the URI is successfully extracted; otherwise, <c>false</c>.</returns>
    private static bool TryExtractControlUri(MethodInfo populateMethod, [NotNullWhen(true)] out string? uri)
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
            int inlineStringToken = BitConverter.ToInt32(methodBody, uriTokenLocation);
            uri = populateMethod.Module.ResolveString(inlineStringToken);
            return uri is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Discovers Avalonia controls present in a given assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan for Avalonia controls.</param>
    /// <returns>An enumerable containing information about discovered Avalonia controls.</returns>
    public static IEnumerable<AvaloniaControlInfo> FindAvaloniaControls(Assembly assembly)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        Type? xamlLoader = assembly.GetType("CompiledAvaloniaXaml.!XamlLoader");
        MethodInfo? tryLoad = xamlLoader?.GetStaticMethods("TryLoad").OrderByDescending(x => x.GetParameters().Length).FirstOrDefault();
        byte[]? tryLoadBody = tryLoad?.GetMethodBody()?.GetILAsByteArray();
        if (tryLoad is null || tryLoadBody is null)
            return [];

        IEnumerable<AvaloniaControlInfo> extractedControls = ExtractAvaloniaControls(tryLoadBody, tryLoad.Module);
        IEnumerable<AvaloniaControlInfo> scannedControls = ScanAvaloniaControls(assembly);
        return extractedControls.Concat(scannedControls).Distinct();
    }

    /// <summary>
    /// Extracts Avalonia control information from the IL of the given method body.
    /// </summary>
    /// <param name="methodBody">The IL method body to scan.</param>
    /// <param name="module">The module containing the method body.</param>
    /// <returns>An enumerable containing information about extracted Avalonia controls.</returns>
    private static IEnumerable<AvaloniaControlInfo> ExtractAvaloniaControls(ReadOnlyMemory<byte> methodBody, Module module)
    {
        MethodBodyReader reader = new(methodBody);
        string? str = null;
        string? uri = null;

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
                uri = str;
                str = null;
                continue;
            }

            if (uri is null || !IsBuildMethod(method))
                continue;

            MethodInfo? populateMethod = FindPopulateMethod(method);
            FieldInfo? populateOverrideField = FindPopulateOverrideField(method);
            Action<object> refresh = GetControlRefreshCallback(method);
            if (populateMethod is null)
                continue;

            yield return new(uri, method, populateMethod, populateOverrideField, refresh);
            (str, uri) = (null, null);
        }
    }

    /// <summary>
    /// Scans the specified assembly for Avalonia controls.
    /// </summary>
    /// <param name="assembly">The assembly to scan for Avalonia controls.</param>
    /// <returns>An enumerable containing information about the discovered Avalonia controls.</returns>
    private static IEnumerable<AvaloniaControlInfo> ScanAvaloniaControls(Assembly assembly)
    {
        foreach (Type type in assembly.GetLoadedTypes())
        {
            MethodInfo? populateMethod = FindPopulateControlMethod(type);
            if (populateMethod is null)
                continue;

            if (!TryExtractControlUri(populateMethod, out string? uri))
                continue;

            MethodBase? buildMethod = type.GetInstanceConstructor();
            if (buildMethod is null)
                continue;

            FieldInfo? populateOverrideField = FindPopulateOverrideField(buildMethod);
            Action<object> refresh = GetControlRefreshCallback(buildMethod);
            yield return new(uri, buildMethod, populateMethod, populateOverrideField, refresh);
        }
    }

    /// <summary>
    /// Discovers named control references within the Avalonia control associated with the given build method.
    /// </summary>
    /// <param name="buildMethod">The build method associated with the control scope to search within.</param>
    /// <returns>An enumerable containing discovered named control references.</returns>
    private static IEnumerable<AvaloniaNamedControlReference> FindAvaloniaNamedControlReferences(MethodBase buildMethod)
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

        return ExtractAvaloniaNamedControlReferences(initializeComponentBody, initializeComponent.Module);
    }

    /// <summary>
    /// Extracts named control references from the IL of the given method body.
    /// </summary>
    /// <param name="methodBody">The IL method body to scan.</param>
    /// <param name="module">The module containing the method body.</param>
    /// <returns>An enumerable containing extracted named control references.</returns>
    private static IEnumerable<AvaloniaNamedControlReference> ExtractAvaloniaNamedControlReferences(ReadOnlyMemory<byte> methodBody, Module module)
    {
        // T Avalonia.Controls.NameScopeExtensions.Find<T>(INameScope, string)
        const string findMethodName = "Find";

        _ = module ?? throw new ArgumentNullException(nameof(module));

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

            FieldInfo field = reader.ResolveField(module);
            if (!findMethod.Name.Equals(findMethodName, StringComparison.Ordinal))
                continue;

            Type[] genericArguments = findMethod.IsGenericMethod ? findMethod.GetGenericArguments() : Type.EmptyTypes;
            Type controlType = genericArguments.Length > 0 && field.FieldType.IsAssignableFrom(genericArguments[genericArguments.Length - 1])
                ? genericArguments[genericArguments.Length - 1]
                : field.FieldType;

            yield return new(name, controlType, field);
        }
    }

    /// <summary>
    /// Finds all parameterless instance methods within the specified control
    /// that are decorated with the <c>AvaloniaHotReloadAttribute</c>.
    /// </summary>
    /// <param name="userControlType">The type to inspect for hot reload callback methods.</param>
    /// <returns>
    /// A collection of <see cref="MethodInfo"/> representing all parameterless instance methods
    /// within the provided control that are decorated with the <c>AvaloniaHotReloadAttribute</c>.
    /// </returns>
    private static IEnumerable<MethodInfo> FindAvaloniaHotReloadCallbacks(MethodBase buildMethod)
    {
        if (buildMethod is not { IsConstructor: true, DeclaringType: Type declaringType })
            return [];

        return declaringType
            .GetInstanceMethods()
            .Where(static x => x.GetParameters().Length == 0)
            .Where(static x => x.GetCustomAttributes(inherit: true)
                .Any(static y => "HotAvalonia.AvaloniaHotReloadAttribute".Equals(y?.GetType().FullName, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Constructs a combined refresh callback for a control, aggregating
    /// hot reload methods and named control refresh actions.
    /// </summary>
    /// <param name="buildMethod">The build method associated with the control.</param>
    /// <returns>
    /// A delegate that, when invoked, executes all associated refresh actions for
    /// the control, including hot reload callbacks and named control refresh methods.
    /// </returns>
    private static Action<object> GetControlRefreshCallback(MethodBase buildMethod)
    {
        Action<object>[] callbacks = FindAvaloniaNamedControlReferences(buildMethod)
            .Select(static x => (Action<object>)x.Refresh)
            .Concat(FindAvaloniaHotReloadCallbacks(buildMethod)
            .Select(static x => x.CreateUnsafeDelegate<Action<object>>()))
            .ToArray();

        if (callbacks.Length == 0)
            return static x => { };

        return (Action<object>)Delegate.Combine(callbacks);
    }

    /// <summary>
    /// Finds the populate override field in relation to the given build method.
    /// </summary>
    /// <param name="buildMethod">The build method for which the field is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the field, or <c>null</c> if not found.</returns>
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

    /// <summary>
    /// Finds the populate method in relation to the given build method.
    /// </summary>
    /// <param name="buildMethod">The build method for which the populate method is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the populate method, or <c>null</c> if not found.</returns>
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

    /// <summary>
    /// Finds the populate method for a user control.
    /// </summary>
    /// <param name="userControlType">The type of the user control for which the populate method is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the populate method, or <c>null</c> if not found.</returns>
    private static MethodInfo? FindPopulateControlMethod(Type userControlType)
        => userControlType.GetStaticMethod("!XamlIlPopulate", [typeof(IServiceProvider), userControlType]);

    /// <inheritdoc cref="FindDynamicPopulateMethods(string)"/>
    internal static IEnumerable<MethodInfo> FindDynamicPopulateMethods(Uri uri)
        => FindDynamicPopulateMethods(uri?.ToString()!);

    /// <summary>
    /// Searches for populate methods associated with the specified URI within the dynamic XAML assembly.
    /// </summary>
    /// <param name="uri">The URI associated with the Avalonia control/resource to be populated.</param>
    /// <returns>An enumerable containing dynamic populate methods corresponding to the specified URI.</returns>
    internal static IEnumerable<MethodInfo> FindDynamicPopulateMethods(string uri)
    {
        Assembly? dynamicXamlAssembly = DynamicXamlAssembly?.Assembly;
        if (dynamicXamlAssembly is null)
            yield break;

        string safeUri = UriHelper.GetSafeUriIdentifier(uri);
        foreach (Type type in dynamicXamlAssembly.GetLoadedTypes())
        {
            if (!type.Name.StartsWith("Builder_", StringComparison.Ordinal))
                continue;

            if (!type.Name.EndsWith(safeUri, StringComparison.Ordinal))
                continue;

            MethodInfo? populateMethod = type.GetStaticMethod("__AvaloniaXamlIlPopulate");
            if (IsPopulateMethod(populateMethod))
                yield return populateMethod;
        }
    }

    /// <summary>
    /// Determines whether the specified member is generated by Avalonia.
    /// </summary>
    /// <param name="member">The member to check.</param>
    /// <returns>
    /// <c>true</c> if the specified member is generated by Avalonia;
    /// otherwise, <c>false</c>.
    /// </returns>
    private static bool IsGeneratedByAvalonia(this MemberInfo member)
    {
        GeneratedCodeAttribute? generatedCodeAttribute = member.GetCustomAttribute<GeneratedCodeAttribute>();
        if (generatedCodeAttribute is not { Tool: not null })
            return false;

        return generatedCodeAttribute.Tool.StartsWith("Avalonia.Generators.", StringComparison.Ordinal);
    }
}
