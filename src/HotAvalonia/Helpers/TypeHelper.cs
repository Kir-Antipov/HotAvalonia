using System.Reflection;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for interacting with types.
/// </summary>
internal static class TypeHelper
{
    /// <summary>
    /// Represents binding flags for a static member, either public or non-public.
    /// </summary>
    private const BindingFlags StaticMember = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Represents binding flags for an instance member, either public or non-public.
    /// </summary>
    private const BindingFlags InstanceMember = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// Searches for the instance constructors defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="ConstructorInfo"/> objects representing all
    /// instance constructors defined for the given <see cref="Type"/>.
    /// </returns>
    public static ConstructorInfo[] GetInstanceConstructors(this Type declaringType)
        => declaringType.GetConstructors(InstanceMember);

    /// <summary>
    /// Searches for an instance constructor whose parameters match the specified argument types.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="types">
    /// An array of <see cref="Type"/> objects representing the number,
    /// order, and type of the parameters for the constructor to get.
    /// </param>
    /// <returns>
    /// A <see cref="ConstructorInfo"/> object representing the instance constructor that matches
    /// the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static ConstructorInfo? GetInstanceConstructor(this Type declaringType, params Type[] types)
        => declaringType.GetConstructor(InstanceMember, null, types, null);

    /// <summary>
    /// Searches for the static methods defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="MethodInfo"/> objects representing
    /// all static methods defined for the given <see cref="Type"/>.
    /// </returns>
    public static MethodInfo[] GetStaticMethods(this Type declaringType)
        => declaringType.GetMethods(StaticMember);

    /// <summary>
    /// Searches for the instance methods defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="MethodInfo"/> objects representing
    /// all instance methods defined for the given <see cref="Type"/>.
    /// </returns>
    public static MethodInfo[] GetInstanceMethods(this Type declaringType)
        => declaringType.GetMethods(InstanceMember);

    /// <summary>
    /// Searches for the specified static methods defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the methods to get.</param>
    /// <returns>
    /// An array of <see cref="MethodInfo"/> objects representing
    /// all matching static methods defined for the given <see cref="Type"/>.
    /// </returns>
    public static IEnumerable<MethodInfo> GetStaticMethods(this Type declaringType, string name)
        => declaringType.GetMethods(StaticMember).Where(x => StringComparer.Ordinal.Equals(x.Name, name));

    /// <summary>
    /// Searches for the specified instance methods defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the methods to get.</param>
    /// <returns>
    /// An array of <see cref="MethodInfo"/> objects representing
    /// all matching instance methods defined for the given <see cref="Type"/>.
    /// </returns>
    public static IEnumerable<MethodInfo> GetInstanceMethods(this Type declaringType, string name)
        => declaringType.GetMethods(InstanceMember).Where(x => StringComparer.Ordinal.Equals(x.Name, name));

    /// <inheritdoc cref="GetStaticMethod(Type, string, Type[])"/>
    public static MethodInfo? GetStaticMethod(this Type declaringType, string name)
        => declaringType.GetMethod(name, StaticMember);

    /// <summary>
    /// Searches for the specified static method.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the methods to get.</param>
    /// <param name="types">
    /// An array of <see cref="Type"/> objects representing the number,
    /// order, and type of the parameters for the method to get.
    /// </param>
    /// <returns>
    /// A <see cref="MethodInfo"/> object representing the method that matches
    /// the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static MethodInfo? GetStaticMethod(this Type declaringType, string name, params Type[] types)
        => declaringType.GetMethod(name, StaticMember, null, types, null);

    /// <inheritdoc cref="GetInstanceMethod(Type, string, Type[])"/>
    public static MethodInfo? GetInstanceMethod(this Type declaringType, string name)
        => declaringType.GetMethod(name, InstanceMember);

    /// <summary>
    /// Searches for the specified instance method.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the methods to get.</param>
    /// <param name="types">
    /// An array of <see cref="Type"/> objects representing the number,
    /// order, and type of the parameters for the method to get.
    /// </param>
    /// <returns>
    /// A <see cref="MethodInfo"/> object representing the method that matches
    /// the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static MethodInfo? GetInstanceMethod(this Type declaringType, string name, params Type[] types)
        => declaringType.GetMethod(name, InstanceMember, null, types, null);

