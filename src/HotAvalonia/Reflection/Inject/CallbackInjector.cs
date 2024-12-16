using System.Reflection;
using System.Reflection.Emit;
using HotAvalonia.Helpers;

namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Provides methods to inject callbacks into methods at runtime.
/// </summary>
internal static class CallbackInjector
{
    /// <summary>
    /// Indicates whether callback injection is supported in the current runtime environment.
    /// </summary>
    public static bool IsSupported => MethodInjector.IsSupported;

    /// <summary>
    /// Indicates whether callback injection is supported in optimized assemblies.
    /// </summary>
    public static bool SupportsOptimizedMethods => MethodInjector.SupportsOptimizedMethods;

    /// <summary>
    /// Throws an exception if callback injection is not supported in the current runtime environment.
    /// </summary>
    /// <exception cref="InvalidOperationException"/>
    public static void ThrowIfNotSupported() => MethodInjector.ThrowIfNotSupported();

    /// <inheritdoc cref="Inject(MethodBase, MethodBase)"/>
    public static IInjection Inject(MethodBase target, Delegate callback)
    {
        _ = callback ?? throw new ArgumentNullException(nameof(callback));

        return Inject(target, callback.Method, callback.Target);
    }

    /// <inheritdoc cref="Inject(MethodBase, MethodBase, object?)"/>
    public static IInjection Inject(MethodBase target, MethodBase callback)
        => Inject(target, callback, thisArg: null);

    /// <summary>
    /// Injects a callback method into the specified target method.
    /// </summary>
    /// <param name="target">The target method where the callback will be injected.</param>
    /// <param name="callback">The callback method to inject.</param>
    /// <param name="thisArg">
    /// An instance of the object to be used when invoking instance method callbacks.
    /// Not applicable for static methods.
    /// </param>
    /// <returns>An <see cref="IInjection"/> instance that represents the method injection.</returns>
    public static IInjection Inject(MethodBase target, MethodBase callback, object? thisArg)
    {
        _ = target ?? throw new ArgumentNullException(nameof(target));
        _ = callback ?? throw new ArgumentNullException(nameof(callback));
        _ = thisArg ?? (callback.IsStatic ? thisArg : throw new ArgumentNullException(nameof(thisArg)));

        ThrowIfNotSupported();

        using IDisposable context = AssemblyHelper.GetDynamicAssembly(out AssemblyBuilder assemblyBuilder, out ModuleBuilder moduleBuilder);

        // Bypass access modifiers
        assemblyBuilder.AllowAccessTo(target);
        assemblyBuilder.AllowAccessTo(callback);

        // Define
        string name = $"Injection_{target.Name}_{Guid.NewGuid():N}";
        TypeBuilder injectionBuilder = moduleBuilder.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        injectionBuilder.AddInterfaceImplementation(typeof(IInjection));

        // Emit
        FieldBuilder? thisArgBuilder = !callback.IsStatic && thisArg is not null ? injectionBuilder.DefineField("s_thisArg", thisArg.GetType(), FieldAttributes.Private | FieldAttributes.Static) : null;
        FieldBuilder? callerMemberBuilder = NeedsCallerMember(callback) ? injectionBuilder.DefineField("s_callerMember", target.GetType(), FieldAttributes.Private | FieldAttributes.Static) : null;
        FieldBuilder methodInjectionBuilder = injectionBuilder.DefineField("_methodInjection", typeof(IInjection), FieldAttributes.Private);
        MethodBuilder invokeBuilder = EmitInvokeMethod(injectionBuilder, target, callback, thisArgBuilder, callerMemberBuilder);
        EmitDisposeMethod(injectionBuilder, [methodInjectionBuilder], [thisArgBuilder, callerMemberBuilder, methodInjectionBuilder]);

        // Build
        Type injectionType = injectionBuilder.CreateTypeInfo();
        IInjection injection = (IInjection)Activator.CreateInstance(injectionType);
        FieldInfo? thisArgField = thisArgBuilder is null ? null : injectionType.GetStaticField(thisArgBuilder.Name);
        FieldInfo? callerMemberField = callerMemberBuilder is null ? null : injectionType.GetStaticField(callerMemberBuilder.Name);
        FieldInfo methodInjectionField = injectionType.GetInstanceField(methodInjectionBuilder.Name) ?? throw new MissingFieldException(injectionType.FullName, methodInjectionBuilder.Name);
        MethodInfo invokeMethod = injectionType.GetMethod(invokeBuilder.Name) ?? throw new MissingMethodException(injectionType.FullName, invokeBuilder.Name);

        // Set up
        thisArgField?.SetValue(null, thisArg);
        callerMemberField?.SetValue(null, target);
        methodInjectionField.SetValue(injection, MethodInjector.Inject(target, invokeMethod));

        return injection;
    }

