using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using HotAvalonia.Helpers;
using HotAvalonia.Logging;
using HotAvalonia.Reflection;

namespace HotAvalonia.Xaml;

/// <summary>
/// Provides functionality to compile XAML documents.
/// </summary>
public static class XamlCompiler
{
    /// <summary>
    /// The prefix used for compiled XAML type names.
    /// </summary>
    private const string CompiledXamlTypeNamePrefix = "Builder_";

    /// <summary>
    /// The name of the method used to populate controls.
    /// </summary>
    private const string PopulateMethodName = "__AvaloniaXamlIlPopulate";

    /// <summary>
    /// The name of the method used to build new control instances.
    /// </summary>
    private const string BuildMethodName = "__AvaloniaXamlIlBuild";

    /// <summary>
    /// The delegate used to compile XAML documents.
    /// </summary>
    private static readonly CompileXamlFunc s_compile = CreateXamlCompiler();

    /// <summary>
    /// Gets the dynamic assembly that houses compiled XAML types.
    /// </summary>
    public static AssemblyBuilder? DynamicXamlAssembly { get; } = GetDynamicXamlAssembly();

    /// <inheritdoc cref="Compile(string, Uri, Assembly?)"/>
    public static CompiledXamlDocument Compile(string xaml, string uri, Assembly? assembly = null)
        => Compile(xaml, new Uri(uri), assembly);

    /// <summary>
    /// Compiles the specified XAML content.
    /// </summary>
    /// <param name="xaml">The XAML content as a string.</param>
    /// <param name="uri">The URI associated with the provided XAML content.</param>
    /// <param name="assembly">An optional assembly context for the compilation.</param>
    /// <returns>A <see cref="CompiledXamlDocument"/> representing the compiled XAML.</returns>
    public static CompiledXamlDocument Compile(string xaml, Uri uri, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentNullException.ThrowIfNull(uri);

        XamlDocument document = new(uri, xaml);
        RuntimeXamlLoaderConfiguration config = CreateDefaultXamlLoaderConfig(document, assembly);
        return Compile(document, config);
    }

    /// <summary>
    /// Compiles the specified <see cref="XamlDocument"/> using the default configuration.
    /// </summary>
    /// <param name="document">The XAML document to compile.</param>
    /// <returns>A <see cref="CompiledXamlDocument"/> representing the compiled XAML.</returns>
    public static CompiledXamlDocument Compile(this XamlDocument document)
    {
        RuntimeXamlLoaderConfiguration config = CreateDefaultXamlLoaderConfig(document, assembly: null);
        return Compile(document, config);
    }

    /// <summary>
    /// Compiles the specified <see cref="XamlDocument"/> using the specified configuration.
    /// </summary>
    /// <param name="document">The XAML document to compile.</param>
    /// <param name="config">The configuration settings for the XAML compiler.</param>
    /// <returns>A <see cref="CompiledXamlDocument"/> representing the compiled XAML.</returns>
    public static CompiledXamlDocument Compile(this XamlDocument document, RuntimeXamlLoaderConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        using IDisposable context = AssemblyHelper.ForceAllowDynamicCode();
        Type compiledXamlType = s_compile([new(document.Uri, document.Stream)], config).First();
        return CreateCompiledXamlDocument(document.Uri, compiledXamlType);
    }

    /// <summary>
    /// Compiles a collection of <see cref="XamlDocument"/> using the default configuration.
    /// </summary>
    /// <param name="documents">The collection of XAML documents to compile.</param>
    /// <returns>An enumerable collection of compiled XAML documents.</returns>
    public static IEnumerable<CompiledXamlDocument> Compile(this IEnumerable<XamlDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        IReadOnlyCollection<XamlDocument> documentCollection = (documents as IReadOnlyCollection<XamlDocument>) ?? documents.ToArray();
        XamlDocument firstDocument = documentCollection.FirstOrDefault();
        RuntimeXamlLoaderConfiguration config = firstDocument.Uri is null ? new() : CreateDefaultXamlLoaderConfig(firstDocument, assembly: null);
        return Compile(documents, config);
    }

