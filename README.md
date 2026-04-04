# OneNote Markdown Exporter  

<!-- markdownlint-disable MD033 -->
<p align="center"><img src="DiggingOut.png" alt="Digging Out"></p>
<!-- markdownlint-enable MD033 -->

Export your Microsoft 365 OneNote notebooks into clean, GitHub‑Flavored Markdown — with hierarchy, metadata, images, and attachments preserved — using the Microsoft Graph API.

Because your notes deserve better than being trapped in a proprietary format forever.

---

## 🚀 What This Tool Does

- Authenticates via Microsoft Graph (delegated user auth)  
- Walks your entire OneNote hierarchy (notebooks → sections → pages)  
- Downloads page content as HTML  
- Converts it to GitHub‑Flavored Markdown  
- Saves images and attachments  
- Writes YAML front matter for easy static‑site or GitHub‑wiki use  
- Supports incremental exports so you can run it again without reprocessing everything  

If you have ever thought, “Why is there no official OneNote export tool?” — this is that tool.

---

## 📦 Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)  
- A Microsoft 365 account with OneNote content  
- Your OneNote notebooks are synchronized to OneDrive
- An Azure AD app registration (instructions below)  
- _(Optional)_ Pandoc [(pandoc.org in Bing)](https://www.bing.com/search?q="https%3A%2F%2Fpandoc.org%2Finstalling.html") if you want the heavyweight HTML→Markdown conversion engine  

---

## 🔐 Azure App Registration

> **Important:** Microsoft removed app‑only authentication for OneNote on **March 31, 2025**.  
> This tool now uses **delegated** (user) authentication only.

If that sentence made your eyes glaze over, do not worry — it just means you will be signing in as yourself rather than letting this app impersonate you. Because I do not have a Microsoft Partner Network ID (the thing that makes an app a “Trusted Publisher”), this exporter cannot access anyone’s account except the one you personally authorize.

By following the steps below, you will create your own Azure app registration and configure it to grant this tool **read‑only access** to your Microsoft account profile — including your OneNote notebooks. In plain English: you stay in control, and the exporter only reads your notes so it can convert them to Markdown. Nothing spooky, nothing shared, nothing written back.

1. Go to  
   **Azure Portal → App registrations → New registration**

   - **Name:** anything you like (e.g., `OneNote Exporter`)  
   - **Supported account types:**  
     _Accounts in any organizational directory and personal Microsoft accounts_  
   - **Redirect URI:**  
     - Platform: **Public client / native**  
     - URI: `http://localhost`

2. Copy your:
   - **Application (client) ID**  
   - **Directory (tenant) ID**

3. Add delegated Microsoft Graph permissions:

  | Permission   | Purpose                                               |
  | ------------ | ----------------------------------------------------- |
  | `Notes.Read` | Read the signed-in user's OneNote notebooks and pages |
  | `User.Read`  | Required for delegated sign-in                        |

  - Personal Microsoft accounts: no admin consent needed  <!-- markdownlint-disable-line MD007 -->
  - Work/school tenants: click **Grant admin consent** if required <!-- markdownlint-disable-line MD007 -->

4. If you are using a personal Microsoft account only, set: <!-- markdownlint-disable-line MD029 -->

   - `TenantId = "consumers"`

   Use `"common"` only if you intentionally want to support both personal and work accounts.

---

## ⚙️ Configuration

```bash
cp src/OneNoteMdExport/appsettings.example.json src/OneNoteMdExport/appsettings.json
```

Edit `appsettings.json`:

```json
{
  "AzureAd": {
    "TenantId": "common",
    "ClientId": "<your-application-client-id>",
    "RedirectUri": "http://localhost",
    "UsePersistentTokenCache": false
  },
  "Export": {
    "OutputDir": "export",
    "UsePandoc": false,
    "PandocPath": "pandoc",
    "IncludeImages": true,
    "IncludeAttachments": true,
    "EmitFrontMatter": true,
    "ThrottleRequestsPerMinute": 100,
    "ThrottleRequestsPerHour": 350,
    "ThrottleConcurrentRequests": 5
  }
}
```

### Notes

- `"consumers"` is safest for personal Microsoft accounts  
- `"common"` works for mixed environments
- `UsePersistentTokenCache = true` lets you reuse tokens across runs (recommended once everything works)
- The throttle settings default to conservative delegated-auth values with headroom below Microsoft's published OneNote limits

---

## ▶️ Running the Export

```bash
cd src/OneNoteMdExport
dotnet run -- --out export
```

On first run, a browser window opens for sign‑in.  
Subsequent requests reuse the in‑memory token cache (or persistent cache if enabled).

A full run log is also written to `<output-dir>/export.log`. The console shows the message text only, while the log file includes full exception details.

### Throttling

Microsoft Graph applies general throttling, and OneNote also has service-specific limits. For this exporter's delegated auth flow, Microsoft's published OneNote limits are:

- 120 requests per minute
- 400 requests per hour
- 5 concurrent requests

Microsoft also publishes higher app-only limits for OneNote (240 requests per minute, 800 per hour, and 20 concurrent requests), but this project uses delegated auth only.

The exporter already retries and paces requests, but large exports can still hit these limits. OneNote resources also do not reliably return a `Retry-After` header on `429 Too Many Requests`, so recovery may require exponential backoff and, in some cases, simply rerunning the export later.

### CLI Options

| Flag                   | Description                                |
| ---------------------- | ------------------------------------------ |
| `--out <dir>`          | Output directory (default: `export`)       |
| `--pandoc`             | Use Pandoc instead of ReverseMarkdown      |
| `--pandoc-path <path>` | Path to Pandoc binary                      |
| `--no-images`          | Skip image download                        |
| `--no-attachments`     | Skip attachment download                   |
| `--no-front-matter`    | Omit YAML front matter                     |
| `--device-code`        | Use device code flow instead of browser    |
| `--notebook <name>`    | Export only a specific notebook            |
| `--verbose` / `-v`     | Debug logging                              |
| `--help` / `-h`        | Show help                                  |

---

## 📁 Output Structure

```text
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
  .manifest.json   ← tracks incremental export state
```

Each Markdown file includes YAML front matter:

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

---

## 🔄 Incremental Exports

Re-running the tool processes only pages whose `lastModifiedDateTime` has changed since the previous run.

State is stored in:

```text
<output-dir>/.manifest.json
```

This makes the exporter safe to run repeatedly — ideal for large notebooks or slow networks.

---

## 🛠️ Planned Features

These options are on the roadmap:

- `--section <name>` — export only a specific section  
- `--max-pages <n>` — limit processing for testing or throttling  
- `--continue-on-error` — keep going even if a page fails  
- `--skip-missing-pages` — ignore pages that enumerate but return `404`  

If you want to contribute, PRs are welcome.

---

## 📝 Markdown Conversion Modes

| Mode                          | How                    | When to Use                              |
| ----------------------------- | ---------------------- | ---------------------------------------- |
| **ReverseMarkdown** (default) | Built-in NuGet package | Simple, fast, no external dependencies   |
| **Pandoc**                    | External binary        | Best fidelity; handles tricky HTML cases |

Both produce GitHub‑Flavored Markdown with tables, fenced code blocks, and task lists.

---

## License

MIT - see [LICENSE.md](LICENSE.md)

---

## 🎉 Final Notes

This project exists because exporting OneNote should not require ritual sacrifice, manual copy‑paste, or a PhD in patience. If this tool saves you even one hour of your life, consider starring the repo — or telling someone else who is trapped in OneNote purgatory.
