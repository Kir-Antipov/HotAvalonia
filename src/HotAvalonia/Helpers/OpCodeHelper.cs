using System.Reflection;
using System.Reflection.Emit;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides helper methods for working with CIL opcodes.
/// </summary>
internal static class OpCodeHelper
{
    /// <summary>
    /// The flag indicating that the <see cref="OpCode"/> is represented by 2 bytes.
    /// </summary>
    private const int Int16OpCodeFlag = 0xFE00;

    /// <summary>
    /// The marker value for two-byte opcodes.
    /// </summary>
    private const int Int16OpCodeMarker = 0xFE;

    /// <summary>
    /// The instruction set containing all <see cref="OpCode"/> instances.
    /// </summary>
    private static readonly Lazy<OpCode[]> s_opCodes = new(CreateInstructionSet, isThreadSafe: true);

    /// <summary>
    /// Attempts to read an <see cref="OpCode"/> from the given IL span.
    /// </summary>
    /// <param name="il">The span containing the IL bytecode.</param>
    /// <param name="opCode">When this method returns, contains the <see cref="OpCode"/> if the method is successful.</param>
    /// <returns><c>true</c> if an <see cref="OpCode"/> was successfully read; otherwise, <c>false</c>.</returns>
    public static bool TryReadOpCode(ReadOnlySpan<byte> il, out OpCode opCode)
    {
        opCode = OpCodes.Nop;

        if (il.IsEmpty)
            return false;

        int opCodeValue = il[0];
        if (opCodeValue >= Int16OpCodeMarker)
        {
            if (il.Length < 1)
                return false;

            opCodeValue = (opCodeValue << 8) | il[1];
        }

        return TryGetOpCode(opCodeValue, out opCode);
    }

    /// <summary>
    /// Tries to get the <see cref="OpCode"/> associated with the given value.
    /// </summary>
    /// <param name="value">The opcode value.</param>
    /// <param name="opCode">When this method returns, contains the <see cref="OpCode"/> if the method is successful.</param>
    /// <returns><c>true</c> if the <see cref="OpCode"/> was found for the given value; otherwise, <c>false</c>.</returns>
    public static bool TryGetOpCode(int value, out OpCode opCode)
    {
        int index = GetOpCodeIndex(value);
        OpCode[] opCodes = s_opCodes.Value;
        if ((uint)index < (uint)opCodes.Length)
        {
            opCode = opCodes[index];
            return true;
        }

        opCode = OpCodes.Nop;
        return false;
    }

    /// <summary>
    /// Creates an instruction set containing all <see cref="OpCode"/> instances.
    /// </summary>
    /// <returns>An array of <see cref="OpCode"/> instances.</returns>
    private static OpCode[] CreateInstructionSet()
    {
        List<OpCode> opCodes = new(256);
        opCodes.AddRange(ExtractAllOpCodes());

        int maxIndex = opCodes.Max(static x => GetOpCodeIndex(x.Value));
        int instructionSetSize = maxIndex + 1;
        OpCode[] instructionSet = new OpCode[instructionSetSize];

        foreach (OpCode opCode in opCodes)
        {
            instructionSet[GetOpCodeIndex(opCode.Value)] = opCode;
        }

        return instructionSet;
    }

    /// <summary>
    /// Gets the index of the given opcode in the instruction set.
    /// </summary>
    /// <param name="value">The opcode value.</param>
    /// <returns>The index of the opcode.</returns>
    private static int GetOpCodeIndex(int value)
        => (value & Int16OpCodeFlag) == Int16OpCodeFlag ? (256 + (value & 0xFF)) : (value & 0xFF);

    /// <summary>
    /// Extracts all opcodes from the <see cref="OpCodes"/> class.
    /// </summary>
    /// <returns>
    /// All opcodes defined in the <see cref="OpCodes"/> class.
    /// </returns>
    private static IEnumerable<OpCode> ExtractAllOpCodes()
    {
        FieldInfo[] fields = typeof(OpCodes).GetStaticFields();
        return fields.Where(static x => x.FieldType == typeof(OpCode)).Select(static x => (OpCode)x.GetValue(null));
    }

    /// <summary>
    /// Calculates the size of the operand associated with the given opcode.
    /// </summary>
    /// <param name="opCode">The operation code in question.</param>
    /// <returns>
    /// The size of the operand in bytes; or 0 if the opcode has no operand.
    /// </returns>
    public static int GetOperandSize(this OpCode opCode)
        => opCode.Size is 0 ? 0 : GetOperandSize(opCode.OperandType);

    /// <summary>
    /// Determines the size of an operand based on its type.
    /// </summary>
    /// <param name="operandType">The type of the operand.</param>
    /// <returns>
    /// The size of the operand in bytes.
    /// </returns>
    public static int GetOperandSize(this OperandType operandType) => operandType switch
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
}
