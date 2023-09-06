using Avalonia.Logging;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for logging within the hot reload context.
/// </summary>
internal static class LoggingHelper
{
    /// <summary>
    /// The log area that relates to the hot reload context.
    /// </summary>
    public const string HotReloadLogArea = "HotReload";

    /// <summary>
    /// The parametrized logger for error-level events related to the hot reload context, or
    /// <c>null</c> if no logger is available.
    /// </summary>
    public static ParametrizedLogger? Logger
        => Avalonia.Logging.Logger.TryGet(LogEventLevel.Error, HotReloadLogArea);
}
