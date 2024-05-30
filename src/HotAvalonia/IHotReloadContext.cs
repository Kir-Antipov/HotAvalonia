using System.Collections;
using System.Reflection;

namespace HotAvalonia;

/// <summary>
/// Represents a context for managing hot reload functionality within an application.
/// </summary>
public interface IHotReloadContext : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether hot reload is currently enabled.
    /// </summary>
    bool IsHotReloadEnabled { get; }

    /// <summary>
    /// Enables the hot reload functionality.
    /// </summary>
    void EnableHotReload();

    /// <summary>
    /// Disables the hot reload functionality.
    /// </summary>
    void DisableHotReload();
}

/// <summary>
/// Provides factory methods and utilities for working with <see cref="IHotReloadContext"/> instances.
/// </summary>
public static class HotReloadContext
{
    /// <summary>
    /// Creates a new <see cref="IHotReloadContext"/> for the specified <see cref="AppDomain"/>
    /// using the provided context factory.
    /// </summary>
    /// <param name="appDomain">The <see cref="AppDomain"/> to create the context for.</param>
    /// <param name="contextFactory">
    /// The factory function to create <see cref="IHotReloadContext"/> instances for
    /// the assemblies that have been loaded into the execution context of
    /// the specified application domain.
    /// </param>
    /// <returns>A new <see cref="IHotReloadContext"/> for the specified <see cref="AppDomain"/>.</returns>
    public static IHotReloadContext FromAppDomain(
        AppDomain appDomain,
        Func<AppDomain, Assembly, IHotReloadContext?> contextFactory)
    {
        _ = appDomain ?? throw new ArgumentNullException(nameof(appDomain));
        _ = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));

        return new AppDomainHotReloadContext(appDomain, contextFactory);
    }

    /// <inheritdoc cref="Combine(IHotReloadContext, IEnumerable&lt;IHotReloadContext&gt;)"/>
    public static IHotReloadContext Combine(this IHotReloadContext context, params IHotReloadContext[] contexts)
        => Combine(context, (IEnumerable<IHotReloadContext>)contexts);

    /// <summary>
    /// Combines the specified <see cref="IHotReloadContext"/> with a collection
    /// of additional contexts into a single <see cref="IHotReloadContext"/>.
    /// </summary>
    /// <param name="context">The base <see cref="IHotReloadContext"/>.</param>
    /// <param name="contexts">A collection of additional contexts to combine with the base one.</param>
    /// <returns>A combined <see cref="IHotReloadContext"/>.</returns>
    public static IHotReloadContext Combine(this IHotReloadContext context, IEnumerable<IHotReloadContext> contexts)
    {
        _ = context ?? throw new ArgumentNullException(nameof(context));
        _ = contexts ?? throw new ArgumentNullException(nameof(contexts));

        return Combine(contexts.Concat([context]));
    }

    /// <summary>
    /// Combines a collection of <see cref="IHotReloadContext"/> instances into
    /// a single <see cref="IHotReloadContext"/>.
    /// </summary>
    /// <param name="contexts">A collection of contexts to combine.</param>
    /// <returns>A combined <see cref="IHotReloadContext"/>.</returns>
    public static IHotReloadContext Combine(this IEnumerable<IHotReloadContext> contexts)
    {
        _ = contexts ?? throw new ArgumentNullException(nameof(contexts));

        IHotReloadContext[] contextArray = contexts
            .SelectMany(static x => x is CombinedHotReloadContext c ? c.AsEnumerable() : [x])
            .Where(static x => x is not null)
            .ToArray();

        return new CombinedHotReloadContext(contextArray);
    }
}