    /// <summary>
    /// Searches for the static fields defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="FieldInfo"/> objects representing
    /// all static fields defined for the given <see cref="Type"/>.
    /// </returns>
    public static FieldInfo[] GetStaticFields(this Type declaringType)
        => declaringType.GetFields(StaticMember);

    /// <summary>
    /// Searches for the instance fields defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="FieldInfo"/> objects representing
    /// all instance fields defined for the given <see cref="Type"/>.
    /// </returns>
    public static FieldInfo[] GetInstanceFields(this Type declaringType)
        => declaringType.GetFields(InstanceMember);

    /// <inheritdoc cref="GetStaticField(Type, string, Type)"/>
    public static FieldInfo? GetStaticField(this Type declaringType, string name)
        => declaringType.GetField(name, StaticMember);

    /// <summary>
    /// Searches for the specified static field.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the field to get.</param>
    /// <param name="type">The type of the field object.</param>
    /// <returns>
    /// A <see cref="FieldInfo"/> object representing the field that matches
    /// the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static FieldInfo? GetStaticField(this Type declaringType, string name, Type type)
        => declaringType.GetField(name, StaticMember) is FieldInfo field && field.FieldType == type ? field : null;

    /// <inheritdoc cref="GetInstanceField(Type, string, Type)"/>
    public static FieldInfo? GetInstanceField(this Type declaringType, string name)
        => declaringType.GetField(name, InstanceMember);

    /// <summary>
    /// Searches for the specified instance field.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the field to get.</param>
    /// <param name="type">The type of the field object.</param>
    /// <returns>
    /// A <see cref="FieldInfo"/> object representing the field that matches
    /// the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static FieldInfo? GetInstanceField(this Type declaringType, string name, Type type)
        => declaringType.GetField(name, InstanceMember) is FieldInfo field && field.FieldType == type ? field : null;

    /// <summary>
    /// Searches for the static properties defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="PropertyInfo"/> objects representing
    /// all static properties defined for the given <see cref="Type"/>.
    /// </returns>
    public static PropertyInfo[] GetStaticProperties(this Type declaringType)
        => declaringType.GetProperties(StaticMember);

    /// <summary>
    /// Searches for the instance properties defined for the given <see cref="Type"/>.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <returns>
    /// An array of <see cref="PropertyInfo"/> objects representing
    /// all instance properties defined for the given <see cref="Type"/>.
    /// </returns>
    public static PropertyInfo[] GetInstanceProperties(this Type declaringType)
        => declaringType.GetProperties(InstanceMember);

    /// <inheritdoc cref="GetStaticProperty(Type, string, Type)"/>
    public static PropertyInfo? GetStaticProperty(this Type declaringType, string name)
        => declaringType.GetProperty(name, StaticMember);

    /// <summary>
    /// Searches for the specified static property.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the property to get.</param>
    /// <param name="type">The type of the property object.</param>
    /// <returns>
    /// A <see cref="PropertyInfo"/> object representing the property that
    /// matches the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static PropertyInfo? GetStaticProperty(this Type declaringType, string name, Type type)
        => declaringType.GetProperty(name, StaticMember) is PropertyInfo prop && prop.PropertyType == type ? prop : null;

    /// <inheritdoc cref="GetInstanceProperty(Type, string, Type)"/>
    public static PropertyInfo? GetInstanceProperty(this Type declaringType, string name)
        => declaringType.GetProperty(name, InstanceMember);

    /// <summary>
    /// Searches for the specified instance property.
    /// </summary>
    /// <param name="declaringType">The type to perform the search for.</param>
    /// <param name="name">The string containing the name of the property to get.</param>
    /// <param name="type">The type of the property object.</param>
    /// <returns>
    /// A <see cref="PropertyInfo"/> object representing the property that
    /// matches the specified requirements, if found; otherwise, <c>null</c>.
    /// </returns>
    public static PropertyInfo? GetInstanceProperty(this Type declaringType, string name, Type type)
        => declaringType.GetProperty(name, InstanceMember) is PropertyInfo prop && prop.PropertyType == type ? prop : null;
}
