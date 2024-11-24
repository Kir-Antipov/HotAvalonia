using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Platform;
using HotAvalonia.Helpers;
using HotAvalonia.IO;

namespace HotAvalonia.Assets;

/// <summary>
/// Provides a way to load dynamic assets.
/// </summary>
internal sealed class DynamicAssetLoader : IAssetLoader
{
    /// <summary>
    /// The fallback asset loader used when dynamic asset loading fails.
    /// </summary>
    private readonly IAssetLoader _assetLoader;

    /// <summary>
    /// The file system accessor.
    /// </summary>
    private readonly CachingFileSystemAccessor _fileSystem;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicAssetLoader"/> class.
    /// </summary>
    /// <param name="fallbackAssetLoader">
    /// The fallback <see cref="IAssetLoader"/> to use when dynamic asset loading fails.
    /// </param>
    public DynamicAssetLoader(IAssetLoader fallbackAssetLoader)
    {
        _assetLoader = fallbackAssetLoader ?? throw new ArgumentNullException(nameof(fallbackAssetLoader));
        _fileSystem = new();
    }

    /// <summary>
    /// Gets the fallback asset loader that is used when dynamic asset loading fails.
    /// </summary>
    public IAssetLoader FallbackAssetLoader => _assetLoader;

    /// <inheritdoc/>
    void IAssetLoader.SetDefaultAssembly(Assembly assembly)
        => _assetLoader.SetDefaultAssembly(assembly);

    /// <inheritdoc/>
    public Assembly? GetAssembly(Uri uri, Uri? baseUri = null)
        => _assetLoader.GetAssembly(uri, baseUri);

    /// <inheritdoc/>
    public bool Exists(Uri uri, Uri? baseUri = null)
    {
        if (_assetLoader.Exists(uri, baseUri))
            return true;

        if (TryGetAssetInfo(uri, baseUri, out AssetInfo? asset))
            return File.Exists(asset.Path);

        return false;
    }

    /// <inheritdoc/>
    public IEnumerable<Uri> GetAssets(Uri uri, Uri? baseUri)
    {
        IEnumerable<Uri> assets = _assetLoader.GetAssets(uri, baseUri);

        if (TryGetAssetInfo(uri, baseUri, out AssetInfo? asset) && Directory.Exists(asset.Path))
        {
            Uri assemblyUri = new UriBuilder(asset.Uri.Scheme, asset.Uri.Host).Uri;
            Uri projectUri = asset.Project;
            IEnumerable<Uri> fileAssets = Directory
                .EnumerateFiles(asset.Path, "*", SearchOption.AllDirectories)
                .Select(x => projectUri.MakeRelativeUri(new(Path.GetFullPath(x))))
                .Select(x => new Uri(assemblyUri, x));

            assets = assets.Concat(fileAssets).Distinct();
        }

        return assets;
    }

    /// <inheritdoc/>
    public Stream Open(Uri uri, Uri? baseUri = null)
        => OpenAndGetAssembly(uri, baseUri).stream;

    /// <inheritdoc/>
    public (Stream stream, Assembly assembly) OpenAndGetAssembly(Uri uri, Uri? baseUri = null)
    {
        if (!TryGetAssetInfo(uri, baseUri, out AssetInfo? asset) || !_fileSystem.Exists(asset.Path))
            return _assetLoader.OpenAndGetAssembly(uri, baseUri);

        return (_fileSystem.Open(asset.Path), asset.Assembly);
    }

    /// <summary>
    /// Attempts to retrieve asset information for the given URI.
    /// </summary>
    /// <param name="uri">The URI of the asset to resolve.</param>
    /// <param name="baseUri">An optional base URI for resolving relative URIs.</param>
    /// <param name="assetInfo">
    /// When this method returns, contains the resolved <see cref="AssetInfo"/>
    /// if the URI is valid; otherwise, <c>null</c>.
    /// </param>
    /// <returns>
    /// <c>true</c> if the asset information was successfully retrieved;
    /// otherwise, <c>false</c>.
    /// </returns>
    private bool TryGetAssetInfo(Uri uri, Uri? baseUri, [NotNullWhen(true)] out AssetInfo? assetInfo)
    {
        assetInfo = null;
        if (uri is null || !uri.IsAbsoluteUri && baseUri is null)
            return false;

        Uri absoluteUri = uri.AsAbsoluteUri(baseUri);
        if (absoluteUri.Scheme != UriHelper.AvaloniaResourceScheme)
            return false;

        Assembly? assembly = GetAssembly(absoluteUri);
        if (assembly is null)
            return false;

        if (!AvaloniaProjectLocator.TryGetDirectoryName(assembly, out string? rootPath))
            return false;

        assetInfo = new(absoluteUri, assembly, rootPath);
        return true;
    }
}
