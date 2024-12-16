using System.Reflection;
using System.Reflection.Emit;
using HotAvalonia.Helpers;

namespace HotAvalonia.Reflection;

/// <summary>
/// Encapsulates a dynamic assembly, providing mechanisms to manage
/// runtime access to types and metadata within the assembly.
/// </summary>
public class DynamicAssembly
{
    /// <summary>
    /// The underlying assembly.
    /// </summary>
    private readonly Assembly _assembly;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicAssembly"/> class.
    /// </summary>
    /// <param name="assembly">The assembly to wrap.</param>
    public DynamicAssembly(Assembly assembly)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    }

    /// <summary>
    /// Gets the name of the assembly.
    /// </summary>
    public string Name => _assembly.GetName()?.Name ?? string.Empty;

    /// <summary>
    /// Gets the underlying assembly.
    /// </summary>
    public Assembly Assembly => _assembly;

    /// <summary>
    /// Grants the dynamic assembly access to another assembly.
    /// </summary>
    /// <param name="assembly">The assembly to allow access to.</param>
    public virtual void AllowAccessTo(Assembly assembly)
    {
        _ = assembly ?? throw new ArgumentNullException(nameof(assembly));

        if (_assembly is AssemblyBuilder assemblyBuilder)
            assemblyBuilder.AllowAccessTo(assembly);
    }
}