    /// <summary>
    /// Compiles a collection of <see cref="XamlDocument"/> using the specified configuration.
    /// </summary>
    /// <param name="documents">The collection of XAML documents to compile.</param>
    /// <param name="config">The configuration settings for the XAML compiler.</param>
    /// <returns>An enumerable collection of compiled XAML documents.</returns>
    public static IEnumerable<CompiledXamlDocument> Compile(this IEnumerable<XamlDocument> documents, RuntimeXamlLoaderConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(config);

        using IDisposable context = AssemblyHelper.ForceAllowDynamicCode();
        RuntimeXamlLoaderDocument[] xamlLoaderDocuments = documents.Select(static x => new RuntimeXamlLoaderDocument(x.Uri, x.Stream)).ToArray();
        return s_compile(xamlLoaderDocuments, config).Zip(xamlLoaderDocuments, static (type, doc) => CreateCompiledXamlDocument(doc.BaseUri!, type));
    }

    /// <summary>
    /// Creates the default configuration for the XAML compiler based on the provided document and assembly.
    /// </summary>
    /// <param name="document">The XAML document for which to create the configuration.</param>
    /// <param name="assembly">
    /// An optional assembly context; if not provided, it will be inferred from the document's URI.
    /// </param>
    /// <returns>A <see cref="RuntimeXamlLoaderConfiguration"/> initialized with default settings.</returns>
    private static RuntimeXamlLoaderConfiguration CreateDefaultXamlLoaderConfig(XamlDocument document, Assembly? assembly)
    {
        Assembly? localAssembly = assembly ?? AssetLoader.GetAssembly(document.Uri);
        bool useCompiledBindings = XamlScanner.UsesCompiledBindingsByDefault(assembly);
        return new() { LocalAssembly = localAssembly, UseCompiledBindingsByDefault = useCompiledBindings };
    }

    /// <summary>
    /// Creates a compiled XAML document from the specified URI and compiled type.
    /// </summary>
    /// <param name="uri">The URI associated with the XAML content.</param>
    /// <param name="compiledXamlType">The type representing the compiled XAML.</param>
    /// <returns>A <see cref="CompiledXamlDocument"/> representing the compiled XAML.</returns>
    private static CompiledXamlDocument CreateCompiledXamlDocument(Uri uri, Type compiledXamlType)
    {
        MethodInfo? populateMethod = compiledXamlType.GetStaticMethod(PopulateMethodName);
        if (populateMethod is null)
            ArgumentException.Throw(nameof(compiledXamlType));

        MethodBase? buildMethod = compiledXamlType.GetStaticMethod(BuildMethodName);
        buildMethod ??= XamlScanner.GetControlConstructor(populateMethod.GetParameters()[^1].ParameterType);
        if (buildMethod is null)
            ArgumentException.Throw(nameof(compiledXamlType));

        return new(uri, buildMethod, populateMethod.CreateUnsafeDelegate<Action<IServiceProvider?, object>>(), null, null);
    }

    /// <summary>
    /// Attempts to retrieve the assembly builder that houses runtime-compiled XAML types.
    /// </summary>
    /// <returns>The assembly builder containing runtime-compiled XAML types, if found; otherwise, <c>null</c>.</returns>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static AssemblyBuilder? GetDynamicXamlAssembly()
    {
        using IDisposable context = AssemblyHelper.GetDynamicAssembly(out AssemblyBuilder assembly, out ModuleBuilder module);
        Assembly xamlLoaderAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
        Type? compiler = xamlLoaderAssembly.GetType("Avalonia.Markup.Xaml.XamlIl.AvaloniaXamlIlRuntimeCompiler");
        FieldInfo? sreAsm = compiler?.GetField("_sreAsm", (BindingFlags)(-1));
        if (compiler is null || sreAsm is null)
            return null;

        sreAsm.SetValue(null, assembly);
        compiler.GetField("_sreBuilder", (BindingFlags)(-1))?.SetValue(null, module);
        compiler.GetField("_sreCanSave", (BindingFlags)(-1))?.SetValue(null, false);
        compiler.GetField("_ignoresAccessChecksFromAttribute", (BindingFlags)(-1))?.SetValue(null, AssemblyHelper.IgnoresAccessChecksFromAttribute);
        compiler.GetField("_sreTypeSystem", (BindingFlags)(-1))?.SetValue(null, DynamicSreTypeSystem.Create(AppDomain.CurrentDomain, assembly));
        return assembly;
    }