    /// <summary>
    /// Emits an intermediary method that can execute a callback and, conditionally, delegate the execution to the target method.
    /// </summary>
    /// <param name="typeBuilder">The type builder used for constructing the method.</param>
    /// <param name="target">The original target method that may be called by the emitted method.</param>
    /// <param name="callback">The callback method to be invoked by the emitted method.</param>
    /// <param name="thisArg">The optional 'this' argument if the callback is an instance method.</param>
    /// <param name="callerMember">The caller member metadata if it is required by the callback method.</param>
    /// <returns>A method builder for the emitted method.</returns>
    private static MethodBuilder EmitInvokeMethod(TypeBuilder typeBuilder, MethodBase target, MethodBase callback, FieldBuilder? thisArg, FieldBuilder? callerMember)
    {
        MethodAttributes attributes = target.IsStatic || MethodInjector.InjectionType is InjectionType.Native ? MethodAttributes.Public | MethodAttributes.Static : MethodAttributes.Public;
        CallingConventions callingConvention = target.CallingConvention & ~(attributes.HasFlag(MethodAttributes.Static) ? CallingConventions.HasThis : 0);
        Type targetReturnType = target.GetReturnType();
        ParameterInfo[] targetParameters = target.GetParameters();
        Type[] targetParameterTypes = Array.ConvertAll(targetParameters, static x => x.ParameterType);

        Type callbackReturnType = callback.GetReturnType();
        ParameterInfo[] callbackParameters = callback.GetParameters();

        List<Type> invokeParameterTypes = new(targetParameterTypes.Length + 2);
        if (MethodInjector.InjectionType is InjectionType.Native)
        {
            invokeParameterTypes.Add(target.GetStaticDelegateType());
            if (target.GetThisType() is Type thisType)
                invokeParameterTypes.Add(thisType);
        }
        invokeParameterTypes.AddRange(targetParameterTypes);

        MethodBuilder invokeBuilder = typeBuilder.DefineMethod(nameof(Action.Invoke), attributes, callingConvention, targetReturnType, invokeParameterTypes.ToArray());
        ILGenerator il = invokeBuilder.GetILGenerator();
        Label executeCallback = il.DefineLabel();
        if (targetReturnType != typeof(void))
            il.DeclareLocal(targetReturnType);

        // --------------- Execute the callback ---------------
        // ?ldsfld s_thisArg            // If not static
        // ldarg.0
        // ldarg.1
        // ...
        // ldarg N
        // call callback
        // ?pop                         // return != typeof(bool) && return != typeof(void)
        // ?brfalse executeCallback     // return == typeof(bool)
        // ?ldloc.0                     // return == typeof(bool) && targetReturn != typeof(void)
        // ?ret                         // return == typeof(bool)
        if (!callback.IsStatic)
            il.Emit(OpCodes.Ldsfld, thisArg ?? throw new ArgumentNullException(nameof(thisArg)));

        ReadOnlySpan<ParameterInfo> availableArgs = targetParameters;
        foreach (ParameterInfo callbackParameter in callbackParameters)
            availableArgs = EmitCallbackParameter(il, callbackParameter, availableArgs, target, callerMember);

        il.EmitCall(callback);

        if (callbackReturnType == typeof(bool))
        {
            il.Emit(OpCodes.Brfalse, executeCallback);

            if (targetReturnType != typeof(void))
                il.Emit(OpCodes.Ldloc_0);

            il.Emit(OpCodes.Ret);
        }
        else if (callbackReturnType != typeof(void))
        {
            il.Emit(OpCodes.Pop);
        }
        // ----------------------------------------------------

        // ------- Redirect the call back to the target -------
        // ExecuteCallback:
        // ldarg.0
        // ldarg.1
        // ...
        // ldarg N
        // call target
        // ret
        il.MarkLabel(executeCallback);

        int shift = (target.IsStatic ? 0 : 1) + (MethodInjector.InjectionType is InjectionType.Native ? 1 : 0);
        int parameterCount = targetParameterTypes.Length + shift;
        for (int i = 0; i < parameterCount; i++)
            il.EmitLdarg(i);

        if (MethodInjector.InjectionType is InjectionType.Native)
        {
            il.EmitCall(invokeParameterTypes.First().GetMethod(nameof(Action.Invoke)));
        }
        else
        {
            il.EmitLdc_IN(target.GetFunctionPointer());
            il.EmitCalli(OpCodes.Calli, target.CallingConvention, targetReturnType, targetParameterTypes, null);
        }
        il.Emit(OpCodes.Ret);
        // ----------------------------------------------------

        return invokeBuilder;
    }

