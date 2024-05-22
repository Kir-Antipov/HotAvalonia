namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Represents a method injection technique.
/// </summary>
/// <remarks>
/// Implementations of this interface should handle the injection process and
/// properly manage all the associated resources.
///
/// When the injection is no longer needed, the instance should be disposed
/// in order to release any allocated resources and revert all the effects
/// caused by the injection.
/// </remarks>
internal interface IInjection : IDisposable;
