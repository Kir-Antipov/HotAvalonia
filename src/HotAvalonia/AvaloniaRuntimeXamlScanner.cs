using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using HotAvalonia.Helpers;
using HotAvalonia.Reflection;

namespace HotAvalonia;

/// <summary>
/// Provides utility methods for identifying and extracting information about Avalonia controls.
/// </summary>
public static class AvaloniaRuntimeXamlScanner
{
    /// <summary>
    /// The prefix used for identifying methods responsible for building resources.
    /// </summary>
    private const string BuildResourceMethodNamePrefix = "Build:";

    /// <summary>
    /// The prefix used for identifying methods responsible for populating resources.
    /// </summary>
    private const string PopulateResourceMethodNamePrefix = "Populate:";

    /// <summary>
    /// The prefix used for identifying fields responsible for overriding methods that populate resources.
    /// </summary>
    private const string PopulateOverrideResourceFieldNamePrefix = "PopulateOverride:";

    /// <summary>
    /// The prefix used for identifying methods responsible for populating controls.
    /// </summary>
    private const string PopulateControlMethodNamePrefix = "!XamlIlPopulate";

    /// <summary>
    /// The name used for identifying fields responsible for overriding methods that populate controls.
    /// </summary>
    private const string PopulateOverrideControlFieldName = "!XamlIlPopulateOverride";

    /// <summary>
    /// Represents binding flags for a static member, either public or non-public.
    /// </summary>
    private const BindingFlags StaticMember = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// The expected parameter types for a valid build method.
    /// </summary>
    private static readonly Type[] s_buildSignature = new[] { typeof(IServiceProvider) };

    /// <summary>
    /// The expected parameter types for a valid populate method.
    /// </summary>
    private static readonly Type[] s_populateSignature = new[] { typeof(IServiceProvider), typeof(object) };

    /// <summary>
    /// The dynamically generated assembly containing compiled XAML.
    /// </summary>
    private static Assembly? s_dynamicXamlAssembly;

    /// <summary>
    /// The dynamically generated assembly containing compiled XAML.
    /// </summary>
    public static Assembly? DynamicXamlAssembly => s_dynamicXamlAssembly ??= FindDynamicXamlAssembly();

    /// <summary>
    /// Locates the dynamically generated assembly containing compiled XAML within the current application domain.
    /// </summary>
    /// <returns>The dynamically generated assembly if found; otherwise, <c>null</c>.</returns>
    private static Assembly? FindDynamicXamlAssembly()
    {
        const string dynamicXamlAssemblyMarkerTypeName = "XamlIlContext";
        const int stringifiedGuidLength = 32; // Guid.NewGuid().ToString("N").Length

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic)
                continue;

            string name = assembly.GetName().Name;
            if (name.Length is not stringifiedGuidLength || !Guid.TryParse(name, out _))
                continue;

