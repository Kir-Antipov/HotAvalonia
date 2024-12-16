using Avalonia;
using HotAvalonia.Helpers;

namespace HotAvalonia.DependencyInjection;

/// <summary>
/// Provides services for Avalonia applications.
/// </summary>
internal sealed class AvaloniaServiceProvider : IServiceProvider
{
    /// <summary>
    /// A registry that maps service types to their corresponding factory functions.
    /// </summary>
    private readonly IDictionary<Type, Func<object?>> _registry;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaServiceProvider"/> class.
    /// </summary>
    /// <param name="registry">
    /// A registry containing service type to factory mappings.
    /// </param>
    private AvaloniaServiceProvider(IDictionary<Type, Func<object?>> registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Gets the current instance of the <see cref="AvaloniaServiceProvider"/>.
    /// </summary>
    public static AvaloniaServiceProvider Current => FromAvaloniaLocator(CurrentLocator);

    /// <summary>
    /// Gets the current instance of the <see cref="AvaloniaLocator"/>.
    /// </summary>
    private static AvaloniaLocator CurrentLocator => typeof(AvaloniaLocator)
        .GetStaticFields()
        .Select(static x => x.GetValue(null))
        .OfType<AvaloniaLocator>()
        .First();

    /// <summary>
    /// Creates an <see cref="AvaloniaServiceProvider"/> instance
    /// from the specified <see cref="AvaloniaLocator"/>.
    /// </summary>
    /// <param name="locator">
    /// An <see cref="AvaloniaLocator"/> instance used to resolve service factories.
    /// </param>
    /// <returns>
    /// An <see cref="AvaloniaServiceProvider"/> initialized with services from the given locator.
    /// </returns>
    private static AvaloniaServiceProvider FromAvaloniaLocator(AvaloniaLocator locator)
    {
        _ = locator ?? throw new ArgumentNullException(nameof(locator));

        IDictionary<Type, Func<object?>> registry = locator.GetType()
            .GetInstanceFields()
            .Select(x => x.GetValue(locator))
            .OfType<IDictionary<Type, Func<object?>>>()
            .First()!;

        return new(registry);
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType)
    {
        _ = serviceType ?? throw new ArgumentNullException(nameof(serviceType));

        if (_registry.TryGetValue(serviceType, out Func<object?>? factory))
            return factory();

        return null;
    }

    /// <summary>
    /// Registers a factory for the specified service type.
    /// </summary>
    /// <param name="serviceType">The <see cref="Type"/> of the service to register.</param>
    /// <param name="factory">A factory function that produces the service instance.</param>
    public void SetService(Type serviceType, Func<IServiceProvider, object?> factory)
    {
        _ = serviceType ?? throw new ArgumentNullException(nameof(serviceType));
        _ = factory ?? throw new ArgumentNullException(nameof(factory));

        _registry[serviceType] = () => factory(this);
    }
}
