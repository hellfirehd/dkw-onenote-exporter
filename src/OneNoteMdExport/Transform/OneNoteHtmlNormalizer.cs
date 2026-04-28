using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Output;

namespace OneNoteMdExport.Transform;

public sealed class OneNoteHtmlNormalizer(ExportOptions opt, ILogger<OneNoteHtmlNormalizer> logger)
{
    private readonly ExportOptions _opt = opt;
    private readonly ILogger<OneNoteHtmlNormalizer> _logger = logger;

    public async Task<String> NormalizeAsync(
        OneNotePageInfo page,
        String html,
        AssetDownloader assets,
        CancellationToken ct = default)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        if (_opt.IncludeImages)
        {
            await ProcessImagesAsync(doc, page, assets, ct);
        }

        StripStyles(doc);
        RemoveEmptyParagraphs(doc);

        var body = doc.DocumentNode.SelectSingleNode("//body");
        return body?.InnerHtml ?? doc.DocumentNode.InnerHtml;
    }

    // ── Image handling ───────────────────────────────────────────────────────

    private async Task ProcessImagesAsync(
        HtmlDocument doc,
        OneNotePageInfo page,
        AssetDownloader assets,
        CancellationToken ct)
    {
        var images = doc.DocumentNode.SelectNodes("//img");
        if (images is null)
        {
            return;
        }

        foreach (var img in images.ToList())
        {
            // Prefer full-resolution src; fall back to regular src
            var src = img.Attributes["data-fullres-src"]?.Value
                       ?? img.Attributes["src"]?.Value;
            var mimeType = img.Attributes["data-src-type"]?.Value;

            if (String.IsNullOrEmpty(src))
            {
                continue;
            }

            var localPath = await assets.DownloadResourceAsync(page, src, mimeType, ct);
            if (localPath is not null)
            {
                img.SetAttributeValue("src", localPath);
                img.Attributes.Remove("data-fullres-src");
                img.Attributes.Remove("data-render-src");
                img.Attributes.Remove("data-src-type");
                img.Attributes.Remove("data-fullres-src-type");
            }
            else
            {
                _logger.LogDebug("Image not downloaded, keeping original src: {Src}", src);
            }
        }
    }

    // ── Style stripping ──────────────────────────────────────────────────────

    private static void StripStyles(HtmlDocument doc)
    {
        foreach (var node in doc.DocumentNode.DescendantsAndSelf().ToList())
        {
            if (node.NodeType != HtmlNodeType.Element)
            {
                continue;
            }

            node.Attributes.Remove("style");
            node.Attributes.Remove("class");
            node.Attributes.Remove("lang");
            node.Attributes.Remove("data-id");
            node.Attributes.Remove("data-tag");
            node.Attributes.Remove("id");
        }
    }

    // ── Empty paragraph removal ──────────────────────────────────────────────

    private static void RemoveEmptyParagraphs(HtmlDocument doc)
    {
        var paras = doc.DocumentNode.SelectNodes("//p");
        if (paras is null)
        {
            return;
        }

        foreach (var p in paras.ToList())
        {
            // Keep paragraphs that contain images or other media
            if (p.Descendants().Any(n => n.Name is "img" or "object" or "video"))
            {
                continue;
            }

            if (String.IsNullOrWhiteSpace(p.InnerText))
            {
                p.ParentNode?.RemoveChild(p);
            }
        }
    }
}
