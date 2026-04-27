using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
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
    /// Yields all pages, either across all notebooks (default) or filtered
    /// to a single notebook when <see cref="ExportOptions.NotebookFilter"/> is set.
    /// Traverses notebook -> sections -> pages because the flat pages endpoint
    /// can fail for accounts with a high number of sections.
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

    // ── Account-wide enumeration ────────────────────────────────────────────

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateAllPagesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation("Enumerating all OneNote pages by notebook and section…");
        var count = 0;

        await foreach (var notebook in EnumerateNotebooksAsync(ct))
        {
            if (notebook.Id is null) continue;

            _logger.LogDebug("Notebook: {Notebook}", notebook.DisplayName);

            await foreach (var page in EnumerateNotebookPagesAsync(notebook.Id, ct))
            {
                count++;
                yield return page;
            }
        }

        _logger.LogInformation("Found {Count} pages.", count);
    }

    // ── Filtered enumeration (notebook → section → pages) ───────────────────

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateByNotebookAsync(
        String notebookName,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var notebooks = await Retry.ExecuteAsync(
            () => _g.Me.Onenote.Notebooks.GetAsync(cancellationToken: ct),
            logger: _logger, ct: ct);

        var notebook = notebooks?.Value?.FirstOrDefault(n =>
            String.Equals(n.DisplayName, notebookName, StringComparison.OrdinalIgnoreCase));

        if (notebook?.Id is null)
        {
            _logger.LogWarning("Notebook '{Name}' not found.", notebookName);
            yield break;
        }

        _logger.LogInformation("Enumerating sections in '{Notebook}'…", notebook.DisplayName);

        await foreach (var page in EnumerateNotebookPagesAsync(notebook.Id, ct))
            yield return page;
    }

    private async IAsyncEnumerable<Notebook> EnumerateNotebooksAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.Notebooks;
        var response = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var notebook in response.Value ?? [])
                yield return notebook;

            if (response.OdataNextLink is null) break;

            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateNotebookPagesAsync(
        String notebookId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var section in EnumerateNotebookSectionsAsync(notebookId, ct))
        {
            if (section.Id is null) continue;

            _logger.LogDebug("  Section: {Section}", section.DisplayName);

            await foreach (var page in EnumerateSectionPagesAsync(section.Id, ct))
                yield return page;
        }

        await foreach (var group in EnumerateNotebookSectionGroupsAsync(notebookId, ct))
        {
            if (group.Id is null) continue;

            _logger.LogDebug("  Section group: {SectionGroup}", group.DisplayName);

            await foreach (var page in EnumerateSectionGroupPagesAsync(group.Id, ct))
                yield return page;
        }
    }

    private async IAsyncEnumerable<OnenoteSection> EnumerateNotebookSectionsAsync(
        String notebookId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.Notebooks[notebookId].Sections;
        var response = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var section in response.Value ?? [])
                yield return section;

            if (response.OdataNextLink is null) break;

            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    private async IAsyncEnumerable<SectionGroup> EnumerateNotebookSectionGroupsAsync(
        String notebookId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.Notebooks[notebookId].SectionGroups;
        var response = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var group in response.Value ?? [])
                yield return group;

            if (response.OdataNextLink is null) break;

            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateSectionGroupPagesAsync(
        String sectionGroupId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var section in EnumerateSectionGroupSectionsAsync(sectionGroupId, ct))
        {
            if (section.Id is null) continue;

            _logger.LogDebug("    Section: {Section}", section.DisplayName);

            await foreach (var page in EnumerateSectionPagesAsync(section.Id, ct))
                yield return page;
        }

        await foreach (var group in EnumerateChildSectionGroupsAsync(sectionGroupId, ct))
        {
            if (group.Id is null) continue;

            _logger.LogDebug("    Section group: {SectionGroup}", group.DisplayName);

            await foreach (var page in EnumerateSectionGroupPagesAsync(group.Id, ct))
                yield return page;
        }
    }

    private async IAsyncEnumerable<OnenoteSection> EnumerateSectionGroupSectionsAsync(
        String sectionGroupId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.SectionGroups[sectionGroupId].Sections;
        var response = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var section in response.Value ?? [])
                yield return section;

            if (response.OdataNextLink is null) break;

            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    private async IAsyncEnumerable<SectionGroup> EnumerateChildSectionGroupsAsync(
        String sectionGroupId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.SectionGroups[sectionGroupId].SectionGroups;
        var response = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
            {
                cfg.QueryParameters.Top = 100;
                cfg.QueryParameters.Select = ["id", "displayName"];
            }, ct),
            logger: _logger, ct: ct);

        while (response is not null)
        {
            foreach (var group in response.Value ?? [])
                yield return group;

            if (response.OdataNextLink is null) break;

            var nextLink = response.OdataNextLink;
            response = await Retry.ExecuteAsync(
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    private async IAsyncEnumerable<OneNotePageInfo> EnumerateSectionPagesAsync(
        String sectionId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = _g.Me.Onenote.Sections[sectionId].Pages;
        var pageResponse = await Retry.ExecuteAsync(
            () => request.GetAsync(cfg =>
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
                () => request.WithUrl(nextLink).GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);
        }
    }

    // ── Content fetch ────────────────────────────────────────────────────────

    /// <summary>Downloads the HTML content of a single page.</summary>
    public async Task<String> GetPageHtmlAsync(OneNotePageInfo page, CancellationToken ct = default)
    {
        try
        {
            var stream = await Retry.ExecuteAsync(
                () => _g.Me.Onenote.Pages[page.Id].Content.GetAsync(cancellationToken: ct),
                logger: _logger, ct: ct);

            return await ReadStreamAsync(stream, ct);
        }
        catch (Exception ex) when (!String.IsNullOrWhiteSpace(page.ContentUrl))
        {
            _logger.LogWarning(
                ex,
                "Falling back to contentUrl for page '{Title}' ({Id}) using {ContentUrl}.",
                page.Title,
                page.Id,
                page.ContentUrl);

            var request = new RequestInformation
            {
                HttpMethod = Method.GET,
                URI = new Uri(page.ContentUrl!, UriKind.Absolute)
            };

            var stream = await Retry.ExecuteAsync(
                () => _g.RequestAdapter.SendPrimitiveAsync<Stream>(request, cancellationToken: ct),
                logger: _logger, ct: ct);

            return await ReadStreamAsync(stream, ct);
        }
    }

    private static async Task<String> ReadStreamAsync(Stream? stream, CancellationToken ct)
    {
        if (stream is null) return String.Empty;

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }
}
