#if NETSTANDARD2_0
using System.Runtime.InteropServices;

namespace HotAvalonia.Helpers;

/// <inheritdoc cref="BitConverter"/>
internal static class BitHelper
{
    /// <inheritdoc cref="BitConverter.ToInt16"/>
    public static short ToInt16(ReadOnlySpan<byte> value) => MemoryMarshal.Read<short>(value);

    /// <inheritdoc cref="BitConverter.ToUInt16"/>
    public static ushort ToUInt16(ReadOnlySpan<byte> value) => MemoryMarshal.Read<ushort>(value);

    /// <inheritdoc cref="BitConverter.ToInt32"/>
    public static int ToInt32(ReadOnlySpan<byte> value) => MemoryMarshal.Read<int>(value);

    /// <inheritdoc cref="BitConverter.ToUInt32"/>
    public static uint ToUInt32(ReadOnlySpan<byte> value) => MemoryMarshal.Read<uint>(value);

    /// <inheritdoc cref="BitConverter.ToInt64"/>
    public static long ToInt64(ReadOnlySpan<byte> value) => MemoryMarshal.Read<long>(value);

    /// <inheritdoc cref="BitConverter.ToUInt64"/>
    public static ulong ToUInt64(ReadOnlySpan<byte> value) => MemoryMarshal.Read<ulong>(value);

    /// <inheritdoc cref="BitConverter.ToSingle"/>
    public static float ToSingle(ReadOnlySpan<byte> value) => MemoryMarshal.Read<float>(value);

    /// <inheritdoc cref="BitConverter.ToDouble"/>
    public static double ToDouble(ReadOnlySpan<byte> value) => MemoryMarshal.Read<double>(value);
}
#endif
