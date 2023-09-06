using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for file operations.
/// </summary>
internal static class FileHelper
{
    /// <summary>
    /// A default timeout in milliseconds for retrying file read attempts.
    /// </summary>
    private const double DefaultTimeout = 5000;

    /// <summary>
    /// A default interval in milliseconds at which to check for the file's state.
    /// </summary>
    private const double DefaultPollingInterval = 50;

    /// <remarks>
    /// Implements retries in case of I/O exceptions.
    /// </remarks>
    /// <param name="path">The file to be opened for reading.</param>
    /// <param name="timeout">The maximum duration to keep retrying file read attempts. Defaults to 5 seconds if not specified or set to zero.</param>
    /// <param name="pollingInterval">The delay between each retry attempt. Defaults to 50 milliseconds if not specified or set to zero.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <inheritdoc cref="File.OpenRead(string)"/>
    public static async Task<FileStream> OpenReadAsync(string path, TimeSpan timeout = default, TimeSpan pollingInterval = default, CancellationToken cancellationToken = default)
    {
        Debug.Assert(File.Exists(path));

        long startTime = Stopwatch.GetTimestamp();

        if (timeout == TimeSpan.Zero)
            timeout = TimeSpan.FromMilliseconds(DefaultTimeout);
        else if (timeout < TimeSpan.Zero)
            timeout = TimeSpan.MaxValue;

        if (pollingInterval <= TimeSpan.Zero)
            pollingInterval = TimeSpan.FromMilliseconds(DefaultPollingInterval);

        while (true)
        {
            await Task.Delay(pollingInterval, cancellationToken).ConfigureAwait(false);

            try
            {
                return File.OpenRead(path);
            }
            catch (IOException)
            {
                TimeSpan elapsedTime = TimeSpan.FromTicks(Stopwatch.GetTimestamp() - startTime);
                if (elapsedTime > timeout)
                    throw;
            }
        }
    }

    /// <remarks>
    /// Implements retries in case of I/O exceptions.
    /// </remarks>
    /// <param name="path">The file to be opened for reading.</param>
    /// <param name="timeout">The maximum duration to keep retrying file read attempts. Defaults to infinite time.</param>
    /// <param name="pollingInterval">The delay between each retry attempt. Defaults to 50 milliseconds if not specified or set to zero.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <inheritdoc cref="File.ReadAllText(string)"/>
    public static async Task<string> ReadAllTextAsync(string path, TimeSpan timeout = default, TimeSpan pollingInterval = default, CancellationToken cancellationToken = default)
    {
        using FileStream stream = await OpenReadAsync(path, timeout, pollingInterval, cancellationToken).ConfigureAwait(false);
        using StreamReader reader = new(stream);

        string text = await reader.ReadToEndAsync().ConfigureAwait(false);
        return text;
    }

    /// <summary>
    /// Provides a string comparison mechanism suitable for comparing file names in a manner consistent with the current operating system.
    /// </summary>
    /// <remarks>
    /// On Windows, this uses a case-insensitive comparison respecting the current culture.
    /// On other platforms, it uses a case-sensitive comparison respecting the current culture.
    /// </remarks>
    public static StringComparer FileNameComparer
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.CurrentCultureIgnoreCase
            : StringComparer.CurrentCulture;
}
