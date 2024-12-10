using Avalonia.Logging;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for logging within the hot reload context.
/// </summary>
internal static class LoggingHelper
{
    /// <summary>
    /// The parametrized logger for error-level events related to the hot reload context, or
    /// <c>null</c> if no logger is available.
    /// </summary>
    private static ParametrizedLogger? Logger { get; } = Avalonia.Logging.Logger.TryGet(LogEventLevel.Error, nameof(HotAvalonia));

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log(string messageTemplate)
        => Logger?.Log(source: null, $" {messageTemplate}");

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log<T0>(string messageTemplate, T0 arg0)
        => Logger?.Log(source: null, $" {messageTemplate}", arg0);

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log<T0, T1>(string messageTemplate, T0 arg0, T1 arg1)
        => Logger?.Log(source: null, $" {messageTemplate}", arg0, arg1);

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log<T0, T1, T2>(string messageTemplate, T0 arg0, T1 arg1, T2 arg2)
        => Logger?.Log(source: null, $" {messageTemplate}", arg0, arg1, arg2);

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log(object? source, string messageTemplate)
        => Logger?.Log(source, messageTemplate);

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log<T0>(object? source, string messageTemplate, T0 arg0)
        => Logger?.Log(source, messageTemplate, arg0);

    /// <inheritdoc cref="Log{T0, T1, T2}(object?, string, T0, T1, T2)"/>
    public static void Log<T0, T1>(object? source, string messageTemplate, T0 arg0, T1 arg1)
        => Logger?.Log(source, messageTemplate, arg0, arg1);

    /// <summary>
    /// Logs an event.
    /// </summary>
    /// <typeparam name="T0">The type of the first object to format.</typeparam>
    /// <typeparam name="T1">The type of the second object to format.</typeparam>
    /// <typeparam name="T2">The type of the third object to format.</typeparam>
    /// <param name="source">The object from which the event originates.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="arg0">The first object to format.</param>
    /// <param name="arg1">The second object to format.</param>
    /// <param name="arg2">The third object to format.</param>
    public static void Log<T0, T1, T2>(object? source, string messageTemplate, T0 arg0, T1 arg1, T2 arg2)
        => Logger?.Log(source, messageTemplate, arg0, arg1, arg2);
}
