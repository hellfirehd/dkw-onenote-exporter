# Copilot Instructions

This is a .NET 8 console application that exports OneNote notebooks to GitHub-flavored Markdown via the Microsoft Graph API. The codebase is a skeleton — the project structure, architecture, and all key skeletons are defined in `OVERVIEW.md`. Implement from there.

## Build & Run

```bash
dotnet restore
dotnet build
dotnet run -- --out export      # export to ./export/
dotnet run -- --help
```

## Architecture

Data flows in one direction: **Auth → Graph → Transform → Output**

```
Program.cs (orchestrator)
  ├── Auth/GraphAuth.cs            → produces GraphServiceClient (MSAL delegated)
  ├── Graph/OneNoteGraphClient.cs  → enumerates pages, fetches HTML content
  ├── Transform/
  │   ├── OneNoteHtmlNormalizer.cs → cleans up OneNote-specific HTML
  │   └── HtmlToMarkdown.cs       → converts normalized HTML to GFM
  └── Output/
      ├── PathLayout.cs            → computes output file paths
      ├── MarkdownWriter.cs        → writes .md files with YAML front matter
      ├── AssetDownloader.cs       → downloads images/attachments, rewrites src
      └── ManifestStore.cs         → .manifest.json for incremental re-runs
```

`Program.cs` wires everything together manually (no DI container). All services receive `ExportOptions` or their direct dependencies via constructor injection.

## Critical Domain Knowledge

**Delegated auth only.** Microsoft removed app-only authentication for the OneNote API on March 31, 2025. `GraphAuth` must use MSAL with delegated auth — device code flow or interactive browser. Required scopes include `Notes.Read.All`.

**Graph API pagination.** The default page size for `/me/onenote/pages` is 20, max is 100. Always follow `@odata.nextLink` until exhausted. `OneNoteGraphClient.EnumeratePagesAsync` must handle this.

**Page content is a Stream.** `GET /me/onenote/pages/{id}/content` returns an HTML stream (not JSON). Use `graphClient.Me.Onenote.Pages[pageId].Content.GetAsync()` and read as string.

**Incremental export.** `ManifestStore` persists a `Dictionary<string, DateTimeOffset>` (pageId → lastModifiedTime) to `.manifest.json` in the output root. `IsUpToDate` skips pages that haven't changed since last run.

**Two conversion modes** controlled by `ExportOptions.UsePandoc`:
- Default: `ReverseMarkdown` NuGet package (in-process, GFM output)
- Optional: shell out to `pandoc -f html -t gfm` (more robust, requires external binary)

## Key Conventions

- All implementation classes are `sealed`
- Private fields use `_camelCase`
- All async methods carry the `Async` suffix
- Namespace pattern: `OneNoteMdExport.<Layer>` (e.g., `OneNoteMdExport.Auth`, `OneNoteMdExport.Transform`)
- `ExportOptions` is the single configuration object passed through the entire call stack
- YAML front matter fields: `onenote_id`, `title`, `created`, `modified` (ISO 8601)
- Utility helpers live in `Util/`: `Slug` (filename sanitization), `Retry` (exponential backoff), `Throttle` (Graph rate-limit handling)

## NuGet Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.Graph 5.*` | Graph SDK |
| `Microsoft.Identity.Client 4.*` | MSAL delegated auth |
| `HtmlAgilityPack 1.*` | HTML parsing in normalizer |
| `ReverseMarkdown 5.*` | In-process HTML→Markdown |
