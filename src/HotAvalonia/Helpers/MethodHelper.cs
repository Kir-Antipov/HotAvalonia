using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for manipulating and obtaining information about methods.
/// </summary>
internal static class MethodHelper
{
    /// <summary>
    /// A .NET version that altered the logic behind the <see cref="RuntimeHelpers.PrepareMethod"/> method,
    /// introducing a breaking change for all code that relied on its ability to immediately
    /// promote a method to Tier1 compilation. With this change, the helper basically became useless,
    /// as calling it now essentially equates to just invoking the method we wanted to pre-JIT.
    /// </summary>
    /// <remarks>
    /// https://github.com/dotnet/runtime/issues/83042
    /// </remarks>
    private static readonly Version s_runtimeVersionThatBrokePrepareMethod = new(7, 0, 0);

    /// <summary>
    /// Ensures that method swapping is available in the current runtime environment.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if method swapping is not available.</exception>
    public static void EnsureMethodSwappingIsAvailable()
    {
        if (!IsMethodSwappingAvailable())
            throw new InvalidOperationException("Method swapping is not available in the current runtime environment.");
    }

    /// <summary>
    /// Checks if method swapping is available in the current runtime environment.
    /// </summary>
    /// <returns><c>true</c> if method swapping is available; otherwise, <c>false</c>.</returns>
    public static bool IsMethodSwappingAvailable()
    {
        if (Environment.Version < s_runtimeVersionThatBrokePrepareMethod)
            return true;

        bool isJitDisabled = Debugger.IsAttached || IsDotnetWatchAttached();
        return isJitDisabled;
    }

    /// <summary>
    /// Determines whether the `dotnet watch` tool is attached to the current process.
    /// </summary>
    /// <returns><c>true</c> if the `dotnet watch` tool is attached; otherwise, <c>false</c>.</returns>
    private static bool IsDotnetWatchAttached()
        => Environment.GetEnvironmentVariable("DOTNET_WATCH") is "1";

    /// <summary>
    /// Gets the type of the instance for instance methods or <c>null</c> for static methods.
    /// </summary>
    /// <param name="method">The method for which to get the instance type.</param>
    /// <returns>
    /// The declaring type of the method if it's an instance method;
    /// otherwise, <c>null</c> for static methods.
    /// </returns>
    public static Type? GetThisType(this MethodBase method)
    {
        if (method.IsStatic)
            return null;

        Type declaringType = method.DeclaringType;
        return declaringType.IsValueType ? declaringType.MakeByRefType() : declaringType;
    }

    /// <summary>
    /// Gets the return type of the method.
    /// </summary>
    /// <param name="method">The method for which to get the return type.</param>
    /// <returns>The return type of the method.</returns>
    public static Type GetReturnType(this MethodBase method)
        => method is MethodInfo methodInfo ? methodInfo.ReturnType : typeof(void);

    /// <summary>
    /// Gets an array of the parameter types for the method.
    /// </summary>
    /// <param name="method">The method for which to get the parameter types.</param>
    /// <returns>An array of the parameter types of the method.</returns>
    public static Type[] GetParameterTypes(this MethodBase method)
        => Array.ConvertAll(method.GetParameters(), static x => x.ParameterType);

    /// <summary>
    /// Gets the delegate type that matches the method's signature.
    /// </summary>
    /// <param name="method">The method for which to get the delegate type.</param>
    /// <returns>A delegate type that matches the method's signature.</returns>
    public static Type GetDelegateType(this MethodBase method)
        => Expression.GetDelegateType([.. method.GetParameterTypes(), method.GetReturnType()]);

    /// <summary>
    /// Gets the delegate type that matches the method's signature,
    /// including the instance type for instance methods.
    /// </summary>
    /// <param name="method">The method for which to get the static delegate type.</param>
    /// <returns>
    /// A delegate type that matches the method's signature,
    /// including the instance type for instance methods.
    /// </returns>
    public static Type GetStaticDelegateType(this MethodBase method)
        => Expression.GetDelegateType([
            .. method.GetThisType() is Type thisType ? [thisType] : Type.EmptyTypes,
            .. method.GetParameterTypes(),
            method.GetReturnType()
        ]);

