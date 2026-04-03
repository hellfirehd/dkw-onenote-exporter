# OneNote Markdown Exporter

Exports your Microsoft 365 OneNote notebooks to GitHub-Flavored Markdown files via the Microsoft Graph API.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- A Microsoft 365 account with OneNote content
- An Azure AD app registration (see below)
- _(Optional)_ [Pandoc](https://pandoc.org/installing.html) if you prefer Pandoc for HTML→Markdown conversion

## Azure App Registration

> **Important:** App-only auth for the OneNote API was removed on March 31, 2025. Only **delegated** (user) auth works.

1. Go to [Azure Portal → App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps) → **New registration**
   - **Name**: anything (e.g. `OneNote Exporter`)
   - **Supported account types**: _Accounts in any organizational directory and personal Microsoft accounts_
   - **Redirect URI**: platform = **Public client / native**, URI = `http://localhost`

2. Copy the **Application (client) ID** and **Directory (tenant) ID**.

3. Under **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions**, add:

   | Permission | Purpose |
   |---|---|
   | `Notes.Read.All` | Read all OneNote notebooks and pages |
   | `User.Read` | Required for delegated sign-in |

   For personal Microsoft accounts (MSA), admin consent is not required.
   For work/school tenants, click **Grant admin consent** if your IT policy requires it.

## Configuration

```bash
cp src/OneNoteMdExport/appsettings.example.json src/OneNoteMdExport/appsettings.json
```

Edit `appsettings.json`:

```json
{
  "AzureAd": {
    "TenantId": "common",
    "ClientId": "<your-application-client-id>",
    "RedirectUri": "http://localhost"
  },
  "Export": {
    "OutputDir": "export",
    "UsePandoc": false,
    "PandocPath": "pandoc",
    "IncludeImages": true,
    "IncludeAttachments": true,
    "EmitFrontMatter": true
  }
}
```

> `TenantId` can be `"common"` for personal accounts, or your specific tenant ID / domain for work accounts.

## Running

```bash
cd src/OneNoteMdExport
dotnet run -- --out export
```

On first run a browser window opens for sign-in. Subsequent runs use the cached in-memory token for the session.

### CLI Options

| Flag | Description |
|---|---|
| `--out <dir>` | Output directory (default: `export`) |
| `--pandoc` | Use Pandoc instead of ReverseMarkdown |
| `--pandoc-path <path>` | Path to pandoc binary (default: `pandoc`) |
| `--no-images` | Skip image download |
| `--no-attachments` | Skip attachment download |
| `--no-front-matter` | Omit YAML front matter |
| `--device-code` | Device code flow instead of interactive browser |
| `--notebook <name>` | Export only this notebook (exact name) |
| `--verbose` / `-v` | Debug-level logging |
| `--help` / `-h` | Show help |

## Output Structure

```
export/
  Personal Notebook/
    Quick Notes/
      2024-01-15 - Shopping list.md
      assets/
        a1b2c3d4.png
    Work/
      2023-06-01 - Meeting notes.md
  Work Notebook/
    ...
  .manifest.json        ← tracks which pages have been exported
```

Each `.md` file begins with YAML front matter:

```yaml
---
onenote_id: "1-abc123..."
title: "Meeting notes"
created: "2023-06-01T09:00:00+00:00"
modified: "2023-06-15T14:30:00+00:00"
notebook: "Work Notebook"
section: "Work"
---
```

## Incremental Exports

Re-running the tool only processes pages whose `lastModifiedDateTime` has changed since the last run. State is stored in `<output-dir>/.manifest.json`.

## Markdown Conversion Modes

| Mode | How | When to use |
|---|---|---|
| **ReverseMarkdown** (default) | In-process NuGet package | No extra setup; works everywhere |
| **Pandoc** | External binary via `--pandoc` | Better edge-case handling; requires Pandoc installed |

Both modes produce GitHub-Flavored Markdown (GFM) with tables, fenced code blocks, and task lists.
