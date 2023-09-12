namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Allows you to specify a parameter that will serve as the return value if the callback overrides the original method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class CallbackResultAttribute : Attribute
{
}