    /// <summary>
    /// Validates the compatibility of a method's parameter types with a given signature.
    /// </summary>
    /// <param name="signature">An array of types that define the expected method parameter types.</param>
    /// <param name="method">The method under examination.</param>
    /// <returns>A boolean value indicating whether the method's parameter types align with the provided signature.</returns>
    public static bool IsSignatureAssignableFrom(Type[] signature, MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != signature.Length)
            return false;

        for (int i = 0; i < signature.Length; ++i)
        {
            if (!signature[i].IsAssignableFrom(parameters[i].ParameterType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Replaces the implementation of a source method with that of a replacement method.
    /// </summary>
    /// <param name="source">The method whose implementation will be overridden.</param>
    /// <param name="replacement">The method that will provide the new implementation.</param>
    public static void OverrideMethod(MethodBase source, MethodBase replacement)
        => OverrideMethod(source, replacement, out _);

    /// <summary>
    /// Replaces the implementation of a source method with that of a replacement method and
    /// returns the original method's function pointer.
    /// </summary>
    /// <param name="source">The method whose implementation will be overridden.</param>
    /// <param name="replacement">The method that will provide the new implementation.</param>
    /// <param name="sourceFunctionPointer">The original function pointer of the source method.</param>
    public static void OverrideMethod(MethodBase source, MethodBase replacement, out nint sourceFunctionPointer)
    {
        nint sourceAddress = GetFunctionPointerAddress(source);

        sourceFunctionPointer = Marshal.ReadIntPtr(sourceAddress);
        nint replacementFunctionPointer = GetFunctionPointer(replacement);

        Marshal.WriteIntPtr(sourceAddress, replacementFunctionPointer);
    }

    /// <summary>
    /// Swaps the implementations of two methods.
    /// </summary>
    /// <param name="left">The first method to swap.</param>
    /// <param name="right">The second method to swap.</param>
    public static void SwapMethods(MethodBase left, MethodBase right)
    {
        nint leftAddress = GetFunctionPointerAddress(left);
        nint rightAddress = GetFunctionPointerAddress(right);

        nint leftFunctionPointer = Marshal.ReadIntPtr(leftAddress);
        nint rightFunctionPointer = Marshal.ReadIntPtr(rightAddress);

        Marshal.WriteIntPtr(leftAddress, rightFunctionPointer);
        Marshal.WriteIntPtr(rightAddress, leftFunctionPointer);
    }

    /// <summary>
    /// Obtains the function pointer for a given method.
    /// </summary>
    /// <param name="method">The method for which to obtain the function pointer.</param>
    /// <returns>The function pointer of the method.</returns>
    public static nint GetFunctionPointer(this MethodBase method)
    {
        RuntimeHelpers.PrepareMethod(method.MethodHandle);

        return method.MethodHandle.GetFunctionPointer();
    }

    /// <summary>
    /// Obtains the memory address at which a method's function pointer is stored.
    /// </summary>
    /// <param name="method">The method for which to obtain the function pointer address.</param>
    /// <returns>The memory address of the method's function pointer.</returns>
    public static nint GetFunctionPointerAddress(this MethodBase method)
    {
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        nint address;

        if (method.IsVirtual && method.DeclaringType is not null)
        {
            const int methodTableOffset32 = 40;
            const int methodTableOffset64 = 64;
            int methodTableOffset = IntPtr.Size == sizeof(int) ? methodTableOffset32 : methodTableOffset64;

            nint methodDescriptor = method.MethodHandle.Value;
            nint methodTable = method.DeclaringType.TypeHandle.Value;

            int methodIndex = (int)((Marshal.ReadInt64(methodDescriptor) >> 32) & 0xFFFF);
            nint firstVirtualMethodAddress = Marshal.ReadIntPtr(methodTable + methodTableOffset);

            address = firstVirtualMethodAddress + methodIndex * IntPtr.Size;
        }
        else
        {
            address = method.MethodHandle.Value + sizeof(long);
        }

        if (Marshal.ReadIntPtr(address) != method.MethodHandle.GetFunctionPointer())
            throw new InvalidOperationException("Unable to determine the function pointer address.");

        return address;
    }
}
