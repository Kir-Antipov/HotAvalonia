using System.Reflection;
using System.Reflection.Emit;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides helper methods to facilitate IL code emission.
/// </summary>
internal static class ILGeneratorHelper
{
    /// <summary>
    /// Emits an opcode to load a native integer constant onto the evaluation stack.
    /// </summary>
    /// <param name="generator">The IL generator used for code emission.</param>
    /// <param name="value">The native integer value to load.</param>
    public static void EmitLdc_IN(this ILGenerator generator, nint value)
    {
        if (IntPtr.Size is sizeof(int))
            generator.Emit(OpCodes.Ldc_I4, (int)value);
        else
            generator.Emit(OpCodes.Ldc_I8, value);
    }

    /// <summary>
    /// Emits an opcode to load a 4-byte integer constant onto the evaluation stack.
    /// </summary>
    /// <param name="generator">The IL generator used for code emission.</param>
    /// <param name="value">The 4-byte integer value to load.</param>
    public static void EmitLdc_I4(this ILGenerator generator, int value)
    {
        switch (value)
        {
            case -1:
                generator.Emit(OpCodes.Ldc_I4_M1);
                return;

            case 0:
                generator.Emit(OpCodes.Ldc_I4_0);
                return;

            case 1:
                generator.Emit(OpCodes.Ldc_I4_1);
                return;

            case 2:
                generator.Emit(OpCodes.Ldc_I4_2);
                return;

            case 3:
                generator.Emit(OpCodes.Ldc_I4_3);
                return;

            case 4:
                generator.Emit(OpCodes.Ldc_I4_4);
                return;

            case 5:
                generator.Emit(OpCodes.Ldc_I4_5);
                return;

            case 6:
                generator.Emit(OpCodes.Ldc_I4_6);
                return;

            case 7:
                generator.Emit(OpCodes.Ldc_I4_7);
                return;

            case 8:
                generator.Emit(OpCodes.Ldc_I4_8);
                return;

            default:
                generator.Emit(OpCodes.Ldc_I4, value);
                return;
        }
    }

    /// <summary>
    /// Emits an opcode to load an argument onto the evaluation stack.
    /// </summary>
    /// <param name="generator">The IL generator used for code emission.</param>
    /// <param name="index">The index of the argument to load.</param>
    public static void EmitLdarg(this ILGenerator generator, int index)
    {
        switch (index)
        {
            case 0:
                generator.Emit(OpCodes.Ldarg_0);
                return;

            case 1:
                generator.Emit(OpCodes.Ldarg_1);
                return;

            case 2:
                generator.Emit(OpCodes.Ldarg_2);
                return;

            case 3:
                generator.Emit(OpCodes.Ldarg_3);
                return;

            case > 4 and <= byte.MaxValue:
                generator.Emit(OpCodes.Ldarg_S, (byte)index);
                return;

            case <= short.MaxValue:
                generator.Emit(OpCodes.Ldarg, (short)index);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Emits an opcode to load a default value onto the evaluation stack based on the given type.
    /// </summary>
    /// <param name="generator">The IL generator used for code emission.</param>
    /// <param name="type">The type for which to load the default value.</param>
    public static void EmitLddefault(this ILGenerator generator, Type type)
    {
        if (!type.IsValueType)
        {
            generator.Emit(OpCodes.Ldnull);
            return;
        }

        LocalBuilder valueInitializer = generator.DeclareLocal(type);
        generator.Emit(OpCodes.Ldloca, valueInitializer);
        generator.Emit(OpCodes.Initobj, type);
        generator.Emit(OpCodes.Ldloc, valueInitializer);
    }

    /// <summary>
    /// Emits a call to the specified method, using the appropriate opcode based on whether the method is virtual.
    /// </summary>
    /// <param name="generator">The IL generator used for code emission.</param>
    /// <param name="method">The method to be called.</param>
    public static void EmitCall(this ILGenerator generator, MethodBase method)
    {
        if (method is MethodInfo methodInfo)
        {
            generator.EmitCall(methodInfo.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, methodInfo, null);
        }
        else
        {
            generator.EmitLdc_IN(method.GetFunctionPointer());
            generator.EmitCalli(OpCodes.Calli, method.CallingConvention, method.GetReturnType(), method.GetParameterTypes(), null);
        }
    }
}
