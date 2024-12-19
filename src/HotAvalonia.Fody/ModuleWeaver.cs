using Fody;

namespace HotAvalonia.Fody;

/// <summary>
/// Represents the main module weaver that orchestrates feature-specific weaving tasks.
/// </summary>
public sealed class ModuleWeaver : BaseModuleWeaver
{
    /// <summary>
    /// The collection of feature weavers used to perform specific weaving tasks.
    /// </summary>
    private readonly FeatureWeaver[] _features;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleWeaver"/> class.
    /// </summary>
    public ModuleWeaver() => _features =
    [
        new PopulateOverrideWeaver(this),
    ];

    /// <inheritdoc/>
    public override IEnumerable<string> GetAssembliesForScanning()
        => _features
            .SelectMany(x => x.GetAssembliesForScanning())
            .Concat(["mscorlib", "netstandard"]);

    /// <inheritdoc/>
    public override void Execute()
    {
        WriteInfo($"Starting weaving '{AssemblyFilePath}'...");

        foreach (FeatureWeaver feature in _features)
        {
            WriteInfo($"Running '{feature.GetType().Name}' against '{AssemblyFilePath}'...");
            feature.Execute();
        }
    }

    /// <inheritdoc/>
    public override void AfterWeaving()
    {
        foreach (FeatureWeaver feature in _features)
            feature.AfterWeaving();

        WriteInfo($"Finished weaving '{AssemblyFilePath}'!");
    }

    /// <inheritdoc/>
    public override void Cancel()
    {
        foreach (FeatureWeaver feature in _features)
            feature.Cancel();
    }
}
