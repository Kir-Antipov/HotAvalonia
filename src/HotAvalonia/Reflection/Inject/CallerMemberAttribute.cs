namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Allows you to obtain the metadata of the member that is the caller of the method.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
internal sealed class CallerMemberAttribute : Attribute
{
}