    /// <summary>
    /// Emits a <c>Dispose</c> method for the specified type that disposes field values and resets them.
    /// </summary>
    /// <param name="typeBuilder">The <see cref="TypeBuilder"/> used to define the type.</param>
    /// <param name="disposeFields">The fields whose values need to be disposed.</param>
    /// <param name="unsetFields">The fields to be unset.</param>
    /// <returns>A <see cref="MethodBuilder"/> representing the generated <c>Dispose</c> method.</returns>
    private static MethodBuilder EmitDisposeMethod(TypeBuilder typeBuilder, FieldBuilder?[] disposeFields, FieldBuilder?[] unsetFields)
    {
        MethodInfo disposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));
        MethodAttributes attributes = MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual;
        MethodBuilder disposeBuilder = typeBuilder.DefineMethod(nameof(IDisposable.Dispose), attributes, typeof(void), Type.EmptyTypes);
        ILGenerator il = disposeBuilder.GetILGenerator();

        foreach (FieldBuilder? disposeField in disposeFields)
        {
            if (disposeField is null)
                continue;

            if (!disposeField.IsStatic)
                il.Emit(OpCodes.Ldarg_0);

            Label isNotDisposable = il.DefineLabel();
            il.Emit(disposeField.IsStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, disposeField);
            il.Emit(OpCodes.Isinst, typeof(IDisposable));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, isNotDisposable);

            il.Emit(OpCodes.Dup);
            il.EmitCall(OpCodes.Callvirt, disposeMethod, null);

            il.MarkLabel(isNotDisposable);
            il.Emit(OpCodes.Pop);
        }

        foreach (FieldBuilder? unsetField in unsetFields)
        {
            if (unsetField is null)
                continue;

            if (!unsetField.IsStatic)
                il.Emit(OpCodes.Ldarg_0);

            il.EmitLddefault(unsetField.FieldType);
            il.Emit(unsetField.IsStatic ? OpCodes.Stsfld : OpCodes.Stfld, unsetField);
        }

        il.Emit(OpCodes.Ret);
        return disposeBuilder;
    }

    /// <summary>
    /// Emits the necessary IL instructions to load an argument for the callback method.
    /// </summary>
    /// <param name="il">The IL generator used for emitting instructions.</param>
    /// <param name="parameter">The parameter in the callback method being processed.</param>
    /// <param name="remainingArgs">The arguments of the calling method that has not been used yet.</param>
    /// <param name="target">The original target method that may be called by the callback.</param>
    /// <param name="callerMember">The caller member metadata if it is required by the callback method.</param>
    /// <returns>A read-only span of remaining arguments after this emission.</returns>
    private static ReadOnlySpan<ParameterInfo> EmitCallbackParameter(ILGenerator il, ParameterInfo parameter, ReadOnlySpan<ParameterInfo> remainingArgs, MethodBase target, FieldBuilder? callerMember)
    {
        Type parameterType = parameter.ParameterType;
        CallbackParameterType callbackParameterType = parameter.GetCallbackParameterType();
        switch (callbackParameterType)
        {
            case CallbackParameterType.CallbackResult when parameter.IsOut && target is MethodInfo targetInfo && targetInfo.ReturnType.IsAssignableFrom(parameterType.GetElementType()):
                il.Emit(OpCodes.Ldloca_S, 0);
                return remainingArgs;

            case CallbackParameterType.Caller when !target.IsStatic && parameterType.IsAssignableFrom(target.DeclaringType):
                il.EmitLdarg(MethodInjector.InjectionType is InjectionType.Native ? 1 : 0);
                return remainingArgs;

            case CallbackParameterType.CallerMember when callerMember is not null && parameterType.IsAssignableFrom(callerMember.FieldType):
                il.Emit(OpCodes.Ldsfld, callerMember);
                return remainingArgs;

            case CallbackParameterType.CallerMemberName when parameterType == typeof(string):
                il.Emit(OpCodes.Ldstr, target.Name);
                return remainingArgs;

            case not CallbackParameterType.None:
                il.EmitLddefault(parameterType);
                return remainingArgs;
        }

        while (true)
        {
            if (remainingArgs.IsEmpty)
                throw new ArgumentException("No suitable argument found for the callback parameter.", parameter.Name);

            ParameterInfo arg = remainingArgs[0];
            remainingArgs = remainingArgs.Slice(1);

            if (!parameterType.IsAssignableFrom(arg.ParameterType))
                continue;

            int shift = (target.IsStatic ? 0 : 1) + (MethodInjector.InjectionType is InjectionType.Native ? 1 : 0);
            il.EmitLdarg(arg.Position + shift);
            return remainingArgs;
        }
    }

    /// <summary>
    /// Determines if a method requires a [CallerMember] parameter.
    /// </summary>
    /// <param name="method">The method to check.</param>
    /// <returns><c>true</c> if the method requires a caller member parameter; otherwise, <c>false</c>.</returns>
    private static bool NeedsCallerMember(MethodBase method)
        => method.GetParameters().Any(static x => x.GetCallbackParameterType() is CallbackParameterType.CallerMember);
}
