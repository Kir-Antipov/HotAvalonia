using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Markup.Xaml;
using HotAvalonia.Helpers;

namespace HotAvalonia.Reflection;

/// <summary>
/// Provides a substitute for the <c>SreAssembly</c> class defined by Avalonia.
/// </summary>
internal static class DynamicSreAssembly
{
    /// <summary>
    /// Represents the <c>HotAvalonia.XamlX.IL.DynamicSreAssembly</c> type.
    /// </summary>
    private static readonly Type s_type = CreateDynamicSreAssemblyType();

    /// <summary>
    /// Creates a new instance of the <see cref="DynamicAssembly"/> class
    /// using the specified assembly and type system.
    /// </summary>
    /// <param name="assembly">The assembly to wrap.</param>
    /// <param name="sreTypeSystem">The <c>SreTypeSystem</c> to use.</param>
    /// <returns>
    /// A <see cref="DynamicAssembly"/> instance wrapping the provided <paramref name="assembly"/>.
    /// </returns>
    public static DynamicAssembly Create(Assembly assembly, object sreTypeSystem)
        => (DynamicAssembly)Activator.CreateInstance(s_type, assembly, sreTypeSystem);

    /// <summary>
    /// Creates a <see cref="Type"/> that serves as a substitute
    /// for Avalonia's <c>SreAssembly</c>.
    /// </summary>
    /// <returns>
    /// A type implementing <c>IXamlAssembly</c> and <c>DynamicAssembly</c>.
    /// </returns>
    private static Type CreateDynamicSreAssemblyType()
    {
        const MethodAttributes VirtualMethod = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;

        using IDisposable context = AssemblyHelper.GetDynamicAssembly(out AssemblyBuilder assemblyBuilder, out ModuleBuilder moduleBuilder);
        string fullName = nameof(DynamicSreAssembly);
        Type? existingType = assemblyBuilder.GetType(fullName, throwOnError: false);
        if (existingType is not null)
            return existingType;

        Assembly xamlAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
        Type parentType = typeof(DynamicAssembly);
        Type interfaceType = xamlAssembly.GetType("XamlX.TypeSystem.IXamlAssembly") ?? typeof(IEquatable<object>);
        Type assemblyType = xamlAssembly.GetType("XamlX.TypeSystem.IXamlAssembly") ?? typeof(object);
        Type xamlType = xamlAssembly.GetType("XamlX.TypeSystem.IXamlType") ?? typeof(object);
        Type customAttribute = xamlAssembly.GetType("XamlX.TypeSystem.IXamlCustomAttribute") ?? typeof(object);
        Type sreTypeSystem = xamlAssembly.GetType("XamlX.IL.SreTypeSystem") ?? typeof(object);
        Type sreType = xamlAssembly.GetType("XamlX.IL.SreTypeSystem+SreType") ?? typeof(object);

        assemblyBuilder.AllowAccessTo(xamlAssembly);

        // public sealed class DynamicSreAssembly : DynamicAssembly, IXamlAssembly
        // {
        TypeBuilder typeBuilder = moduleBuilder.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        typeBuilder.SetParent(typeof(DynamicAssembly));
        typeBuilder.AddInterfaceImplementation(interfaceType);

        //     private readonly SreTypeSystem _system;
        FieldBuilder systemFieldBuilder = typeBuilder.DefineField("_system", sreTypeSystem, FieldAttributes.Private | FieldAttributes.InitOnly);

        //     private readonly HashSet<Assembly> _assemblies;
        FieldBuilder assembliesFieldBuilder = typeBuilder.DefineField("_assemblies", typeof(HashSet<Assembly>), FieldAttributes.Private | FieldAttributes.InitOnly);

        //     private readonly Dictionary<string, SreType> _types;
        FieldBuilder typesFieldBuilder = typeBuilder.DefineField("_types", typeof(Dictionary<,>).MakeGenericType(typeof(string), sreType), FieldAttributes.Private | FieldAttributes.InitOnly);

        //     public DynamicSreAssembly(Assembly xamlAssembly, SreTypeSystem system) : base(xamlAssembly)
        //     {
        //         _system = system;
        //         _assemblies = new();
        //         _types = new();
        //     }
        ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard | CallingConventions.HasThis,
            [typeof(Assembly), systemFieldBuilder.FieldType]
        );
        ILGenerator ctorIl = ctorBuilder.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Call, parentType.GetInstanceConstructor(typeof(Assembly))!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Stfld, systemFieldBuilder);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Newobj, assembliesFieldBuilder.FieldType.GetInstanceConstructor()!);
        ctorIl.Emit(OpCodes.Stfld, assembliesFieldBuilder);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Newobj, typesFieldBuilder.FieldType.GetInstanceConstructor()!);
        ctorIl.Emit(OpCodes.Stfld, typesFieldBuilder);
        ctorIl.Emit(OpCodes.Ret);

        //     string IXamlAssembly.Name => Name;
        PropertyBuilder nameBuilder = typeBuilder.DefineProperty(nameof(DynamicAssembly.Name), PropertyAttributes.None, typeof(string), null);
        MethodBuilder getNameBuilder = typeBuilder.DefineMethod($"get_{nameBuilder.Name}", VirtualMethod | MethodAttributes.SpecialName, typeof(string), Type.EmptyTypes);
        nameBuilder.SetGetMethod(getNameBuilder);
        ILGenerator getNameIl = getNameBuilder.GetILGenerator();
        getNameIl.Emit(OpCodes.Ldarg_0);
        getNameIl.Emit(OpCodes.Call, parentType.GetProperty(nameof(DynamicAssembly.Name))!.GetMethod!);
        getNameIl.Emit(OpCodes.Ret);

        //     public IReadOnlyList<IXamlCustomAttribute> CustomAttributes => [];
        PropertyBuilder customAttributesBuilder = typeBuilder.DefineProperty("CustomAttributes", PropertyAttributes.None, typeof(IReadOnlyList<>).MakeGenericType(customAttribute), null);
        MethodBuilder getCustomAttributesBuilder = typeBuilder.DefineMethod($"get_{customAttributesBuilder.Name}", VirtualMethod | MethodAttributes.SpecialName, customAttributesBuilder.PropertyType, Type.EmptyTypes);
        customAttributesBuilder.SetGetMethod(getCustomAttributesBuilder);
        ILGenerator getCustomAttributesIl = getCustomAttributesBuilder.GetILGenerator();
        getCustomAttributesIl.Emit(OpCodes.Call, typeof(Array).GetMethod(nameof(Array.Empty), Type.EmptyTypes).MakeGenericMethod(customAttribute));
        getCustomAttributesIl.Emit(OpCodes.Ret);

        //     public override void AllowAccessTo(Assembly assembly)
        //     {
        //         if (_assemblies.Contains(assembly))
        //             return;
        //
        //         base.AllowAccessTo(assembly);
        //         foreach (Type type in assembly.GetLoadedTypes())
        //         {
        //             if (!type.IsPublic)
        //                 _types[type.FullName] = _system.ResolveType(type);
        //         }
        //         _assemblies.Add(assembly);
        //     }
        MethodInfo allowDeclaration = typeof(DynamicAssembly).GetMethod(nameof(DynamicAssembly.AllowAccessTo), [typeof(Assembly)]);
        MethodBuilder allowBuilder = typeBuilder.DefineMethod(nameof(DynamicAssembly.AllowAccessTo), VirtualMethod ^ MethodAttributes.NewSlot, typeof(void), [typeof(Assembly)]);
        typeBuilder.DefineMethodOverride(allowBuilder, allowDeclaration);
        ILGenerator allowIl = allowBuilder.GetILGenerator();
        LocalBuilder allowIlEnumerator = allowIl.DeclareLocal(typeof(IEnumerator<Type>));
        LocalBuilder allowIlCurrent = allowIl.DeclareLocal(typeof(Type));
        Label allowIlEnd = allowIl.DefineLabel();
        Label allowIlLoopHead = allowIl.DefineLabel();
        Label allowIlLoopNext = allowIl.DefineLabel();
        allowIl.Emit(OpCodes.Ldarg_0);
        allowIl.Emit(OpCodes.Ldfld, assembliesFieldBuilder);
        allowIl.Emit(OpCodes.Ldarg_1);
        allowIl.Emit(OpCodes.Call, assembliesFieldBuilder.FieldType.GetMethod(nameof(HashSet<Assembly>.Contains), [typeof(Assembly)])!);
        allowIl.Emit(OpCodes.Brtrue_S, allowIlEnd);
        allowIl.Emit(OpCodes.Ldarg_0);
        allowIl.Emit(OpCodes.Ldarg_1);
        allowIl.Emit(OpCodes.Call, allowDeclaration);
        if (sreTypeSystem != typeof(object))
        {
            allowIl.Emit(OpCodes.Ldarg_1);
            allowIl.Emit(OpCodes.Call, typeof(AssemblyHelper).GetStaticMethod(nameof(AssemblyHelper.GetLoadedTypes), [typeof(Assembly)])!);
            allowIl.Emit(OpCodes.Callvirt, typeof(IEnumerable<Type>).GetMethod(nameof(IEnumerable<Type>.GetEnumerator), Type.EmptyTypes)!);
            allowIl.Emit(OpCodes.Stloc_S, allowIlEnumerator);
            allowIl.Emit(OpCodes.Br_S, allowIlLoopHead);

            allowIl.MarkLabel(allowIlLoopNext);
            allowIl.Emit(OpCodes.Ldloc_S, allowIlEnumerator);
            allowIl.Emit(OpCodes.Callvirt, typeof(IEnumerator<Type>).GetProperty(nameof(IEnumerator<Type>.Current))!.GetMethod!);
            allowIl.Emit(OpCodes.Dup);
            allowIl.Emit(OpCodes.Stloc_S, allowIlCurrent);
            allowIl.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.IsPublic))!.GetMethod!);
            allowIl.Emit(OpCodes.Brtrue_S, allowIlLoopHead);

            allowIl.Emit(OpCodes.Ldarg_0);
            allowIl.Emit(OpCodes.Ldfld, typesFieldBuilder);
            allowIl.Emit(OpCodes.Ldloc_S, allowIlCurrent);
            allowIl.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.FullName))!.GetMethod!);
            allowIl.Emit(OpCodes.Ldarg_0);
            allowIl.Emit(OpCodes.Ldfld, systemFieldBuilder);
            allowIl.Emit(OpCodes.Ldloc_S, allowIlCurrent);
            allowIl.Emit(OpCodes.Callvirt, sreTypeSystem.GetInstanceMethod("ResolveType", [typeof(Type)])!);
            allowIl.Emit(OpCodes.Callvirt, typesFieldBuilder.FieldType.GetProperty("Item", sreType, [typeof(string)])!.SetMethod);

            allowIl.MarkLabel(allowIlLoopHead);
            allowIl.Emit(OpCodes.Ldloc_S, allowIlEnumerator);
            allowIl.Emit(OpCodes.Callvirt, typeof(IEnumerator).GetMethod(nameof(IEnumerator.MoveNext), Type.EmptyTypes)!);
            allowIl.Emit(OpCodes.Brtrue_S, allowIlLoopNext);

            allowIl.Emit(OpCodes.Ldloc_S, allowIlEnumerator);
            allowIl.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose), Type.EmptyTypes)!);
        }
        allowIl.Emit(OpCodes.Ldarg_0);
        allowIl.Emit(OpCodes.Ldfld, assembliesFieldBuilder);
        allowIl.Emit(OpCodes.Ldarg_1);
        allowIl.Emit(OpCodes.Call, typeof(HashSet<Assembly>).GetMethod(nameof(HashSet<Assembly>.Add), [typeof(Assembly)])!);
        allowIl.Emit(OpCodes.Pop);
        allowIl.MarkLabel(allowIlEnd);
        allowIl.Emit(OpCodes.Ret);

        //     public IXamlType? FindType(string fullName)
        //     {
        //         _types.TryGetValue(fullName, out SreType? type);
        //         return type;
        //     }
        MethodBuilder findTypeBuilder = typeBuilder.DefineMethod("FindType", VirtualMethod, xamlType, [typeof(string)]);
        ILGenerator findTypeIl = findTypeBuilder.GetILGenerator();
        LocalBuilder foundTypeBuilder = findTypeIl.DeclareLocal(sreType);
        findTypeIl.Emit(OpCodes.Ldarg_0);
        findTypeIl.Emit(OpCodes.Ldfld, typesFieldBuilder);
        findTypeIl.Emit(OpCodes.Ldarg_1);
        findTypeIl.Emit(OpCodes.Ldloca_S, foundTypeBuilder);
        findTypeIl.Emit(OpCodes.Callvirt, typesFieldBuilder.FieldType.GetMethod(nameof(Dictionary<string, string>.TryGetValue), [typeof(string), sreType.MakeByRefType()]));
        findTypeIl.Emit(OpCodes.Pop);
        findTypeIl.Emit(OpCodes.Ldloc, foundTypeBuilder);
        findTypeIl.Emit(OpCodes.Ret);

        //     public bool Equals(IXamlAssembly other)
        //         => this == other;
        MethodBuilder equalsBuilder = typeBuilder.DefineMethod(nameof(Equals), VirtualMethod, typeof(bool), [assemblyType]);
        ILGenerator equalsIl = equalsBuilder.GetILGenerator();
        equalsIl.Emit(OpCodes.Ldarg_0);
        equalsIl.Emit(OpCodes.Ldarg_1);
        equalsIl.Emit(OpCodes.Ceq);
        equalsIl.Emit(OpCodes.Ret);

        // }
        return typeBuilder.CreateTypeInfo();
    }
}
