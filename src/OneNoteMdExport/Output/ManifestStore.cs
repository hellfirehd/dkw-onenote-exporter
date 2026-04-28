using Microsoft.Kiota.Abstractions;
using OneNoteMdExport.Graph;
using System.Text.Json;

namespace OneNoteMdExport.Output;

/// <summary>
/// Persists page export state to <c>.manifest.json</c> so that re-runs can skip
/// unchanged pages and defer failed pages until the current sweep completes.
/// </summary>
public sealed class ManifestStore
{
    private readonly String _path;
    private readonly PathLayout _layout;
    private readonly ManifestDocument _manifest = new();

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    public ManifestStore(PathLayout layout)
    {
        _layout = layout;
        Directory.CreateDirectory(layout.Root);
        _path = Path.Combine(layout.Root, ".manifest.json");

        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Pages", out _))
            {
                _manifest = JsonSerializer.Deserialize<ManifestDocument>(json) ?? new ManifestDocument();
            }
            else
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<String, DateTimeOffset>>(json) ?? [];
                _manifest = UpgradeLegacy(legacy);
            }

            if (_manifest.ActiveSweep < 1)
            {
                _manifest.ActiveSweep = 1;
            }
        }
        catch { /* corrupt manifest — start fresh */ }
    }

    public ManifestDecision GetDecision(OneNotePageInfo page)
    {
        if (!_manifest.Pages.TryGetValue(page.Id, out var state))
        {
            return ManifestDecision.Process;
        }

        if (state.LastSucceededModifiedTime is { } succeeded &&
            succeeded >= page.LastModifiedTime)
        {
            return ManifestDecision.UpToDate;
        }

        if (state.LastAttemptSweep == _manifest.ActiveSweep &&
            state.LastAttemptedModifiedTime is { } attempted &&
            attempted >= page.LastModifiedTime)
        {
            return ManifestDecision.AlreadyAttemptedThisSweep;
        }

        return ManifestDecision.Process;
    }

    /// <summary>
    /// Renames any existing files with the old date-prefixed naming to the new naming scheme.
    /// This handles the migration when the filename format changes from "{date} - {title}.md" to "{title}.md".
    /// </summary>
    public void MigrateOldFileNamesAsync()
    {
        try
        {
            foreach (var state in _manifest.Pages.Values)
            {
                // Try to find and rename files with old naming pattern
                var notebookDirs = Directory.EnumerateDirectories(_layout.Root);
                foreach (var notebookDir in notebookDirs)
                {
                    if (Path.GetFileName(notebookDir) == ".manifest.json")
                        continue;

                    var sectionDirs = Directory.EnumerateDirectories(notebookDir);
                    foreach (var sectionDir in sectionDirs)
                    {
                        var files = Directory.EnumerateFiles(sectionDir, "*.md");
                        foreach (var file in files)
                        {
                            var fileName = Path.GetFileName(file);
                            // Old pattern: "yyyy-MM-dd - {title}.md"
                            if (HasLegacyDatePrefix(fileName))
                            {
                                var newFileName = fileName.Substring(13); // Remove "yyyy-MM-dd - "
                                var newPath = Path.Combine(sectionDir, newFileName);

                                if (!File.Exists(newPath))
                                {
                                    File.Move(file, newPath, overwrite: false);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Silently ignore migration errors; they don't block export
            Console.Error.WriteLine(ex.ToString());
        }
    }

    public async Task MarkSuccessAsync(OneNotePageInfo page, CancellationToken ct = default)
    {
        var state = GetOrCreateState(page.Id);
        state.LastSucceededModifiedTime = page.LastModifiedTime;
        state.LastAttemptedModifiedTime = page.LastModifiedTime;
        state.LastAttemptSweep = _manifest.ActiveSweep;
        state.LastOutcome = "Succeeded";
        state.LastErrorStatus = null;
        state.LastErrorMessage = null;

        await SaveAsync(ct);
    }

    public async Task MarkFailureAsync(OneNotePageInfo page, Exception ex, CancellationToken ct = default)
    {
        var state = GetOrCreateState(page.Id);
        state.LastAttemptedModifiedTime = page.LastModifiedTime;
        state.LastAttemptSweep = _manifest.ActiveSweep;
        state.LastOutcome = "Failed";
        state.LastErrorStatus = ex is ApiException apiEx ? apiEx.ResponseStatusCode : null;
        state.LastErrorMessage = ex.Message;

        await SaveAsync(ct);
    }

    public async Task CompleteSweepAsync(CancellationToken ct = default)
    {
        _manifest.ActiveSweep++;
        await SaveAsync(ct);
    }

    private ManifestPageState GetOrCreateState(String pageId)
    {
        if (_manifest.Pages.TryGetValue(pageId, out var state))
        {
            return state;
        }

        state = new ManifestPageState();
        _manifest.Pages[pageId] = state;
        return state;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(_manifest, JsonOpts);
        await File.WriteAllTextAsync(_path, json, ct);
    }

    private static Boolean HasLegacyDatePrefix(String fileName)
        => fileName.Length > 13 &&
           Char.IsDigit(fileName[0]) &&
           Char.IsDigit(fileName[1]) &&
           Char.IsDigit(fileName[2]) &&
           Char.IsDigit(fileName[3]) &&
           fileName[4] == '-' &&
           Char.IsDigit(fileName[5]) &&
           Char.IsDigit(fileName[6]) &&
           fileName[7] == '-' &&
           Char.IsDigit(fileName[8]) &&
           Char.IsDigit(fileName[9]) &&
           fileName[10] == ' ' &&
           fileName[11] == '-' &&
           fileName[12] == ' ';

    private static ManifestDocument UpgradeLegacy(Dictionary<String, DateTimeOffset> legacy)
    {
        var manifest = new ManifestDocument();

        foreach (var (pageId, modifiedTime) in legacy)
        {
            manifest.Pages[pageId] = new ManifestPageState
            {
                LastSucceededModifiedTime = modifiedTime,
                LastAttemptedModifiedTime = modifiedTime,
                LastAttemptSweep = 0,
                LastOutcome = "Succeeded",
            };
        }

        return manifest;
    }
}

public enum ManifestDecision
{
    Process,
    UpToDate,
    AlreadyAttemptedThisSweep,
}

public sealed class ManifestDocument
{
    public Int32 SchemaVersion { get; set; } = 2;
    public Int32 ActiveSweep { get; set; } = 1;
    public Dictionary<String, ManifestPageState> Pages { get; set; } = [];
}

public sealed class ManifestPageState
{
    public DateTimeOffset? LastSucceededModifiedTime { get; set; }
    public DateTimeOffset? LastAttemptedModifiedTime { get; set; }
    public Int32? LastAttemptSweep { get; set; }
    public String? LastOutcome { get; set; }
    public Int32? LastErrorStatus { get; set; }
    public String? LastErrorMessage { get; set; }
}
