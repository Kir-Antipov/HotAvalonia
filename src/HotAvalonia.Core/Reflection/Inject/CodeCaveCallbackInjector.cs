using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HotAvalonia.Helpers;

namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Provides callback injection capabilities by overwriting the body of a target method with a stub that redirects execution to a callback function.
/// </summary>
internal sealed class CodeCaveCallbackInjector : RuntimeCallbackInjector
{
    /// <summary>
    /// Gets a value indicating whether this injector is supported in the current environment.
    /// </summary>
    public static bool IsSupported => RuntimeInformation.ProcessArchitecture
        is Architecture.X86 or Architecture.X64 && IsJitOptimizerDisabled;

    public override InjectionType InjectionType => InjectionType.CodeCave;

    protected override IDisposable CreateHook(MethodBase target, MethodInfo callback, object? thisArg = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(callback);
        if (thisArg is not null)
            ArgumentException.Throw(nameof(thisArg), "Bound callbacks are not supported.");

        nint fromAddress = GetStubAddress(target);
        nint toAddress = GetStubAddress(callback);
        return new Hook(fromAddress, GenerateStub(toAddress));
    }

    protected override void LoadDelegate(ILGenerator il, MethodBase target, Type delegateType, ref Action<IDisposable> ctor, MethodBuilder dispose)
    {
        TypeBuilder builder = (TypeBuilder)dispose.DeclaringType!;
        FieldInfo delegateField = builder.DefineField("s_delegate", delegateType, FieldAttributes.Private | FieldAttributes.Static);
        Delegate targetDelegate = CreateDelegateClone(target, delegateType);
        ctor += x => x.GetType().GetField(delegateField.Name, (BindingFlags)(-1)).SetValue(null, targetDelegate);

        il.Emit(OpCodes.Ldsfld, delegateField);
        dispose.GetILGenerator().Emit(OpCodes.Ldnull);
        dispose.GetILGenerator().Emit(OpCodes.Stsfld, delegateField);
    }

    private static nint GetStubAddress(MethodBase method)
    {
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        nint stubPtr = Marshal.ReadIntPtr(method.MethodHandle.Value, IntPtr.Size * 2);

        // This is a terrible, terrible heuristic!
        //
        // So, In older runtimes, the pointer we obtain might not be a pointer at all, but a bunch of flags.
        // In that case, the pointer we are actually looking for is located one nint before that.
        // To detect this in a runtime-agnostic manner, we basically need to check whether the value we read
        // "looks like a pointer". How on earth is one supposed to implement that? Great question, actually!
        // Unfortunately, the answer to it is there is no way of doing so :)
        //
        // That said, thanks to "modern" memory mapping, function pointers for managed methods tend to be
        // located relatively close to each other. I.e., if a value is orders of magnitude away from
        // something we definitely know to be a function pointer, it is probably a flag, and vice versa.
        // As of .NET 10 (at least in my testing), this heuristic is no longer necessary, as the pointer
        // now appears to always reside in a predictable location. Thus, it is better to disable the check
        // entirely to avoid false positives.
        //
        // Also, please note: this is god-awful code and absolutely not suitable for production use.
        // It is good enough™ for this project because the worst it can do in the event of a misfire
        // is corrupt some memory and crash the application being debugged, which I find acceptable
        // given the circumstances.
        // However, if you came here looking for an injection technique for some serious business logic,
        // do not blindly copy this questionable code. Go install MonoMod instead.
        nint functionPtr = method.MethodHandle.GetFunctionPointer();
        if (Environment.Version.Major < 10 && (stubPtr == 0 || functionPtr / stubPtr + stubPtr / functionPtr >= 0xF))
            return Marshal.ReadIntPtr(method.MethodHandle.Value, IntPtr.Size);

        return stubPtr;
    }

