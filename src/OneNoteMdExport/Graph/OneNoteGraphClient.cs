using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Util;

namespace OneNoteMdExport.Graph;

public sealed class OneNoteGraphClient
{
    private readonly GraphServiceClient _g;
    private readonly ILogger<OneNoteGraphClient> _logger;

    public OneNoteGraphClient(GraphServiceClient g, ILogger<OneNoteGraphClient> logger)
    {
        _g = g;
        _logger = logger;
    }

    /// <summary>
    /// Yields all pages, either flat across all notebooks (default) or filtered
    /// to a single notebook when <see cref="ExportOptions.NotebookFilter"/> is set.
    /// Handles @odata.nextLink pagination; requests up to 100 pages per batch.
    /// </summary>
    public async IAsyncEnumerable<OneNotePageInfo> EnumeratePagesAsync(
        ExportOptions opt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (opt.NotebookFilter is not null)
        {
            await foreach (var p in EnumerateByNotebookAsync(opt.NotebookFilter, ct))
                yield return p;
        }
        else
        {
            await foreach (var p in EnumerateAllPagesAsync(ct))
                yield return p;
        }
    }

    // ── Flat enumeration ────────────────────────────────────────────────────

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateAllPagesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Enumerating all OneNote pages…");
        int count = 0;

        var response = await Retry.ExecuteAsync(
            () => _g.Me.Onenote.Pages.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Expand = ["parentNotebook", "parentSection"];
                cfg.QueryParameters.Orderby = ["lastModifiedDateTime desc"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var page in response.Value ?? [])
            {
                count++;
                yield return OneNotePageInfo.FromGraph(page);
            }

            if (response.OdataNextLink is null) break;

            _logger.LogDebug("Fetching next batch…");
            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => _g.Me.Onenote.Pages.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }

        _logger.LogInformation("Found {Count} pages.", count);
    }

    // ── Filtered enumeration (notebook → section → pages) ───────────────────

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateByNotebookAsync(
        string notebookName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var notebooks = await Retry.ExecuteAsync(
            () => _g.Me.Onenote.Notebooks.GetAsync(cancellationToken: ct),
            logger: _logger, ct: ct);

        var notebook = notebooks?.Value?.FirstOrDefault(n =>
            string.Equals(n.DisplayName, notebookName, StringComparison.OrdinalIgnoreCase));

        if (notebook?.Id is null)
        {
            _logger.LogWarning("Notebook '{Name}' not found.", notebookName);
            yield break;
        }

        _logger.LogInformation("Enumerating sections in '{Notebook}'…", notebook.DisplayName);

        var notebookId = notebook.Id;
        var sections = await Retry.ExecuteAsync(
            () => _g.Me.Onenote.Notebooks[notebookId].Sections.GetAsync(cancellationToken: ct),
            logger: _logger, ct: ct);

        foreach (var section in sections?.Value ?? [])
        {
            if (section.Id is null) continue;
            _logger.LogDebug("  Section: {Section}", section.DisplayName);

            var sectionId = section.Id;
            var pageResponse = await Retry.ExecuteAsync(
                () => _g.Me.Onenote.Sections[sectionId].Pages.GetAsync(cfg =>
                {
                    cfg.QueryParameters.Top = 100;
                    cfg.QueryParameters.Expand = ["parentNotebook", "parentSection"];
                }, ct),
                logger: _logger, ct: ct);

            while (pageResponse is not null)
            {
                foreach (var page in pageResponse.Value ?? [])
                    yield return OneNotePageInfo.FromGraph(page);

                if (pageResponse.OdataNextLink is null) break;

                var nextLink = pageResponse.OdataNextLink;
                pageResponse = await Retry.ExecuteAsync(
                    () => _g.Me.Onenote.Pages.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                    logger: _logger, ct: ct);
            }
        }
    }

    // ── Content fetch ────────────────────────────────────────────────────────

    /// <summary>Downloads the HTML content of a single page.</summary>
    public async Task<string> GetPageHtmlAsync(string pageId, CancellationToken ct = default)
    {
        var stream = await Retry.ExecuteAsync(
            () => _g.Me.Onenote.Pages[pageId].Content.GetAsync(cancellationToken: ct),
            logger: _logger, ct: ct);

        if (stream is null) return string.Empty;

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