    /// <summary>
    /// Creates a XAML compiler delegate.
    /// </summary>
    /// <returns>A <see cref="CompileXamlFunc"/> delegate used to compile XAML documents.</returns>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static CompileXamlFunc CreateXamlCompiler()
    {
        try
        {
            Assembly xamlLoaderAssembly = typeof(AvaloniaRuntimeXamlLoader).Assembly;
            Type? originalCompiler = xamlLoaderAssembly.GetType("Avalonia.Markup.Xaml.XamlIl.AvaloniaXamlIlRuntimeCompiler");
            MethodInfo? loadGroupSreCore = originalCompiler?.GetStaticMethod("LoadGroupSreCore", [typeof(IReadOnlyCollection<RuntimeXamlLoaderDocument>), typeof(RuntimeXamlLoaderConfiguration)]);
            return RecompileXamlCompiler(loadGroupSreCore);
        }
        catch (Exception e)
        {
            Logger.LogError("Failed to recompile XamlIl compiler: {Exception}", e);
        }

        // Welp, at least we have a fallback.
        // Somehow, we managed to live off it until v3, so it's not *that* bad.
        // However, I would definitely prefer a much more predictable solution
        // with no side effects, which we aim to achieve by recompiling the compiler itself.
        return Load;
    }

    /// <summary>
    /// Loads compiled types from XAML documents using the default XAML loader.
    /// </summary>
    /// <param name="documents">A read-only collection of runtime XAML loader documents.</param>
    /// <param name="config">The configuration settings for the XAML loader.</param>
    /// <returns>An enumerable collection of compiled types.</returns>
    private static IEnumerable<Type> Load(IReadOnlyCollection<RuntimeXamlLoaderDocument> documents, RuntimeXamlLoaderConfiguration config)
    {
        HashSet<Type>[] oldCompiledTypes = documents.Select(static x => new HashSet<Type>(FindCompiledXamlTypes(x.BaseUri!.ToString()))).ToArray();
        _ = AvaloniaRuntimeXamlLoader.LoadGroup(documents, config);
        return documents.Select((x, i) => FindCompiledXamlTypes(x.BaseUri!.ToString()).FirstOrDefault(y => !oldCompiledTypes[i].Contains(y)));
    }

    /// <summary>
    /// Finds compiled XAML types within the dynamic assembly that match the specified URI.
    /// </summary>
    /// <param name="uri">The URI used to identify the compiled XAML types.</param>
    /// <returns>An enumerable collection of types whose names match the specified URI.</returns>
    private static IEnumerable<Type> FindCompiledXamlTypes(string uri)
    {
        Assembly? dynamicXamlAssembly = DynamicXamlAssembly;
        if (dynamicXamlAssembly is null)
            yield break;

        string safeUri = UriHelper.GetSafeUriIdentifier(uri);
        foreach (Type type in dynamicXamlAssembly.GetLoadedTypes())
        {
            if (!type.Name.StartsWith(CompiledXamlTypeNamePrefix, StringComparison.Ordinal))
                continue;

            if (!type.Name.EndsWith(safeUri, StringComparison.Ordinal))
                continue;

            MethodInfo? populateMethod = type.GetStaticMethod(PopulateMethodName);
            if (XamlScanner.IsPopulateMethod(populateMethod))
                yield return type;
        }
    }

