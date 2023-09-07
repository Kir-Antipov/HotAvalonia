using System.Text;

namespace HotAvalonia.Helpers;

/// <summary>
/// Provides utility methods for manipulating and resolving URI paths.
/// </summary>
internal static class UriHelper
{
    /// <summary>
    /// Combines a root path with a URI to form a complete path.
    /// </summary>
    /// <param name="root">The root path.</param>
    /// <param name="uri">The URI to resolve.</param>
    /// <returns>The combined path.</returns>
    public static string ResolvePathFromUri(string root, Uri uri)
        => Path.Combine(root, uri.AbsolutePath.Substring(1));

    /// <summary>
    /// Combines a root path with a URI string to form a complete path.
    /// </summary>
    /// <param name="root">The root path.</param>
    /// <param name="uri">The URI string to resolve.</param>
    /// <returns>The combined path.</returns>
    public static string ResolvePathFromUri(string root, string uri)
        => Path.Combine(root, GetUriPathFromString(uri).Substring(1));

    /// <summary>
    /// Resolves a host path using a given URI and path.
    /// </summary>
    /// <param name="uri">The URI to use for resolving.</param>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved host path.</returns>
    public static string ResolveHostPath(Uri uri, string path)
        => ResolveHostPathFromUriPath(uri.AbsolutePath, path);

    /// <summary>
    /// Resolves a host path using a given URI string and path.
    /// </summary>
    /// <param name="uri">The URI string to use for resolving.</param>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved host path.</returns>
    public static string ResolveHostPath(string uri, string path)
        => ResolveHostPathFromUriPath(GetUriPathFromString(uri), path);

    /// <summary>
    /// Resolves a host path using a URI path and another path.
    /// </summary>
    /// <param name="uriPath">The URI path to use for resolving.</param>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The resolved host path.</returns>
    private static string ResolveHostPathFromUriPath(string uriPath, string path)
    {
        ReadOnlySpan<char> uriPathSpan = uriPath.AsSpan();
        ReadOnlySpan<char> pathSpan = new Uri(path).AbsolutePath.AsSpan();

        if (uriPath.EndsWith("/"))
            uriPathSpan = uriPathSpan.Slice(0, uriPathSpan.Length - 1);

        if (path.EndsWith("/"))
            pathSpan = pathSpan.Slice(0, pathSpan.Length - 1);

        int depth = 0;
        foreach (char x in uriPathSpan)
            if (x is '/')
                ++depth;

        for (int i = 0; i < depth; ++i)
        {
            int separatorIndex = pathSpan.LastIndexOf('/');
            if (separatorIndex is -1)
                break;

            pathSpan = pathSpan.Slice(0, separatorIndex);
        }

        return pathSpan.ToString();
    }

    /// <summary>
    /// Extracts the path segment from a URI string.
    /// </summary>
    /// <param name="uri">The URI string.</param>
    /// <returns>The extracted path segment.</returns>
    private static string GetUriPathFromString(string uri)
    {
        int protocolIndex = uri.IndexOf(Uri.SchemeDelimiter);
        int firstSegmentIndex = uri.IndexOf('/', protocolIndex is -1 ? 0 : (protocolIndex + Uri.SchemeDelimiter.Length));
        string uriPath = firstSegmentIndex is -1 ? uri : uri.Substring(firstSegmentIndex);

        return uriPath;
    }

    /// <summary>
    /// Generates a URI identifier with unsafe characters replaced.
    /// </summary>
    /// <param name="uri">The URI for which to generate an identifier.</param>
    /// <returns>The safe URI identifier.</returns>
    public static string GetSafeUriIdentifier(Uri uri)
        => GetSafeUriIdentifier(uri.ToString());

    /// <summary>
    /// Generates a URI identifier with unsafe characters replaced from a URI string.
    /// </summary>
    /// <param name="uri">The URI string for which to generate an identifier.</param>
    /// <returns>The safe URI identifier.</returns>
    public static string GetSafeUriIdentifier(string uri)
    {
        StringBuilder safeUri = new(uri);
        for (int i = 0; i < safeUri.Length; ++i)
        {
            // Do NOT replace with `!char.IsLetterOrDigit(...)`
            // We are mimicking the logic that Avalonia uses:
            // https://github.com/AvaloniaUI/Avalonia/blob/d6ecb517b8dd2597aa72fb09cad65b299b8d9bb1/src/Markup/Avalonia.Markup.Xaml.Loader/AvaloniaXamlIlRuntimeCompiler.cs#L342C1-L350C10
            if (safeUri[i] is ':' or '/' or '?' or '=' or '.')
                safeUri[i] = '_';
        }

        return safeUri.ToString();
    }
}
