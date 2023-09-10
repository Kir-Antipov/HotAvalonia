using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

#if NETSTANDARD2_0
using BitConverter = HotAvalonia.Helpers.BitHelper;
#endif

namespace HotAvalonia.Reflection;

/// <summary>
/// Represents a reader that can parse the byte stream of a method body,
/// reading its IL opcodes and operands.
/// </summary>
internal struct MethodBodyReader
{
    /// <summary>
    /// A flag representing a 16-bit opcode value.
    /// </summary>
    private const short Int16OpCodeFlag = 0xFE;

    /// <summary>
    /// A dictionary containing <see cref="OpCodes"/> mapped to their respective opcode values.
    /// </summary>
    private static readonly Lazy<Dictionary<short, OpCode>> s_opCodes = new(
        static () => GetAllOpCodes().ToDictionary(static x => x.Value));

    /// <summary>
    /// The byte sequence that constitutes the method body.
    /// </summary>
    private readonly ReadOnlyMemory<byte> _methodBody;

    /// <summary>
    /// The current IL opcode being examined.
    /// </summary>
    private OpCode _opCode;

    /// <summary>
    /// The position of the current opcode in the method body.
    /// </summary>
    private int _position;

    /// <summary>
    /// The number of bytes consumed so far while reading the method body.
    /// </summary>
    private int _bytesConsumed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MethodBodyReader"/> struct.
    /// </summary>
    /// <param name="methodBody">The byte sequence that represents the method body to be read.</param>
    public MethodBodyReader(ReadOnlyMemory<byte> methodBody)
    {
        _methodBody = methodBody;
    }

    /// <summary>
    /// The position of the current opcode in the method body.
    /// </summary>
    public readonly int Position => _position;

    /// <summary>
    /// The number of bytes consumed so far while reading the method body.
    /// </summary>
    public readonly int BytesConsumed => _bytesConsumed;

    /// <summary>
    /// The current IL opcode being examined.
    /// </summary>
    public readonly OpCode OpCode => _opCode;

    /// <summary>
    /// The operand associated with the current opcode.
    /// </summary>
    public readonly ReadOnlySpan<byte> Operand
    {
        get
        {
            int size = _opCode.Size;
            if (size is 0)
                return Array.Empty<byte>();

            return _methodBody.Slice(_position + size, GetOperandSize(_opCode.OperandType)).Span;
        }
    }

    /// <summary>
    /// The jump table associated with the "switch" opcode.
    /// </summary>
    public readonly ReadOnlySpan<int> JumpTable
    {
        get
        {
            if (_opCode.Value != OpCodes.Switch.Value)
                return Array.Empty<int>();

            int start = _position + sizeof(byte) + sizeof(int);
            int byteLength = _bytesConsumed - start;

            ReadOnlySpan<byte> jumpTable = _methodBody.Slice(start, byteLength).Span;
            return MemoryMarshal.Cast<byte, int>(jumpTable);
        }
    }

    /// <summary>
    /// Advances the reader to the next opcode in the method body.
    /// </summary>
    /// <returns>
    /// <c>true</c> if the reader successfully advances to the next opcode; otherwise, <c>false</c>.
    /// </returns>
    public bool Next()
    {
        ReadOnlySpan<byte> methodBody = _methodBody.Span;
        int nextPosition = _bytesConsumed;
        if (nextPosition >= methodBody.Length)
            return false;

        short newOpCodeValue = methodBody[nextPosition];
        int operandStart = nextPosition + 1;
        if (newOpCodeValue >= Int16OpCodeFlag)
        {
            if (operandStart >= methodBody.Length)
                return false;

            newOpCodeValue = (short)((newOpCodeValue << 8) | methodBody[operandStart]);
            ++operandStart;
        }

        if (!s_opCodes.Value.TryGetValue(newOpCodeValue, out OpCode newOpCode))
            return false;

        int nextBytesConsumed = operandStart + GetOperandSize(newOpCode.OperandType);
        if (nextBytesConsumed > methodBody.Length)
            return false;

        if (newOpCode.Value == OpCodes.Switch.Value)
        {
            int n = BitConverter.ToInt32(methodBody.Slice(operandStart));
            nextBytesConsumed += n * sizeof(int);
            if (nextBytesConsumed > methodBody.Length)
                return false;
        }

        _opCode = newOpCode;
        _position = nextPosition;
        _bytesConsumed = nextBytesConsumed;

        return true;
    }

