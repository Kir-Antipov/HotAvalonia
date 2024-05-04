using System.Reflection;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace HotAvalonia;

/// <summary>
/// Represents a reference to a named control within an Avalonia UI.
/// </summary>
public sealed class AvaloniaNamedControlReference
{
    /// <summary>
    /// The name of the control.
    /// </summary>
    private readonly string _name;

    /// <summary>
    /// The type of the control.
    /// </summary>
    private readonly Type _controlType;

    /// <summary>
    /// A cache field associated with this control reference.
    /// </summary>
    private readonly FieldInfo? _cache;

    /// <inheritdoc cref="AvaloniaNamedControlReference(string, Type, FieldInfo?)"/>
    public AvaloniaNamedControlReference(string name, Type type)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _controlType = type ?? throw new ArgumentNullException(nameof(type));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaNamedControlReference"/> class.
    /// </summary>
    /// <param name="name">The name of the control.</param>
    /// <param name="type">The type of the control.</param>
    /// <param name="cache">A cache field associated with this control reference.</param>
    internal AvaloniaNamedControlReference(string name, Type type, FieldInfo? cache)
        : this(name, type)
    {
        _cache = cache;
    }

    /// <summary>
    /// The name of the control.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// The type of the control.
    /// </summary>
    public Type ControlType => _controlType;

    /// <summary>
    /// Resolves the named control within the specified scope.
    /// </summary>
    /// <param name="scope">The scope within which to resolve the control.</param>
    /// <returns>The resolved control, if found; otherwise, <c>null</c>.</returns>
    public object? Resolve(object scope)
    {
        _ = scope ?? throw new ArgumentNullException(nameof(scope));

        object? control = (scope as ILogical)?.FindNameScope()?.Find(_name);
        return _controlType.IsAssignableFrom(control?.GetType()) ? control : null;
    }

    /// <summary>
    /// Invalidates the internal cache associated with this reference
    /// within the specified scope.
    /// </summary>
    /// <param name="scope">The scope within which to refresh the cache.</param>
    internal void Refresh(object scope)
    {
        _ = scope ?? throw new ArgumentNullException(nameof(scope));
        if (_cache is { IsStatic: false } && !_cache.DeclaringType.IsAssignableFrom(scope.GetType()))
            return;

        _cache?.SetValue(scope, Resolve(scope));
    }
}
