using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Markup.Xaml;
using HotAvalonia.Helpers;

namespace HotAvalonia.Assets;

/// <summary>
/// Provides a way to dynamically load assets of type <typeparamref name="TAsset"/>.
/// </summary>
/// <typeparam name="TAsset">The type of the asset to be dynamically loaded.</typeparam>
internal sealed class DynamicAssetTypeConverter<TAsset> : TypeConverter where TAsset : notnull
{
    /// <summary>
    /// Gets a singleton instance of the <see cref="DynamicAssetTypeConverter{TAsset}"/>.
    /// </summary>
    public static DynamicAssetTypeConverter<TAsset> Instance { get; } = new();

    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    /// <inheritdoc/>
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string path)
            return base.ConvertFrom(context, culture, value);

        IUriContext? uriContext = (context as IServiceProvider)?.GetService(typeof(IUriContext)) as IUriContext;
        Uri uri = new(path, path.StartsWith("/") ? UriKind.Relative : UriKind.RelativeOrAbsolute);
        Uri? baseUri = uriContext?.BaseUri;

        return DynamicAsset<TAsset>.Create(uri, baseUri);
    }
}

/// <inheritdoc cref="DynamicAssetTypeConverter{TAsset}"/>
/// <typeparam name="TAssetTypeConverter">
/// The specific type of the <see cref="TypeConverter"/> to wrap
/// the <see cref="DynamicAssetTypeConverter{TAsset}"/> with.
/// </typeparam>
internal static class DynamicAssetTypeConverter<TAsset, TAssetTypeConverter>
    where TAsset : notnull
    where TAssetTypeConverter : TypeConverter
{
    /// <summary>
    /// Gets a singleton instance of the <typeparamref name="TAssetTypeConverter"/>.
    /// </summary>
    public static TAssetTypeConverter Instance { get; } = (TAssetTypeConverter)Activator.CreateInstance(
        DynamicAssetTypeConverterBuilder.CreateDynamicAssetConverterType(typeof(TAsset), typeof(TAssetTypeConverter))
    );
}