    private static byte[] GenerateStub(nint targetAddress)
    {
        byte[] stub;
        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X86:
                stub = [
                    0x68, 0, 0, 0, 0,   // push <targetAddress>
                    0xC3,               // ret
                ];
                BitConverter.TryWriteBytes(stub.AsSpan(1, sizeof(int)), (int)targetAddress);
                break;

            case Architecture.X64:
                stub = [
                    0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, // mov rax <targetAddress>
                    0x50,                               // push rax
                    0xC3,                               // ret
                ];
                BitConverter.TryWriteBytes(stub.AsSpan(2, sizeof(long)), targetAddress);
                break;

            case Architecture.Arm64:
                stub = [
                    0x5E, 0x00, 0x00, 0x58, // ldr x30, target
                    0xC0, 0x03, 0x5F, 0xD6, // ret
                    0, 0, 0, 0, 0, 0, 0, 0, // target: .quad <targetAddress>
                ];
                BitConverter.TryWriteBytes(stub.AsSpan(8, sizeof(long)), targetAddress);
                break;

            default:
                PlatformNotSupportedException.Throw();
                stub = [];
                break;
        }
        return stub;
    }

    private static Delegate CreateDelegateClone(MethodBase method, Type delegateType)
    {
        MethodInfo invoke = delegateType.GetMethod(nameof(Action.Invoke))!;
        DynamicMethod clonedMethod = method switch
        {
            { DeclaringType: Type owner } => new(method.Name, invoke.ReturnType, invoke.GetParameterTypes(), owner),
            _ => new(method.Name, invoke.ReturnType, invoke.GetParameterTypes(), method.Module),
        };
        CloneMethodBody(clonedMethod.GetILGenerator(), method, out bool initLocals);
        clonedMethod.InitLocals = initLocals;
        return clonedMethod.CreateDelegate(delegateType);
    }

    private static void CloneMethodBody(ILGenerator il, MethodBase method, out bool initLocals)
    {
        MethodBody? body = method.GetMethodBody();
        byte[]? ilBody = body?.GetILAsByteArray();
        if (body is null || ilBody is not { Length: > 0 })
            ArgumentException.Throw(nameof(method), "Unable to clone a method with no body.");

        initLocals = body.InitLocals;
        Module metadataTokenResolver = method.Module;
        Type[]? genericTypeArguments = method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null;
        Type[]? genericMethodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        il.DeclareLocals(body.LocalVariables);
        Dictionary<int, Label> labels = il.DefineLabels(ilBody);
        IList<ExceptionHandlingClause> clauses = body.ExceptionHandlingClauses;
        List<ExceptionRegionBoundary> boundaries = ExceptionRegionBoundary.ToBoundaries(clauses, clauses.Count);
        int boundaryIndex = MarkExceptionRegionBoundary(il, 0, boundaries, 0);
        if (labels.TryGetValue(0, out Label label))
            il.MarkLabel(label);

        MethodBodyReader reader = new(ilBody);
        while (reader.Next())
        {
            OpCode opCode = reader.OpCode;
            int labelOffset = reader.BytesConsumed;
            switch (opCode.OperandType)
            {
                // ILGenerator automatically emits endfilter, endfinally, or leave whenever we exit an exception region.
                // And there is no way to prevent this, because methods such as BeginExceptionBlock, EndExceptionBlock,
                // etc. are the only supported way to emit exception regions.
                //
                // So, if we detect one of these instructions and it's located immediately before the end of
                // an exception region, we avoid emitting it ourselves and let ILGenerator do its dirty job.
                //
                // Note that endfilter does not require such a check, because according to ECMA-335 it MUST be the last instruction
                // in a filter block. The same cannot be said for endfinally, though, as it is technically valid, albeit under very
                // esoteric circumstances, to have multiple endfinally instructions within the same finally block.
                case OperandType.InlineNone when opCode == OpCodes.Endfilter:
                case OperandType.InlineNone when opCode == OpCodes.Endfinally && IsEndBoundary(labelOffset, boundaries, boundaryIndex):
                case OperandType.InlineBrTarget when opCode == OpCodes.Leave && IsEndBoundary(labelOffset, boundaries, boundaryIndex):
                case OperandType.ShortInlineBrTarget when opCode == OpCodes.Leave_S && IsEndBoundary(labelOffset, boundaries, boundaryIndex):
                    break;

                case OperandType.InlineBrTarget:
                    il.Emit(opCode, labels[labelOffset + reader.GetInt32()]);
                    break;

                case OperandType.InlineI:
                    il.Emit(opCode, reader.GetInt32());
                    break;

                case OperandType.InlineI8:
                    il.Emit(opCode, reader.GetInt64());
                    break;

                case OperandType.InlineNone:
                    il.Emit(opCode);
                    break;

                case OperandType.InlineR:
                    il.Emit(opCode, reader.GetDouble());
                    break;

                case OperandType.InlineString:
                    il.Emit(opCode, metadataTokenResolver.ResolveString(reader.GetInt32()));
                    break;

                case OperandType.InlineVar:
                    il.Emit(opCode, reader.GetInt16());
                    break;

                case OperandType.ShortInlineR:
                    il.Emit(opCode, reader.GetSingle());
                    break;

                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    il.Emit(opCode, reader.GetByte());
                    break;

                case OperandType.InlineSwitch:
                    ReadOnlySpan<int> jumpTable = reader.JumpTable;
                    Label[] switchLabels = new Label[jumpTable.Length];
                    for (int i = 0; i < switchLabels.Length; i++)
                        switchLabels[i] = labels[labelOffset + jumpTable[i]];

                    il.Emit(opCode, switchLabels);
                    break;

                case OperandType.ShortInlineBrTarget:
                    // There is no way to ensure that ILGenerator actually emits exactly what we ask it to emit
                    // (which is pretty messed up, I know; what a wonderful "low-level" API).
                    // As a result, there is also no way to guarantee that jump instructions will be able to use
                    // their short forms if the method body unexpectedly grows.
                    // To be safe, we therefore de-optimize all such opcodes to their long forms.
                    opCode = opCode switch
                    {
                        _ when opCode == OpCodes.Br_S => OpCodes.Br,
                        _ when opCode == OpCodes.Brtrue_S => OpCodes.Brtrue,
                        _ when opCode == OpCodes.Brfalse_S => OpCodes.Brfalse,
                        _ when opCode == OpCodes.Leave_S => OpCodes.Leave,
                        _ when opCode == OpCodes.Beq_S => OpCodes.Beq,
                        _ when opCode == OpCodes.Bne_Un_S => OpCodes.Bne_Un,
                        _ when opCode == OpCodes.Bge_S => OpCodes.Bge,
                        _ when opCode == OpCodes.Bge_Un_S => OpCodes.Bge_Un,
                        _ when opCode == OpCodes.Bgt_S => OpCodes.Bgt,
                        _ when opCode == OpCodes.Bgt_Un_S => OpCodes.Bgt_Un,
                        _ when opCode == OpCodes.Ble_S => OpCodes.Ble,
                        _ when opCode == OpCodes.Ble_Un_S => OpCodes.Ble_Un,
                        _ when opCode == OpCodes.Blt_S => OpCodes.Blt,
                        _ when opCode == OpCodes.Blt_Un_S => OpCodes.Blt_Un,
                        _ => opCode,
                    };
                    il.Emit(opCode, labels[labelOffset + reader.GetSByte()]);
                    break;

                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    switch (metadataTokenResolver.ResolveMember(reader.GetInt32(), genericTypeArguments, genericMethodArguments))
                    {
                        case FieldInfo inlineField:
                            il.Emit(opCode, inlineField);
                            break;

                        case MethodInfo inlineMethod:
                            il.Emit(opCode, inlineMethod);
                            break;

                        case ConstructorInfo inlineCtor:
                            il.Emit(opCode, inlineCtor);
                            break;

                        case Type inlineType:
                            il.Emit(opCode, inlineType);
                            break;

                        default:
                            goto operandTypeDefault;
                    }
                    break;

                // One valid opcode we currently cannot process is calli, because re-emitting it would require
                // parsing a method signature from a byte array. There is zero benefit in doing that for this
                // project, as the methods we target never use calli. However, I have a library in the works
                // that already includes a signature parser, so when I inevitably move HotAvalonia to it, this
                // limitation can be addressed as well.
                default:
                operandTypeDefault:
                    throw new InvalidOperationException($"Invalid or unknown opcode '{opCode}' at 'IL_{reader.Position:X4}'.");
            }

            boundaryIndex = MarkExceptionRegionBoundary(il, labelOffset, boundaries, boundaryIndex);
            if (labels.TryGetValue(labelOffset, out label))
                il.MarkLabel(label);
        }

        static bool IsEndBoundary(int offset, List<ExceptionRegionBoundary> boundaries, int i)
        {
            for (; (uint)i < (uint)boundaries.Count; i++)
            {
                ExceptionRegionBoundary boundary = boundaries[i];
                if (boundary.Offset != offset)
                    break;

                if ((boundary.Kind & ExceptionRegionBoundaryKind.End) != 0)
                    return true;
            }
            return false;
        }

        static int MarkExceptionRegionBoundary(ILGenerator il, int offset, List<ExceptionRegionBoundary> boundaries, int i)
        {
            for (; (uint)i < (uint)boundaries.Count; i++)
            {
                ExceptionRegionBoundary boundary = boundaries[i];
                if (boundary.Offset != offset)
                    break;

                switch (boundary.Kind)
                {
                    case ExceptionRegionBoundaryKind.TryStart:
                        il.BeginExceptionBlock();
                        break;

                    case ExceptionRegionBoundaryKind.FilterStart:
                        il.BeginExceptFilterBlock();
                        break;

                    case ExceptionRegionBoundaryKind.CatchStart when (boundary.Clause.Flags & ExceptionHandlingClauseOptions.Filter) != 0:
                        il.BeginCatchBlock(exceptionType: null!);
                        break;

                    case ExceptionRegionBoundaryKind.CatchStart:
                        il.BeginCatchBlock(boundary.Clause.CatchType);
                        break;

                    case ExceptionRegionBoundaryKind.FinallyStart:
                        il.BeginFinallyBlock();
                        break;

                    case ExceptionRegionBoundaryKind.FaultStart:
                        il.BeginFaultBlock();
                        break;

                    case ExceptionRegionBoundaryKind.FaultEnd:
                    case ExceptionRegionBoundaryKind.CatchEnd:
                    case ExceptionRegionBoundaryKind.FinallyEnd:
                        // Before calling EndExceptionBlock(), we must ensure that the current clause is
                        // the last one associated with its try block.
                        ExceptionHandlingClause clause = boundary.Clause;
                        int tryOffset = clause.TryOffset;
                        int tryLength = clause.TryLength;
                        int handlerEnd = clause.HandlerOffset + clause.HandlerLength;
                        for (int j = boundaries.Count - 1; j > i; j--)
                        {
                            ExceptionHandlingClause nextClause = boundaries[j].Clause;
                            if ((nextClause.TryOffset, nextClause.TryLength) != (tryOffset, tryLength))
                                continue;

                            if (nextClause.HandlerOffset + nextClause.HandlerLength > handlerEnd)
                                goto default;
                        }
                        il.EndExceptionBlock();
                        break;

                    default:
                        break;
                }
            }
            return i;
        }
    }

    private static bool UnlockMemoryPage(nint address, int length)
    {
        if (Environment.OSVersion.Platform is PlatformID.Unix)
            return UnlockMemoryPageUnix(address, length);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return UnlockMemoryPageWindows(address, length);

        return false;
    }

    private static bool UnlockMemoryPageUnix(nint address, int length)
    {
        [DllImport("libc", SetLastError = true)]
        static extern int mprotect(nint lpAddress, uint dwSize, uint flags);

        const uint rwx = 7;
        nint regionStart = address & -Environment.SystemPageSize;
        nint regionSize = address - regionStart + length;
        return mprotect(regionStart, (uint)regionSize, rwx) == 0;
    }

    private static bool UnlockMemoryPageWindows(nint address, int length)
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtect(nint lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

        const uint PAGE_EXECUTE_READWRITE = 0x40;
        return VirtualProtect(address, (uint)length, PAGE_EXECUTE_READWRITE, out _);
    }

    private sealed class Hook : IDisposable
    {
        private readonly nint _address;

        private readonly byte[] _body;

        private readonly byte[] _stub;

        public Hook(nint address, byte[] stub)
        {
            _address = address;
            _body = new byte[stub.Length];
            _stub = stub;

            Marshal.Copy(address, _body, 0, _body.Length);
            Apply();
        }

        ~Hook()
        {
            Undo();
        }

        public void Apply()
        {
            UnlockMemoryPage(_address, _stub.Length);
            Marshal.Copy(_stub, 0, _address, _stub.Length);
        }

        public void Undo()
        {
            UnlockMemoryPage(_address, _body.Length);
            Marshal.Copy(_body, 0, _address, _body.Length);
        }

        public void Dispose()
        {
            Undo();
            GC.SuppressFinalize(this);
        }
    }
}
