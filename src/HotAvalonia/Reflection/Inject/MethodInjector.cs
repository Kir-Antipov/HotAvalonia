using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HotAvalonia.Helpers;
using MonoMod.Core.Platforms;
using MonoMod.RuntimeDetour;

namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Provides methods for injecting and replacing method implementations at runtime.
/// </summary>
internal static class MethodInjector
{
    /// <summary>
    /// A .NET version that altered the logic behind the <c>RuntimeHelpers.PrepareMethod</c> method,
    /// introducing a breaking change for all code that relied on its ability to immediately
    /// promote a method to Tier1 compilation. With this change, the helper basically became useless,
    /// as calling it now essentially equates to just invoking the method we wanted to pre-JIT.
    /// </summary>
    /// <remarks>
    /// https://github.com/dotnet/runtime/issues/83042
    /// </remarks>
    private static readonly Version s_runtimeVersionThatBrokePrepareMethod = new(7, 0, 0);

    /// <summary>
    /// The .NET version that completely broke bytecode injections.
    /// </summary>
    private static readonly Version s_runtimeVersionThatBrokeBytecodeInjections = new(9, 0, 0);

    /// <summary>
    /// Gets the type of the injection technique supported by the current runtime environment.
    /// </summary>
    public static InjectionType InjectionType { get; } = DetectSupportedInjectionType();

    /// <summary>
    /// Indicates whether method injection is supported in the current runtime environment.
    /// </summary>
    public static bool IsSupported => InjectionType is not InjectionType.None;

    /// <summary>
    /// Indicates whether method injection is supported in optimized assemblies.
    /// </summary>
    public static bool SupportsOptimizedMethods => InjectionType is InjectionType.Native;

    /// <summary>
    /// Injects a replacement method implementation for the specified source method.
    /// </summary>
    /// <param name="source">The method to be replaced.</param>
    /// <param name="replacement">The replacement method implementation.</param>
    /// <returns>An <see cref="IInjection"/> instance representing the method injection.</returns>
    /// <exception cref="InvalidOperationException"/>
    public static IInjection Inject(MethodBase source, MethodInfo replacement) => InjectionType switch
    {
        InjectionType.Bytecode => new BytecodeInjection(source, replacement),
        InjectionType.Native => new NativeInjection(source, replacement),
        _ => ThrowNotSupportedException(),
    };

    /// <summary>
    /// Throws an exception if method injection is not supported in the current runtime environment.
    /// </summary>
    /// <exception cref="InvalidOperationException"/>
    public static void ThrowIfNotSupported()
    {
        if (!IsSupported)
            ThrowNotSupportedException();
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> indicating that method injection is not supported.
    /// </summary>
    /// <returns>This method never returns; it always throws an exception.</returns>
    /// <exception cref="InvalidOperationException">Always thrown to indicate that method injection is not available.</exception>
    private static IInjection ThrowNotSupportedException()
        => throw new InvalidOperationException("Method injection is not available in the current runtime environment.");

    /// <summary>
    /// Determines whether the `dotnet watch` tool is attached to the current process.
    /// </summary>
    /// <returns><c>true</c> if the `dotnet watch` tool is attached; otherwise, <c>false</c>.</returns>
    private static bool IsDotnetWatchAttached()
        => Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1";

    /// <summary>
    /// Detects the type of the method injection technique supported by the current runtime environment.
    /// </summary>
    /// <returns>The <see cref="Inject.InjectionType"/> supported by the current runtime environment.</returns>
    private static InjectionType DetectSupportedInjectionType()
    {
        try
        {
            // Enable dynamic code generation, which is required for MonoMod to function.
            using IDisposable context = AssemblyHelper.ForceAllowDynamicCode();

            // `PlatformTriple.Current` may throw exceptions such as:
            //  - NotImplementedException
            //  - PlatformNotSupportedException
            //  - etc.
            // This happens if the current environment is not (yet) supported.
            if (PlatformTriple.Current is not null)
                return InjectionType.Native;
        }
        catch { }

        if (Environment.Version < s_runtimeVersionThatBrokePrepareMethod)
            return InjectionType.Bytecode;

        bool isJitDisabled = Debugger.IsAttached || IsDotnetWatchAttached();
        if (Environment.Version < s_runtimeVersionThatBrokeBytecodeInjections && isJitDisabled)
            return InjectionType.Bytecode;

        return InjectionType.None;
    }
}

/// <summary>
/// Provides functionality to inject a replacement method using native code hooks.
/// </summary>
file sealed class NativeInjection : IInjection
{
    /// <summary>
    /// The hook used for the method injection.
    /// </summary>
    private readonly Hook _hook;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeInjection"/> class.
    /// </summary>
    /// <param name="source">The method to be replaced.</param>
    /// <param name="replacement">The replacement method implementation.</param>
    public NativeInjection(MethodBase source, MethodInfo replacement)
    {
        // Enable dynamic code generation, which is required for MonoMod to function.
        // Note that we cannot enable it forcefully just once and call it a day,
        // because this only affects the current thread.
        _ = AssemblyHelper.ForceAllowDynamicCode();

        _hook = new(source, replacement, applyByDefault: true);
    }

    /// <summary>
    /// Applies the method injection.
    /// </summary>
    public void Apply() => _hook.Apply();

    /// <summary>
    /// Reverts all the effects caused by the method injection.
    /// </summary>
    public void Undo() => _hook.Undo();

    /// <inheritdoc/>
    public void Dispose() => _hook.Dispose();
}

/// <summary>
/// Provides functionality to inject a replacement method by modifying bytecode references.
/// </summary>
file sealed class BytecodeInjection : IInjection
{
    /// <summary>
    /// The reference to the method's function pointer.
    /// </summary>
    private readonly nint _methodBodyRef;

    /// <summary>
    /// The original method body.
    /// </summary>
    private readonly nint _methodBody;

    /// <summary>
    /// The replacement method body.
    /// </summary>
    private readonly nint _methodBodyReplacement;

    /// <summary>
    /// Initializes a new instance of the <see cref="BytecodeInjection"/> class.
    /// </summary>
    /// <param name="source">The method to be replaced.</param>
    /// <param name="replacement">The replacement method implementation.</param>
    public BytecodeInjection(MethodBase source, MethodBase replacement)
    {
        _methodBodyRef = source.GetFunctionPointerAddress();
        _methodBody = Marshal.ReadIntPtr(_methodBodyRef);
        _methodBodyReplacement = replacement.GetFunctionPointer();

        Apply();
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="BytecodeInjection"/> class.
    /// Reverts the method injection if not already done.
    /// </summary>
    ~BytecodeInjection()
    {
        Undo();
    }

    /// <summary>
    /// Applies the method injection.
    /// </summary>
    public void Apply() => Marshal.WriteIntPtr(_methodBodyRef, _methodBodyReplacement);

    /// <summary>
    /// Reverts all the effects caused by the method injection.
    /// </summary>
    public void Undo() => Marshal.WriteIntPtr(_methodBodyRef, _methodBody);

    /// <inheritdoc/>
    public void Dispose()
    {
        Undo();
        GC.SuppressFinalize(this);
    }
}
