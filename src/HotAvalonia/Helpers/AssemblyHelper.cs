using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for interacting with assemblies.
/// </summary>
internal static class AssemblyHelper
{
    /// <summary>
    /// The lazily initialized constructor for the <c>IgnoresAccessChecksToAttribute</c> type.
    /// </summary>
    /// <remarks>
    /// The constructor accepts a single string representing the name of the assembly
    /// which internals you need to access.
    /// </remarks>
    private static readonly Lazy<ConstructorInfo> s_ignoresAccessChecksToAttribute = new(
        static () => CreateIgnoresAccessChecksToAttributeType().GetConstructor([typeof(string)]),
        isThreadSafe: true
    );

    /// <summary>
    /// The lazily initialized delegate to get the assembly name from
    /// an <c>IgnoresAccessChecksToAttribute</c> instance.
    /// </summary>
    private static readonly Lazy<Func<Attribute, string?>> s_getAssemblyName = new(
        static () => (Func<Attribute, string?>)Delegate.CreateDelegate(
            typeof(Func<Attribute, string?>),
            s_ignoresAccessChecksToAttribute.Value.DeclaringType,
            $"TryGet{nameof(AssemblyName)}"
        ),
        isThreadSafe: true
    );

    /// <summary>
    /// Retrieves all loadable types from a given assembly.
    /// </summary>
    /// <param name="assembly">The assembly from which to retrieve types.</param>
    /// <returns>An enumerable of types available in the provided assembly.</returns>
    /// <remarks>
    /// This method attempts to get all types from the assembly, but in case of a
    /// <see cref="ReflectionTypeLoadException"/>, it will return the types that are loadable.
    /// </remarks>
    public static IEnumerable<Type> GetLoadedTypes(this Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(static x => x is not null)!;
        }
    }

    /// <summary>
    /// Adds the <c>IgnoresAccessChecksToAttribute</c> to the source assembly to allow access
    /// to the specified target assembly.
    /// </summary>
    /// <param name="sourceAssembly">The source assembly to which the attribute is added.</param>
    /// <param name="targetAssemblyName">The name of the target assembly.</param>
    private static void AllowAccessTo(this AssemblyBuilder sourceAssembly, string targetAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(targetAssemblyName))
            return;

        ConstructorInfo attributeCtor = s_ignoresAccessChecksToAttribute.Value;
        Func<Attribute, string?> getAssemblyName = s_getAssemblyName.Value;
        IEnumerable<Attribute> definedAttributes = sourceAssembly.GetCustomAttributes(attributeCtor.DeclaringType);
        foreach (Attribute definedAttribute in definedAttributes)
        {
            if (string.Equals(getAssemblyName(definedAttribute), targetAssemblyName, StringComparison.Ordinal))
                return;
        }

        CustomAttributeBuilder ignoresAccessCheckAttribute = new(attributeCtor, [targetAssemblyName]);
        sourceAssembly.SetCustomAttribute(ignoresAccessCheckAttribute);
    }

    /// <summary>
    /// Adds the <c>IgnoresAccessChecksToAttribute</c> to the source assembly to allow access
    /// to the specified target assembly.
    /// </summary>
    /// <param name="sourceAssembly">The source assembly to which the attribute is added.</param>
    /// <param name="targetAssemblyName">The <see cref="AssemblyName"/> of the target assembly.</param>
    public static void AllowAccessTo(this AssemblyBuilder sourceAssembly, AssemblyName targetAssemblyName)
    {
        string name = targetAssemblyName.Name;
        byte[] key = targetAssemblyName.GetPublicKey();
        if (!string.IsNullOrWhiteSpace(name) && key is { Length: > 0 })
            name = $"{name}, PublicKey={BitConverter.ToString(key).Replace("-", string.Empty).ToUpperInvariant()}";

        sourceAssembly.AllowAccessTo(name);
    }

    /// <summary>
    /// Adds the <c>IgnoresAccessChecksToAttribute</c> to the source assembly to allow access
    /// to the specified target assembly.
    /// </summary>
    /// <param name="sourceAssembly">The source assembly to which the attribute is added.</param>
    /// <param name="targetAssembly">The target assembly.</param>
    public static void AllowAccessTo(this AssemblyBuilder sourceAssembly, Assembly targetAssembly)
        => sourceAssembly.AllowAccessTo(targetAssembly.GetName());

    /// <summary>
    /// Adds the <c>IgnoresAccessChecksToAttribute</c> to the source assembly to allow access
    /// to the assembly containing the specified target type.
    /// </summary>
    /// <param name="sourceAssembly">The source assembly to which the attribute is added.</param>
    /// <param name="targetType">The target type whose assembly access is needed.</param>
    public static void AllowAccessTo(this AssemblyBuilder sourceAssembly, Type targetType)
        => sourceAssembly.AllowAccessTo(targetType.Assembly.GetName());

    /// <summary>
    /// Adds the <c>IgnoresAccessChecksToAttribute</c> to the source assembly to allow access
    /// to the assemblies referenced by the specified target method.
    /// </summary>
    /// <param name="sourceAssembly">The source assembly to which the attribute is added.</param>
    /// <param name="targetMethod">The target method whose referenced assemblies' access is needed.</param>
    public static void AllowAccessTo(this AssemblyBuilder sourceAssembly, MethodBase targetMethod)
    {
        IEnumerable<Assembly> referencedAssemblies = ((targetMethod as MethodInfo)?
            .GetGenericArguments() ?? Type.EmptyTypes)
            .Concat([targetMethod.DeclaringType, targetMethod.GetReturnType()])
            .Concat(targetMethod.GetParameters().Select(static x => x.ParameterType))
            .Select(static x => x.Assembly)
            .Distinct();

        foreach (Assembly assembly in referencedAssemblies)
            sourceAssembly.AllowAccessTo(assembly.GetName());
    }

    /// <summary>
    /// Creates a dynamic type that represents the <c>System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute</c>.
    /// </summary>
    /// <remarks>
    /// This <i>undocumented</i> attribute allows bypassing access checks to internal members of a specified assembly.
    ///
    /// You can think of it as a long-lost cousin of the <see cref="InternalsVisibleToAttribute"/>, which works
    /// somewhat similarly, but in the opposite direction. I.e., instead of us giving another assembly permission
    /// to access our internals, the other assembly can happily help itself and freely get to our internal members.
    /// </remarks>
    /// <returns>The dynamically created type that represents <c>IgnoresAccessChecksToAttribute</c>.</returns>
    private static Type CreateIgnoresAccessChecksToAttributeType()
    {
        const string attributeName = "System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute";
        const string moduleName = "IgnoresAccessChecksToAttributeDefinition";

        string assemblyName = $"{moduleName}+{Guid.NewGuid():N}";
        AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new(assemblyName), AssemblyBuilderAccess.RunAndCollect);
        ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(moduleName);

        TypeBuilder attributeBuilder = moduleBuilder.DefineType(attributeName, TypeAttributes.Class | TypeAttributes.Public, typeof(Attribute));

        PropertyBuilder namePropertyBuilder = attributeBuilder.DefineProperty(nameof(AssemblyName), PropertyAttributes.None, typeof(string), Type.EmptyTypes);
        FieldBuilder nameFieldBuilder = attributeBuilder.DefineField($"<{namePropertyBuilder.Name}>k__BackingField", typeof(string), FieldAttributes.Private);
        MethodBuilder nameGetterBuilder = attributeBuilder.DefineMethod($"get_{namePropertyBuilder.Name}", MethodAttributes.Public, typeof(string), Type.EmptyTypes);
        ILGenerator nameIl = nameGetterBuilder.GetILGenerator();
        nameIl.Emit(OpCodes.Ldarg_0);
        nameIl.Emit(OpCodes.Ldfld, nameFieldBuilder);
        nameIl.Emit(OpCodes.Ret);
        namePropertyBuilder.SetGetMethod(nameGetterBuilder);

        MethodBuilder nameStaticGetterBuilder = attributeBuilder.DefineMethod($"TryGet{namePropertyBuilder.Name}", MethodAttributes.Public | MethodAttributes.Static, typeof(string), [typeof(Attribute)]);
        ILGenerator staticNameIl = nameStaticGetterBuilder.GetILGenerator();
        Label isNotAttribute = staticNameIl.DefineLabel();
        staticNameIl.Emit(OpCodes.Ldarg_0);
        staticNameIl.Emit(OpCodes.Isinst, attributeBuilder);
        staticNameIl.Emit(OpCodes.Dup);
        staticNameIl.Emit(OpCodes.Brfalse_S, isNotAttribute);
        staticNameIl.Emit(OpCodes.Ldfld, nameFieldBuilder);
        staticNameIl.MarkLabel(isNotAttribute);
        staticNameIl.Emit(OpCodes.Ret);

        ConstructorInfo superCtor = typeof(Attribute).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).First(static x => x.GetParameters().Length is 0);
        ConstructorBuilder ctorBuilder = attributeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        ILGenerator ctorIl = ctorBuilder.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, nameFieldBuilder);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, superCtor);
        ctorIl.Emit(OpCodes.Ret);

        CustomAttributeBuilder attributeUsage = new(
            typeof(AttributeUsageAttribute).GetConstructor([typeof(AttributeTargets)]),
            [AttributeTargets.Assembly],

            [typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))],
            [true]
        );
        attributeBuilder.SetCustomAttribute(attributeUsage);

        return attributeBuilder.CreateTypeInfo();
    }
}
