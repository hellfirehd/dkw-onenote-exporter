namespace OneNoteMdExport.Graph;

/// <summary>Immutable summary of a OneNote page, decoupled from the Graph SDK model.</summary>
public sealed record OneNotePageInfo(
    string Id,
    string Title,
    string NotebookName,
    string SectionName,
    DateTimeOffset CreatedTime,
    DateTimeOffset LastModifiedTime,
    string? ContentUrl)
{
    internal static OneNotePageInfo FromGraph(Microsoft.Graph.Models.OnenotePage p) =>
        new(
            p.Id ?? throw new InvalidOperationException("Page has no ID"),
            p.Title ?? "Untitled",
            p.ParentNotebook?.DisplayName ?? "Unknown Notebook",
            p.ParentSection?.DisplayName ?? "Unknown Section",
            p.CreatedDateTime ?? DateTimeOffset.UtcNow,
            p.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            p.ContentUrl);
}
