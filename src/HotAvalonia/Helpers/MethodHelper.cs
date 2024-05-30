using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Cecil;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for obtaining information about methods.
/// </summary>
internal static class MethodHelper
{
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

    /// <summary>
    /// Gets the file path of the source code file where the specified method is defined.
    /// </summary>
    /// <param name="method">The method to get the file path for.</param>
    /// <returns>
    /// The file path of the source code file where the specified method is defined,
    /// or <c>null</c> if the path cannot be determined.
    /// </returns>
    public static string? GetFilePath(this MethodBase method)
    {
        if (method is not { DeclaringType: { FullName.Length: > 0, Assembly.IsDynamic: false } })
            return null;

        string? location = method.DeclaringType.Assembly.Location;
        if (location is null || !File.Exists(location))
            return null;

        ParameterInfo[] parameters = method.GetParameters();
        TypeDefinition typeDefinition;
        try
        {
            AssemblyDefinition asmDefinition = AssemblyDefinition.ReadAssembly(location, new() { ReadSymbols = true });
            typeDefinition = asmDefinition.MainModule.GetType(method.DeclaringType.FullName);
        }
        catch
        {
            return null;
        }

        foreach (MethodDefinition methodDefinition in typeDefinition.Methods)
        {
            if (methodDefinition.Name != method.Name)
                continue;

            if (methodDefinition.ReturnType.Name != method.GetReturnType().Name)
                continue;

            if (methodDefinition.Parameters.Count != parameters.Length)
                continue;

            bool hasSameParameters = parameters
                .Zip(methodDefinition.Parameters, static (x, y) => x.ParameterType.Name == y.ParameterType.Name)
                .All(static x => x);

            if (!hasSameParameters)
                continue;

            return methodDefinition.DebugInformation.SequencePoints.FirstOrDefault()?.Document.Url;
        }
        return null;
    }
}
