using System.Reflection;
using System.Runtime.CompilerServices;

namespace HotAvalonia.Reflection.Inject;

/// <summary>
/// Enumerates the types of special callback parameters identified by their attributes.
/// </summary>
internal enum CallbackParameterType
{
    /// <summary>
    /// Indicates that the parameter is not specialized.
    /// </summary>
    None,

    /// <summary>
    /// Indicates that the parameter serves as the return value if the callback overrides the original method.
    /// </summary>
    CallbackResult,

    /// <summary>
    /// Indicates that the parameter receives the instance of the object that is the caller of the method.
    /// </summary>
    Caller,

    /// <summary>
    /// Indicates that the parameter receives the metadata of the member that is the caller of the method.
    /// </summary>
    CallerMember,

    /// <summary>
    /// Indicates that the parameter receives the name of the member that is the caller of the method.
    /// </summary>
    CallerMemberName,
}

/// <summary>
/// Provides extension methods for the <see cref="CallbackParameterType"/> enumeration.
/// </summary>
internal static class CallbackParameterTypeExtensions
{
    /// <summary>
    /// Determines the <see cref="CallbackParameterType"/> for a given parameter based on its attributes.
    /// </summary>
    /// <param name="parameter">The parameter to inspect.</param>
    /// <returns>The identified <see cref="CallbackParameterType"/> for the parameter.</returns>
    public static CallbackParameterType GetCallbackParameterType(this ParameterInfo parameter)
    {
        foreach (CustomAttributeData customAttribute in parameter.CustomAttributes)
        {
            switch (customAttribute.AttributeType.Name)
            {
                case nameof(CallbackResultAttribute) when parameter.IsOut:
                    return CallbackParameterType.CallbackResult;

                case nameof(CallerAttribute):
                    return CallbackParameterType.Caller;

                case nameof(CallerMemberAttribute) when typeof(MemberInfo).IsAssignableFrom(parameter.ParameterType):
                    return CallbackParameterType.CallerMember;

                case nameof(CallerMemberNameAttribute) when parameter.ParameterType == typeof(string):
                    return CallbackParameterType.CallerMemberName;
            }
        }

        return CallbackParameterType.None;
    }
}
