namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Allows you to obtain the instance of the object that is the caller of the method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class CallerAttribute : Attribute
{
}
