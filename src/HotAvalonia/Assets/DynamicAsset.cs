using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Platform;
using HotAvalonia.Helpers;
using HotAvalonia.IO;

namespace HotAvalonia.Assets;

/// <summary>
/// Represents a dynamic asset that can be created from a URI, stream, or file.
/// Automatically observes changes to the underlying file and refreshes the asset when updates occur.
/// </summary>
/// <typeparam name="TAsset">The type of the asset.</typeparam>
internal sealed class DynamicAsset<TAsset> : IObservable<TAsset> where TAsset : notnull
{
    /// <summary>
    /// A factory delegate for creating a new asset instance.
    /// </summary>
    private static readonly Func<DynamicAsset<TAsset>, Stream?, TAsset> s_create;

    /// <summary>
    /// A factory delegate for creating an asset from a stream.
    /// </summary>
    private static readonly Func<Stream, TAsset> s_fromStream;

    /// <summary>
    /// A factory delegate for creating an asset from a file name.
    /// </summary>
    private static readonly Func<string, TAsset> s_fromFileName;

    /// <summary>
    /// A copier delegate for copying state from one asset instance to another.
    /// </summary>
    private static readonly Action<TAsset, TAsset> s_copy;

    /// <summary>
    /// Initializes static state of the <see cref="DynamicAsset{TAsset}"/> class.
    /// </summary>
    static DynamicAsset()
    {
        s_fromStream = (Func<Stream, TAsset>)DynamicAssetBuilder.CreateAssetFactory(
            typeof(Func<Stream, TAsset>),
            typeof(TAsset)
        );

        s_fromFileName = (Func<string, TAsset>)DynamicAssetBuilder.CreateAssetFactory(
            typeof(Func<string, TAsset>),
            typeof(TAsset)
        );

        s_create = (Func<DynamicAsset<TAsset>, Stream?, TAsset>)DynamicAssetBuilder.CreateAssetFactory(
            typeof(Func<DynamicAsset<TAsset>, Stream?, TAsset>),
            DynamicAssetBuilder.CreateDynamicAssetType(typeof(TAsset))
        );

        s_copy = (Action<TAsset, TAsset>)DynamicAssetBuilder.CreateAssetCopier(typeof(TAsset));
    }


    /// <summary>
    /// The URI associated with the asset.
    /// </summary>
    private readonly Uri _uri;

    /// <summary>
    /// The asset instance.
    /// </summary>
    private readonly TAsset _asset;