    /// <summary>
    /// Searches for the first occurrence of the specified opcode in the given method body and returns its index position.
    /// </summary>
    /// <param name="methodBody">The method body in which to search for the opcode.</param>
    /// <param name="opCode">The opcode to search for.</param>
    /// <returns>
    /// The zero-based index of the first occurrence of the specified op code in the method body;
    /// or -1 if the opcode is not found.
    /// </returns>
    public static int IndexOf(ReadOnlyMemory<byte> methodBody, short opCode)
    {
        MethodBodyReader reader = new(methodBody);
        while (reader.Next())
        {
            if (reader.OpCode.Value == opCode)
                return reader.Position;
        }
        return -1;
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a byte.
    /// </summary>
    /// <returns>The byte representation of the operand.</returns>
    public readonly byte GetByte()
    {
        EnsureOperandSize(sizeof(byte));

        return _methodBody.Span[_position + _opCode.Size];
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a signed byte (sbyte).
    /// </summary>
    /// <returns>The signed byte representation of the operand.</returns>
    public readonly sbyte GetSByte()
    {
        EnsureOperandSize(sizeof(sbyte));

        return (sbyte)_methodBody.Span[_position + _opCode.Size];
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a 16-bit integer (short).
    /// </summary>
    /// <returns>The 16-bit integer representation of the operand.</returns>
    public readonly short GetInt16()
    {
        EnsureOperandSize(sizeof(short));

        return BitConverter.ToInt16(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as an unsigned 16-bit integer (ushort).
    /// </summary>
    /// <returns>The unsigned 16-bit integer representation of the operand.</returns>
    public readonly ushort GetUInt16()
    {
        EnsureOperandSize(sizeof(ushort));

        return BitConverter.ToUInt16(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a 32-bit integer (int).
    /// </summary>
    /// <returns>The 32-bit integer representation of the operand.</returns>
    public readonly int GetInt32()
    {
        EnsureOperandSize(sizeof(int));

        return BitConverter.ToInt32(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as an unsigned 32-bit integer (uint).
    /// </summary>
    /// <returns>The unsigned 32-bit integer representation of the operand.</returns>
    public readonly uint GetUInt32()
    {
        EnsureOperandSize(sizeof(uint));

        return BitConverter.ToUInt32(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a 64-bit integer (long).
    /// </summary>
    /// <returns>The 64-bit integer representation of the operand.</returns>
    public readonly long GetInt64()
    {
        EnsureOperandSize(sizeof(long));

        return BitConverter.ToInt64(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as an unsigned 64-bit integer (ulong).
    /// </summary>
    /// <returns>The unsigned 64-bit integer representation of the operand.</returns>
    public readonly ulong GetUInt64()
    {
        EnsureOperandSize(sizeof(ulong));

        return BitConverter.ToUInt64(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a single-precision floating-point number (float).
    /// </summary>
    /// <returns>The single-precision floating-point representation of the operand.</returns>
    public readonly float GetSingle()
    {
        EnsureOperandSize(sizeof(float));

        return BitConverter.ToSingle(Operand);
    }

    /// <summary>
    /// Retrieves the operand of the current opcode as a double-precision floating-point number (double).
    /// </summary>
    /// <returns>The double-precision floating-point representation of the operand.</returns>
    public readonly double GetDouble()
    {
        EnsureOperandSize(sizeof(double));

        return BitConverter.ToDouble(Operand);
    }

    /// <summary>
    /// Resolves a signature using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the signature token in.</param>
    /// <returns>The byte array representing the signature.</returns>
    public readonly byte[] ResolveSignature(Module module)
        => module.ResolveSignature(GetInt32());

    /// <summary>
    /// Resolves a string using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the string token in.</param>
    /// <returns>The resolved string.</returns>
    public readonly string ResolveString(Module module)
        => module.ResolveString(GetInt32());

    /// <summary>
    /// Resolves a field using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the field token in.</param>
    /// <returns>The <see cref="FieldInfo"/> object representing the resolved field.</returns>
    public readonly FieldInfo ResolveField(Module module)
        => module.ResolveField(GetInt32());

    /// <summary>
    /// Resolves a field using the specified module and generic arguments.
    /// </summary>
    /// <param name="module">The module to resolve the field token in.</param>
    /// <param name="genericTypeArguments">The type arguments for a generic type.</param>
    /// <param name="genericMethodArguments">The type arguments for a generic method.</param>
    /// <returns>The <see cref="FieldInfo"/> object representing the resolved field.</returns>
    public readonly FieldInfo ResolveField(Module module, Type[] genericTypeArguments, Type[] genericMethodArguments)
        => module.ResolveField(GetInt32(), genericTypeArguments, genericMethodArguments);

    /// <summary>
    /// Resolves a method using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the method token in.</param>
    /// <returns>The <see cref="MethodBase"/> object representing the resolved method.</returns>
    public readonly MethodBase ResolveMethod(Module module)
        => module.ResolveMethod(GetInt32());

    /// <summary>
    /// Resolves a method using the specified module and generic arguments.
    /// </summary>
    /// <param name="module">The module to resolve the method token in.</param>
    /// <param name="genericTypeArguments">The type arguments for a generic type.</param>
    /// <param name="genericMethodArguments">The type arguments for a generic method.</param>
    /// <returns>The <see cref="MethodBase"/> object representing the resolved method.</returns>
    public readonly MethodBase ResolveMethod(Module module, Type[] genericTypeArguments, Type[] genericMethodArguments)
        => module.ResolveMethod(GetInt32(), genericTypeArguments, genericMethodArguments);

    /// <summary>
    /// Resolves a member using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the member token in.</param>
    /// <returns>The <see cref="MemberInfo"/> object representing the resolved member.</returns>
    public readonly MemberInfo ResolveMember(Module module)
        => module.ResolveMember(GetInt32());

    /// <summary>
    /// Resolves a member using the specified module and generic arguments.
    /// </summary>
    /// <param name="module">The module to resolve the member token in.</param>
    /// <param name="genericTypeArguments">The type arguments for a generic type.</param>
    /// <param name="genericMethodArguments">The type arguments for a generic method.</param>
    /// <returns>The <see cref="MemberInfo"/> object representing the resolved member.</returns>
    public readonly MemberInfo ResolveMember(Module module, Type[] genericTypeArguments, Type[] genericMethodArguments)
        => module.ResolveMember(GetInt32(), genericTypeArguments, genericMethodArguments);

    /// <summary>
    /// Resolves a type using the specified module.
    /// </summary>
    /// <param name="module">The module to resolve the type token in.</param>
    /// <returns>The <see cref="Type"/> object representing the resolved type.</returns>
    public readonly Type ResolveType(Module module)
        => module.ResolveType(GetInt32());

    /// <summary>
    /// Resolves a type using the specified module and generic arguments.
    /// </summary>
    /// <param name="module">The module to resolve the type token in.</param>
    /// <param name="genericTypeArguments">The type arguments for a generic type.</param>
    /// <param name="genericMethodArguments">The type arguments for a generic method.</param>
    /// <returns>The <see cref="Type"/> object representing the resolved type.</returns>
    public readonly Type ResolveType(Module module, Type[] genericTypeArguments, Type[] genericMethodArguments)
        => module.ResolveType(GetInt32(), genericTypeArguments, genericMethodArguments);

    /// <summary>
    /// Ensures that the operand size of the current opcode matches the expected size.
    /// </summary>
    /// <param name="size">The expected size of the operand in bytes.</param>
    /// <exception cref="InvalidOperationException">Thrown when the operand size does not match the expected size.</exception>
    private readonly void EnsureOperandSize(int size)
    {
        if (GetOperandSize(_opCode) == size)
            return;

        ThrowInvalidOperationException_SizeDoesNotMatch(size, GetOperandSize(_opCode));

        static void ThrowInvalidOperationException_SizeDoesNotMatch(int expectedSize, int actualSize)
            => throw new InvalidOperationException($"The operand size ({actualSize} bytes) does not match the expected size ({expectedSize} bytes).");
    }

    /// <summary>
    /// Calculates the size of the operand associated with the given opcode.
    /// </summary>
    /// <param name="opCode">The operation code in question.</param>
    /// <returns>
    /// The size of the operand in bytes; or 0 if the opcode has no operand.
    /// </returns>
    private static int GetOperandSize(OpCode opCode)
        => opCode.Size is 0 ? 0 : GetOperandSize(opCode.OperandType);

    /// <summary>
    /// Determines the size of an operand based on its type.
    /// </summary>
    /// <param name="operandType">The type of the operand.</param>
    /// <returns>
    /// The size of the operand in bytes.
    /// </returns>
    public static int GetOperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineSwitch or OperandType.InlineTok or OperandType.InlineType
            or OperandType.ShortInlineR => sizeof(int),
        OperandType.InlineI8 or OperandType.InlineR => sizeof(long),
        OperandType.InlineVar => sizeof(short),
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => sizeof(byte),
        _ => 0,
    };

    /// <summary>
    /// Retrieves all opcodes defined in the <see cref="OpCodes"/> class.
    /// </summary>
    /// <returns>
    /// All opcodes defined in the <see cref="OpCodes"/> class.
    /// </returns>
    private static IEnumerable<OpCode> GetAllOpCodes()
    {
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        return fields.Where(static x => x.FieldType == typeof(OpCode)).Select(static x => (OpCode)x.GetValue(null));
    }
}
