Love those two options — and given your background, I’d actually give you **both** in a way that’s immediately useful:

1.  a **C# repo skeleton** that’s intentionally “Copilot-friendly” (clear seams, TODOs, interfaces), and
2.  a **drop-in GitHub Copilot prompt** that tells Copilot exactly what to implement, with acceptance criteria and guardrails.

Below is a complete starter package you can paste into a new repo.

***

## Grounding notes (why this design looks the way it does)

*   Microsoft Graph’s OneNote API exposes notebooks/sections/pages and can return **page HTML content** (and resources). [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/onenote-list-notebooks?view=graph-rest-1.0), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)
*   Listing pages is paged: default top is 20 with `@odata.nextLink`; max `top` is 100. [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content)
*   For page body, you typically request `.../onenote/pages/{id}/content` (HTML stream). [\[stackoverflow.com\]](https://stackoverflow.com/questions/56085690/how-do-i-retrieve-the-content-of-a-onenote-page-using-the-microsoft-graph-net-s), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content)
*   **App-only auth is no longer supported** for OneNote Graph API as of **March 31, 2025**; this pushes you to delegated auth for your own export tool. [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/integrate-with-onenote)
*   For HTML → Markdown, you can either:
    *   shell out to **Pandoc** (supports `gfm` output),  or [\[github.com\]](https://github.com/jgm/pandoc), [\[pandoc.org\]](https://pandoc.org/demo/example33/8.22-markdown-variants.html)
    *   do it in-process with **ReverseMarkdown** (has GitHub-flavored options). [\[github.com\]](https://github.com/mysticmind/reversemarkdown-net), [\[nuget.org\]](https://www.nuget.org/packages/ReverseMarkdown)
*   Microsoft also explicitly warns OneNote API has limitations for backup/restore scenarios; so we design for *export* + incremental reruns rather than “perfect backup semantics.” [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content)
*   In your tenant I can see a top-level OneNote artifact named “OneNote” in files, so an export pipeline is relevant. [\[onedrive.live.com\]](https://onedrive.live.com/?id=2c63528a-8cab-418e-b398-a9a0478258b3&cid=b5a8243bd24b48e5&web=1)

***

# Option 1 — C# skeleton repo (Copilot-ready)

### Repo layout

    onenote-md-export/
      src/
        OneNoteMdExport/
          OneNoteMdExport.csproj
          Program.cs
          appsettings.example.json
          Cli/
            ExportOptions.cs
            CommandLine.cs
          Auth/
            GraphAuth.cs
          Graph/
            OneNoteGraphClient.cs
            Models.cs
          Transform/
            OneNoteHtmlNormalizer.cs
            HtmlToMarkdown.cs
          Output/
            PathLayout.cs
            MarkdownWriter.cs
            AssetDownloader.cs
            ManifestStore.cs
          Util/
            Slug.cs
            Retry.cs
            Throttle.cs
      README.md
      .gitignore

***

## `OneNoteMdExport.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Graph + auth -->
    <PackageReference Include="Microsoft.Graph" Version="5.*" />
    <PackageReference Include="Microsoft.Identity.Client" Version="4.*" />

    <!-- HTML parsing for normalization -->
    <PackageReference Include="HtmlAgilityPack" Version="1.*" />

    <!-- Markdown conversion option B (in-proc) -->
    <PackageReference Include="ReverseMarkdown" Version="5.*" />
  </ItemGroup>
</Project>
```

> Notes: Graph SDK and MSAL packages are standard choices; the OneNote endpoints + content flow are documented by Microsoft Learn and widely used.   
> ReverseMarkdown is optional but included to avoid depending on an external `pandoc` binary. [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content), [\[stackoverflow.com\]](https://stackoverflow.com/questions/56085690/how-do-i-retrieve-the-content-of-a-onenote-page-using-the-microsoft-graph-net-s) [\[github.com\]](https://github.com/mysticmind/reversemarkdown-net), [\[nuget.org\]](https://www.nuget.org/packages/ReverseMarkdown)

***

## `appsettings.example.json`

```json
{
  "AzureAd": {
    "TenantId": "common",
    "ClientId": "00000000-0000-0000-0000-000000000000",
    "RedirectUri": "http://localhost"
  },
  "Export": {
    "OutputDir": "export",
    "UsePandoc": false,
    "PandocPath": "pandoc",
    "IncludeAttachments": true,
    "IncludeImages": true,
    "EmitFrontMatter": true
  }
}
```

***

## `Program.cs` (orchestrator)

```csharp
using OneNoteMdExport.Auth;
using OneNoteMdExport.Cli;
using OneNoteMdExport.Graph;
using OneNoteMdExport.Output;
using OneNoteMdExport.Transform;

var options = CommandLine.Parse(args);

var auth = new GraphAuth(options);
var graphClient = await auth.CreateGraphClientAsync();

var oneNote = new OneNoteGraphClient(graphClient);

var layout = new PathLayout(options);
var manifest = new ManifestStore(layout);
var assets = new AssetDownloader(graphClient, layout, options);

var normalizer = new OneNoteHtmlNormalizer(options);
var md = new HtmlToMarkdown(options);

var writer = new MarkdownWriter(layout, options);

await foreach (var page in oneNote.EnumeratePagesAsync(options))
{
    if (manifest.IsUpToDate(page)) continue;

    var html = await oneNote.GetPageHtmlAsync(page.Id);
    var normalizedHtml = await normalizer.NormalizeAsync(page, html, assets);

    var markdown = await md.ConvertAsync(normalizedHtml);

    await writer.WritePageAsync(page, markdown);
    await manifest.MarkDoneAsync(page);
}

Console.WriteLine("Done.");
```

***

## `Auth/GraphAuth.cs` (delegated auth)

Because app-only is no longer supported for OneNote API after March 31, 2025, we implement delegated auth for “export my notes.” [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/integrate-with-onenote)

Skeleton (device code flow is easiest for a console tool; Copilot can implement using MSAL):

```csharp
using Microsoft.Graph;
using Microsoft.Identity.Client;

namespace OneNoteMdExport.Auth;

public sealed class GraphAuth
{
    private readonly ExportOptions _opt;

    public GraphAuth(ExportOptions opt) => _opt = opt;

    public async Task<GraphServiceClient> CreateGraphClientAsync()
    {
        // TODO: implement MSAL delegated auth (device code or interactive).
        // Must request scopes that cover OneNote read: e.g. Notes.Read or Notes.Read.All etc.
        // See MS Graph OneNote docs for permissions on list pages / notebooks. [2](https://learn.microsoft.com/en-us/graph/api/onenote-list-notebooks?view=graph-rest-1.0)[3](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)

        throw new NotImplementedException();
    }
}
```

***

## `Graph/OneNoteGraphClient.cs`

This wraps the key flows:

*   list notebooks / sections / pages (paged; handle nextLink) [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/onenote-list-notebooks?view=graph-rest-1.0), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)
*   get page HTML content via `/pages/{id}/content` [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content), [\[stackoverflow.com\]](https://stackoverflow.com/questions/56085690/how-do-i-retrieve-the-content-of-a-onenote-page-using-the-microsoft-graph-net-s)

```csharp
using Microsoft.Graph;

namespace OneNoteMdExport.Graph;

public sealed class OneNoteGraphClient
{
    private readonly GraphServiceClient _g;

    public OneNoteGraphClient(GraphServiceClient g) => _g = g;

    public async IAsyncEnumerable<OneNotePageInfo> EnumeratePagesAsync(ExportOptions opt)
    {
        // TODO:
        // - Option A: list all pages across notebooks: GET /me/onenote/pages
        // - Option B: enumerate notebooks -> sections -> pages
        // Remember: default top is 20 and nextLink pagination; max top 100. [3](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)
        yield break;
    }

    public async Task<string> GetPageHtmlAsync(string pageId)
    {
        // SDK pattern: graphClient.Me.Onenote.Pages[pageId].Content.Request().GetAsync()
        // This returns Stream; read as string HTML. [4](https://stackoverflow.com/questions/56085690/how-do-i-retrieve-the-content-of-a-onenote-page-using-the-microsoft-graph-net-s)
        throw new NotImplementedException();
    }
}
```

***

## `Transform/OneNoteHtmlNormalizer.cs`

This is where you make output pleasant and stable.

Normalization goals:

*   strip OneNote’s heavy inline styles
*   ensure headings become real `<h1>`..`<h6>` (so Markdown converter produces `#` etc.)
*   rewrite images/resources to local paths (if you want full portability)

```csharp
using OneNoteMdExport.Graph;
using OneNoteMdExport.Output;

namespace OneNoteMdExport.Transform;

public sealed class OneNoteHtmlNormalizer
{
    private readonly ExportOptions _opt;

    public OneNoteHtmlNormalizer(ExportOptions opt) => _opt = opt;

    public Task<string> NormalizeAsync(OneNotePageInfo page, string html, AssetDownloader assets)
    {
        // TODO:
        // - Parse HTML with HtmlAgilityPack
        // - Map OneNote-specific constructs to more semantic HTML
        // - Rewrite <img> src or data-render-src into downloaded local assets (if enabled)
        // - Preserve code blocks, tables, lists
        return Task.FromResult(html);
    }
}
```

***

## `Transform/HtmlToMarkdown.cs`

Two strategies:

### Strategy A: Pandoc (external binary)

Pandoc supports **GitHub-Flavored Markdown** output (`gfm`). [\[github.com\]](https://github.com/jgm/pandoc), [\[pandoc.org\]](https://pandoc.org/demo/example33/8.22-markdown-variants.html)

### Strategy B: ReverseMarkdown (in-process)

ReverseMarkdown supports GitHub-flavored features (tables/tasklists) via configuration. [\[github.com\]](https://github.com/mysticmind/reversemarkdown-net), [\[nuget.org\]](https://www.nuget.org/packages/ReverseMarkdown)

Skeleton:

```csharp
using System.Diagnostics;
using ReverseMarkdown;

namespace OneNoteMdExport.Transform;

public sealed class HtmlToMarkdown
{
    private readonly ExportOptions _opt;

    public HtmlToMarkdown(ExportOptions opt) => _opt = opt;

    public async Task<string> ConvertAsync(string html)
    {
        if (_opt.UsePandoc)
        {
            // TODO: write html to temp, run pandoc -f html -t gfm, read output
            // Pandoc supports gfm as an output format. [6](https://github.com/jgm/pandoc)[7](https://pandoc.org/demo/example33/8.22-markdown-variants.html)
            throw new NotImplementedException();
        }

        var config = new Config(githubFlavoured: true);
        var converter = new Converter(config);
        return await Task.FromResult(converter.Convert(html));
    }
}
```

***

## `Output/PathLayout.cs`

```csharp
using OneNoteMdExport.Graph;
using OneNoteMdExport.Util;

namespace OneNoteMdExport.Output;

public sealed class PathLayout
{
    private readonly ExportOptions _opt;

    public PathLayout(ExportOptions opt) => _opt = opt;

    public string Root => Path.GetFullPath(_opt.OutputDir);

    public string PagePath(OneNotePageInfo p)
    {
        // TODO: choose layout:
        // Root/{Notebook}/{Section}/{CreatedDate} - {Title}.md
        var safeTitle = Slug.FileName(p.Title);
        return Path.Combine(Root, safeTitle + ".md");
    }

    public string AssetsDir(OneNotePageInfo p)
        => Path.Combine(Path.GetDirectoryName(PagePath(p))!, "assets");
}
```

***

## `Output/MarkdownWriter.cs` + front matter

```csharp
using OneNoteMdExport.Graph;

namespace OneNoteMdExport.Output;

public sealed class MarkdownWriter
{
    private readonly PathLayout _layout;
    private readonly ExportOptions _opt;

    public MarkdownWriter(PathLayout layout, ExportOptions opt)
    {
        _layout = layout;
        _opt = opt;
    }

    public async Task WritePageAsync(OneNotePageInfo page, string markdown)
    {
        var path = _layout.PagePath(page);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (_opt.EmitFrontMatter)
        {
            var fm =
$@"---
onenote_id: ""{page.Id}""
title: ""{page.Title.Replace("\"","\\\"")}"" 
created: ""{page.CreatedTime:O}""
modified: ""{page.LastModifiedTime:O}""
---
";
            markdown = fm + "\n" + markdown;
        }

        await File.WriteAllTextAsync(path, markdown);
    }
}
```

***

## `Output/ManifestStore.cs` (incremental reruns)

This avoids reprocessing 15 years of pages every run — compare `lastModifiedTime` and skip unchanged.

```csharp
using System.Text.Json;
using OneNoteMdExport.Graph;

namespace OneNoteMdExport.Output;

public sealed class ManifestStore
{
    private readonly string _path;
    private readonly Dictionary<string, DateTimeOffset> _seen = new();

    public ManifestStore(PathLayout layout)
    {
        _path = Path.Combine(layout.Root, ".manifest.json");
        if (File.Exists(_path))
        {
            var json = File.ReadAllText(_path);
            _seen = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? new();
        }
    }

    public bool IsUpToDate(OneNotePageInfo p)
        => _seen.TryGetValue(p.Id, out var t) && t >= p.LastModifiedTime;

    public async Task MarkDoneAsync(OneNotePageInfo p)
    {
        _seen[p.Id] = p.LastModifiedTime;
        var json = JsonSerializer.Serialize(_seen, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_path, json);
    }
}
```

***

# Option 2 — “tell GitHub Copilot what to do” prompt (copy/paste)

Use this as your **single Copilot Chat message** (or as `.github/copilot-instructions.md` if you use that workflow). It’s explicit, testable, and aligned with Graph’s OneNote behavior.

***

## Copilot Prompt

> You are implementing a .NET 8 console tool that exports my OneNote (Microsoft 365) pages to Markdown using Microsoft Graph OneNote API.
>
> Requirements:
>
> 1.  Use delegated authentication (interactive or device code). App-only auth is not allowed for OneNote API. (The API no longer supports app-only auth after 2025-03-31.) [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/integrate-with-onenote)
> 2.  Enumerate pages across all notebooks or by notebook->section->pages. Handle paging via `@odata.nextLink`. Default page list returns 20; max `top` is 100. [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)
> 3.  For each page, download HTML content via `/me/onenote/pages/{id}/content` (Graph SDK: `graphClient.Me.Onenote.Pages[pageId].Content.Request().GetAsync()` returns a stream). [\[stackoverflow.com\]](https://stackoverflow.com/questions/56085690/how-do-i-retrieve-the-content-of-a-onenote-page-using-the-microsoft-graph-net-s), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content)
> 4.  Convert HTML to GitHub-Flavored Markdown. Provide two modes:
>     *   In-process using ReverseMarkdown with GitHubFlavored enabled. [\[github.com\]](https://github.com/mysticmind/reversemarkdown-net), [\[nuget.org\]](https://www.nuget.org/packages/ReverseMarkdown)
>     *   External using Pandoc (`-f html -t gfm`). Pandoc supports `gfm` format. [\[github.com\]](https://github.com/jgm/pandoc), [\[pandoc.org\]](https://pandoc.org/demo/example33/8.22-markdown-variants.html)
> 5.  Write one `.md` file per page in a stable folder structure. Include YAML front matter containing: onenote\_id, title, created, modified.
> 6.  Implement incremental export using a `.manifest.json` that tracks pageId -> lastModifiedTime and skips unchanged pages.
> 7.  (Optional but preferred) Download images/resources and rewrite HTML before conversion so Markdown references local asset paths.
>
> Deliverables:
>
> *   Implement all TODOs in the provided skeleton classes.
> *   Add a `README.md` with setup steps: Azure app registration, required delegated scopes for listing notebooks/pages, and how to run the tool. (Permissions for list notebooks/pages are documented in Microsoft Graph OneNote API reference.) [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/onenote-list-notebooks?view=graph-rest-1.0), [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/api/section-list-pages?view=graph-rest-1.0)
> *   Provide a sample `appsettings.json` and support CLI overrides for output dir and conversion mode.
> *   Add basic logging and retry/backoff for throttling and transient errors.
>
> Acceptance criteria:
>
> *   Running `dotnet run -- --out export` produces Markdown files for multiple pages.
> *   Re-running immediately results in no rewrites for unchanged pages (manifest skip works).
> *   Markdown contains headings/lists/tables reasonably preserved (GFM tables where possible).
>
> Do not invent unsupported Graph endpoints. Stick to documented OneNote API resource paths and content endpoint. [\[learn.microsoft.com\]](https://learn.microsoft.com/en-us/graph/onenote-get-content)

***

## Which option should you pick?

**Pick option 1 (skeleton)** if you want to stay in control of architecture and let Copilot fill in seams.  
**Pick option 2 (prompt)** if you want to drive Copilot harder and accept it may refactor your structure.

Given you said “I’ll supervise Copilot,” I’d do **Option 1 + the prompt** (best of both worlds): you commit the skeleton, then paste the prompt to implement TODOs.

***

## Tiny “bonus” recommendation (pragmatic)

If you want maximum portability with minimum fuss, start with **ReverseMarkdown mode** (no external dependency) and only add Pandoc if you’re unhappy with edge cases. ReverseMarkdown explicitly supports GitHub-flavored behavior and is actively maintained.   
Pandoc is fantastic when installed, and it explicitly supports `gfm`. [\[github.com\]](https://github.com/mysticmind/reversemarkdown-net), [\[nuget.org\]](https://www.nuget.org/packages/ReverseMarkdown) [\[github.com\]](https://github.com/jgm/pandoc), [\[pandoc.org\]](https://pandoc.org/demo/example33/8.22-markdown-variants.html)

***

If you tell me “ReverseMarkdown-only” vs “Pandoc-first”, I’ll tailor the skeleton a bit (mostly around the converter and asset pipeline).