/// <summary>
/// A combined hot reload context that manages multiple <see cref="IHotReloadContext"/> instances.
/// </summary>
file sealed class CombinedHotReloadContext : IHotReloadContext, IEnumerable<IHotReloadContext>
{
    /// <summary>
    /// The <see cref="IHotReloadContext"/> instances to be managed.
    /// </summary>
    private readonly IHotReloadContext[] _contexts;

    /// <summary>
    /// Initializes a new instance of the <see cref="CombinedHotReloadContext"/> class.
    /// </summary>
    /// <param name="contexts">The <see cref="IHotReloadContext"/> instances to be managed.</param>
    public CombinedHotReloadContext(IHotReloadContext[] contexts)
    {
        _contexts = contexts;
    }

    /// <inheritdoc/>
    public bool IsHotReloadEnabled
        => _contexts.Length != 0 && _contexts.All(static x => x.IsHotReloadEnabled);

    /// <inheritdoc/>
    public void EnableHotReload()
    {
        foreach (IHotReloadContext context in _contexts)
            context.EnableHotReload();
    }

    /// <inheritdoc/>
    public void DisableHotReload()
    {
        foreach (IHotReloadContext context in _contexts)
            context.DisableHotReload();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IHotReloadContext context in _contexts)
            context.Dispose();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public IEnumerator<IHotReloadContext> GetEnumerator()
        => _contexts.AsEnumerable().GetEnumerator();
}

/// <summary>
/// A hot reload context that operates within an <see cref="AppDomain"/> and manages
/// automatically created hot reload contexts for dynamically loaded assemblies.
/// </summary>
file sealed class AppDomainHotReloadContext : IHotReloadContext
{
    /// <summary>
    /// The <see cref="AppDomain"/> associated with this hot reload context.
    /// </summary>
    private readonly AppDomain _appDomain;

    /// <summary>
    /// The factory function for creating <see cref="IHotReloadContext"/> instances
    /// for dynamically loaded assemblies.
    /// </summary>
    private readonly Func<AppDomain, Assembly, IHotReloadContext?> _contextFactory;

    /// <summary>
    /// The hot reload context responsible for managing dynamically loaded assemblies.
    /// </summary>
    private IHotReloadContext _context;

    /// <summary>
    /// An object used to synchronize access to the <see cref="_context"/>.
    /// </summary>
    private readonly object _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDomainHotReloadContext"/> class.
    /// </summary>
    /// <param name="appDomain">The <see cref="AppDomain"/> to manage.</param>
    /// <param name="contextFactory">
    /// The factory function to create <see cref="IHotReloadContext"/> instances for
    /// dynamically loaded assemblies.
    /// </param>
    public AppDomainHotReloadContext(AppDomain appDomain, Func<AppDomain, Assembly, IHotReloadContext?> contextFactory)
    {
        _appDomain = appDomain;
        _contextFactory = contextFactory;
        _lock = new();

        _context = appDomain.GetAssemblies()
            .Select(x => _contextFactory(_appDomain, x))
            .Where(x => x is not null)!
            .Combine();

        appDomain.AssemblyLoad += OnAssemblyLoad;
    }

    /// <inheritdoc/>
    public bool IsHotReloadEnabled
    {
        get
        {
            lock (_lock)
                return _context.IsHotReloadEnabled;
        }
    }

    /// <inheritdoc/>
    public void EnableHotReload()
    {
        lock (_lock)
            _context.EnableHotReload();
    }

    /// <inheritdoc/>
    public void DisableHotReload()
    {
        lock (_lock)
            _context.DisableHotReload();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _appDomain.AssemblyLoad -= OnAssemblyLoad;
        _context.Dispose();
    }

    /// <summary>
    /// Handles the <see cref="AppDomain.AssemblyLoad"/> event, creating and combining hot reload contexts
    /// for newly loaded assemblies.
    /// </summary>
    /// <param name="sender">The source of the event, typically an <see cref="AppDomain"/>.</param>
    /// <param name="eventArgs">The event data containing the loaded assembly.</param>
    private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs eventArgs)
    {
        AppDomain appDomain = sender as AppDomain ?? _appDomain;
        Assembly? assembly = eventArgs?.LoadedAssembly;
        if (assembly is null)
            return;

        IHotReloadContext? assemblyContext = _contextFactory(appDomain, assembly);
        if (assemblyContext is null)
            return;

        lock (_lock)
        {
            if (_context.IsHotReloadEnabled)
                assemblyContext.EnableHotReload();

            _context = _context.Combine(assemblyContext);
        }
    }
}
