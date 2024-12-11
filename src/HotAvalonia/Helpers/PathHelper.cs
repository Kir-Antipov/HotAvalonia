using System.Runtime.InteropServices;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides helper methods for working with file paths.
/// </summary>
internal static class PathHelper
{
    /// <summary>
    /// Provides a string comparison mechanism suitable for comparing
    /// file names in a manner consistent with the current operating system.
    /// </summary>
    /// <remarks>
    /// On Windows, this uses a case-insensitive comparison.
    /// On other platforms, it uses a case-sensitive comparison.
    /// </remarks>
    public static StringComparer PathComparer { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.CurrentCultureIgnoreCase
            : StringComparer.CurrentCulture;

    /// <inheritdoc cref="PathComparer"/>
    public static StringComparison PathComparison { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.CurrentCultureIgnoreCase
            : StringComparison.CurrentCulture;

    /// <summary>
    /// Extracts the volume name from the given path.
    /// </summary>
    /// <param name="path">
    /// The file path from which to extract the volume name.
    /// </param>
    /// <returns>
    /// The volume name if it exists; otherwise, an empty string.
    /// </returns>
    public static string GetVolumeName(string path)
    {
        int i = path.IndexOf(Path.VolumeSeparatorChar);
        return i <= 0 ? string.Empty : path.Substring(0, i);
    }

    /// <summary>
    /// Finds the common base path shared by two file paths.
    /// </summary>
    /// <param name="leftPath">The first file path to compare.</param>
    /// <param name="rightPath">The second file path to compare.</param>
    /// <returns>
    /// The longest common base path shared by both file paths.
    /// If no common path exists, an empty string is returned.
    /// </returns>
    public static string GetCommonPath(string leftPath, string rightPath)
    {
        StringComparison comparison = PathComparison;
        ReadOnlySpan<char> left = Path.GetFullPath(leftPath).AsSpan();
        ReadOnlySpan<char> right = Path.GetFullPath(rightPath).AsSpan();
        int length = 0;

        while (true)
        {
            ReadOnlySpan<char> remLeft = left.Slice(length);
            ReadOnlySpan<char> remRight = right.Slice(length);
            int hasSeparator = 1;

            int nextLeftLength = remLeft.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (nextLeftLength < 0)
            {
                nextLeftLength = remLeft.Length;
                hasSeparator = 0;
            }

            int nextRightLength = remRight.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (nextRightLength < 0)
            {
                nextRightLength = remRight.Length;
                hasSeparator = 0;
            }

            if (nextLeftLength != nextRightLength)
                break;

            ReadOnlySpan<char> nextLeft = left.Slice(length, nextLeftLength);
            ReadOnlySpan<char> nextRight = right.Slice(length, nextRightLength);
            if (!nextLeft.Equals(nextRight, comparison))
                break;

            length += nextLeftLength + hasSeparator;
            if (length >= left.Length || length >= right.Length)
                break;
        }

        return left.Slice(0, length).ToString();
    }
}
