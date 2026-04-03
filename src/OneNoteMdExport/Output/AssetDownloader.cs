using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Util;

namespace OneNoteMdExport.Output;

public sealed class AssetDownloader
{
    private readonly GraphServiceClient _g;
    private readonly PathLayout _layout;
    private readonly ExportOptions _opt;
    private readonly ILogger<AssetDownloader> _logger;

    // Cache resource URL → relative local path so the same image isn't downloaded twice
    private readonly Dictionary<string, string> _cache = [];

    // Extracts the OneNote resource ID from a Graph resource URL
    private static readonly Regex ResourceIdRegex = new(
        @"/onenote/resources/([^/?]+)/content",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AssetDownloader(
        GraphServiceClient g,
        PathLayout layout,
        ExportOptions opt,
        ILogger<AssetDownloader> logger)
    {
        _g = g;
        _layout = layout;
        _opt = opt;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the resource at <paramref name="url"/> into the page's assets directory
    /// and returns the relative path from the page's directory, or <c>null</c> if skipped/failed.
    /// </summary>
    public async Task<string?> DownloadResourceAsync(
        OneNotePageInfo page,
        string url,
        string? mimeType,
        CancellationToken ct = default)
    {
        if (!ShouldDownload(mimeType)) return null;

        // Return cached path for the same resource
        if (_cache.TryGetValue(url, out var cached)) return cached;

        var match = ResourceIdRegex.Match(url);
        if (!match.Success)
        {
            _logger.LogDebug("Cannot extract resource ID from URL: {Url}", url);
            return null;
        }

        var resourceId = Uri.UnescapeDataString(match.Groups[1].Value);

        try
        {
            using var stream = await Retry.ExecuteAsync(
                () => _g.Me.Onenote.Resources[resourceId].Content.GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);

            if (stream is null) return null;

            var assetsDir = _layout.AssetsDir(page);
            Directory.CreateDirectory(assetsDir);

            var ext = GuessExtension(mimeType, url);
            // Use a short hash of the resource ID for stable, collision-free filenames
            var hash = Math.Abs(resourceId.GetHashCode()).ToString("x8");
            var fileName = $"{hash}{ext}";
            var filePath = Path.Combine(assetsDir, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                await stream.CopyToAsync(fs, ct);

            // Return a relative path (using forward slashes for Markdown portability)
            var pageDir = Path.GetDirectoryName(_layout.PagePath(page))!;
            var relativePath = Path.GetRelativePath(pageDir, filePath).Replace('\\', '/');

            _cache[url] = relativePath;
            _logger.LogDebug("Downloaded asset → {Path}", relativePath);
            return relativePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download resource {ResourceId}", resourceId);
            return null;
        }
    }

    private bool ShouldDownload(string? mimeType)
    {
        if (mimeType is null) return _opt.IncludeImages; // assume image when unknown
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return _opt.IncludeImages;
        return _opt.IncludeAttachments;
    }

    private static string GuessExtension(string? mimeType, string url)
    {
        if (mimeType is not null)
        {
            return mimeType.ToLowerInvariant() switch
            {
                "image/png" => ".png",
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                "image/svg+xml" => ".svg",
                "image/bmp" => ".bmp",
                "image/tiff" => ".tiff",
                _ => ".bin",
            };
        }

        // Fall back to URL extension
        var urlPath = url.Split('?')[0];
        var ext = Path.GetExtension(urlPath);
        return string.IsNullOrEmpty(ext) ? ".bin" : ext;
    }
}