            if (assembly.GetLoadedTypes().Any(x => x.Name is dynamicXamlAssemblyMarkerTypeName))
                return assembly;
        }

        return null;
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
        const int commonLdstrLocation = 0x14;

        uri = null;

        byte[]? methodBody = populateMethod.GetMethodBody()?.GetILAsByteArray();
        if (methodBody is null)
            return false;

        int ldstrLocation = methodBody.Length > commonLdstrLocation && methodBody[commonLdstrLocation] == OpCodes.Ldstr.Value
            ? commonLdstrLocation
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

        MethodInfo? tryLoad = FindTryLoadMethod(assembly);
        byte[]? tryLoadBody = tryLoad?.GetMethodBody()?.GetILAsByteArray();
        if (tryLoad is null || tryLoadBody is null)
            return Array.Empty<AvaloniaControlInfo>();

        return ExtractAvaloniaControls(tryLoadBody, tryLoad.Module);
    }

    /// <summary>
    /// Extracts Avalonia control information from the IL of the given method body.
    /// </summary>
    /// <param name="methodBody">The IL method body to scan.</param>
    /// <param name="module">The module containing the method body.</param>
    /// <returns>An enumerable containing information about extracted Avalonia controls.</returns>
    private static IEnumerable<AvaloniaControlInfo> ExtractAvaloniaControls(ReadOnlyMemory<byte> methodBody, Module module)
    {
        _ = module ?? throw new ArgumentNullException(nameof(module));

        MethodBodyReader reader = new(methodBody);
        string? str = null;
        string? uri = null;

        while (reader.Next())
        {
            short opCode = reader.OpCode.Value;
            if (opCode == OpCodes.Ret.Value)
            {
                (str, uri) = (null, null);
                continue;
            }

            if (opCode == OpCodes.Ldstr.Value)
            {
                str = reader.ResolveString(module);
                continue;
            }

            if (opCode != OpCodes.Call.Value && opCode != OpCodes.Newobj.Value)
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
            if (populateMethod is null)
                continue;

            yield return new(uri, method, populateMethod, populateOverrideField);
            (str, uri) = (null, null);
        }
    }

    /// <summary>
    /// Identifies the <c>TryLoad</c> method in a given assembly's <c>XamlLoader</c> type.
    /// </summary>
    /// <param name="assembly">The assembly containing the <c>XamlLoader</c> type.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the <c>TryLoad</c> method, or <c>null</c> if not found.</returns>
    private static MethodInfo? FindTryLoadMethod(Assembly assembly)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        Type? xamlLoader = FindXamlLoader(assembly);
        return xamlLoader is null ? null : FindTryLoadMethod(xamlLoader);
    }

    /// <summary>
    /// Identifies the <c>TryLoad</c> method in a given <c>XamlLoader</c> type.
    /// </summary>
    /// <param name="xamlLoaderType">The <c>XamlLoader</c> type containing the <c>TryLoad</c> method.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the <c>TryLoad</c> method, or <c>null</c> if not found.</returns>
    private static MethodInfo? FindTryLoadMethod(Type xamlLoaderType)
    {
        const string tryLoadMethodName = "TryLoad";

        _ = xamlLoaderType ?? throw new ArgumentNullException(nameof(xamlLoaderType));

        return xamlLoaderType
            .GetMethods(StaticMember)
            .Where(static x => x.Name is tryLoadMethodName)
            .OrderByDescending(static x => x.GetParameters().Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Finds the <c>XamlLoader</c> type in a given assembly.
    /// </summary>
    /// <param name="assembly">The assembly to search for the <c>XamlLoader</c> type.</param>
    /// <returns>The type representing <c>XamlLoader</c>, or <c>null</c> if not found.</returns>
    private static Type? FindXamlLoader(Assembly assembly)
    {
        const string xamlLoaderNamespaceNamespace = "CompiledAvaloniaXaml";
        const string xamlLoaderTypeName = "!XamlLoader";

        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        return assembly
            .GetLoadedTypes()
            .FirstOrDefault(static x => x.Namespace is xamlLoaderNamespaceNamespace && x.Name is xamlLoaderTypeName);
    }


    /// <summary>
    /// Finds the populate override field in relation to the given build method.
    /// </summary>
    /// <param name="buildMethod">The build method for which the field is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the field, or <c>null</c> if not found.</returns>
    private static FieldInfo? FindPopulateOverrideField(MethodBase buildMethod)
    {
        _ = buildMethod ?? throw new ArgumentNullException(nameof(buildMethod));

        if (buildMethod.DeclaringType is not Type declaringType)
            return null;

        string populateName = buildMethod.Name.StartsWith(BuildResourceMethodNamePrefix, StringComparison.Ordinal)
            ? $"{PopulateOverrideResourceFieldNamePrefix}{buildMethod.Name.Substring(BuildResourceMethodNamePrefix.Length)}"
            : PopulateOverrideControlFieldName;

        FieldInfo? field = declaringType.GetField(populateName, StaticMember);
        return IsPopulateOverrideField(field) ? field : null;
    }

    /// <summary>
    /// Finds the populate method in relation to the given build method.
    /// </summary>
    /// <param name="buildMethod">The build method for which the populate method is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the populate method, or <c>null</c> if not found.</returns>
    private static MethodInfo? FindPopulateMethod(MethodBase buildMethod)
    {
        _ = buildMethod ?? throw new ArgumentNullException(nameof(buildMethod));

        if (buildMethod.DeclaringType is not Type declaringType)
            return null;

        if (!buildMethod.Name.StartsWith(BuildResourceMethodNamePrefix, StringComparison.Ordinal))
            return FindPopulateControlMethod(declaringType);

        string populateName = $"{PopulateResourceMethodNamePrefix}{buildMethod.Name.Substring(BuildResourceMethodNamePrefix.Length)}";
        return declaringType
            .GetMethods(StaticMember)
            .FirstOrDefault(x => x.Name == populateName && IsPopulateMethod(x));
    }

    /// <summary>
    /// Finds the populate method for a user control.
    /// </summary>
    /// <param name="userControlType">The type of the user control for which the populate method is sought.</param>
    /// <returns>The <see cref="MethodInfo"/> object representing the populate method, or <c>null</c> if not found.</returns>
    private static MethodInfo? FindPopulateControlMethod(Type userControlType)
    {
        _ = userControlType ?? throw new ArgumentNullException(nameof(userControlType));

        return userControlType
            .GetMethods(StaticMember)
            .FirstOrDefault(static x =>
                x.Name.StartsWith(PopulateControlMethodNamePrefix, StringComparison.Ordinal)
                && IsPopulateMethod(x));
    }

    /// <inheritdoc cref="FindDynamicPopulateMethods(string)"/>
    internal static IEnumerable<MethodInfo> FindDynamicPopulateMethods(Uri uri)
    {
        _ = uri ?? throw new ArgumentNullException(nameof(uri));

        return FindDynamicPopulateMethods(uri.ToString());
    }

    /// <summary>
    /// Searches for populate methods associated with the specified URI within the dynamic XAML assembly.
    /// </summary>
    /// <param name="uri">The URI associated with the Avalonia control/resource to be populated.</param>
    /// <returns>An enumerable containing dynamic populate methods corresponding to the specified URI.</returns>
    internal static IEnumerable<MethodInfo> FindDynamicPopulateMethods(string uri)
    {
        const string dynamicXamlBuilderClassNamePrefix = "Builder_";
        const string dynamicPopulateMethodName = "__AvaloniaXamlIlPopulate";

        _ = uri ?? throw new ArgumentNullException(nameof(uri));

        Assembly? dynamicXamlAssembly = DynamicXamlAssembly;
        if (dynamicXamlAssembly is null)
            yield break;

        string safeUri = UriHelper.GetSafeUriIdentifier(uri);
        foreach (Type type in dynamicXamlAssembly.GetLoadedTypes())
        {
            if (!type.Name.StartsWith(dynamicXamlBuilderClassNamePrefix, StringComparison.Ordinal))
                continue;

            if (!type.Name.EndsWith(safeUri, StringComparison.Ordinal))
                continue;

            MethodInfo? populateMethod = type.GetMethod(dynamicPopulateMethodName, StaticMember);
            if (IsPopulateMethod(populateMethod))
                yield return populateMethod;
        }
    }
}
