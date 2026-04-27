using System.Text.Json;

namespace OneNoteMdExport.Cli;

public static class CommandLine
{
    public static ExportOptions? Parse(String[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            return null;
        }

        var opts = LoadFromFile();
        return ApplyArgs(opts, args);
    }

    private static ExportOptions LoadFromFile()
    {
        var candidates = new[]
        {
            "appsettings.json",
            "appsettings.Application.json",
            "appsettings.Development.json",
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.Application.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var ad = root.TryGetProperty("AzureAd", out var adEl) ? adEl : default;
                var exp = root.TryGetProperty("Export", out var expEl) ? expEl : default;

                return new ExportOptions
                {
                    TenantId = Str(ad, "TenantId") ?? "common",
                    ClientId = Str(ad, "ClientId") ?? String.Empty,
                    RedirectUri = Str(ad, "RedirectUri") ?? "http://localhost",
                    UsePersistentTokenCache = Bool(ad, "UsePersistentTokenCache") ?? false,
                    OutputDir = Str(exp, "OutputDir") ?? "export",
                    UsePandoc = Bool(exp, "UsePandoc") ?? false,
                    PandocPath = Str(exp, "PandocPath") ?? "pandoc",
                    IncludeImages = Bool(exp, "IncludeImages") ?? true,
                    IncludeAttachments = Bool(exp, "IncludeAttachments") ?? true,
                    EmitFrontMatter = Bool(exp, "EmitFrontMatter") ?? true,
                    ThrottleRequestsPerMinute = Int(exp, "ThrottleRequestsPerMinute") ?? 100,
                    ThrottleRequestsPerHour = Int(exp, "ThrottleRequestsPerHour") ?? 350,
                    ThrottleConcurrentRequests = Int(exp, "ThrottleConcurrentRequests") ?? 5,
                };
            }
            catch { /* corrupt/missing — fall through */ }
        }

        return new ExportOptions();
    }

    private static ExportOptions ApplyArgs(ExportOptions opts, String[] args)
    {
        String? outputDir = null, pandocPath = null, notebook = null;
        Boolean? usePandoc = null, noImages = null, noAttachments = null,
              noFrontMatter = null, deviceCode = null, verbose = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outputDir = args[++i]; break;
                case "--pandoc":
                    usePandoc = true; break;
                case "--pandoc-path" when i + 1 < args.Length:
                    pandocPath = args[++i]; break;
                case "--no-images":
                    noImages = true; break;
                case "--no-attachments":
                    noAttachments = true; break;
                case "--no-front-matter":
                    noFrontMatter = true; break;
                case "--device-code":
                    deviceCode = true; break;
                case "--notebook" when i + 1 < args.Length:
                    notebook = args[++i]; break;
                case "--verbose" or "-v":
                    verbose = true; break;
            }
        }

        return opts with
        {
            OutputDir = outputDir ?? opts.OutputDir,
            UsePandoc = usePandoc ?? opts.UsePandoc,
            PandocPath = pandocPath ?? opts.PandocPath,
            IncludeImages = noImages.HasValue ? !noImages.Value : opts.IncludeImages,
            IncludeAttachments = noAttachments.HasValue ? !noAttachments.Value : opts.IncludeAttachments,
            EmitFrontMatter = noFrontMatter.HasValue ? !noFrontMatter.Value : opts.EmitFrontMatter,
            UseDeviceCode = deviceCode ?? opts.UseDeviceCode,
            NotebookFilter = notebook ?? opts.NotebookFilter,
            Verbose = verbose ?? opts.Verbose,
        };
    }

    private static void PrintHelp() => Console.WriteLine("""
        OneNote Markdown Exporter

        Usage: dotnet run -- [options]

        Options:
          --out <dir>          Output directory (default: export)
          --pandoc             Use Pandoc for HTML->Markdown conversion
          --pandoc-path <path> Path to pandoc binary (default: pandoc)
          --no-images          Skip image download
          --no-attachments     Skip attachment download
          --no-front-matter    Omit YAML front matter
          --device-code        Use device code flow instead of interactive browser
          --notebook <name>    Export only pages from the named notebook
          --verbose, -v        Verbose logging
          --help, -h           Show this help
        """);

    private static String? Str(JsonElement el, String prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static Boolean? Bool(JsonElement el, String prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static Int32? Int(JsonElement el, String prop) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) &&
        v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out var value)
            ? value : null;
}