    /// <summary>
    /// Recompiles the XAML compiler so that it returns the generated types directly,
    /// without attempting to unnecessarily instantiate them when we don't need it.
    /// </summary>
    /// <param name="compile">The original compile method to recompile.</param>
    /// <returns>A <see cref="CompileXamlFunc"/> delegate that can be used to compile XAML documents.</returns>
    [MethodImpl(MethodImplOptions.NoOptimization)]
    private static CompileXamlFunc RecompileXamlCompiler(MethodInfo? compile)
    {
        if (compile?.GetMethodBody() is not MethodBody body || body.GetILAsByteArray() is not byte[] bodyIl)
            throw new ArgumentException("Provided method does not have a body.", nameof(compile));

        // `ILGenerator` is atrociously designed. Somebody, in their infinite wisdom, decided that
        // making a class responsible for emitting opcodes user-hecking-friendly was a good idea.
        // As a consequence of this decision, we now have `BeginExceptionBlock`, `EndExceptionBlock`,
        // etc., which automatically emit opcodes that you don't want. Since `ILGenerator` doesn't
        // support removing already emitted instructions, this problem becomes quite complicated to
        // solve. The best approach here would be to dig into the internals of `ILGenerator` and use
        // its private mechanism for defining protected regions. However, CoreCLR and Mono handle this
        // **very** differently, and I don't really want to deal with that right now. Maybe I'll make
        // a separate library for it later.
        //
        // For now, though, we can take the simplest (and stupidest) approach - just ignore exception
        // blocks. `LoadGroupSreCore` only uses a few of them, and that's only because of `try-finally`
        // blocks automatically generated by `using` directives for disposable types.
        // The second `IDisposable` in question is literally an array enumerator (i.e., its `Dispose()`
        // is a no-op), while the first one will be disposed properly no matter what.
        //
        // To "skip" the exception blocks, we need to replace `leave`, `leave.s`, and `endfinally` opcodes
        // with `nop`s. This preserves the natural flow of the function because `leave` instructions literally
        // point to the instruction **right after** their respective `finally` blocks, which we still need
        // to execute. However, this approach is pretty fragile. Thus, if we detect that the method has changed
        // and gained/lost an exception block or two, just abort and fall back to using the original compiler as-is.
        if (body.ExceptionHandlingClauses.Any(x => x.Flags != ExceptionHandlingClauseOptions.Finally))
            throw new NotSupportedException("Provided method contains unsupported exception block(s).");

        Module module = compile.Module;
        string name = $"{compile.DeclaringType}.CompileGroupSreCore";
        Type returnType = typeof(IEnumerable<Type>);
        Type[] parameterTypes = compile.GetParameterTypes();
        using IDisposable ctx = MethodHelper.DefineDynamicMethod(name, returnType, parameterTypes, out DynamicMethod recompiled);

        ILGenerator il = recompiled.GetILGenerator();
        il.DeclareLocals(body.LocalVariables);

        MethodBodyReader reader = new(bodyIl);
        while (reader.Next())
        {
            switch (reader.OpCode.OperandType)
            {
                case OperandType.InlineI8:
                    il.Emit(reader.OpCode, reader.GetInt64());
                    break;

                case OperandType.InlineField:
                    il.Emit(reader.OpCode, reader.ResolveField(module));
                    break;

                case OperandType.InlineNone when reader.OpCode == OpCodes.Endfinally:
                    il.Emit(OpCodes.Nop);
                    break;

                case OperandType.InlineNone:
                    il.Emit(reader.OpCode);
                    break;

                case OperandType.ShortInlineR:
                    il.Emit(reader.OpCode, reader.GetSingle());
                    break;

                case OperandType.InlineR:
                    il.Emit(reader.OpCode, reader.GetDouble());
                    break;

                case OperandType.InlineString:
                    il.Emit(reader.OpCode, reader.ResolveString(module));
                    break;

                case OperandType.InlineType:
                    il.Emit(reader.OpCode, reader.ResolveType(module));
                    break;

                case OperandType.InlineVar:
                    il.Emit(reader.OpCode, reader.GetInt16());
                    break;

                case OperandType.InlineBrTarget when reader.OpCode == OpCodes.Leave:
                    il.Emit(OpCodes.Nop);
                    il.Emit(OpCodes.Nop);
                    il.Emit(OpCodes.Nop);
                    il.Emit(OpCodes.Nop);
                    il.Emit(OpCodes.Nop);
                    break;

                case OperandType.InlineBrTarget:
                case OperandType.InlineI:
                    il.Emit(reader.OpCode, reader.GetInt32());
                    break;

                case OperandType.ShortInlineBrTarget when reader.OpCode == OpCodes.Leave_S:
                    il.Emit(OpCodes.Nop);
                    il.Emit(OpCodes.Nop);
                    break;

                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.Emit(reader.OpCode, reader.GetByte());
                    break;

                case OperandType.InlineMethod:
                    switch (reader.ResolveMethod(module))
                    {
                        case ConstructorInfo ctor:
                            il.Emit(reader.OpCode, ctor);
                            continue;

                        // Avalonia automatically emits an IgnoresAccessCheckToAttribute for the local assembly.
                        // First of all, we don't need to waste time on this, because our patched version of
                        // SreTypeSystem should already have emitted one for every loaded assembly.
                        // In addition, Avalonia doesn't even check whether it has already emitted a similar
                        // attribute, so it ends up doing it again and again, bloating the assembly builder's
                        // metadata and leaking memory faster than we absolutely need to.
                        // So, to fix this, we simply replace the call to the offending method with a no-op stub.
                        case { Name: nameof(EmitIgnoresAccessCheckToAttribute) }:
                            static void EmitIgnoresAccessCheckToAttribute(AssemblyName assemblyName) => _ = assemblyName;
                            il.Emit(reader.OpCode, new Action<AssemblyName>(EmitIgnoresAccessCheckToAttribute).Method);
                            continue;

                        // Recently (?), Avalonia has also begun emitting an IgnoresAccessCheckToAttribute for
                        // every assembly that specifies an InternalsVisibleToAttribute referencing the local
                        // assembly. For the same reasons as above, we don't need this either. And while
                        // EmitIgnoresAccessCheckToAttribute was patched out primarily due to metadata bloat
                        // concerns, this one has to go because every time it is called, it performs a full
                        // scan of all currently loaded assemblies - no caching, no nothing!
                        // This takes a noticeable amount of time that would be better spent on XAML compilation.
                        // For the sake of simplicity, we replace it with a stub that always returns an empty set
                        // (unfortunately, it must be allocated anew each and every time, but given that we are
                        // working with dynamic code generation, it honestly doesn't matter).
                        case { Name: nameof(FindAssembliesGrantingInternalAccess) }:
                            static HashSet<Assembly> FindAssembliesGrantingInternalAccess(Assembly assembly) => [];
                            il.Emit(reader.OpCode, new Func<Assembly, HashSet<Assembly>>(FindAssembliesGrantingInternalAccess).Method);
                            continue;

                        // AvaloniaXamlIlRuntimeCompiler automatically creates a Build method for every provided
                        // document, since it is designed to immediately instantiate a control compiled from
                        // the provided XAML. This feature is not only useless to us, as we only work with
                        // already-instantiated user controls that simply need to be repopulated, but
                        // it also artificially limits the kinds of controls we are able to compile:
                        // if a control does not define a parameterless ctor or a ctor accepting a single
                        // IServiceProvider, LoadGroupSreCore will throw an exception stating that
                        // it's unable to create a Build method for such a scenario.
                        //
                        // In order to circumvent this, we need to patch out a call
                        // to the offending DefineBuildMethod method.
                        //
                        // In older Avalonia versions, this was quite straightforward, as the method was called
                        // directly from LoadGroupSreCore. The only thing worth noting here is that, at some
                        // point, its signature changed slightly, with the last parameter morphing from
                        // `bool isPublic` into `XamlVisibility visibility`.
                        // However, in IL, all numeric values that are smaller than or equal in size to int32
                        // (including booleans and int32-based enums) are handled as int32s, so it's safe
                        // to use the same substitute method that accepts an `int` in their place.
                        //
                        // In modern versions of Avalonia, however, the call to DefineBuildMethod was moved into
                        // a lazily invoked delegate. As a result, patching it out directly would require us
                        // to create a copy of yet another method, which would be somewhat cumbersome and
                        // more involved than I would like.
                        //
                        // Alternatively, we can take advantage of the fact that the delegate calls the offending
                        // method conditionally and manipulate its logic to achieve the same outcome.
                        // LoadGroupSreCore supports populating already-existing instances: when provided with
                        // one, it does not attempt to create a builder method. To capitalize on that, we
                        // hijack a call to the `IEnumerator<RuntimeXamlLoaderDocument>.Current` getter,
                        // replacing the value it returns with one whose `RootInstance` is set to a bogus
                        // value; then we replace all calls to the `RuntimeXamlLoaderDocument.RootInstance`
                        // getter with one that always returns null. This allows the rest of the method
                        // to remain blissfully unaware of its existence, while the delegate that captures
                        // the document instance sees that RootInstance is not null and therefore does not
                        // emit a Build method. As simple as that.
                        case { Name: nameof(DefineBuildMethod) } m when (reader.OpCode == OpCodes.Callvirt || reader.OpCode == OpCodes.Call) && m.GetParameters() is [_, _, _, { ParameterType: Type p }] && (p == typeof(bool) || p.IsEnum && p.GetEnumUnderlyingType() == typeof(int)):
                            static object? DefineBuildMethod(object? compiler, object? builder, object? parsed, object? buildName, int visibility) => null;
                            il.Emit(OpCodes.Call, new Func<object?, object?, object?, object?, int, object?>(DefineBuildMethod).Method);
                            continue;

                        // ^^^^^^^^^^
                        case { DeclaringType.IsValueType: false, Name: nameof(get_Current) } m when (reader.OpCode == OpCodes.Callvirt || reader.OpCode == OpCodes.Call) && typeof(IEnumerator<RuntimeXamlLoaderDocument>).IsAssignableFrom(m.DeclaringType):
                            static RuntimeXamlLoaderDocument get_Current(IEnumerator<RuntimeXamlLoaderDocument> e) => new(e.Current.BaseUri, Array.Empty<object>(), e.Current.XamlStream) { ServiceProvider = e.Current.ServiceProvider };
                            il.Emit(OpCodes.Call, new Func<IEnumerator<RuntimeXamlLoaderDocument>, RuntimeXamlLoaderDocument>(get_Current).Method);
                            continue;

                        // ^^^^^^^^^^
                        case { DeclaringType.Name: nameof(RuntimeXamlLoaderDocument), Name: nameof(get_RootInstance) } when reader.OpCode == OpCodes.Callvirt || reader.OpCode == OpCodes.Call:
                            static object? get_RootInstance(RuntimeXamlLoaderDocument document) => null;
                            il.Emit(OpCodes.Call, new Func<RuntimeXamlLoaderDocument, object?>(get_RootInstance).Method);
                            continue;

                        // `LoadGroupSreCore` uses `types.Zip(documents, (x, y) => (x, y)).Select(...).ToArray()`
                        // to instantiate generated types. So, if we want to get those as-is, this is the best
                        // place to intercept the method's flow. When execution reaches the `.Zip(...)` call,
                        // there are three objects on the stack: a collection of generated types, the documents
                        // they were created from, and a no-op selector. If we simply pop the last two, we get
                        // exactly what we need. Slap a `ret` after that, and we're done.
                        case { DeclaringType.Name: nameof(Enumerable), Name: nameof(Enumerable.Zip) }:
                            il.Emit(OpCodes.Pop);
                            il.Emit(OpCodes.Pop);
                            il.Emit(OpCodes.Nop);
                            il.Emit(OpCodes.Nop);
                            il.Emit(OpCodes.Ret);
                            return recompiled.CreateDelegate<CompileXamlFunc>();

                        case MethodInfo method:
                            il.Emit(reader.OpCode, method);
                            continue;
                    }
                    goto default;

                default:
                    throw new NotSupportedException($"Provided method contains unsupported instruction: {reader.OpCode}");
            }
        }

        // We should have never reached this point.
        throw new NotSupportedException("Provided method contains unsupported exit point.");
    }
}
