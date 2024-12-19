using System.Xml.Linq;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TypeSystem = Fody.TypeSystem;

namespace HotAvalonia.Fody;

/// <summary>
/// Represents a base class for feature-specific weaving logic.
/// </summary>
internal abstract class FeatureWeaver
{
    /// <summary>
    /// The root module weaver providing context and shared functionality.
    /// </summary>
    protected readonly BaseModuleWeaver _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureWeaver"/> class.
    /// </summary>
    /// <param name="root">The root module weaver providing context and shared functionality.</param>
    protected FeatureWeaver(BaseModuleWeaver root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <inheritdoc cref="BaseModuleWeaver.Config"/>
    public XElement Config => _root.Config;

    /// <inheritdoc cref="BaseModuleWeaver.ModuleDefinition"/>
    public ModuleDefinition ModuleDefinition => _root.ModuleDefinition;

    /// <inheritdoc cref="BaseModuleWeaver.AssemblyResolver"/>
    public IAssemblyResolver AssemblyResolver => _root.AssemblyResolver;

    /// <inheritdoc cref="BaseModuleWeaver.TypeSystem"/>
    public TypeSystem TypeSystem => _root.TypeSystem;

    /// <inheritdoc cref="BaseModuleWeaver.AssemblyFilePath"/>
    public string AssemblyFilePath => _root.AssemblyFilePath;

    /// <inheritdoc cref="BaseModuleWeaver.ProjectDirectoryPath"/>
    public string ProjectDirectoryPath => _root.ProjectDirectoryPath;

    /// <inheritdoc cref="BaseModuleWeaver.ProjectFilePath"/>
    public string ProjectFilePath => _root.ProjectFilePath;

    /// <inheritdoc cref="BaseModuleWeaver.DocumentationFilePath"/>
    public string? DocumentationFilePath => _root.DocumentationFilePath;

    /// <inheritdoc cref="BaseModuleWeaver.AddinDirectoryPath"/>
    public string AddinDirectoryPath => _root.AddinDirectoryPath;

    /// <inheritdoc cref="BaseModuleWeaver.SolutionDirectoryPath"/>
    public string SolutionDirectoryPath => _root.SolutionDirectoryPath;

    /// <inheritdoc cref="BaseModuleWeaver.References"/>
    public string References => _root.References;

    /// <inheritdoc cref="BaseModuleWeaver.ReferenceCopyLocalPaths"/>
    public List<string> ReferenceCopyLocalPaths => _root.ReferenceCopyLocalPaths;

    /// <inheritdoc cref="BaseModuleWeaver.RuntimeCopyLocalPaths"/>
    public List<string> RuntimeCopyLocalPaths => _root.RuntimeCopyLocalPaths;

    /// <inheritdoc cref="BaseModuleWeaver.DefineConstants"/>
    public List<string> DefineConstants => _root.DefineConstants;


    /// <inheritdoc cref="BaseModuleWeaver.GetAssembliesForScanning"/>
    public virtual IEnumerable<string> GetAssembliesForScanning() => [];

    /// <inheritdoc cref="BaseModuleWeaver.Execute"/>
    public abstract void Execute();

    /// <inheritdoc cref="BaseModuleWeaver.AfterWeaving"/>
    public virtual void AfterWeaving()
    {
    }

    /// <inheritdoc cref="BaseModuleWeaver.Cancel"/>
    public virtual void Cancel()
    {
    }


    /// <inheritdoc cref="BaseModuleWeaver.FindTypeDefinition"/>
    public TypeDefinition FindTypeDefinition(string name) => _root.FindTypeDefinition(name);

    /// <inheritdoc cref="BaseModuleWeaver.TryFindTypeDefinition"/>
    public bool TryFindTypeDefinition(string name, out TypeDefinition? type) => _root.TryFindTypeDefinition(name, out type);

    /// <inheritdoc cref="BaseModuleWeaver.ResolveAssembly"/>
    public AssemblyDefinition? ResolveAssembly(string name) => _root.ResolveAssembly(name);

    /// <inheritdoc cref="BaseModuleWeaver.WriteDebug(string)"/>
    public void WriteDebug(string message) => _root.WriteDebug(message);

    /// <inheritdoc cref="BaseModuleWeaver.WriteInfo(string)"/>
    public void WriteInfo(string message) => _root.WriteInfo(message);

    /// <inheritdoc cref="BaseModuleWeaver.WriteWarning(string)"/>
    public void WriteWarning(string message) => _root.WriteWarning(message);

    /// <inheritdoc cref="BaseModuleWeaver.WriteWarning(string, SequencePoint?)"/>
    public void WriteWarning(string message, SequencePoint? sequencePoint) => _root.WriteWarning(message, sequencePoint);

    /// <inheritdoc cref="BaseModuleWeaver.WriteError(string)"/>
    public void WriteError(string message) => _root.WriteError(message);

    /// <inheritdoc cref="BaseModuleWeaver.WriteError(string, SequencePoint?)"/>
    public void WriteError(string message, SequencePoint? sequencePoint) => _root.WriteError(message, sequencePoint);

    /// <inheritdoc cref="BaseModuleWeaver.WriteMessage(string, MessageImportance)"/>
    public void WriteMessage(string message, MessageImportance importance) => _root.WriteMessage(message, importance);
}
