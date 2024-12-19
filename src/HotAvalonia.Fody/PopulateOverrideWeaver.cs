using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using DelegateReference = (Mono.Cecil.TypeReference Delegate, Mono.Cecil.MethodReference Invoke);
using AvaloniaResource = (string Uri, Mono.Cecil.MethodDefinition Build, Mono.Cecil.MethodDefinition Populate);

namespace HotAvalonia.Fody;

/// <summary>
/// Weaves the "populate override" logic into compiled Avalonia resources.
/// </summary>
internal sealed class PopulateOverrideWeaver : FeatureWeaver
{
    /// <summary>
    /// A reference to the delegate type used for the "populate override" mechanism.
    /// </summary>
    private readonly Lazy<DelegateReference> _populateOverrideReference;

    /// <summary>
    /// Initializes a new instance of the <see cref="PopulateOverrideWeaver"/> class.
    /// </summary>
    /// <param name="root">The root module weaver providing context and shared functionality.</param>
    public PopulateOverrideWeaver(BaseModuleWeaver root) : base(root)
    {
        _populateOverrideReference = new(ImportPopulateOverrideDelegate);
    }

    /// <inheritdoc/>
    public override IEnumerable<string> GetAssembliesForScanning() =>
    [
        "System.Runtime",
        "System.ComponentModel",
    ];

    /// <inheritdoc/>
    public override void Execute()
    {
        const string resourceContainerName = "CompiledAvaloniaXaml.!AvaloniaResources";
        TypeDefinition? resourceContainer = ModuleDefinition.GetType(resourceContainerName);
        if (resourceContainer is null)
        {
            WriteInfo($"'{resourceContainerName}' not found in '{AssemblyFilePath}'.");
            return;
        }

        AvaloniaResource[] resources = ExtractAvaloniaResources(resourceContainer);
        foreach (AvaloniaResource resource in resources)
        {
            WriteInfo($"Processing '{resource.Uri}'...");
            ProcessAvaloniaResource(resourceContainer, resource);
        }
    }

    /// <summary>
    /// Extracts Avalonia resources from the specified resource container type.
    /// </summary>
    /// <param name="declaringType">The type containing the resource definitions.</param>
    /// <returns>An array of <see cref="AvaloniaResource"/> objects representing the extracted resources.</returns>
    private static AvaloniaResource[] ExtractAvaloniaResources(TypeDefinition declaringType)
        => declaringType
            .GetMethods()
            .Where(x => x.IsStatic && x.HasBody && x.HasParameters && x.Name.Contains(':'))
            .GroupBy(x => x.Name.Substring(x.Name.IndexOf(':') + 1))
            .Select(x => (
                Uri: x.Key,
                Build: x.FirstOrDefault(y => y.Name.StartsWith("Build:") && y.Parameters.Count == 1),
                Populate: x.FirstOrDefault(y => y.Name.StartsWith("Populate:") && y.Parameters.Count == 2)
            ))
            .Where(x => x.Build is not null && x.Populate is not null)!
            .ToArray();

    /// <summary>
    /// Processes an individual Avalonia resource by injecting "populate override" functionality.
    /// </summary>
    /// <param name="declaringType">The type containing the resource definitions.</param>
    /// <param name="resource">The resource to process.</param>
    private void ProcessAvaloniaResource(TypeDefinition declaringType, AvaloniaResource resource)
    {
        string populateOverrideName = $"PopulateOverride:{resource.Uri}";
        FieldDefinition populateOverride = CreatePopulateOverride(populateOverrideName);
        declaringType.Fields.Add(populateOverride);

        string populateTrampolineName = $"PopulateTrampoline:{resource.Uri}";
        MethodDefinition populateTrampoline = CreatePopulateTrampoline(populateTrampolineName, resource.Populate, populateOverride);
        declaringType.Methods.Add(populateTrampoline);

        ReplaceMethodReferences(resource.Build, resource.Populate, populateTrampoline);
    }

    /// <summary>
    /// Creates a field to hold a "populate override" delegate.
    /// </summary>
    /// <param name="name">The name of the field.</param>
    /// <returns>A new <see cref="FieldDefinition"/> representing the populate override field.</returns>
    private FieldDefinition CreatePopulateOverride(string name)
        => new(name, FieldAttributes.Public | FieldAttributes.Static, _populateOverrideReference.Value.Delegate);