    /// <summary>
    /// The file observer that watches the file associated with the asset
    /// and triggers refresh operations on file changes.
    /// </summary>
    private readonly FileObserver<TAsset> _fileObserver;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicAsset{TAsset}"/> class using a URI.
    /// </summary>
    /// <param name="uri">The URI of the asset.</param>
    private DynamicAsset(Uri uri)
    {
        _uri = uri;
        _asset = s_create(this, null);
        _fileObserver = new(uri.LocalPath, Refresh);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicAsset{TAsset}"/> class using a stream.
    /// </summary>
    /// <param name="stream">The stream containing the asset data.</param>
    /// <param name="uri">The URI of the asset.</param>
    /// <param name="fileName">The file name of the asset.</param>
    private DynamicAsset(Stream stream, Uri uri, string fileName)
    {
        _uri = uri;
        _asset = s_create(this, stream);
        _fileObserver = new(fileName, Refresh);
    }

    /// <summary>
    /// Gets the URI associated with the asset.
    /// </summary>
    public Uri Uri => _uri;

    /// <summary>
    /// Gets the instance of the asset.
    /// </summary>
    public TAsset Asset => _asset;

    /// <summary>
    /// Creates a new asset instance from the specified URI.
    /// </summary>
    /// <param name="uri">The URI of the asset to create.</param>
    /// <param name="baseUri">An optional base URI to resolve relative URIs.</param>
    /// <returns>A new instance of the asset.</returns>
    public static TAsset Create(Uri uri, Uri? baseUri = null)
    {
        _ = uri ?? throw new ArgumentNullException(nameof(uri));

        if (!uri.IsAbsoluteUri && baseUri is not null)
            uri = new(baseUri, uri);

        if (uri.IsAbsoluteUri && uri.IsFile)
            return new DynamicAsset<TAsset>(uri).Asset;

        (Stream stream, Assembly assembly) = AssetLoader.OpenAndGetAssembly(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != UriHelper.AvaloniaResourceScheme)
            return s_fromStream(stream);

        if (!AvaloniaProjectLocator.TryGetDirectoryName(assembly, out string? rootPath))
            return s_fromStream(stream);

        string fileName = UriHelper.ResolvePathFromUri(rootPath, uri);
        return new DynamicAsset<TAsset>(stream, uri, fileName).Asset;
    }

    /// <summary>
    /// Refreshes the asset.
    /// </summary>
    /// <returns>The refreshed asset.</returns>
    private TAsset Refresh()
    {
        TAsset newAsset = s_fromFileName is not null && _uri.IsFile
            ? s_fromFileName(_uri.LocalPath) : s_fromStream(AssetLoader.Open(_uri));

        s_copy(newAsset, _asset);
        return newAsset;
    }

    /// <inheritdoc/>
    public IDisposable Subscribe(IObserver<TAsset> observer)
        => _fileObserver.Subscribe(observer);
}

/// <summary>
/// Provides functionality to generate types and delegates
/// for working with dynamic assets.
/// </summary>
file static class DynamicAssetBuilder
{
    /// <summary>
    /// The module builder used to define dynamic types.
    /// </summary>
    private static readonly Lazy<ModuleBuilder> s_moduleBuilder = new(CreateModuleBuilder, isThreadSafe: true);

    /// <summary>
    /// Creates a type that represents a dynamic version of the provided asset type.
    /// </summary>
    /// <param name="assetType">The type of the asset for which to make a dynamic version.</param>
    /// <returns>A newly generated <see cref="Type"/> representing the dynamically asset type.</returns>
    public static Type CreateDynamicAssetType(Type assetType)
    {
        const MethodAttributes VirtualMethod = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;

        _ = assetType ?? throw new ArgumentNullException(nameof(assetType));

        ModuleBuilder moduleBuilder = s_moduleBuilder.Value;
        AssemblyBuilder assemblyBuilder = (AssemblyBuilder)moduleBuilder.Assembly;

        string fullName = $"{assetType.FullName}$Dynamic";
        Type? existingType = assemblyBuilder.GetType(fullName, throwOnError: false);
        if (existingType is not null)
            return existingType;

        assemblyBuilder.AllowAccessTo(assetType);

        // public sealed class {TAsset}$Dynamic : TAsset, IObservable<TAsset>, IDisposable
        // {
        TypeBuilder typeBuilder = moduleBuilder.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        typeBuilder.SetParent(assetType);
        typeBuilder.AddInterfaceImplementation(typeof(IObservable<>).MakeGenericType(assetType));
        typeBuilder.AddInterfaceImplementation(typeof(IDisposable));

        //     private DynamicAsset<TAsset> _asset;
        FieldBuilder assetFieldBuilder = typeBuilder.DefineField("_asset", typeof(DynamicAsset<>).MakeGenericType(assetType), FieldAttributes.Private);

        //     public {TAsset}$Dynamic(DynamicAsset<TAsset> asset, Stream? stream)
        //     {
        //         if (stream is null)
        //             base(asset.Uri.LocalPath);
        //         else
        //             base(stream);
        //
        //         _asset = asset;
        //     }
        ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard | CallingConventions.HasThis,
            [assetFieldBuilder.FieldType, typeof(Stream)]
        );
        ILGenerator ctorIl = ctorBuilder.GetILGenerator();
        Label ctorEnd = ctorIl.DefineLabel();
        Label streamCtorStart = ctorIl.DefineLabel();

        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Brtrue_S, streamCtorStart);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Call, assetFieldBuilder.FieldType.GetProperty(nameof(Uri))!.GetMethod);
        ctorIl.Emit(OpCodes.Call, typeof(Uri).GetProperty(nameof(Uri.LocalPath))!.GetMethod);
        ctorIl.Emit(OpCodes.Call, assetType.GetInstanceConstructor(typeof(string))!);
        ctorIl.Emit(OpCodes.Br_S, ctorEnd);

        ctorIl.MarkLabel(streamCtorStart);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_2);
        ctorIl.Emit(OpCodes.Call, assetType.GetInstanceConstructor(typeof(Stream))!);

        ctorIl.MarkLabel(ctorEnd);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, assetFieldBuilder);
        ctorIl.Emit(OpCodes.Ret);

        //     public virtual IDisposable Subscribe(IObserver<TAsset> observer)
        //         => _asset.Subscribe(observer);
        MethodBuilder subscribeBuilder = typeBuilder.DefineMethod(
            nameof(IObservable<string>.Subscribe), VirtualMethod,
            typeof(IDisposable), [typeof(IObserver<>).MakeGenericType(assetType)]
        );
        ILGenerator subscribeIl = subscribeBuilder.GetILGenerator();
        subscribeIl.Emit(OpCodes.Ldarg_0);
        subscribeIl.Emit(OpCodes.Ldfld, assetFieldBuilder);
        subscribeIl.Emit(OpCodes.Ldarg_1);
        subscribeIl.Emit(OpCodes.Call, assetFieldBuilder.FieldType.GetMethod(nameof(IObservable<string>.Subscribe))!);
        subscribeIl.Emit(OpCodes.Ret);

        //     public virtual void Dispose()
        //     {
        //         base.Dispose();   // If `TAsset` is actually `IDisposable`.
        //         _asset.Dispose(); // If `_asset` is actually `IDisposable`.
        //     }
        MethodBuilder disposeBuilder = typeBuilder.DefineMethod(
            nameof(IDisposable.Dispose), VirtualMethod,
            typeof(void), Type.EmptyTypes
        );
        ILGenerator disposeIl = disposeBuilder.GetILGenerator();
        if (assetType.GetMethod(nameof(IDisposable.Dispose), Type.EmptyTypes) is MethodInfo assetDispose)
        {
            disposeIl.Emit(OpCodes.Ldarg_0);
            disposeIl.Emit(OpCodes.Call, assetDispose);
        }
        if (assetFieldBuilder.FieldType.GetMethod(nameof(IDisposable.Dispose)) is MethodInfo assetFieldDispose)
        {
            disposeIl.Emit(OpCodes.Ldarg_0);
            disposeIl.Emit(OpCodes.Ldfld, assetFieldBuilder);
            disposeIl.Emit(OpCodes.Call, assetFieldDispose);
        }
        disposeIl.Emit(OpCodes.Ret);

        // }
        return typeBuilder.CreateTypeInfo();
    }

    /// <summary>
    /// Creates a delegate for constructing an asset of the specified type.
    /// </summary>
    /// <param name="delegateType">The type of delegate to create.</param>
    /// <param name="assetType">The type of the asset that the delegate will construct.</param>
    /// <returns>A delegate of the specified type that can be used to construct assets.</returns>
    public static Delegate CreateAssetFactory(Type delegateType, Type assetType)
    {
        _ = delegateType ?? throw new ArgumentNullException(nameof(delegateType));
        _ = assetType ?? throw new ArgumentNullException(nameof(assetType));

        MethodInfo? invoke = delegateType.GetMethod(nameof(Action.Invoke));
        _ = invoke ?? throw new MissingMethodException(delegateType.FullName, nameof(Action.Invoke));

        Type[] parameterTypes = invoke.GetParameterTypes();
        ConstructorInfo? ctor = assetType.GetInstanceConstructor(parameterTypes);
        _ = ctor ?? throw new MissingMethodException(assetType.FullName, ".ctor");

        // public static TAsset Create{TAsset}(...args)
        //     => new(...args);
        using IDisposable context = MethodHelper.DefineDynamicMethod($"Create{assetType.Name}", assetType, parameterTypes, out DynamicMethod factory);
        ILGenerator il = factory.GetILGenerator();

        for (int i = 0; i < parameterTypes.Length; i++)
            il.EmitLdarg(i);

        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Ret);

        return factory.CreateDelegate(delegateType);
    }

    /// <summary>
    /// Creates a delegate for copying state from one instance of an asset to another.
    /// </summary>
    /// <param name="assetType">The type of the asset to copy.</param>
    /// <returns>A delegate that performs a copy operation.</returns>
    public static Delegate CreateAssetCopier(Type assetType)
    {
        _ = assetType ?? throw new ArgumentNullException(nameof(assetType));

        Type delegateType = typeof(Action<,>).MakeGenericType(assetType, assetType);
        MethodInfo disposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!;
        FieldInfo[] fields = assetType.GetInstanceFields();

        // public static void Copy{TAsset}(TAsset from, TAsset to)
        // {
        using IDisposable context = MethodHelper.DefineDynamicMethod($"Copy{assetType.Name}", typeof(void), [assetType, assetType], out DynamicMethod copier);
        ILGenerator il = copier.GetILGenerator();

        //     if (to.fieldN is IDisposable)
        //     {
        //         ((IDisposable)to.fieldN).Dispose();
        //     }
        foreach (FieldInfo field in fields)
        {
            Label next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Isinst, typeof(IDisposable));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldfld, field);
            il.EmitCall(OpCodes.Callvirt, disposeMethod, null);
            il.MarkLabel(next);
        }

        //     to.fieldN = from.fieldN;
        foreach (FieldInfo field in fields)
        {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Stfld, field);
        }

        il.Emit(OpCodes.Ret);

        // }
        return copier.CreateDelegate(delegateType);
    }

    /// <summary>
    /// Creates and returns a dynamic module builder.
    /// </summary>
    /// <returns>A new instance of <see cref="ModuleBuilder"/>.</returns>
    private static ModuleBuilder CreateModuleBuilder()
    {
        string assemblyName = $"{nameof(HotAvalonia)}.{nameof(Assets)}.Dynamic";
        _ = AssemblyHelper.DefineDynamicAssembly(assemblyName, out AssemblyBuilder assemblyBuilder);
        assemblyBuilder.AllowAccessTo(typeof(DynamicAsset<>));

        return assemblyBuilder.DefineDynamicModule(assemblyName);
    }
}