/// <summary>
/// Provides functionality to dynamically generate custom asset type converters at runtime.
/// </summary>
file static class DynamicAssetTypeConverterBuilder
{
    /// <summary>
    /// The module builder used to define dynamic types.
    /// </summary>
    private static readonly Lazy<ModuleBuilder> s_moduleBuilder = new(CreateModuleBuilder, isThreadSafe: true);

    /// <summary>
    /// Creates a type that serves as a custom asset type converter.
    /// </summary>
    /// <param name="assetType">The type of the asset to be dynamically loaded.</param>
    /// <param name="assetConverterType">The base type of the asset type converter.</param>
    /// <returns>
    /// A <see cref="Type"/> representing the generated asset type converter,
    /// which wraps the <see cref="DynamicAssetTypeConverter{TAsset}"/> logic.
    /// </returns>
    public static Type CreateDynamicAssetConverterType(Type assetType, Type assetConverterType)
    {
        const MethodAttributes MethodOverride = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.Virtual;

        _ = assetType ?? throw new ArgumentNullException(nameof(assetType));
        _ = assetConverterType ?? throw new ArgumentNullException(nameof(assetConverterType));

        ModuleBuilder moduleBuilder = s_moduleBuilder.Value;
        AssemblyBuilder assemblyBuilder = (AssemblyBuilder)moduleBuilder.Assembly;

        string fullName = $"{assetConverterType.FullName}$Dynamic";
        Type? existingType = assemblyBuilder.GetType(fullName, throwOnError: false);
        if (existingType is not null)
            return existingType;

        assemblyBuilder.AllowAccessTo(assetType);
        assemblyBuilder.AllowAccessTo(assetConverterType);

        Type dynamicConverterType = typeof(DynamicAssetTypeConverter<>).MakeGenericType(assetType);
        MethodInfo dynamicConverterGetInstance = dynamicConverterType.GetProperty("Instance")!.GetMethod;

        // public sealed class {TAssetTypeConverter}$Dynamic : TAssetTypeConverter
        // {
        TypeBuilder typeBuilder = moduleBuilder.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        typeBuilder.SetParent(assetConverterType);

        //     public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        //     {
        //         DynamicAssetTypeConverter<TAsset> converter = DynamicAssetTypeConverter<TAsset>.Instance;
        //         return converter.CanConvertFrom(context, sourceType) || base.CanConvertFrom(context, sourceType);
        //     }
        MethodBuilder canConvertBuilder = typeBuilder.DefineMethod(
            nameof(TypeConverter.CanConvertFrom), MethodOverride,
            typeof(bool), [typeof(ITypeDescriptorContext), typeof(Type)]
        );
        ILGenerator canConvertIl = canConvertBuilder.GetILGenerator();
        Label canConvertEnd = canConvertIl.DefineLabel();

        canConvertIl.Emit(OpCodes.Call, dynamicConverterGetInstance);
        canConvertIl.Emit(OpCodes.Ldarg_1);
        canConvertIl.Emit(OpCodes.Ldarg_2);
        canConvertIl.Emit(OpCodes.Call, dynamicConverterType.GetMethod(nameof(TypeConverter.CanConvertFrom), [typeof(ITypeDescriptorContext), typeof(Type)])!);
        canConvertIl.Emit(OpCodes.Dup);
        canConvertIl.Emit(OpCodes.Brtrue_S, canConvertEnd);
        canConvertIl.Emit(OpCodes.Pop);

        canConvertIl.Emit(OpCodes.Ldarg_0);
        canConvertIl.Emit(OpCodes.Ldarg_1);
        canConvertIl.Emit(OpCodes.Ldarg_2);
        canConvertIl.Emit(OpCodes.Call, assetConverterType.GetMethod(nameof(TypeConverter.CanConvertFrom), [typeof(ITypeDescriptorContext), typeof(Type)])!);

        canConvertIl.MarkLabel(canConvertEnd);
        canConvertIl.Emit(OpCodes.Ret);

        //     public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        //     {
        //         DynamicAssetTypeConverter<TAsset> converter = DynamicAssetTypeConverter<TAsset>.Instance;
        //         if (value is null || !converter.CanConvertFrom(context, value.GetType()))
        //             return base.ConvertFrom(context, culture, value!);
        //
        //         return converter.ConvertFrom(context, culture, value);
        //     }
        MethodBuilder convertBuilder = typeBuilder.DefineMethod(
            nameof(TypeConverter.ConvertFrom), MethodOverride,
            typeof(object), [typeof(ITypeDescriptorContext), typeof(CultureInfo), typeof(object)]
        );
        ILGenerator convertIl = convertBuilder.GetILGenerator();
        Label convertStart = convertIl.DefineLabel();
        Label convertEnd = convertIl.DefineLabel();

        convertIl.Emit(OpCodes.Ldarg_3);
        convertIl.Emit(OpCodes.Brfalse_S, convertStart);

        convertIl.Emit(OpCodes.Call, dynamicConverterGetInstance);
        convertIl.Emit(OpCodes.Ldarg_1);
        convertIl.Emit(OpCodes.Ldarg_3);
        convertIl.Emit(OpCodes.Call, typeof(object).GetMethod(nameof(GetType))!);
        convertIl.Emit(OpCodes.Call, dynamicConverterType.GetMethod(nameof(TypeConverter.CanConvertFrom), [typeof(ITypeDescriptorContext), typeof(Type)])!);
        convertIl.Emit(OpCodes.Brfalse_S, convertStart);

        convertIl.Emit(OpCodes.Call, dynamicConverterGetInstance);
        convertIl.Emit(OpCodes.Ldarg_1);
        convertIl.Emit(OpCodes.Ldarg_2);
        convertIl.Emit(OpCodes.Ldarg_3);
        convertIl.Emit(OpCodes.Call, dynamicConverterType.GetMethod(nameof(TypeConverter.ConvertFrom), [typeof(ITypeDescriptorContext), typeof(CultureInfo), typeof(object)])!);
        convertIl.Emit(OpCodes.Br_S, convertEnd);

        convertIl.MarkLabel(convertStart);
        convertIl.Emit(OpCodes.Ldarg_0);
        convertIl.Emit(OpCodes.Ldarg_1);
        convertIl.Emit(OpCodes.Ldarg_2);
        convertIl.Emit(OpCodes.Ldarg_3);
        convertIl.Emit(OpCodes.Call, assetConverterType.GetMethod(nameof(TypeConverter.ConvertFrom), [typeof(ITypeDescriptorContext), typeof(CultureInfo), typeof(object)])!);

        convertIl.MarkLabel(convertEnd);
        convertIl.Emit(OpCodes.Ret);

        // }
        return typeBuilder.CreateTypeInfo();
    }

    /// <summary>
    /// Creates and returns a dynamic module builder.
    /// </summary>
    /// <returns>A new instance of <see cref="ModuleBuilder"/>.</returns>
    private static ModuleBuilder CreateModuleBuilder()
    {
        string assemblyName = $"{nameof(HotAvalonia)}.{nameof(Assets)}.Dynamic";
        _ = AssemblyHelper.DefineDynamicAssembly(assemblyName, out AssemblyBuilder assemblyBuilder);
        assemblyBuilder.AllowAccessTo(typeof(DynamicAssetTypeConverter<>));

        return assemblyBuilder.DefineDynamicModule(assemblyName);
    }
}