    /// <summary>
    /// Creates a trampoline method that invokes the populate method, optionally using the override delegate.
    /// </summary>
    /// <param name="name">The name of the trampoline method.</param>
    /// <param name="populate">The original populate method.</param>
    /// <param name="populateOverride">The populate override field.</param>
    /// <returns>A new <see cref="MethodDefinition"/> representing the trampoline method.</returns>
    private MethodDefinition CreatePopulateTrampoline(string name, MethodDefinition populate, FieldDefinition populateOverride)
    {
        MethodDefinition populateTrampoline = new(name, populate.Attributes, populate.ReturnType);
        foreach (ParameterDefinition parameter in populate.Parameters)
            populateTrampoline.Parameters.Add(parameter);

        ILProcessor il = populateTrampoline.Body.GetILProcessor();
        Instruction noOverride = il.Create(OpCodes.Nop);

        il.Emit(OpCodes.Ldsfld, populateOverride);
        il.Emit(OpCodes.Brfalse_S, noOverride);

        il.Emit(OpCodes.Ldsfld, populateOverride);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _populateOverrideReference.Value.Invoke);
        il.Emit(OpCodes.Ret);

        il.Append(noOverride);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, populate);
        il.Emit(OpCodes.Ret);

        return populateTrampoline;
    }

    /// <summary>
    /// Replaces references to the target method with references
    /// to the replacement method in the specified method body.
    /// </summary>
    /// <param name="method">The method in which to replace references.</param>
    /// <param name="target">The original target method reference.</param>
    /// <param name="replacement">The replacement method reference.</param>
    private static void ReplaceMethodReferences(MethodDefinition method, MethodReference target, MethodReference replacement)
    {
        foreach (Instruction instruction in method.Body.Instructions)
        {
            // Obviously, determining method equality solely by name is not correct. However, it suits our
            // needs in this simple case, and I couldn't be bothered to implement it properly right now.
            // So what? Bite me.
            if (instruction.Operand is MethodReference callee && callee.Name == target.Name)
                instruction.Operand = replacement;
        }
    }

    /// <summary>
    /// Imports the delegate type and its invoke method used for the populate override mechanism.
    /// </summary>
    /// <returns>
    /// A <see cref="DelegateReference"/> containing the imported delegate type and its invoke method.
    /// </returns>
    private DelegateReference ImportPopulateOverrideDelegate()
    {
        TypeDefinition actionTypeDef = FindTypeDefinition("System.Action`2");
        MethodDefinition actionInvokeDef = actionTypeDef.GetMethods().FirstOrDefault(x => x.Name == nameof(Action.Invoke));
        TypeReference actionTypeRef = ModuleDefinition.ImportReference(actionTypeDef);
        MethodReference actionInvokeRef = ModuleDefinition.ImportReference(actionInvokeDef);

        TypeDefinition serviceProviderDef = FindTypeDefinition("System.IServiceProvider");
        TypeReference serviceProviderRef = ModuleDefinition.ImportReference(serviceProviderDef);
        GenericInstanceType populateOverrideDelegateDef = actionTypeRef.MakeGenericInstanceType(serviceProviderRef, TypeSystem.ObjectReference);
        TypeReference populateOverrideDelegateRef = ModuleDefinition.ImportReference(populateOverrideDelegateDef);

        MethodReference populateOverrideInvokeRef = new(actionInvokeRef.Name, actionInvokeRef.ReturnType)
        {
            DeclaringType = populateOverrideDelegateRef,
            HasThis = actionInvokeRef.HasThis,
            ExplicitThis = actionInvokeRef.ExplicitThis,
            CallingConvention = actionInvokeRef.CallingConvention,
        };
        foreach (ParameterDefinition parameter in actionInvokeRef.Parameters)
            populateOverrideInvokeRef.Parameters.Add(parameter);

        populateOverrideInvokeRef = ModuleDefinition.ImportReference(populateOverrideInvokeRef, populateOverrideDelegateRef);
        return (populateOverrideDelegateDef, populateOverrideInvokeRef);
    }
}
